using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Herbs_And_Fungi.Patcher;

/// <summary>
/// H&F stands alone: obtaining Dried Ginger / Ground Ginseng (via <see cref="SpawnService.CardSpawned"/>)
/// sets the corresponding quest-completion stat to 1, which is what actually gates the Anti-Nausea
/// Tea (A4) / Stimulant Tea (A3) blueprints (see their StatValues). These are independent, item-
/// triggered reveals per Village_Master_Plan's Apothecary reveal table — A1/A2 (CMC's own herb-fetch
/// and pigment quests) do NOT bulk-unlock them. A prior CMC-stat poll fallback that unlocked both
/// teas together on A1 completion was removed 2026-07-21 (wrong signal — see fleet Stage 0 item 1).
/// </summary>
internal static class ApothecaryQuestGatePatch
{
    private const string QuestFlagGinsengUid = "herbs_fungi_quest_ginseng_collection";
    private const string QuestFlagGingerUid = "herbs_fungi_quest_ginger_collection";
    private const string DriedGingerItemUid = "herbs_fungi_dried_ginger";
    private const string GroundGinsengItemUid = "herbs_fungi_ginseng_ground";

    private static bool _initialized;
    private static Type _uidType;
    private static MethodInfo _getFromIdMethod;
    private static object _ginsengFlag;
    private static object _gingerFlag;
    private static bool _ginsengFlagSet;
    private static bool _gingerFlagSet;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            // Item-triggered only: obtaining either item, once, ever, with or without CMC
            // installed, satisfies the corresponding StatValues gate on its own blueprint.
            SpawnService.CardSpawned += OnCardSpawned;

            Plugin.Logger.LogDebug("[ApothecaryQuestGate] Patched.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[ApothecaryQuestGate] Failed to apply: {ex}");
        }
    }

    private static void OnCardSpawned(object card, string uid)
    {
        try
        {
            if (uid == DriedGingerItemUid)
            {
                if (_gingerFlagSet) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm != null && ResolveTypes() && InitializeStatReferences(gm))
                {
                    SetStatValue(gm, QuestFlagGingerUid, 1.0f);
                    _gingerFlagSet = true;
                }
            }
            else if (uid == GroundGinsengItemUid)
            {
                if (_ginsengFlagSet) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm != null && ResolveTypes() && InitializeStatReferences(gm))
                {
                    SetStatValue(gm, QuestFlagGinsengUid, 1.0f);
                    _ginsengFlagSet = true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[ApothecaryQuestGate] OnCardSpawned failed: {ex}");
        }
    }

    private static bool ResolveTypes()
    {
        if (_getFromIdMethod != null) return true;

        var uidType = CardUtil.FindGameType("UniqueIDScriptable");
        if (uidType == null)
        {
            Plugin.Logger.LogWarning("[ApothecaryQuestGate] UniqueIDScriptable type not found — quest-flag gate cannot resolve.");
            return false;
        }

        _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

        _uidType = uidType;
        if (_getFromIdMethod == null)
            Plugin.Logger.LogWarning("[ApothecaryQuestGate] UniqueIDScriptable.GetFromID(string) not found — quest-flag gate cannot resolve.");
        return _getFromIdMethod != null;
    }

    private static bool InitializeStatReferences(object gm)
    {
        if (!_initialized)
        {
            _ginsengFlag ??= _getFromIdMethod?.Invoke(null, new object[] { QuestFlagGinsengUid });
            _gingerFlag ??= _getFromIdMethod?.Invoke(null, new object[] { QuestFlagGingerUid });
            _initialized = (_ginsengFlag != null && _gingerFlag != null);
            if (!_initialized)
            {
                Plugin.Logger.LogWarning($"[ApothecaryQuestGate] Could not resolve quest-flag stats (ginseng={(_ginsengFlag != null)}, ginger={(_gingerFlag != null)}) — Anti-Nausea/Stimulant Tea gate will not advance.");
            }
        }
        return _initialized;
    }

    private static void SetStatValue(object gm, string statUid, float value)
    {
        try
        {
            object statRef = null;
            if (statUid == QuestFlagGinsengUid) statRef = _ginsengFlag;
            else if (statUid == QuestFlagGingerUid) statRef = _gingerFlag;

            if (statRef == null) return;

            if (CardUtil.GetCachedField(gm.GetType(), "StatsDict")?.GetValue(gm) is not IDictionary statsDict)
                return;

            if (!statsDict.Contains(statRef)) return;
            var inGameStat = statsDict[statRef];
            if (inGameStat == null) return;

            StatAccess.SetCurrentValue(inGameStat, value);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[ApothecaryQuestGate] SetStatValue failed for {statUid}: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

}
