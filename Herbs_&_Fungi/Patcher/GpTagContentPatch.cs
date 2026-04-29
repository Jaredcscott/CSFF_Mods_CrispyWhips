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
    /// Populates the four auto-created CardTabGroups used as RequiredTabGroup refs by the
    /// pickle blueprints. The CSFFModFramework auto-creates empty CardTabGroups for
    /// unresolved GpTag_* references, so the in-game blueprint slot displays the raw GpTag
    /// name and accepts no cards. We fill IncludedCards with the proper raw-only ingredient
    /// lists and set a friendly TabName.
    /// </summary>
    public static class GpTagContentPatch
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, (string Label, string[] CardUids)> Spec =
            new(StringComparer.Ordinal)
        {
            ["GpTag_HnF_Pickleable_Frog"] = ("Raw Frog", new[]
            {
                "6f0031502c513cc4085f7c8b912b7a3c", // FrogRaw
            }),
            ["GpTag_HnF_Pickleable_Vegetable"] = ("Raw Vegetable", new[]
            {
                "herbs_fungi_ginger",
                "herbs_fungi_ginseng",
                "5ad63f32ed767c64190a64d79841d023", // FirerootFresh
                "ac174c399999d14489fb3788c8931e93", // TurnrootFresh
            }),
            ["GpTag_HnF_Pickleable_Mushroom"] = ("Raw Mushroom", new[]
            {
                "herbs_fungi_golden_oyster",
                "herbs_fungi_king_oyster",
                "herbs_fungi_morel_mushroom",
                "herbs_fungi_shiitake",
            }),
            ["GpTag_HnF_Pickleable_Meat"] = ("Raw Meat", new[]
            {
                "5dc9560f81be57c4b9e22780f8a5ad78", // BirdMeatRaw
                "3103c059d34608a4bb95c426d9ac77e0", // FishMeatFattyRaw
                "272fe32c20eebef4d82290c7a31ac885", // FishMeatLeanRaw
                "be70da939b3b49040817669f6e681ef1", // FishMeatFillingRaw
                "fe07d4d800bcc8646a0ff2513c78d5df", // MeatRaw
                "8942f341438ddd54a9f8ffc0765adf7b", // MeatMincedRaw
                "e91048e3986b7d24c81daecd50dbf3c1", // MeatBrainRaw
                "c7316260c15f62d4388ac38f348e30ba", // MeatHeartRaw
                "3f25c8648b35bb34daa5e7c869d5cfdd", // MeatLiverRaw
                "1952653c957ceaf45aea2c8770035f16", // MeatLungRaw
                "2016ec25335a2e044968ed6afc679b81", // MeatKidneyRaw
                "0e4108f9784d20c4a9aafd550b20cf23", // MeatStomachRaw
                "44a84ff12c3d43d4ea2c5c8162bf35c1", // MeatStomachCalfRaw
                "84d0e7f1c3991ac40ad70277e7119674", // MeatIntestinesRaw
                "e8cc003db16702d4e9d2ddeac6b9b759", // MeatFillingRaw
                "1f50bcfa52e10064dbacc3834f741700", // MeatFillingRichRaw
            }),
        };

        private static ManualLogSource Logger => Plugin.Logger;

        public static void Populate()
        {
            try
            {
                var ctgType = AccessTools.TypeByName("CardTabGroup");
                if (ctgType == null) { Logger?.LogError("[GpTag] CardTabGroup type not found"); return; }
                var cardDataType = AccessTools.TypeByName("CardData");
                var uidType      = AccessTools.TypeByName("UniqueIDScriptable");
                if (cardDataType == null || uidType == null) { Logger?.LogError("[GpTag] CardData/UniqueIDScriptable type not found"); return; }

                var findGeneric = typeof(Resources).GetMethods()
                    .FirstOrDefault(m => m.Name == "FindObjectsOfTypeAll" && m.IsGenericMethod);
                if (findGeneric == null) { Logger?.LogError("[GpTag] Resources.FindObjectsOfTypeAll<T> not found"); return; }

                var allGroups = findGeneric.MakeGenericMethod(ctgType).Invoke(null, null) as Array;
                if (allGroups == null) { Logger?.LogError("[GpTag] FindObjectsOfTypeAll returned null"); return; }

                var byName = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var g in allGroups)
                {
                    if (g is UnityEngine.Object uo && Spec.ContainsKey(uo.name))
                        byName[uo.name] = g;
                }

                var includedField = AccessTools.Field(ctgType, "IncludedCards");
                var tabNameField  = AccessTools.Field(ctgType, "TabName");
                if (includedField == null) { Logger?.LogError("[GpTag] CardTabGroup.IncludedCards field not found"); return; }

                var getFromID = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetFromID" && m.IsGenericMethodDefinition)
                    ?.MakeGenericMethod(cardDataType);
                if (getFromID == null) { Logger?.LogError("[GpTag] UniqueIDScriptable.GetFromID<T> not found"); return; }

                int populated = 0;
                foreach (var kv in Spec)
                {
                    var name = kv.Key;
                    var label = kv.Value.Label;
                    var uids = kv.Value.CardUids;
                    if (!byName.TryGetValue(name, out var group))
                    {
                        Logger?.LogWarning($"[GpTag] {name} not found at runtime — blueprint slot will not be populated");
                        continue;
                    }

                    // Resolve CardData refs
                    var resolved = new List<object>(uids.Length);
                    foreach (var u in uids)
                    {
                        var cd = getFromID.Invoke(null, new object[] { u });
                        if (cd != null) resolved.Add(cd);
                        else Logger?.LogWarning($"[GpTag] {name}: card not found '{u}'");
                    }
                    if (resolved.Count == 0) continue;

                    // Write IncludedCards (CardData[] or List<CardData> — handle both)
                    var fieldType = includedField.FieldType;
                    if (fieldType.IsArray)
                    {
                        var arr = Array.CreateInstance(cardDataType, resolved.Count);
                        for (int i = 0; i < resolved.Count; i++) arr.SetValue(resolved[i], i);
                        includedField.SetValue(group, arr);
                    }
                    else
                    {
                        var listType = typeof(List<>).MakeGenericType(cardDataType);
                        var list = Activator.CreateInstance(listType) as IList;
                        foreach (var cd in resolved) list.Add(cd);
                        includedField.SetValue(group, list);
                    }

                    // Set friendly TabName.DefaultText so the blueprint slot doesn't show "GpTag_..."
                    if (tabNameField != null)
                    {
                        var tabName = tabNameField.GetValue(group);
                        if (tabName != null)
                        {
                            var dtField = tabName.GetType().GetField("DefaultText", All);
                            if (dtField != null)
                            {
                                dtField.SetValue(tabName, label);
                                tabNameField.SetValue(group, tabName);
                            }
                        }
                    }

                    populated++;
                    Logger?.LogDebug($"[GpTag] populated {name} with {resolved.Count} cards (label='{label}')");
                }

                Logger?.LogInfo($"[GpTag] populated {populated}/{Spec.Count} pickleable GpTags");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[GpTag] Populate failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }
    }
}
