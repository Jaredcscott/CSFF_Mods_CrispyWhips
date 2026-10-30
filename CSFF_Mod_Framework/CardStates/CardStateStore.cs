using System.Text;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CSFFModFramework.CardStates;

/// <summary>
/// The live half of <see cref="Api.CardState"/>: the values of the run in progress, the sidecar file read at run start
/// and written after each save, and the carry through transforms and blueprints. Harmony wiring is in
/// <see cref="CardStatePatch"/>; the file format, id scan and merge are in <see cref="CardStateCodec"/>.
///
/// <para><b>The run.</b> State belongs to one <c>GameManager.CurrentSaveData</c> object, which the game builds afresh at
/// every run start (<c>GameManager</c> copies the loaded checkpoint, <c>.decomp/GameManager.cs</c> 2446). Any call that
/// finds a different object first drops every value and reads the sidecar of the file that run was loaded from,
/// so a reload, a main-menu round trip or another slot never sees the previous run's values, and nothing needs a
/// per-run event subscription to reset it.</para>
/// </summary>
internal static class CardStateStore
{
    private sealed class Policy
    {
        public bool Transform;
        public CardStateMerge Blueprint;
        public HashSet<string> OnlyInto;      // null: every result
        public string SeedStat;               // null: no seed
        public HashSet<string> SeedCards;
    }

    // Name -> policy. Process-wide: declarations are made once (plugin Awake) or idempotently.
    private static readonly Dictionary<string, Policy> _policies = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _transformNames = new(StringComparer.Ordinal);
    private static int _blueprintNames;

    // The run.
    private static object _runKey;
    private static int _runSlot = -1;
    private static string _runSaveFile;
    private static Dictionary<long, Dictionary<string, float>> _values = new();
    private static bool _sidecarMissing;       // the run's save file has a name but no sidecar
    private static bool _lostWarned;           // the one "values start empty" warning of this run

    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

    internal static bool AnyTransformCarry => _transformNames.Count > 0;
    internal static bool AnyBlueprintCarry => _blueprintNames > 0;

    // ── Run ──────────────────────────────────────────────────────────────────

    /// <summary>The live GameManager and its run's state, switching to a new run when the save data object changed.</summary>
    internal static bool EnsureRun(out GameManager gm)
    {
        gm = MBSingleton<GameManager>.Instance;
        if (!gm) { gm = null; return false; }
        var save = gm.CurrentSaveData;
        if (save == null) return false;
        if (ReferenceEquals(save, _runKey)) return true;
        StartRun(save);
        return true;
    }

    private static void StartRun(GameSaveDataByReference save)
    {
        _runKey = save;
        _values = new Dictionary<long, Dictionary<string, float>>();
        _sidecarMissing = false;
        _lostWarned = false;
        CardStateCarry.ResetRun();

        var gl = GameLoad.Instance;
        _runSlot = gl ? gl.CurrentGameDataIndex : -1;
        _runSaveFile = save.SaveFileName;
        var path = SidecarPath(_runSlot, _runSaveFile);
        if (path == null) return;   // a new game: nothing saved yet
        try
        {
            if (!File.Exists(path)) { _sidecarMissing = true; return; }
            var text = File.ReadAllText(path, Encoding.UTF8);
            var parsed = CardStateCodec.Parse(text, out int badLines, out string error);
            if (parsed == null)
            {
                Log.Warn($"CardState: {path} could not be read ({error}), so the card values of this save start empty.");
                _lostWarned = true;
                return;
            }
            if (badLines > 0)
                Log.Warn($"CardState: {path}: {badLines} unreadable line(s) skipped; those card values start empty.");
            _values = parsed;
            Log.Debug($"CardState: read {CountValues(_values)} value(s) on {_values.Count} card(s) from {path}.");
        }
        catch (Exception ex)
        {
            Log.Warn($"CardState: reading {path} failed, so the card values of this save start empty: {Log.ExceptionText(ex)}");
            _lostWarned = true;
            _values = new Dictionary<long, Dictionary<string, float>>();
        }
    }

    /// <summary>
    /// Run start (<c>OnGMInitialized</c>, every run): reads the sidecar now, and when it is missing while cards on
    /// the loaded board carry ids, says once that their values are gone. A save that never had a value (no id)
    /// starts empty in silence.
    /// </summary>
    internal static void OnRunStarted()
    {
        try
        {
            if (!EnsureRun(out var gm)) return;
            if (!_sidecarMissing || _lostWarned) return;
            int withId = 0;
            var all = gm.AllCards;
            if (all != null)
                foreach (var c in all)
                    if (c && ReadId(c) != 0) withId++;
            if (withId == 0) return;
            _lostWarned = true;
            Log.Warn($"CardState: {withId} card(s) in this save carry framework card values, but "
                   + $"{SidecarPath(_runSlot, _runSaveFile)} is missing, so their values start empty.");
        }
        catch (Exception ex) { WarnOnce("run-start", $"CardState: the run-start check threw: {Log.ExceptionText(ex)}"); }
    }

