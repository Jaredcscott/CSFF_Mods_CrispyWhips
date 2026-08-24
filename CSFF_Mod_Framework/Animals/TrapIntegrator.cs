using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// M4 — makes a manifest-declared species catchable by the four vanilla land traps, and owns the
/// only place in the animal system that MUTATES SHARED VANILLA DATA. Schema reference:
/// Documentation/Design/Animals_Schema.md §Traps; author cookbook: Documentation/CSFF_Patterns.md
/// §Adding a Roaming Animal → Traps + bait.
///
/// <para><b>Engine pipeline this builds into</b> (all citations verified against the EA 0.66h
/// export in <c>Documentation/GameData/CSFF-JsonData_Current/</c> and <c>.decomp/</c> while
/// building M4 — the prior spike was written against 0.65h and one of its facts had already
/// drifted, see "obfuscated names" below):</para>
/// <list type="number">
/// <item><b>Bait theft.</b> A hungry agent's feed duty (<see cref="DutyBuilder"/>'s
/// <c>AffectItems</c> action) destroys the bait sitting in the trap's inventory. Destroying it
/// routes through <c>GameManager.RemoveCard</c> → <c>InGameCardBase.CheckForOnInteractAction</c>
/// with <c>InteractionTriggerTypes.RemoveItemFromInventory</c> — the trap's single
/// <c>OnInteractActions[0]</c> ("Trap") has <c>InteractionTypes.MaskValue: 16</c>, i.e. exactly
/// that trigger.</item>
/// <item><b>Interactor check.</b> <c>OnInteractAction.ValidInteractor</c> walks
/// <c>TriggerInteractors</c> (<c>OnInteractAction.cs:11,23-33</c>) and each
/// <c>NPCOrNPCTagRef.CheckNPC</c> returns <c>_NPC.NPCModel == Agent</c> for an agent-typed entry.
/// The vanilla list is a CLOSED 20-entry array, identical in length on all four land traps, and
/// a species that is not in it can never spring a trap. <b>This is the list this class
/// appends to, and the reason the file is safety-critical.</b> The player never matches (the
/// player branch requires a TAG-typed entry), so player bait removal cannot spring a trap.</item>
/// <item><b>Type stamp then catch roll.</b> The action's <c>NPCStatModifications</c> stamp
/// <c>AgentTrapType</c> with the trap's type code, then <c>GameManager.ActionRoutine</c> rolls
/// <c>AgentActionChance</c> and, on success, sets
/// <c>CurrentActionRequest = NPCActionTriggerRequest("Interact with a Trap", trap, tick)</c>.
/// The agent must therefore own an <c>AgentActions</c> entry whose <c>ActionID</c> is EXACTLY
/// that string (the trap's <c>TriggerActionInAgent</c> value) or the catch silently no-ops.</item>
/// <item><b>Catch.</b> That agent action drops the catch card with
/// <c>DropCardsInTriggerContainer: true</c>, so the card lands inside the now-sprung Triggered
/// trap. Which card appears is chosen by weighting its drop collections on the
/// <c>AgentTrapType</c> value that was just stamped — that is how vanilla snares take birds
/// ALIVE (<c>PartridgeTied*</c>) while pits take boars alive and everything else drops a
/// carcass.</item>
/// </list>
///
/// <para><b>Novel-engine findings recorded while building this (M4) — each is a silent failure
/// no build step catches:</b></para>
/// <list type="bullet">
/// <item><b>The `AgentTrapType` stamp values in the game's own documentation string are WRONG for
/// two of the four traps.</b> <c>NPCStat/AgentTrapType</c>'s description reads
/// "Deadfall = 10, Snare = 20, PitTrap = 30, LogTrap = 40", but the cards actually stamp
/// LogTrap <b>+30</b> and PitTrap <b>+40</b> (each trap's <c>OnInteractActions[0]
/// .NPCStatModifications[0].ValueChange</c>). The agents' drop gates read the stat, so the CARD
/// values are authoritative and <see cref="TrapKind.TypeStamp"/> below uses them. Authoring a
/// catch result against the doc string silently yields a sprung trap containing nothing.</item>
/// <item><b>Obfuscated export names are NOT stable across game versions — never hardcode one.</b>
/// The 0.65h spike recorded the trap-container tag as <c>Image_7173</c>; in 0.66h the four set
/// traps' <c>CardTagsWarpData</c> exports read <c>Outline_7624 / Outline_7626 /
/// PulsingOutline_7768 / PulsingOutline_7767</c>, and the traps' <c>InventoryFilter.TagFilters</c>
/// export as UI object names (<c>Text</c>, <c>ButtonText</c>, <c>Handle</c>). These are Unity
/// asset file names, not the SO's runtime <c>.name</c> (root CLAUDE.md §Obfuscated WarpData Names).
/// <see cref="TrapContainerTags"/> therefore derives the container tag by INTERSECTING the live
/// <c>CardTags</c> of the four resolved trap cards at load and using the resulting SO references
/// by identity — no name ever crosses the boundary.</item>
/// <item><b><c>CardData.CompleteInventoryFilter</c> memoizes into a private <c>CachedFilter</c>
/// and only refills it while <c>CachedFilter.IsEmpty</c></b> (<c>CardData.cs:1340-1346</c>,
/// cleared only by <c>CardData.Init()</c>). Mirroring a catch card into a Triggered trap's
/// <c>InventoryFilter</c> after something has read the complete filter would therefore be a
/// no-op — the classic cache divergence of root CLAUDE.md §Runtime Card State Caching. This
/// class runs in the LoadOrchestrator phase (before <c>GameManager.Awake</c>) so the cache is
/// still cold, and it additionally clears the field defensively after every mutation.</item>
/// <item><b>There are SIX trap NPCStats, not seven.</b> The plan doc says seven; the registry
/// holds <c>AgentTrapType</c>, <c>AgentTrapCunning</c>, and exactly four per-type variants
/// (<c>Snare</c>, <c>Deadfall</c>, <c>LogTrap</c>, <c>PitTrap</c>). There is no cage/funnel
/// cunning stat — the Funnel and Deadfall "catch prey" timers are an agent-free
/// <c>CookingRecipes</c> path with no NPCStat involvement at all.</item>
/// <item><b>The engine runs <c>CheckForActions()</c> TWICE on the tick a trap springs, and the
/// first pass happens BEFORE the trigger request exists.</b> <c>InGameNPCStat.ApplyInstantModifier</c>
/// adds the agent to <c>npcActionsToCheck</c> for any non-zero stat delta, so the trap-type stamp
/// itself schedules a sweep at <c>GameManager.cs:4364</c>; <c>CurrentActionRequest</c> is only
/// assigned at <c>GameManager.cs:4396</c>, ahead of the second sweep. Any <c>Repeat</c> agent
/// action is eligible in that first pass (its <c>LastPlayedTick</c> is still the previous tick —
/// <c>InGameNPC.cs:1764</c>), so a standalone "reset trap type" action ZEROES THE STAMP BEFORE THE
/// CATCH ACTION READS IT. Vanilla ships exactly such an action (<c>Agent_Partridge1</c> "Reset
/// Agent Trap Type", unconditional <c>-100</c>, <c>Repeat</c>) and is subject to the same ordering,
/// so mirroring vanilla here reproduces the bug instead of avoiding it. This class therefore emits
/// NO standalone reset: the catch action clears its own stamp
/// (<see cref="BuildCatchStatChanges"/>), and a stamp left by a FAILED catch roll is cleared by
/// <see cref="AnimalLifecycleTicker"/> on the framework's own tick, outside the engine's action
/// sweep where it cannot race anything.</item>
/// <item><b>An all-zero-weight drop-collection set does not drop nothing — it picks UNIFORMLY AT
/// RANDOM.</b> <c>GameManager.cs:7594-7599</c> short-circuits <c>TotalValue == 0</c> to
/// <c>Random.Range(0, DropsInfo.Length)</c>, ignoring every weight. So a gate that fails silently
/// degrades to a lottery across every catch card, including another trap type's. The fallback
/// collection therefore carries a non-zero base weight (vanilla's carcass-base-100 layout) to keep
/// the out-of-range read deterministic.</item>
/// </list>
/// </summary>
internal static class TrapIntegrator
{
    /// <summary>One vanilla land trap: the set card whose <c>TriggerInteractors</c> we join, the
    /// triggered card whose inventory filter we mirror catch cards into, and the
    /// <c>AgentTrapType</c> value the set card stamps on a springing agent.
    /// UIDs verified against the EA 0.66h export.</summary>
    internal sealed class TrapKind
    {
        public readonly string Name;
        public readonly string SetUid;
        public readonly string TriggeredUid;
        public readonly int TypeStamp;

