using System;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher;

/// <summary>
/// Adds a "Swim across the river" action to BOTH banks of the River Clearing crossing - the
/// vanilla River Clearing CT8 (<see cref="RiverClearingCt8Uid"/>) and CMC's Village Path clone
/// CT8 (<see cref="VillagePathCt8Uid"/>) - so the crossing is ALWAYS possible, bridge or no
/// bridge, Village Pathfinder trait or not, in either direction.
///
/// <para><b>Why this exists.</b> The eastward River Clearing -> Village Path compass button is a
/// framework <c>ConnectionGates</c> entry (<c>WorldMap/MapNodes.json</c>: <c>ImprovementBuilt
/// cmcimpriverbridge</c> OR <c>PerkEquipped traitsperkvillagepath</c>, <c>HideTravelDA: true</c>).
/// Every other way into the village cluster - the Portal Hub (whose "Return to Portal" card
/// decays after a day), the Sett Warren climbing rope from Greenfalls, or any teleport - lands a
/// player on the village side with no bridge and no way to build one from there. Nexus report
/// 2026-09-15 (TheFifthLorax): "sent to the village area without completing the bridge ... stuck
/// ... without being able to go back to the river clearing." A swim that costs something real is
/// the design answer: the River Bridge stays the comfortable, dry, cheap crossing, and the
/// Pathfinder trait still pre-builds it, but neither is a key any more.</para>
///
/// <para><b>Why a DismantleAction with NO compass direction.</b> Vanilla travel is driven
/// entirely by an action's produced Environment card: <c>CardAction.TravelDestination</c> walks
/// <c>ProducedCards</c> for a CT4 drop and <c>GameManager.ProduceCards</c> assigns
/// <c>NextEnvironment</c> from it (.decomp/CardAction.cs:963-993, GameManager.cs:6923/7437). The
/// compass is presentation only: <c>ExplorationPopup.SetupButtons</c> reparents a DA to a compass
/// slot when <c>HasExplorationDirection</c> is set and otherwise lists it with the location's
/// ordinary actions (.decomp/ExplorationPopup.cs:665-690). So a direction-less travel DA renders
/// as a normal button, and - the load-bearing part - it is INVISIBLE to every framework strip:
/// <c>ConnectionGateService.StripOneDa</c> and <c>WorldMapInjector.StripTravelDAsFromCard</c> both
/// select by <c>HasExplorationDirection</c>/<c>ExplorationDirection</c>, so the gate that hides the
/// bridge button can never touch the swim. Nothing here registers a gate, patches a coroutine,
/// or moves the player from C#: the action is plain data the vanilla travel pipeline consumes.</para>
///
/// <para><b>Novice / skilled pair.</b> Two same-named DAs are appended per bank, gated on
/// mutually exclusive <c>Skill_Swimming</c> ranges with <c>HideAllWhenNotMet</c>. That is exactly
/// how vanilla ships its day/night travel pair: <c>DismantleActionButton.Setup</c> returns false
/// (and hides the button) when <c>StatsAreCorrect</c> fails with an EMPTY missing-stats list,
/// which is what a HideAll gate produces, and <c>ExplorationPopup</c> only adds a name to its
/// duplicate-suppression list after a Setup that returned TRUE - so exactly one of the pair ever
/// renders (.decomp/DismantleActionButton.cs Setup, ExplorationPopup.cs:654-664). CMC's Swimmer
/// trait (<c>traits_perk_swimmer</c>) starts the skill at 50, which is the threshold, so the trait
/// finally does what its description promises ("reduces the stamina cost of crossing water"); a
/// non-swimmer trains the skill by swimming.</para>
///
/// <para><b>Why <see cref="FrameworkEvents.GameDataReady"/>.</b> It fires from the tail of the
/// framework's own LoadMainGameData postfix, after <c>LoadOrchestrator.Execute()</c> - i.e. after
/// WarpResolver (so <c>*WarpData</c> strings are dead and every reference below is set as a
/// resolved object) and after <c>WorldMapInjector.PrepareAll</c> has created and registered the
/// <c>cmcLocVillagePath</c> clone and stripped its inherited Green Tangle exits. Mutating the
/// process-wide CardData at data-load time, before any run exists, also means every
/// <c>InGameCardBase</c> built later snapshots the full list, so the cached-array divergence that
/// killed the bridge button (retro river-bridge-east-click-noop) cannot arise here. LoadMainGameData
/// can run more than once per process, so the append is idempotent on the action's
/// <c>LocalizationKey</c>.</para>
///
/// <para><b>Costs</b> (vanilla scales: Stamina 0..32, Energy 0..96, Wetness 0..100; a vanilla
/// travel hop costs Stamina 12 and 43 calories). Novice: needs Stamina 20+, costs Stamina 20,
/// Energy 16, 60 calories, sets Wetness 100, trains Swimming +2. Skilled (Swimming 50+): needs
/// Stamina 12+, costs Stamina 12, Energy 8, 45 calories, sets Wetness 100, trains Swimming +1.
/// Both take 2 ticks (30 min) and ask for confirmation first.</para>
/// </summary>
internal static class RiverSwimPatch
{
    // River Clearing: vanilla CT8 location card + its CT4 env (Documentation/GameData/
    // CSFF-JsonData_Current/.../CardData/River_ClearingOak_RiverClearing.json). The env UID is the
    // same one WorldMap/MapNodes.json's Village Path connection and RiverBridgeUnlockPatch use.
    private const string RiverClearingCt8Uid = "3c7144d78a55ebb45b0fdf1598cf6a04";
    private const string RiverClearingEnvUid = "2b19b942a09fdd148a43798e942a74eb";

