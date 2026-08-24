using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// M3 — compiles a manifest <c>Tracks</c> section into the species' <c>NPCAgent.DefaultTracks</c>
/// (<see cref="NPCTrackingSetup"/>) and owns the <c>LeaveTracks</c> policy for the move actions
/// <see cref="DutyBuilder"/> generates. Schema reference: Documentation/Design/Animals_Schema.md
/// §Tracks; author cookbook: Documentation/CSFF_Patterns.md §Adding a Roaming Animal.
///
/// <para><b>Engine pipeline this builds into</b> (decompile citations are against
/// <c>.decomp/</c>, EA 0.66h):</para>
/// <list type="number">
/// <item><b>Lay.</b> <c>MoveDutyAction.PickDestinationAndMoveThere</c>
/// (<c>MoveDutyAction.cs:114-145</c>) — when the move action has <c>LeaveTracks</c> AND the
/// selected setup has <c>Active</c>, it appends a <c>new NPCTrackingInfo(setup, npc, name)</c> to
/// the <b>ORIGIN</b> environment's <c>EnvironmentSaveData.NPCTracks</c> (the env the animal is
/// LEAVING — <c>CurrentEnvironment</c> is read before the move). Setup selection is
/// <c>OverrideAgentDefaultTracks.Active ? Override : NPCModel.DefaultTracks</c>, so
/// <c>NPCAgent.DefaultTracks</c> is the per-species knob and per-action overrides are unused by
/// vanilla (0 of 94 track-leaving move exports override).</item>
/// <item><b>Discover.</b> <c>CheckForTracks</c> (the LIVE copy is
/// <c>EnvironmentSaveDataByReference.cs:464-514</c>; <c>EnvironmentSaveData.cs:470-520</c> is a
/// byte-identical serialization-variant twin — reason about both) rolls each pending record
/// exactly once, from exactly two triggers: the player ARRIVING in that env
/// (<c>GameManager.cs:10265</c>), and a track laid in the player's current env. The record is
/// consumed in every branch — expired, failed, or found. There is no second chance and no
/// re-roll on a later visit.</item>
/// <item><b>Spawn.</b> A synthetic <c>CardAction("Spawn Agent Tracks", …)</c> places the card —
/// onto <c>GameManager.CurrentEnvironmentCard</c>, i.e. the PLAYER's board, NOT the env whose
/// save data was rolled (<c>EnvironmentSaveDataByReference.cs:512</c>). That is only safe
/// because both callers are already the player's env; anything that calls
/// <c>CheckForTracks</c> on a remote env would spawn its cards under the player's feet.
/// The global <c>TrackingSettings.TransferTracksLifetimeOntoDurability</c> (MaskValue 1) writes
/// the record's REMAINING lifetime onto the card's Spoilage ("Freshness")
/// (<c>GameManager.cs:7284-7299</c>), and the setup's <c>NPCStatsToTrackDurabilities</c>
/// snapshot injects (e.g. blood → Special1).</item>
/// </list>
///
/// <para><b>Novel-engine findings recorded while building this (M3, 2026-08-16) — each is a
/// silent or crashing failure that no log or compile step catches:</b></para>
/// <list type="bullet">
/// <item><b>A track-leaving agent with a null <c>WeightCategory</c> is a latent
/// NullReferenceException, not a cosmetic gap.</b> <c>NPCTrackingInfo.ToDescription</c>
/// (<c>NPCTrackingInfo.cs:209-217</c>) walks <c>TrackingSettings.TrackingInfo</c> and, once the
/// player's Skill_Tracking reaches the <c>Weight</c> step's threshold (2 in vanilla), calls
/// <c>LocalizedString.TrackingWeight(TrackedAgent.WeightCategory)</c>, which dereferences
/// <c>_Weight.CategoryName</c> with NO null guard (<c>LocalizedString.cs:2124-2127</c>). The
/// crash lands in the card-description path, i.e. when the player inspects the discovered track —
/// far from the manifest that caused it. <see cref="AnimalValidator"/> therefore rejects a
/// <c>Tracks.Enabled</c> species with no resolvable WeightCategory, and this builder degrades to
/// Active=false rather than arming the crash.</item>
/// <item><b><c>TrackLifetime</c> of 0 means "never expires", not "expires immediately".</b>
/// <c>CheckForTracks</c> guards its expiry branch with <c>… &amp;&amp; TrackLifetime &gt; 0</c>
/// (<c>EnvironmentSaveData.cs:481</c>), so a 0 rolled lifetime leaves an immortal pending record
/// in the env's save data. The validator enforces Min &gt;= 1. Note the roll is
/// <c>Random.Range(x, y + 1)</c> — INCLUSIVE of Max (<c>NPCTrackingInfo.cs:70</c>).</item>
/// <item><b>There is no per-species reveal SKILL, and no "hidden at low skill" by default.</b>
/// The skill is global (<c>TrackingSettings.TrackingStat</c> = Skill_Tracking) and its base curve
/// already yields <b>50% at skill 0</b> rising to 100% at 150. The only per-species lever is
/// additive: <c>AddedChanceToDiscoverTracks</c> (a flat Vector2 rolled per track) and
/// <c>AddedStatChanceToDiscover</c> (a stat-driven <c>InterpolatedValue</c>). A species can only
/// be made HARDER to find than vanilla by emitting a <b>negative</b> curve — which is what
/// <c>RevealSkillCurve</c> compiles to. Both ends are deterministic:
/// <c>TrackingAttemptReport.Success</c> returns false at TotalChance &lt;= 0 and true at &gt;= 100
/// (<c>TrackingAttemptReport.cs:49-64</c>), so a curve crossing zero gives a crisp, testable
/// "invisible at low skill / guaranteed at high skill". This is not an invention: vanilla
/// <c>Agent_ForestBeast</c> is the precedent, using five negative Moonlight-keyed
/// <c>AddedStatChanceToDiscover</c> entries (down to -100) to make its tracks undiscoverable in
/// the dark.</item>
/// <item><b>There is no leave-FREQUENCY knob in the engine.</b> One track per qualifying move,
/// unconditionally. The only real frequency control an author has is WHICH duties carry
/// <c>LeaveTracks</c> — exposed as the per-action <c>LeaveTracks</c> flag in the
/// <c>CustomDuties</c> DSL, with <c>Tracks.Enabled</c> as the species-wide master switch.</item>
/// <item><b>Tracks land where the animal CAME FROM.</b> A species whose only movement is
/// "teleport to the player" therefore drops its tracks in envs the player has already left, and
/// they are only rolled when the player comes BACK. An off-stage roost (Spirit World) is
/// relocated by <see cref="AnimalLifecycleTicker"/> via a direct <c>MoveNPC</c>, which is not a
/// duty action and so never lays a track — Spirit World tracks are impossible by
/// construction.</item>
/// </list>
/// </summary>
internal static class TrackBuilder
{
    /// <summary>Vanilla <c>Skill_Tracking</c> GameStat — the global tracking skill
    /// (<c>TrackingSettings.TrackingStat</c>). Default for <c>Tracks.RevealSkill</c>.</summary>
    public const string SkillTrackingUid = "e83cdd915095bf84b97045e3c3bb6ef5";