        public TrapKind(string name, string setUid, string triggeredUid, int typeStamp)
        {
            Name = name; SetUid = setUid; TriggeredUid = triggeredUid; TypeStamp = typeStamp;
        }
    }

    /// <summary>The four land traps that route through the agent system. FunnelTrap is
    /// deliberately absent: it has no <c>OnInteractActions</c> and no agent path at all (its
    /// catches come from a <c>CookingRecipes</c> timer), so there is nothing for a species to
    /// opt into. TypeStamp values are read off the CARDS, not the stat's doc string — see the
    /// class remarks.</summary>
    private static readonly TrapKind[] Kinds =
    {
        new TrapKind("Deadfall", "46cb54af052a5854981a9b65b4ab8b48", "945f8180e8f8ee04d9dbc64dbb7794d5", 10),
        new TrapKind("Snare",    "ece4346feb9fd2c46b6703a117d0bd3b", "ae3695c9d9c5f6f469584754ad4c38c3", 20),
        new TrapKind("LogTrap",  "7ee6dbdbddc8e7e4e9eb065f6a22df39", "9463d107c1237224fb6956f3df0cf5c8", 30),
        new TrapKind("PitTrap",  "a44728813728a6d4e92f13745790930c", "b2eab4cc863925146a748d67bb41dec3", 40),
    };

    /// <summary>The exact string the trap cards carry in <c>TriggerActionInAgent</c>. An agent
    /// action must match it byte-for-byte to receive the catch request.</summary>
    public const string TrapActionId = "Interact with a Trap";

    /// <summary>Runtime NPCStat names (the registry keys carry a parenthetical doc annotation;
    /// the runtime <c>.name</c> is the bare identifier). Order matters only for logging.</summary>
    private const string StatTrapType = "AgentTrapType";
    /// <summary>The GUID the vanilla trap cards stamp — used to cross-check that the by-name
    /// lookup returned the same SO instance (see <see cref="Apply"/>).</summary>
    public const string TrapTypeStatUid = "cf4a2923e7142524f8aff07c95a916e5";
    private const string StatCunningBase = "AgentTrapCunning";
    private static readonly Dictionary<string, string> CunningStatByKind = new(StringComparer.Ordinal)
    {
        ["Snare"]    = "AgentTrapCunningSnare",
        ["Deadfall"] = "AgentTrapCunningDeadfall",
        ["LogTrap"]  = "AgentTrapCunningLogTrap",
        ["PitTrap"]  = "AgentTrapCunningPitTrap",
    };

    /// <summary>Vanilla's hard-immunity sentinel for a per-type cunning stat.</summary>
    public const float ImmuneCunning = 10000f;

    /// <summary>Number of species that ended the load actually joined to at least one vanilla
    /// trap. Reported once per load by <see cref="AnimalService"/>.</summary>
    public static int AppliedCount { get; private set; }

    private static bool _tagsResolved;
    private static CardTag[] _containerTags = Array.Empty<CardTag>();
    private static FieldInfo _cachedFilterField;

    public static void Reset()
    {
        AppliedCount = 0;
        _tagsResolved = false;
        _containerTags = Array.Empty<CardTag>();
    }

    // ------------------------------------------------------- container tag derivation ---

