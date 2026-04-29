using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Clears hardcoded OverrideEnvironment on all mod perks so perk items
/// respect the player's actual starting location choice.
///
/// Previously every mod (H&amp;F, ACT, WDI) had its own copy of this logic.
/// </summary>
internal static class PerkRelocationService
{
    public static void ClearOverrideEnvironments(IEnumerable allData, List<ModManifest> mods)
    {
        var modUniqueIds = JsonDataLoader.AllModUniqueIds;
        if (modUniqueIds.Count == 0) return;

        var overrideEnvField = AccessTools.Field(typeof(CharacterPerk), "OverrideEnvironment");
        if (overrideEnvField == null) return;

        int cleared = 0;
        foreach (var entry in allData)
        {
            if (!(entry is CharacterPerk perk) || string.IsNullOrEmpty(perk.UniqueID))
                continue;

            if (!modUniqueIds.Contains(perk.UniqueID))
                continue;

            var current = overrideEnvField.GetValue(perk) as CardData;
            if (current == null) continue;

            overrideEnvField.SetValue(perk, null);
            cleared++;
        }

        if (cleared > 0)
            Log.Debug($"PerkRelocationService: cleared OverrideEnvironment on {cleared} mod perks");
    }
}
