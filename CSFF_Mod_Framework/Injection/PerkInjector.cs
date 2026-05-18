using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

internal static class PerkInjector
{
    private const string SituationalPerkGroupGuid = "72120cda8e1cef540b1b25118dd7edaa";

    public static void InjectAll(IEnumerable allData, List<ModManifest> mods)
    {
        // Build lookups from already-loaded game data
        var perks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // Cache type refs once to avoid string allocation + comparison per allData item
        var perkTabGroupType = ReflectionCache.FindType("PerkTabGroup");
        var perkGroupType    = ReflectionCache.FindType("PerkGroup");

        foreach (var item in allData)
        {
            if (item == null) continue;
            if (item is not UniqueIDScriptable uidObj || string.IsNullOrEmpty(uidObj.UniqueID)) continue;

            if (item is CharacterPerk)
                perks[uidObj.UniqueID] = item;

            var itemType = item.GetType();
            if ((perkTabGroupType != null && itemType == perkTabGroupType) ||
                (perkGroupType    != null && itemType == perkGroupType))
                groups[uidObj.UniqueID] = item;
        }

        if (perks.Count == 0) return;

        // Build perk → group mapping from the JSON cache populated by JsonDataLoader.
        // Iterates only mod-loaded UIDs — vanilla perks are not processed.
        var perkToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uid in Loading.JsonDataLoader.AllModUniqueIds)
        {
            if (!perks.ContainsKey(uid)) continue; // not a CharacterPerk

            string groupId = null;
            if (Loading.JsonDataLoader.ParsedJsonByUniqueId.TryGetValue(uid, out var parsed)
                && parsed.TryGetValue("CharacterPerkPerkGroup", out var g))
                groupId = g as string;

            perkToGroup[uid] = string.IsNullOrEmpty(groupId) ? SituationalPerkGroupGuid : groupId;
        }

        if (perkToGroup.Count == 0) return;

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

        // Build inverted map: perk UniqueID → group UniqueIDs that currently contain it.
        // Replaces the O(P×G) all-groups scan with a targeted lookup during removal.
        var perkInGroupGuids = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var grpKvp in groups)
        {
            var fi = GetContainedPerks(grpKvp.Value);
            if (fi?.GetValue(grpKvp.Value) is not IList containedList) continue;
            foreach (var p in containedList)
            {
                if (p is UniqueIDScriptable us && !string.IsNullOrEmpty(us.UniqueID))
                {
                    if (!perkInGroupGuids.TryGetValue(us.UniqueID, out var gl))
                        perkInGroupGuids[us.UniqueID] = gl = new List<string>();
                    gl.Add(grpKvp.Key);
                }
            }
        }

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

            // Remove from groups where this perk already appeared (inverted-map lookup)
            if (perkInGroupGuids.TryGetValue(kvp.Key, out var containingGroupGuids))
            {
                foreach (var otherGroupGuid in containingGroupGuids)
                {
                    if (otherGroupGuid.Equals(targetGroupId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!groups.TryGetValue(otherGroupGuid, out var otherGroup)) continue;
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
        }

        Log.Debug($"PerkInjector: injected {injected} perks into groups" +
                 (relocated > 0 ? $", relocated {relocated} from wrong tabs" : ""));
    }
}
