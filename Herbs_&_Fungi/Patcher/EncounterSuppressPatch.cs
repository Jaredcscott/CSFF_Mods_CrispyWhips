using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Herbs_And_Fungi.Patcher
{
    /// <summary>
    /// When a Death Cap Ward is placed in the player's current environment, rolls a chance
    /// to suppress incoming wild-animal encounters. NPC-driven encounters are never affected.
    /// </summary>
    public static class EncounterSuppressPatch
    {
        private const string WardPlacedID = "herbs_fungi_death_cap_ward_placed";
        private const float SuppressionChance = 0.60f;

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type _inGameCardBaseType;
        private static bool _reflectionInit;

        private static ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var encounterPopupType = AccessTools.TypeByName("EncounterPopup");
                if (encounterPopupType == null)
                {
                    Logger?.LogError("[EncounterSuppress] EncounterPopup type not found");
                    return;
                }
                var startEncounter = encounterPopupType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "StartEncounter" && m.GetParameters().Length == 3);
                if (startEncounter == null)
                {
                    Logger?.LogError("[EncounterSuppress] EncounterPopup.StartEncounter(3 args) not found");
                    return;
                }
                harmony.Patch(startEncounter,
                    prefix: new HarmonyMethod(typeof(EncounterSuppressPatch), nameof(StartEncounter_Prefix)));
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[EncounterSuppress] ApplyPatch failed: {ex.Message}");
            }
        }

        private static bool StartEncounter_Prefix(object _Encounter, object _WithNPC, bool _SkipEvent)
        {
            try
            {
                if (_WithNPC != null) return true;
                if (!HasWardInPlayerEnv()) return true;
                if (UnityEngine.Random.value > SuppressionChance) return true;

                Logger?.LogInfo("[DeathCapWard] Ward repelled a wild encounter.");
                return false;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[EncounterSuppress] prefix error: {ex.Message}");
                return true;
            }
        }

        private static void InitReflection()
        {
            if (_reflectionInit) return;
            _reflectionInit = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_inGameCardBaseType == null) _inGameCardBaseType = asm.GetType("InGameCardBase", false);
                if (_inGameCardBaseType != null) break;
            }
        }

        private static bool HasWardInPlayerEnv()
        {
            InitReflection();
            if (_inGameCardBaseType == null) return false;

            var allCards = UnityEngine.Object.FindObjectsOfType(_inGameCardBaseType);
            if (allCards == null) return false;

            foreach (var c in allCards)
            {
                if (c == null) continue;
                if (GetUniqueId(c) != WardPlacedID) continue;
                if (IsInPlayerEnv(c)) return true;
            }
            return false;
        }

        private static string GetUniqueId(object card)
        {
            try
            {
                var cardModel = GetMemberValue(card, "CardModel");
                if (cardModel == null) return null;
                return GetMemberValue(cardModel, "UniqueID") as string;
            }
            catch { return null; }
        }

        private static bool IsInPlayerEnv(object card)
        {
            try
            {
                var env = GetMemberValue(card, "CardEnvironment");
                if (env == null) return false;
                var prop = env.GetType().GetProperty("MatchesPlayerEnv", BindingFlags.Instance | BindingFlags.Public);
                if (prop == null) return false;
                return (bool)prop.GetValue(env, null);
            }
            catch { return false; }
        }

        private static object GetMemberValue(object target, string name)
        {
            if (target == null) return null;
            var t = target.GetType();
            var prop = t.GetProperty(name, Flags);
            if (prop != null && prop.CanRead) { try { return prop.GetValue(target, null); } catch { } }
            var field = t.GetField(name, Flags);
            if (field != null) { try { return field.GetValue(target); } catch { } }
            return null;
        }
    }
}
