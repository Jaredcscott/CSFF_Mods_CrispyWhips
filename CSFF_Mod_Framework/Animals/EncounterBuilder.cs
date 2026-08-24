using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// M5 — resolves a manifest's <c>Encounter</c> section to a real <see cref="Encounter"/>: either
/// the hand-authored <c>Ref</c> (with an optional <c>BodyTemplate</c> override applied on top), or
/// a full generated Encounter built from the manifest's Blood/Size/Awareness/Cover/Stealth/Passive
/// fields. Also compiles the <c>Aggression</c> block into a generated night-attack-style
/// <c>NPCDuty</c> via <see cref="DutyBuilder"/>'s existing <c>StartEncounterDutyAction</c> idiom —
/// see <see cref="DutyBuilder.AttachAggressionDuty"/>, never a cloned vanilla attack duty (those
/// fire a hardwired encounter — research §5.1).
///
/// <para><b>Spike findings this builder is built against</b> (re-verified against framework
/// 2.23.5+ and game data EA 0.66h — the original research (2026-07-09/10, EA 0.65h/framework
/// 2.11.1) is superseded here, not re-derived blind):</para>
/// <list type="bullet">
/// <item><b>BodyTemplate name-fallback CONFIRMED working — no C# fallback patch built.</b>
/// <see cref="WarpResolver.Lookup"/>'s non-UID-SO branch (<c>Data/WarpResolver.cs:808-822</c>)
/// resolves by name against <see cref="Database.GetTypedSO"/> / <c>AllScriptableObjectDict</c>,
/// which <see cref="Database.InitFromGame"/> populates from a SINGLE
/// <c>Resources.FindObjectsOfTypeAll&lt;ScriptableObject&gt;()</c> scan — this finds vanilla
/// <c>BodyTemplate</c> assets (confirmed present as <c>"Combat_ BTDuck"</c>, leading space, in
/// <c>ScriptableObjectObjectName/BodyTemplate.txt</c>) because Unity resolves an
/// <c>Encounter.EnemyBodyTemplate</c> direct object reference transitively when any vanilla
/// Encounter loads. Empirical confirmation: the Owl's hand-authored
/// <c>Encounter/Encounter_WildOwl.json</c> has shipped <c>EnemyBodyTemplateWarpData: "Combat_
/// BTDuck"</c> since before M0, and <c>EncounterPopup.GenerateEnemyWound</c>
/// (<c>.decomp/EncounterPopup.cs:3907-3917</c>) dereferences <c>EnemyBodyTemplate.Head/.Torso/...</c>
/// with NO null guard — a null template is a guaranteed NRE on the FIRST successful hit. The
/// `retro_owl_taming_carcass_on_kill` acid test (T2.30, PASS 2026-07-21) proves a wild owl was
/// killed via the Approach → Encounter → EnemyDefeatedEffects (AgentBlood -10000) chain with no
/// crash report — that chain requires landing hits, which requires a resolved, non-null
/// EnemyBodyTemplate. This builder therefore uses the pure-name path
/// (<see cref="Database.GetTypedSO"/>) exactly like <c>WarpResolver</c> does, with no
/// <c>FindObjectsOfTypeAll(BodyTemplate)</c> fallback scan.</item>
/// <item><b>EncounterResultEffect: 8 reachable blocks, ALL always emitted non-null with empty
/// arrays</b> — <c>EnemyDefeatedEffects, EnemyEscapedEffects, PlayerEscapedEffects,
/// PlayerDemoralizedEffects, Special1-4Effects</c> (<c>.decomp/Encounter.cs:89-107</c>). The
/// confirmed crash class: <c>EncounterPopup</c>'s own result-selection switch dereferences
/// <c>Special1Effects.OnlyTriggerOnMainActions</c> etc. UNCONDITIONALLY the moment that
/// <c>EncounterResult</c> is reached (<c>.decomp/EncounterPopup.cs:2343-2380</c>) — a SEPARATE,
/// earlier code path from the null-checked <c>DoEndOfEncounterActions</c> caller
/// (<c>:2579-2601</c>). Vanilla <c>Combat_EncounterDuck.json</c> populates all 8 (4 of them
/// entirely empty/flavourless) — this generator matches that shape.</item>
/// <item><b>EncounterStartingOptions.EventSetup (private) and Encounter.DefaultPlayerWounds /
/// EnemyAction.PlayerWounds are a SECOND and THIRD crash class, not documented in the original
/// research.</b> The Owl's hand-authored file omits <c>StartingOptions</c> entirely — safe there
/// only because the framework's JSON-load path (<c>CreateInstanceSafe</c>) null-hygienes
/// serializable class fields before a mod ever sees the object; THIS builder constructs a bare
/// <c>ScriptableObject.CreateInstance&lt;Encounter&gt;()</c> with no such hygiene pass, so every
/// class-typed field reachable from the popup's live combat flow must be explicitly constructed.
/// <c>EncounterPopup.cs:3619</c> calls <c>CurrentEnemyAction.PlayerWounds.GetWoundsForSeverity(...)</c>
/// with NO null guard on ANY successful enemy hit — <c>PlayerWounds</c> (a plain class, not a
/// UnityEngine.Object) null-checks its OWN internal wound-tier arrays
/// (<c>.decomp/PlayerWounds.cs:20,26,32,38</c>), so a bare <c>new PlayerWounds()</c> (all four
/// tiers null) is a fully safe "this attack never assigns a wound card" default — but the
/// INSTANCE itself must not be null. Set on every generated <see cref="EnemyAction"/> AND on
/// <c>Encounter.DefaultPlayerWounds</c> (the line-3622/3626 fallback chain re-dereferences it).</item>
/// </list>
/// </summary>
internal static class EncounterBuilder
{
    /// <summary>Vanilla Combat_EncounterDuck — last-resort fallback when a manifest wants an
    /// Approach button or an Aggression duty but authored neither Ref nor generation fields.</summary>
    private const string DuckEncounterUid = "e774dab1421d04d458199c9973472c9f";

