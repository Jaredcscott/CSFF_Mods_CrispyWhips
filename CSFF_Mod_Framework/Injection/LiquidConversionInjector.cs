using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Declarative solid-to-liquid conversions (framework 2.29.0, fleet plan 4.3, credit Chiwei): a mod's powder
/// goes into a jar, pouch or barrel and comes back out the way vanilla salt and flour do. Each mod may ship
/// <c>LiquidConversions.json</c> beside <c>ModInfo.json</c>; <see cref="LiquidConversionTable"/> validates and
/// merges them.
///
/// <para><b>Why a postfix, not an append to vanilla's array.</b> Vanilla keeps its 26 pairs in
/// <c>GameManager.LiquidTransfers.SolidToLiquid</c>, an Inspector-wired asset no JSON reaches. Its private
/// dictionary fills lazily only while empty, and <c>FillDicts</c> throws on a duplicate and then leaves a
/// half-filled dictionary that silently breaks every later lookup. Every reader goes through the static
/// <c>LiquidTransferRules.LiquidConversion(CardData)</c> (two callers: <c>InGameCardBase.SetModel</c>, which
/// fills the card's <c>SolidToLiquidInfo</c>, and <c>CardData.EmptyLiquidAction</c>, the "take it back out"
/// drop), so one postfix serves every consumer. It returns at once when vanilla already answered, so vanilla
/// always wins, and otherwise answers only for a card in the merged table. Finding:
/// <c>Documentation/Research/Fleet/APE_Port/W0.3_powder_to_liquid.md</c>.</para>
///
/// <para><b>Timing.</b> <see cref="LoadAll"/> runs in the framework's load pass after WarpResolver
/// (root CLAUDE.md "Post-WarpResolver SO References"), so every UID resolves to the CardData instance the game
/// uses, and builds one cached <see cref="SolidToLiquidConversion"/> per pair. It runs again, from scratch, on
/// every load. Vanilla's own table can only be read once a run has a GameManager, so the pairs that reuse a
/// vanilla card are taken out (with a Warn) on the first lookup that hits the table, before it answers.</para>
///
/// <para><b>A fresh powder's pour quantity.</b> Vanilla's <c>InGameCardBase.Init</c> sets
/// <c>CurrentLiquidQuantity = SolidToLiquidInfo.LiquidQuantity</c> for a solid with a liquid version, after
/// <c>SetModel</c> has asked this postfix, so a spawned powder pours its Quantity with no more code. An
/// in-place model swap skips <c>Init</c>; <see cref="CardUtil.TransformCardInPlace"/> sets it itself.</para>
/// </summary>
internal static class LiquidConversionInjector
{
    // Keyed by the resolved card's UniqueID (solid AND liquid), so a UID written as an alias still matches.
    private static readonly Dictionary<string, SolidToLiquidConversion> _byUid = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> _modByUid = new(StringComparer.Ordinal);
    private static LiquidConversionTable _table;
    private static bool _vanillaChecked;
    private static bool _patchApplied;

    internal static int PairCount => _byUid.Count / 2;