    /// <summary>The CardTag(s) shared by all four live set-trap cards — the handle a feed duty
    /// uses in <c>LookInContainersSettings.AllowedContainers</c> so a hungry animal will raid a
    /// baited trap. Derived by INTERSECTING the resolved cards' own <c>CardTags</c> at load
    /// rather than by name, because the exported names are unstable Unity asset names (class
    /// remarks). Cached per load; returns empty (never null) when derivation fails.</summary>
    public static CardTag[] TrapContainerTags()
    {
        if (_tagsResolved) return _containerTags;
        _tagsResolved = true;

        List<CardTag> shared = null;
        int resolved = 0;

        // Intersect across BOTH the set and triggered forms of all four traps (8 cards). The set
        // traps alone are NOT specific enough: their intersection also contains a generic
        // storage-container tag carried by ~30 vanilla cards (Basket, ClayJar, ClayStoragePot,
        // ClothSack, CookingPot, FermentationBin, Shelf, WoodenPlate, ...). Handing that to the
        // feed duty's AllowedContainers would let a hungry animal DESTROY food out of the
        // player's storage — LookInContainersSettings.ShouldLookInContainer matches on ANY entry,
        // and the feed action's CardModifications.Destroy deletes the card outright. Widening the
        // intersection to the triggered traps removes it, because those generic containers do not
        // carry the traps-only tags.
        foreach (var uid in Kinds.Select(k => k.SetUid).Concat(Kinds.Select(k => k.TriggeredUid)))
        {
            var card = GameRegistry.GetByUid<CardData>(uid);
            if (card == null) continue;
            resolved++;

            var tags = card.CardTags ?? Array.Empty<CardTag>();
            if (shared == null)
                shared = tags.Where(t => t != null).ToList();
            else
                shared.RemoveAll(t => !tags.Contains(t));
        }

        // Belt-and-braces: drop any surviving tag that is carried by a card which is not one of
        // the eight traps. Cheap (one pass over the registry) and it makes the "traps only"
        // property hold by construction rather than by the intersection happening to work out.
        if (shared is { Count: > 0 })
        {
            var trapUids = new HashSet<string>(Kinds.Select(k => k.SetUid).Concat(Kinds.Select(k => k.TriggeredUid)), StringComparer.Ordinal);
            var leaked = new HashSet<CardTag>();
            foreach (var so in Database.GetAllOfType(typeof(CardData)))
            {
                if (so is not CardData other || trapUids.Contains(other.UniqueID ?? "")) continue;
                // Vanilla ships three orphaned CardType-2 duplicates of the trap cards themselves
                // (NOT_USED_LogTrapOld / PitTrapOld / SnareTrapOld — same tags, same display name,
                // confirmed via the 0.66i export to be unreferenced by any blueprint or spawn
                // path). They still carry the traps-only outline tag, so without this exclusion
                // they falsely "leak" it and zero out the entire derivation every load. They can
                // never appear on a board, so letting the derived tag also match them is harmless.
                if (other.name != null && other.name.StartsWith("NOT_USED_", StringComparison.Ordinal)) continue;
                var otherTags = other.CardTags;
                if (otherTags == null) continue;
                foreach (var tag in shared)
                    if (tag != null && otherTags.Contains(tag)) leaked.Add(tag);
            }
            if (leaked.Count > 0)
            {
                Log.Debug($"Animals: trap-container derivation dropped {leaked.Count} tag(s) also carried by non-trap cards: "
                        + string.Join(", ", leaked.Select(t => t.name)));
                shared.RemoveAll(leaked.Contains);
            }
        }

        if (resolved == 0 || shared == null || shared.Count == 0)
        {
            // ONE Error, then degrade — never throw. A species can still be joined to the traps'
            // TriggerInteractors without this; only the generated feed duty needs it.
            Log.Error($"Animals: trap-container tag derivation failed ({resolved}/{Kinds.Length} vanilla trap cards resolved, "
                    + $"{shared?.Count ?? 0} shared CardTags) — generated feed duties cannot target baited traps this load. "
                    + "The four land traps are matched by UniqueID; a game update that re-IDs them would produce exactly this line.");
            _containerTags = Array.Empty<CardTag>();
            return _containerTags;
        }

        _containerTags = shared.ToArray();
        Log.Debug($"Animals: trap-container tags derived from {resolved} live trap cards: "
                + string.Join(", ", _containerTags.Select(t => t.name)));
        return _containerTags;
    }

    /// <summary>The CardTags the vanilla land traps themselves accept into their bait slot —
    /// i.e. everything the player is allowed to bait a trap WITH. Used as the default food pool
    /// for a generated feed duty when the manifest names no explicit bait.
    ///
    /// <para>This is derived, never authored, for the same reason as
    /// <see cref="TrapContainerTags"/>: a trap's <c>InventoryFilter.TagFilters</c> exports as
    /// meaningless UI asset names (<c>Text</c>, <c>ButtonText</c>, <c>Handle</c>,
    /// <c>CookedNotificationIcon</c>, <c>ConfirmSaveLoadText</c> in the 0.66h dump), while the
    /// live SOs behind them are the real animal-food tags. Deriving keeps "what the animal will
    /// eat" and "what the trap will hold" the same set BY CONSTRUCTION — the alternative,
    /// hand-authoring a bait tag, silently produces an animal that ignores every baited trap the
    /// player can actually build.</para>
    ///
    /// <para>Only positive (non-<c>NOT</c>) tag filters are returned, and CARD filters are
    /// deliberately excluded: on the set traps those list the CATCH results (carcasses,
    /// <c>PartridgeTied*</c>), and folding them into a food pool would have a species happily
    /// eating other animals' catches — including, for a species whose own catch card is a live
    /// form, itself.</para></summary>
    public static CardTag[] VanillaBaitTags()
    {
        var tags = new List<CardTag>();
        foreach (var kind in Kinds)
        {
            var card = GameRegistry.GetByUid<CardData>(kind.SetUid);
            var filters = card?.InventoryFilter.TagFilters;
            if (filters == null) continue;

            foreach (var filter in filters)
                if (!filter.NOT && filter.Tag != null && !tags.Contains(filter.Tag))
                    tags.Add(filter.Tag);
        }
        return tags.ToArray();
    }

    // ------------------------------------------------------------------- entry point ---