    /// <summary>Vanilla <c>DefaultTracksCard</c> — used when a manifest omits
    /// <c>Tracks.TrackCard</c> (the engine falls back to it via
    /// <c>TrackingSettings.GetTracksCard</c> when the record carries no card override).</summary>
    public const string DefaultTracksCardUid = "61c72034eb78c95489d2e70d7fb808f9";

    /// <summary>Skill_Tracking's authored range, used as the default
    /// <c>RevealSkillCurve.StatRange</c>.</summary>
    private const float SkillTrackingMax = 150f;

    /// <summary>Number of species that ended the load with <c>DefaultTracks.Active == true</c>
    /// because of their manifest. Reported once per load by <see cref="AnimalService"/>.</summary>
    public static int AppliedCount { get; private set; }

    /// <summary>Agent UniqueID → SpeciesId, and agent SO name → SpeciesId, for the two diagnostic
    /// hooks below. <c>OnTracksSpawned</c> hands us the agent UID; <c>OnTrackingAttempt</c> only
    /// hands us a TrackID string shaped <c>{agentName}_{env}_{actionID}_{tick}</c>
    /// (<c>NPCTrackingInfo.GenerateTrackID</c>), so species attribution there is prefix matching
    /// on the agent's SO name.</summary>
    private static readonly Dictionary<string, string> _speciesByAgentUid = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> _speciesByAgentName = new(StringComparer.Ordinal);
    private static bool _diagnosticsSubscribed;

    public static void Reset()
    {
        AppliedCount = 0;
        _speciesByAgentUid.Clear();
        _speciesByAgentName.Clear();
    }

