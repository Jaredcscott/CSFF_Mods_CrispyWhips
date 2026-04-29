using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CSFFModFramework.Patching.Performance;

// InGameCardBase.LateUpdate runs every frame for EVERY active card — visible or not.
// On late-game saves (390+ days, many sites and placed improvements) there can be
// hundreds of InGameCardBase instances across non-player environments. Each call
// invokes CheckIfVisibleOnScreen (Rect.Overlaps + several field checks), plus
// collision-enable state updates, even when nothing actually changed on screen.
//
// This prefix throttles LateUpdate to 1-in-N frames for cards that are:
//   * off-screen (VisibleOnScreen == false), AND
//   * not animating (IsPulsing == false, IsTimeAnimated == false), AND
//   * not destroying, AND
//   * the game isn't transitioning environments (EnvironmentTransition == false)
//
// Visible, pulsing, time-animated, destroying, or mid-env-transition cards run
// every frame as before — no behavior change for anything the player is looking at.
//
// Worst-case visibility lag for an off-screen card becoming visible: (N-1) frames.
// At 60 fps and N=3, that's ~33 ms — below perception for card visuals popping in
// at screen edges or during scroll. On-screen cards pay zero throttle cost.
//
// Members (IsPulsing / IsTimeAnimated / GM) are non-public-accessible in the
// nstripped Assembly-CSharp reference, so we reach them via cached compiled
// delegates bound once at startup — direct callvirt overhead per-frame, no
// per-call reflection.
internal static class OffScreenCardThrottle
{
    private static int _throttleFrames = 3;
    private static bool _enabled = true;

    // Map InGameCardBase.GetInstanceID() → frame index of last LateUpdate run.
    // Cleaned periodically to bound memory when cards are destroyed over a session.
    private static readonly Dictionary<int, int> _lastRun = new();
    private const int CleanupIntervalFrames = 10000;   // ~2.8 min at 60 fps
    private static int _nextCleanupFrame;

    // Bound at startup via AccessTools — zero per-call reflection at runtime.
    // Each delegate takes an InGameCardBase (boxed as object for portability) and
    // returns the relevant bool / field value.
    private static Func<object, bool> _getVisibleOnScreen;
    private static Func<object, bool> _getIsPulsing;
    private static Func<object, bool> _getIsTimeAnimated;
    private static Func<object, bool> _getDestroyed;
    private static Func<object, object> _getGM;
    private static Func<object, bool> _getEnvironmentTransition;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var enabledCfg = config.Bind(
            "Performance", "OffScreenCardThrottleEnabled", true,
            "Throttle InGameCardBase.LateUpdate for off-screen, non-animating cards. "
            + "Reduces per-frame cost on late-game saves with many placed improvements. "
            + "Set false to disable if you observe visual glitches at screen edges.");
        var framesCfg = config.Bind(
            "Performance", "OffScreenCardThrottleFrames", 3,
            "Number of frames between LateUpdate runs for eligible off-screen cards. "
            + "2 = near-zero lag. 3 = default (balanced). 4+ = more savings, more lag. "
            + "Clamped to [2, 10].");

        _enabled = enabledCfg.Value;
        _throttleFrames = Mathf.Clamp(framesCfg.Value, 2, 10);

        if (!_enabled)
        {
            Util.Log.Debug("OffScreenCardThrottle: disabled via config.");
            return;
        }

        var icbType = AccessTools.TypeByName("InGameCardBase");
        if (icbType == null)
        {
            Util.Log.Warn("OffScreenCardThrottle: InGameCardBase type not found; patch skipped.");
            return;
        }

        _getVisibleOnScreen  = BuildBoolGetter(icbType, "VisibleOnScreen");
        _getIsPulsing        = BuildBoolGetter(icbType, "IsPulsing");
        _getIsTimeAnimated   = BuildBoolGetter(icbType, "IsTimeAnimated");
        _getDestroyed        = BuildBoolGetter(icbType, "Destroyed");
        _getGM               = BuildFieldGetter(icbType, "GM");

        var gmType = AccessTools.TypeByName("GameManager");
        if (gmType != null)
            _getEnvironmentTransition = BuildBoolGetter(gmType, "EnvironmentTransition");

        // Visible/pulsing/animated getters are essential — refuse to patch if any is missing.
        if (_getVisibleOnScreen == null || _getIsPulsing == null || _getIsTimeAnimated == null)
        {
            Util.Log.Warn("OffScreenCardThrottle: required accessors not found (VisibleOnScreen/IsPulsing/IsTimeAnimated); patch skipped.");
            return;
        }

        var target = AccessTools.Method(icbType, "LateUpdate");
        if (target == null)
        {
            Util.Log.Warn("OffScreenCardThrottle: InGameCardBase.LateUpdate not found; patch skipped.");
            return;
        }

