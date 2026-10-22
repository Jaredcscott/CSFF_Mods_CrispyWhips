using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Clears a hardcoded OverrideEnvironment on mod perks so perk items
/// respect the player's actual starting location choice.
///
/// Previously every mod (H&amp;F, ACT, WDI) had its own copy of this logic. Its target was a
/// starting-ITEM perk carrying an environment it did not mean to set (a vanilla env left in a
/// perk copied from a vanilla template): GameManager.GetStartingEnv takes the first perk's
/// OverrideEnvironment, so such a perk would drag the run to that board whatever start the
/// player picked.
///
/// An override that names a MOD-OWNED Environment card is kept (after 2.28.7): pointing a perk at a
/// board the mod itself ships can only be deliberate, and it is the whole point of a
/// start-location perk (Far Territory's Island Start, ftEnvLandfallStrand). Clearing it started
/// that run at the character's default board instead (harness row T2.1911).
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
        int kept = 0;
        foreach (var entry in allData)
        {
            if (!(entry is CharacterPerk perk) || string.IsNullOrEmpty(perk.UniqueID))
                continue;

            if (!modUniqueIds.Contains(perk.UniqueID))
                continue;

            var current = overrideEnvField.GetValue(perk) as CardData;
            if (current == null) continue;

            if (!ShouldClearOverride(current.UniqueID, current.CardType == CardTypes.Environment, modUniqueIds))
            {
                kept++;
                Log.Debug($"PerkRelocationService: kept OverrideEnvironment {current.UniqueID} on {perk.UniqueID} (a mod-owned environment)");
                continue;
            }

            overrideEnvField.SetValue(perk, null);
            cleared++;
        }

        if (cleared > 0 || kept > 0)
            Log.Debug($"PerkRelocationService: cleared OverrideEnvironment on {cleared} mod perks, kept {kept} that name a mod-owned environment");
    }

    /// <summary>
    /// True when a mod perk's resolved OverrideEnvironment should be cleared: every target except
    /// an Environment card that a framework-loaded mod owns. Takes plain values so the decision is
    /// testable outside the game (Development_Tools/Tests/Framework-PerkOverrideEnvironment.Tests.ps1).
    /// </summary>
    internal static bool ShouldClearOverride(string overrideUid, bool overrideIsEnvironment, ICollection<string> modUniqueIds)
    {
        if (string.IsNullOrEmpty(overrideUid) || !overrideIsEnvironment || modUniqueIds == null)
            return true;
        return !modUniqueIds.Contains(overrideUid);
    }
}
