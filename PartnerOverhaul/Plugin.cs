using BepInEx;
using HarmonyLib;

namespace PartnerOverhaul
{
    // Ships zero new CardData — every fix here mutates an EXISTING vanilla CardData/NPCDuty
    // object at boot (Patcher/GameLoadPatch.cs) or Harmony-patches vanilla behavior directly.
    // The framework SoftDependency below is deliberate even though no framework JSON services
    // are consumed: our GameLoad.LoadMainGameData postfix needs to run after the framework's
    // own repair passes (ForeignInstanceReconciler, etc.) on that same vanilla hook — the
    // original "Architecture decision" writeup was in Documentation/Plans/PartnerOverhaul/
    // PartnerOverhaul_Plan.md, retired 2026-09-05 (171efccbb); read it pre-deletion via
    // `git show 171efccbb^:Documentation/Plans/PartnerOverhaul/PartnerOverhaul_Plan.md`.
    [BepInDependency("crispywhips.CSFFModFramework", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    internal class Plugin : BaseUnityPlugin
    {
        private const string PluginGuid = "crispywhips.partner_overhaul";
        public const string PluginName = "PartnerOverhaul";
        public const string PluginVersion = "1.1.0";

        internal new static BepInEx.Logging.ManualLogSource Logger;
        internal static Plugin Instance { get; private set; }
        private static Harmony _harmony;

        // Phase 2 — diagnostics for the still-unconfirmed bugs (fishing line, brain-tanning,
        // clothes/temperature, cauldron ownership). Default OFF as of 1.0.5: shipping these ON
        // put ~46 Info lines per boot into every player's log (the FindCardToEquip equip trace
        // alone fired 16x/boot). Ask a reporter to flip this on and re-capture instead.
        public static ConfigEntry<bool> EnableDiagnostics { get; private set; }

        // Diagnostics for the still-unconfirmed Pouch/acorn-flour stacking math bug — separate
        // subsystem/toggle from EnableDiagnostics above. Default OFF for the same reason.
        public static ConfigEntry<bool> EnablePouchDiagnostics { get; private set; }

        // 1.1.0 (folded in 2026-10-05) - Fire Keeping's tag-fuel decision also passes on Charcoal (GameLoadPatch Fix 4),
        // so the 1.1.0 Feed Charcoal wiring is reachable; no config, no vanilla card retagged.
        // 1.1.0 - refuse a powder pour that would destroy a whole card to add part of it (the
        // reported pouch/acorn-flour stacking-math bug). A bug fix, so default ON; switchable
        // because it changes a mechanic every powder in the game shares.
        public static ConfigEntry<bool> RefusePartialPowderPours { get; private set; }

        // Phase 4 — small opt-in QoL additions, default OFF (these are new features / design
        // changes, not bug fixes — see root CLAUDE.md § Feature Honesty).
        public static ConfigEntry<bool> ReduceNPCMoveCosts { get; private set; }
        public static ConfigEntry<int> NPCMoveCostReductionPercent { get; private set; }
        public static ConfigEntry<string> ReservedFuelUIDs { get; private set; }
        public static ConfigEntry<bool> ConsolidateWounds { get; private set; }
        public static ConfigEntry<bool> PartnersDrinkFromLakes { get; private set; }
        public static ConfigEntry<bool> ColdPartnerAsksForWarmth { get; private set; }

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            _harmony = new Harmony(PluginGuid);

            EnableDiagnostics = Config.Bind(
                "Diagnostics",
                "Enable Diagnostics",
                false,
                "Log extra LogInfo detail for a handful of still-unconfirmed Partner bugs (fishing line, brain-tanning, clothes/temperature, cauldron ownership). Default OFF: the FindCardToEquip trace fires on every equip re-evaluation and dominated the log. TURN THIS ON before capturing a LogOutput.log for any of those four bug reports.");

            EnablePouchDiagnostics = Config.Bind(
                "Diagnostics",
                "Enable Pouch Transfer Diagnostics",
                false,
                "Log extra LogInfo detail for the reported Pouch/acorn-flour stacking math bug (generic liquid-transfer clamp). Default OFF. TURN THIS ON before capturing a LogOutput.log for that bug report.");

            RefusePartialPowderPours = Config.Bind(
                "Fixes",
                "Refuse Partial Powder Pours",
                true,
                "Refuses pouring a powder (Acorn Flour, ground medicine, any solid with a liquid form) into a container that has room for only part of it. Vanilla destroys the whole card and keeps only the part that fits: a Pouch holds 150 and one Acorn Flour pours 112.5, so a second flour lost 75. Pours that fit are unchanged.");

            ReduceNPCMoveCosts = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Reduce Partner Move Costs",
                false,
                "Discounts the resource-stat cost a Partner pays for inter-environment travel. Note: vanilla has no 'path tile' concept for NPC movement (only the player's own travel-DA speed bonus does), so this is a general move-cost discount, not a path-specific one.");

            NPCMoveCostReductionPercent = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Partner Move Cost Reduction (%)",
                50,
                new BepInEx.Configuration.ConfigDescription(
                    "How much to discount Partner travel move-costs by, if the option above is enabled.",
                    new BepInEx.Configuration.AcceptableValueRange<int>(0, 100)));

            ReservedFuelUIDs = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Reserved Fuel/Wood UIDs",
                "",
                "Comma-separated CardData UniqueIDs a Partner should NEVER auto-select for Fire Keeping (e.g. to keep Wood for crafting and let it burn twigs instead). Scope: only an allied Partner's Fire Keeping duty (PartnerDuty_Firekeeping, on any fire that offers it, modded ones included); every other duty and every other NPC, wild animals included, still sees these items. No in-game UI for this yet: edit this config value directly.");