        var prefix = new HarmonyMethod(AccessTools.Method(typeof(OffScreenCardThrottle), nameof(Prefix)));
        try
        {
            harmony.Patch(target, prefix: prefix);
            Util.Log.Info($"OffScreenCardThrottle: enabled (1-in-{_throttleFrames} frames for off-screen cards).");
        }
        catch (Exception ex)
        {
            Util.Log.Error($"OffScreenCardThrottle: failed to patch: {ex.Message}");
        }
    }

    // Prefix returns `false` to skip the original LateUpdate for this frame.
    // Returning `true` runs the game's LateUpdate normally.
    //
    // __instance is typed `object` so Harmony matches regardless of runtime subtype
    // (InGameCardBase, InGameDraggableCard, InGameNPC, etc.).
    private static bool Prefix(object __instance)
    {
        if (!_enabled || __instance == null) return true;

        // Let the original run for any card the player cares about visually.
        if (_getVisibleOnScreen(__instance)) return true;
        if (_getIsPulsing(__instance)) return true;
        if (_getIsTimeAnimated(__instance)) return true;
        if (_getDestroyed != null && _getDestroyed(__instance)) return true;

        // During env transitions, cards are actively transitioning visibility —
        // let everything run normally so visuals attach/detach on the correct frame.
        if (_getGM != null && _getEnvironmentTransition != null)
        {
            var gm = _getGM(__instance);
            if (gm != null && _getEnvironmentTransition(gm)) return true;
        }

        // __instance is always a UnityEngine.Object (MonoBehaviour on an active GameObject).
        // GetInstanceID is defined on UnityEngine.Object and is cheap.
        int frame = Time.frameCount;
        int id = ((UnityEngine.Object)__instance).GetInstanceID();

        if (_lastRun.TryGetValue(id, out int last) && (frame - last) < _throttleFrames)
            return false;   // skip this frame

        _lastRun[id] = frame;

        if (frame >= _nextCleanupFrame)
        {
            _nextCleanupFrame = frame + CleanupIntervalFrames;
            PruneStale(frame);
        }

        return true;
    }

    // Drop entries that haven't updated in a long while — they likely belong to
    // destroyed cards whose instance IDs will never appear again.
    private static void PruneStale(int now)
    {
        int cutoff = CleanupIntervalFrames + _throttleFrames * 10;
        List<int> stale = null;
        foreach (var kv in _lastRun)
        {
            if (now - kv.Value > cutoff)
            {
                stale ??= new List<int>();
                stale.Add(kv.Key);
            }
        }
        if (stale != null)
            foreach (var k in stale) _lastRun.Remove(k);
    }

    // --- accessor builders ---
    // Both return null gracefully if the member isn't found, so the patch can
    // degrade (skip itself) rather than crash if the game renames something.

    // Open-instance delegates can't bind a Func<object,T> directly to an instance method
    // declared on a derived type — the CLR requires the first parameter to match the
    // method's `this` type exactly. Build via Expression.Lambda so the downcast happens
    // inline and the compiled delegate runs at near-native call speed.
    private static Func<object, bool> BuildBoolGetter(Type owner, string name)
    {
        var prop = owner.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanRead && prop.PropertyType == typeof(bool))
        {
            var getter = prop.GetGetMethod(true);
            if (getter != null)
            {
                var p = Expression.Parameter(typeof(object), "o");
                var call = Expression.Call(Expression.Convert(p, owner), getter);
                return Expression.Lambda<Func<object, bool>>(call, p).Compile();
            }
        }
        var field = AccessTools.Field(owner, name);
        if (field != null && field.FieldType == typeof(bool))
        {
            var p = Expression.Parameter(typeof(object), "o");
            var read = Expression.Field(Expression.Convert(p, owner), field);
            return Expression.Lambda<Func<object, bool>>(read, p).Compile();
        }
        Util.Log.Debug($"OffScreenCardThrottle: bool accessor for {owner.Name}.{name} not found.");
        return null;
    }

    private static Func<object, object> BuildFieldGetter(Type owner, string name)
    {
        var field = AccessTools.Field(owner, name);
        if (field != null)
        {
            var p = Expression.Parameter(typeof(object), "o");
            var read = Expression.Convert(Expression.Field(Expression.Convert(p, owner), field), typeof(object));
            return Expression.Lambda<Func<object, object>>(read, p).Compile();
        }
        var prop = owner.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop != null && prop.CanRead)
        {
            var getter = prop.GetGetMethod(true);
            if (getter != null)
            {
                var p = Expression.Parameter(typeof(object), "o");
                var call = Expression.Convert(Expression.Call(Expression.Convert(p, owner), getter), typeof(object));
                return Expression.Lambda<Func<object, object>>(call, p).Compile();
            }
        }
        Util.Log.Debug($"OffScreenCardThrottle: field/property accessor for {owner.Name}.{name} not found.");
        return null;
    }
}
