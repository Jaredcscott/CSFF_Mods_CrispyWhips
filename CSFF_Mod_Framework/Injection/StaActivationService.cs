using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Phase-2 SelfTriggeredAction (STA) support — load-time validation plus a
/// run-start activation check.
///
/// <para>No injector is needed for STAs. <c>GameManager.InitializeStatsAndActions()</c>
/// (called on every run start) iterates <c>GameLoad.Instance.DataBase.AllData</c>,
/// collects every exact-type <see cref="SelfTriggeredAction"/> into
/// <c>GameManager.AllSelfActions</c>, calls <c>Init()</c> (merging per-action save
/// state by <c>ActionName.DefaultText</c>), and registers each Action's
/// <c>StatChangeTrigger</c> stats as listeners. JsonDataLoader already registers mod
/// STAs into AllData, so discovery is automatic.</para>
///
/// <para>What vanilla does NOT do is tolerate authoring mistakes:
/// a null <c>Actions</c> array NREs InitializeStatsAndActions; an Action with zero
/// <c>StatChangeTrigger</c> entries can never fire (zero conditions are never "all
/// true" in <c>StatTriggeredActionStatus</c>); an unresolved trigger Stat is skipped
/// with at most a Player.log error; and OnlyOnce / ResetWhenOutOfRange actions persist
/// their played-state keyed by <c>ActionName.DefaultText</c>, so empty or duplicate
/// names silently break save round-trips. <see cref="ValidateAll"/> normalizes the
/// fatal cases and warns on the silent ones, then arms a one-line
/// <c>OnGMInitialized</c> confirmation that the mod STAs actually reached
/// <c>GameManager.AllSelfActions</c>.</para>
/// </summary>
internal static class StaActivationService
{
    private static readonly List<SelfTriggeredAction> _modStas = new();
    private static bool _subscribed;
    private static bool _firstRunConfirmed;
    private static Action _gmInitializedHandler;

    public static void ValidateAll(IEnumerable allData)
    {
        _modStas.Clear();
        var modUniqueIds = Loading.JsonDataLoader.AllModUniqueIds;
        if (modUniqueIds.Count == 0) return;

        int warnings = 0;
        foreach (var item in allData)
        {
            // Exact-type match mirrors GameManager.InitializeStatsAndActions —
            // a subclass would not be discovered by vanilla, so don't validate it either.
            if (item is not SelfTriggeredAction sta || sta.GetType() != typeof(SelfTriggeredAction)) continue;
            if (string.IsNullOrEmpty(sta.UniqueID) || !modUniqueIds.Contains(sta.UniqueID)) continue;

            _modStas.Add(sta);
            warnings += Validate(sta);
        }

        if (_modStas.Count == 0)
        {
            Log.Debug("StaActivation: no mod SelfTriggeredActions loaded");
            return;
        }

        Log.Debug($"StaActivation: {_modStas.Count} mod SelfTriggeredAction(s) validated, {warnings} warning(s) — GameManager discovers them from AllData at run start");
        TrySubscribeToGmInitialized();
    }

