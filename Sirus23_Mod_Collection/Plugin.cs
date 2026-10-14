using BepInEx;
using BepInEx.Logging;
using CSFFModFramework.Api;
using HarmonyLib;
using Sirus23ModCollection.Patcher;

namespace Sirus23ModCollection;

[BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
internal class Plugin : ContentModPlugin
{
    private const string PluginGuid = "crispywhips.Sirus23ModCollection";
    public const string PluginName = "Sirus23 Mod Collection";
    public const string PluginVersion = "1.22.3";

    internal new static ManualLogSource Logger { get; private set; }

    protected override void OnModAwake()
    {
        Logger = base.Logger;
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        // Wolf upkeep ticks on the framework's TickEvents (no local Update polling).
        TryApply("WolfTick", WolfTickPatch.Register);
        TryApply("OwlRoost", OwlRoostPatch.Register);
        TryApply("CompanionHunt", CompanionHuntPatch.Register);
        // Recall Whistle: brings a companion left behind in another room onto the player's board (W0-24 route).
        TryApply("RecallWhistle", RecallWhistlePatch.Register);
        // Truffle hog (feature 37): a run-start pass, subscribed once per process, that does nothing without Herbs and Fungi.
        TryApply("TruffleHog", TruffleHogPatch.Register);
        TryApply("CompanionContainerGuard", () => CompanionContainerGuardPatch.ApplyPatch(harmony));
        // "Stay Here"/"Follow Me" DAs toggle SpecialDurability2; this patch makes the
        // toggle actually suppress/resume the AlwaysUpdate follow-along behavior.
        TryApply("CompanionStay", () => CompanionStayPatch.ApplyPatch(harmony));
        // Suppresses the WILD Owl's nightly seek-player teleport duty when the player is in a
        // cave/indoor environment — the untamed-agent twin of CompanionStay's cave exclusion.
        TryApply("WildOwlDutyGuard", () => WildOwlDutyGuardPatch.ApplyPatch(harmony));
        // Owl tame/companion is now fully manifest-driven (Animals/Owl.json Interactions/
        // Companion, framework TameInteractionBuilder + CompanionService, M6), so there is no
        // mod-side owl lifecycle patch, and CompanionHuntPatch registers no OwlTameInit handler.
        // Fox taming is a GameSourceModify patch onto the real vanilla Agent_Fox1/Agent_Fox2
        // NPCAgents (GameSourceModify/VanillaFox_Agent1.json, VanillaFox_Agent2.json) — no
        // separate wild-fox species/patcher; CompanionHuntPatch's FoxTameInit handler matches
        // the vanilla agents directly.
        // Wildlife encounter suppression ships as EncounterGuards/WolfGuard.json —
        // evaluated by the framework's single StartEncounter prefix.
        // Sheep butter/reproduction patching runs once game data is ready.
        TryApply("SheepHusbandry", GameLoadPatch.Register);
        // Night-time escape/predation roll for unpenned tame sheep/rams; ticks on
        // TickEvents.DayRollover (no local Update polling). See SheepPenPatch doc comment
        // for scope limits and the balance-placeholder chances.
        TryApply("SheepPen", SheepPenPatch.Register);
        // "Tend Flock" herd QoL button on the Sheep Feeder — shears every ready tame/
        // lactating sheep in the current environment in one click. See SheepFeederPatch
        // doc comment for why milking is intentionally out of scope.
        TryApply("SheepFeeder", SheepFeederPatch.Register);
        // Keeps vanilla clearings' "Grazing Animals (+1 Happines)" remote passive (an additive
        // +1 on Usage) off the sheep: the Lactating Sheep keeps milk in Usage, and no sheep has
        // a Happiness stat. See SheepGrazingGuardPatch doc comment.
        TryApply("SheepGrazingGuard", () => SheepGrazingGuardPatch.ApplyPatch(harmony));
    }
}
