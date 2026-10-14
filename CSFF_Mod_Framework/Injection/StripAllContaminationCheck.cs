namespace CSFFModFramework.Injection;

/// <summary>
/// The run-start "Contamination check" for a <c>StripAllInheritedDrops</c> clone node's SAVED board
/// (<see cref="WorldMapInjector"/>'s <c>PreCreateCloneEnvSaveData</c>), as one pure decision so
/// <c>Development_Tools/Tests/Framework-StripAllContaminationCheck.Tests.ps1</c> can run it on
/// fixture node data. BCL types only, on purpose: the gate compiles this file on its own.
///
/// <para><strong>What it is for:</strong> clearing a saved cave board that holds copies of DECLARED
/// drops it should not: extra vein copies left by the old session-scoped watch-spawn path, or a
/// card another cave declares (iron veins in the tin cave). Either way the board is wiped and
/// re-seeded from <c>DefaultEnvCardDrops</c>.</para>
///
/// <para><strong>What it must never do (plan section 30, framework 2.28.0):</strong> count cards
/// that are not declared drops. Until 2.28.0 the check compared the saved board's whole card count
/// (<c>AllCardsCount(false)</c>) with <c>ExtraDropUIDs.Count + 1 + SealableGates slack</c>, so one
/// ore a player left in Advanced Copper Tools' Iron Vein Cave (no head-room at all), or the Rock
/// Quarry's own <c>StoneHeavy</c>, wiped the whole board at the next run start. A card that is not
/// declared anywhere is now never counted and never causes a wipe.</para>
/// </summary>
internal static class StripAllContaminationCheck
{
    /// <summary>
    /// Suffix <c>CardCloneService.GetEnvLocalVariant</c> gives the AlwaysUpdate=false clone it drops
    /// in place of an AlwaysUpdate=true ExtraDrop, so the saved board names that clone, not the
    /// declared UID.
    /// </summary>
    internal const string EnvLocalSuffix = "__envlocal";

    /// <summary>The declared UID a saved card stands for: its own UID, less the env-local suffix.</summary>
    internal static string Canonical(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return uid;
        return uid.EndsWith(EnvLocalSuffix, StringComparison.OrdinalIgnoreCase)
            ? uid.Substring(0, uid.Length - EnvLocalSuffix.Length)
            : uid;
    }

    /// <summary>
    /// What this env's saved board may legitimately hold, as UID to most copies: one per
    /// <c>ExtraDropUIDs</c> entry (a UID listed three times may stand three times), its CT8 location
    /// card once, and one per <c>SealableGates</c> entry, on ANY node, that seeds its challenge card
    /// on this env (the slack the old count check added for uncleared walls).
    /// </summary>
    internal static Dictionary<string, int> DeclaredHere(string envUid, string locationUid,
        IEnumerable<string> extraDropUids, IEnumerable<(string ChallengeUid, IEnumerable<string> SeedOnEnvUids)> gates)
    {
        var max = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        void Add(string uid)
        {
            uid = Canonical(uid);
            if (string.IsNullOrEmpty(uid)) return;
            max[uid] = max.TryGetValue(uid, out var n) ? n + 1 : 1;
        }

        if (extraDropUids != null)
            foreach (var uid in extraDropUids) Add(uid);
        Add(locationUid);
        if (gates != null && !string.IsNullOrEmpty(envUid))
            foreach (var g in gates)
                if (g.SeedOnEnvUids != null && g.SeedOnEnvUids.Contains(envUid, StringComparer.OrdinalIgnoreCase))
                    Add(g.ChallengeUid);
        return max;
    }

    /// <summary>
    /// The UIDs some OTHER cave declares: every <c>ExtraDropUIDs</c> entry of the other StripAll
    /// nodes and every <c>SealableGates</c> challenge card. It may hold cards this cave declares too
    /// (Forest Lore's <c>fl_squeeze</c> is declared by both the Sunken Lake and the Ossuary; a wall
    /// can seed on two caves): <see cref="WipeReason"/> counts a card this cave declares as its own
    /// first, so a shared card is never cross-cave. The caller leaves out any card a player can
    /// carry, so a player's item never reads as another cave's drop.
    /// </summary>
    internal static HashSet<string> DeclaredElsewhere(IEnumerable<string> otherCavesUids)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (otherCavesUids == null) return set;
        foreach (var raw in otherCavesUids)
        {
            var uid = Canonical(raw);
            if (!string.IsNullOrEmpty(uid)) set.Add(uid);
        }
        return set;
    }

    /// <summary>
    /// The decision. <paramref name="savedBoardUids"/> is every card on the saved board (its regular
    /// cards and its top-level containers), by UID. Returns null to keep the board, else why it is
    /// wiped: a declared UID standing more times than declared (a duplicate), or a UID another cave
    /// declares and this one does not (cross-cave). Every other card, a player's ore or a dropped
    /// <c>StoneHeavy</c>, is not counted.
    /// </summary>
    internal static string WipeReason(IEnumerable<string> savedBoardUids,
        IReadOnlyDictionary<string, int> declaredHere, ICollection<string> declaredElsewhere)
    {
        if (savedBoardUids == null) return null;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string firstForeign = null;
        foreach (var raw in savedBoardUids)
        {
            var uid = Canonical(raw);
            if (string.IsNullOrEmpty(uid)) continue;
            if (declaredHere != null && declaredHere.ContainsKey(uid))
                seen[uid] = seen.TryGetValue(uid, out var n) ? n + 1 : 1;
            else if (firstForeign == null && declaredElsewhere != null && declaredElsewhere.Contains(uid))
                firstForeign = uid;
        }

        foreach (var kv in seen)
            if (kv.Value > declaredHere[kv.Key])
                return $"duplicate declared drop '{kv.Key}' ({kv.Value} on the saved board, {declaredHere[kv.Key]} declared)";
        if (firstForeign != null)
            return $"'{firstForeign}' is declared for another cave (cross-cave contamination)";
        return null;
    }
}
