using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

internal static class PerkInjector
{
    private const string SituationalPerkGroupGuid = "72120cda8e1cef540b1b25118dd7edaa";

    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        // Build map: perk UniqueID → target PerkGroup UniqueID
        var perkToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in mods)
        {
            var perkDir = Path.Combine(mod.DirectoryPath, "CharacterPerk");
            if (!Directory.Exists(perkDir)) continue;

            foreach (var file in Directory.GetFiles(perkDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var uid = PathUtil.QuickExtractString(json, "UniqueID");
                    if (string.IsNullOrEmpty(uid)) continue;

                    var groupId = PathUtil.QuickExtractString(json, "CharacterPerkPerkGroup");
                    perkToGroup[uid] = string.IsNullOrEmpty(groupId) ? SituationalPerkGroupGuid : groupId;
                }
                catch { }
            }
        }

        if (perkToGroup.Count == 0) return;

        // Build lookups
        var perks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in allData)
        {
            if (item == null) continue;
            if (item is not UniqueIDScriptable uidObj || string.IsNullOrEmpty(uidObj.UniqueID)) continue;

            if (item is CharacterPerk)
                perks[uidObj.UniqueID] = item;

            // PerkTabGroup check via type name (we can't reference the type directly)
            var typeName = item.GetType().Name;
            if (typeName == "PerkTabGroup" || typeName == "PerkGroup")
                groups[uidObj.UniqueID] = item;
        }

        // Cache ContainedPerks field per type (PerkTabGroup and PerkGroup have different FieldInfo)
        var containedPerksFieldCache = new Dictionary<Type, System.Reflection.FieldInfo>();

        System.Reflection.FieldInfo GetContainedPerks(object obj)
        {
            var type = obj.GetType();
            if (!containedPerksFieldCache.TryGetValue(type, out var field))
            {
                field = type.GetField("ContainedPerks",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                containedPerksFieldCache[type] = field;
            }
            return field;
        }

        int injected = 0;
        int relocated = 0;

        foreach (var kvp in perkToGroup)
        {
            if (!perks.TryGetValue(kvp.Key, out var perk)) continue;

            var targetGroupId = kvp.Value;
            if (!groups.TryGetValue(targetGroupId, out var group))
            {
                // Fallback to Situational
                if (!groups.TryGetValue(SituationalPerkGroupGuid, out group))
                    continue;
            }

            var containedPerksField = GetContainedPerks(group);
            if (containedPerksField == null) continue;

            // Add to target group
            var containedPerks = containedPerksField.GetValue(group) as IList;
            if (containedPerks != null && !containedPerks.Contains(perk))
            {
                containedPerks.Add(perk);
                injected++;
                if (group is UniqueIDScriptable uidGroup)
                    Loading.FrameworkDirtyTracker.MarkDirty(uidGroup);
            }

            // Remove from all OTHER groups (game may have auto-placed perks in wrong tabs)
            foreach (var otherGroup in groups.Values)
            {
                if (otherGroup == group) continue;
                var otherField = GetContainedPerks(otherGroup);
                if (otherField == null) continue;
                var otherPerks = otherField.GetValue(otherGroup) as IList;
                if (otherPerks != null && otherPerks.Contains(perk))
                {
                    otherPerks.Remove(perk);
                    relocated++;
                    if (otherGroup is UniqueIDScriptable uidOther)
                        Loading.FrameworkDirtyTracker.MarkDirty(uidOther);
                }
            }
        }

        Log.Info($"PerkInjector: injected {injected} perks into groups" +
                 (relocated > 0 ? $", relocated {relocated} from wrong tabs" : ""));
    }
}
