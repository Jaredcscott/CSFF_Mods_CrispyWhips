using System;
using System.Collections;
using HarmonyLib;
using BepInEx.Logging;
using UnityEngine;

namespace Advanced_Copper_Tools.Patcher
{
    // Spiritual ward for the merged metal armor (owner decision A4, 2026-10-22; table in ArmorWardTable.cs).
    //
    // CardData.ArmorValueMultiplier has one caller in the game, EncounterPopup.ApplyArmorCard inside
    // GenerateAndApplyPlayerWound (.decomp/EncounterPopup.cs 3784; research section 6, F3), so a postfix there
    // touches only the wound roll. It is still gated: GameLoadPatch.EncounterArmorRepair_Prefix (ACT's existing
    // prefix on GenerateAndApplyPlayerWound) calls BeginWound, which sets SpiritualOnly when EVERY damage type
    // of the current enemy action is DMG_Spiritual (a mixed attack gets no armor bonus in vanilla either,
    // .decomp/BodyTemplate.cs CalculateArmor), and the finalizer clears it however the method exits.
    internal static class ArmorWardPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        internal const string SpiritualDamageName = "DMG_Spiritual";

        // True only between BeginWound and the GenerateAndApplyPlayerWound finalizer, for a purely spiritual blow.
        internal static bool SpiritualOnly;

        private static readonly System.Collections.Generic.HashSet<string> _warned =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (!_warned.Add(cause)) return;
            Logger?.LogError("[ACT-Ward] " + message);
        }

        internal static void Apply(Harmony harmony, System.Reflection.MethodInfo woundMethod)
        {
            try
            {
                var multiplier = AccessTools.Method(typeof(CardData), "ArmorValueMultiplier", new[] { typeof(InGameCardBase) });
                if (multiplier == null)
                {
                    WarnOnce("multiplier-missing", "CardData.ArmorValueMultiplier(InGameCardBase) not found; the merged armor's spiritual ward is off (Ghost Bronze and the shields block only their plain armor against spirits).");
                    return;
                }
                harmony.Patch(multiplier, postfix: new HarmonyMethod(typeof(ArmorWardPatch), nameof(ArmorValueMultiplier_Postfix)));
                if (woundMethod != null)
                    harmony.Patch(woundMethod, finalizer: new HarmonyMethod(typeof(ArmorWardPatch), nameof(Wound_Finalizer)));
                else
                    WarnOnce("wound-missing", "GenerateAndApplyPlayerWound not found; the spiritual ward is never armed.");
            }
            catch (Exception ex)
            {
                WarnOnce("patch-failed", "patching the spiritual ward failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        // Called from GameLoadPatch.EncounterArmorRepair_Prefix with the EncounterPopup instance.
        internal static void BeginWound(object encounterPopup)
        {
            SpiritualOnly = false;
            try
            {
                var popup = encounterPopup as EncounterPopup;
                if (popup == null)
                {
                    WarnOnce("popup-null", "BeginWound: the wound prefix passed no EncounterPopup; the spiritual ward stays off.");
                    return;
                }
                var encounter = popup.CurrentEncounter;
                if (encounter == null)
                {
                    WarnOnce("encounter-null", "BeginWound: EncounterPopup.CurrentEncounter was null; the spiritual ward stays off for this blow.");
                    return;
                }
                var action = encounter.CurrentEnemyAction;
                if (action == null) return;   // no attack this round: nothing to ward
                SpiritualOnly = IsSpiritualOnly(action.DamageTypes);
            }
            catch (Exception ex)
            {
                SpiritualOnly = false;
                WarnOnce("begin-failed", "BeginWound failed; the spiritual ward stays off: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        // Every entry named DMG_Spiritual, non-null and non-empty: the game's own rule for when a
        // damage-type armor modifier counts (.decomp/BodyTemplate.cs CalculateArmor).
        internal static bool IsSpiritualOnly(IList damageTypes)
        {
            if (damageTypes == null || damageTypes.Count == 0) return false;
            foreach (var entry in damageTypes)
            {
                var so = entry as UnityEngine.Object;
                if (so == null) return false;
                if (!string.Equals(so.name, SpiritualDamageName, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static void ArmorValueMultiplier_Postfix(CardData __instance, InGameCardBase _ForCard, ref float __result)
        {
            if (!SpiritualOnly) return;
            try
            {
                if (__instance == null || _ForCard == null) return;
                var uid = __instance.UniqueID;
                if (!ArmorWardTable.IsMerged(uid)) return;
                int rung = Mathf.RoundToInt(_ForCard.CurrentSpecial4);
                float factor;
                if (ArmorWardTable.TryGetFactor(uid, rung, out factor))
                    __result = factor;
                else
                    WarnOnce("rung-" + rung, "merged armor " + uid + " has Metal Type " + rung + ", which the ward table does not list; it blocks its plain armor only.");
            }
            catch (Exception ex)
            {
                WarnOnce("postfix-failed", "the spiritual ward postfix failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
        }

        private static Exception Wound_Finalizer(Exception __exception)
        {
            SpiritualOnly = false;
            return __exception;
        }
    }
}
