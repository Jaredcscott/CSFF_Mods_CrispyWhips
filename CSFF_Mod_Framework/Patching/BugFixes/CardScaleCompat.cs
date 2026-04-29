using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CSFFModFramework.Patching.BugFixes;

// CardSizeReduce compat for EA 0.62b+
//
// Root cause (confirmed 2026-04-20): GameManager.IsInitializing and GameManager.NotInBase
// changed from public fields to auto-properties in EA 0.62b. CSR uses AccessTools.Field()
// which only finds fields, not auto-property backing fields, so it gets null and never sets
// IsEnable=true — all CSR scaling patches return early, producing no scaling.
//
// Fix: patch AccessTools.Field to fall back to the C# compiler-generated backing field
// (<PropName>k__BackingField) when the exact field name is not found on GameManager or
// InGameCardBase. This restores CSR's ability to read/write these properties at runtime.
// Once fixed, CSR handles all scaling itself and our shim is not needed.
internal static class CardScaleCompat
{
    private static float _scale = 1f;
    private static string[] _scaledLineNames = Array.Empty<string>();
    // When CSR is present it already adjusts slot container sizes for all lines via DoubleLine/SlotScale.
    // Scaling element positions on top of that causes double-reduction and layout breakage.
    // In supplement mode we apply only localScale (LateUpdate) for Base/Location, not GetElementPosition.
    private static bool _csrSupplementMode;

    private static Type _dvlgType;
    private static Type _cardLineType;
    private static Type _cardBaseType;

    private static readonly HashSet<int> _scaledGoIds = new();
    private static readonly HashSet<int> _scaledTransformIds = new();
    private static readonly List<Component> _scaledLineRefs = new();

    private static MethodInfo _updateListMethod;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        // If ModCore is present, don't apply any Pikachu compat — let their mods run cleanly.
        if (IsModCorePresent())
        {
            Util.Log.Info("CardScaleCompat: ModCore detected; skipping all Pikachu compat.");
            return;
        }

        var cfgPath = Path.Combine(Paths.ConfigPath, "Pikachu.CSFF.CardSizeReduce.cfg");
        if (!File.Exists(cfgPath)) return;

        // NOTE: can't use Chainloader.PluginInfos here (CSR loads after us); inspect DLL on disk.
        var csrVersion = FindCsrDllVersion();

        var scaleCfg = config.Bind(
            "CardScale", "SlotScaleFactor", 0.75f,
            "Card slot scale factor. 1.0 = default, 0.75 = 75%.");
        _scale = Mathf.Clamp(scaleCfg.Value, 0.25f, 1f);

        if (csrVersion != null)
        {
            // CSR is installed. Fix its broken AccessTools.Field calls so it can read
            // GameManager.IsInitializing and GameManager.NotInBase (now auto-properties).
            // Apply the fix synchronously here — CSR's PatchAll runs after our Awake returns.
            ApplyCsrFieldFix(harmony);
            // CSR handles Inventory/Explorable/Blueprint scaling (IsAllowScaleDown()=true).
            // Base and Location lines are excluded by CSR's IsAllowScaleDown()=false.
            // Run our shim for those two lines so all card rows scale uniformly.
            _scaledLineNames = new[] { "BaseSlotsLine", "LocationSlotsLine" };
            _csrSupplementMode = true;
            if (!Mathf.Approximately(_scale, 1f))
                Plugin.Instance.StartCoroutine(SetupCompat(harmony));
            Util.Log.Info($"CardScaleCompat: CSR {csrVersion} detected; AccessTools.Field fix applied, shim active for BaseSlotsLine/LocationSlotsLine (scale={_scale:P0}).");
            return;
        }

        // CSR config exists but DLL not found — run our own shim as fallback for all lines.
        Util.Log.Info("CardScaleCompat: CSR config present but DLL not found; activating fallback scaling shim.");

        var linesCfg = config.Bind(
            "CardScale", "ScaledLines",
            "ItemSlotsLine,BaseSlotsLine,LocationSlotsLine,ExplorableSlotsLine,BlueprintSlotsLine",
            "Comma-separated GraphicsManager CardLine fields to scale (fallback shim, only active when CSR DLL is absent).");
        _scaledLineNames = SplitCsv(linesCfg.Value);

        if (Mathf.Approximately(_scale, 1f)) return;

