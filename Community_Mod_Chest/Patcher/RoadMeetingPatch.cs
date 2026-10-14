using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Chance meetings on the road (CMC round 2, plan feature C6). Every 3 s, when the player stands on
    /// an outdoor tile that is neither a resident's home door nor that resident's work post and that
    /// resident is standing on the same tile, the resident stops for a word: Trust +<see cref="TrustGain"/>,
    /// a small Morale lift, one line in the game's own message popup, and a per-resident cooldown stat
    /// (<see cref="CooldownTicks"/> ticks) so it happens at most once per resident every few days.
    ///
    /// Who can meet you (Documentation/Research/Community_Mod_Chest/engine_reads.md W0.8, read from the
    /// schedule patches): ONLY the Apothecary and the Professor. The Miller and Weaver have no outdoor
    /// tile that is neither their home door nor their work post (their one outdoor tile, cmcEnvVillage, is
    /// both), so they are deliberately absent from <see cref="Specs"/>.
    ///   - Apothecary: cmcEnvPineTrail, cmcEnvHighGrove, cmcEnvMossyClearing (her home door tile is
    ///     cmcEnvForagingForest, her work and stall are in cmcEnvVillage).
    ///   - Professor (foraging phase): cmcEnvVillagePath, cmcEnvVillageFarm, cmcEnvForagingForest (his home
    ///     and work is the Academy, whose door tile is cmcEnvVillage).
    /// Presence is each resident's OWN CurrentEnvironment.EnvCard unique id compared as a string with the
    /// player's GameQuery.CurrentEnvironmentUniqueId (the InnEveningPatch read), never an AllCards scan
    /// and never a cached EnvID. With the Quiet Village perk the residents do not commute, so the poll
    /// returns at once and nothing fires.
    ///
    /// Registration runs once per process from Plugin.Awake under an _initialized guard (CLAUDE.md
    /// "Per-Run Event Registration", pattern b), so reloads never duplicate the poll.
    /// </summary>
    internal static class RoadMeetingPatch
    {
        // TUNABLE placeholders (plan open questions; the closeout may retune them).
        /// <summary>Ticks of cooldown after a meeting. 72 ticks is 18 in-game hours. The two cooldown
        /// GameStats must allow at least this much (their MinMaxValue y), which the round-2 gate asserts.</summary>
        internal const float CooldownTicks = 72f;
        internal const float TrustGain = 1f;
        internal const float MoraleLift = 3f;

        internal const string MoraleStatUid = "4a27fb5da9326b545a5ef73f2b80316e";
        internal const string TitleKey = "CMC_RoadMeeting_Title";
        private const float PollSeconds = 3f;

        private sealed class Spec
        {
            public string Name;
            public string AgentUid;
            public string TrustStatUid;
            public string CooldownStatUid;
            public string LineKey;
            public string[] Tiles;
        }

        private static readonly Spec[] Specs =
        {
            new Spec
            {
                Name = "the Apothecary", AgentUid = "cmcApothecaryAgent", TrustStatUid = "cmcStatApothecaryTrust",
                CooldownStatUid = "cmcStatRoadMeetApothecary", LineKey = "CMC_RoadMeeting_Apothecary",
                Tiles = new[] { "cmcEnvPineTrail", "cmcEnvHighGrove", "cmcEnvMossyClearing" },
            },
            new Spec
            {
                Name = "the Professor", AgentUid = "cmcProfessorAgent", TrustStatUid = "cmcStatProfessorTrust",
                CooldownStatUid = "cmcStatRoadMeetProfessor", LineKey = "CMC_RoadMeeting_Professor",
                Tiles = new[] { "cmcEnvVillagePath", "cmcEnvVillageFarm", "cmcEnvForagingForest" },
            },
        };

        private static bool _initialized;
        private static FieldInfo _localizationTextsField;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            TickEvents.Interval(PollSeconds, Poll, "RoadMeeting");
            Plugin.Logger.LogDebug("[RoadMeetingPatch] initialized.");
        }

        private static void Poll()
        {
            try
            {
                string playerEnv = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(playerEnv)) return; // main menu or mid-travel: nothing to read
                if (!Specs.Any(s => s.Tiles.Contains(playerEnv, StringComparer.OrdinalIgnoreCase))) return;
                if (QuietVillagePerkPatch.IsActive()) return; // residents do not commute

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;

                foreach (var spec in Specs)
                {
                    if (!spec.Tiles.Contains(playerEnv, StringComparer.OrdinalIgnoreCase)) continue;

                    float cooldown = HiddenStat.Get(spec.CooldownStatUid);
                    if (cooldown < 0f)
                    {
                        WarnOnce("cooldown-read-" + spec.CooldownStatUid, $"[RoadMeetingPatch] {spec.CooldownStatUid} is not readable - {spec.Name} never stops for a word on the road.");
                        continue;
                    }
                    if (cooldown > 0.01f) continue;

                    var npc = FindResidentOnTile(gm, spec, playerEnv);
                    if (npc == null) continue;

                    Meet(npc, spec, playerEnv);
                }
            }
            catch (Exception ex)
            {
                WarnOnce("poll-exception", $"[RoadMeetingPatch] Poll failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>The live InGameNPC for this resident when its own CurrentEnvironment is the tile the
        /// player stands on, else null.</summary>
        private static object FindResidentOnTile(object gm, Spec spec, string playerEnv)
        {
            if (Reflect.GetMember(gm, "AllNPCs") is not IEnumerable allNpcs)
            {
                WarnOnce("allnpcs-null", "[RoadMeetingPatch] GameManager.AllNPCs not readable - nobody stops for a word on the road.");
                return null;
            }

            foreach (var npc in allNpcs)
            {
                if (npc == null) continue;
                var model = Reflect.GetMember(npc, "NPCModel");
                if (model == null) continue;
                if (!string.Equals(CardUtil.GetCardUniqueId(model), spec.AgentUid, StringComparison.OrdinalIgnoreCase)) continue;

                var env = Reflect.GetMember(npc, "CurrentEnvironment");
                if (env == null || Reflect.GetMember(env, "IsNull") is true) return null; // not placed anywhere yet
                var envCard = Reflect.GetMember(env, "EnvCard");
                if (envCard == null)
                {
                    WarnOnce("envcard-null", "[RoadMeetingPatch] a resident's CurrentEnvironment.EnvCard is not readable - that resident never meets you on the road.");
                    return null;
                }
                return string.Equals(CardUtil.GetCardUniqueId(envCard), playerEnv, StringComparison.OrdinalIgnoreCase) ? npc : null;
            }
            return null;
        }

        private static void Meet(object npc, Spec spec, string tileUid)
        {
            // The cooldown is written FIRST: if it cannot be written the meeting is skipped entirely,
            // because trust without the latch would repeat on every poll while both stand on the tile.
            // Clamped to the stat's own live maximum (a value past the top would land on it anyway).
            float cooldownMax = HiddenStat.GetMax(spec.CooldownStatUid);
            float cooldownValue = cooldownMax > 0f ? Math.Min(CooldownTicks, cooldownMax) : CooldownTicks;
            if (!HiddenStat.Set(spec.CooldownStatUid, cooldownValue))
            {
                WarnOnce("cooldown-write-" + spec.CooldownStatUid, $"[RoadMeetingPatch] {spec.CooldownStatUid} could not be written - {spec.Name} does not stop for a word, so the meeting cannot repeat every poll.");
                return;
            }

            var resident = new InnEveningPatch.Resident { Name = spec.Name, AgentUid = spec.AgentUid, TrustStatUid = spec.TrustStatUid };
            bool trusted = InnEveningPatch.AddNpcTrust(npc, resident, TrustGain);
            bool lifted = RaiseMorale();
            if (!lifted)
                WarnOnce("morale-write", $"[RoadMeetingPatch] Morale ({MoraleStatUid}) could not be raised - a road meeting gives trust and a line but no mood lift.");

            Plugin.Logger.LogInfo($"[RoadMeetingPatch] met {spec.Name} on {tileUid}: trust {(trusted ? "+" + TrustGain.ToString("0") : "not written")}, morale {(lifted ? "+" + MoraleLift.ToString("0") : "not raised")}, cooldown {CooldownTicks:0} ticks.");

            ShowLine(spec);
        }

        /// <summary>Morale + MoraleLift, held to the stat's live maximum. False when Morale is unreadable.</summary>
        private static bool RaiseMorale()
        {
            float current = HiddenStat.Get(MoraleStatUid);
            if (current < 0f) return false;
            float max = HiddenStat.GetMax(MoraleStatUid);
            float next = current + MoraleLift;
            if (max > 0f) next = Math.Min(next, max);
            return HiddenStat.SetWithStatuses(MoraleStatUid, next);
        }

        private static void ShowLine(Spec spec)
        {
            string title = LocalizedText(TitleKey);
            string line = LocalizedText(spec.LineKey);
            if (string.IsNullOrWhiteSpace(line))
            {
                WarnOnce("line-" + spec.LineKey, $"[RoadMeetingPatch] No localization row for '{spec.LineKey}', so the meeting with {spec.Name} shows no line.");
                return;
            }
            // Reflection, not compile-time: the game's own SimpleMessagePopup (the popup the framework's
            // PortalService uses for its no-op feedback) is not in every reference copy of Assembly-CSharp.
            var gmType = CardUtil.FindGameType("GraphicsManager");
            var instance = gmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            var popup = instance == null ? null : Reflect.GetMember(instance, "MessagePopup");
            var setup = popup?.GetType().GetMethod("Setup", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(string), typeof(string), typeof(UnityEngine.Sprite) }, null);
            if (popup == null || setup == null)
            {
                WarnOnce("popup-null", "[RoadMeetingPatch] GraphicsManager.Instance.MessagePopup.Setup is unavailable - road meetings show no line.");
                return;
            }
            setup.Invoke(popup, new object[] { string.IsNullOrWhiteSpace(title) ? "" : title.Trim(), line.Trim(), null });
        }

        /// <summary>The current-language text of a CMC localization key (LocalizationManager.CurrentTexts, the
        /// lookup VillageHallBoardsPatch makes), with no English copy kept here: the CSV row is the only text.</summary>
        internal static string LocalizedText(string key)
        {
            if (_localizationTextsField == null)
            {
                var locType = CardUtil.FindGameType("LocalizationManager");
                _localizationTextsField = locType?.GetField("CurrentTexts",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (_localizationTextsField == null)
                {
                    WarnOnce("texts-field", "[RoadMeetingPatch] LocalizationManager.CurrentTexts was not found, so road meetings show no line.");
                    return null;
                }
            }
            return _localizationTextsField.GetValue(null) is Dictionary<string, string> texts && texts.TryGetValue(key, out var text)
                ? text
                : null;
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
