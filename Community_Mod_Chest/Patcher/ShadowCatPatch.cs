using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Shadow the Cat (Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §3.6/§10.7) — a second,
    /// wholly independent companion-cat chain modeled on LostCatPatch.cs. Shares zero
    /// UIDs/files/state with Ash (cmcStrayCat/cmcInnCat/cmcStatLostCat) — Ash is untouchable
    /// (see memory: user_cat_ashton).
    ///
    /// No hidden GameStat gates the spawn. Unlike Ash's hand-authored state machine, the
    /// gate here is AcademyCourseService.HasCourse(GradHerbalism), which returns true
    /// forever once earned (perks never un-grant) — so the dupe-guard
    /// (ShadowExistsAnywhere, scanning GameManager.AllCards for either cat form) is the
    /// ONLY thing stopping a repeat spawn every 2-second tick and is load-bearing here in
    /// a way it isn't for Ash.
    /// </summary>
    internal static class ShadowCatPatch
    {
        private const string UntamedUid = "cmcShadowCat";
        private const string TamedUid = "cmcShadowCatTamed";
        private const string ApothecaryCabinUid = "cmcApothecaryCabin";
        private const string ApothecaryCabinBlueprintUid = "BpCMCApothecaryCabin"; // construction model on board during build

        // Same forest as Ash (cmcEnvForagingForest) — deliberate design tension, flagged in
        // the master plan §9.7: differentiate narratively (nocturnal/pitch-black) rather than
        // by hour window, since both prowl dusk-to-dawn.
        private const string TargetEnvUid = "cmcEnvForagingForest";
        private const float ProwlStartHour = 17f; // dusk
        private const float ProwlEndHour = 6f;    // dawn

        private static bool _initialized;
        private static bool _cabinBuilt;  // Once true in this session, stays true — Shadow never respawns

        private static Type _gmType;
        private static MethodInfo _getFromIdMethod; // UniqueIDScriptable.GetFromID(string)

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(2f, CheckAndSpawn, "ShadowCatProwlCheck");
            Plugin.Logger.LogDebug("[ShadowCatPatch] initialized.");
        }

        private static bool ResolveTypes()
        {
            if (_gmType != null && _getFromIdMethod != null) return true;

            _gmType = CardUtil.FindGameType("GameManager");
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (_gmType == null || uidType == null) return false;

            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static void CheckAndSpawn()
        {
            try
            {
                if (!ResolveTypes()) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                // Gate 1: Check if cabin has been built in this session (persistent across board state transitions)
                if (_cabinBuilt) return;

                // Cheap gates next: hour window, then environment, then the perk check
                var hour = GameQuery.HourOfDay;
                if (hour < ProwlStartHour && hour >= ProwlEndHour) return;

                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, TargetEnvUid, StringComparison.Ordinal)) return;

                if (!AcademyCourseService.HasCourse(AcademyCourseService.GradHerbalism)) return;

                // Gate 2: cabin already built
                if (ApothecaryCabinExists(gm))
                {
                    _cabinBuilt = true;
                    return;
                }

                // Gate 3: final construction stage in progress — tamed Shadow was consumed but cabin not yet placed
                if (CabinUnderFinalStage(gm))
                {
                    _cabinBuilt = true;
                    return;
                }

                // Gate 4: dupe-guard
                if (ShadowExistsAnywhere(gm)) return;

                // SpawnService.Spawn returns null on success (GiveCard is void this game
                // version) — verify by re-querying the board, not the return value.
                SpawnService.Spawn(UntamedUid);
                if (ShadowExistsAnywhere(gm))
                    Plugin.Logger.LogInfo("[ShadowCatPatch] Shadow slips out from between the pines.");
                else
                    Plugin.Logger.LogWarning("[ShadowCatPatch] Shadow cat did not appear after spawn — is cmcShadowCat loaded?");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[ShadowCatPatch] CheckAndSpawn failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool ShadowExistsAnywhere(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                var uid = Reflect.GetMember(model, "UniqueID") as string;
                if (uid == UntamedUid || uid == TamedUid) return true;
            }
            return false;
        }

        private static bool ApothecaryCabinExists(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                var uid = Reflect.GetMember(model, "UniqueID") as string;
                if (uid == ApothecaryCabinUid) return true;
            }
            return false;
        }

        // Blueprint model on board + tamed Shadow absent = final stage is consuming Shadow
        private static bool CabinUnderFinalStage(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return false;
            bool blueprintModelFound = false;
            bool tamedFound = false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                var uid = Reflect.GetMember(model, "UniqueID") as string;
                if (uid == ApothecaryCabinBlueprintUid) blueprintModelFound = true;
                if (uid == TamedUid) tamedFound = true;
            }
            return blueprintModelFound && !tamedFound;
        }
    }
}
