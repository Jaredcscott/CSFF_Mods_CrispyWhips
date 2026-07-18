using CSFFModFramework.Data;
using CSFFModFramework.Loading;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// Constructs and registers the generated SOs for one species, then wires the pieces shared by
/// both paths: generated NPCDuties (<see cref="DutyBuilder"/>) and the lifecycle ticker
/// (<see cref="AnimalLifecycleTicker"/>).
///
/// <para>Generated path (no Agent.Ref): NPCAgent shell + shared vanilla NPCStats
/// (AgentExists/AgentBlood/AgentSatiation/AgentPoisonResistance/AgentRespawnCounter/AgentCounter
/// — per-agent NPCStatInstance overrides make sharing safe) + full lifecycle AgentActions
/// (death/carcass/parking) + optional Approach interaction.</para>
///
/// <para>Ref path (Agent.Ref = hand-authored NPCAgent UID): the agent itself is untouched;
/// the manifest fills the gaps — spawn registration (M1), generated duties + lifecycle
/// ticker driven by the Agent.Stats UID map (M2).</para>
///
/// <para>Registration mirrors JsonDataLoader/CardCloneService: first-wins into the game
/// registry + additive AllData, and the generated UIDs join JsonDataLoader's mod-UID maps so
/// downstream framework passes (NPCAgentActivationService validation, null compaction) treat
/// them as mod content.</para>
/// </summary>
internal static class AnimalAssetFactory
{
    /// <summary>Vanilla Combat_EncounterDuck — M1 smoke-test default for the Approach button
    /// until EncounterBuilder lands in M5.</summary>
    private const string DuckEncounterUid = "e774dab1421d04d458199c9973472c9f";

    private static FieldInfo _uidField;

    public static NPCAgent BuildAgent(AnimalManifest m)
    {
        var spiritWorld = GameRegistry.GetByUid<CardData>(SpawnRegistrar.SpiritWorldUid);
        if (spiritWorld == null)
        {
            Log.Error($"Animals: {m.SourceFile}: Env_SpiritWorld ({SpawnRegistrar.SpiritWorldUid}) not found in registry — species skipped");
            return null;
        }
        var roostEnv = ResolveRoost(m, spiritWorld);

        NPCAgent agent;
        DutyBuilder.SpeciesStats stats;

        if (m.AgentRef != null)
        {
            // Escape hatch: hand-authored agent — nothing to generate; the manifest fills the
            // gaps (spawn registration, generated duties, lifecycle ticker).
            agent = GameRegistry.GetByUid(m.AgentRef) as NPCAgent;
            if (agent == null) return null;
            stats = ResolveRefStats(m);
        }
        else
        {
            stats = new DutyBuilder.SpeciesStats();
            agent = BuildGeneratedAgent(m, spiritWorld, stats);
            if (agent == null) return null;
        }

        int duties = DutyBuilder.AttachDuties(agent, m, stats, roostEnv);
        if (duties > 0)
            Log.Debug($"Animals: {m.SpeciesId}: {duties} generated dut(y/ies) on '{agent.name}'");

        RegisterTicker(m, agent, stats, roostEnv);
        return agent;
    }

    // ------------------------------------------------------------ generated path ---