    public static int AppliedCount { get; private set; }
    public static void Reset() => AppliedCount = 0;

    /// <summary>Resolves this species' Encounter: Ref (with optional BodyTemplate override) &gt;
    /// generation &gt; vanilla-duck fallback (only if something actually needs one) &gt; null.
    /// Errors are already collected by <see cref="AnimalValidator"/>; this only logs Warn/Error
    /// for conditions the validator can't fully rule out (e.g. a live registry miss).</summary>
    public static Encounter Resolve(AnimalManifest m)
    {
        if (m.EncounterRef != null)
        {
            if (GameRegistry.GetByUid(m.EncounterRef) is not Encounter refEncounter)
            {
                Log.Error($"Animals: {m.SourceFile}: Encounter.Ref '{m.EncounterRef}' did not resolve at build time");
                return null;
            }
            ApplyBodyTemplateOverride(refEncounter, m);
            return refEncounter;
        }

        if (m.HasEncounterGeneration)
        {
            var generated = BuildGenerated(m);
            if (generated != null) AppliedCount++;
            return generated;
        }

        if (!m.ApproachButton && !m.AggressionEnabled) return null;

        if (m.AgentRef != null)
        {
            // A Ref-path agent with no manifest Encounter section already has its OWN
            // hand-authored Approach button wired to its own Encounter JSON (WarpResolver
            // resolved it before this ever runs) — that is authoritative, not a gap to fill.
            // Falling through to the vanilla duck here would let ApplyApproachButton silently
            // REPLACE a perfectly good species-specific encounter with Combat_EncounterDuck
            // (wrong body template/portrait, AND its EnemyDefeatedEffects target the vanilla
            // duck's own NPCStat, so the species' real Blood/Exists stats never reach zero —
            // the agent looks unkillable). Confirmed bug: Sirus23 Wild Fox (Animals/Fox.json has
            // no Encounter section) had its Approach button silently overwritten this way.
            Log.Debug($"Animals: {m.SourceFile}: Agent.Ref with no Encounter.Ref/generation — leaving the hand-authored agent's own Approach/Encounter untouched");
            return null;
        }

        if (GameRegistry.GetByUid(DuckEncounterUid) is Encounter fallback)
        {
            Log.Warn($"Animals: {m.SourceFile}: no Encounter.Ref or generation fields — Approach/Aggression wired to vanilla Combat_EncounterDuck as a last resort");
            return fallback;
        }
        Log.Error($"Animals: {m.SourceFile}: no usable Encounter and the vanilla Combat_EncounterDuck fallback is unavailable — Approach/Aggression skipped");
        return null;
    }

