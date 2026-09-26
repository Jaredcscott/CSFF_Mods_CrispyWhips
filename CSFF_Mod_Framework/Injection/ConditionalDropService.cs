using System.Reflection;
using CSFFModFramework.Data;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;
using static CSFFModFramework.Loading.WorldMapLoader;

namespace CSFFModFramework.Injection;

/// <summary>
/// Manages runtime-conditional board drops declared via <c>ConditionalDrops</c> in
/// <c>WorldMap/MapNodes.json</c>. Unlike <c>ExtraDropUIDs</c> (which are seeded into
/// <c>DefaultEnvCardDrops</c> and persist in <c>EnvironmentsData</c>), conditional drops
/// are spawned and removed at runtime based on conditions evaluated on env arrival and
/// day/season ticks.
///
/// <para>Use cases: seasonal crop fields, perk-unlocked NPCs, quest items, first-visit
/// rewards, day-range limited content. Do NOT use for resource cards that need to persist
/// their depletion state across sessions — use <c>ExtraDropUIDs</c> for those.</para>
///
/// <para>Called from <see cref="WorldMapInjector.OnGameManagerInitialized"/> and from
/// <see cref="WorldMapInjector.OnEnvWatchTick"/>.</para>
/// </summary>
internal static class ConditionalDropService
{
    private const BindingFlags BF =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // envUID → registered drops for that env
    private static readonly Dictionary<string, List<ConditionalDropDefinition>>
        _dropsByEnv = new(StringComparer.OrdinalIgnoreCase);

    // envUID → resolved CardData for each drop (parallel list)
    private static readonly Dictionary<string, List<CardData>>
        _cardsByEnv = new(StringComparer.OrdinalIgnoreCase);

    // FirstVisit tracking: envs the player has entered this run (reset by OnRunStart —
    // a previous save's visits must not suppress FirstVisit drops in a newly loaded one)
    private static readonly HashSet<string> _visitedEnvs = new(StringComparer.OrdinalIgnoreCase);

    // Nodes already registered — OnGMInitialized fires on every run start with the same
    // persistent MapNodeDefinition instances; re-registering would AddRange the same drops
    // again and evaluate (and spawn) each one N times after N save loads.
    private static readonly HashSet<MapNodeDefinition> _registeredNodes = new();

    private static bool _subscribedToDTP;
    private static readonly HashSet<string> _removeFailedWarned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when at least one conditional drop has been registered this session.</summary>
    internal static bool HasDrops => _dropsByEnv.Count > 0;

    // ── public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Load-time half of the ForceStay rule, called from <see cref="WorldMapInjector.PrepareAll"/>:
    /// sets <c>AlwaysUpdate=false</c> on every ForceStay drop's CardData before any save's cards
    /// exist, so the game never registers a live copy in <c>GameManager.AlwaysUpdateCards</c>.
    /// A UID that does not resolve here is skipped silently: <see cref="RegisterNode"/> warns
    /// about the same UID at run start.
    /// </summary>
    internal static void ApplyForceStayAtLoad(IEnumerable<MapNodeDefinition> defs)
    {
        int patched = 0;
        foreach (var def in defs)
        {
            if (def?.ConditionalDrops == null) continue;
            foreach (var drop in def.ConditionalDrops)
            {
                if (!drop.ForceStay || string.IsNullOrEmpty(drop.UID)) continue;
                if (GameRegistry.GetByUid(drop.UID) is CardData card && card.AlwaysUpdate)
                {
                    card.AlwaysUpdate = false;
                    patched++;
                    Log.Debug($"ConditionalDropService: patched AlwaysUpdate=false on '{drop.UID}' at load (ForceStay; would follow player on env transition)");
                }
            }
        }
        if (patched > 0)
            Log.Info($"ConditionalDropService: patched AlwaysUpdate=false on {patched} ForceStay card(s) at load");
    }

    /// <summary>Removes every live card of <paramref name="card"/> from GameManager.AlwaysUpdateCards.</summary>
    private static int PurgeAlwaysUpdateEntries(CardData card)
    {
        var gm = MBSingleton<GameManager>.Instance;
        if (gm == null || gm.AlwaysUpdateCards == null) return 0;
        return gm.AlwaysUpdateCards.RemoveAll(c => c != null && c.CardModel == card);
    }