    // Village Path: CMC's clone of vanilla Green Tangle (WorldMap/MapNodes.json entry 0).
    private const string VillagePathCt8Uid = "cmcLocVillagePath";
    private const string VillagePathEnvUid = "cmcEnvVillagePath";

    // Vanilla GameStat GUIDs - Documentation/GameData/CSFF-JsonData_Current/UniqueIDScriptableGUID/GameStat.json.
    private const string StaminaStatUid       = "1cfd30cf13b69b949a0ac521f55a59a2"; // Stamina, 0..32
    private const string WetnessStatUid       = "08d242ad068c51b4fbef44d546645ac7"; // Wetness, 0..100
    private const string EnergyStatUid        = "9e03b1c645e4c1c40b382ceac8387d4e"; // Energy (sleep meter), 0..96
    private const string WeightStatUid        = "16af37a364285d14586629e3e0700e55"; // Weight (calories)
    private const string SkillSwimmingStatUid = "d637f65d4aa266843b99eaa57404767e"; // Skill_Swimming, 0..150

    // Vanilla River: its "Swim" action carries the SwimmingAction tag this crossing borrows.
    private const string VanillaRiverCardUid = "f6ba7b43e3e920b49a84510ca27f527c";
    private const string SwimActionTagName   = "SwimmingAction";
    private static bool _swimTagWarned;

    // Localization keys - rows live in Localization/SimpEn.csv + SimpCn.csv. The DefaultText below
    // is the English fallback; CSV is authoritative at runtime (root CLAUDE.md § Localization CSV).
    internal const string ActionNameKey  = "CMC_RiverSwim_ActionName";
    internal const string ActionNameText = "Swim across the river";
    internal const string DescriptionKey = "CMC_RiverSwim_ActionDescription";
    private  const string DescriptionText =
        "Wade in and swim for the far bank. No bridge needed, but the cold current drains most of " +
        "your stamina, leaves you soaked and weary, and burns a good meal's worth of calories. " +
        "Strong swimmers (Swimming 50+) cross with far less effort, and every crossing trains the skill.";
    internal const string ConfirmKey = "CMC_RiverSwim_ConfirmText";
    private  const string ConfirmText =
        "Swim across? You need most of your stamina, and you will reach the far bank soaked and exhausted.";
    internal const string FadeKey = "CMC_RiverSwim_FadeMessage";
    private  const string FadeText = "You plunge into the cold current and strike out for the far bank...";

    private const int   SwimDaytimeCost         = 2;    // 2 ticks = 30 in-game minutes
    private const float SkilledSwimmingThreshold = 50f; // == the Swimmer trait's StartingStatModifiers head start
    private const float SkillSwimmingMax        = 150f; // Skill_Swimming.json MinMaxValue.y
    private const float StaminaMax              = 32f;  // Stamina.json MinMaxValue.y

    private const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool _initialized;

    /// <summary>One row per variant of the pair; both rows are appended to each bank.</summary>
    private readonly struct SwimVariant
    {
        public SwimVariant(string label, float skillMin, float skillMax, float staminaRequired,
            float stamina, float energy, float calories, float skillGain)
        {
            Label = label; SkillMin = skillMin; SkillMax = skillMax; StaminaRequired = staminaRequired;
            Stamina = stamina; Energy = energy; Calories = calories; SkillGain = skillGain;
        }
        public readonly string Label;
        public readonly float SkillMin, SkillMax;   // Skill_Swimming HideAll gate (Floor-rounded compare)
        public readonly float StaminaRequired;       // Stamina gate, notifies when not met
        public readonly float Stamina, Energy, Calories, SkillGain; // costs are positive here, applied negative
    }