    /// <summary>The <c>LeaveTracks</c> value the generated seek/wander move actions should carry.
    /// Default is true (matching vanilla, where 94 of 159 move exports leave tracks and the real
    /// gate is <c>DefaultTracks.Active</c>); an explicit <c>"Tracks": { "Enabled": false }</c>
    /// turns the flag off at the action level as well, so a species that opts out costs nothing
    /// at move time. A manifest with no <c>Tracks</c> section keeps the pre-M3 behavior
    /// (LeaveTracks true, gated by whatever the agent's own DefaultTracks says).</summary>
    public static bool LeaveTracksOnGeneratedMoves(AnimalManifest m) => !m.HasTracks || m.TracksEnabled;

    /// <summary>The <c>LeaveTracks</c> value for a <c>CustomDuties</c> Move action: the author's
    /// per-action flag, force-cleared when the species opted out of tracks entirely.</summary>
    public static bool LeaveTracksOnCustomMove(AnimalManifest m, bool authored)
        => authored && LeaveTracksOnGeneratedMoves(m);

    /// <summary>Writes the manifest's <c>Tracks</c> section onto <paramref name="agent"/>'s
    /// <c>DefaultTracks</c>. A manifest with NO <c>Tracks</c> section leaves the agent's own
    /// authored setup untouched (Ref-path agents may configure tracks in their own JSON);
    /// a manifest WITH one is authoritative and overwrites it. Returns true when the species
    /// ends up actively leaving tracks.</summary>
    public static bool Apply(NPCAgent agent, AnimalManifest m, DutyBuilder.SpeciesStats stats)
    {
        if (agent == null || m == null || !m.HasTracks) return false;

        if (!m.TracksEnabled)
        {
            var off = agent.DefaultTracks;
            off.Active = false;
            agent.DefaultTracks = off;
            Log.Debug($"Animals: {m.SpeciesId}: Tracks.Enabled=false — agent leaves no tracks");
            return false;
        }

        // The description path NREs on a null WeightCategory once the player's tracking skill
        // reaches the Weight info step (see the class doc-comment). Refuse rather than arm it.
        if (agent.WeightCategory == null)
        {
            Log.Error($"Animals: {m.SourceFile}: Tracks.Enabled requires a resolvable WeightCategory on the agent "
                    + "(LocalizedString.TrackingWeight dereferences it with no null check once Skill_Tracking >= 2) — tracks disabled for this species");
            var guarded = agent.DefaultTracks;
            guarded.Active = false;
            agent.DefaultTracks = guarded;
            return false;
        }

        var setup = new NPCTrackingSetup
        {
            Active = true,
            TrackLifetime = new Vector2Int(m.TrackLifetimeMin, m.TrackLifetimeMax),
            AddedChanceToDiscoverTracks = new Vector2((float)m.TrackAddedDiscoverMin, (float)m.TrackAddedDiscoverMax),
            AddedStatChanceToDiscover = BuildRevealCurve(m),
            OverrideDefaultTracksCard = ResolveTrackCard(m),
            // Overwritten at lay time from the actual path (MoveDutyAction.cs:118-129) — the
            // authored value is never read, so any member of the enum is equally correct here.
            TracksDirection = CardinalDirections.North,
            // AddToArray: keep the global TrackingSettings.NPCStatsToTrackDurabilities transfers
            // and ADD the species' own (OverrideArray would silently drop the vanilla ones).
            OverrideTrackDurabilities = ArrayOverrideOptions.AddToArray,
            NPCStatsToTrackDurabilities = BuildDurabilityTransfers(m, stats),
        };

        agent.DefaultTracks = setup;
        AppliedCount++;
        if (!string.IsNullOrEmpty(agent.UniqueID)) _speciesByAgentUid[agent.UniqueID] = m.SpeciesId;
        if (!string.IsNullOrEmpty(agent.name)) _speciesByAgentName[agent.name] = m.SpeciesId;
        SubscribeDiagnostics();

        string curveText = setup.AddedStatChanceToDiscover.Length > 0
            ? $"{m.TrackRevealChanceMin}..{m.TrackRevealChanceMax} pts over skill {m.TrackRevealStatMin}-{m.TrackRevealStatMax}"
            : "none (vanilla 50%-at-skill-0 curve only)";
        Log.Info($"Animals: {m.SpeciesId}: tracks armed — card '{(setup.OverrideDefaultTracksCard != null ? setup.OverrideDefaultTracksCard.UniqueID : "vanilla DefaultTracksCard")}', "
               + $"lifetime {setup.TrackLifetime.x}-{setup.TrackLifetime.y} ticks, reveal curve {curveText}, "
               + $"blood trail {(setup.NPCStatsToTrackDurabilities.Length > 0 ? "on" : "off")}");
        return true;
    }

