using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;

namespace Advanced_Copper_Tools.Patcher
{
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                if (gameLoadType == null)
                {
                    Logger.LogError("[ACT] GameLoad type not found; load patches not applied.");
                    return;
                }

                var loadMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                if (loadMethod == null)
                {
                    Logger.LogError("[ACT] GameLoad.LoadMainGameData not found; load patches not applied.");
                    return;
                }

                var postfix = new HarmonyMethod(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
                postfix.after = new[] { "crispywhips.CSFFModFramework" };
                harmony.Patch(loadMethod, postfix: postfix);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] Failed to patch GameLoad.LoadMainGameData: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        static void LoadMainGameData_Postfix(object __instance)
        {
            try
            {
                var dbField = AccessTools.Field(__instance.GetType(), "DataBase");
                var db = dbField?.GetValue(__instance);
                if (db == null) { Logger.LogError("[ACT] Could not access GameLoad.DataBase"); return; }

                var allDataField = AccessTools.Field(db.GetType(), "AllData");
                var allData = allDataField?.GetValue(db) as IEnumerable;
                if (allData == null) { Logger.LogError("[ACT] Could not access DataBase.AllData"); return; }

                VanillaFireKettlePatch.InjectKettleSlots(allData);
                PatchLeatherDefaultStats(allData);
                DiagnoseArmorEquipmentTags(allData);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] LoadMainGameData postfix error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // SkinLeatherSmall spawned by perks starts with all stats at 0.
        // Blueprint requirements (e.g. Bp_LeatherGreaves) check Special4 (Skin Type) >= 200.
        // Setting defaults here so perk-spawned leather is immediately usable.
        private static void PatchLeatherDefaultStats(IEnumerable allData)
        {
            const string LeatherGuid = "669497ecaa30b634c86bea6b61f31e63"; // SkinLeatherSmall
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var item in allData)
            {
                if (item == null) continue;
                var uid = AccessTools.Field(item.GetType(), "UniqueID")?.GetValue(item) as string;
                if (uid != LeatherGuid) continue;

                SetStatFloat(item, "SpecialDurability4", "FloatValue", 250f, Flags); // Skin Type → tier-1 crafting range (200–299)
                SetStatFloat(item, "SpoilageTime",       "FloatValue", 288f, Flags); // Tannins → full
                SetStatFloat(item, "UsageDurability",    "FloatValue", 288f, Flags); // Flexibility → full
                Logger.LogDebug("[ACT] SkinLeatherSmall default stats patched (SkinType=250, Tannins=288, Flex=288).");
                return;
            }
            Logger.LogError("[ACT] PatchLeatherDefaultStats: SkinLeatherSmall not found in AllData.");
        }

        private static readonly string[] ArmorUids = {
            "advanced_copper_tools_copper_helmet",
            "advanced_copper_tools_copper_breastplate",
            "advanced_copper_tools_copper_gauntlets",
            "advanced_copper_tools_copper_greaves",
        };

        // All four copper armor items share the same multiplier parameters.
        private const string ArmorMultiplierEntryJson =
            "{\"InputDurability\":64,\"Value\":{\"Active\":true," +
            "\"InputValueRange\":{\"x\":0.0,\"y\":100.0}," +
            "\"OutputValueRange\":{\"x\":1.0,\"y\":1.5}," +
            "\"WhenOutOfRange\":0,\"OutOfRangeCustomValue\":0.0}}";

        private static void DiagnoseArmorEquipmentTags(IEnumerable allData)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var armorSet = new System.Collections.Generic.HashSet<string>(ArmorUids);
            foreach (var item in allData)
            {
                if (item == null) continue;
                var uid = AccessTools.Field(item.GetType(), "UniqueID")?.GetValue(item) as string;
                if (uid == null || !armorSet.Contains(uid)) continue;

                // EquipmentTags diagnostic
                var etField = item.GetType().GetField("EquipmentTags", Flags)
                           ?? item.GetType().GetField("<EquipmentTags>k__BackingField", Flags);
                var etArray = etField?.GetValue(item) as System.Collections.IList;
                int count = etArray?.Count ?? -1;
                string tags = count > 0
                    ? string.Join(", ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Cast<object>(etArray), t => {
                        var n = t?.GetType().GetProperty("name")?.GetValue(t) ?? t?.GetType().GetField("name")?.GetValue(t) ?? "?";
                        var id = (t as UnityEngine.Object)?.GetInstanceID();
                        return $"{n}(#{id})";
                    }))
                    : "(none)";
                Logger.LogInfo($"[ACT-Diag] {uid}: EquipmentTags count={count} names=[{tags}]");

                var isArmorField = item.GetType().GetField("IsArmor", Flags);
                Logger.LogInfo($"[ACT-Diag] {uid}: IsArmor={isArmorField?.GetValue(item)}");

                // ArmorValues diagnostic
                var avField = item.GetType().GetField("ArmorValues", Flags);
                var av = avField?.GetValue(item);
                if (av != null)
                {
                    var head = av.GetType().GetField("HeadArmor", Flags)?.GetValue(av);
                    var torso = av.GetType().GetField("TorsoArmor", Flags)?.GetValue(av);
                    Logger.LogInfo($"[ACT-Diag] {uid}: ArmorValues Head={head} Torso={torso}");
                }

                // ArmorValueDurabilitiesMultiplier diagnostic + fix
                var multField = item.GetType().GetField("ArmorValueDurabilitiesMultiplier", Flags);
                var multArray = multField?.GetValue(item) as Array;
                int multCount = multArray?.Length ?? -1;
                Logger.LogInfo($"[ACT-Diag] {uid}: ArmorValueDurabilitiesMultiplier count={multCount}");

                if (multCount == 0 && multField != null)
                    FixArmorMultiplier(item, uid, multField);
            }
        }

        private static void FixArmorMultiplier(object card, string uid, FieldInfo multField)
        {
            try
            {
                var elementType = multField.FieldType.GetElementType();
                if (elementType == null)
                {
                    Logger.LogError($"[ACT-Fix] {uid}: could not get ArmorValueDurabilitiesMultiplier element type");
                    return;
                }

                // JsonUtility.FromJson works for single objects (not arrays).
                // We create one entry and wrap it in a 1-element array.
                var entry = UnityEngine.JsonUtility.FromJson(ArmorMultiplierEntryJson, elementType);
                if (entry == null)
                {
                    Logger.LogError($"[ACT-Fix] {uid}: JsonUtility.FromJson returned null for multiplier entry (type={elementType.Name})");
                    return;
                }

                var newArray = Array.CreateInstance(elementType, 1);
                newArray.SetValue(entry, 0);
                multField.SetValue(card, newArray);
                Logger.LogInfo($"[ACT-Fix] {uid}: ArmorValueDurabilitiesMultiplier restored (1 entry, type={elementType.Name})");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT-Fix] {uid}: FixArmorMultiplier failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void SetStatFloat(object card, string statFieldName, string subFieldName, float value, BindingFlags flags)
        {
            var statField = card.GetType().GetField(statFieldName, flags);
            if (statField == null) return;
            var stat = statField.GetValue(card);
            if (stat == null) return;
            var floatField = stat.GetType().GetField(subFieldName, flags);
            if (floatField == null) return;
            floatField.SetValue(stat, value);
            // Write back in case the stat is a value type (struct).
            statField.SetValue(card, stat);
        }


    }
}
