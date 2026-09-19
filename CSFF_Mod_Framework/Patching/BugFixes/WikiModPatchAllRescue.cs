using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Stops ONE stale WikiMod patch class from silently killing every WikiMod patch class after it.
///
/// The failure this exists for (EA 0.67i, WikiMod 3.5.1, confirmed 2026-09-11):
///   1. The game changed GraphicsManager.AddSlot from (SlotsTypes, CardData, CardData, int) to
///      (SlotsTypes, CardData, CardData, InGameNPCOrPlayer, int), and made it private.
///   2. WikiMod.GraphicsManagerMod.AddSlotPrefix still declares [HarmonyPatch] against the old
///      4-arg signature, so AccessTools.DeclaredMethod returns null and PatchClassProcessor.Patch
///      throws "ArgumentException: Undefined target method ... AddSlotPrefix".
///   3. Harmony.PatchAll iterates AccessTools.GetTypesFromAssembly(assembly).Do(...) with NO
///      try/catch, so that single throw aborts the WHOLE enumeration and propagates out of
///      WikiMod.Plugin.Awake.
///
/// The damage is therefore never "one broken feature": it is every patch class that sorts after the
/// broken one in metadata order, plus every statement after PatchAll() in WikiMod's Awake. Measured
/// on the deployed 3.5.1 binary: GraphicsManagerMod is type 274 of 582, and 23 of WikiMod's 52
/// Harmony patch classes never applied - InGameCardBaseMod (the detailed card hover tooltip, i.e.
/// "WikiMod stopped showing card stats"), TooltipMod, HoverTooltipPatches, InspectionPopupMod,
/// InGameStatMod, StatDetailsPopupMod, StatInfluenceInfoMod, WeightBarMod, OptionsMenuMod and more,
/// plus EmojiSpriteRuntimeLoader.Initialize and seven explicit TryPatchMethod calls in Awake.
/// Nothing is logged by WikiMod itself, so to a player the mod simply half-disappears.
///
/// What this guard does: a finalizer on PatchClassProcessor.Patch that, when the failing patch class
/// belongs to the WikiMod assembly, logs one actionable Warn and swallows the exception so PatchAll
/// continues to the next type. One class is lost instead of all of its successors.
///
/// Scope is deliberately narrow. A failure in any OTHER assembly (our own mods included) is rethrown
/// untouched, because swallowing those would hide real breakage in code we control. Widening this to
/// other third-party plugins is a one-line change in IsWikiMod, and should not be made without the
/// same kind of measurement this one carries.
///
/// Correctness note: the rescue only restores classes that CAN bind. A RefCheck pass over the live
/// WikiMod 3.5.1 binary found exactly 3 unresolved game members out of 2159 (AddSlot,
/// FindPileForCard, MoveCardToSlot), all confined to DynamicLayoutSlotStackingMod.TryRelocate,
/// ClingyCat.OnCardLoaded and EquipTabContent.HandleSlotAction - none of them in a rescued class -
/// and all 23 rescued classes resolve their declared patch targets against EA 0.67i. The rescue is
/// still a stopgap, not a fix: the real fix is updating WikiMod to the build made for this game
/// version, which is what the Warn tells the player to do.
///
/// Ordering requirement: this MUST be installed synchronously from the framework's Awake, not from
/// the deferred coroutine WikiModQuickFindFix uses. BepInEx loads the framework 3rd and WikiMod last
/// (measured: positions 3 and 21 of 21), and WikiMod's Awake runs its PatchAll before our next frame,
/// so a deferred install would arrive after the damage. Nothing here touches WikiMod types, so there
/// is nothing to wait for: PatchClassProcessor lives in 0Harmony, which is always loaded.
/// </summary>
internal static class WikiModPatchAllRescue
{
    private const string WikiModAssemblyName = "WikiMod";
    private const int MaxSkipLogs = 5;

    private static bool _rescue = true;
    private static bool _patched;
    private static FieldInfo _containerTypeField;
    private static int _skipCount;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        if (_patched) return;

        // No WikiMod on disk: install nothing. Checked from the filesystem rather than from loaded
        // assemblies because WikiMod has not loaded yet at framework Awake (that is the whole point).
        var wikiModVersion = FindWikiModDllVersion();
        if (wikiModVersion == null) return;

