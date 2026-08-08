using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Village Academy Lecture Hall mechanics (the cmcAcademyLectern card inside the
    /// cmcAcademyInterior env; the cmcAcademy entrance only has the "Enter" DA):
    ///   - The lectern holds a "Tuition Account" balance (SpecialDurability4, max 1000).
    ///     Drag Salt / a metal Nugget (any type) / a Duros Coin onto the lectern (the
    ///     "Deposit Currency" CI) to add its value to the balance — see
    ///     <see cref="CurrencyValue"/>. A deposit is rejected outright (item untouched)
    ///     if the account has no headroom left at all.
    ///   - Courses run as 2-hour study sessions (DaytimeCost 8). Progress lives on
    ///     the lectern's SpecialDurability1/2/3 (hours studied: Architecture 72,
    ///     Metallurgy 60, Herbalism 30) — persisted with the instanced env's board.
    ///     Each session adds +2h via the DA's ReceivingCardChanges (pure JSON).
    ///   - "Study ..." DAs hide natively (RequiredReceivingDurabilities) once only
    ///     one session remains; a "Final Exam: ..." DA becomes visible for that last
    ///     session and grants the hidden Graduate perk natively via AddedInRunPerks.
    ///   - Tuition is a one-time enrollment fee (100), gated here on the FIRST session
    ///     of each course only and drawn from the Tuition Account when that session
    ///     completes.
    ///   - AfterWrapped asks AcademyCourseService to resync blueprint unlocks after
    ///     a final exam (Architecture/Metallurgy course rewards, incl. the
    ///     Forge/Workshop combo that needs both degrees).
    ///   - CardAction.CollectActionModifiers postfix sets the native
    ///     ActionBlockedMessage so the buttons show pre-emptively greyed out with a
    ///     clear tooltip reason; the Cancel handler below remains as a safety net.
    /// Same Cancel/AfterWrapped/CollectActionModifiers gating architecture as
    /// InnPatch, but its own account stat/price (see above).
    /// </summary>
    internal static class AcademyPatch
    {
        private const string LecternUid = "cmcAcademyLectern";
        private const string BalanceStat = "SpecialDurability4";
        private const float TuitionPrice = 100f;
        private const float AccountMax = 1000f;
        private const float SessionHours = 2f;

        private const string NoTuitionMessage =
            "Enrollment draws 100 from the Tuition Account — deposit Salt, coins, or nuggets on the lectern first.";
        private const string AlreadyGraduatedMessage =
            "You have already completed this course.";
        private const string IntroductionsFirstMessage =
            "The Professor doesn't take students he hasn't met properly — present yourself to the Keeper at the Village Inn first.";

        private sealed class CourseInfo
        {
            public string NameFragment;   // matched against the action name
            public string GradPerk;       // hidden Graduate perk UID
            public string StatName;       // JSON stat name for course progress (SpecialDurability1, SpoilageTime, etc.)
            public float  TotalHours;     // course length in hours (SpecialDurability max)
        }

        private static readonly CourseInfo[] Courses =
        {
            new CourseInfo { NameFragment = "Architecture", GradPerk = AcademyCourseService.GradArchitecture, StatName = "SpecialDurability1", TotalHours = 72f },
            new CourseInfo { NameFragment = "Metallurgy",   GradPerk = AcademyCourseService.GradMetallurgy,   StatName = "SpecialDurability2", TotalHours = 60f },
            new CourseInfo { NameFragment = "Herbalism",    GradPerk = AcademyCourseService.GradHerbalism,    StatName = "SpecialDurability3", TotalHours = 30f },
            new CourseInfo { NameFragment = "Fishing",      GradPerk = AcademyCourseService.GradFishing,      StatName = "SpoilageTime",       TotalHours = 24f },
            new CourseInfo { NameFragment = "Armorer",      GradPerk = AcademyCourseService.GradArmorer,      StatName = "UsageDurability",    TotalHours = 24f },
            new CourseInfo { NameFragment = "Medicine",     GradPerk = AcademyCourseService.GradMedicine,     StatName = "FuelCapacity",       TotalHours = 24f }
        };

        // Armorer's reward (ACT copper armor) only makes sense if ACT is installed —
        // without it, the course is pruned entirely from the lectern's DismantleActions
        // rather than left as a dead-end study line with no payoff.
        private const string ActPluginGuid = "crispywhips.advanced_copper_tools";

        private static bool _initialized;

        // ── Registration ─────────────────────────────────────────────────────

        public static void Initialize(Harmony harmony)
        {
            if (_initialized) return;
            _initialized = true;

            if (CardUtil.FindGameType("InGameCardBase") == null)
            {
                Plugin.Logger.LogWarning("[AcademyPatch] InGameCardBase not found — academy account mechanics inactive.");
                return;
            }

            TryPatchBlockedMessage(harmony);

            // Prune the Armorer course entirely if its payoff mod isn't installed —
            // runs once, before any board/save exists, so no cache-divergence risk.
            FrameworkEvents.GameDataReady += PruneUnavailableCourses;

            // Cancel gate — blocks a Study action without tuition or when already graduated
            ActionRouter.Register(new ActionHandler
            {
                Name          = "AcademyStudyGate",
                CardPredicate = ctx => ctx.CardUid == LecternUid && IsStudyAction(ctx.ActionName),
                Timing        = ActionTiming.Cancel,
                Before        = StudyGate,
            });

            // AfterWrapped — perk grant + stat drain are native; this draws tuition
            // from the account and resyncs course-gated blueprint unlocks
            ActionRouter.Register(new ActionHandler
            {
                Name          = "AcademyStudyComplete",
                CardPredicate = ctx => ctx.CardUid == LecternUid && IsStudyAction(ctx.ActionName),
                Timing        = ActionTiming.AfterWrapped,
                After         = StudyCompleted,
            });

            // Deposit CI — Cancel gate first (registration order matters: a full
            // account must short-circuit before AcademyDepositApply ever runs, so the
            // dragged item is never consumed for zero value).
            ActionRouter.Register(new ActionHandler
            {
                Name          = "AcademyDepositGate",
                CardPredicate = ctx => ctx.CardUid == LecternUid && IsDepositAction(ctx.ActionName),
                Timing        = ActionTiming.Cancel,
                Before        = DepositGate,
            });

            // Timing.Before (not AfterWrapped): the balance MUST be written before the
            // native routine's own MiniTicksCost advance runs (UseMiniTicks:CostsAMiniTick,
            // 1 mini-tick = 3 in-game minutes) — writing it in AfterWrapped would apply the
            // new balance only after that tick's refresh pass already read the stale value
            // (memory: reference_givecard_postfix_stat_init — stat writes precede ticks).
            ActionRouter.Register(new ActionHandler
            {
                Name          = "AcademyDepositApply",
                CardPredicate = ctx => ctx.CardUid == LecternUid && IsDepositAction(ctx.ActionName),
                Timing        = ActionTiming.Before,
                Before        = DepositApply,
            });

            Plugin.Logger.LogDebug("[AcademyPatch] initialized.");
        }

        // ── Mod-presence course pruning ─────────────────────────────────────────

        /// <summary>Removes the Armorer course's Study/Final Exam actions from the
        /// lectern if Advanced Copper Tools isn't installed — its whole payoff is
        /// ACT's copper armor, so without ACT it would be a study line that never
        /// pays off. Runs once at game-data-ready, before any board/save exists.</summary>
        private static void PruneUnavailableCourses()
        {
            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(ActPluginGuid))
                    return; // ACT installed — Armorer course stays

                var lecternCard = UniqueIDScriptable.GetFromID<CardData>(LecternUid);
                if (lecternCard?.DismantleActions == null) return;

                var kept = new List<DismantleCardAction>(lecternCard.DismantleActions.Count);
                int removed = 0;
                foreach (var da in lecternCard.DismantleActions)
                {
                    if (da != null && da.ActionName.DefaultText != null && da.ActionName.DefaultText.Contains("Armorer"))
                    {
                        removed++;
                        continue;
                    }
                    kept.Add(da);
                }

                if (removed > 0)
                {
                    lecternCard.DismantleActions = kept;
                    Plugin.Logger.LogInfo($"[AcademyPatch] Advanced Copper Tools not installed — removed {removed} Armorer course action(s) from the lectern.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyPatch] PruneUnavailableCourses failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Action name predicates ────────────────────────────────────────────

        private static bool IsStudyAction(string name) =>
            name != null && (name.Contains("Study") || name.Contains("Final Exam"));

        private static bool IsFinalExamAction(string name) =>
            name != null && name.Contains("Final Exam");

        private static bool IsDepositAction(string name) =>
            name != null && name.Contains("Deposit");

        /// <summary>Course descriptor for a Study/Final Exam action name, or null.</summary>
        private static CourseInfo CourseForAction(string name)
        {
            if (name == null) return null;
            foreach (var course in Courses)
            {
                if (name.Contains(course.NameFragment))
                    return course;
            }
            return null;
        }

        /// <summary>Hours studied so far for a course, read off the lectern card's
        /// raw durability stat (e.g. SpecialDurability1 holds 0..72 for Architecture),
        /// or -1 if unreadable.</summary>
        private static float HoursStudied(object lecternCard, CourseInfo course)
        {
            if (lecternCard == null || course == null) return -1f;
            float hours = CardUtil.GetDurability(lecternCard, course.StatName);
            if (float.IsNaN(hours))
            {
                Plugin.Logger.LogDebug($"[AcademyPatch] HoursStudied: '{course.StatName}' unreadable on {lecternCard.GetType().FullName}.");
                return -1f;
            }
            Plugin.Logger.LogDebug($"[AcademyPatch] HoursStudied: course={course.NameFragment} stat={course.StatName} hours={hours}/{course.TotalHours}");
            return hours;
        }

        // ── Pre-emptive blocked-message tooltip (native vanilla gating) ────────

        private static void TryPatchBlockedMessage(Harmony harmony)
        {
            try
            {
                var cardActionType = AccessTools.TypeByName("CardAction");
                if (cardActionType == null)
                {
                    Plugin.Logger.LogWarning("[AcademyPatch] CardAction type not found — blocked-message tooltip inactive.");
                    return;
                }

                var method = AccessTools.Method(cardActionType, "CollectActionModifiers");
                if (method == null)
                {
                    Plugin.Logger.LogWarning("[AcademyPatch] CollectActionModifiers not found — blocked-message tooltip inactive.");
                    return;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(typeof(AcademyPatch), nameof(CollectActionModifiers_Postfix)));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyPatch] TryPatchBlockedMessage failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // __0 = _ReceivingCard (InGameCardBase) — the card the DismantleAction belongs to.
        private static void CollectActionModifiers_Postfix(object __instance, object __0)
        {
            try
            {
                if (__instance == null || __0 == null) return;
                if (CardUtil.GetCardUniqueId(__0) != LecternUid) return;

                string actionName = CardUtil.GetActionName(__instance);
                if (!IsStudyAction(actionName)) return;

                string message = BlockReason(actionName, __0);
                if (message == null) return; // course affordable and not yet taken — leave untouched

                // Don't clobber a message another mechanism may have already set this pass.
                if (Reflect.GetMember(__instance, "ActionBlockedMessage") is string existing && !string.IsNullOrEmpty(existing)) return;

                Reflect.SetMember(__instance, "ActionBlockedMessage", message);
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyPatch] CollectActionModifiers_Postfix failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Study gate ────────────────────────────────────────────────────────

        /// <summary>Why this Study/Final Exam action is blocked right now, or null if
        /// allowed. Every session — including the final exam — draws 100 tuition.</summary>
        private static string BlockReason(string actionName, object lecternCard)
        {
            var course = CourseForAction(actionName);
            if (course == null) return null;

            // PR-1 village phase gate (Event Timeline plan §2): study opens with the intro.
            if (!VillageClock.PhaseAtLeast(1f))
                return IntroductionsFirstMessage;

            if (AcademyCourseService.HasCourse(course.GradPerk))
                return AlreadyGraduatedMessage; // safety net — the DAs hide natively at 100%

            if (!HasTuition(lecternCard))
                return NoTuitionMessage;

            return null;
        }

        private static bool StudyGate(ActionContext ctx)
        {
            string reason = BlockReason(ctx.ActionName, ctx.Card);
            if (reason != null)
                Plugin.Logger.LogDebug($"[AcademyPatch] '{ctx.ActionName}' blocked — {reason}");
            return reason != null; // return true = cancel
        }

        // ── Tuition (drawn from the Tuition Account balance) ──────────────────

        private static bool HasTuition(object lecternCard)
        {
            float balance = CardUtil.GetDurability(lecternCard, BalanceStat);
            return !float.IsNaN(balance) && balance >= TuitionPrice;
        }

        private static void StudyCompleted(ActionContext ctx)
        {
            var course = CourseForAction(ctx.ActionName);
            float hours = HoursStudied(ctx.Card, course);

            Plugin.Logger.LogDebug($"[AcademyPatch] StudyCompleted: action='{ctx.ActionName}' course={course?.NameFragment ?? "null"} hoursAfter={hours}");

            // Every session — including the final exam — draws the tuition fee from the
            // account now (StudyGate already verified the balance covers it).
            if (course != null)
            {
                float balance = CardUtil.GetDurability(ctx.Card, BalanceStat);
                if (float.IsNaN(balance)) balance = 0f;
                float newBalance = Math.Max(balance - TuitionPrice, 0f);
                CardUtil.SetDurability(ctx.Card, BalanceStat, newBalance);
                CardVisualsRefresh.RefreshDurabilityVisuals(ctx.Card);
                CardVisualsRefresh.RefreshOpenInventoryPopup();
                Plugin.Logger.LogDebug($"[AcademyPatch] Tuition drawn for {course.NameFragment}: hoursAfter={hours} -{TuitionPrice:0} (balance {balance:0} -> {newBalance:0}/{AccountMax:0}).");
            }

            if (IsFinalExamAction(ctx.ActionName))
            {
                AcademyCourseService.ResyncAfterCourse();
                Plugin.Logger.LogInfo($"[AcademyPatch] Course completed: {ctx.ActionName}.");
            }
            else if (course != null)
            {
                Plugin.Logger.LogDebug($"[AcademyPatch] Study session complete: {ctx.ActionName} ({hours}/{course.TotalHours}h).");
            }
        }

        // ── Deposit CI ──────────────────────────────────────────────────────────

        private static bool DepositGate(ActionContext ctx)
        {
            float balance = CardUtil.GetDurability(ctx.Card, BalanceStat);
            bool full = !float.IsNaN(balance) && balance >= AccountMax - 0.5f;
            if (full)
                Plugin.Logger.LogDebug("[AcademyPatch] Deposit blocked — Tuition account is full.");
            return full; // return true = cancel
        }

        private static bool DepositApply(ActionContext ctx)
        {
            float value = CurrencyValue.ValueOf(ctx.GivenCard);
            if (value <= 0f)
            {
                Plugin.Logger.LogWarning("[AcademyPatch] Deposit triggered but the dragged card had no recognized currency value.");
                return true;
            }

            float balance = CardUtil.GetDurability(ctx.Card, BalanceStat);
            if (float.IsNaN(balance)) balance = 0f;
            float newBalance = Math.Min(balance + value, AccountMax);
            CardUtil.SetDurability(ctx.Card, BalanceStat, newBalance);
            CardVisualsRefresh.RefreshDurabilityVisuals(ctx.Card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            Plugin.Logger.LogDebug($"[AcademyPatch] Deposited {value:0} into the Tuition account (balance now {newBalance:0}/{AccountMax:0}).");
            return true;
        }
    }
}
