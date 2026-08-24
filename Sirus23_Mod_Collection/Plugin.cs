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
    public const string PluginVersion = "1.20.2";

    internal new static ManualLogSource Logger { get; private set; }

    protected override void OnModAwake()
    {
        Logger = base.Logger;
    }

    protected override void RegisterPatches(Harmony harmony)
    {
        // Wolf upkeep ticks on the framework's TickEvents (no local Update polling).
        TryApply("WolfTick", WolfTickPatch.Register);
        TryApply("CompanionHunt", CompanionHuntPatch.Register);
        TryApply("CompanionContainerGuard", () => CompanionContainerGuardPatch.ApplyPatch(harmony));
        // "Stay Here"/"Follow Me" DAs toggle SpecialDurability2; this patch makes the
        // toggle actually suppress/resume the AlwaysUpdate follow-along behavior.
        TryApply("CompanionStay", () => CompanionStayPatch.ApplyPatch(harmony));
        // Suppresses the WILD Owl's nightly seek-player teleport duty when the player is in a
        // cave/indoor environment — the untamed-agent twin of CompanionStay's cave exclusion.
        TryApply("WildOwlDutyGuard", () => WildOwlDutyGuardPatch.ApplyPatch(harmony));
        // Owl tame/companion is now fully manifest-driven (Animals/Owl.json Interactions/
        // Companion, framework TameInteractionBuilder + CompanionService, M6) — no more
        // WildOwlLifecyclePatch; CompanionHuntPatch no longer registers an OwlTameInit handler.
        // Fox NPC-cache reset + tame-retirement helpers for CompanionHuntPatch's FoxTameInit —
        // Fox hasn't migrated onto the framework companion service yet (see
        // WildFoxLifecyclePatch doc).
        TryApply("WildFoxLifecycle", WildFoxLifecyclePatch.Register);
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
    }
}