    /// <summary>Applies the manifest's <c>Traps</c> section to <paramref name="agent"/>: stamps the
    /// six trap NPCStats, generates the catch + reset agent actions, joins the vanilla traps'
    /// <c>TriggerInteractors</c>, and mirrors the catch cards into the Triggered traps' inventory
    /// filters. Returns true when the species ended up joined to at least one trap.
    ///
    /// <para>Every vanilla mutation below is ADDITIVE and IDEMPOTENT: entries are matched by
    /// object identity before appending, nothing vanilla is ever removed, reordered, or rewritten,
    /// and re-running the whole phase (which happens on every <c>LoadMainGameData</c>) is a no-op
    /// after the first pass. This is the root CLAUDE.md §Vanilla Data Protection contract —
    /// "only modify vanilla objects when intentionally injecting mod content".</para></summary>
    public static bool Apply(NPCAgent agent, AnimalManifest m, DutyBuilder.SpeciesStats stats)
    {
        if (agent == null || m == null || !m.HasTraps) return false;

        if (!m.TrapsEnabled)
        {
            Log.Debug($"Animals: {m.SpeciesId}: Traps.Enabled=false — species ignores all traps");
            return false;
        }

        var trapTypeStat = ResolveStat(StatTrapType, m);
        if (trapTypeStat == null)
        {
            Log.Error($"Animals: {m.SourceFile}: vanilla NPCStat '{StatTrapType}' not found — traps skipped for this species");
            return false;
        }

        // The whole catch gate depends on the by-NAME lookup above returning the SAME SO instance
        // the trap cards stamp by GUID: InGameNPC keys NPCStatsDict by SO instance, so a
        // WarpResolver-created duplicate would make every gate read 0 forever, silently.
        var byUid = GameRegistry.GetByUid<NPCStat>(TrapTypeStatUid);
        if (byUid != null && !ReferenceEquals(byUid, trapTypeStat))
        {
            Log.Error($"Animals: {m.SourceFile}: '{StatTrapType}' resolves to two different instances "
                    + $"(by-name #{trapTypeStat.GetInstanceID()}, by-UID #{byUid.GetInstanceID()}) — using the by-UID instance, "
                    + "which is the one the vanilla trap cards stamp");
            trapTypeStat = byUid;
        }

        StampTrapStats(agent, m, trapTypeStat);

        // The catch + reset actions must exist BEFORE the agent joins any TriggerInteractors:
        // a joined agent with no "Interact with a Trap" action springs traps and is never caught,
        // which reads in-game as "the trap ate my bait and did nothing".
        int actions = AttachTrapActions(agent, m, stats, trapTypeStat);
        if (actions == 0)
        {
            Log.Error($"Animals: {m.SourceFile}: could not generate the '{TrapActionId}' agent action — "
                    + "species NOT joined to the vanilla traps (it would spring them without ever being caught)");
            return false;
        }

        int joined = JoinTriggerInteractors(agent, m);

        // Mirror only AFTER the species has actually joined something. Mirroring first would
        // leave a failed species' catch cards appended to vanilla filters — a shared-vanilla
        // write on behalf of a species that ends up disabled.
        if (joined == 0) return false;
        int mirrored = MirrorCatchCards(m);

        AppliedCount++;
        Log.Info($"Animals: {m.SpeciesId}: traps armed — joined {joined}/{Kinds.Length} vanilla trap interactor list(s), "
               + $"{m.CatchResults.Count} catch result set(s), {mirrored} catch card(s) mirrored into triggered-trap filters, "
               + $"wariness {m.TrapWariness} (per-type: {DescribePerType(m)})");
        return true;
    }

    private static string DescribePerType(AnimalManifest m)
    {
        var parts = Kinds.Select(k =>
        {
            float v = m.TrapPerType.TryGetValue(k.Name, out var set) ? (float)set : ImmuneCunning;
            return $"{k.Name}={(v >= ImmuneCunning ? "immune" : v.ToString("0.#"))}";
        });
        return string.Join(" ", parts);
    }

    // ------------------------------------------------------------------- agent stats ---

    /// <summary>Adds the six trap NPCStats to the agent as per-agent <see cref="NPCStatInstance"/>
    /// overrides, mirroring how vanilla <c>Agent_Partridge1</c> authors them. Idempotent by
    /// ModelStat: a stat the hand-authored agent already declares is LEFT ALONE (the author's
    /// value wins over the manifest's, matching the Ref-path contract everywhere else in this
    /// system).</summary>
    private static void StampTrapStats(NPCAgent agent, AnimalManifest m, NPCStat trapTypeStat)
    {
        var existing = (agent.AgentStats ?? Array.Empty<NPCStatInstance>()).ToList();
        bool Has(NPCStat s) => s != null && existing.Any(i => i.ModelStat == s);

        int added = 0;

        // AgentTrapType: the stamp target. Range must span every trap's stamp value (max 40) or
        // SetStatValue clamps the stamp away and the catch gate never matches.
        if (!Has(trapTypeStat))
        {
            if (AddStat(existing, trapTypeStat, start: 0f, minMax: new Vector2(0f, 100f))) added++;
        }
        else
        {
            // The author's own declaration wins, but if they capped its range below the highest
            // stamp this species can receive, the stamp is clamped away and NOTHING works — with
            // no other symptom. Warn rather than silently deferring.
            int highestStamp = Kinds
                .Where(k => m.TrapPerType.TryGetValue(k.Name, out var c) && c < ImmuneCunning)
                .Select(k => k.TypeStamp)
                .DefaultIfEmpty(0)
                .Max();
            var declared = existing.First(i => i.ModelStat == trapTypeStat);
            if (TryGetDeclaredMax(declared, out float declaredMax) && declaredMax < highestStamp)
                Log.Error($"Animals: {m.SourceFile}: the agent declares '{StatTrapType}' with a maximum of {declaredMax}, "
                        + $"but this species opts into a trap that stamps {highestStamp}. SetStatValue clamps to the maximum, so the "
                        + "catch gate can never match and every catch would drop the fallback card. Raise the stat's OverrideMinMaxValue.");
            else
                Log.Debug($"Animals: {m.SpeciesId}: agent already declares '{StatTrapType}' — keeping the author's stat instance");
        }

        var cunningBase = ResolveStat(StatCunningBase, m);
        if (cunningBase != null && !Has(cunningBase)
            && AddStat(existing, cunningBase, start: (float)m.TrapWariness, minMax: new Vector2(0f, 100f))) added++;

        foreach (var kind in Kinds)
        {
            if (!CunningStatByKind.TryGetValue(kind.Name, out var statName)) continue;
            var stat = ResolveStat(statName, m);
            if (stat == null || Has(stat)) continue;

            // Absent from PerType = immune, matching vanilla's opt-in convention (a species is
            // only catchable by the traps its author named).
            float value = m.TrapPerType.TryGetValue(kind.Name, out var v) ? (float)v : ImmuneCunning;
            if (AddStat(existing, stat, start: value, minMax: new Vector2(value, value))) added++;
        }

        if (added > 0)
        {
            agent.AgentStats = existing.ToArray();
            Log.Debug($"Animals: {m.SpeciesId}: {added} trap NPCStat instance(s) stamped ({agent.AgentStats.Length} total on agent)");
        }
    }

