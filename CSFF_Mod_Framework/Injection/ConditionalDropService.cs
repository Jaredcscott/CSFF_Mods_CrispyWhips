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
    private static MethodInfo _destroyCardMethod;
    private static bool _destroyMethodResolved;

    /// <summary>True when at least one conditional drop has been registered this session.</summary>
    internal static bool HasDrops => _dropsByEnv.Count > 0;

    // ── public API ──────────────────────────────────────────────────────────

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

            // Patch AlwaysUpdate=false so CT2 board cards don't follow the player.
            if (drop.ForceStay && card.AlwaysUpdate)
            {
                card.AlwaysUpdate = false;
                alwaysUpdatePatched++;
                Log.Debug($"ConditionalDropService: patched AlwaysUpdate=false on '{drop.UID}' (was true — would follow player on env transition)");
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
        if (!_dropsByEnv.ContainsKey(envUID)) return;

        bool firstVisit = _visitedEnvs.Add(envUID);  // returns true on first insert
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
        catch { }
        if (cardType < 2) return;

        var boardCards = Api.GameQuery.CardsInPlayerEnv();
        foreach (var ingameCard in boardCards)
        {
            if (!string.Equals(CardUtil.GetCardUniqueId(ingameCard), uid, StringComparison.OrdinalIgnoreCase))
                continue;
            CallDestroyCard(ingameCard);
            Log.Debug($"ConditionalDropService: removed out-of-condition '{uid}' from board");
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

    private static void CallDestroyCard(object ingameCard)
    {
        if (!_destroyMethodResolved)
        {
            _destroyMethodResolved = true;
            var t = ingameCard.GetType();
            for (; t != null; t = t.BaseType)
            {
                _destroyCardMethod = t.GetMethod("DestroyCard", BF, null, Type.EmptyTypes, null);
                if (_destroyCardMethod != null) break;
            }
            if (_destroyCardMethod == null)
                Log.Warn("ConditionalDropService: InGameCardBase.DestroyCard() not found — board card removal unavailable");
        }
        _destroyCardMethod?.Invoke(ingameCard, null);
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
            Log.Warn($"ConditionalDropService: IsQuestActive('{questUID}') failed: {ex.Message}");
            return false;
        }
    }

}
