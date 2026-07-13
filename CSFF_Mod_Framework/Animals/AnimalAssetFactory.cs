using CSFFModFramework.Data;
using CSFFModFramework.Loading;
using CSFFModFramework.Reflection;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// Constructs and registers the generated SOs for one species. M1 scope: the NPCAgent shell
/// with the minimal lifecycle and the optional Approach interaction. Registration mirrors
/// JsonDataLoader/CardCloneService: first-wins into the game registry + additive AllData, and
/// the generated UIDs join JsonDataLoader's mod-UID maps so downstream framework passes
/// (NPCAgentActivationService validation, null compaction) treat them as mod content.
/// </summary>
internal static class AnimalAssetFactory
{
    /// <summary>Vanilla Combat_EncounterDuck — M1 smoke-test default for the Approach button
    /// until EncounterBuilder lands in M5.</summary>
    private const string DuckEncounterUid = "e774dab1421d04d458199c9973472c9f";

    private static FieldInfo _uidField;

    public static NPCAgent BuildAgent(AnimalManifest m)
    {
        // Escape hatch: hand-authored agent — nothing to generate in M1; SpawnRegistrar
        // handles the gap this milestone fills.
        if (m.AgentRef != null)
            return GameRegistry.GetByUid(m.AgentRef) as NPCAgent;

        var uid = AnimalUid.For(m.SpeciesId, AnimalUid.PartAgent);
        var agent = ScriptableObject.CreateInstance<NPCAgent>();
        agent.name = uid;
        agent.hideFlags = HideFlags.DontUnloadUnusedAsset;

        _uidField ??= AccessTools.Field(typeof(UniqueIDScriptable), "UniqueID")
                   ?? AccessTools.Field(typeof(UniqueIDScriptable), "uniqueID")
                   ?? AccessTools.Field(typeof(UniqueIDScriptable), "m_UniqueID");
        if (_uidField == null)
        {
            Log.Error("Animals: UniqueIDScriptable.UniqueID field not found — cannot build agents");
            return null;
        }
        _uidField.SetValue(agent, uid);

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
        agent.AgentStats = Array.Empty<NPCStatInstance>();           // shared-stat wiring lands in M2
        agent.AgentDuties = Array.Empty<NPCDutyRef>();               // DutyBuilder lands in M2
        agent.AgentPassiveEffects = Array.Empty<PassiveEffect>();
        agent.DebugActionIDs = new List<string>();
        agent.CannotTrade = true;
        agent.TradingConditions = EmptyCondition();
        agent.StartingInventory = Array.Empty<NPCInventoryElement>();
        agent.TradingModifiers = Array.Empty<NPCTradingValueModifier>();
        agent.ModifyNPCStatsPerTradeValue = Array.Empty<TradingNPCStatInstantModifier>();
        agent.CommissionsBp = Array.Empty<CardData>();

        var homeEnv = GameRegistry.GetByUid<CardData>(m.HomeEnv);
        var spiritWorld = GameRegistry.GetByUid<CardData>(SpawnRegistrar.SpiritWorldUid);
        if (spiritWorld == null)
        {
            Log.Error($"Animals: {m.SourceFile}: Env_SpiritWorld ({SpawnRegistrar.SpiritWorldUid}) not found in registry — cannot build lifecycle");
            return null;
        }
        agent.AgentActions = LifecycleTemplateBuilder.BuildMinimalLifecycle(m, homeEnv, spiritWorld);

        agent.Interactions = BuildInteractions(m);

        if (!Register(agent, m)) return null;
        return agent;
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
    private static FieldInfo _timesField;
    private static FieldInfo _timeTypeField;
    private static FieldInfo _compareTypeField;
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

    /// <summary>Adds an [startHour, endHour) wrap-around-capable hour window to a condition.
    /// InGameTimeCondition's fields are private [SerializeField] — set via reflection on a boxed
    /// instance (DaysOrHours.HourIs = 0; CompareType is unused on the HourIs path).</summary>
    public static void SetConditionTimes(ref GeneralCondition condition, int startHour, int endHour)
    {
        _timesField ??= ReflectionCache.GetField(typeof(GeneralCondition), "RequiredInGameTimes");
        _timeTypeField ??= ReflectionCache.GetField(typeof(InGameTimeCondition), "TimeType");
        _startValueField ??= ReflectionCache.GetField(typeof(InGameTimeCondition), "StartValue");
        _endValueField ??= ReflectionCache.GetField(typeof(InGameTimeCondition), "EndValue");
        if (_timesField == null || _timeTypeField == null || _startValueField == null || _endValueField == null)
        {
            Log.Error("Animals: InGameTimeCondition fields not found — hour windows unavailable (agent will be always-active)");
            return;
        }

        object time = new InGameTimeCondition();
        _timeTypeField.SetValue(time, DaysOrHours.HourIs);
        _startValueField.SetValue(time, startHour);
        _endValueField.SetValue(time, endHour);

        var windows = Array.CreateInstance(typeof(InGameTimeCondition), 1);
        windows.SetValue(time, 0);

        object boxed = condition;
        _timesField.SetValue(boxed, windows);
        condition = (GeneralCondition)boxed;
    }
}
