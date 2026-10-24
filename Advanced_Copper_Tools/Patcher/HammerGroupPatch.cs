using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using CSFFModFramework.Api;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Fills GpTag_ACT_ShapingHammer, the hammering-tool slot of the pan and stove blueprints
    /// (Bp_CopperPanBlank, Bp_WearableCopperPan, Bp_CopperStove). The rest of ACT uses vanilla's
    /// GpTag_HammeringToolGeneral, which takes 27 tools including knives, axes and the whetstone;
    /// Chiwei (Nexus, 2026-09-28) asked that these three take only a forge hammer, a mallet, a
    /// stone, an antler or a shovel.
    ///
    /// No mod ships a CardTabGroup JSON (root CLAUDE.md, Blueprints), so the group is filled here:
    /// WarpResolver, which runs in the framework's LoadMainGameData postfix before this one,
    /// creates an EMPTY CardTabGroup for a GpTag name nothing defines, and without this pass the
    /// slot shows the raw name and accepts nothing. Same idea as HerbsAndFungi's GpTagContentPatch,
    /// but the group is found through the blueprints that name it, so no FindObjectsOfTypeAll.
    /// Reflection only in this patch. ACT's DLL as a whole does carry typed game references since
    /// 1.18.13 (ArmorWardPatch), so its lib/ game references follow the root CLAUDE.md
    /// Game-Update Reference Refresh.
    /// </summary>
    internal static class HammerGroupPatch
    {
        internal const string GroupName = "GpTag_ACT_ShapingHammer";
        private const string LabelKey = "Advanced_Copper_Tools_GpTag_ShapingHammer_TabName";
        private const string LabelText = "Hammering Tool";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly string[] BlueprintUids =
        {
            "advanced_copper_tools_bp_pan_blank",
            "advanced_copper_tools_bp_wearable_metal_pan",
            "advanced_copper_tools_bp_copper_stove",
        };

        private static readonly string[] ToolUids =
        {
            "e118b8cd90f14b048aab78a0d37e8f61", // ForgeHammer
            "2914e01d9af26f24d92ff61389fb0195", // ToolOrWeapon_Hammer_Metal (metal forge hammer)
            "c8f0e1c1277396f4fbbaed9b62bd1e73", // Mallet
            "a7384e5147b23a642809451cc4ef24fb", // StoneSmall
            "8cae876b49f3aef45afc1ad96b1a4628", // Antler
            "2466c0bf349c4f84db55a4ee8ad67300", // ShovelWooden
            "3587432ac7e29784991ff2cccf92891d", // ToolOrWeapon_Shovel_Metal
            "c1681bf59e15f3045aee3cbc58ec9b0c", // ToolOrWeaponBlunt_Shovel_Metal
        };

        private static ManualLogSource Logger => Plugin.Logger;

        internal static void Populate(IEnumerable allData)
        {
            try
            {
                var wanted = new HashSet<string>(BlueprintUids, StringComparer.Ordinal);
                wanted.UnionWith(ToolUids);
                var byUid = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var item in allData)
                {
                    if (item == null) continue;
                    var uid = item.GetType().GetField("UniqueID", Flags)?.GetValue(item) as string;
                    if (uid != null && wanted.Contains(uid)) byUid[uid] = item;
                }

                var groups = new List<UnityEngine.Object>();
                foreach (var bpUid in BlueprintUids)
                {
                    if (!byUid.TryGetValue(bpUid, out var bp))
                    {
                        Logger.LogWarning($"[ACT] {GroupName}: blueprint '{bpUid}' not loaded; its hammer slot is not filled.");
                        continue;
                    }
                    int found = CollectGroups(bp, groups);
                    if (found == 0)
                        Logger.LogWarning($"[ACT] {GroupName}: blueprint '{bpUid}' has no element naming the group; nothing to fill there.");
                }
                if (groups.Count == 0)
                {
                    Logger.LogWarning($"[ACT] {GroupName}: no loaded blueprint carries the group; the pan and stove hammer slots accept nothing.");
                    return;
                }

                var tools = new List<object>(ToolUids.Length);
                foreach (var toolUid in ToolUids)
                {
                    if (byUid.TryGetValue(toolUid, out var tool)) tools.Add(tool);
                    else Logger.LogWarning($"[ACT] {GroupName}: vanilla tool '{toolUid}' not found; the slot will not list it.");
                }

                foreach (var group in groups)
                {
                    var groupType = group.GetType();
                    var includedField = groupType.GetField("IncludedCards", Flags);
                    if (includedField == null)
                    {
                        Logger.LogWarning($"[ACT] {GroupName}: {groupType.Name}.IncludedCards not found; the slot accepts nothing.");
                        continue;
                    }
                    var included = includedField.GetValue(group) as IList;
                    if (included == null)
                    {
                        var cardDataType = includedField.FieldType.IsGenericType ? includedField.FieldType.GetGenericArguments()[0] : null;
                        if (cardDataType == null)
                        {
                            Logger.LogWarning($"[ACT] {GroupName}: IncludedCards is null and its element type is unknown; the slot accepts nothing.");
                            continue;
                        }
                        included = Activator.CreateInstance(typeof(List<>).MakeGenericType(cardDataType)) as IList;
                        includedField.SetValue(group, included);
                    }
                    foreach (var tool in tools)
                        if (!included.Contains(tool)) included.Add(tool);

                    var tabNameField = groupType.GetField("TabName", Flags);
                    var label = tabNameField == null ? null
                        : LocalizedStringBuilder.CreateLike(tabNameField.GetValue(group), LabelKey, LabelText);
                    if (label != null) tabNameField.SetValue(group, label);
                    else Logger.LogWarning($"[ACT] {GroupName}: could not set the slot's label; it shows the raw group name.");
                }

                Logger.LogDebug($"[ACT] {GroupName}: {tools.Count} tools across {groups.Count} group instance(s).");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[ACT] {GroupName} fill failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Adds every distinct RequiredTabGroup named GroupName in the blueprint's stages to
        // `groups` and returns how many elements named it.
        private static int CollectGroups(object blueprint, List<UnityEngine.Object> groups)
        {
            int found = 0;
            var stages = blueprint.GetType().GetField("BlueprintStages", Flags)?.GetValue(blueprint) as Array;
            if (stages == null) return 0;
            foreach (var stage in stages)
            {
                if (stage == null) continue;
                var elements = stage.GetType().GetField("RequiredElements", Flags)?.GetValue(stage) as Array;
                if (elements == null) continue;
                foreach (var element in elements)
                {
                    if (element == null) continue;
                    var group = element.GetType().GetField("RequiredTabGroup", Flags)?.GetValue(element) as UnityEngine.Object;
                    if (group == null || group.name != GroupName) continue;
                    found++;
                    if (!groups.Contains(group)) groups.Add(group);
                }
            }
            return found;
        }
    }
}
