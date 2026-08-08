using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Inn Keeper's lost cat (Documentation/Design/Village_InnKeeper_Plan.md, Chat-dialog arc).
    ///
    /// State machine lives in the hidden GameStat cmcStatLostCat (GameStat/CMC_LostCat.json),
    /// written ONLY by authored JSON actions — this patch never writes it:
    ///   0 = story not heard; 1 = Inn Keeper's dialog answer armed the search (+1 via
    ///   DialogAction.StatModifications); 2 = cat befriended (+1 via the Stray Cat's feed CI);
    ///   3 = Inn Keeper thanked, reward given (+1 via the thank-you dialog line).
    ///
    /// This patch only handles the one thing JSON cannot: spawning the Stray Cat card when the
    /// player is standing in the right environment during the right hours while the stat is
    /// exactly in the "armed" band. Spawn goes through Api.SpawnService (framework-sanctioned;
    /// avoids the raw GiveCard reflection dance). Dupe-safety is a global GameManager.AllCards
    /// scan for either cat form — checked LAST because it is the only non-trivial gate.
    /// </summary>
    internal static class LostCatPatch
    {
        private const string QuestStatUid = "cmcStatLostCat";
        private const string StrayCatUid = "cmcStrayCat";
        private const string InnCatUid = "cmcInnCat";

        // Where and when the cat prowls — must stay consistent with the Inn Keeper's dialog text
        // (CMC_InnKeeperTalk: "seen prowling the Foraging Forest around dusk").
        private const string TargetEnvUid = "cmcEnvForagingForest";
        private const float ProwlStartHour = 17f; // dusk
        private const float ProwlEndHour = 6f;    // dawn

        private static bool _initialized;

        private static Type _gmType;
        private static MethodInfo _getFromIdMethod; // UniqueIDScriptable.GetFromID(string)
        private static object _questStat;           // GameStat SO

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(2f, CheckAndSpawn, "LostCatProwlCheck");
            Plugin.Logger.LogDebug("[LostCatPatch] initialized.");
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

                // Cheap gates first: hour window, then environment, then quest stat.
                var hour = GameQuery.HourOfDay;
                if (hour < ProwlStartHour && hour >= ProwlEndHour) return;

                if (!string.Equals(GameQuery.CurrentEnvironmentUniqueId, TargetEnvUid, StringComparison.Ordinal)) return;

                var statValue = ReadQuestStat(gm);
                if (statValue < 0.5f || statValue >= 1.5f) return; // only the "armed" band spawns

                if (CatExistsAnywhere(gm)) return;

                var spawned = SpawnService.Spawn(StrayCatUid);
                if (spawned != null)
                    Plugin.Logger.LogInfo("[LostCatPatch] The stray cat slinks out of the undergrowth.");
                else
                    Plugin.Logger.LogWarning("[LostCatPatch] Spawn returned null — is cmcStrayCat loaded?");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[LostCatPatch] CheckAndSpawn failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static float ReadQuestStat(object gm)
        {
            _questStat ??= _getFromIdMethod.Invoke(null, new object[] { QuestStatUid });
            if (_questStat == null) return -1f;

            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return -1f;
            if (!statsDict.Contains(_questStat)) return -1f;
            var inGameStat = statsDict[_questStat];
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        private static bool CatExistsAnywhere(object gm)
        {
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return false;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                var model = Reflect.GetMember(card, "CardModel");
                if (model == null) continue;
                var uid = Reflect.GetMember(model, "UniqueID") as string;
                if (uid == StrayCatUid || uid == InnCatUid) return true;
            }
            return false;
        }
    }
}