    /// <summary>
    /// Registers one node's ConditionalDrops and patches <c>AlwaysUpdate=false</c> on any
    /// ForceStay=true card whose CardData has AlwaysUpdate=true (prevents CT2 board cards
    /// from following the player on environment transitions — CLAUDE.md §AlwaysUpdate).
    /// </summary>
    internal static void RegisterNode(MapNodeDefinition def)
    {
        if (def?.ConditionalDrops == null || def.ConditionalDrops.Count == 0) return;
        if (!_registeredNodes.Add(def)) return;   // already registered on a previous run start

        var resolved = new List<CardData>(def.ConditionalDrops.Count);
        var kept     = new List<ConditionalDropDefinition>(def.ConditionalDrops.Count);
        int alwaysUpdatePatched = 0;

        foreach (var drop in def.ConditionalDrops)
        {
            if (string.IsNullOrEmpty(drop.UID)) continue;
            var card = GameRegistry.GetByUid(drop.UID) as CardData;
            if (card == null)
            {
                Log.Warn($"ConditionalDropService: UID '{drop.UID}' not found in registry (mod {def.SourceMod}) — skipping drop");
                continue;
            }

            // ApplyForceStayAtLoad already cleared AlwaysUpdate on every ForceStay card it could
            // resolve at load. Still true here means a card that pass missed, and any copy of it the
            // save loaded is already in GameManager.AlwaysUpdateCards under the old flag. RemoveCard
            // will not take such an entry out once the flag is false, so remove it now; otherwise
            // the first travel leaves a pooled card in the list, CalculateEnvWeightsRoutine throws
            // on it and environment weights stop updating for the rest of the run.
            if (drop.ForceStay && card.AlwaysUpdate)
            {
                card.AlwaysUpdate = false;
                alwaysUpdatePatched++;
                int purged = PurgeAlwaysUpdateEntries(card);
                Log.Warn($"ConditionalDropService: '{drop.UID}' still had AlwaysUpdate=true at run start (missed at load); " +
                         $"set false and removed {purged} live entr{(purged == 1 ? "y" : "ies")} from GameManager.AlwaysUpdateCards");
            }

            kept.Add(drop);
            resolved.Add(card);
        }

        if (alwaysUpdatePatched > 0)
            Log.Info($"ConditionalDropService: patched AlwaysUpdate=false on {alwaysUpdatePatched} card(s) for '{def.EnvironmentUID}'");

        if (kept.Count == 0) return;

        var envUID = def.EnvironmentUID;
        if (!_dropsByEnv.TryGetValue(envUID, out var existingDrops))
        {
            _dropsByEnv[envUID] = kept;
            _cardsByEnv[envUID] = resolved;
        }
        else
        {
            existingDrops.AddRange(kept);
            _cardsByEnv[envUID].AddRange(resolved);
        }

        // Subscribe to DTP ticks once (for SeasonRange / DayRange re-evaluation).
        if (!_subscribedToDTP)
        {
            _subscribedToDTP = true;
            Api.TickEvents.DayRollover += OnDayRollover;
        }

        Log.Debug($"ConditionalDropService: registered {kept.Count} conditional drop(s) for '{envUID}'");
    }

    /// <summary>
    /// Per-run state reset. Called from <see cref="WorldMapInjector.OnGameManagerInitialized"/>
    /// before the RegisterNode loop so FirstVisit tracking starts fresh for the loaded save.
    /// </summary>
    internal static void OnRunStart() => _visitedEnvs.Clear();

    /// <summary>
    /// Called from <see cref="WorldMapInjector.OnEnvWatchTick"/> when the player enters a new env.
    /// Evaluates all conditional drops for this env and spawns/removes as needed.
    /// </summary>
    internal static void OnEnvArrival(string envUID)
    {
        if (string.IsNullOrEmpty(envUID)) return;
        if (!_dropsByEnv.ContainsKey(envUID))
        {
            Log.Debug($"ConditionalDropService: OnEnvArrival('{envUID}') — no registered drops for this env (known: {string.Join(",", _dropsByEnv.Keys)})");
            return;
        }

        bool firstVisit = _visitedEnvs.Add(envUID);  // returns true on first insert
        Log.Debug($"ConditionalDropService: OnEnvArrival('{envUID}') firstVisit={firstVisit} — evaluating {_dropsByEnv[envUID].Count} drop(s)");
        EvaluateEnv(envUID, firstVisit);
    }

    // ── private ─────────────────────────────────────────────────────────────

    private static void OnDayRollover()
    {
        var curEnv = Api.GameQuery.CurrentEnvironmentUniqueId;
        if (string.IsNullOrEmpty(curEnv)) return;
        if (!_dropsByEnv.ContainsKey(curEnv)) return;
        EvaluateEnv(curEnv, firstVisit: false);
    }