            ConsolidateWounds = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Consolidate Duplicate Wounds",
                false,
                "Stops a combat round from applying a wound you already have equipped (e.g. accumulating 28 separate Minor Lacerations). Vanilla spawns a fresh, independently-healing wound card every round with no dedup — the count is accurate, not a display bug, so removing it is a combat design change, not a bug fix. Damage, stat changes, armor wear and the encounter log are unaffected. NOT yet confirmed in-game.");

            PartnersDrinkFromLakes = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Partners Drink From Lakes And Streams",
                false,
                "Lets a Partner drink from a lake, bog, marsh, mire, swamp or stream the way vanilla lets it drink from a river, and lets its Seek Drink trip end at one. Default OFF: Seek Drink walks to the closest water, so on a map with both a river and a lake the destination can change. Ponds are not included (their water is a poorer, riskier drink). Read at game load, so changing it needs a restart.");

            ColdPartnerAsksForWarmth = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Cold Partner Asks For Warmth",
                false,
                "Makes a following Partner ask you for warmth while it \"Looks very cold.\" (Body Temperature 1 to 50) with no fire in reach, instead of only once it \"Looks hypothermic.\" (0 and below), when vanilla first lets it. It reuses the game's own Express Need for Warmth request, so it asks at most once per dialog cooldown, only while it is on your board, and moves nobody: it does not change what the Partner does about the cold, and no other duty is affected. Read at game load, so changing it needs a restart; BepInEx keeps a value already written in an existing .cfg.");

            try { Patcher.GameLoadPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"GameLoadPatch failed: {ex}"); }

            try { Patcher.NpcSoundEnvGatePatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"NpcSoundEnvGatePatch failed: {ex}"); }

            try { Patcher.DialogInterruptPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"DialogInterruptPatch failed: {ex}"); }

            try { Patcher.PartnerDiagnosticsPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerDiagnosticsPatch failed: {ex}"); }

            try { Patcher.PouchTransferDiagnosticsPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PouchTransferDiagnosticsPatch failed: {ex}"); }

            try { Patcher.PartialPowderPourPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartialPowderPourPatch failed: {ex}"); }

            try { Patcher.PathSpeedPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PathSpeedPatch failed: {ex}"); }

            try { Patcher.ColdPartnerWarmthPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"ColdPartnerWarmthPatch failed: {ex}"); }

            try { Patcher.WoodReserveListPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"WoodReserveListPatch failed: {ex}"); }

            try { Patcher.WoundStackingPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"WoundStackingPatch failed: {ex}"); }

            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