    private static FieldInfo _overrideStartField;
    private static FieldInfo _overrideMinMaxField;

    private static bool AddStat(List<NPCStatInstance> into, NPCStat stat, float start, Vector2 minMax)
    {
        if (stat == null) return false;
        var instance = new NPCStatInstance { ModelStat = stat };

        _overrideStartField ??= AccessTools.Field(typeof(NPCStatInstance), "OverrideStartingValue");
        _overrideMinMaxField ??= AccessTools.Field(typeof(NPCStatInstance), "OverrideMinMaxValue");
        if (_overrideStartField == null || _overrideMinMaxField == null)
        {
            Log.Error("Animals: NPCStatInstance override fields not found — trap stats cannot be stamped");
            return false;
        }

        _overrideStartField.SetValue(instance, new OptionalFloatValue(true, start));
        _overrideMinMaxField.SetValue(instance, new OptionalRangeValue(true, minMax.x, minMax.y));
        into.Add(instance);
        return true;
    }

    /// <summary>Reads an <see cref="NPCStatInstance"/>'s OverrideMinMaxValue maximum, if the
    /// author set one. False = no override, so the stat's own SO default applies.</summary>
    private static bool TryGetDeclaredMax(NPCStatInstance instance, out float max)
    {
        max = 0f;
        try
        {
            _overrideMinMaxField ??= AccessTools.Field(typeof(NPCStatInstance), "OverrideMinMaxValue");
            // OptionalValue.Active is protected; the type carries an implicit bool operator that
            // exposes exactly that flag.
            if (_overrideMinMaxField?.GetValue(instance) is not OptionalRangeValue range || !range) return false;
            max = range.MaxValue;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug($"Animals: could not read OverrideMinMaxValue off an NPCStatInstance: {Log.ExceptionText(ex)}");
            return false;
        }
    }

    private static NPCStat ResolveStat(string name, AnimalManifest m)
    {
        if (Database.GetTypedSO(typeof(NPCStat), name) is NPCStat stat) return stat;
        Log.Warn($"Animals: {m.SourceFile}: vanilla NPCStat '{name}' not found — that trap facet is unavailable for this species");
        return null;
    }

    // ----------------------------------------------------------------- agent actions ---

    /// <summary>Generates the two agent actions a trappable species needs and appends them
    /// idempotently (by ActionID):
    /// <list type="bullet">
    /// <item>"Interact with a Trap" — the request target the trap fires on a successful catch
    /// roll. Drops the catch card INSIDE the sprung trap, retires the agent, and parks it in the
    /// Spirit World so the world doesn't keep a caught animal walking around.</item>
    /// <item>A paired reset action that returns AgentTrapType to 0 — without it the stamp
    /// persists and any trap-type-gated action re-fires every tick (see class remarks).</item>
    /// </list>
    /// Returns the number of actions the agent ends up owning of these two.</summary>
    private static int AttachTrapActions(NPCAgent agent, AnimalManifest m, DutyBuilder.SpeciesStats stats, NPCStat trapTypeStat)
    {
        var existing = (agent.AgentActions ?? Array.Empty<NPCAction>()).ToList();
        bool HasId(string id) => existing.Any(a => a != null && string.Equals(a.ActionID, id, StringComparison.Ordinal));

        int owned = 0;

        if (HasId(TrapActionId))
        {
            // A hand-authored agent may already own it (Ref path). Respect the author's version.
            Log.Debug($"Animals: {m.SpeciesId}: agent already declares '{TrapActionId}' — keeping the hand-authored action");
            owned++;
        }
        else
        {
            var catchAction = BuildCatchAction(m, stats, trapTypeStat);
            if (catchAction != null) { existing.Add(catchAction); owned++; }
        }

        // NO standalone "reset trap type" agent action is generated. It would be eligible in the
        // pre-request CheckForActions pass and would zero the stamp before the catch action reads
        // it — see BuildCatchStatChanges. The successful-catch path clears the stamp itself; a
        // stamp left by a failed roll is cleared by AnimalLifecycleTicker.

        agent.AgentActions = existing.ToArray();
        return owned;
    }

