using CSFFModFramework.Api;
using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// M6 — the framework absorption of Sirus23's <c>CompanionHuntPatch.InitFreshOwlCompanion</c> /
/// <c>WildOwlLifecyclePatch</c> tame→retire→init flow (211 + 287 lines of owl-specific mod C#,
/// see Documentation/Design/Animal_System_M0-M2_As_Built.md). Keyed off a manifest
/// <c>Interactions[].OnSuccess.GiveCard</c> that matches the species' <c>Companion.Card</c>.
///
/// <para>Registers ONE <see cref="ActionRouter"/> <see cref="ActionTiming.AfterWrapped"/>
/// handler per qualifying Interaction. <see cref="ActionHandler.CardPredicate"/> matches by
/// reference against the species' own live <c>InGameNPC.AssociatedCard</c> — NEVER
/// <c>CardUid</c>, because <c>InGameNPC.CreateModelCard()</c> never sets
/// <c>CardData.UniqueID</c> on the synthesized board card (memory
/// <c>reference_actionrouter_npcagent_carduid</c>). The handler fires AFTER the wrapped
/// action's own coroutine — including its native <c>ProducedCards</c> resolution — has fully
/// completed, so by the time it runs, a successful roll has ALREADY placed the companion card
/// on the board (exactly the ordering <c>CompanionHuntPatch.InitFreshOwlCompanion</c>'s own doc
/// comment documents as load-bearing: retiring the wild agent synchronously INSIDE
/// <c>GameManager.ActionRoutine</c>'s own stat block re-enters <c>CheckForActions()</c>
/// mid-coroutine and eats the companion spawn — see memory
/// <c>reference_npcaction_move_before_drop_race</c>).</para>
///
/// <para>On success: retires the wild agent (its <c>Agent.Stats.Exists</c> stat → 0) and
/// relocates it to the Spirit World via a direct, SYNCHRONOUS <c>GameManager.MoveNPC</c> call
/// — not a stat-write-and-wait, which leaves the board card interactive for several further
/// player actions (the Attempt-12 interactivity gap this system was built to close). Then
/// inits the companion's zeroed durability stats to full: <c>GameManager.GiveCard</c> is
/// <c>void</c> in this game version, so a freshly spawned card can never receive a stat
/// override from the spawning call itself (memory
/// <c>reference_givecard_postfix_stat_init</c>) — left alone, a companion card whose
/// <c>SpoilageTime</c> carries <c>HasActionOnZero</c> (e.g. <c>OwlCompanion.json</c>'s "flies
/// away if starved") self-destructs the moment anything spends a daytime point.</para>
///
/// <para>On fail (companion NOT found on board): optionally relocates the wild agent to a
/// random <c>Movement.WanderEnvs</c> entry (<c>OnFail.Flee</c>) — an aggressive fail instead
/// fires <c>Encounter.Ref</c> natively via the Fail-Attack <c>CardsDropCollection.
/// DroppedEncounter</c> TameInteractionBuilder already wired, so Flee applies unconditionally
/// to any fail (fleeing FROM an attack it just triggered is coherent) rather than needing to
/// distinguish which fail sub-collection the engine picked.</para>
///
/// <para><b>Deliberately out of scope</b> (Sirus23 keeps these — they are NOT owl-specific):
/// container-guard (any card can be dragged into storage unless a mod patches
/// <c>InGameCardBase.CanReceiveInInventoryInstance</c>, e.g. Sirus's
/// <c>CompanionContainerGuardPatch</c>, which already covers Wolf/Fox/Owl uniformly) and
/// auto-feed/auto-drink/health/morale upkeep (Sirus's <c>WolfTickPatch</c>, likewise shared
/// across three companions, only one of which is manifest-driven). Persistence needs no new
/// code either: a plain <c>AlwaysUpdate</c> CT0 card saves/restores through the same vanilla
/// path every companion pet already uses; <c>Spawn.SuppressWhileCardOnBoard</c> (M2,
/// <see cref="AnimalLifecycleTicker"/>) already re-derives wild-agent suppression from board
/// state on every load, so the retirement here needs no explicit save hook of its own.</para>
/// </summary>
internal static class CompanionService
{
    /// <summary>Number of Interaction→Companion links armed this load. Reported once per load
    /// by <see cref="AnimalService"/>.</summary>
    public static int AppliedCount { get; private set; }