    private static int Validate(SelfTriggeredAction sta)
    {
        int warnings = 0;

        if (sta.Actions == null)
        {
            // Vanilla reads Actions.Length right after Init() — normalize to empty.
            sta.Actions = new FromStatChangeAction[0];
            Log.Warn($"StaActivation: {sta.name} has no Actions array — normalized to empty (the STA will do nothing)");
            return 1;
        }

        // Null elements NRE in Init() (Actions[i].StatChangeTrigger.Length) — compact them out.
        if (Array.Exists(sta.Actions, a => a == null))
        {
            sta.Actions = sta.Actions.Where(a => a != null).ToArray();
            Log.Warn($"StaActivation: {sta.name} had null Action entries — removed");
            warnings++;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < sta.Actions.Length; i++)
        {
            var action = sta.Actions[i];
            string label = $"{sta.name}.Actions[{i}]";

            if (action.ActionName == null)
            {
                // Init() reads ActionName.DefaultText for the per-action save-state ID.
                action.ActionName = new LocalizedString { LocalizationKey = "IGNOREKEY", DefaultText = $"{sta.name}_{i}" };
                Log.Warn($"StaActivation: {label} had no ActionName — assigned save-state ID '{sta.name}_{i}'");
                warnings++;
            }

            if (action.StatChangeTrigger == null)
                action.StatChangeTrigger = new StatValueTrigger[0];

            if (action.StatChangeTrigger.Length == 0)
            {
                Log.Warn($"StaActivation: {label} has no StatChangeTrigger entries — it can never fire (zero conditions are never 'all true')");
                warnings++;
            }

            for (int t = 0; t < action.StatChangeTrigger.Length; t++)
            {
                if (!action.StatChangeTrigger[t].Stat)
                {
                    Log.Warn($"StaActivation: {label}.StatChangeTrigger[{t}] has no resolved Stat (check StatWarpData GUID) — the trigger never matches");
                    warnings++;
                }
            }

            var id = action.ActionName.DefaultText;
            if (action.RepeatOptions != StatTriggerTypes.Repeat)
            {
                // OnlyOnce / ResetWhenOutOfRange persist played-state keyed by this ID.
                if (string.IsNullOrEmpty(id))
                {
                    Log.Warn($"StaActivation: {label} is {action.RepeatOptions} but ActionName.DefaultText is empty — played-state cannot persist across save/load (it will re-fire each load)");
                    warnings++;
                }
                else if (!seenIds.Add(id))
                {
                    Log.Warn($"StaActivation: {label} duplicates ActionName.DefaultText '{id}' — save-state of same-named actions collides");
                    warnings++;
                }
            }
            else if (!string.IsNullOrEmpty(id))
            {
                seenIds.Add(id);
            }
        }

        return warnings;
    }

    /// <summary>
    /// Subscribes to <c>GameManager.OnGMInitialized</c> (public static Action field,
    /// invoked at the end of <c>FinishInitializing</c>) so we can confirm the mod STAs
    /// reached <c>GameManager.AllSelfActions</c> after every run start. Idempotent.
    /// Same pattern as <see cref="Loading.BlueprintContainerSaveLoadFix"/>.
    /// </summary>
    private static void TrySubscribeToGmInitialized()
    {
        if (_subscribed) return;
        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            var field = gmType?.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(Action))
            {
                Log.Warn("StaActivation: GameManager.OnGMInitialized not found — run-start activation check disabled");
                return;
            }

            _gmInitializedHandler = OnGameManagerInitialized;
            field.SetValue(null, (Action)Delegate.Combine((Action)field.GetValue(null), _gmInitializedHandler));
            _subscribed = true;
            Log.Debug("StaActivation: subscribed to GameManager.OnGMInitialized");
        }
        catch (Exception ex)
        {
            Log.Warn($"StaActivation: failed to subscribe to OnGMInitialized: {Log.ExceptionText(ex)}");
        }
    }

    private static void OnGameManagerInitialized()
    {
        try
        {
            var gmType = AccessTools.TypeByName("GameManager");
            var instanceProp = gmType?.GetProperty("Instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            var gm = instanceProp?.GetValue(null);
            var allSelfField = gmType?.GetField("AllSelfActions",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var list = gm != null ? allSelfField?.GetValue(gm) as IList : null;
            if (list == null)
            {
                Log.Warn("StaActivation: could not read GameManager.AllSelfActions — cannot confirm activation");
                return;
            }

            int present = 0;
            List<string> missing = null;
            foreach (var sta in _modStas)
            {
                if (list.Contains(sta)) present++;
                else (missing ??= new List<string>()).Add(sta.name);
            }

            if (missing != null)
                Log.Warn($"StaActivation: only {present}/{_modStas.Count} mod SelfTriggeredAction(s) reached GameManager.AllSelfActions — missing: {string.Join(", ", missing)}");
            else if (!_firstRunConfirmed)
            {
                // One Info line per session (first run start) per the logging norms;
                // subsequent run starts log at Debug.
                _firstRunConfirmed = true;
                Log.Info($"StaActivation: {present}/{_modStas.Count} mod SelfTriggeredAction(s) active in GameManager.AllSelfActions");
            }
            else
                Log.Debug($"StaActivation: {present}/{_modStas.Count} mod SelfTriggeredAction(s) active in GameManager.AllSelfActions");
        }
        catch (Exception ex)
        {
            Log.Warn($"StaActivation: run-start activation check failed: {Log.ExceptionText(ex)}");
        }
    }
}