    private static NPCAgent BuildGeneratedAgent(AnimalManifest m, CardData spiritWorld, DutyBuilder.SpeciesStats stats)
    {
        var uid = AnimalUid.For(m.SpeciesId, AnimalUid.PartAgent);
        var agent = ScriptableObject.CreateInstance<NPCAgent>();
        agent.name = uid;
        agent.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (!SetUniqueId(agent, uid)) return null;

        agent.AgentName = new LocalizedString
        {
            ParentObjectID = "",
            LocalizationKey = m.LocalizationKey ?? "",
            DefaultText = m.DisplayName,
        };
        agent.AgentDescription = new LocalizedString { ParentObjectID = "", LocalizationKey = "", DefaultText = m.DisplayName };

        if (!string.IsNullOrEmpty(m.Sprite))
        {
            if (Database.SpriteDict != null && Database.SpriteDict.TryGetValue(m.Sprite, out var sprite))
                agent.AgentImage = sprite;
            else
                Log.Warn($"Animals: {m.SourceFile}: sprite '{m.Sprite}' not found — agent card will be blank");
        }

        if (!string.IsNullOrEmpty(m.WeightCategory))
        {
            agent.WeightCategory = Database.GetTypedSO(typeof(AgentWeightCategory), m.WeightCategory) as AgentWeightCategory;
            if (agent.WeightCategory == null)
                Log.Warn($"Animals: {m.SourceFile}: WeightCategory '{m.WeightCategory}' not found — using engine default");
        }

        // Null-array hygiene: the engine assumes Unity-serialized (never-null) arrays.
        agent.AgentTags = Array.Empty<NPCTag>();
        agent.HidingConditions = EmptyCondition();
        agent.DismantleActions = Array.Empty<DismantleCardAction>();
        agent.DragAndDropActions = Array.Empty<CardOnCardAction>();
        agent.AmbientSounds = Array.Empty<NPCAmbienceSettings>();
        agent.AgentDuties = Array.Empty<NPCDutyRef>();
        agent.AgentPassiveEffects = Array.Empty<PassiveEffect>();
        agent.DebugActionIDs = new List<string>();
        agent.CannotTrade = true;   // the Attempt-4 lesson: default-on Trade was the "still Trade" bug
        agent.TradingConditions = EmptyCondition();
        agent.StartingInventory = Array.Empty<NPCInventoryElement>();
        agent.TradingModifiers = Array.Empty<NPCTradingValueModifier>();
        agent.ModifyNPCStatsPerTradeValue = Array.Empty<TradingNPCStatInstantModifier>();
        agent.CommissionsBp = Array.Empty<CardData>();

        agent.AgentStats = BuildSharedStats(m, stats);

        // Hidden while roosting: time-windowed HidingConditions covering the inactive hours.
        // Mechanism exists in the engine but has zero vanilla users — flagged UNTESTED #3;
        // the first in-game confirmation closes it.
        if (m.HiddenWhileRoosting && m.HasActivityWindow)
        {
            var hiding = EmptyCondition();
            SetConditionTimes(ref hiding, m.ActiveEnd, m.ActiveStart);
            agent.HidingConditions = hiding;
        }

        var homeEnv = GameRegistry.GetByUid<CardData>(m.HomeEnv);
        var carcass = m.CarcassCard != null ? GameRegistry.GetByUid<CardData>(m.CarcassCard) : null;
        if (m.CarcassCard != null && carcass == null)
            Log.Warn($"Animals: {m.SourceFile}: Carcass.Card '{m.CarcassCard}' not found — death action will drop nothing");
        agent.AgentActions = LifecycleTemplateBuilder.BuildLifecycle(m, homeEnv, spiritWorld, stats, carcass);

        agent.Interactions = BuildInteractions(m);

        if (!Register(agent, m)) return null;
        return agent;
    }

    /// <summary>Wires the shared vanilla NPCStats onto the generated agent via per-agent
    /// NPCStatInstance overrides. AgentExists starts at 1 (default is 0); AgentCounter's
    /// self-increment rate is zeroed (it serves as the suppressed-respawn timer store);
    /// AgentRespawnCounter's range is widened to cover Spawn.DeathRespawnTicks (SetStatValue
    /// clamps to MinMax — a range smaller than the target would stall the timer forever).</summary>
    private static NPCStatInstance[] BuildSharedStats(AnimalManifest m, DutyBuilder.SpeciesStats stats)
    {
        var instances = new List<NPCStatInstance>();

        stats.Exists = AddShared(instances, m, "AgentExists", startOverride: 1f);
        stats.Blood = AddShared(instances, m, "AgentBlood",
            startOverride: (float?)m.AgentBlood?.Start ?? 100f,
            minMaxOverride: new Vector2(0f, (float?)m.AgentBlood?.Max ?? 100f),
            rateOverride: (float?)m.AgentBlood?.RatePerTick);
        AddShared(instances, m, "AgentSatiation",
            startOverride: (float?)m.AgentSatiation?.Start,
            minMaxOverride: m.AgentSatiation?.Max != null ? new Vector2(0f, (float)m.AgentSatiation.Max) : null,
            rateOverride: (float?)m.AgentSatiation?.RatePerTick);
        AddShared(instances, m, "AgentPoisonResistance",
            startOverride: (float?)m.AgentPoisonResistance?.Start,
            minMaxOverride: m.AgentPoisonResistance?.Max != null ? new Vector2(0f, (float)m.AgentPoisonResistance.Max) : null,
            rateOverride: (float?)m.AgentPoisonResistance?.RatePerTick);

        if (m.DeathRespawnTicks > 0)
            stats.RespawnTimer = AddShared(instances, m, "AgentRespawnCounter",
                minMaxOverride: new Vector2(0f, m.DeathRespawnTicks));
        if (m.SuppressWhileCardOnBoard != null)
            stats.SuppressRespawnTimer = AddShared(instances, m, "AgentCounter",
                minMaxOverride: new Vector2(0f, Math.Max(1, m.SuppressedRespawnTicks)),
                rateOverride: 0f);

        return instances.ToArray();
    }