    private static readonly SwimVariant[] Variants =
    {
        // Skill ranges are integer-exclusive: StatValueTrigger.IsInRange floors the value before
        // the compare (.decomp/StatValueTrigger.cs:43), so 0..49 and 50..150 partition 0..150.
        new SwimVariant("novice",  0f,  SkilledSwimmingThreshold - 1f, staminaRequired: 20f, stamina: 20f, energy: 16f, calories: 60f, skillGain: 2f),
        new SwimVariant("skilled", SkilledSwimmingThreshold, SkillSwimmingMax, staminaRequired: 12f, stamina: 12f, energy: 8f, calories: 45f, skillGain: 1f),
    };

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        FrameworkEvents.GameDataReady += OnGameDataReady;
        Plugin.Logger.LogDebug("[RiverSwimPatch] initialized.");
    }

    private static void OnGameDataReady()
    {
        try
        {
            var stats = ResolveStats();
            if (stats == null) return; // already warned

            string riverClearing = AddSwimToBank(RiverClearingCt8Uid, VillagePathEnvUid, "River Clearing -> Village Path", stats);
            string villagePath   = AddSwimToBank(VillagePathCt8Uid, RiverClearingEnvUid, "Village Path -> River Clearing", stats);

            // One Info line for the whole pass (root CLAUDE.md § Mod Logging Norms). "present" is the
            // normal steady state on a second LoadMainGameData in the same process, not a failure.
            Plugin.Logger.LogInfo(
                $"[RiverSwimPatch] '{ActionNameText}': River Clearing={riverClearing}, Village Path={villagePath}.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[RiverSwimPatch] OnGameDataReady failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private sealed class ResolvedStats
    {
        public GameStat Stamina, Wetness, Energy, Weight, SkillSwimming;
    }

    private static ResolvedStats ResolveStats()
    {
        var r = new ResolvedStats
        {
            Stamina       = UniqueIDScriptable.GetFromID<GameStat>(StaminaStatUid),
            Wetness       = UniqueIDScriptable.GetFromID<GameStat>(WetnessStatUid),
            Energy        = UniqueIDScriptable.GetFromID<GameStat>(EnergyStatUid),
            Weight        = UniqueIDScriptable.GetFromID<GameStat>(WeightStatUid),
            SkillSwimming = UniqueIDScriptable.GetFromID<GameStat>(SkillSwimmingStatUid),
        };
        var missing = new List<string>();
        if (r.Stamina == null)       missing.Add("Stamina");
        if (r.Wetness == null)       missing.Add("Wetness");
        if (r.Energy == null)        missing.Add("Energy");
        if (r.Weight == null)        missing.Add("Weight");
        if (r.SkillSwimming == null) missing.Add("Skill_Swimming");
        if (missing.Count == 0) return r;

        Plugin.Logger.LogWarning(
            $"[RiverSwimPatch] vanilla GameStat(s) not found: {string.Join(", ", missing)} - the river swim " +
            "will not be added this boot. Re-verify the GUIDs against the current game data.");
        return null;
    }

    /// <summary>
    /// Appends the novice/skilled swim pair to <paramref name="bankCt8Uid"/>, travelling to
    /// <paramref name="destEnvUid"/>. Returns a one-word outcome for the summary line:
    /// "added", "present" (idempotent re-run) or "missing:&lt;what&gt;" (nothing appended).
    /// </summary>
    private static string AddSwimToBank(string bankCt8Uid, string destEnvUid, string label, ResolvedStats stats)
    {
        var bank = CardUtil.GetCardDataById(bankCt8Uid) as CardData;
        if (bank == null)
        {
            Plugin.Logger.LogWarning($"[RiverSwimPatch] {label}: location card '{bankCt8Uid}' not found - no swim on this bank.");
            return "missing:bank";
        }
        var destEnv = CardUtil.GetCardDataById(destEnvUid) as CardData;
        if (destEnv == null)
        {
            Plugin.Logger.LogWarning($"[RiverSwimPatch] {label}: destination env '{destEnvUid}' not found - no swim on this bank.");
            return "missing:env";
        }

        var list = bank.DismantleActions;
        if (list == null)
        {
            Plugin.Logger.LogWarning($"[RiverSwimPatch] {label}: '{bankCt8Uid}' has a null DismantleActions list - no swim on this bank.");
            return "missing:list";
        }

        // Idempotent: LoadMainGameData (and so GameDataReady) can fire more than once per process.
        foreach (var existing in list)
            if (existing != null && ActionNameKey.Equals(existing.ActionName.LocalizationKey, StringComparison.Ordinal))
                return "present";

        // Template: the bank's first compass travel DA (River Clearing's own "North"; on Village
        // Path one the framework injected). Cloning a real travel DA inherits the shape vanilla's
        // travel pipeline expects - ActionTags, sounds, FadeToBlack, UseMiniTicks, the empty
        // RequiredReceiving* blocks - instead of guessing defaults for ~60 fields.
        DismantleCardAction template = null;
        foreach (var da in list)
            if (da != null && da.HasExplorationDirection) { template = da; break; }
        if (template == null)
        {
            Plugin.Logger.LogWarning($"[RiverSwimPatch] {label}: '{bankCt8Uid}' has no travel DA to clone - no swim on this bank.");
            return "missing:template";
        }

        foreach (var variant in Variants)
        {
            var swim = BuildSwimAction(template, destEnv, variant, stats);
            if (swim == null) return "missing:build"; // already warned
            list.Add(swim);
        }
        Plugin.Logger.LogDebug($"[RiverSwimPatch] {label}: appended {Variants.Length} swim action(s) to '{bankCt8Uid}' -> '{destEnvUid}'.");
        return "added";
    }

    private static DismantleCardAction BuildSwimAction(DismantleCardAction template, CardData destEnv,
        SwimVariant variant, ResolvedStats stats)
    {
        var swim = new DismantleCardAction();

        // Shallow-copy every field down the hierarchy, private ones included (CardAction keeps
        // ActionSounds / FadeToBlack / FadeMessage private). Array references are shared with the
        // template on purpose: they are read-only data (sounds); ActionTags and the mutable parts -
        // RequiredStatValues, StatModifications, ProducedCards and every LocalizedString - are
        // replaced with fresh instances below, so the template is never mutated.
        for (var t = swim.GetType(); t != null && t != typeof(object); t = t.BaseType)
            foreach (var fi in t.GetFields(BF | BindingFlags.DeclaredOnly))
                fi.SetValue(swim, fi.GetValue(template));

        // ActionTags is the one shared array that must NOT stay shared: the swim also carries
        // vanilla's SwimmingAction tag, so Swimming Aid's tiers and the Sinker trait's
        // ActionModifier reach this crossing the way they reach a vanilla river Swim. Appending to
        // the copied reference would tag the template's own "North" as a swim.
        swim.ActionTags = WithSwimTag(template.ActionTags);

        swim.ActionName        = Text(ActionNameKey, ActionNameText);
        swim.ActionDescription = Text(DescriptionKey, DescriptionText);
        swim.ConfirmPopup      = true;
        swim.CustomConfirmText = Text(ConfirmKey, ConfirmText);
        SetPrivate(swim, "FadeMessage", Text(FadeKey, FadeText));

        // NOT a compass button: this is what keeps every direction-keyed strip (ConnectionGate
        // HideTravelDA, WorldMapInjector clone strip, InjectTravelDA collision check) away from it.
        swim.HasExplorationDirection = false;
        swim.ExplorationDirection    = default;
        swim.DontShowOnCompass       = false;
        swim.PerformUponInspection   = false;
        swim.AlwaysShow              = false;

        swim.DaytimeCost           = SwimDaytimeCost;
        swim.HideIfConditionsNotMet = false; // show the button greyed with the "not enough stamina" notice

        swim.RequiredStatValues = new[]
        {
            // Skill gate: HideAll - the wrong variant of the pair disappears entirely.
            new StatValueTrigger
            {
                Stat = stats.SkillSwimming,
                TriggerRange = new Vector2(variant.SkillMin, variant.SkillMax),
                NotifyWhenNotMet = false,
                HideAllWhenNotMet = true,
            },
            // Stamina gate: notify - too tired to attempt it, but the option stays visible.
            new StatValueTrigger
            {
                Stat = stats.Stamina,
                TriggerRange = new Vector2(variant.StaminaRequired, StaminaMax),
                NotifyWhenNotMet = true,
                HideAllWhenNotMet = false,
            },
        };

        swim.StatModifications = new[]
        {
            Mod(stats.Stamina,       -variant.Stamina),
            Mod(stats.Energy,        -variant.Energy),
            Mod(stats.Weight,        -variant.Calories),
            Mod(stats.Wetness,       +100f),          // soaked - the same value vanilla's "Wash Yourself" writes
            Mod(stats.SkillSwimming, +variant.SkillGain),
        };

        if (!BuildTravelDrop(swim, template, destEnv))
        {
            Plugin.Logger.LogWarning($"[RiverSwimPatch] could not rebuild ProducedCards for the {variant.Label} swim - no swim added.");
            return null;
        }
        return swim;
    }

    /// <summary>
    /// Fresh ProducedCards whose single drop is <paramref name="destEnv"/>. The collection is a
    /// public-field copy of the template's first collection (uses, weights, chance modifiers) with
    /// its private runtime state left fresh; the drop is a struct copy of the template's first
    /// travel drop with only the card swapped, so DropChance and the (1,1) quantity that
    /// CardsDropCollection.GetTravelDestination requires are vanilla's own values.
    /// </summary>
    private static bool BuildTravelDrop(DismantleCardAction swim, DismantleCardAction template, CardData destEnv)
    {
        var droppedCardsField = typeof(CardsDropCollection).GetField("DroppedCards", BF);
        if (droppedCardsField == null) return false;

        var templateCollections = template.ProducedCards;
        if (templateCollections == null || templateCollections.Length == 0 || templateCollections[0] == null) return false;
        var templateCollection = templateCollections[0];

        var templateDrops = droppedCardsField.GetValue(templateCollection) as CardDrop[];
        if (templateDrops == null || templateDrops.Length == 0) return false;

        var fresh = new CardsDropCollection();
        foreach (var fi in typeof(CardsDropCollection).GetFields(BindingFlags.Instance | BindingFlags.Public))
            fi.SetValue(fresh, fi.GetValue(templateCollection));

        var drop = templateDrops[0];       // struct copy
        drop.DroppedCard = destEnv;
        drop.Quantity = Vector2Int.one;    // (0,0) would make GetTravelDestination return EnvID.Empty
        droppedCardsField.SetValue(fresh, new[] { drop });

        swim.ProducedCards = new[] { fresh };
        return true;
    }

    /// <summary>
    /// A NEW array: the template's travel tags plus vanilla's SwimmingAction. CardAction.HasActionTag
    /// compares references, so the tag is read off a vanilla swim action (River's own "Swim") instead
    /// of being created: that is the instance WarpResolver binds a perk's AppliesToWarpData to.
    /// </summary>
    private static ActionTag[] WithSwimTag(ActionTag[] templateTags)
    {
        var tags = new List<ActionTag>();
        if (templateTags != null) tags.AddRange(templateTags);
        var swimTag = ResolveSwimTag();
        if (swimTag != null && !tags.Contains(swimTag)) tags.Add(swimTag);
        return tags.ToArray();
    }

    private static ActionTag ResolveSwimTag()
    {
        var river = CardUtil.GetCardDataById(VanillaRiverCardUid) as CardData;
        if (river?.DismantleActions != null)
            foreach (var da in river.DismantleActions)
            {
                if (da?.ActionTags == null) continue;
                foreach (var tag in da.ActionTags)
                    if (tag != null && string.Equals(tag.name, SwimActionTagName, StringComparison.Ordinal))
                        return tag;
            }

        if (!_swimTagWarned)
        {
            _swimTagWarned = true;
            Plugin.Logger.LogWarning(
                $"[RiverSwimPatch] no ActionTag named '{SwimActionTagName}' on vanilla River ('{VanillaRiverCardUid}', " +
                $"{(river == null ? "card not found" : "card found")}) - the river swim keeps its travel tags only, so " +
                "Swimming Aid and the Sinker trait will not change its costs.");
        }
        return null;
    }

    private static StatModifier Mod(GameStat stat, float value) => new StatModifier
    {
        // Resolved SO reference, never StatWarpData: WarpResolver has already run.
        Stat = stat,
        ValueModifier = new Vector2(value, value),
        // RateModifier / Min / Max / CannotModifyBeyond / ApplyEachTick / InstantModifier /
        // IsInverse left at default - the shape of every vanilla travel-DA stat entry.
    };

    private static LocalizedString Text(string key, string defaultText) => new LocalizedString
    {
        ParentObjectID = "",
        LocalizationKey = key,
        DefaultText = defaultText,
    };

    private static void SetPrivate(object target, string fieldName, object value)
    {
        for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            var fi = t.GetField(fieldName, BF | BindingFlags.DeclaredOnly);
            if (fi == null) continue;
            fi.SetValue(target, value);
            return;
        }
        Plugin.Logger.LogDebug($"[RiverSwimPatch] field '{fieldName}' not found on {target.GetType().Name} - left as copied from the template.");
    }
}