        Plugin.Instance.StartCoroutine(SetupCompat(harmony));
        Util.Log.Info($"CardScaleCompat: fallback shim active - slot scale {_scale:P0}, lines=[{string.Join(",", _scaledLineNames)}]");
    }

    // Patch AccessTools.Field to resolve auto-property backing fields when the plain field name
    // is not found. Scoped to GameManager and InGameCardBase only to avoid unintended side effects.
    // This fixes CSR 3.3.0's inability to read IsInitializing / NotInBase in EA 0.62b.
    static void ApplyCsrFieldFix(Harmony harmony)
    {
        try
        {
            var fieldMethod = typeof(AccessTools).GetMethod(
                "Field",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Type), typeof(string) },
                null);
            if (fieldMethod == null)
            {
                Util.Log.Warn("CardScaleCompat: AccessTools.Field(Type,string) not found — CSR fix skipped.");
                return;
            }
            harmony.Patch(fieldMethod,
                postfix: new HarmonyMethod(typeof(CardScaleCompat), nameof(AccessToolsField_Postfix)));
            Util.Log.Debug("[CSC] AccessTools.Field patched for auto-property backing field fallback.");
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"CardScaleCompat: failed to patch AccessTools.Field: {ex.Message}");
        }
    }

    // Postfix on AccessTools.Field: when the exact field is not found on a game type, try the
    // C# auto-property backing field pattern "<PropName>k__BackingField".
    static void AccessToolsField_Postfix(Type type, string name, ref FieldInfo __result)
    {
        if (__result != null || type == null || name == null) return;
        if (type.Name != "GameManager" && type.Name != "InGameCardBase") return;
        var backing = type.GetField(
            $"<{name}>k__BackingField",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (backing != null)
        {
            __result = backing;
            Util.Log.Debug($"[CSC] AccessTools.Field: {type.Name}.{name} → <{name}>k__BackingField");
        }
    }

    static bool IsModCorePresent()
    {
        try
        {
            // Check if any ModCore DLL is present in the plugins directory.
            // ModCore GUIDs: Pikachu.CSTI.ModCore, Pikachu.CSFF.ModCore, Dop.plugin.CSTI.ModLoader
            var pluginsDir = Paths.PluginPath;
            if (string.IsNullOrEmpty(pluginsDir) || !Directory.Exists(pluginsDir)) return false;

            // Scan for known ModCore DLL names
            foreach (var dllName in new[] { "ModCore.dll", "ModLoader.dll" })
            {
                var dlls = Directory.GetFiles(pluginsDir, dllName, SearchOption.AllDirectories);
                if (dlls.Length > 0) return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"[CSC] IsModCorePresent: {ex.Message}");
            return false;
        }
    }

    static Version FindCsrDllVersion()
    {
        try
        {
            // Scan BepInEx/plugins recursively for any DLL named CardSizeReduce.dll
            // and return its AssemblyName.Version. GetAssemblyName doesn't load the
            // assembly into the AppDomain, so it's safe to call during plugin init.
            var pluginsDir = Paths.PluginPath;
            if (string.IsNullOrEmpty(pluginsDir) || !Directory.Exists(pluginsDir)) return null;
            foreach (var dll in Directory.GetFiles(pluginsDir, "CardSizeReduce.dll", SearchOption.AllDirectories))
            {
                try { return System.Reflection.AssemblyName.GetAssemblyName(dll).Version; }
                catch { }
            }
            return null;
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"[CSC] FindCsrDllVersion: {ex.Message}");
            return null;
        }
    }

    static Version GetInstalledPluginVersion(string guid)
    {
        try
        {
            // BepInEx 5.x: Chainloader.PluginInfos[guid].Metadata.Version
            var chainloaderType = Type.GetType("BepInEx.Bootstrap.Chainloader, BepInEx");
            if (chainloaderType == null) return null;
            var infosProp = chainloaderType.GetProperty("PluginInfos", BindingFlags.Public | BindingFlags.Static);
            var infos = infosProp?.GetValue(null) as System.Collections.IDictionary;
            if (infos == null || !infos.Contains(guid)) return null;
            var pluginInfo = infos[guid];
            if (pluginInfo == null) return null;
            var metaProp = pluginInfo.GetType().GetProperty("Metadata");
            var meta = metaProp?.GetValue(pluginInfo);
            if (meta == null) return null;
            var verProp = meta.GetType().GetProperty("Version");
            return verProp?.GetValue(meta) as Version;
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"[CSC] GetInstalledPluginVersion({guid}): {ex.Message}");
            return null;
        }
    }

    static string[] SplitCsv(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var p in value.Split(',')) { var t = p.Trim(); if (t.Length > 0) list.Add(t); }
        return list.ToArray();
    }

    static IEnumerator SetupCompat(Harmony harmony)
    {
        yield return null;

        _dvlgType = FindGameType("DynamicViewLayoutGroup");
        _cardLineType = FindGameType("ZoomLevelCardLine") ?? FindGameType("CardLine");
        _cardBaseType = FindGameType("InGameCardBase");

        if (_dvlgType == null || _cardLineType == null)
        {
            Util.Log.Warn("CardScaleCompat: DynamicViewLayoutGroup or ZoomLevelCardLine not found.");
            yield break;
        }

        _updateListMethod = _dvlgType.GetMethod("UpdateList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        var gmType = FindGameType("GraphicsManager");
        bool ok = true;

        if (gmType != null)
            ok &= SafePatcher.TryPatch(harmony, gmType, "Init",
                postfix: new HarmonyMethod(typeof(CardScaleCompat), nameof(GraphicsManagerInit_Postfix)));

        ok &= SafePatcher.TryPatch(harmony, _dvlgType, "UpdateList",
            prefix: new HarmonyMethod(typeof(CardScaleCompat), nameof(UpdateList_Prefix)));

        ok &= SafePatcher.TryPatch(harmony, _dvlgType, "GetElementPosition",
            postfix: new HarmonyMethod(typeof(CardScaleCompat), nameof(GetElementPosition_Postfix)));

        if (_cardBaseType != null)
            ok &= SafePatcher.TryPatch(harmony, _cardBaseType, "LateUpdate",
                postfix: new HarmonyMethod(typeof(CardScaleCompat), nameof(LateUpdate_Postfix)));

        if (!ok) Util.Log.Warn("CardScaleCompat: one or more patches failed.");
        Util.Log.Debug($"CardScaleCompat: patches active (scale={_scale:P0})");

        yield return new WaitForSeconds(2f);
        RebuildScaledTransforms();
        ForceUpdateLists();
    }

    static Type FindGameType(string name)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var an = asm.GetName().Name;
            if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass") continue;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types ?? Array.Empty<Type>(); }
            catch { continue; }
            foreach (var t in types)
            {
                if (t == null) continue;
                try { if (t.Name == name) return t; } catch { }
            }
        }
        return null;
    }

    static void GraphicsManagerInit_Postfix(object __instance)
    {
        try
        {
            _scaledGoIds.Clear();
            _scaledTransformIds.Clear();
            _scaledLineRefs.Clear();
            var bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var gmType = __instance.GetType();
            foreach (var fname in _scaledLineNames)
            {
                var fi = gmType.GetField(fname, bf);
                if (fi == null) { Util.Log.Debug($"[CSC] Field not found: {fname}"); continue; }
                var cl = fi.GetValue(__instance) as Component;
                if (cl == null) continue;
                _scaledGoIds.Add(cl.gameObject.GetInstanceID());
                _scaledLineRefs.Add(cl);
                Util.Log.Debug($"[CSC] Register: {fname}");
            }
            Util.Log.Info($"CardScaleCompat: GraphicsManager.Init - {_scaledGoIds.Count} lines registered");
        }
        catch (Exception ex) { Util.Log.Warn($"CardScaleCompat GraphicsManagerInit_Postfix: {ex.Message}"); }
    }

    static void UpdateList_Prefix(object __instance)
    {
        if (_scaledGoIds.Count == 0) return;
        var comp = __instance as Component;
        if (comp == null) return;
        if (!_scaledGoIds.Contains(comp.gameObject.GetInstanceID())) return;
        try { RebuildScaledTransforms(); } catch { }
    }

    static void GetElementPosition_Postfix(object __instance, ref Vector3 __result)
    {
        if (_csrSupplementMode || _scaledGoIds.Count == 0) return;
        var comp = __instance as Component;
        if (comp == null) return;
        if (!_scaledGoIds.Contains(comp.gameObject.GetInstanceID())) return;
        __result = new Vector3(__result.x * _scale, __result.y * _scale, __result.z);
    }

    static void LateUpdate_Postfix(object __instance)
    {
        if (_scaledTransformIds.Count == 0) return;
        var comp = __instance as Component;
        if (comp == null) return;
        var rt = comp.transform as RectTransform;
        if (rt == null) return;
        if (!_scaledTransformIds.Contains(rt.GetInstanceID())) return;
        if (!Mathf.Approximately(rt.localScale.x, _scale))
            rt.localScale = new Vector3(_scale, _scale, 1f);
    }

    static void RebuildScaledTransforms()
    {
        _scaledTransformIds.Clear();
        if (_cardBaseType == null || _scaledLineRefs.Count == 0) return;
        foreach (var cl in _scaledLineRefs)
        {
            if (cl == null) continue;
            foreach (Component card in cl.GetComponentsInChildren(_cardBaseType, includeInactive: false))
            {
                var rt = card.transform as RectTransform;
                if (rt != null) _scaledTransformIds.Add(rt.GetInstanceID());
            }
        }
        Util.Log.Debug($"[CSC] Whitelist: {_scaledTransformIds.Count} cards");
    }

    static void ForceUpdateLists()
    {
        if (_updateListMethod == null || _dvlgType == null || _scaledGoIds.Count == 0) return;
        try
        {
            foreach (var obj in UnityEngine.Object.FindObjectsOfType(_dvlgType))
            {
                var comp = obj as Component;
                if (comp == null || !_scaledGoIds.Contains(comp.gameObject.GetInstanceID())) continue;
                _updateListMethod.Invoke(comp, null);
            }
        }
        catch (Exception ex) { Util.Log.Debug($"[CSC] ForceUpdateLists: {ex.Message}"); }
    }
}