    private static FieldInfo _overrideStartField;
    private static FieldInfo _overrideMinMaxField;
    private static FieldInfo _overrideRateField;

    private static NPCStat AddShared(List<NPCStatInstance> instances, AnimalManifest m, string statName,
        float? startOverride = null, Vector2? minMaxOverride = null, float? rateOverride = null)
    {
        if (Database.GetTypedSO(typeof(NPCStat), statName) is not NPCStat stat)
        {
            Log.Error($"Animals: {m.SourceFile}: shared vanilla NPCStat '{statName}' not found — agent stat skipped");
            return null;
        }

        var instance = new NPCStatInstance { ModelStat = stat };

        _overrideStartField ??= AccessTools.Field(typeof(NPCStatInstance), "OverrideStartingValue");
        _overrideMinMaxField ??= AccessTools.Field(typeof(NPCStatInstance), "OverrideMinMaxValue");
        _overrideRateField ??= AccessTools.Field(typeof(NPCStatInstance), "OverrideRate");

        if (startOverride is { } start)
            _overrideStartField?.SetValue(instance, new OptionalFloatValue(true, start));
        if (minMaxOverride is { } minMax)
            _overrideMinMaxField?.SetValue(instance, new OptionalRangeValue(true, minMax.x, minMax.y));
        if (rateOverride is { } rate)
            _overrideRateField?.SetValue(instance, new OptionalFloatValue(true, rate));

        instances.Add(instance);
        return stat;
    }

    // ------------------------------------------------------------------ ref path ---

    /// <summary>Ref path: resolves the manifest's Agent.Stats UID map onto real NPCStat refs.
    /// Unknown roles were already rejected by the validator; unresolvable UIDs too.</summary>
    private static DutyBuilder.SpeciesStats ResolveRefStats(AnimalManifest m)
    {
        var stats = new DutyBuilder.SpeciesStats();
        if (m.AgentStatMap == null) return stats;
        stats.Exists = ResolveStat(m, "Exists");
        stats.Blood = ResolveStat(m, "Blood");
        stats.RespawnTimer = ResolveStat(m, "RespawnTimer");
        stats.SuppressRespawnTimer = ResolveStat(m, "SuppressRespawnTimer");
        return stats;
    }

    private static NPCStat ResolveStat(AnimalManifest m, string role)
    {
        if (m.AgentStatMap == null || !m.AgentStatMap.TryGetValue(role, out var uid)) return null;
        var stat = GameRegistry.GetByUid<NPCStat>(uid);
        if (stat == null)
            Log.Error($"Animals: {m.SourceFile}: Agent.Stats.{role} '{uid}' does not resolve to an NPCStat");
        return stat;
    }

    // ---------------------------------------------------------------- shared bits ---

    private static CardData ResolveRoost(AnimalManifest m, CardData spiritWorld)
    {
        if (!m.HasActivityWindow || m.Roost == "SpiritWorld") return spiritWorld;
        var env = GameRegistry.GetByUid<CardData>(m.Roost);
        if (env == null)
        {
            Log.Warn($"Animals: {m.SourceFile}: ActivityWindow.Roost '{m.Roost}' not found — falling back to the Spirit World");
            return spiritWorld;
        }
        return env;
    }