    private static void ApplyBodyTemplateOverride(Encounter encounter, AnimalManifest m)
    {
        if (string.IsNullOrEmpty(m.EncounterBodyTemplate)) return;
        var bt = ResolveBodyTemplate(m);
        if (bt != null) encounter.EnemyBodyTemplate = bt;
    }

    private static BodyTemplate ResolveBodyTemplate(AnimalManifest m)
    {
        if (string.IsNullOrEmpty(m.EncounterBodyTemplate)) return null;
        if (Database.GetTypedSO(typeof(BodyTemplate), m.EncounterBodyTemplate) is BodyTemplate bt) return bt;
        Log.Warn($"Animals: {m.SourceFile}: Encounter.BodyTemplate '{m.EncounterBodyTemplate}' not found by runtime name — "
               + "EncounterPopup.GenerateEnemyWound will NullReferenceException the first time this enemy is successfully hit "
               + "(verify against Documentation/GameData/CSFF-JsonData_Current/ScriptableObjectObjectName/BodyTemplate.txt)");
        return null;
    }

    /// <summary>The generated Encounter path never set <c>EncounterImage</c> (unlike a
    /// hand-authored Encounter JSON, which always carries its own
    /// <c>EncounterImageWarpData</c>) — a null Sprite there renders as a blank enemy icon in the
    /// combat popup (confirmed: Sirus23 Wild Owl, the only species on the generated path). Reuses
    /// the species' own portrait: <c>m.Sprite</c> on the fully-generated path (same lookup
    /// <see cref="AnimalAssetFactory.BuildGeneratedAgent"/> uses for <c>AgentImage</c>), or the
    /// Ref-path agent's already-resolved <c>AgentImage</c> (WarpResolver ran long before this).</summary>
    private static Sprite ResolveEncounterImage(AnimalManifest m)
    {
        if (!string.IsNullOrEmpty(m.Sprite) && Database.SpriteDict != null
            && Database.SpriteDict.TryGetValue(m.Sprite, out var sprite))
            return sprite;

        if (m.AgentRef != null && GameRegistry.GetByUid(m.AgentRef) is NPCAgent refAgent)
            return refAgent.AgentImage;

        return null;
    }

    // ------------------------------------------------------------------ generation ---