    /// <summary>The catch action. Each manifest <c>CatchResults</c> entry becomes one drop
    /// collection whose weight is zero EXCEPT when <c>AgentTrapType</c> equals that trap's stamp
    /// value — the vanilla alive-vs-carcass mechanism
    /// (<c>NPCStatBasedDropChanceModifier.GetExtraWeight</c> returns <c>InterpWeightRange.x</c>
    /// in range and 0 out of range under <c>Add0Weight</c>).</summary>
    private static NPCAction BuildCatchAction(AnimalManifest m, DutyBuilder.SpeciesStats stats, NPCStat trapTypeStat)
    {
        var collections = new List<CardsDropCollection>();

        foreach (var result in m.CatchResults)
        {
            var kind = Kinds.FirstOrDefault(k => string.Equals(k.Name, result.TrapType, StringComparison.OrdinalIgnoreCase));
            if (kind == null)
            {
                Log.Warn($"Animals: {m.SourceFile}: CatchResults TrapType '{result.TrapType}' is not one of "
                       + $"{string.Join("/", Kinds.Select(k => k.Name))} — entry skipped");
                continue;
            }

            // ONE COLLECTION PER DROP, not one collection holding every drop. A drop collection
            // is an all-or-nothing bundle; the engine picks between COLLECTIONS by weight. Vanilla
            // encodes "snares take partridges alive" as competing collections (carcass -100 vs.
            // PartridgeTied +60/+40), so alive-vs-carcass has to be modelled the same way or the
            // animal would drop its live form AND its corpse together.
            //
            // The LAST drop is the FALLBACK, and it is the reason this mirrors vanilla's odd
            // base-weight layout instead of using a uniform base of 0. If every collection has
            // base weight 0 and all weight comes from an in-range NPCStat modifier, then any
            // read where the stamp is out of range leaves TotalValue == 0 — and
            // GameManager.cs:7594-7599 does NOT then drop nothing, it picks UNIFORMLY AT RANDOM
            // across every collection, ignoring all weights. That turns a missed gate into a
            // silent lottery (and, for a species opting into several trap types, lets a snare
            // drop the pit-trap card). Giving the fallback a non-zero base makes the out-of-range
            // read deterministic instead. Vanilla does exactly this: carcass base 100 with a
            // -100 in-range modifier, tied forms base 0 with +60/+40.
            int fallbackIndex = result.Drops.Count - 1;
            for (int di = 0; di < result.Drops.Count; di++)
            {
                var drop = result.Drops[di];
                bool isFallback = di == fallbackIndex;
                var card = GameRegistry.GetByUid<CardData>(drop.Card);
                if (card == null)
                {
                    Log.Warn($"Animals: {m.SourceFile}: CatchResults[{kind.Name}] card '{drop.Card}' not found — drop skipped");
                    continue;
                }
                // ctor sets Quantity = Vector2Int.one; the (0,0) default of a field-initialised
                // CardDrop would silently drop nothing (root CLAUDE.md §Forage Drop Injection).
                var cardDrop = new CardDrop(card)
                {
                    DropChance = new CardDropChance
                    {
                        Active = true,
                        BaseDropChance = 100f,
                        DurabilitiesModifiers = Array.Empty<DurabilityInterpolatedValue>(),
                        StatsModifiers = Array.Empty<StatInterpolatedValue>(),
                        CardsModifiers = Array.Empty<CardOrTagIndividualDropChanceModifier>(),
                        NPCStatsModifiers = Array.Empty<NPCStatInterpolatedValue>(),
                    },
                    DropsDurabilityMultipliers = Array.Empty<DurabilityInterpolatedValue>(),
                    DropsStatMultipliers = Array.Empty<StatInterpolatedValue>(),
                    AddedFlavours = Array.Empty<FlavourAndIntensitySetup>(),
                    AddedSpiceTags = Array.Empty<SpiceTag>(),
                };

                int weight = Math.Max(1, drop.Weight);

                // Fallback: base FallbackBaseWeight, and the in-range modifier ADJUSTS it to the
                // authored weight (delta may be negative). Others: base 0, in-range +weight.
                int baseWeight = isFallback ? FallbackBaseWeight : 0;
                int inRangeDelta = weight - baseWeight;

                var collection = NewCollection($"{m.SpeciesId}_{kind.Name}_{card.name}", baseWeight);
                collection.NPCStatsDropChanceModifiers = new[]
                {
                    new NPCStatBasedDropChanceModifier
                    {
                        Stat = trapTypeStat,
                        UseAssociatedAgent = true,
                        TargetAgent = null,
                        StatRange = new Vector2(kind.TypeStamp, kind.TypeStamp),
                        InterpWeightRange = new Vector2Int(inRangeDelta, inRangeDelta),
                        WhenOutOfRange = StatDropChanceModOutOfRange.Add0Weight,
                    },
                };
                SetPrivateDrops(collection, new[] { cardDrop });
                collections.Add(collection);
            }
        }

        if (collections.Count == 0)
        {
            Log.Warn($"Animals: {m.SourceFile}: Traps declared but no usable CatchResults — "
                   + "a caught animal would vanish leaving an empty sprung trap");
        }

        var action = new NPCAction
        {
            ActionID = TrapActionId,
            ActionLocalizedName = new LocalizedString { ParentObjectID = "", LocalizationKey = "IGNOREKEY", DefaultText = TrapActionId },
            ActionPopupText = default,
            // The trap sets CurrentActionRequest; only OnlyWhenTriggered actions consume it.
            RepeatOptions = NPCActionRepeatOptions.OnlyWhenTriggered,
            // Vanilla's equivalent is false. True would let an agent that happens to be in combat
            // when its trap springs consume the bait, keep the AgentTrapType stamp and produce
            // nothing — InGameNPC.cs:1710-1717 marks the request Performed BEFORE the combat
            // check and then continues, so the catch is simply lost with no log.
            CannotPerformWhileInCombat = false,
            CanBePerformedDuringTriggeredActions = new List<string>(),
            Conditions = AnimalAssetFactory.EmptyCondition(),
            StatModifications = Array.Empty<StatModifier>(),
            NPCStatModifications = BuildCatchStatChanges(stats, trapTypeStat),
            DroppedCards = collections.ToArray(),
            // Routes the drops into the sprung (transformed) trap rather than the open board.
            DropCardsInTriggerContainer = true,
            // MoveAfterOtherEffects — MUST NOT be left at the default MoveBeforeOtherEffects.
            // InGameNPC.cs:1786-1811 runs MoveNPCFromNPCAction BEFORE ToAction(), and the drop
            // path resolves its target environment from the agent's own card
            // (GameManager.cs:6914). Moving first registers the catch card against the Spirit
            // World while its container is still the trap in the player's environment — the card
            // ends up neither in the trap nor on the board. Vanilla sets 1 on every agent action
            // that both drops and moves.
            MoveTiming = NPCActionMoveTiming.MoveAfterOtherEffects,
            InventoryActions = Array.Empty<NPCInventoryAction>(),
            SetNPCHome = Array.Empty<NPCChangeHome>(),
            TriggerHidingGroups = Array.Empty<NPCHidingGroup>(),
            SpawnNPCS = Array.Empty<CustomAgentSpawning>(),
            DeleteNPCs = Array.Empty<DeleteNPC>(),
            MoveToEnvironment = GameRegistry.GetByUid<CardData>(SpawnRegistrar.SpiritWorldUid),
        };
        return action;
    }

