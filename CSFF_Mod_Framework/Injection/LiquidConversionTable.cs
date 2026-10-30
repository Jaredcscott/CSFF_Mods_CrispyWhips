using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>One accepted solid-to-liquid pair from a mod's <c>LiquidConversions.json</c>.</summary>
internal sealed class LiquidConversionRow
{
    public string Mod;
    public string Solid;
    public string Liquid;
    public float Quantity;

    public override string ToString() => $"{Solid} -> {Liquid} x{Quantity}";
}

/// <summary>A row that was left out, and why. <see cref="UnresolvedUid"/> is set only when a UID named no card.</summary>
internal sealed class LiquidConversionIssue
{
    public string Mod;
    public string UnresolvedUid;
    public string Message;

    public override string ToString() => Message;
}

/// <summary>
/// The merged solid-to-liquid table (framework 2.29.0, fleet plan 4.3, credit Chiwei). Every mod may ship
/// <c>LiquidConversions.json</c> beside <c>ModInfo.json</c>:
/// <code>[ { "Solid": "&lt;CardData UID&gt;", "Liquid": "&lt;CT9 CardData UID&gt;", "Quantity": 5 } ]</code>
/// <see cref="Build"/> validates each row and merges the mods in load order, keyed by BOTH the solid and the
/// liquid UID, as vanilla's <c>LiquidTransferRules.FillDicts</c> keys its own table: a card can be in one pair
/// only. The first mod to name a card keeps it; a later row naming the same card is left out with an issue
/// naming both mods. Finding: <c>Documentation/Research/Fleet/APE_Port/W0.3_powder_to_liquid.md</c>.
///
/// Pure (BCL types only, the card lookup is a delegate), so
/// <c>Development_Tools/Tests/Framework-LiquidConversions.Tests.ps1</c> runs it outside the game.
/// <see cref="LiquidConversionInjector"/> is the game side.
/// </summary>
internal sealed class LiquidConversionTable
{
    /// <summary>The file a mod ships beside <c>ModInfo.json</c>.</summary>
    internal const string FileName = "LiquidConversions.json";

    /// <summary>What <c>cardTypeOf</c> returns for a UID that names no card.</summary>
    internal const int Unresolved = -1;

    /// <summary><c>CardTypes.Liquid</c>.</summary>
    internal const int LiquidCardType = 9;

    public readonly List<LiquidConversionRow> Rows = new();
    public readonly List<LiquidConversionIssue> Issues = new();
    private readonly Dictionary<string, LiquidConversionRow> _byUid = new(StringComparer.Ordinal);

    /// <summary>The pair a card is in, looked up by its solid OR its liquid UID; null when it is in none.</summary>
    internal LiquidConversionRow Find(string uid)
        => !string.IsNullOrEmpty(uid) && _byUid.TryGetValue(uid, out var row) ? row : null;

    internal int Count => Rows.Count;

    /// <summary>
    /// Validates and merges the files of several mods, given in load order (<paramref name="modNames"/>[i]
    /// shipped <paramref name="jsonTexts"/>[i]). <paramref name="cardTypeOf"/> returns a UID's CardType, or
    /// <see cref="Unresolved"/> when no card has that UID. A row is left out, with one issue, when: it is not an
    /// object; Solid or Liquid is missing or empty; Quantity is missing, not a number, NaN, infinite or not above
    /// 0 (the take-out drop divides by it: <c>CardsDropCollection</c> returns floor(liquid / Quantity) solids);
    /// Solid equals Liquid; either UID names no card; the Liquid is not a liquid (CardType 9) or the Solid is one;
    /// or either card is already in an earlier pair.
    /// </summary>
    internal static LiquidConversionTable Build(IList<string> modNames, IList<string> jsonTexts, Func<string, int> cardTypeOf)
    {
        var table = new LiquidConversionTable();
        if (modNames == null || jsonTexts == null) return table;
        int n = Math.Min(modNames.Count, jsonTexts.Count);
        for (int i = 0; i < n; i++)
            table.AddFile(modNames[i] ?? "", jsonTexts[i], cardTypeOf);
        return table;
    }