    private static Encounter BuildGenerated(AnimalManifest m)
    {
        var uid = AnimalUid.For(m.SpeciesId, AnimalUid.PartEncounter);
        var enc = ScriptableObject.CreateInstance<Encounter>();
        enc.name = uid;
        enc.hideFlags = HideFlags.DontUnloadUnusedAsset;
        if (!AnimalAssetFactory.SetUniqueId(enc, uid)) return null;

        string displayName = m.DisplayName ?? m.SpeciesId;
        enc.EncounterTitle = new LocalizedString { ParentObjectID = "", LocalizationKey = "IGNOREKEY", DefaultText = displayName };
        enc.EncounterStartingLog = new EncounterLogMessage("");
        enc.EnemyName = new LocalizedString { ParentObjectID = "", LocalizationKey = "IGNOREKEY", DefaultText = displayName.ToLowerInvariant() };
        enc.UsesPlural = false;

        enc.EnemyCover = new Vector2((float)m.EncounterCoverMin, (float)m.EncounterCoverMax);
        enc.PlayerCover = new Vector2(75f, 75f);
        enc.EnemyStealth = new Vector2((float)m.EncounterStealthMin, (float)m.EncounterStealthMax);
        enc.EnemyAwareness = new Vector2((float)m.EncounterAwarenessMin, (float)m.EncounterAwarenessMax);
        enc.NoStealthCalculation = false;
        enc.EnemySize = new Vector2((float)m.EncounterSize, (float)m.EncounterSize);

        enc.EnemyBodyTemplate = ResolveBodyTemplate(m);
        if (enc.EnemyBodyTemplate == null)
            Log.Warn($"Animals: {m.SourceFile}: generated encounter '{m.SpeciesId}' has no EnemyBodyTemplate — "
                   + "the FIRST successful player hit will NullReferenceException (confirmed unconditional dereference)");

        enc.EncounterImage = ResolveEncounterImage(m);
        if (enc.EncounterImage == null)
            Log.Warn($"Animals: {m.SourceFile}: generated encounter '{m.SpeciesId}' has no EncounterImage — "
                   + "the combat popup will show a blank enemy icon");

        enc.EnemyArmor = new ArmorValues();
        enc.DamageTypeAddedClash = Array.Empty<DamageTypeArmorModifier>();
        enc.DamageTypeAddedDmg = Array.Empty<DamageTypeArmorModifier>();

        enc.MeleeSkill = MakeEnemyValue("Melee Skill", 20f, 20f);
        enc.RangedSkill = MakeEnemyValue("", 0f, 0f);
        enc.Blood = MakeEnemyValue("Blood", (float)m.EncounterBlood, (float)m.EncounterBlood,
            maxValue: (float)m.EncounterBlood, onZero: EncounterResult.EnemyDefeated);
        enc.Stamina = MakeEnemyValue("Stamina", 100f, 100f);
        enc.Morale = MakeEnemyValue("Morale", 100f, 100f);
        enc.Value1 = default;
        enc.Value2 = default;
        enc.Value3 = default;
        enc.Value4 = MakeEnemyValue("PoisonResistance", 12f, 12f, maxValue: 12f);

        enc.EnemyActions = BuildEnemyActions(m);
        enc.PlayerActions = Array.Empty<GenericEncounterPlayerAction>();
        enc.DefaultPlayerWounds = new PlayerWounds();

        enc.EnemyDefeatedEffects = BuildDefeatedEffect(m);
        enc.EnemyEscapedEffects = EmptyResultEffect("");
        enc.PlayerEscapedEffects = EmptyResultEffect("");
        enc.PlayerDemoralizedEffects = EmptyResultEffect("");
        enc.Special1Effects = EmptyResultEffect("");
        enc.Special2Effects = EmptyResultEffect("");
        enc.Special3Effects = EmptyResultEffect("");
        enc.Special4Effects = EmptyResultEffect("");

        enc.StartingOptions = BuildStartingOptions(m);

        AnimalAssetFactory.RegisterGenerated(enc, m);
        Log.Info($"Animals: {m.SpeciesId}: generated Encounter '{uid}' (Blood {m.EncounterBlood}, Passive={m.EncounterPassive}, ForceFight={m.EncounterForceFight})");
        return enc;
    }

    /// <summary>Notice (never attacks) + Escape always; an attack action only when
    /// <c>Encounter.Passive</c> is false. Damage is TUNABLE, scaled off Blood to roughly match the
    /// proven owl hand-authored numbers (Blood 40 → Damage [4.8, 14]; the owl's shipped file used
    /// [5, 15]) — no per-species Damage field exists in the manifest schema (v1).</summary>
    private static EnemyAction[] BuildEnemyActions(AnimalManifest m)
    {
        var notice = new EnemyAction
        {
            BaseWeight = 40,
            DoesNotAttack = true,
            DamageTypes = new List<DamageType>(),
            PlayerWounds = new PlayerWounds(),
        };
        var escape = new EnemyAction
        {
            BaseWeight = 15,
            DoesNotAttack = true,
            IsEscapeAction = true,
            EncounterResult = EncounterResult.EnemyEscaped,
            DamageTypes = new List<DamageType>(),
            PlayerWounds = new PlayerWounds(),
        };

        if (m.EncounterPassive)
            return new[] { notice, escape };

        var attack = new EnemyAction
        {
            BaseWeight = 20,
            DoesNotAttack = false,
            Damage = new Vector2((float)(m.EncounterBlood * 0.12), (float)(m.EncounterBlood * 0.35)),   // TUNABLE
            DamageTypes = new List<DamageType>(),
            PlayerWounds = new PlayerWounds(),
        };
        return new[] { notice, attack, escape };
    }