    /// <summary>Retires the caught agent (exists → 0) and clears its own <c>AgentTrapType</c>
    /// stamp as part of the same action.
    ///
    /// <para><b>The stamp clear lives HERE rather than in a separate repeating agent action, and
    /// that placement is the fix for a real race.</b> On the tick a trap springs, the engine runs
    /// <c>CheckForActions()</c> TWICE: once immediately after the stat stamp is applied
    /// (<c>GameManager.cs:4364</c>, reached because <c>InGameNPCStat.ApplyInstantModifier</c> adds
    /// the agent to <c>npcActionsToCheck</c> for any non-zero delta), and again only after
    /// <c>CurrentActionRequest</c> is assigned (<c>GameManager.cs:4396</c>). A standalone
    /// <c>Repeat</c> reset action is eligible in that FIRST pass — its <c>LastPlayedTick</c> is
    /// still the previous tick — so it zeroes the stamp before the catch action ever reads it, and
    /// every trap-type gate on the drop collections evaluates out of range. Vanilla ships exactly
    /// such a standalone reset (<c>Agent_Partridge1</c> "Reset Agent Trap Type", unconditional
    /// <c>-100</c> every tick) and is subject to the same ordering, so copying vanilla here would
    /// have reproduced the bug rather than avoided it.</para>
    ///
    /// <para>Folding the clear into the catch action removes the racer entirely: the stamp is
    /// still standing when this action's own drop report is computed, and is cleared as part of
    /// the same action's effects. A stamp left behind by a FAILED catch roll is cleared instead by
    /// <see cref="AnimalLifecycleTicker"/> on the framework's own tick — outside the engine's
    /// action sweep, so it cannot race anything — which also prevents the stamp accumulating
    /// (20 + 20 = 40 would select a different trap's catch card entirely).</para>
    ///
    /// <para>Deliberately does NOT touch blood — a trapped animal is removed, not wounded, and
    /// the generator invariant is that duty-gate stats have exactly one writer. Note this also
    /// means the framework's kill-respawn timer (which is keyed on blood) does NOT bring a
    /// trapped species back; <see cref="AnimalValidator"/> rejects a trappable manifest that has
    /// no <c>Spawn.SuppressWhileCardOnBoard</c> respawn path for exactly that reason.</para></summary>
    private static NPCStatInstantModifier[] BuildCatchStatChanges(DutyBuilder.SpeciesStats stats, NPCStat trapTypeStat)
    {
        var changes = new List<NPCStatInstantModifier>();

        if (stats?.Exists != null)
            changes.Add(new NPCStatInstantModifier
            {
                TargetStat = stats.Exists,
                UseAssociatedAgent = true,
                TargetAgent = null,
                ValueChange = new Vector2(-1f, -1f),
                IgnoreNovelty = false,
            });

        if (trapTypeStat != null)
            changes.Add(new NPCStatInstantModifier
            {
                TargetStat = trapTypeStat,
                UseAssociatedAgent = true,
                TargetAgent = null,
                // Clamped at the stat's minimum (0).
                ValueChange = new Vector2(-999999f, -999999f),
                IgnoreNovelty = true,
            });

        return changes.ToArray();
    }

    /// <summary>Base weight carried by the FALLBACK collection so an out-of-range
    /// <c>AgentTrapType</c> read yields a deterministic drop instead of the engine's
    /// uniform-random <c>TotalValue == 0</c> path (<c>GameManager.cs:7594-7599</c>).
    /// Matches vanilla's carcass base of 100.</summary>
    private const int FallbackBaseWeight = 100;

    private static CardsDropCollection NewCollection(string name, int baseWeight) => new()
    {
        CollectionName = name,
        CountsAsSuccess = true,
        RevealInventory = false,
        // (0,0) matches vanilla's trap collections — a non-zero CollectionUses becomes a
        // one-catch-per-card limiter if NPC-action collections ever get registered.
        CollectionUses = Vector2Int.zero,
        CollectionWeight = baseWeight,
        StatsDropChanceModifiers = Array.Empty<StatBasedDropChanceModifier>(),
        CardDropChanceModifiers = Array.Empty<CardBasedDropChanceModifier>(),
        StatModifications = Array.Empty<ConditionalStatModifier>(),
    };

    private static FieldInfo _droppedCardsField;

    private static void SetPrivateDrops(CardsDropCollection collection, CardDrop[] drops)
    {
        _droppedCardsField ??= AccessTools.Field(typeof(CardsDropCollection), "DroppedCards");
        if (_droppedCardsField == null)
        {
            Log.Error("Animals: CardsDropCollection.DroppedCards field not found — trap catch drops will be empty");
            return;
        }
        _droppedCardsField.SetValue(collection, drops);
    }

    // -------------------------------------------------- vanilla TriggerInteractors ---

    /// <summary>THE shared-vanilla mutation. Appends one <see cref="NPCOrNPCTagRef"/> naming this
    /// agent to each vanilla land trap's <c>OnInteractAction.TriggerInteractors</c> array.
    ///
    /// <para>Safety contract, enforced here rather than assumed:
    /// <list type="bullet">
    /// <item>Append-only — the existing 20 vanilla entries are copied verbatim and never
    /// reordered, rewritten, or removed.</item>
    /// <item>Idempotent by object identity — if any entry already targets this exact agent SO the
    /// trap is skipped, so re-running the load phase (every <c>LoadMainGameData</c>) cannot grow
    /// the list.</item>
    /// <item>Dirty-tracked — only traps the species actually opted into (a non-immune per-type
    /// cunning) are touched at all.</item>
    /// <item>Fail-soft — a missing trap card or a null action list logs and skips that trap
    /// instead of throwing, so one game-update rename cannot take the whole load down.</item>
    /// </list></para></summary>
    private static int JoinTriggerInteractors(NPCAgent agent, AnimalManifest m)
    {
        int joined = 0;

        foreach (var kind in Kinds)
        {
            // Opt-in only: an immune (or unlisted) trap type is left completely untouched.
            float cunning = m.TrapPerType.TryGetValue(kind.Name, out var v) ? (float)v : ImmuneCunning;
            if (cunning >= ImmuneCunning) continue;

            var card = GameRegistry.GetByUid<CardData>(kind.SetUid);
            if (card == null)
            {
                Log.Warn($"Animals: {m.SourceFile}: vanilla {kind.Name} trap ({kind.SetUid}) not found — species not joined to it");
                continue;
            }

            // NOTE: OnInteractActions is a List<OnInteractAction>, NOT an array — the same shape
            // trap as DismantleActions (root CLAUDE.md §Runtime DismantleAction Injection).
            var actions = card.OnInteractActions;
            if (actions == null || actions.Count == 0)
            {
                Log.Warn($"Animals: {m.SourceFile}: vanilla {kind.Name} trap has no OnInteractActions — species not joined to it");
                continue;
            }

            bool touched = false;
            foreach (var action in actions)
            {
                if (action == null) continue;

                // Join ONLY the action that actually routes a catch to the agent. All four traps
                // currently carry exactly one OnInteractAction, so this is inert today — but if a
                // game update adds a second one with a deliberately narrow interactor allowlist,
                // an unfiltered loop would silently widen it.
                if (!string.Equals(action.TriggerActionInAgent, TrapActionId, StringComparison.Ordinal)) continue;

                var current = action.TriggerInteractors ?? Array.Empty<NPCOrNPCTagRef>();
                if (current.Any(r => ReferenceEquals(r.Target, agent)))
                {
                    touched = true;   // already joined on a previous load — still counts as joined
                    continue;
                }

                var grown = new NPCOrNPCTagRef[current.Length + 1];
                Array.Copy(current, grown, current.Length);
                grown[current.Length] = new NPCOrNPCTagRef { Target = agent, NOT = false };
                action.TriggerInteractors = grown;
                touched = true;

                Log.Debug($"Animals: {m.SpeciesId}: joined {kind.Name} '{action.ActionName.DefaultText}' "
                        + $"TriggerInteractors ({current.Length} -> {grown.Length})");
            }

            if (touched) joined++;
        }

        return joined;
    }