    private static void EvaluateEnv(string envUID, bool firstVisit)
    {
        if (!_dropsByEnv.TryGetValue(envUID, out var drops)) return;
        var cards = _cardsByEnv[envUID];

        for (int i = 0; i < drops.Count; i++)
        {
            var drop = drops[i];
            var card = cards[i];
            try
            {
                bool condMet = EvaluateCondition(drop.Condition, envUID, firstVisit);

                if (condMet)
                    SpawnIfNeeded(drop, card);
                else
                    RemoveIfPresent(drop.UID, card);
            }
            catch (Exception ex)
            {
                Log.Warn($"ConditionalDropService: error evaluating drop '{drop.UID}' in '{envUID}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static void SpawnIfNeeded(ConditionalDropDefinition drop, CardData card)
    {
        int count = CountOnBoard(drop.UID);
        Log.Debug($"ConditionalDropService: SpawnIfNeeded('{drop.UID}') count={count} max={drop.MaxOnBoard}");
        if (count >= drop.MaxOnBoard) return;

        for (int n = count; n < drop.MaxOnBoard; n++)
            Api.SpawnService.Spawn(drop.UID);

        Log.Debug($"ConditionalDropService: spawned '{drop.UID}' (was {count}, max {drop.MaxOnBoard})");
    }

    private static void RemoveIfPresent(string uid, CardData card)
    {
        // Only actively remove CT2+ board structures; CT0 items might be in player inventory.
        // CT2 placed cards cannot be inventoried, so presence == board card.
        int cardType = -1;
        try { cardType = Convert.ToInt32(CardUtil.GetMemberValue(card, "CardType") ?? -1); }
        catch (System.Exception ex) { Log.Debug($"ConditionalDropService.RemoveIfPresent('{uid}'): CardType read threw: {ex}"); }
        if (cardType < 2) return;

        var boardCards = Api.GameQuery.CardsInPlayerEnv();
        foreach (var ingameCard in boardCards)
        {
            if (!string.Equals(CardUtil.GetCardUniqueId(ingameCard), uid, StringComparison.OrdinalIgnoreCase))
                continue;
            // CardUtil.TryRemoveCard drives vanilla's RemoveCard coroutine, which is what takes a
            // card out of AllCards. Before 2.26.6 this called a parameterless DestroyCard that no
            // longer exists (EA 0.66i+ has only IEnumerator DestroyCard(bool)), so nothing was ever
            // removed and out-of-season fields stacked (CMC's cmcEnvVillageFarm).
            if (CardUtil.TryRemoveCard(ingameCard))
                Log.Debug($"ConditionalDropService: removed out-of-condition '{uid}' from board");
            else if (_removeFailedWarned.Add(uid))
                Log.Warn($"ConditionalDropService: could not remove out-of-condition '{uid}' from the board; it stays until the player removes it");
        }
    }

    private static int CountOnBoard(string uid)
    {
        int count = 0;
        foreach (var c in Api.GameQuery.CardsInPlayerEnv())
            if (string.Equals(CardUtil.GetCardUniqueId(c), uid, StringComparison.OrdinalIgnoreCase))
                count++;
        return count;
    }

    // ── condition evaluation ─────────────────────────────────────────────────

    private static bool EvaluateCondition(DropConditionDefinition cond, string envUID, bool firstVisit)
    {
        if (cond == null) return true;
        switch (cond.Type?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "alwaystrue":
                return true;

            case "hasperk":
                return CardUtil.IsPerkEquipped(cond.UID);

            case "improvementbuilt":
                return CardUtil.IsImprovementBuilt(cond.EnvUID, cond.UID);

            case "seasonrange":
                int season = GetSeasonIndex();
                return season >= cond.From && season <= cond.To;

            case "dayrange":
                int day = Api.GameQuery.CurrentDay;
                return day >= cond.From && day <= cond.To;

            case "firstvisit":
                return firstVisit;

            case "questactive":
                return IsQuestActive(cond.QuestUID);

            default:
                Log.Warn($"ConditionalDropService: unknown condition type '{cond.Type}' — treating as true");
                return true;
        }
    }

    private static int GetSeasonIndex()
    {
        var season = Api.GameQuery.CurrentSeason;
        if (string.IsNullOrEmpty(season)) return 0;
        switch (season.Trim().ToLowerInvariant())
        {
            case "spring": return 1;
            case "summer": return 2;
            case "autumn":
            case "fall":   return 3;
            case "winter": return 4;
            default:       return 0;
        }
    }

    private static readonly Dictionary<Type, MethodInfo> _questCompleteMethodCache = new();

    /// <summary>
    /// True when the quest identified by <paramref name="questUID"/> has started but not yet
    /// completed. <c>QuestLog</c> (a <c>UniqueIDScriptable</c>, resolvable via
    /// <see cref="GameRegistry.GetByUid"/> like any other) exposes <c>QuestStarted</c> and
    /// <c>QuestComplete(bool)</c> — reflected here since QuestLog is not a compile-time type in
    /// this project (matches the existing reflection pattern in <c>QuestInjector.cs</c>).
    /// </summary>
    private static bool IsQuestActive(string questUID)
    {
        if (string.IsNullOrEmpty(questUID)) return false;
        try
        {
            var quest = GameRegistry.GetByUid(questUID);
            if (quest == null) return false;
            var qt = quest.GetType();
            if (qt.Name != "QuestLog")
            {
                Log.Warn($"ConditionalDropService: QuestActive UID '{questUID}' resolves to a {qt.Name}, not a QuestLog");
                return false;
            }

            if (CardUtil.GetMemberValue(quest, "QuestStarted") is not true) return false;

            if (!_questCompleteMethodCache.TryGetValue(qt, out var completeMethod))
                _questCompleteMethodCache[qt] = completeMethod = qt.GetMethod("QuestComplete",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // QuestComplete(_AndValidated: false) — an unvalidated-complete quest should still
            // count as "no longer active" for a board-drop gate.
            var complete = completeMethod?.Invoke(quest, new object[] { false });
            return complete is not true;
        }
        catch (Exception ex)
        {
            Log.Warn($"ConditionalDropService: IsQuestActive('{questUID}') failed: {Log.ExceptionText(ex)}");
            return false;
        }
    }

}