    /// <summary>Correct by default per the schema: defeat writes Blood -10000 (UseAssociatedAgent)
    /// so the AGENT's own generated death action (LifecycleTemplateBuilder, already wired to a
    /// blood-dead-band condition) drops the carcass where it died and the lifecycle ticker starts
    /// the kill-respawn timer — the encounter itself does nothing else. Escape/PlayerEscape/
    /// PlayerDemoralized/Special1-4 stay flavour-only in v1 (matches the proven, shipped
    /// Encounter_WildOwl.json shape exactly): the M2/M4 lifecycle ticker's respawn-timer machinery
    /// is exclusively blood-death-scoped (AnimalLifecycleTicker.TickKillRespawn resets the timer to
    /// 0 every tick blood is healthy), so writing to it here would be silently undone next tick;
    /// and forcing the internal Exists stat to 0 on escape (vanilla's own approach, confirmed via
    /// Combat_EncounterDuck.json's EnemyEscapedEffects) has no matching "come back later" trigger
    /// for a non-suppressed wild individual in this milestone's ticker — that would strand the
    /// species parked in the Spirit World forever. A real "temporarily flee, reappear later" state
    /// is a deferred design gap, not silently invented here.</summary>
    private static EncounterResultEffect BuildDefeatedEffect(AnimalManifest m)
    {
        var effect = EmptyResultEffect("");
        var blood = m.AgentRef != null ? ResolveRefBloodStat(m) : Database.GetTypedSO(typeof(NPCStat), "AgentBlood") as NPCStat;
        if (blood == null)
        {
            Log.Warn($"Animals: {m.SourceFile}: could not resolve a Blood NPCStat for the generated encounter's defeat effect — defeating this enemy will not trigger the agent's death/carcass action");
            return effect;
        }
        effect.NPCStatChanges = new[]
        {
            new NPCStatInstantModifier { TargetStat = blood, UseAssociatedAgent = true, ValueChange = new Vector2(-10000f, -10000f) },
        };
        effect.SaveEncounterToNPC = true;
        return effect;
    }

    private static NPCStat ResolveRefBloodStat(AnimalManifest m)
        => m.AgentStatMap != null && m.AgentStatMap.TryGetValue("Blood", out var uid)
            ? GameRegistry.GetByUid<NPCStat>(uid)
            : null;

    private static EncounterResultEffect EmptyResultEffect(string logText) => new()
    {
        ResultLog = new EncounterLogMessage(logText),
        DroppedCards = new List<CardData>(),
        StatChanges = Array.Empty<StatModifier>(),
        NPCStatChanges = Array.Empty<NPCStatInstantModifier>(),
        TransferValuesToStats = Array.Empty<EnemyValueToStatModifier>(),
    };

    private static FieldInfo _startingValueField;

    private static EnemyValue MakeEnemyValue(string displayName, float min, float max, float maxValue = 0f,
        EncounterResult onZero = EncounterResult.Ongoing)
    {
        _startingValueField ??= AccessTools.Field(typeof(EnemyValue), "StartingValue");
        object boxed = new EnemyValue { Name = displayName, MaxValue = maxValue, OnZeroEncounterResult = onZero };
        if (_startingValueField != null)
            _startingValueField.SetValue(boxed, new Vector2(min, max));
        else
            Log.Error("Animals: EnemyValue.StartingValue field not found — generated encounter stat will start at 0");
        return (EnemyValue)boxed;
    }

    private static FieldInfo _eventSetupField;

    private static EncounterStartingOptions BuildStartingOptions(AnimalManifest m)
    {
        var options = new EncounterStartingOptions();
        _eventSetupField ??= AccessTools.Field(typeof(EncounterStartingOptions), "EventSetup");
        if (_eventSetupField != null)
            _eventSetupField.SetValue(options, m.EncounterForceFight
                ? EncounterStartConfigurations.ForceFight
                : EncounterStartConfigurations.FightOrEscape);
        else
            Log.Error("Animals: EncounterStartingOptions.EventSetup field not found — the generated encounter's popup may not offer Fight/Escape buttons correctly");
        return options;
    }
}