    // ── Card id ──────────────────────────────────────────────────────────────

    internal static long ReadId(InGameCardBase card)
    {
        var dc = card.DroppedCollections;
        if (dc == null) return 0;
        return dc.TryGetValue(CardStateCodec.IdKey, out var v) ? CardStateCodec.ToId(v.x, v.y) : 0;
    }

    internal static long EnsureId(InGameCardBase card)
    {
        long id = ReadId(card);
        if (id != 0) return id;
        do { id = CardStateCodec.NewId(); } while (_values.ContainsKey(id));
        WriteId(card, id);
        return id;
    }

    internal static void WriteId(InGameCardBase card, long id)
    {
        card.DroppedCollections ??= new Dictionary<string, Vector2Int>();
        card.DroppedCollections[CardStateCodec.IdKey] = new Vector2Int(CardStateCodec.IdLow(id), CardStateCodec.IdHigh(id));
    }

    private static InGameCardBase AsCard(object card, string member)
    {
        if (card is InGameCardBase igc && igc) return igc;
        if (card != null && card is not InGameCardBase)
            WarnOnce("not-card:" + member + ":" + card.GetType().FullName,
                $"CardState.{member}: '{card.GetType().FullName}' is not an in-game card (InGameCardBase); nothing is read or written.");
        return null;
    }

    // ── Values ───────────────────────────────────────────────────────────────

    internal static float Get(object card, string name, float fallback)
    {
        var c = AsCard(card, "Get");
        if (c == null || string.IsNullOrEmpty(name) || !EnsureRun(out _)) return fallback;
        long id = ReadId(c);
        return id != 0 && _values.TryGetValue(id, out var vals) && vals.TryGetValue(name, out var v) ? v : fallback;
    }

    internal static bool Has(object card, string name)
    {
        var c = AsCard(card, "Has");
        if (c == null || string.IsNullOrEmpty(name) || !EnsureRun(out _)) return false;
        long id = ReadId(c);
        return id != 0 && _values.TryGetValue(id, out var vals) && vals.ContainsKey(name);
    }

    internal static bool Set(object card, string name, float value)
    {
        var problem = CardStateCodec.NameProblem(name);
        if (problem != null) { WarnOnce("set-name:" + name, $"CardState.Set refused '{name}': {problem}."); return false; }
        if (float.IsNaN(value) || float.IsInfinity(value)) { WarnOnce("set-value:" + name, $"CardState.Set refused '{name}' = {value}: only finite numbers are kept."); return false; }
        var c = AsCard(card, "Set");
        if (c == null) return false;
        if (!EnsureRun(out _)) { WarnOnce("set-norun", "CardState.Set was called with no run in progress; nothing is kept."); return false; }
        SetRaw(EnsureId(c), name, value);
        return true;
    }

    internal static bool Remove(object card, string name)
    {
        var c = AsCard(card, "Remove");
        if (c == null || string.IsNullOrEmpty(name) || !EnsureRun(out _)) return false;
        long id = ReadId(c);
        if (id == 0 || !_values.TryGetValue(id, out var vals) || !vals.Remove(name)) return false;
        vals.Remove(CardStateCodec.CountName(name));
        if (vals.Count == 0) _values.Remove(id);
        return true;
    }

