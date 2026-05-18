using System;
using System.Collections;
using System.Collections.Generic;
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
        private static Type _gameManagerType;
        private static PropertyInfo _gameManagerInstanceProp;
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
                Logger?.LogError($"[EncounterSuppress] ApplyPatch failed: {ex}");
            }
        }

        private static bool StartEncounter_Prefix(object _Encounter, object _WithNPC, bool _SkipEvent)
        {
            try
            {
                if (_WithNPC != null) return true;
                if (!HasWardInPlayerEnv()) return true;
                if (UnityEngine.Random.value > SuppressionChance) return true;

                Logger?.LogDebug("[DeathCapWard] Ward repelled a wild encounter.");
                return false;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[EncounterSuppress] prefix error: {ex}");
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
                if (_gameManagerType == null) _gameManagerType = asm.GetType("GameManager", false);
                if (_inGameCardBaseType != null && _gameManagerType != null) break;
            }
            _gameManagerInstanceProp = _gameManagerType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private static bool HasWardInPlayerEnv()
        {
            InitReflection();
            foreach (var c in CollectCards())
            {
                if (c == null) continue;
                if (GetUniqueId(c) != WardPlacedID) continue;
                if (IsInPlayerEnv(c)) return true;
            }
            return false;
        }

        private static IEnumerable<object> CollectCards()
        {
            var cards = CollectGameManagerCards();
            if (cards.Count > 0) return cards;

            return CollectUnityCards();
        }

        private static List<object> CollectGameManagerCards()
        {
            var cards = new List<object>();
            try
            {
                if (_gameManagerInstanceProp == null) return cards;

                var gm = _gameManagerInstanceProp.GetValue(null, null);
                var allCards = GetMemberValue(gm, "AllCards") as IEnumerable;
                if (allCards == null) return cards;

                foreach (var card in allCards)
                {
                    if (card != null) cards.Add(card);
                }
            }
            catch
            {
                return new List<object>();
            }
            return cards;
        }

        private static List<object> CollectUnityCards()
        {
            var cards = new List<object>();
            if (_inGameCardBaseType == null || !typeof(UnityEngine.Object).IsAssignableFrom(_inGameCardBaseType)) return cards;

            try
            {
                var allCards = UnityEngine.Object.FindObjectsOfType(_inGameCardBaseType);
                if (allCards == null) return cards;

                foreach (var card in allCards)
                {
                    if (card != null) cards.Add(card);
                }
            }
            catch
            {
                return new List<object>();
            }
            return cards;
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
                if (prop != null && (bool)prop.GetValue(env, null)) return true;

                var currentEnv = GetCurrentEnvironment();
                if (currentEnv == null) return false;
                var matchesEnv = env.GetType().GetMethod("MatchesEnv", BindingFlags.Instance | BindingFlags.Public, null, new[] { currentEnv.GetType() }, null);
                if (matchesEnv == null) return false;
                return (bool)matchesEnv.Invoke(env, new[] { currentEnv });
            }
            catch { return false; }
        }

        private static object GetCurrentEnvironment()
        {
            try
            {
                if (_gameManagerInstanceProp == null) return null;

                var gm = _gameManagerInstanceProp.GetValue(null, null);
                return GetMemberValue(gm, "CurrentEnvironment");
            }
            catch { return null; }
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