    private static void RegisterTicker(AnimalManifest m, NPCAgent agent, DutyBuilder.SpeciesStats stats, CardData roostEnv)
    {
        bool wantsWindow = m.HasActivityWindow;
        bool wantsKillTimer = m.DeathRespawnTicks > 0;
        bool wantsSuppression = m.SuppressWhileCardOnBoard != null;
        if (!wantsWindow && !wantsKillTimer && !wantsSuppression) return;

        if (wantsKillTimer && (stats.Blood == null || stats.RespawnTimer == null))
        {
            Log.Warn($"Animals: {m.SourceFile}: Spawn.DeathRespawnTicks needs Blood + RespawnTimer stats — kill-respawn timer disabled");
            wantsKillTimer = false;
        }
        if (wantsSuppression && (stats.Exists == null || stats.SuppressRespawnTimer == null))
        {
            Log.Warn($"Animals: {m.SourceFile}: Spawn.SuppressWhileCardOnBoard needs Exists + SuppressRespawnTimer stats — suppression disabled");
            wantsSuppression = false;
        }
        if (wantsWindow && stats.Exists == null)
            Log.Warn($"Animals: {m.SourceFile}: ActivityWindow relocation works best with an Exists stat — relocating regardless of retirement state");

        AnimalLifecycleTicker.Register(new AnimalLifecycleTicker.SpeciesLifecycle
        {
            SpeciesId = m.SpeciesId,
            Agent = agent,
            Stats = stats,
            HasActivityWindow = wantsWindow,
            ActiveStart = m.ActiveStart,
            ActiveEnd = m.ActiveEnd,
            RoostEnv = roostEnv,
            DeathRespawnTicks = wantsKillTimer ? m.DeathRespawnTicks : 0,
            SuppressCardUid = wantsSuppression ? m.SuppressWhileCardOnBoard : null,
            SuppressedRespawnTicks = m.SuppressedRespawnTicks,
        });
    }

    private static NPCInteractionButton[] BuildInteractions(AnimalManifest m)
    {
        if (!m.ApproachButton) return Array.Empty<NPCInteractionButton>();

        var encounterUid = m.EncounterRef ?? DuckEncounterUid;
        if (GameRegistry.GetByUid(encounterUid) is not Encounter encounter)
        {
            Log.Warn($"Animals: {m.SourceFile}: encounter '{encounterUid}' not found — Approach button skipped");
            return Array.Empty<NPCInteractionButton>();
        }
        if (m.EncounterRef == null)
            Log.Warn($"Animals: {m.SourceFile}: no Encounter.Ref — Approach wired to vanilla Combat_EncounterDuck (M1 smoke test; EncounterBuilder lands in M5)");

        return new[]
        {
            new NPCInteractionButton
            {
                ButtonName = new LocalizedString { ParentObjectID = "", LocalizationKey = "IGNOREKEY", DefaultText = "Approach" },
                Conditions = EmptyCondition(),
                ButtonType = NPCButtonOptions.Encounter,
                DroppedEncounter = encounter,
                SkipEncounterEvent = false,
            },
        };
    }

    // --------------------------------------------------------------- registration ---

    /// <summary>Stamps a UniqueID onto a freshly created UniqueIDScriptable (the field is
    /// non-public). Returns false (with one Error) if the field can't be found.</summary>
    public static bool SetUniqueId(UniqueIDScriptable so, string uid)
    {
        _uidField ??= AccessTools.Field(typeof(UniqueIDScriptable), "UniqueID")
                   ?? AccessTools.Field(typeof(UniqueIDScriptable), "uniqueID")
                   ?? AccessTools.Field(typeof(UniqueIDScriptable), "m_UniqueID");
        if (_uidField == null)
        {
            Log.Error("Animals: UniqueIDScriptable.UniqueID field not found — cannot build generated objects");
            return false;
        }
        _uidField.SetValue(so, uid);
        return true;
    }