    // ------------------------------------------------ triggered-trap filter mirror ---

    /// <summary>Mirrors each catch card into the matching Triggered trap's
    /// <c>InventoryFilter.AcceptedCards</c>, exactly as vanilla does for its own catches
    /// (SnareTrapTriggered accepts the four small-game carcasses plus <c>PartridgeTied*</c>).
    ///
    /// <para><b>This mirror is REQUIRED, not cosmetic — the long-standing "does the drop path
    /// bypass the filter?" question is now settled: it does NOT.</b> The chain is
    /// <c>NPCAction.ToAction</c> (sets <c>PotentialContainers = [the sprung trap]</c> when
    /// <c>DropCardsInTriggerContainer</c>) → <c>GameManager.AddCard</c> (accepts the first
    /// container whose <c>GetIndexForInventory</c> is >= 0) →
    /// <c>InGameCardBase.GetIndexForInventory</c> (<c>InGameCardBase.cs:2637</c>, returns -1 when
    /// <c>CanReceiveInInventory</c> is false) → <c>CanReceiveInInventory</c>
    /// (<c>InGameCardBase.cs:3003</c>: <c>CompleteInventoryFilter.SupportsCard</c>). A catch card
    /// missing from this list does not error — the drop silently falls through to the board
    /// instead of landing in the trap. Vanilla has exactly that bug: <c>PartridgeTiedMale</c> is
    /// a valid snare drop but is absent from <c>SnareTrapTriggered</c>'s accepted list (which
    /// lists <c>PartridgeTiedFemale</c> twice), so a trapped male partridge lands beside the trap
    /// rather than inside it.</para>
    ///
    /// <para><c>CardFilter</c> is a struct field, so this is a read-modify-write, followed by a
    /// defensive clear of the memoized <c>CardData.CachedFilter</c> (class remarks).</para></summary>
    private static int MirrorCatchCards(AnimalManifest m)
    {
        int mirrored = 0;

        foreach (var result in m.CatchResults)
        {
            var kind = Kinds.FirstOrDefault(k => string.Equals(k.Name, result.TrapType, StringComparison.OrdinalIgnoreCase));
            if (kind == null) continue;

            // Only mirror for trap types this species actually opted into — a CatchResults entry
            // for an immune type is dead config and must not touch that vanilla trap card.
            float cunning = m.TrapPerType.TryGetValue(kind.Name, out var v) ? (float)v : ImmuneCunning;
            if (cunning >= ImmuneCunning) continue;

            var triggered = GameRegistry.GetByUid<CardData>(kind.TriggeredUid);
            if (triggered == null)
            {
                Log.Warn($"Animals: {m.SourceFile}: vanilla {kind.Name}Triggered ({kind.TriggeredUid}) not found — catch cards not mirrored into its filter");
                continue;
            }

            var cards = result.Drops
                .Select(d => GameRegistry.GetByUid<CardData>(d.Card))
                .Where(c => c != null)
                .ToArray();
            if (cards.Length == 0) continue;

            var filter = triggered.InventoryFilter;
            var accepted = (filter.AcceptedCards ?? Array.Empty<CardData>()).ToList();
            int before = accepted.Count;

            foreach (var card in cards)
                if (!accepted.Contains(card)) accepted.Add(card);

            if (accepted.Count == before) continue;

            filter.AcceptedCards = accepted.ToArray();
            triggered.InventoryFilter = filter;
            ClearFilterCache(triggered);
            mirrored += accepted.Count - before;

            Log.Debug($"Animals: {m.SpeciesId}: mirrored {accepted.Count - before} catch card(s) into {kind.Name}Triggered "
                    + $"InventoryFilter.AcceptedCards ({before} -> {accepted.Count})");
        }

        return mirrored;
    }

    /// <summary>Resets <c>CardData.CachedFilter</c> so a later <c>CompleteInventoryFilter</c> read
    /// rebuilds from the mutated <c>InventoryFilter</c>. Breadcrumbed rather than silent (D17):
    /// if the field is ever renamed the mirror silently stops taking effect, which would look
    /// like "the trap rejects the catch card" with no other symptom.</summary>
    private static void ClearFilterCache(CardData card)
    {
        try
        {
            _cachedFilterField ??= AccessTools.Field(typeof(CardData), "CachedFilter");
            if (_cachedFilterField == null)
            {
                Log.Debug($"Animals: CardData.CachedFilter not found — filter mirror on '{card.name}' may be masked by the memoized complete filter");
                return;
            }
            _cachedFilterField.SetValue(card, default(CardFilter));
        }
        catch (Exception ex)
        {
            Log.Debug($"Animals: clearing CachedFilter on '{card?.name}' failed: {Log.ExceptionText(ex)}");
        }
    }
}