        var rescueCfg = config.Bind(
            "Compatibility", "RescueStaleWikiModPatchAll", true,
            "When a WikiMod patch class cannot bind to this game version, skip just that class so "
            + "WikiMod's Harmony PatchAll can finish. Set false to only log the problem and let "
            + "WikiMod abort as it would without the framework, which silently drops every WikiMod "
            + "patch class after the broken one (card tooltips, inspection popup, stat readouts) "
            + "until WikiMod is updated.");
        _rescue = rescueCfg.Value;

        _containerTypeField = AccessTools.Field(typeof(PatchClassProcessor), "containerType");
        if (_containerTypeField == null)
        {
            Util.Log.Warn("WikiModPatchAllRescue: PatchClassProcessor.containerType not found on this Harmony build; "
                          + "guard not installed (a stale WikiMod will abort its own PatchAll).");
            return;
        }

        var target = AccessTools.Method(typeof(PatchClassProcessor), "Patch");
        if (target == null)
        {
            Util.Log.Warn("WikiModPatchAllRescue: PatchClassProcessor.Patch not found on this Harmony build; "
                          + "guard not installed (a stale WikiMod will abort its own PatchAll).");
            return;
        }

        try
        {
            harmony.Patch(target, finalizer: new HarmonyMethod(typeof(WikiModPatchAllRescue), nameof(PatchClassFinalizer)));
            _patched = true;
            Util.Log.Debug($"WikiModPatchAllRescue: guard installed (WikiMod v{wikiModVersion} on disk, rescue={_rescue}).");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"WikiModPatchAllRescue: could not patch PatchClassProcessor.Patch: {Util.Log.ExceptionText(ex)}");
        }
    }

    /// <summary>
    /// Finalizer on PatchClassProcessor.Patch. Swallows only a WikiMod-owned patch class failure;
    /// every other assembly's exception is returned unchanged so it still surfaces.
    /// </summary>
    static Exception PatchClassFinalizer(object __instance, ref List<MethodInfo> __result, Exception __exception)
    {
        if (__exception == null) return null;

        Type container;
        try
        {
            container = _containerTypeField?.GetValue(__instance) as Type;
        }
        catch (Exception ex)
        {
            // Cannot attribute the failure, so never swallow it.
            Util.Log.Debug($"WikiModPatchAllRescue: containerType read failed: {ex.GetType().Name} {ex.Message}");
            return __exception;
        }

        if (container == null || !IsWikiMod(container)) return __exception;

        _skipCount++;
        if (_skipCount <= MaxSkipLogs) LogSkip(container, __exception);
        if (_skipCount == MaxSkipLogs)
            Util.Log.Warn("WikiModFix: further WikiMod patch-class bind failures suppressed silently.");

        if (!_rescue) return __exception;

        // Patch() returns the list of patched methods; PatchAll discards it, but hand back an empty
        // list rather than null so a direct CreateClassProcessor(...).Patch() caller cannot NRE.
        __result ??= new List<MethodInfo>();
        return null;
    }

    static bool IsWikiMod(Type container)
    {
        try
        {
            return string.Equals(container.Assembly.GetName().Name, WikiModAssemblyName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"WikiModPatchAllRescue: assembly-name read failed for {container}: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    static void LogSkip(Type container, Exception ex)
    {
        // Harmony wraps the real cause (ArgumentException: Undefined target method ...) in a
        // HarmonyException, whose own Message is just "Patching exception in method null".
        var cause = ex.InnerException ?? ex;
        var remaining = CountFollowingPatchClasses(container);
        var remainingText = remaining >= 0
            ? $"{remaining} later WikiMod patch class(es)"
            : "every later WikiMod patch class";

        string version;
        try { version = container.Assembly.GetName().Version?.ToString() ?? "unknown"; }
        catch (Exception vex)
        {
            version = "unknown";
            Util.Log.Debug($"WikiModPatchAllRescue: version read failed: {vex.GetType().Name} {vex.Message}");
        }

        Util.Log.Warn(
            $"WikiModFix: WikiMod patch class '{container.FullName}' cannot bind to this game version ({cause.GetType().Name}: {cause.Message}). "
            + $"Harmony's PatchAll aborts the whole pass at the first such failure, which would silently drop {remainingText} "
            + "plus everything after PatchAll() in WikiMod's Awake - its card hover tooltips, inspection popup and stat readouts among them. "
            + $"The installed WikiMod (v{version}) is built against an older game version; update WikiMod to the build for this game version. "
            + (_rescue
                ? "The framework skipped this one class so WikiMod's remaining patch classes still applied."
                : "RescueStaleWikiModPatchAll is disabled, so WikiMod's PatchAll was left to abort."));
    }

    /// <summary>
    /// How many Harmony patch classes sort after this one, i.e. what an abort here would cost.
    /// Assembly.GetTypes() is the same call Harmony's own PatchAll enumerates
    /// (AccessTools.GetTypesFromAssembly), so the index comparison is exact rather than an
    /// assumption about metadata order. Returns -1 when the count cannot be established.
    /// </summary>
    static int CountFollowingPatchClasses(Type container)
    {
        try
        {
            Type[] types;
            try
            {
                types = container.Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                // Partial type load: recover from the types that DID load rather than swallowing
                // (framework CLAUDE.md, ReflectionCache rule). This is an informational count only
                // (feeds the "N later patch class(es)" warning text), so a breadcrumb - not a
                // fallback value - is the whole fix; the outer catch below still logs+returns -1
                // for anything that throws afterward.
                var loaded = rtle.Types?.Count(t => t != null) ?? 0;
                Util.Log.Debug($"WikiModPatchAllRescue: CountFollowingPatchClasses - Assembly partial type load "
                              + $"({loaded} of {rtle.Types?.Length ?? 0} types usable): {rtle.GetType().Name}");
                types = Array.FindAll(rtle.Types, t => t != null);
            }

            var idx = Array.IndexOf(types, container);
            if (idx < 0) return -1;

            var count = 0;
            for (var i = idx + 1; i < types.Length; i++)
                if (IsPatchClass(types[i]))
                    count++;
            return count;
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"WikiModPatchAllRescue: follower count failed: {ex.GetType().Name} {ex.Message}");
            return -1;
        }
    }

    static bool IsPatchClass(Type t)
    {
        if (t == null) return false;
        try
        {
            // Class-level [HarmonyPatch], or any declared method carrying a HarmonyLib attribute.
            // HarmonyPrefix/Postfix/Finalizer derive from Attribute, NOT from HarmonyAttribute, so a
            // single IsAssignableFrom test against HarmonyAttribute would miss most patch methods.
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0) return true;

            foreach (var m in t.GetMethods(AccessTools.all))
            {
                if (m.DeclaringType != t) continue;
                foreach (var attr in m.GetCustomAttributes(false))
                {
                    var at = attr.GetType();
                    if (at.Namespace == "HarmonyLib" && at.Name.StartsWith("Harmony", StringComparison.Ordinal))
                        return true;
                }
            }
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"WikiModPatchAllRescue: patch-class probe failed for {t}: {ex.GetType().Name} {ex.Message}");
        }
        return false;
    }

    static Version FindWikiModDllVersion()
    {
        try
        {
            // GetAssemblyName reads metadata without loading the assembly into the AppDomain, so it
            // is safe during plugin init (same idiom as CardScaleCompat.FindCsrDllVersion).
            var pluginsDir = Paths.PluginPath;
            if (string.IsNullOrEmpty(pluginsDir) || !Directory.Exists(pluginsDir)) return null;
            foreach (var dll in Directory.GetFiles(pluginsDir, "WikiMod.dll", SearchOption.AllDirectories))
            {
                try { return AssemblyName.GetAssemblyName(dll).Version; }
                catch (Exception ex)
                {
                    Util.Log.Debug($"WikiModPatchAllRescue: GetAssemblyName failed for {dll}: {ex.GetType().Name} {ex.Message}");
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"WikiModPatchAllRescue: FindWikiModDllVersion: {Util.Log.ExceptionText(ex)}");
            return null;
        }
    }
}