    /// <summary>
    /// Reads every mod's <c>LiquidConversions.json</c> in load order, validates and merges them, and caches one
    /// conversion per accepted pair. Call after WarpResolver.
    /// </summary>
    public static void LoadAll(List<ModManifest> mods)
    {
        _byUid.Clear();
        _modByUid.Clear();
        _table = null;
        _vanillaChecked = false;

        var names = new List<string>();
        var texts = new List<string>();
        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, LiquidConversionTable.FileName);
            if (!File.Exists(path)) continue;
            try
            {
                texts.Add(File.ReadAllText(path));
                names.Add(mod.Name);
            }
            catch (Exception ex)
            {
                Log.Warn($"[LiquidConversions] [{mod.Name}] could not read {LiquidConversionTable.FileName}: {Log.ExceptionText(ex)}");
            }
        }
        if (names.Count == 0) return;

        var table = _table = LiquidConversionTable.Build(names, texts, CardTypeOf);

        int skipped = 0, unresolved = 0;
        var missed = new List<string>();
        foreach (var issue in table.Issues)
        {
            if (issue.UnresolvedUid != null)
            {
                if (UnresolvedUid.ExpectedPresent(issue.UnresolvedUid))
                    Log.Warn($"[LiquidConversions] {issue.Message} (UID '{issue.UnresolvedUid}' does not resolve)");
                else
                    Log.Debug($"[LiquidConversions] {issue.Message} (UID '{issue.UnresolvedUid}' does not resolve; a partner mod not installed?)");
                unresolved++;
                missed.Add(issue.UnresolvedUid);
                continue;
            }
            Log.Warn($"[LiquidConversions] {issue.Message}");
            skipped++;
        }

        foreach (var row in table.Rows)
        {
            var solid = GameRegistry.GetByUid(row.Solid) as CardData;
            var liquid = GameRegistry.GetByUid(row.Liquid) as CardData;
            if (solid == null || liquid == null) { skipped++; continue; }
            if (_byUid.ContainsKey(solid.UniqueID) || _byUid.ContainsKey(liquid.UniqueID))
            {
                // Two rows that wrote different UIDs (one an alias) for the same card.
                Log.Warn($"[LiquidConversions] [{row.Mod}] {row}: '{solid.UniqueID}' or '{liquid.UniqueID}' is already paired by "
                    + $"[{ModOf(solid.UniqueID) ?? ModOf(liquid.UniqueID)}] under another UID; skipped.");
                skipped++;
                continue;
            }
            var conversion = new SolidToLiquidConversion { SolidCard = solid, ToLiquidCard = liquid, LiquidQuantity = row.Quantity };
            _byUid[solid.UniqueID] = conversion;
            _byUid[liquid.UniqueID] = conversion;
            _modByUid[solid.UniqueID] = row.Mod;
            _modByUid[liquid.UniqueID] = row.Mod;
            Log.Debug($"[LiquidConversions] [{row.Mod}] {row}");
        }

        if (skipped > 0 || unresolved > 0)
            Log.Info($"[LiquidConversions] {PairCount} solid-to-liquid pair(s) loaded ({skipped} skipped, {unresolved} unresolved"
                + (unresolved > 0 ? $": {UnresolvedUid.Sample(missed)}" : "") + ").");
        else
            Log.Info($"[LiquidConversions] {PairCount} solid-to-liquid pair(s) loaded.");
    }

    /// <summary>Installs the postfix once per process, and only when some mod declared a pair.</summary>
    public static void ApplyPatch(Harmony harmony)
    {
        if (_patchApplied || _byUid.Count == 0) return;
        _patchApplied = true;
        try
        {
            var target = AccessTools.Method(typeof(LiquidTransferRules), nameof(LiquidTransferRules.LiquidConversion), new[] { typeof(CardData) });
            if (target == null)
            {
                Log.Warn("[LiquidConversions] LiquidTransferRules.LiquidConversion(CardData) not found (a game update?); mod powders will not pour.");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(LiquidConversionInjector), nameof(LiquidConversion_Postfix)));
            Log.Debug("[LiquidConversions] patched LiquidTransferRules.LiquidConversion.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[LiquidConversions] failed to patch LiquidTransferRules.LiquidConversion; mod powders will not pour: {Log.ExceptionText(ex)}");
        }
    }

    // Runs for every SetModel (every card the game builds), so the miss path is a null test, a count test and
    // one dictionary lookup.
    private static void LiquidConversion_Postfix(CardData _ForCard, ref SolidToLiquidConversion __result)
    {
        if (__result != null || _byUid.Count == 0 || (object)_ForCard == null) return;
        try
        {
            var uid = _ForCard.UniqueID;
            if (string.IsNullOrEmpty(uid) || !_byUid.ContainsKey(uid)) return;

            // Vanilla answers nothing without a run (no GameManager or no LiquidTransfers); do the same.
            var gm = MBSingleton<GameManager>.Instance;
            if (!gm || !gm.LiquidTransfers) return;
            if (!_vanillaChecked) DropVanillaOverlaps(gm.LiquidTransfers);

            if (_byUid.TryGetValue(uid, out var conversion)) __result = conversion;
        }
        catch (Exception ex)
        {
            Log.Warn($"[LiquidConversions] lookup for '{_ForCard?.UniqueID}' threw: {Log.ExceptionText(ex)}");
        }
    }

    // A card in vanilla's table already has its pair, and vanilla's model allows one pair per card, so a mod
    // pair that names one is taken out whole (vanilla would win for that card anyway, leaving the other half of
    // the mod pair pointing at it). Once per load.
    private static void DropVanillaOverlaps(LiquidTransferRules rules)
    {
        _vanillaChecked = true;
        var vanilla = new HashSet<string>(StringComparer.Ordinal);
        if (rules.SolidToLiquid != null)
            foreach (var c in rules.SolidToLiquid)
            {
                if (c == null) continue;
                if (c.SolidCard) vanilla.Add(c.SolidCard.UniqueID);
                if (c.ToLiquidCard) vanilla.Add(c.ToLiquidCard.UniqueID);
            }

        // The table holds the UIDs as written (an alias among them); compare the cards they resolve to.
        var removed = _table?.RemoveWhere(uid => vanilla.Contains(ResolvedUid(uid))) ?? new List<LiquidConversionRow>();
        foreach (var row in removed)
        {
            string solid = ResolvedUid(row.Solid), liquid = ResolvedUid(row.Liquid);
            // Only this row's own cached pair: a row LoadAll already skipped (an alias of a taken card) has none.
            if (!_byUid.TryGetValue(solid, out var conv) || conv.ToLiquidCard == null || conv.ToLiquidCard.UniqueID != liquid) continue;
            Log.Warn($"[LiquidConversions] [{row.Mod}] {row}: a card in this pair is already in the base game's own "
                + "solid-to-liquid table, which keeps it; skipped.");
            _byUid.Remove(solid);
            _byUid.Remove(liquid);
        }
        Log.Debug($"[LiquidConversions] checked against {rules.SolidToLiquid?.Length ?? 0} vanilla pair(s): {removed.Count} mod pair(s) taken out, {PairCount} left.");
    }

    private static string ResolvedUid(string uid) => GameRegistry.GetByUid(uid) is CardData card ? card.UniqueID : uid;

    private static int CardTypeOf(string uid)
        => GameRegistry.GetByUid(uid) is CardData card ? (int)card.CardType : LiquidConversionTable.Unresolved;

    private static string ModOf(string uid) => uid != null && _modByUid.TryGetValue(uid, out var mod) ? mod : null;
}