    private static readonly List<ActionHandler> _registered = new();

    /// <summary>Unregisters every handler from the previous load — required because
    /// <c>AnimalService.LoadAll</c> can re-run within one process (returning to the main menu),
    /// and each pass builds a FRESH <see cref="NPCAgent"/> instance. A stale handler from a
    /// prior load would still reference the OLD (now-orphaned) agent/companion objects.</summary>
    public static void Reset()
    {
        foreach (var handler in _registered) ActionRouter.Unregister(handler);
        _registered.Clear();
        AppliedCount = 0;
    }

    public static void Apply(NPCAgent agent, AnimalManifest m, DutyBuilder.SpeciesStats stats, CardData spiritWorld)
    {
        if (agent == null || m == null || m.Interactions.Count == 0) return;

        var wanderEnvs = m.WanderEnvs.Count > 0
            ? m.WanderEnvs.Select(GameRegistry.GetByUid<CardData>).Where(e => e != null).ToList()
            : new List<CardData>();

        foreach (var ix in m.Interactions)
        {
            if (string.IsNullOrEmpty(ix.GiveCard)) continue;
            var companionCard = GameRegistry.GetByUid<CardData>(ix.GiveCard);
            if (companionCard == null) continue;   // TameInteractionBuilder already logged this

            bool initStats = m.HasCompanion
                && string.Equals(m.CompanionCard, ix.GiveCard, StringComparison.Ordinal)
                && m.CompanionInitStatsOnSpawn;

            var speciesId = m.SpeciesId;
            var ixName = ix.Name;
            var despawnAgent = ix.DespawnAgent;
            var flee = ix.Flee;
            var companionUid = companionCard.UniqueID;
            var actionNamePrefix = ix.ActionName ?? ix.Name;

            var handler = ActionRouter.Register(new ActionHandler
            {
                Name = $"Animals:{speciesId}:{ixName}",
                CardPredicate = ctx => IsThisAgentsCard(agent, ctx.Card),
                ActionNamePrefix = actionNamePrefix,
                Timing = ActionTiming.AfterWrapped,
                After = _ =>
                {
                    var companions = FindLiveCardsInPlayerEnv(companionUid);
                    if (companions.Count > 0)
                    {
                        Log.Info($"Animals: {speciesId}: Interaction '{ixName}' succeeded — companion '{companionUid}' is live.");
                        if (initStats) foreach (var companion in companions) InitCompanionStatsToFull(companion, speciesId);
                        if (despawnAgent) RetireAndRelocate(agent, stats, spiritWorld, speciesId);
                    }
                    else
                    {
                        Log.Info($"Animals: {speciesId}: Interaction '{ixName}' failed.");
                        if (flee) FleeToWanderEnv(agent, wanderEnvs, speciesId);
                    }
                },
            });

            if (handler != null)
            {
                _registered.Add(handler);
                AppliedCount++;
            }
        }
    }

    private static bool IsThisAgentsCard(NPCAgent agent, object card)
    {
        if (card == null) return false;
        var gm = MBSingleton<GameManager>.Instance;
        var npc = gm?.AllNPCs?.FirstOrDefault(n => n != null && n.NPCModel == agent);
        return npc != null && ReferenceEquals(npc.AssociatedCard, card);
    }

    // Every live instance of the companion card on the player's board. AllCards also holds carried
    // and background cards (root CLAUDE.md, AllCards env scoping), and a player can already own a
    // companion of the same card, so before 2.26.8 the first UID match could be an off-board or older
    // instance: it read as "tame succeeded" and had its stats topped up instead of the new one.
    private static List<InGameCardBase> FindLiveCardsInPlayerEnv(string uid)
    {
        var found = new List<InGameCardBase>();
        var gm = MBSingleton<GameManager>.Instance;
        if (gm?.AllCards == null) return found;
        foreach (var card in gm.AllCards)
            if (card && card.CardModel != null && card.CardModel.UniqueID == uid && card.CardEnvironment.MatchesPlayerEnv)
                found.Add(card);
        return found;
    }

