using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CommunityModChest.Patcher;
using CSFFModFramework.Api;
using HarmonyLib;

namespace CommunityModChest;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("crispywhips.advanced_copper_tools", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("crispywhips.Herbs_And_Fungi", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("crispywhips.waterdriveninfrastructure", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("crispywhips.Sirus23ModCollection", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : ContentModPlugin
{
    private const string PluginGuid = "crispywhips.CommunityModChest";
    public const string PluginName = "Community Mod Chest";
    public const string PluginVersion = "1.48.3";

    internal new static ManualLogSource Logger { get; private set; }
    internal static ConfigEntry<bool> EnableAshPartnerSpike { get; private set; }
    internal static ConfigEntry<bool> ForceClearVillageCrime { get; private set; }
    internal static ConfigEntry<bool> EnableGuardDiagnostics { get; private set; }
    internal static ConfigEntry<bool> EnableJailSafetyNet { get; private set; }

    protected override void OnModAwake()
    {
        Logger = base.Logger;
        EnableAshPartnerSpike = Config.Bind(
            "Debug",
            "EnableAshPartnerSpike",
            false,
            "Enables the unfinished Ash Partner NPCDuty spike (spawns a second 'Ash' NPC with debug-only " +
            "buttons). Off by default — this is Phase 0 research scaffolding, not shipped content.");
        ForceClearVillageCrime = Config.Bind(
            "Debug",
            "ForceClearVillageCrime",
            false,
            "Admin override: holds the hidden Village Crime stat at 0, clearing a Banished state without " +
            "a save edit. Turn on to recover a run locked out of the village, then turn it back off.");
        EnableGuardDiagnostics = Config.Bind(
            "Debug",
            "EnableGuardDiagnostics",
            false,
            "Keeps re-logging each Town Guard's duty list, weight, and the engine's own " +
            "NPCDutySelectionInfo.GetNotSelectableReport() every poll while tuning patrols. Off by " +
            "default; a ONE-SHOT report per guard is always logged at Info regardless of this flag.");
        EnableJailSafetyNet = Config.Bind(
            "Debug",
            "EnableJailSafetyNet",
            true,
            "Reactive emergency ration top-up if a jailed player's Weight or Hydration crosses a " +
            "critical threshold (Village_Master_Plan.md Risk R9) -- defense-in-depth on top of the " +
            "daily ration, not a substitute for it. ON by default; turn OFF only to run the " +
            "safety-critical acceptance test that the baseline ration alone never lets Weight or " +
            "Hydration reach their GameOver floor across a full 8-day sentence.");
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        TryApply("ArmorCardsPatch", () => ArmorCardsPatch.ApplyPatch(harmony));
        // River Bridge improvement moved to declarative InjectImprovementInto.json 2026-07-02
        // (framework CSFFModFramework.Injection.ImprovementInjector) — RiverBridgeImprovementPatch.cs retired.
        // Diagnostic patch (2026-07-16) confirmed root cause 2026-07-17 and was removed per its own
        // retrospective next-steps (Documentation/Retrospectives/river-bridge-improvement-menu-2026-07-16.md).
        // RiverBridgeUnlockPatch (renamed from VillagePathfinderBridgePatch 2026-08-09) now bypasses
        // the HasPlank discovery gate for every player, not just Village Pathfinder perk holders —
        // old saves could otherwise never satisfy that gate while standing at River Clearing and the
        // bridge slot would never appear. Perk holders additionally get it auto-built.
        TryApply("RiverBridgeUnlockPatch", RiverBridgeUnlockPatch.Initialize);
        // Pre-creates GameManager.EnvironmentsData entries for the 7 non-instanced interior CT4
        // envs at run start — without this, first entry fails ChangeEnvironment's
        // EnvironmentsData.ContainsKey gate and leaks the outdoor Village CT8 onto the interior
        // board (.audit/player-report-triage-2026-08-10.md finding 4, outdoor-leak half).
        TryApply("InteriorEnvSaveDataPatch", InteriorEnvSaveDataPatch.Initialize);
        TryApply("ForageInjectionPatch", ForageInjectionPatch.Register);
        // Restores tree regrowth on all 12 CMC map locations — StripLegacyBoardUIDs (WorldMap/
        // MapNodes.json) removes the vanilla "Create X Tree if missing" actions along with the
        // Nettle/Clover/Meadowgrass patches it's meant to strip from the finished Village.
        TryApply("TreeRespawnPatch", () => TreeRespawnPatch.Initialize());
        TryApply("TraitsTickHandler", () => TraitsTickHandler.Initialize());
        TryApply("TraitsActionHandler", () => TraitsActionHandler.Initialize());
        // Clears Rotten Remains that accumulate in NPC inventories as their carried food spoils.
        TryApply("NpcRottenRemainsCleanupPatch", () => NpcRottenRemainsCleanupPatch.Initialize());
        TryApply("PerkItemInitPatch", PerkItemInitPatch.Register);
        // Village Farm seasonal crop fields are now declarative: framework ConditionalDrops
        // (SeasonRange) in WorldMap/MapNodes.json spawn one per-season field at the farm.
        // The old bespoke VillageFarmSeasonalCropPatch (+ cmcfarmfield* stats) was removed —
        // it relied on GameQuery.CurrentSeason, which returned null until the framework fix.
        TryApply("MarketStallPatch", () => MarketStallPatch.Initialize());
        TryApply("VillageReputationPatch", () => VillageReputationPatch.Initialize());
        TryApply("VillageCrimePatch", () => VillageCrimePatch.Initialize());
        TryApply("InnPatch", () => InnPatch.Initialize(harmony));
        TryApply("InnFireplacePatch", () => InnFireplacePatch.Initialize());
        TryApply("InnKeeperSpawnPatch", () => InnKeeperSpawnPatch.Initialize(harmony));
        TryApply("InnKeeperDialogSchedulePatch", () => InnKeeperDialogSchedulePatch.Initialize());
        TryApply("LostCatPatch", () => LostCatPatch.Initialize());
        TryApply("AshPosePatch", () => AshPosePatch.Initialize());
        TryApply("AshBoarHuntPatch", () => AshBoarHuntPatch.Initialize(harmony));
        TryApply("AshCatTickPatch", AshCatTickPatch.Initialize);
        // Ash Partner conversion — Phase 0 spike (EA 0.66 Partner NPC system reaction plan;
        // "Convert Ash the Cat into a hands-free Partner companion"). Gated off by default 2026-08-06
        // (critical-analysis CRITICAL C6): this spike is debug-only — it unconditionally spawned
        // cmcAshPartnerAgent, a second NPC named "Ash" using the same portrait as the real cmcInnCat
        // companion, with player-visible DismantleActions literally reading "[Debug] Start Wandering" /
        // "Remove before shipping" — with no quest gate, no opt-in, and no disclosure in ModInfo/README/
        // CHANGELOG. Gated behind EnableAshPartnerSpike (default false) rather than deleted — it's a
        // confirmed-working proof-of-concept (NPCDuty MoveToPlayer pathing + weighted duty-switching)
        // worth keeping for the real Phase 3 quest-gated conversion trigger. Flip the config on only for
        // development verification of that mechanism.
        if (EnableAshPartnerSpike.Value)
        {
            TryApply("AshPartnerDutyPatch", () => AshPartnerDutyPatch.Initialize());
            TryApply("AshPartnerSpawnPatch", () => AshPartnerSpawnPatch.Initialize(harmony));
        }
        else
        {
            Logger.LogDebug("[Plugin] Ash Partner spike disabled (EnableAshPartnerSpike=false).");
        }
        // TEMPORARY — diagnosing "vanilla Partner never follows into the CMC map" (2026-08-10).
        // Remove once CompanionFollowDiagnostics.cs's root cause is confirmed and fixed.
        TryApply("CompanionFollowDiagnostics", () => CompanionFollowDiagnostics.Initialize());
        // Building interiors (Inn/Academy) are never WorldMap graph nodes, so the vanilla
        // NPCDuty/MoveDutyAction follow mechanism can never route a Partner through their doors
        // regardless of the outdoor-pathing question CompanionFollowDiagnostics is chasing —
        // this bypasses pathfinding and mirrors an allied companion's env directly onto the
        // player's own Enter/Exit transitions for those two doors.
        TryApply("PartnerIndoorFollowPatch", () => PartnerIndoorFollowPatch.Initialize(harmony));
        // Shadow the Cat — independent companion chain, spawn-gated on Herbalism graduation
        // (AcademyCourseService.GradHerbalism) rather than a hidden GameStat. See
        // Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §3.6/§10.7.
        TryApply("ShadowCatPatch", () => ShadowCatPatch.Initialize());
        TryApply("IronRodFishingPatch", IronRodFishingPatch.Register);
        // Higher Education / Village Academy (perk-gated course system)
        TryApply("AcademyCourseService", AcademyCourseService.Initialize);
        TryApply("AcademyPatch", () => AcademyPatch.Initialize(harmony));
        // Graduate perk — grants all 6 Academy graduate perks + backfills Lecture Hall
        // course progress, independent of Village Founder (split 2026-07-23).
        TryApply("GraduatePerkPatch", () => GraduatePerkPatch.Initialize(harmony));
        TryApply("ProfessorSchedulePatch", () => ProfessorSchedulePatch.Initialize(harmony));
        // Cottage residents (Miller/Weaver/Apothecary) — move in one week after their home is built.
        TryApply("CottageResidentSpawnPatch", () => CottageResidentSpawnPatch.Initialize(harmony));
        // Miller/Weaver daily schedule — home at their cottage overnight, occasional wandering/
        // Inn/Academy visits by day. Apothecary keeps her own separate commute scheduler.
        TryApply("CottageResidentSchedulePatch", () => CottageResidentSchedulePatch.Initialize(harmony));
        // Miller's Copper Chest (Village_Master_Plan.md §10.8.3) — weekly accrual (retargeted from
        // his satchel restock, above), "Sell to the Miller" afford-gated CI, and the "Search for
        // valuables" burglary DA with its detection roll. Miller-only this pass per §10.8.3.7.
        // Registered AFTER CottageResidentSchedulePatch, whose IsResidentHome query the theft
        // detection reads.
        TryApply("CopperChestPatch", () => CopperChestPatch.Initialize());
        // Village Apothecary — post-move-in stall spawn, cabin<->village commute, and trade restock.
        TryApply("ApothecarySchedulePatch", () => ApothecarySchedulePatch.Initialize(harmony));
        // Village Guards (Village_Master_Plan.md §10.8.1) — the duty builder MUST initialize
        // before the spawn patch: GuardSpawnPatch refuses to place any guard until
        // GuardDutyPatch.DutiesReady flips, because InGameNPC.Init snapshots AgentDuties once.
        TryApply("GuardDutyPatch", () => GuardDutyPatch.Initialize());
        TryApply("GuardSpawnPatch", () => GuardSpawnPatch.Initialize(harmony));
        // Attack action (§10.8.4) — the button and both crime penalties are declarative on the
        // guards' NPCAgent/Encounter JSON; this only publishes "who was standing there" so a
        // later chunk's pursuit duty can re-arm the instant a fight starts in front of a guard.
        TryApply("GuardWitnessPatch", () => GuardWitnessPatch.Initialize(harmony));
        // Wanted-tier escalate-on-sight (§10.8.2, 25-59 crime) — a co-located guard's Suspicion
        // rises short of pursuit; that band's player-visible half is Sterling's own dialog
        // (CMC_SterlingTalk_Wanted, pure JSON). Registered after GuardWitnessPatch, whose
        // FindGuardsAt this reuses.
        TryApply("GuardWantedReactionPatch", () => GuardWantedReactionPatch.Initialize());
        // Guard gauntlet bookkeeping (§10.8.6). Who won each fight is declarative on the
        // Encounter assets; this owns only the season respawn timer, the all-four-down pardon,
        // and the arrest-pending extension point the Jail chunk (§10.8.7) consumes.
        TryApply("GuardOutcomePatch", () => GuardOutcomePatch.Initialize());
        // The Village Jail (§10.8.7) — sentencing, the daily sentence decrement (without which
        // the cell is a softlock) and the warden-absence window. MUST initialize after
        // GuardOutcomePatch: it subscribes to that class's ArrestPendingRaised event.
        TryApply("JailPatch", () => JailPatch.Initialize());
        // The Hidden Tunnel (§10.8.8) — bed/tunnel toggle, dig progress, getting caught. Only
        // needs JailPatch's UnguardedStatUid/SentenceOriginalStatUid to already be readable, not
        // strict init ordering, but registering right after keeps the two jail chunks together.
        TryApply("JailEscapePatch", () => JailEscapePatch.Initialize());
        TryApply("HerbalismForagePatch", () => HerbalismForagePatch.Apply(harmony));
        // Village Academy / Inn interiors actively manage Body Temperature to 75% while
        // the player is indoors — heats a cold player, cools an overheated one.
        TryApply("IndoorHeatCapPatch", IndoorHeatCapPatch.Initialize);
        // Village Founder perk — instantly fast-forwards every shipped village beat.
        TryApply("VillageFounderPerkPatch", () => VillageFounderPerkPatch.Initialize(harmony));
        // Quest-chain chassis (PR-2, Village_Master_Plan.md §3.4) — arms each villager's next
        // main-chain quest one village-week after the last was thanked.
        TryApply("QuestChainSchedulePatch", () => QuestChainSchedulePatch.Initialize());
        // Town Hall Boards (PR-3, Village_Master_Plan.md §3.5) — date-stamps each of the 7
        // tracked village structures the tick it's found complete; read by the Town/Construction
        // Board's gated DA entries.
        TryApply("VillageChroniclePatch", () => VillageChroniclePatch.Initialize());
        TryApply("VillageHallBoardsPatch", () => VillageHallBoardsPatch.Initialize(harmony));
    }
}
