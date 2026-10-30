using BepInEx;
using HarmonyLib;

namespace PartnerOverhaul
{
    // Ships its own CardData only for the opt-in Ember Pot (1.1.5, CardData/); every fix here mutates an
    // EXISTING vanilla CardData/NPCDuty object at boot (Patcher/GameLoadPatch.cs) or Harmony-patches vanilla behavior directly.
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
        public const string PluginVersion = "1.1.7";

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
        // 1.1.3: Hearth Fire Keeping and Warm Up moved from an always-on fix to this opt-in (vanilla's
        // Hearth carries no duties; it is the strongest vanilla fire). Default OFF.
        public static ConfigEntry<bool> PartnersTendTheHearth { get; private set; }
        // 1.1.4 (PO-C6) - the Partner opens PO's talk scene by itself. Partner-started, so opt-in,
        // default OFF (plan section 3.0, owner tile 4 A). Read at game load. PO-C8 (1.1.5) shares it:
        // the spoken follow-ups after vanilla's Express remarks (Patcher/PartnerExpressVoicePatch.cs).
        public static ConfigEntry<bool> PartnerStartsConversations { get; private set; }
        // 1.1.4 (PO-C1) - the "Talk with <name>" answer in the Partner's Talk menu. Player-started and
        // costs time like vanilla's Casual Talk, so default ON (plan section 3.0 opt-in rule).
        public static ConfigEntry<bool> PartnerTalkAnswer { get; private set; }
        // 1.1.5 (PO-C12) - spoken follow-ups after an expedition report. It changes a Partner-started
        // vanilla dialog, so default OFF (plan section 3.0 opt-in rule, like PO-C6/PO-C8). Read at game load.
        public static ConfigEntry<bool> ExpeditionStories { get; private set; }
        // 1.1.5 (plan section 2) - the craftable Ember Pot a following Partner wears for warmth. A design
        // change, so opt-in, default OFF (owner tile 7). Read at game load.
        public static ConfigEntry<bool> PartnerEmberPot { get; private set; }

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
                "Lets a Partner skip the resource-stat cost of an inter-environment hop on a chance set by the percentage below (at 50, it pays nothing on about half of its hops and the full cost on the rest). Only your Partner: wild animals pay in full. Note: vanilla has no 'path tile' concept for NPC movement (only the player's own travel-DA speed bonus does), so this is not a path-specific bonus.");

            NPCMoveCostReductionPercent = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Partner Move Cost Reduction (%)",
                50,
                new BepInEx.Configuration.ConfigDescription(
                    "The chance, in percent, that a Partner's hop costs nothing, if the option above is enabled.",
                    new BepInEx.Configuration.AcceptableValueRange<int>(0, 100)));

            ReservedFuelUIDs = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Reserved Fuel/Wood UIDs",
                "",
                "Comma-separated CardData UniqueIDs a Partner should NEVER auto-select for Fire Keeping (e.g. to keep Wood for crafting and let it burn twigs instead; Charcoal, 51c751469bd15ce46804e399479d1764, can be listed too). Scope: only an allied Partner's Fire Keeping duty (PartnerDuty_Firekeeping, on any fire that offers it, modded ones included); every other duty and every other NPC, wild animals included, still sees these items. No in-game UI for this yet: edit this config value directly.");

            ConsolidateWounds = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Consolidate Duplicate Wounds",
                false,
                "Makes combat LESS DANGEROUS: a combat round no longer adds a wound you already have equipped, so a repeat wound's pain, bleeding and infection no longer stack (vanilla gives every Minor Laceration its own Pain +100, Blood Loss +0.5 and Sepsis rate +10, and 28 of them can pile up). Vanilla spawns a fresh, independently-healing wound card every round with no dedup; the count is accurate, not a display bug, so removing it is a combat design change, not a bug fix. The hit's own damage, armor wear and the encounter log are unaffected. Confirmed in-game (2026-09-07). Leave this false to choose per run instead: a game started with the character screen's Easy Mode toggle ticked consolidates wounds for that run only.");

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

            PartnersTendTheHearth = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Partners Tend The Hearth",
                false,
                "Lets you mark a Hearth or a Hearth (Awakened) for Fire Keeping, so a Partner feeds it Firewood, fuel and Charcoal like a Campfire (the mark survives the fire dying), and lets a Partner Warm Up at it. The Hearth only accepts a feed while its fuel is below 50% (a placeholder limit, tunable in a later version), which also applies to you; Campfires and Fireplaces are unchanged. Default OFF: vanilla gives the Hearth no Partner duties, and it is already the strongest fire in the game. Up to 1.1.2 this was always on. With it off, a Hearth mark already in a save does nothing and is not lost. Read at game load, so changing it needs a restart; BepInEx keeps a value already written in an existing .cfg.");

            PartnerStartsConversations = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Partner Starts Conversations",
                false,
                "Lets your Partner start a short conversation with you by itself (the same talk as \"Talk with <name>\"), at most once a day, only while it is on your board and more likely in the morning. It never interrupts a ritual, and it stays quiet while it is hungry, cold, guarded, neglected, stressed or close to leaving, so the game's own requests always come first. Each line takes 15 minutes and gives no stats. Also gives the game's own Partner requests a voice: after the narration (\"Your partner approaches you and expresses the urgent need for food.\") and Continue, the Partner says it in its own words, worded by its personality and whether it is stressed or happy; the game's effects of the request apply once, unchanged (Express Blame and Express Ultimatum stay exactly as the game wrote them). Needs CSFFModFramework. Default OFF: vanilla's Partner never starts small talk and its requests are narration only. Read at game load, so changing it needs a restart; BepInEx keeps a value already written in an existing .cfg.");

            PartnerTalkAnswer = Config.Bind(
                "Conversations",
                "Talk With Your Partner",
                true,
                "Adds a \"Talk with <name>\" answer to your Partner's Talk menu that opens a short spoken conversation. Each line it says takes 15 minutes and, like Casual Talk, puts off the Partner's own next remark; it gives no stats. Needs CSFFModFramework, which loads the conversation: without it the Talk menu is exactly vanilla. Default ON because you choose to start it. Changing it needs a restart.");

            ExpeditionStories = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Expedition Stories",
                false,
                "After your Partner returns from a foraging, hunting or fishing expedition and you press Continue on its report, it says a short line about what it brought back (mushrooms, berries, a big catch) instead of the report simply ending. Words only: each line takes 15 minutes, puts off the Partner's own next remark, and gives no stats; the game's report and its effects are unchanged. Needs CSFFModFramework. Default OFF: vanilla's report ends at Continue. Read at game load, so changing it needs a restart; BepInEx keeps a value already written in an existing .cfg.");

            PartnerEmberPot = Config.Bind(
                "QoL (opt-in feature, not a bug fix)",
                "Ember Pot",
                false,
                "Adds an Ember Pot to the Survival > Fire blueprints (a Clay Bowl and a Rope). Drag it onto a burning fire to fill it with embers (the fire loses fuel), then put it in your Partner's inventory: it wears the pot at its belt and feels a little warmer, less than beside a fire, so a following Partner lasts longer on a cold night with no fire. The embers burn about six hours; add Charcoal to keep them going. It warms you too if you wear it yourself. Needs CSFFModFramework. Default OFF: vanilla gives a Partner no way to carry warmth. With it off the blueprint is hidden (unless you already researched it) and a pot already in your save gives no warmth. Read at game load, so changing it needs a restart; BepInEx keeps a value already written in an existing .cfg.");

            try { Patcher.GameLoadPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"GameLoadPatch failed: {ex}"); }

            try { Patcher.NpcSoundEnvGatePatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"NpcSoundEnvGatePatch failed: {ex}"); }

            try { Patcher.DialogInterruptPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"DialogInterruptPatch failed: {ex}"); }

            try { Patcher.PartnerNameInDialogPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerNameInDialogPatch failed: {ex}"); }

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

            try { Patcher.PartnerTalkAnswerPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerTalkAnswerPatch failed: {ex}"); }

            try { Patcher.PartnerCmcTopicPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerCmcTopicPatch failed: {ex}"); }

            try { Patcher.PartnerMemoryPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerMemoryPatch failed: {ex}"); }

            try { Patcher.PartnerStartsTalkDutyPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerStartsTalkDutyPatch failed: {ex}"); }

            try { Patcher.PartnerExpeditionStoryPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerExpeditionStoryPatch failed: {ex}"); }

            try { Patcher.PartnerFirstVisitPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerFirstVisitPatch failed: {ex}"); }

            try { Patcher.PartnerExpressVoicePatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"PartnerExpressVoicePatch failed: {ex}"); }
            try { Patcher.EmberPotPatch.ApplyPatch(_harmony); }
            catch (Exception ex) { Logger.LogError($"EmberPotPatch failed: {ex}"); }

            Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            Patcher.PartnerMemoryPatch.Unsubscribe();
            Patcher.EmberPotPatch.Unsubscribe();
            _harmony?.UnpatchSelf();
        }
    }
}
