using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using UnityEngine;

namespace Skill_Speed_Boost.Patcher
{
    /// <summary>
    /// Finds the skill GameStats and applies the staleness settings to them. The actual XP
    /// multiplier is applied at runtime by MorningBonusPatch's ChangeStatValue postfix; this class
    /// only owns the four novelty fields each skill's GameStat carries (UsesNovelty,
    /// StalenessMultiplier, MaxStalenessStack, NoveltyCooldownDuration).
    ///
    /// Those fields are read LIVE from the stat model every time the game uses them
    /// (InGameStat.RegisterAction, GetStalenessModifier and UpdateTicks, and the UsesNovelty test in
    /// GameManager.ChangeStat), so a write lands on the next action. Until 1.11.0 the only write was
    /// the GameLoad.LoadMainGameData postfix, which runs once per PROCESS, so a staleness change made
    /// in-game did nothing until the game was quit and relaunched, and the docs promised a save
    /// reload would do it, which it never did. Now the same write runs from three places, all
    /// through <see cref="ApplyStalenessTo"/>: the load scan, every run start (the
    /// GameManager.InitializeStatsAndActions postfix), and the moment a staleness setting changes.
    ///
    /// The staleness-off write is destructive (UsesNovelty false, StalenessMultiplier 0,
    /// MaxStalenessStack 0), so each skill's four fields are captured the first time this class
    /// sees it, before it has written anything, and every later write is computed from those
    /// originals plus the current settings. That is also why the re-apply walks the CAPTURED set:
    /// after an off-write a rescan keyed on UsesNovelty would find no skills at all.
    /// </summary>
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        // Populated during GameStat scan; consumed by MorningBonusPatch.IsSkillStat and the
        // per-skill multiplier lookup in the same postfix.
        internal static readonly HashSet<string> SkillUniqueIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, string> SkillNamesByUniqueId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Vanilla stats that set UsesNovelty but are NOT skills. Measured on the EA 0.67i export:
        // 41 GameStats set UsesNovelty; 31 are Skill_*, the first 9 below are mental stats, and
        // the 10th (Perception) IS a skill and is documented as one. EA 0.68 added Arousal, which
        // also sets UsesNovelty: on a live 0.68 game this scan admitted it as a 34th "skill"
        // (read back from SkillNamesByUniqueId, 2026-09-19), so every XP multiplier scaled its
        // gains and the staleness settings rewrote its novelty fields. A game update can add
        // another such stat, so re-read that dictionary after each one. Every name below matches
        // its GameName.DefaultText exactly, which is what the Ordinal comparer requires.
        internal static readonly HashSet<string> IgnoredNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Stress",
            "Morale",
            "Profile",
            "Altered Mindstate",
            "Mental Structure",
            "Focus",
            "Gratification",
            "Loneliness",
            "Thought Depth",
            "Arousal"
        };

        /// <summary>
        /// The four novelty fields of one GameStat, as read from it or as about to be written to it.
        /// Pure data with no game types, so the arithmetic below can be compiled and run by
        /// Development_Tools/Tests/SkillSpeedBoost-BonusLogic.Tests.ps1 (which needs C# 5 here).
        /// </summary>
        internal struct NoveltyFields
        {
            public readonly bool UsesNovelty;
            public readonly float StalenessMultiplier;
            public readonly int MaxStalenessStack;
            public readonly int NoveltyCooldownDuration;

            public NoveltyFields(bool usesNovelty, float stalenessMultiplier, int maxStalenessStack, int noveltyCooldownDuration)
            {
                UsesNovelty = usesNovelty;
                StalenessMultiplier = stalenessMultiplier;
                MaxStalenessStack = maxStalenessStack;
                NoveltyCooldownDuration = noveltyCooldownDuration;
            }

            public bool SameAs(NoveltyFields other)
            {
                return UsesNovelty == other.UsesNovelty
                    && StalenessMultiplier == other.StalenessMultiplier
                    && MaxStalenessStack == other.MaxStalenessStack
                    && NoveltyCooldownDuration == other.NoveltyCooldownDuration;
            }

            public override string ToString()
            {
                return "UsesNovelty=" + UsesNovelty + ", StalenessMultiplier=" + StalenessMultiplier
                    + ", MaxStalenessStack=" + MaxStalenessStack + ", NoveltyCooldownDuration=" + NoveltyCooldownDuration;
            }
        }

        /// <summary>One captured skill: its stat, its name and the values to restore.</summary>
        private sealed class CapturedSkill
        {
            public object Stat;
            public string Name;
            public NoveltyFields Originals;
            // What the stat held right after this mod's last write, read back from it. A field that
            // differs from this at the next apply was changed by someone else since (see RebaseOriginals).
            public NoveltyFields AfterLastWrite;
            public bool HasWritten;
            // A Partner_Skill_* NPCStat (plan E2) rather than a player skill GameStat: written only while
            // PartnerSkillsFollowSettings is on, and given its originals back when it is off. Name is then
            // the player's skill it follows, whose per-skill staleness settings it takes.
            public bool Partner;
        }

        // Keyed by the GameStat instance itself, not by UniqueID: a stat whose UniqueID cannot be
        // read is still a skill the scan configures (it always was), and two instances sharing one
        // UniqueID each keep their own originals.
        private static readonly Dictionary<object, CapturedSkill> _captured =
            new Dictionary<object, CapturedSkill>(new IdentityComparer());
        private static bool _firstScanDone;

        /// <summary>True once the load scan has filled <see cref="SkillNamesByUniqueId"/>.</summary>
        internal static bool SkillScanDone => _firstScanDone;

        // D17: each failure cause warns once, then stays quiet. The re-apply runs at every run start
        // and on every staleness setting change, so an unkeyed warning would repeat for every skill.
        private static readonly HashSet<string> _warnedCauses = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// True for a stat this mod deliberately does not treat as a skill, read from the stat
        /// MODEL rather than from a UniqueID set.
        ///
        /// Exists because UsesNovelty is not a skill test: the load-time scan skips the nine
        /// mental stats above before they ever reach <see cref="SkillUniqueIds"/>, so any second
        /// opinion that falls back to UsesNovelty alone reaches the OPPOSITE verdict on exactly
        /// those nine. Both paths have to consult one list or they disagree silently.
        /// </summary>
        internal static bool IsIgnoredStatName(object statModel)
        {
            if (statModel == null) return false;
            if (!Reflect.TryGetMember(statModel, "GameName", out var gameNameObj) || gameNameObj == null)
                return false;
            if (!Reflect.TryGetMember(gameNameObj, "DefaultText", out var nameTextObj) || nameTextObj == null)
                return false;

            var name = nameTextObj.ToString();
            if (string.IsNullOrWhiteSpace(name)) return false;

            // Raw first, to match the scan's own comparison exactly, then trimmed as a courtesy.
            return IgnoredNames.Contains(name) || IgnoredNames.Contains(name.Trim());
        }

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = Reflect.TryGetType("GameLoad");
                if (gameLoadType == null)
                {
                    Logger.LogError("[SkillSpeedBoost] GameLoad type not found");
                }
                else
                {
                    var loadMainGameDataMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                    if (loadMainGameDataMethod == null)
                    {
                        Logger.LogError("[SkillSpeedBoost] LoadMainGameData method not found");
                    }
                    else
                    {
                        harmony.Patch(
                            loadMainGameDataMethod,
                            postfix: new HarmonyMethod(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix))
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SkillSpeedBoost] Patch error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }

            // Separate try: a failure on one patch must not take the other down with it.
            try
            {
                // Private method, resolved by name. This is where the game itself builds a run's
                // stat dictionary, once per run start (a load or a new game), and the hook root
                // CLAUDE.md names for per-run world state ("Harmony Patching Pitfalls").
                var gmType = Reflect.TryGetType("GameManager");
                var runStartMethod = gmType == null ? null : AccessTools.Method(gmType, "InitializeStatsAndActions");
                if (runStartMethod == null)
                {
                    Logger.LogWarning(
                        "[SkillSpeedBoost] GameManager.InitializeStatsAndActions not found, so staleness settings are not " +
                        "re-applied at each run start. They still apply when the game starts and the moment you change them in-game.");
                }
                else
                {
                    harmony.Patch(
                        runStartMethod,
                        postfix: new HarmonyMethod(typeof(GameLoadPatch), nameof(InitializeStatsAndActions_Postfix))
                    );
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SkillSpeedBoost] Run-start patch error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// After game data loads, identify skill stats (GameStat where UsesNovelty == true, minus
        /// <see cref="IgnoredNames"/>), record each skill's UniqueID + GameName for the runtime
        /// multiplier hook in MorningBonusPatch, and apply the staleness settings to each.
        /// </summary>
        static void LoadMainGameData_Postfix(object __instance)
        {
            try
            {
                SkillUniqueIds.Clear();
                SkillNamesByUniqueId.Clear();

                var gameStatType = Reflect.TryGetType("GameStat");
                if (gameStatType == null)
                {
                    Logger.LogWarning("[SkillSpeedBoost] GameStat type not found — skill config skipped");
                    return;
                }

                // TryGetType resolves by SIMPLE NAME across every loaded assembly, so a foreign
                // assembly defining its own unrelated "GameStat" could win the lookup. Resources.
                // FindObjectsOfTypeAll requires a UnityEngine.Object subclass: handed anything else
                // it logs a Unity error and can return null, NRE-ing the loop below.
                if (!typeof(UnityEngine.Object).IsAssignableFrom(gameStatType))
                {
                    Logger.LogWarning(
                        $"[SkillSpeedBoost] 'GameStat' resolved to {gameStatType.FullName}, which is not a " +
                        "UnityEngine.Object - skill config skipped");
                    return;
                }

                // Direct GameStat scan — only ~30 stats vs 4000+ ScriptableObjects.
                // FindObjectsOfTypeAll covers both vanilla GameStats and any registered
                // by mods via the framework (which creates them via ScriptableObject.CreateInstance).
                var stats = Resources.FindObjectsOfTypeAll(gameStatType);

                int skillCount = 0;
                int stalenessOnCount = 0;
                int stalenessOffCount = 0;

                // Effective-settings summary: built only when the player asked for it, so the
                // normal path allocates nothing. Emitted after the loop so the lines land
                // together rather than interleaved with anything else patching at load time.
                var effectiveLines = Plugin.LogEffectiveSettings ? new List<string>() : null;

                foreach (var entry in stats)
                {
                    string skillForLog = null;
                    try
                    {
                        if (entry == null) continue;

                        // A stat captured on an earlier pass is a skill even though this mod's own
                        // staleness-off write has since cleared its UsesNovelty.
                        if (!_captured.ContainsKey(entry) && !Reflect.GetBool(entry, "UsesNovelty", false))
                        {
                            continue;
                        }

                        string skillName = "";
                        if (Reflect.TryGetMember(entry, "GameName", out var gameNameObj) && gameNameObj != null)
                        {
                            if (Reflect.TryGetMember(gameNameObj, "DefaultText", out var nameTextObj) && nameTextObj != null)
                            {
                                skillName = nameTextObj.ToString() ?? "";
                            }
                        }

                        if (IgnoredNames.Contains(skillName))
                        {
                            continue;
                        }
                        skillForLog = skillName;

                        if (Reflect.TryGetMember(entry, "UniqueID", out var uidObj) && uidObj is string skillUniqueId
                            && !string.IsNullOrWhiteSpace(skillUniqueId))
                        {
                            var uid = skillUniqueId.Trim();
                            SkillUniqueIds.Add(uid);
                            if (!string.IsNullOrWhiteSpace(skillName))
                            {
                                var trimmedName = skillName.Trim();
                                SkillNamesByUniqueId[uid] = trimmedName;
                                // Register any skills that aren't in the hard-coded core list
                                // so players can configure per-skill multipliers for mod-added skills.
                                SkillConfigManager.RegisterDynamicSkill(Plugin.Instance.Config, trimmedName);
                            }
                        }

                        skillCount++;

                        if (effectiveLines != null && !string.IsNullOrWhiteSpace(skillName))
                            effectiveLines.Add(DescribeEffectiveSettings(skillName.Trim()));

                        if (ApplyStalenessTo(entry, skillName.Trim(), out var written))
                        {
                            if (written.UsesNovelty) stalenessOnCount++;
                            else stalenessOffCount++;
                        }
                    }
                    catch (Exception exSkill)
                    {
                        // A skill skipped here keeps the game's own staleness whatever this mod's
                        // settings say, which a player sees, so warn once per skill (this path runs
                        // once per load, and again on every staleness setting change).
                        string skipped = string.IsNullOrWhiteSpace(skillForLog) ? "(unnamed stat)" : skillForLog.Trim();
                        if (_warnedCauses.Add("staleness-skip:" + skipped))
                            Logger.LogWarning(
                                $"[SkillSpeedBoost] skill '{skipped}' was skipped during staleness apply, so its staleness and per-skill settings do not apply: {exSkill.GetType().Name}: {exSkill.Message}");
                    }
                }

                // The Partner NPC's skills (plan E2): captured here whether or not the option is on, so
                // their originals are known before anything is written; with it off nothing is.
                CapturePartnerSkills();

                _firstScanDone = true;

                // The skill names are known now, so a SynergyGroupOverrides entry naming no loaded skill
                // can be reported (a reload before this point had nothing to compare against).
                SkillSynergies.WarnUnknownOverrideSkills();
                Plugin.WarnUnknownSeasonSkills();
                Plugin.WarnUnknownEnvironmentSkills();
                Plugin.WarnUnknownToolSkills();
                Plugin.WarnUnknownActionSkills();
                Plugin.WarnUnknownPerkSkills();
                Plugin.WarnUnknownWindowSkills();

                Logger.LogDebug(
                    $"[SkillSpeedBoost] skill config applied: {skillCount} skills, staleness on={stalenessOnCount}, " +
                    $"staleness off={stalenessOffCount}, multiplier={Plugin.SkillExpMultiplier}x (runtime hook)"
                );

                if (effectiveLines != null)
                    LogEffectiveSettings(effectiveLines);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SkillSpeedBoost] Error modifying skills: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Captures the four novelty fields of every Partner_Skill_* NPCStat whose stem maps to a skill
        /// (plan E2; PartnerSkillPatch.Resolve, so the gain patch agrees), and applies the partner rule
        /// to each: the player's staleness settings while PartnerSkillsFollowSettings is on, the
        /// originals while it is off. Called once, from the load scan; the run-start and settings
        /// re-apply then walk the captured set like the player's skills. Never throws.
        /// </summary>
        private static void CapturePartnerSkills()
        {
            try
            {
                var npcStatType = Reflect.TryGetType("NPCStat");
                if (npcStatType == null || !typeof(UnityEngine.Object).IsAssignableFrom(npcStatType))
                {
                    if (_warnedCauses.Add("partner:type"))
                        Logger.LogWarning("[SkillSpeedBoost] NPCStat type not found, so Partner skills keep the game's own staleness whatever PartnerSkillsFollowSettings says.");
                    return;
                }

                int captured = 0;
                foreach (var entry in Resources.FindObjectsOfTypeAll(npcStatType))
                {
                    if (entry == null) continue;
                    string skill = PartnerSkillPatch.Resolve(entry, Plugin.PartnerSkillsFollowSettings);
                    if (skill == null) continue; // not a partner skill, or an unmapped one (warned once while the option is on)
                    if (ApplyStalenessTo(entry, skill, out _, partner: true)) captured++;
                }

                if (captured == 0 && _warnedCauses.Add("partner:none"))
                    Logger.LogDebug("[SkillSpeedBoost] no Partner_Skill_* stat was found at load, so PartnerSkillsFollowSettings changes no staleness.");
                else
                    Logger.LogDebug($"[SkillSpeedBoost] captured {captured} Partner skill stats (PartnerSkillsFollowSettings={Plugin.PartnerSkillsFollowSettings}).");
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"[SkillSpeedBoost] Capturing the Partner skills failed, so they keep the game's own staleness: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Postfix on GameManager.InitializeStatsAndActions: runs once at every run start (a load or
        /// a new game). Re-applies the current staleness settings to every captured skill, then
        /// lets ProgressionStore move the progression files to this save slot's folder when
        /// PerSaveProgression is on (plan D2; the slot index is final here, plan W0.6). A named
        /// method on purpose: later per-run work (partner skills) belongs here too, rather than in a
        /// second postfix on the same method. Neither call throws.
        /// </summary>
        static void InitializeStatsAndActions_Postfix(object __instance)
        {
            AreaFamiliarityPatch.ResetActionContext();
            ReapplyStaleness("run start");
            ProgressionStore.OnRunStart(__instance);
        }

        /// <summary>
        /// Re-applies the current staleness settings to every skill captured so far. Called at each
        /// run start and from the SettingChanged handlers of EnableSkillStaleness and every per-skill
        /// staleness toggle and rate (Plugin.Awake, SkillConfigManager), so an in-game change lands
        /// at once. Walks the captured set, never a rescan: after a staleness-off write no skill
        /// carries UsesNovelty any more, so a rescan keyed on it would find nothing to switch back on.
        /// Before the first scan there is nothing captured, and the scan applies the settings itself.
        /// Never throws: it runs inside GameManager.Awake and inside BepInEx's config events.
        /// </summary>
        internal static void ReapplyStaleness(string reason)
        {
            try
            {
                if (_captured.Count == 0) return;

                int stalenessOnCount = 0;
                int stalenessOffCount = 0;
                var skills = new List<CapturedSkill>(_captured.Values);
                foreach (var skill in skills)
                {
                    try
                    {
                        if (skill.Stat is UnityEngine.Object unityStat && unityStat == null)
                        {
                            // A destroyed asset is not C#-null under an object-typed test (root
                            // CLAUDE.md "Harmony Patching Pitfalls"); drop it rather than write to it.
                            _captured.Remove(skill.Stat);
                            Logger.LogDebug($"[SkillSpeedBoost] '{skill.Name}' no longer exists; dropped from the staleness set.");
                            continue;
                        }

                        if (ApplyStalenessTo(skill.Stat, skill.Name, out var written))
                        {
                            if (written.UsesNovelty) stalenessOnCount++;
                            else stalenessOffCount++;
                        }
                    }
                    catch (Exception exSkill)
                    {
                        Logger.LogDebug(
                            $"[SkillSpeedBoost] staleness re-apply skipped '{skill.Name}': {exSkill.GetType().Name}: {exSkill.Message}");
                    }
                }

                Logger.LogDebug(
                    $"[SkillSpeedBoost] staleness re-applied ({reason}): {skills.Count} skills, " +
                    $"staleness on={stalenessOnCount}, staleness off={stalenessOffCount}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[SkillSpeedBoost] Error re-applying staleness ({reason}): {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// The ONE place this mod writes a skill's novelty fields. Captures the stat's originals on
        /// first sight, folds in any change another mod made since this mod's last write, works out
        /// the target from the originals plus the current settings (<see cref="ResolveNoveltyWrite"/>)
        /// and writes each field that differs. Idempotent: the result depends only on the originals
        /// and the settings, never on what was written before. Returns false when the stat could not
        /// be read or written, which has already been warned about once per cause.
        /// </summary>
        internal static bool ApplyStalenessTo(object stat, string skillName, out NoveltyFields written, bool partner = false)
        {
            written = default(NoveltyFields);
            if (stat == null) return false;

            if (!TryReadNoveltyFields(stat, out var current)) return false;

            if (!_captured.TryGetValue(stat, out var skill))
            {
                // Nothing in this mod has written this stat yet, so what it holds now is the original.
                skill = new CapturedSkill { Stat = stat, Name = skillName ?? "", Originals = current, Partner = partner };
                _captured[stat] = skill;
                if (_firstScanDone)
                    Logger.LogDebug($"[SkillSpeedBoost] '{skill.Name}' was first seen after the load scan; captured its staleness values now.");
            }
            else if (skill.HasWritten)
            {
                var rebased = RebaseOriginals(skill.Originals, skill.AfterLastWrite, current);
                if (!rebased.SameAs(skill.Originals))
                {
                    // Another plugin wrote one of these fields after this mod did (WikiMod's
                    // StatStalenessMultiplier rewrites StalenessMultiplier at load, for one). Keep its
                    // value as the one to restore rather than undoing it on every run start.
                    Logger.LogDebug(
                        $"[SkillSpeedBoost] '{skill.Name}' was changed by another mod since this mod last wrote it; " +
                        $"restoring {rebased} from now on instead of {skill.Originals}.");
                    skill.Originals = rebased;
                }
            }

            string name = skill.Name;
            bool perSkillOn = string.IsNullOrWhiteSpace(name) || SkillConfigManager.GetSkillUseStaleness(name);
            float rate = string.IsNullOrWhiteSpace(name) ? 1f : SkillConfigManager.GetSkillStalenessRate(name);
            // A Partner skill (plan E2) follows the player's settings for the skill it maps to only while
            // PartnerSkillsFollowSettings is on; off, it gets its originals back.
            var target = skill.Partner
                ? ResolvePartnerNoveltyWrite(skill.Originals, Plugin.PartnerSkillsFollowSettings, Plugin.EnableSkillStaleness, perSkillOn, rate)
                : ResolveNoveltyWrite(skill.Originals, Plugin.EnableSkillStaleness, perSkillOn, rate);

            bool ok = true;
            if (current.UsesNovelty != target.UsesNovelty)
                ok &= NoteWrite("UsesNovelty", Reflect.SetMember(stat, "UsesNovelty", target.UsesNovelty));
            if (current.StalenessMultiplier != target.StalenessMultiplier)
                ok &= NoteWrite("StalenessMultiplier", Reflect.SetMember(stat, "StalenessMultiplier", target.StalenessMultiplier));
            if (current.MaxStalenessStack != target.MaxStalenessStack)
                ok &= NoteWrite("MaxStalenessStack", Reflect.SetMember(stat, "MaxStalenessStack", target.MaxStalenessStack));
            if (current.NoveltyCooldownDuration != target.NoveltyCooldownDuration)
                ok &= NoteWrite("NoveltyCooldownDuration", Reflect.SetMember(stat, "NoveltyCooldownDuration", target.NoveltyCooldownDuration));

            // Remember what the stat really holds now (read back, not assumed), so the next apply can
            // tell this mod's own write from someone else's.
            skill.HasWritten = TryReadNoveltyFields(stat, out var afterWrite);
            if (skill.HasWritten) skill.AfterLastWrite = afterWrite;

            written = target;
            return ok;
        }

        /// <summary>
        /// The four values to write for one skill: with staleness off (globally or for this skill)
        /// the three fields that switch novelty off are zeroed and the cooldown keeps its original;
        /// with it on, the three originals come back and the cooldown follows the rate.
        /// </summary>
        internal static NoveltyFields ResolveNoveltyWrite(NoveltyFields originals, bool staleOn, bool perSkillOn, float rate)
        {
            if (!staleOn || !perSkillOn)
            {
                // Disable staleness at the stat level: GameManager.ChangeStat then skips novelty
                // entirely, so the runtime hook sees raw gains with no staleness reduction.
                return new NoveltyFields(false, 0f, 0, originals.NoveltyCooldownDuration);
            }

            // Staleness enabled: NoveltyCooldownDuration controls decay speed.
            return new NoveltyFields(originals.UsesNovelty, originals.StalenessMultiplier,
                originals.MaxStalenessStack, ComputeCooldown(rate));
        }

        /// <summary>
        /// The four values to write for one Partner skill (plan E2): its originals while
        /// PartnerSkillsFollowSettings is off, so an off option leaves every partner stat exactly as the
        /// game made it; otherwise the player's rule for the skill it maps to (ResolveNoveltyWrite).
        /// </summary>
        internal static NoveltyFields ResolvePartnerNoveltyWrite(NoveltyFields originals, bool followOn, bool staleOn, bool perSkillOn, float rate)
        {
            if (!followOn) return originals;
            return ResolveNoveltyWrite(originals, staleOn, perSkillOn, rate);
        }

        /// <summary>
        /// Field by field: a value that differs from what this mod left there last time was changed
        /// by someone else since, so it becomes the value to restore; an unchanged field keeps the
        /// captured original.
        /// </summary>
        internal static NoveltyFields RebaseOriginals(NoveltyFields originals, NoveltyFields afterLastWrite, NoveltyFields current)
        {
            return new NoveltyFields(
                current.UsesNovelty != afterLastWrite.UsesNovelty ? current.UsesNovelty : originals.UsesNovelty,
                current.StalenessMultiplier != afterLastWrite.StalenessMultiplier ? current.StalenessMultiplier : originals.StalenessMultiplier,
                current.MaxStalenessStack != afterLastWrite.MaxStalenessStack ? current.MaxStalenessStack : originals.MaxStalenessStack,
                current.NoveltyCooldownDuration != afterLastWrite.NoveltyCooldownDuration ? current.NoveltyCooldownDuration : originals.NoveltyCooldownDuration);
        }

        /// <summary>
        /// Reads the four novelty fields. False (warned once per field) when one is missing or not of
        /// its expected type, so a game update that renames or retypes one is visible in the log
        /// instead of silently leaving staleness unapplied.
        /// </summary>
        private static bool TryReadNoveltyFields(object stat, out NoveltyFields fields)
        {
            fields = default(NoveltyFields);
            if (!Reflect.TryGetMember(stat, "UsesNovelty", out var usesObj) || !(usesObj is bool uses))
                return WarnReadOnce("UsesNovelty", "bool");
            if (!Reflect.TryGetMember(stat, "StalenessMultiplier", out var multObj) || !(multObj is float mult))
                return WarnReadOnce("StalenessMultiplier", "float");
            if (!Reflect.TryGetMember(stat, "MaxStalenessStack", out var stackObj) || !(stackObj is int stack))
                return WarnReadOnce("MaxStalenessStack", "int");
            if (!Reflect.TryGetMember(stat, "NoveltyCooldownDuration", out var cooldownObj) || !(cooldownObj is int cooldown))
                return WarnReadOnce("NoveltyCooldownDuration", "int");

            fields = new NoveltyFields(uses, mult, stack, cooldown);
            return true;
        }

        private static bool WarnReadOnce(string field, string expectedType)
        {
            if (_warnedCauses.Add("read:" + field))
                Logger.LogWarning(
                    $"[SkillSpeedBoost] GameStat.{field} could not be read as {expectedType}, so staleness settings are not applied " +
                    "to skills (a game update may have renamed it).");
            return false;
        }

        private static bool NoteWrite(string field, bool succeeded)
        {
            if (!succeeded && _warnedCauses.Add("write:" + field))
                Logger.LogWarning($"[SkillSpeedBoost] GameStat.{field} could not be written, so a staleness setting may not apply.");
            return succeeded;
        }

        private sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        /// <summary>
        /// NoveltyCooldownDuration for a given per-skill staleness rate. Base 12 = 3 in-game
        /// hours (1 unit = 15 minutes); the rate scales the cooldown inversely, so 2x faster
        /// decay halves it. Shared by the apply path and the effective-settings log so the two
        /// can never disagree about what a rate actually produces.
        /// </summary>
        private static int ComputeCooldown(float stalenessRate)
        {
            return (int)Math.Max(1, Math.Round(12.0 / stalenessRate));
        }

        /// <summary>
        /// One resolved-settings line for a single skill: the multiplier that will actually be
        /// used and where it came from, then the staleness state and, when staleness is live,
        /// the decay rate and the cooldown it produces.
        /// </summary>
        private static string DescribeEffectiveSettings(string skillName)
        {
            int resolved = SkillConfigManager.GetSkillMultiplier(skillName);

            string source;
            if (!Plugin.EnablePerSkillMultipliers)
                source = "global; per-skill overrides disabled";
            else if (SkillConfigManager.TryGetRawSkillMultiplier(skillName, out int raw) && raw != 1)
                source = raw == 0 ? "per-skill override: levelling disabled" : "per-skill override";
            else
                source = "global";

            string staleness;
            if (!Plugin.EnableSkillStaleness)
                staleness = "staleness off (global)";
            else if (!SkillConfigManager.GetSkillUseStaleness(skillName))
                staleness = "staleness off (per-skill)";
            else
            {
                float rate = SkillConfigManager.GetSkillStalenessRate(skillName);
                staleness = $"staleness on (rate {rate:0.##}x, cooldown {ComputeCooldown(rate)})";
            }

            return $"[EffectiveSettings] {skillName}: XP {resolved}x ({source}), {staleness}";
        }

        /// <summary>
        /// Emits the effective-settings summary. Gated on the LogEffectiveSettings config entry
        /// by the caller, which is why this is allowed to use LogInfo in a per-skill loop: the
        /// whole point is to be readable in a default LogOutput.log, and it is off by default.
        /// Same shape as the existing SkillSynergiesDebugLog gate.
        /// </summary>
        private static void LogEffectiveSettings(List<string> lines)
        {
            Logger.LogInfo(
                $"[EffectiveSettings] {lines.Count} skills. Profile={Plugin.ActiveProfile}, " +
                $"GlobalExpMultiplier={Plugin.SkillExpMultiplier}x, PerSkillMultipliers={Plugin.EnablePerSkillMultipliers}, " +
                $"Staleness={Plugin.EnableSkillStaleness}");

            string familiarity = Plugin.AreaFamiliarityEnabled
                ? $"on (+{Plugin.AreaFamiliarityMinBonus * 100f:0.#}% at a new location rising to +{Plugin.AreaFamiliarityMaxBonus * 100f:0.#}% over {Plugin.AreaFamiliarityVisitsForMaxBonus} actions)"
                : "off";
            string fade = Plugin.AreaFamiliarityDecayEnabled
                ? $"on (-{Plugin.AreaFamiliarityDecayPerDay:0.##} actions a day after {Plugin.AreaFamiliarityDecayAfterDays} days away)"
                : "off";
            string morning = Plugin.MorningBonusEnabled
                ? $"on ({Plugin.MorningBonusMultiplier:0.##}x between {Plugin.MorningStartHour:0}:00 and {Plugin.MorningEndHour:0}:00)"
                : "off";
            string night = Plugin.NightBonusEnabled
                ? $"on ({Plugin.NightBonusMultiplier:0.##}x between {Plugin.NightStartHour:0}:00 and {Plugin.NightEndHour:0}:00)"
                : "off";
            string station = Plugin.StationBonusEnabled
                ? $"on ({Plugin.StationBonusCount} structure(s))"
                : "off";
            string season = Plugin.SeasonBonusEnabled
                ? $"on ({Plugin.SeasonBonusCount} entries)"
                : "off";
            string environment = Plugin.EnvironmentBonusEnabled
                ? $"on ({Plugin.EnvironmentBonusCount} entries)"
                : "off";
            string tool = Plugin.ToolBonusEnabled
                ? $"on ({Plugin.ToolBonusCount} entries)"
                : "off";
            string action = Plugin.ActionBonusEnabled
                ? $"on ({Plugin.ActionBonusCount} entries)"
                : "off";
            string perk = Plugin.PerkBonusEnabled
                ? $"on ({Plugin.PerkBonusCount} perks mapped)"
                : "off";
            string perSave = Plugin.PerSaveProgression
                ? (Plugin.PerSaveResetOnNewGame ? "on (a new game clears its slot's files)" : "on (a new game keeps its slot's files)")
                : "off";
            string focus = Plugin.FocusBonusEnabled
                ? $"on ({Plugin.FocusBonusMultiplier:0.##}x In the Flow)"
                : "off";
            string restedBank = Plugin.RestedBankEnabled
                ? $"on ({Plugin.RestedBankMultiplier:0.##}x, {Plugin.RestedBankGainsPerDay} gains banked a day up to {Plugin.RestedBankMaxGains})"
                : "off";
            string milestone = Plugin.MilestoneBonusEnabled
                ? $"on (+{Plugin.MilestoneBonusXp:0.##} XP at every {Plugin.MilestoneEvery} points, once per character)"
                : "off";
            string xpOverflow = Plugin.XpOverflowEnabled
                ? $"on ({Plugin.XpOverflowFraction * 100f:0.#}% of the XP a maxed skill cannot take, to its least trained related skill)"
                : "off";
            string xpReadout = Plugin.XpReadoutEnabled
                ? $"on (a toast when the bonuses change, the same ones again after {Plugin.XpReadoutMinSeconds:0.#} s)"
                : "off";
            string partnerSkills = Plugin.PartnerSkillsFollowSettings
                ? "on (the Partner's skills take your staleness and your global or per-skill multiplier, never a situational bonus)"
                : "off";
            string streak = Plugin.StreakBonusEnabled
                ? $"on (+{Plugin.StreakBonusPerDay * 100f:0.#}% a day for up to {Plugin.StreakBonusMaxDays} days)"
                : "off";
            string levelScaling = Plugin.LevelScalingEnabled
                ? $"on (up to +{Plugin.LevelScalingMaxBonus * 100f:0.#}%, {Plugin.LevelScalingCurve} curve)"
                : "off";
            string cap = Plugin.MaxComposedMultiplier > 0f
                ? $"{Plugin.MaxComposedMultiplier:0.##}x"
                : "uncapped";
            // Echoed whether or not synergies are on: it is the tuning a player turns them on into.
            string synergyTuning =
                $"+{Plugin.SynergyBonusPerAction * 100f:0.#}% an action up to {Plugin.SynergyMaxCombo} actions, " +
                $"reset after {Plugin.SynergyComboTimeoutMinutes:0.#} {(Plugin.SynergyUsesGameMinutes ? "in-game" : "real")} minutes, " +
                (Plugin.SynergyRequireDifferentSkill ? "only a change of skill grows a combo" : "any related action grows a combo");

            Logger.LogInfo(
                $"[EffectiveSettings] AreaFamiliarity={familiarity}, MorningBonus={morning}, LevelScaling={levelScaling}, " +
                $"SkillSynergies={(Plugin.SkillSynergiesEnabled ? "on" : "off")}, " +
                $"DailyFirstUse={(Plugin.DailyFirstUseBonusEnabled ? $"on ({Plugin.DailyFirstUseMultiplier:0.##}x)" : "off")}, " +
                $"WellRested={(Plugin.WellRestedBonusEnabled ? $"on ({Plugin.WellRestedMultiplier:0.##}x above {Plugin.WellRestedThreshold:0}%)" : "off")}, " +
                $"LowConditionPenalty={(Plugin.LowConditionPenaltyEnabled ? $"on ({Plugin.LowConditionMultiplier:0.##}x below {Plugin.LowConditionThreshold:0}%)" : "off")}, " +
                $"MaxComposedMultiplier={cap}");

            // The 1.11.0 settings get a line of their own rather than being spliced into the one
            // above: harness scenarios (T1.74, T2.189) match contiguous runs of that line, so it
            // stays byte-identical to 1.10.6 for any config that leaves these at their defaults.
            // Settings added after 1.11.0's first four are appended to the END of this line, never
            // spliced in, so a scenario matching an earlier run of it still matches (plan S4).
            Logger.LogInfo(
                $"[EffectiveSettings] NightBonus={night}, StationBonus={station}, Streak={streak}, " +
                $"AreaFamiliarityFade={fade}, DailyDigest={(Plugin.LogDailyXpSummary ? "on" : "off")}, " +
                $"SynergyTuning={synergyTuning}, " +
                $"SynergyOverrides={SkillSynergies.DescribeGroupOverrides()}, " +
                $"SeasonBonus={season}, " +
                $"EnvironmentBonus={environment}, " +
                $"ToolBonus={tool}, " +
                $"ActionBonus={action}, " +
                $"PerkBonus={perk}, " +
                $"PerSave={perSave}, " +
                $"FocusBonus={focus}, " +
                $"WindowSkills=morning {Plugin.MorningSkillsDescription}, night {Plugin.NightSkillsDescription}, " +
                $"RestedBank={restedBank}, " +
                $"Milestone={milestone}, " +
                $"XpOverflow={xpOverflow}, " +
                $"XpReadout={xpReadout}, " +
                $"PartnerSkills={partnerSkills}");

            foreach (var line in lines)
                Logger.LogInfo(line);
        }
    }
}