    private void AddFile(string mod, string json, Func<string, int> cardTypeOf)
    {
        object root;
        try { root = MiniJson.Parse(json ?? ""); }
        catch (Exception ex)
        {
            Issue(mod, null, $"[{mod}] {FileName} is not valid JSON ({ex.GetType().Name}: {ex.Message}); the whole file is skipped.");
            return;
        }
        if (root is not List<object> list)
        {
            Issue(mod, null, $"[{mod}] {FileName} must be a JSON array of {{ \"Solid\", \"Liquid\", \"Quantity\" }} objects; the whole file is skipped.");
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            var where = $"[{mod}] {FileName} row {i}";
            if (list[i] is not Dictionary<string, object> obj)
            {
                Issue(mod, null, $"{where} is not an object; skipped.");
                continue;
            }

            var solid = obj.TryGetValue("Solid", out var s) ? s as string : null;
            var liquid = obj.TryGetValue("Liquid", out var l) ? l as string : null;
            if (string.IsNullOrEmpty(solid) || string.IsNullOrEmpty(liquid))
            {
                Issue(mod, null, $"{where} needs both \"Solid\" and \"Liquid\" UIDs; skipped.");
                continue;
            }

            if (!TryReadQuantity(obj, out var quantity))
            {
                Issue(mod, null, $"{where} ({solid} -> {liquid}): \"Quantity\" must be a number above 0 (liquid units one solid card becomes); skipped.");
                continue;
            }

            if (string.Equals(solid, liquid, StringComparison.Ordinal))
            {
                Issue(mod, null, $"{where}: Solid and Liquid are the same card ('{solid}'); skipped.");
                continue;
            }

            int solidType = cardTypeOf != null ? cardTypeOf(solid) : Unresolved;
            int liquidType = cardTypeOf != null ? cardTypeOf(liquid) : Unresolved;
            if (solidType == Unresolved)
            {
                Issue(mod, solid, $"{where}: Solid '{solid}' names no loaded card; skipped.");
                continue;
            }
            if (liquidType == Unresolved)
            {
                Issue(mod, liquid, $"{where}: Liquid '{liquid}' names no loaded card; skipped.");
                continue;
            }
            if (liquidType != LiquidCardType)
            {
                Issue(mod, null, $"{where}: Liquid '{liquid}' is CardType {liquidType}, not a liquid (9); skipped.");
                continue;
            }
            if (solidType == LiquidCardType)
            {
                Issue(mod, null, $"{where}: Solid '{solid}' is itself a liquid (CardType 9); skipped.");
                continue;
            }

            var taken = Find(solid) ?? Find(liquid);
            if (taken != null)
            {
                var card = Find(solid) != null ? solid : liquid;
                Issue(mod, null, $"{where} ({solid} -> {liquid}): '{card}' is already in the pair {taken} from [{taken.Mod}], "
                    + $"which loads first and keeps it; a card can be in one pair only, so [{mod}]'s row is skipped.");
                continue;
            }

            var row = new LiquidConversionRow { Mod = mod, Solid = solid, Liquid = liquid, Quantity = quantity };
            Rows.Add(row);
            _byUid[solid] = row;
            _byUid[liquid] = row;
        }
    }

    /// <summary>
    /// Takes out every pair whose solid or liquid <paramref name="isTaken"/> says is already spoken for (the
    /// vanilla table, read once the game has one), and returns the pairs taken out, in order.
    /// </summary>
    internal List<LiquidConversionRow> RemoveWhere(Func<string, bool> isTaken)
    {
        var removed = new List<LiquidConversionRow>();
        if (isTaken == null) return removed;
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            var row = Rows[i];
            if (!isTaken(row.Solid) && !isTaken(row.Liquid)) continue;
            Rows.RemoveAt(i);
            _byUid.Remove(row.Solid);
            _byUid.Remove(row.Liquid);
            removed.Insert(0, row);
        }
        return removed;
    }

    private static bool TryReadQuantity(Dictionary<string, object> obj, out float quantity)
    {
        // MiniJson reads every JSON number as a double; a quoted "5" is a string and is refused.
        quantity = 0f;
        if (!obj.TryGetValue("Quantity", out var raw) || raw is not double d) return false;
        if (double.IsNaN(d) || double.IsInfinity(d) || d <= 0 || d > float.MaxValue) return false;
        quantity = (float)d;
        return true;
    }

    private void Issue(string mod, string unresolvedUid, string message)
        => Issues.Add(new LiquidConversionIssue { Mod = mod, UnresolvedUid = unresolvedUid, Message = message });
}
