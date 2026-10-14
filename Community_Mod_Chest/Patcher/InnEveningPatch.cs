using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Evenings at the Inn (CMC 1.69.0, Documentation/Design/CMC_Content_Update_1_69_0_As_Built.md features 4 and 5).
    ///   - Every 5 s, publishes cmcStatInnCompany: how many of the Miller, Weaver, Apothecary and
    ///     Professor are in cmcInnInterior with the player, or 0 whenever the player is elsewhere.
    ///     Each resident is counted by its OWN CurrentEnvironment (the IsOwnerHome idiom in
    ///     CopperChestPatch), never by an AllCards scan, which is current-environment-scoped and
    ///     would count carried or background cards (CLAUDE.md "Runtime Card Spawning").
    ///   - The Inn Counter's "Join the Table" DA and "Play for the Room" CI are gated in JSON on
    ///     company [2, 4] and cmcStatInnEveningCooldown [0, 0]; their mood, cooldown and Inn
    ///     friendship effects are JSON StatModifications. The AfterWrapped handlers here add the
    ///     one thing JSON cannot express: +2 / +3 trust to each resident ACTUALLY at the table,
    ///     re-counted at the moment the action finishes rather than read from the 5 s snapshot.
    ///   - With the Quiet Village perk the cottage hour clock never sends the Miller and Weaver
    ///     here (CottageResidentSchedulePatch), so the table rarely fills: intended, documented.
    /// Registration runs once per process from Plugin.Awake under an _initialized guard
    /// (CLAUDE.md "Per-Run Event Registration", pattern b), so reloads never duplicate handlers.
    /// </summary>
    internal static class InnEveningPatch
    {
        private const string CounterUid = "cmcInnCounter";
        private const string InnInteriorUid = "cmcInnInterior";
        private const string CompanyStatUid = "cmcStatInnCompany";
        internal const string JoinTableKeyPrefix = "CMC_InnCounter_JoinTable";
        internal const string PlayForRoomKeyPrefix = "CMC_InnCounter_PlayForRoom";
        private const float JoinTableTrust = 2f;
        private const float PlayForRoomTrust = 3f;
        private const float TrustMax = 100f;

        internal sealed class Resident
        {
            public string Name;
            public string AgentUid;
            public string TrustStatUid;
        }

        private static readonly Resident[] Residents =
        {
            new Resident { Name = "the Miller",     AgentUid = "cmcMillerAgent",     TrustStatUid = "cmcStatMillerTrust" },
            new Resident { Name = "the Weaver",     AgentUid = "cmcWeaverAgent",     TrustStatUid = "cmcStatWeaverTrust" },
            new Resident { Name = "the Apothecary", AgentUid = "cmcApothecaryAgent", TrustStatUid = "cmcStatApothecaryTrust" },
            new Resident { Name = "the Professor",  AgentUid = "cmcProfessorAgent",  TrustStatUid = "cmcStatProfessorTrust" },
        };

        private static bool _initialized;
        private static MethodInfo _getFromIdMethod;
        private static readonly Dictionary<string, object> _statCache = new(StringComparer.Ordinal);
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.Interval(5f, PublishCompany, "InnEveningCompany");

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnEveningJoinTable",
                CardUid         = CounterUid,
                ActionKeyPrefix = JoinTableKeyPrefix,
                Timing          = ActionTiming.AfterWrapped,
                After           = ctx => RewardTable(JoinTableTrust, "Join the Table"),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnEveningPlayForRoom",
                CardUid         = CounterUid,
                ActionKeyPrefix = PlayForRoomKeyPrefix,
                Timing          = ActionTiming.AfterWrapped,
                After           = ctx => RewardTable(PlayForRoomTrust, "Play for the Room"),
            });

            Plugin.Logger.LogDebug("[InnEveningPatch] initialized.");
        }

        // ── Company count ─────────────────────────────────────────────────────────────────────

        private static void PublishCompany()
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return; // main menu: nothing to publish

                int company = GameQuery.CurrentEnvironmentUniqueId == InnInteriorUid
                    ? PresentResidents(gm).Count
                    : 0;

                float current = ReadStat(gm, CompanyStatUid);
                if (current < 0f)
                {
                    WarnOnce("company-read", $"[InnEveningPatch] {CompanyStatUid} is not readable - Join the Table and Play for the Room stay hidden.");
                    return;
                }
                if (Math.Abs(current - company) < 0.01f) return;

                if (WriteStat(gm, CompanyStatUid, company))
                    Plugin.Logger.LogDebug($"[InnEveningPatch] Inn company {current:0} -> {company}.");
                else
                    WarnOnce("company-write", $"[InnEveningPatch] {CompanyStatUid} write failed - the Inn evening actions will not track who is present.");
            }
            catch (Exception ex)
            {
                WarnOnce("publish-exception", $"[InnEveningPatch] PublishCompany failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>The residents in cmcInnInterior right now, each with its live InGameNPC.</summary>
        internal static List<KeyValuePair<Resident, object>> PresentResidents(object gm) =>
            PresentResidentsIn(gm, InnInteriorUid);

        /// <summary>The residents in the given environment UID right now (the read AcademyGuestPatch
        /// reuses for cmcAcademyInterior), each with its live InGameNPC.</summary>
        internal static List<KeyValuePair<Resident, object>> PresentResidentsIn(object gm, string envUid)
        {
            var present = new List<KeyValuePair<Resident, object>>();
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
            {
                WarnOnce("allnpcs-null", "[InnEveningPatch] GameManager.AllNPCs not readable - the Inn table always reads empty.");
                return present;
            }

            bool sawAnyModel = false;
            bool sawAnyNpc = false;
            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                sawAnyNpc = true;
                var model = Reflect.GetMember(npc, "NPCModel");
                if (model == null) continue;
                sawAnyModel = true;

                string agentUid = CardUtil.GetCardUniqueId(model);
                var resident = Residents.FirstOrDefault(r => string.Equals(r.AgentUid, agentUid, StringComparison.OrdinalIgnoreCase));
                if (resident == null) continue;
                if (present.Any(p => p.Key == resident)) continue; // a duplicate NPC instance counts once

                var env = Reflect.GetMember(npc, "CurrentEnvironment");
                if (env == null || Reflect.GetMember(env, "IsNull") is true) continue; // not placed anywhere yet
                var envCard = Reflect.GetMember(env, "EnvCard");
                if (envCard == null)
                {
                    WarnOnce("envcard-null", "[InnEveningPatch] a resident's CurrentEnvironment.EnvCard is not readable - that resident never counts as present.");
                    continue;
                }
                if (string.Equals(CardUtil.GetCardUniqueId(envCard), envUid, StringComparison.OrdinalIgnoreCase))
                    present.Add(new KeyValuePair<Resident, object>(resident, npc));
            }

            if (sawAnyNpc && !sawAnyModel)
                WarnOnce("npcmodel-null", "[InnEveningPatch] no InGameNPC exposed an NPCModel - the Inn table always reads empty.");
            return present;
        }

        // ── Trust reward ──────────────────────────────────────────────────────────────────────

        private static void RewardTable(float trust, string actionLabel)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null)
                {
                    WarnOnce("reward-gm-null", $"[InnEveningPatch] {actionLabel}: GameManager not found - no trust awarded.");
                    return;
                }

                var present = PresentResidents(gm);
                if (present.Count == 0)
                {
                    // The JSON gate needs 2+, so this means everyone left between the 5 s publish
                    // and the end of the hour. Not an error; nobody is there to warm to you.
                    Plugin.Logger.LogDebug($"[InnEveningPatch] {actionLabel}: nobody left at the table - no trust awarded.");
                    return;
                }

                var names = new List<string>();
                foreach (var pair in present)
                {
                    if (AddNpcTrust(pair.Value, pair.Key, trust)) names.Add(pair.Key.Name);
                }
                Plugin.Logger.LogInfo($"[InnEveningPatch] {actionLabel}: +{trust:0} trust with {(names.Count > 0 ? string.Join(", ", names) : "nobody (writes failed)")}.");
            }
            catch (Exception ex)
            {
                WarnOnce("reward-exception", $"[InnEveningPatch] {actionLabel} reward failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>Adds to a live resident's NPC trust stat with the same SetStatValueFromEditor
        /// idiom as CopperChestPatch.AddResidentTrust (a local copy: that one is private and keyed
        /// on a chest config).</summary>
        internal static bool AddNpcTrust(object npc, Resident resident, float amount)
        {
            var stat = StatRef(resident.TrustStatUid);
            if (stat == null)
            {
                WarnOnce("trust-stat-" + resident.TrustStatUid, $"[InnEveningPatch] {resident.TrustStatUid} did not resolve - {resident.Name} gains no Inn trust.");
                return false;
            }
            if (Reflect.GetMember(npc, "NPCStatsDict") is not IDictionary statsDict || !statsDict.Contains(stat))
            {
                WarnOnce("trust-dict-" + resident.AgentUid, $"[InnEveningPatch] {resident.Name} has no {resident.TrustStatUid} in NPCStatsDict - no Inn trust.");
                return false;
            }
            var inGameStat = statsDict[stat];
            if (inGameStat == null)
            {
                WarnOnce("trust-stat-null-" + resident.AgentUid, $"[InnEveningPatch] {resident.Name}'s {resident.TrustStatUid} entry is null - no Inn trust.");
                return false;
            }

            var getStatValue = npc.GetType().GetMethod("GetStatValue",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { stat.GetType() }, null);
            if (getStatValue == null)
            {
                WarnOnce("getstatvalue-null", "[InnEveningPatch] InGameNPC.GetStatValue not found - no Inn trust.");
                return false;
            }
            var setter = inGameStat.GetType().GetMethod("SetStatValueFromEditor",
                BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(float) }, null);
            if (setter == null)
            {
                WarnOnce("setter-null", "[InnEveningPatch] InGameNPCStat.SetStatValueFromEditor not found - no Inn trust.");
                return false;
            }

            float current = getStatValue.Invoke(npc, new[] { stat }) is float f ? f : 0f;
            float next = Math.Min(TrustMax, current + amount);
            setter.Invoke(inGameStat, new object[] { next });
            Plugin.Logger.LogDebug($"[InnEveningPatch] {resident.Name} trust {current:0.0} -> {next:0.0}.");
            return true;
        }

        // ── Stat helpers (local copies of the VillageFounderPerkPatch idiom) ───────────────────

        private static bool ResolveGetFromId()
        {
            if (_getFromIdMethod != null) return true;
            var uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (uidType == null) return false;
            _getFromIdMethod = uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return _getFromIdMethod != null;
        }

        private static object StatRef(string uid)
        {
            if (_statCache.TryGetValue(uid, out var cached)) return cached;
            if (!ResolveGetFromId())
            {
                WarnOnce("getfromid-null", "[InnEveningPatch] UniqueIDScriptable.GetFromID(string) not found - Inn evening stats inactive.");
                return null;
            }
            var stat = _getFromIdMethod.Invoke(null, new object[] { uid });
            if (stat != null) _statCache[uid] = stat;
            return stat;
        }

        internal static float ReadStat(object gm, string uid)
        {
            var stat = StatRef(uid);
            if (stat == null) return -1f;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return -1f;
            if (!statsDict.Contains(stat)) return -1f;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return -1f;
            return Reflect.GetMember(inGameStat, "SimpleCurrentValue") is float f ? f : -1f;
        }

        internal static bool WriteStat(object gm, string uid, float value)
        {
            var stat = StatRef(uid);
            if (stat == null) return false;
            if (Reflect.GetMember(gm, "StatsDict") is not IDictionary statsDict) return false;
            if (!statsDict.Contains(stat)) return false;
            var inGameStat = statsDict[stat];
            if (inGameStat == null) return false;
            return Reflect.SetMember(inGameStat, "CurrentBaseValue", value);
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