    internal static IReadOnlyList<string> Names(object card)
    {
        var c = AsCard(card, "Names");
        if (c == null || !EnsureRun(out _)) return Array.Empty<string>();
        long id = ReadId(c);
        if (id == 0 || !_values.TryGetValue(id, out var vals)) return Array.Empty<string>();
        var names = vals.Keys.Where(n => !CardStateCodec.IsCountName(n)).ToList();
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    internal static void SetRaw(long id, string name, float value)
    {
        if (!_values.TryGetValue(id, out var vals)) _values[id] = vals = new Dictionary<string, float>(StringComparer.Ordinal);
        vals[name] = value;
    }

    internal static Dictionary<string, float> ValuesOf(long id)
        => id != 0 && _values.TryGetValue(id, out var vals) ? vals : null;

    internal static void RemoveRaw(long id, string name)
    {
        if (!_values.TryGetValue(id, out var vals)) return;
        vals.Remove(name);
        if (vals.Count == 0) _values.Remove(id);
    }

    // ── Policies ─────────────────────────────────────────────────────────────

    internal static bool DeclareCarry(string name, bool throughTransform, CardStateMerge throughBlueprint, string[] onlyInto)
    {
        var problem = CardStateCodec.NameProblem(name);
        if (problem != null) { Log.Warn($"CardState.DeclareCarry refused '{name}': {problem}."); return false; }
        if (!_policies.TryGetValue(name, out var p)) _policies[name] = p = new Policy();
        if (p.Blueprint != CardStateMerge.None) _blueprintNames--;
        p.Transform = throughTransform;
        p.Blueprint = throughBlueprint;
        if (p.Blueprint != CardStateMerge.None) _blueprintNames++;
        var only = onlyInto?.Where(u => !string.IsNullOrEmpty(u)).ToList();
        p.OnlyInto = only != null && only.Count > 0 ? new HashSet<string>(only, StringComparer.Ordinal) : null;
        if (throughTransform) _transformNames.Add(name); else _transformNames.Remove(name);
        Log.Debug($"CardState: '{name}' carried through transforms: {throughTransform}, through blueprints: {throughBlueprint}"
                + (p.OnlyInto != null ? $" (only into {string.Join(", ", p.OnlyInto)})" : ""));
        return true;
    }

    internal static bool DeclareBlueprintSeed(string name, string stat, string[] cards)
    {
        var problem = CardStateCodec.NameProblem(name);
        if (problem != null) { Log.Warn($"CardState.DeclareBlueprintSeed refused '{name}': {problem}."); return false; }
        var uids = cards?.Where(u => !string.IsNullOrEmpty(u)).ToList();
        if (string.IsNullOrEmpty(stat) || uids == null || uids.Count == 0)
        {
            Log.Warn($"CardState.DeclareBlueprintSeed refused '{name}': it needs a durability stat name and at least one card UniqueID.");
            return false;
        }
        if (!_policies.TryGetValue(name, out var p)) _policies[name] = p = new Policy();
        p.SeedStat = stat;
        p.SeedCards = new HashSet<string>(uids, StringComparer.Ordinal);
        if (p.Blueprint == CardStateMerge.None)
            Log.Warn($"CardState.DeclareBlueprintSeed: '{name}' has no blueprint merge declared yet (DeclareCarry), so the seed does nothing until it does.");
        return true;
    }

    internal static bool CarriedThroughTransform(string name) => _transformNames.Contains(name);

    internal static ICollection<string> TransformNames => _transformNames;

    /// <summary>The names with a blueprint merge, with the merge, the result filter and the seed.</summary>
    internal static IEnumerable<(string name, CardStateMerge merge, HashSet<string> onlyInto, string seedStat, HashSet<string> seedCards)> BlueprintPolicies()
    {
        foreach (var kv in _policies)
            if (kv.Value.Blueprint != CardStateMerge.None)
                yield return (kv.Key, kv.Value.Blueprint, kv.Value.OnlyInto, kv.Value.SeedStat, kv.Value.SeedCards);
    }

    // ── Transform in place ───────────────────────────────────────────────────

    /// <summary>The id before <c>CardUtil.TransformCardInPlace</c> swaps the model (0 when none or not a card).</summary>
    internal static long IdBeforeTransform(object card)
    {
        try { return card is InGameCardBase c && c ? ReadId(c) : 0; }
        catch (Exception ex) { WarnOnce("tip-before:" + Log.CauseKey(ex), $"CardState: reading a card id before a transform threw: {Log.ExceptionText(ex)}"); return 0; }
    }

    /// <summary>
    /// After a successful <c>CardUtil.TransformCardInPlace</c>: the card keeps its id (stamped again if the reinit
    /// path cleared its collections) and loses every name not carried through transforms.
    /// </summary>
    internal static void AfterTransformInPlace(object card, long idBefore)
    {
        if (idBefore == 0) return;
        try
        {
            if (card is not InGameCardBase c || !c) return;
            if (ReadId(c) != idBefore) WriteId(c, idBefore);
            if (!EnsureRun(out _)) return;
            DropUncarried(idBefore);
        }
        catch (Exception ex) { WarnOnce("tip-after:" + Log.CauseKey(ex), $"CardState: carrying values through a transform threw: {Log.ExceptionText(ex)}"); }
    }

    internal static void DropUncarried(long id)
    {
        var vals = ValuesOf(id);
        if (vals == null) return;
        foreach (var n in CardStateCodec.NamesDroppedByTransform(vals.Keys.ToList(), _transformNames))
            vals.Remove(n);
        if (vals.Count == 0) _values.Remove(id);
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// After the game wrote save file <paramref name="saveFileName"/> of slot <paramref name="slot"/> with the text
    /// <paramref name="json"/>: keep only the ids that text holds (in memory too: a card that is in no save is
    /// gone), write the sidecar (or delete it when nothing is left), and delete the sidecars of save files that no
    /// longer exist. Does nothing, and costs one file check, for a save whose run never held a value.
    /// </summary>
    internal static void OnSaveWritten(int slot, string saveFileName, string json)
    {
        string path = null;
        try
        {
            if (!EnsureRun(out _)) return;   // a save rewritten at the main menu or during load: leave its sidecar alone
            path = SidecarPath(slot, saveFileName);
            if (path == null) return;
            bool exists = File.Exists(path);
            if (_values.Count == 0 && !exists) return;

            var present = CardStateCodec.ScanIds(json, out int malformed);
            if (malformed > 0)
                WarnOnce("scan-malformed", $"CardState: {malformed} card id entr(y/ies) in {saveFileName} could not be read; their values are kept only if another entry names them.");
            int before = _values.Count;
            foreach (var id in _values.Keys.ToList())
                if (!present.Contains(id)) _values.Remove(id);
            int pruned = before - _values.Count;

            if (_values.Count == 0)
            {
                if (exists) File.Delete(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, CardStateCodec.Format(_values), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            if (pruned > 0) Log.Debug($"CardState: {pruned} card(s) no longer in the game; their values were dropped.");
            Log.Debug($"CardState: wrote {CountValues(_values)} value(s) on {_values.Count} card(s) for {saveFileName}.");
            SweepOrphans(slot);
        }
        catch (Exception ex)
        {
            WarnOnce("save:" + Log.CauseKey(ex), $"CardState: writing {path ?? saveFileName} failed, so card values set since the last good write are kept only until the game closes: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>Deletes every sidecar in the slot's folder whose save file is gone (a deleted custom checkpoint).</summary>
    private static void SweepOrphans(int slot)
    {
        var slotDir = GameLoad.GameFileDirectoryPath(slot);
        var dir = Path.Combine(slotDir, CardStateCodec.FolderName);
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(CardStateCodec.Extension + ".tmp", StringComparison.OrdinalIgnoreCase)) { File.Delete(file); continue; }
            var save = CardStateCodec.SaveFileNameOf(name);
            if (save == null) continue;
            if (!File.Exists(Path.Combine(slotDir, save)))
            {
                File.Delete(file);
                Log.Debug($"CardState: removed {name}: its save file {save} is gone.");
            }
        }
    }

    internal static string SidecarPath(int slot, string saveFileName)
    {
        if (slot < 0) return null;
        var name = CardStateCodec.SidecarFileName(saveFileName);
        if (name == null) return null;
        return Path.Combine(Path.Combine(GameLoad.GameFileDirectoryPath(slot), CardStateCodec.FolderName), name);
    }

    // ── Diagnostics (harness and log) ────────────────────────────────────────

    /// <summary>The card's id as 16 hex digits, or "" when it has none (TestHarness reads it through /reflect/get).</summary>
    internal static string IdTextOf(object card)
    {
        if (card is not InGameCardBase c || !c) return "";
        long id = ReadId(c);
        return id == 0 ? "" : CardStateCodec.IdText(id);
    }

    /// <summary>The text of the current slot's sidecar for <paramref name="saveFileName"/>, or "&lt;missing&gt;".</summary>
    internal static string ReadSidecarText(string saveFileName)
    {
        var gl = GameLoad.Instance;
        var path = SidecarPath(gl ? gl.CurrentGameDataIndex : -1, saveFileName);
        if (path == null) return "<no path>";
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "<missing>";
    }

    /// <summary>Cards and values in memory for the run in progress.</summary>
    internal static string Summary()
    {
        EnsureRun(out _);
        return $"{_values.Count} card(s), {CountValues(_values)} value(s), slot {_runSlot}, loaded from '{_runSaveFile}'";
    }

    private static int CountValues(Dictionary<long, Dictionary<string, float>> state)
    {
        int n = 0;
        foreach (var v in state.Values) n += v.Count;
        return n;
    }

    internal static void WarnOnce(string key, string message)
    {
        if (_warned.Add(key)) Log.Warn(message);
    }
}