    /// <summary>Registers a generated UniqueIDScriptable (duty, stat, …) exactly like the
    /// agent: game registry + AllData + JsonDataLoader mod-UID maps.</summary>
    public static void RegisterGenerated(UniqueIDScriptable so, AnimalManifest m)
    {
        if (!GameRegistry.TryRegister(so))
        {
            var existing = GameRegistry.GetByUid(so.UniqueID);
            if (!ReferenceEquals(existing, so))
            {
                Log.Warn($"Animals: {m.SourceFile}: generated UID '{so.UniqueID}' ({so.name}) already registered to another object — keeping the first registration");
                return;
            }
        }
        GameRegistry.TryAddToAllData(so);
        JsonDataLoader.AllModUniqueIds.Add(so.UniqueID);
        JsonDataLoader.UniqueIdToModName[so.UniqueID] = m.SourceMod;
        JsonDataLoader.LoadedObjectsByUniqueId[so.UniqueID] = so;
    }

    private static bool Register(NPCAgent agent, AnimalManifest m)
    {
        // Mirror JsonDataLoader: let the game's Init() run its own bookkeeping first.
        var init = ReflectionCache.GetMethod(typeof(NPCAgent), "Init");
        if (init != null)
        {
            try { init.Invoke(agent, null); }
            catch { /* Init may fail before full resolution — that's OK */ }
        }

        if (!GameRegistry.TryRegister(agent))
        {
            var existing = GameRegistry.GetByUid(agent.UniqueID);
            if (!ReferenceEquals(existing, agent))
            {
                Log.Error($"Animals: {m.SourceFile}: generated UID '{agent.UniqueID}' already registered to another object — species skipped (UID collision; change SpeciesId)");
                return false;
            }
        }
        GameRegistry.TryAddToAllData(agent);

        JsonDataLoader.AllModUniqueIds.Add(agent.UniqueID);
        JsonDataLoader.UniqueIdToModName[agent.UniqueID] = m.SourceMod;
        JsonDataLoader.LoadedObjectsByUniqueId[agent.UniqueID] = agent;
        return true;
    }

    // ------------------------------------------------- condition construction ---

    private static FieldInfo[] _conditionArrayFields;
    private static FieldInfo _timeTypeField;
    private static FieldInfo _startValueField;
    private static FieldInfo _endValueField;

    /// <summary>A GeneralCondition with every array field materialized as empty — matching what
    /// Unity/JSON deserialization always produces. default(GeneralCondition) has null arrays,
    /// which not every engine consumer null-checks.</summary>
    public static GeneralCondition EmptyCondition()
    {
        _conditionArrayFields ??= typeof(GeneralCondition)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.FieldType.IsArray)
            .ToArray();

        object boxed = new GeneralCondition();
        foreach (var field in _conditionArrayFields)
            field.SetValue(boxed, Array.CreateInstance(field.FieldType.GetElementType(), 0));
        return (GeneralCondition)boxed;
    }

    /// <summary>An [startHour, endHour) hour-window condition entry (wrap-around capable).
    /// InGameTimeCondition's fields are private [SerializeField] — set via reflection on a
    /// boxed instance (DaysOrHours.HourIs = 0; CompareType is unused on the HourIs path).</summary>
    public static InGameTimeCondition MakeHourWindow(int startHour, int endHour)
    {
        _timeTypeField ??= ReflectionCache.GetField(typeof(InGameTimeCondition), "TimeType");
        _startValueField ??= ReflectionCache.GetField(typeof(InGameTimeCondition), "StartValue");
        _endValueField ??= ReflectionCache.GetField(typeof(InGameTimeCondition), "EndValue");
        if (_timeTypeField == null || _startValueField == null || _endValueField == null)
        {
            Log.Error("Animals: InGameTimeCondition fields not found — hour windows unavailable (condition will be always-true)");
            return default;
        }

        object time = new InGameTimeCondition();
        _timeTypeField.SetValue(time, DaysOrHours.HourIs);
        _startValueField.SetValue(time, startHour);
        _endValueField.SetValue(time, endHour);
        return (InGameTimeCondition)time;
    }

    /// <summary>Adds an [startHour, endHour) wrap-around-capable hour window to a condition.</summary>
    public static void SetConditionTimes(ref GeneralCondition condition, int startHour, int endHour)
    {
        var windows = new[] { MakeHourWindow(startHour, endHour) };
        condition.RequiredInGameTimes = windows;
    }
}