    /// <summary>Sets every ACTIVE durability stat to its max, but only on an instance whose active
    /// stats ALL read exactly 0: that snapshot is unambiguously "never initialized," never
    /// legitimate neglect (mirrors Sirus23's own <c>WolfTickPatch.TryInitFreshSpawn</c>). An
    /// instance with any active stat above 0 is an older companion, or one that spawned with its
    /// JSON values (framework 2.26.7 onward), and is left alone.</summary>
    private static void InitCompanionStatsToFull(InGameCardBase companion, string speciesId)
    {
        var zeroed = new List<(string Stat, float Max)>();
        foreach (var statName in CardUtil.AllDurabilityStats)
        {
            float cur = CardUtil.GetDurability(companion, statName);
            float max = CardUtil.GetDurabilityMax(companion, statName);
            if (float.IsNaN(cur) || float.IsNaN(max) || max <= 0f) continue;
            if (cur > 0f) return;
            zeroed.Add((statName, max));
        }
        int fixedCount = 0;
        foreach (var (stat, max) in zeroed)
            if (CardUtil.SetDurability(companion, stat, max)) fixedCount++;
        if (fixedCount > 0)
            Log.Info($"Animals: {speciesId}: companion spawned with {fixedCount} zeroed stat(s) — initialized to full immediately after tame.");
    }

    private static FieldInfo _npcStatsDictField;

    private static void RetireAndRelocate(NPCAgent agent, DutyBuilder.SpeciesStats stats, CardData spiritWorld, string speciesId)
    {
        var gm = MBSingleton<GameManager>.Instance;
        var npc = gm?.AllNPCs?.FirstOrDefault(n => n != null && n.NPCModel == agent);
        if (npc == null)
        {
            Log.Warn($"Animals: {speciesId}: companion tame succeeded but the wild agent's live InGameNPC could not be resolved — retirement skipped (the suppression safety net in AnimalLifecycleTicker will force-retire on the next tick).");
            return;
        }

        if (stats?.Exists != null)
        {
            _npcStatsDictField ??= AccessTools.Field(typeof(InGameNPC), "NPCStatsDict");
            if (_npcStatsDictField?.GetValue(npc) is Dictionary<NPCStat, InGameNPCStat> dict
                && dict.TryGetValue(stats.Exists, out var existsStat) && existsStat != null)
            {
                existsStat.SetStatValueFromEditor(0f);
                Log.Info($"Animals: {speciesId}: wild agent retired (exists -> 0) after a successful tame.");
            }
            else
            {
                Log.Warn($"Animals: {speciesId}: could not write the Exists stat on retirement — the suppression safety net will force-retire on the next tick.");
            }
        }

        if (spiritWorld != null)
        {
            gm.MoveNPC(npc, new EnvID(spiritWorld, false));
            Log.Info($"Animals: {speciesId}: wild agent relocated to the Spirit World immediately — no interactivity gap.");
        }
    }

    private static void FleeToWanderEnv(NPCAgent agent, List<CardData> wanderEnvs, string speciesId)
    {
        if (wanderEnvs == null || wanderEnvs.Count == 0)
        {
            Log.Debug($"Animals: {speciesId}: OnFail.Flee requested but Movement.WanderEnvs is empty — nothing to flee to, skipped.");
            return;
        }
        var gm = MBSingleton<GameManager>.Instance;
        var npc = gm?.AllNPCs?.FirstOrDefault(n => n != null && n.NPCModel == agent);
        if (npc == null) return;

        var dest = wanderEnvs[UnityEngine.Random.Range(0, wanderEnvs.Count)];
        gm.MoveNPC(npc, new EnvID(dest, false));
        Log.Info($"Animals: {speciesId}: fled to '{dest.name}' after a failed interaction.");
    }
}
