using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Enhances the Large Copper Saw's tree-cutting effectiveness.
    /// When the saw is dragged onto a large tree, an extra -25 Progress is applied
    /// before the normal Cut Tree action (which also does -25), totalling -50 per chop.
    /// Result: Pine/Willow in 1 action, Oak in 2 actions (50% per hit).
    /// </summary>
    public static class SawEffectPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        private const string SawUniqueID = "advanced_copper_tools_large_saw";

        private static readonly HashSet<string> LargeTreeGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "41fbf7771da1b9f4ea13af0bc1ea4341", // TreePineLarge
            "14201221856a7b34fb86d602e0359b83", // TreeOakLarge
            "e8287a79ea2ea4245b4a83ce727c4c9d", // TreeBirchLarge
            "f27a6838066ae10428aa6df6d6259221"  // TreeWillowLarge
        };

        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Cached reflection accessors (populated on first use)
        private static PropertyInfo _cardModelProp;
        private static FieldInfo _uniqueIdField;
        private static bool _reflectionCached;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameManagerType = AccessTools.TypeByName("GameManager");
                if (gameManagerType == null)
                {
                    Logger.LogError("[SawEffect] GameManager type not found.");
                    return;
                }

                var actionRoutine = AccessTools.Method(gameManagerType, "ActionRoutine");
                if (actionRoutine == null)
                {
                    Logger.LogError("[SawEffect] GameManager.ActionRoutine not found.");
                    return;
                }

                var prefix = AccessTools.Method(typeof(SawEffectPatch), nameof(ActionRoutine_Prefix));
                harmony.Patch(actionRoutine, prefix: new HarmonyMethod(prefix));
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SawEffect] Failed to apply patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Prefix on GameManager.ActionRoutine. When the Large Saw is dragged onto a large tree,
        /// apply an extra -25 to the tree's Progress before the normal action runs.
        /// </summary>
        static void ActionRoutine_Prefix(
            object _Action,
            object _ReceivingCard,
            object _GivenCard)
        {
            try
            {
                if (_GivenCard == null || _ReceivingCard == null) return;

                string givenUid = GetCardUniqueId(_GivenCard);
                if (givenUid != SawUniqueID) return;

                string recvUid = GetCardUniqueId(_ReceivingCard);
                if (recvUid == null || !LargeTreeGuids.Contains(recvUid)) return;

                // Apply extra -25 Progress damage to the tree
                float extraDamage = -25f;
                if (ApplyProgressChange(_ReceivingCard, extraDamage))
                {
                    Logger.LogDebug($"[SawEffect] Applied extra {extraDamage} Progress to {recvUid}");
                }
            }
            catch (Exception ex)
            {
                // Silently fail — don't break the game action
                Logger.LogError($"[SawEffect] Prefix error: {ex.Message}");
            }
        }

        private static string GetCardUniqueId(object inGameCard)
        {
            if (inGameCard == null) return null;

            CacheReflection(inGameCard);

            if (_cardModelProp == null) return null;
            var cardData = _cardModelProp.GetValue(inGameCard);
            if (cardData == null) return null;

            if (_uniqueIdField == null)
                _uniqueIdField = cardData.GetType().GetField("UniqueID", Flags)
                              ?? AccessTools.Field(cardData.GetType(), "UniqueID");
            if (_uniqueIdField == null) return null;

            return _uniqueIdField.GetValue(cardData) as string;
        }

        private static void CacheReflection(object inGameCard)
        {
            if (_reflectionCached) return;
            _reflectionCached = true;

            var cardType = inGameCard.GetType();
            _cardModelProp = cardType.GetProperty("CardModel", Flags)
                          ?? cardType.GetProperty("CardData", Flags);
        }

        /// <summary>
        /// Directly modify the Progress durability on an InGameCardBase instance.
        /// Path: card.DurabilityStats.Progress.CurrentValue (or equivalent)
        /// </summary>
        private static bool ApplyProgressChange(object inGameCard, float change)
        {
            var cardType = inGameCard.GetType();

            // Try direct CurrentProgress field/property first
            var directProgress = cardType.GetProperty("CurrentProgress", Flags);
            if (directProgress != null && directProgress.CanRead && directProgress.CanWrite)
            {
                float val = Convert.ToSingle(directProgress.GetValue(inGameCard));
                directProgress.SetValue(inGameCard, val + change);
                return true;
            }

            // Try DurabilityStats -> Progress -> CurrentValue path
            var statsField = cardType.GetField("DurabilityStats", Flags)
                          ?? cardType.GetField("CardDurabilities", Flags)
                          ?? cardType.GetField("Durabilities", Flags);
            if (statsField == null) return TryFlatDurabilityField(inGameCard, cardType, change);

            var stats = statsField.GetValue(inGameCard);
            if (stats == null) return false;

            var statsType = stats.GetType();
            var progressProp = statsType.GetProperty("Progress", Flags)
                            ?? statsType.GetProperty("CurrentProgress", Flags);
            if (progressProp == null)
            {
                // Try as a field instead
                var progressField = statsType.GetField("Progress", Flags);
                if (progressField == null) return TryFlatDurabilityField(inGameCard, cardType, change);

                var progressVal = progressField.GetValue(stats);
                if (progressVal == null) return false;
                return ModifyDurabilityValue(progressVal, progressField, stats, statsField, inGameCard, change);
            }

            var progressObj = progressProp.GetValue(stats);
            if (progressObj == null) return false;

            return ModifyDurabilityValue(progressObj, null, stats, statsField, inGameCard, change);
        }

        private static bool ModifyDurabilityValue(object durObj, FieldInfo durField, object parent,
            FieldInfo parentField, object root, float change)
        {
            var durType = durObj.GetType();

            // Try CurrentValue property
            var curProp = durType.GetProperty("CurrentValue", Flags)
                       ?? durType.GetProperty("FloatValue", Flags);
            if (curProp != null && curProp.CanRead && curProp.CanWrite)
            {
                float cur = Convert.ToSingle(curProp.GetValue(durObj));
                curProp.SetValue(durObj, cur + change);

                // Write back if value type
                if (durType.IsValueType && durField != null)
                {
                    durField.SetValue(parent, durObj);
                    if (parent != null && parent.GetType().IsValueType && parentField != null)
                        parentField.SetValue(root, parent);
                }
                return true;
            }

            // Try FloatValue field
            var valField = durType.GetField("CurrentValue", Flags)
                        ?? durType.GetField("FloatValue", Flags);
            if (valField != null)
            {
                float cur = Convert.ToSingle(valField.GetValue(durObj));
                valField.SetValue(durObj, cur + change);

                if (durType.IsValueType && durField != null)
                {
                    durField.SetValue(parent, durObj);
                    if (parent != null && parent.GetType().IsValueType && parentField != null)
                        parentField.SetValue(root, parent);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Fallback: look for a flat CurrentProgress or ProgressValue field directly on the card.
        /// </summary>
        private static bool TryFlatDurabilityField(object card, Type cardType, float change)
        {
            foreach (var name in new[] { "CurrentProgress", "ProgressValue", "Progress" })
            {
                var field = cardType.GetField(name, Flags);
                if (field != null && (field.FieldType == typeof(float) || field.FieldType == typeof(double)))
                {
                    float cur = Convert.ToSingle(field.GetValue(card));
                    field.SetValue(card, cur + change);
                    return true;
                }

                var prop = cardType.GetProperty(name, Flags);
                if (prop != null && prop.CanRead && prop.CanWrite &&
                    (prop.PropertyType == typeof(float) || prop.PropertyType == typeof(double)))
                {
                    float cur = Convert.ToSingle(prop.GetValue(card));
                    prop.SetValue(card, cur + change);
                    return true;
                }
            }
            return false;
        }
    }
}