    // ------------------------------------------------------------- diagnostics ---

    /// <summary>Subscribes the two static tracking events, which have ZERO vanilla subscribers —
    /// meaning that without this, laying and rolling a track produce no log output whatsoever and
    /// "I never found owl tracks" is indistinguishable from "the owl never moved", "the roll
    /// failed", and "the record expired". Both hooks are filtered to framework-generated species.
    ///
    /// <para>Info level during M3 bring-up per the Animal-system process rule (diagnostics at
    /// LogInfo while a milestone is unverified, demoted to LogDebug at milestone close) — these
    /// are the lines the in-game acceptance pass reads. Demote once tracks are confirmed.</para></summary>
    private static void SubscribeDiagnostics()
    {
        if (_diagnosticsSubscribed) return;
        GameManager.OnTracksSpawned += OnTracksSpawned;
        GameManager.OnTrackingAttempt += OnTrackingAttempt;
        _diagnosticsSubscribed = true;
        Log.Debug("Animals: track diagnostics subscribed (OnTracksSpawned + OnTrackingAttempt)");
    }

    /// <summary>Stage 1 evidence: the animal LAID a track (record stored on the env it left).</summary>
    private static void OnTracksSpawned(NPCTrackingInfo info, EnvID env)
    {
        try
        {
            if (info == null || !_speciesByAgentUid.TryGetValue(info.TrackNPCID ?? "", out var species)) return;
            Log.Info($"Animals: {species}: LEFT TRACKS in '{env.SimpleEnvName}' — lifetime {info.TrackLifetime} ticks, "
                   + $"added chance {info.AddedChanceToDiscover:0.#} pts, direction {info.Direction}, id '{info.TrackID}'");
        }
        catch (Exception ex)
        {
            Log.Debug($"Animals: OnTracksSpawned diagnostic failed (agent '{info?.TrackNPCID}'): {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>Stage 2 evidence: the player ROLLED against a pending record. Fires on every
    /// branch, including the deterministic TotalChance &lt;= 0 skip (where Roll is still -1), so
    /// this is the line that proves the low-skill gate is doing what the manifest asked.</summary>
    private static void OnTrackingAttempt(TrackingAttemptReport report)
    {
        try
        {
            var species = SpeciesForTrackId(report.TracksID);
            if (species == null) return;
            string outcome = report.TotalChance <= 0f ? "SKIPPED (chance <= 0, track discarded unseen)"
                           : report.Success ? "FOUND" : "MISSED";
            Log.Info($"Animals: {species}: TRACKING ROLL — skill curve {report.TrackingStatChance:0.#} + added {report.AddedTrackingChance:0.#} "
                   + $"= total {report.TotalChance:0.#}%, roll {report.Roll:0.#} → {outcome} (id '{report.TracksID}')");
        }
        catch (Exception ex)
        {
            Log.Debug($"Animals: OnTrackingAttempt diagnostic failed (id '{report.TracksID}'): {Log.ExceptionText(ex)}");
        }
    }

    /// <summary>TrackID is <c>{agentName}_{env}_{actionID}_{tick}</c>; the agent's SO name is the
    /// only species handle in it. Matches the longest registered agent name that prefixes the id
    /// so one species' name can't shadow another's by being a prefix of it.</summary>
    private static string SpeciesForTrackId(string trackId)
    {
        if (string.IsNullOrEmpty(trackId) || _speciesByAgentName.Count == 0) return null;
        string best = null, bestName = null;
        foreach (var kv in _speciesByAgentName)
        {
            if (!trackId.StartsWith(kv.Key, StringComparison.Ordinal)) continue;
            if (bestName == null || kv.Key.Length > bestName.Length) { bestName = kv.Key; best = kv.Value; }
        }
        return best;
    }

    // ------------------------------------------------------------------ pieces ---

    /// <summary>Resolves <c>Tracks.TrackCard</c>. Null (omitted) is legal and means "use the
    /// vanilla DefaultTracksCard" — the engine substitutes it whenever the record carries no
    /// card override (<c>TrackingSettings.GetTracksCard</c>, <c>TrackingSettings.cs:62-78</c>),
    /// so we deliberately leave the field null rather than resolving the vanilla UID here.</summary>
    private static CardData ResolveTrackCard(AnimalManifest m)
    {
        if (string.IsNullOrEmpty(m.TrackCard)) return null;

        var card = GameRegistry.GetByUid<CardData>(m.TrackCard);
        if (card == null)
        {
            // Validator already rejects an unresolvable UID; this is the belt-and-braces path.
            Log.Warn($"Animals: {m.SourceFile}: Tracks.TrackCard '{m.TrackCard}' not found — falling back to the vanilla DefaultTracksCard");
            return null;
        }
        if ((int)card.CardType != 2)
            Log.Warn($"Animals: {m.SourceFile}: Tracks.TrackCard '{card.name}' is CardType {(int)card.CardType}; vanilla track cards are CardType 2 (placed structure) — the spawn may behave unexpectedly");
        return card;
    }

    /// <summary>Compiles <c>Tracks.RevealSkill</c> + <c>Tracks.RevealSkillCurve</c> into the one
    /// <c>StatInterpolatedValue</c> the engine ADDS to its global discovery curve. Returns an
    /// empty (never null) array when the manifest asks for no curve — the ctor null-guards, but
    /// the generator invariant is "materialized, never null".</summary>
    private static StatInterpolatedValue[] BuildRevealCurve(AnimalManifest m)
    {
        if (!m.HasTrackRevealCurve) return Array.Empty<StatInterpolatedValue>();

        var skill = ResolveRevealSkill(m);
        if (skill == null)
        {
            Log.Warn($"Animals: {m.SourceFile}: Tracks.RevealSkill '{m.TrackRevealSkill ?? "Skill_Tracking"}' not found — reveal curve skipped (vanilla discovery curve only)");
            return Array.Empty<StatInterpolatedValue>();
        }

        return new[]
        {
            new StatInterpolatedValue
            {
                InputStat = skill,
                UseStatPercentage = false,
                Value = new InterpolatedValue
                {
                    Active = true,
                    InputValueRange = new Vector2((float)m.TrackRevealStatMin, (float)m.TrackRevealStatMax),
                    OutputValueRange = new Vector2((float)m.TrackRevealChanceMin, (float)m.TrackRevealChanceMax),
                    // Clamp to the end values outside the range instead of collapsing to 0 —
                    // Return0Value would make a NEGATIVE curve silently stop penalising above
                    // the authored max, i.e. the opposite of the author's intent.
                    WhenOutOfRange = InterpolatedValueOutOfRange.UseMinMaxOutputValue,
                    OutOfRangeCustomValue = 0f,
                },
            },
        };
    }

    private static GameStat ResolveRevealSkill(AnimalManifest m)
    {
        var uidOrName = m.TrackRevealSkill;
        if (string.IsNullOrEmpty(uidOrName))
            return GameRegistry.GetByUid<GameStat>(SkillTrackingUid);

        return GameRegistry.GetByUid<GameStat>(uidOrName)
            ?? Database.GetTypedSO(typeof(GameStat), uidOrName) as GameStat;
    }

    /// <summary>Compiles <c>Tracks.BloodTrail</c> into the boar-pattern NPCStat→durability
    /// snapshot: the agent's blood (0-100) is frozen onto the track card's Special1 at lay time,
    /// which is what drives the vanilla card's "Blood Trail" AlternateName (Special1 range
    /// 0-50). Empty array, never null.</summary>
    private static NPCStatInterpolatedDurabilityModifier[] BuildDurabilityTransfers(AnimalManifest m, DutyBuilder.SpeciesStats stats)
    {
        if (!m.TrackBloodTrail) return Array.Empty<NPCStatInterpolatedDurabilityModifier>();

        var blood = stats?.Blood;
        if (blood == null)
        {
            Log.Warn($"Animals: {m.SourceFile}: Tracks.BloodTrail needs the species' blood NPCStat "
                   + "(Agent.Stats.Blood on the Ref path) — blood trails disabled");
            return Array.Empty<NPCStatInterpolatedDurabilityModifier>();
        }

        return new[]
        {
            new NPCStatInterpolatedDurabilityModifier
            {
                UseAssociatedAgent = true,
                TargetAgent = null,
                TargetStat = blood,
                OutputDurability = DurabilitiesTypes.Special1,
                Value = new InterpolatedValue
                {
                    Active = true,
                    InputValueRange = new Vector2(0f, 100f),
                    OutputValueRange = new Vector2(0f, 100f),
                    WhenOutOfRange = InterpolatedValueOutOfRange.UseMinMaxOutputValue,
                    OutOfRangeCustomValue = 0f,
                },
            },
        };
    }

    /// <summary>Default <c>RevealSkillCurve.StatRange</c> when the manifest gives only a
    /// ChanceRange: 0 → Skill_Tracking's authored maximum (150).</summary>
    public static (double Min, double Max) DefaultRevealStatRange => (0d, SkillTrackingMax);
}
