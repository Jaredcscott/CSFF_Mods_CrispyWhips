using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    ///     the lectern's SpecialDurability1/2/3/SpoilageTime/UsageDurability/FuelCapacity/
    ///     Progress (hours studied per course, one course per stat slot — all 8 of
    ///     CardData's durability slots are used) — persisted with the (non-instanced)
    ///     env's board. Carpentry (the 7th course) lives on the Progress slot; it
    ///     originally shipped on a separate "Carpentry Bench" card because the first 6
    ///     courses already claimed every SpecialDurability/Spoilage/Usage/Fuel slot —
    ///     folded back onto the lectern once the previously-unused Progress slot (JSON
    ///     field "Progress", runtime "CurrentProgress", incremented via
    ///     ReceivingCardChanges.ChargesChange, gated via RequiredProgressPercent) was
    ///     found to be free, so there is only ever one physical course-study card.
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
    ///   - ReconcileCourseProgress (real-time interval, while the player stands in the
    ///     Academy interior) is a self-healing backstop: if a course's graduate perk is
    ///     held (AcademyCourseService.HasCourse) but the lectern's own progress stat for
    ///     that course reads below its max — a desync reported in-game 2026-08-09 on a
    ///     manually-studied save (perk survived, hours-studied read 0%) — it force-sets
    ///     the stat back to max. Perk possession is the authoritative "done" signal
    ///     everywhere else in this file (AlreadyGraduatedMessage, AcademyCourseService's
    ///     blueprint gate); this makes the displayed progress agree with it regardless of
    ///     what desynced the durability value, without needing to know the original cause.
    /// Same Cancel/AfterWrapped/CollectActionModifiers gating architecture as
    /// InnPatch, but its own account stat/price (see above).
    /// </summary>
    internal static class AcademyPatch
    {
        private const string LecternUid = "cmcAcademyLectern";
        private static readonly string[] HostCardUids = { LecternUid };
        private const string AcademyInteriorEnvUid = "cmcAcademyInterior";
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
            public string NameFragment;            // matched against the action name
            public string GradPerk;                // hidden Graduate perk UID
            public string StatName;                // JSON stat name for course progress (SpecialDurability1, SpoilageTime, etc.)
            public float  TotalHours;               // course length in hours (SpecialDurability max)
            public string HostCardUid = LecternUid; // which physical card carries this course's DAs/progress stat
        }

        private static readonly CourseInfo[] Courses =
        {
            new CourseInfo { NameFragment = "Architecture", GradPerk = AcademyCourseService.GradArchitecture, StatName = "SpecialDurability1", TotalHours = 72f },
            new CourseInfo { NameFragment = "Metallurgy",   GradPerk = AcademyCourseService.GradMetallurgy,   StatName = "SpecialDurability2", TotalHours = 60f },
            new CourseInfo { NameFragment = "Herbalism",    GradPerk = AcademyCourseService.GradHerbalism,    StatName = "SpecialDurability3", TotalHours = 30f },
            new CourseInfo { NameFragment = "Fishing",      GradPerk = AcademyCourseService.GradFishing,      StatName = "SpoilageTime",       TotalHours = 24f },
            new CourseInfo { NameFragment = "Armorer",      GradPerk = AcademyCourseService.GradArmorer,      StatName = "UsageDurability",    TotalHours = 24f },
            new CourseInfo { NameFragment = "Medicine",     GradPerk = AcademyCourseService.GradMedicine,     StatName = "FuelCapacity",       TotalHours = 24f },
            new CourseInfo { NameFragment = "Carpentry",    GradPerk = AcademyCourseService.GradCarpentry,    StatName = "Progress",           TotalHours = 24f }
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
                CardPredicate = ctx => IsHostCard(ctx.CardUid) && IsStudyAction(ctx.ActionName),
                Timing        = ActionTiming.Cancel,
                Before        = StudyGate,
            });

            // AfterWrapped — perk grant + stat drain are native; this draws tuition
            // from the account and resyncs course-gated blueprint unlocks
            ActionRouter.Register(new ActionHandler
            {
                Name          = "AcademyStudyComplete",
                CardPredicate = ctx => IsHostCard(ctx.CardUid) && IsStudyAction(ctx.ActionName),
                Timing        = ActionTiming.AfterWrapped,
                After         = StudyCompleted,
            });

            // Deposit CI — Cancel gate first (registration order matters: a full
            // account must short-circuit before AcademyDepositApply ever runs, so the
            // dragged item is never consumed for zero value).
            ActionRouter.Register(new ActionHandler
            {
                Name          = "AcademyDepositGate",
                CardPredicate = ctx => IsHostCard(ctx.CardUid) && IsDepositAction(ctx.ActionName),
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
                CardPredicate = ctx => IsHostCard(ctx.CardUid) && IsDepositAction(ctx.ActionName),
                Timing        = ActionTiming.Before,
                Before        = DepositApply,
            });

            // Retired Carpentry Bench (1.68.23). 1.68.2 folded the Carpentry course onto the
            // lectern and DELETED CMC_CarpentryBench.json, which made GameManager.LoadCard drop
            // every saved bench on load (a UID that no longer resolves returns false there) along
            // with whatever tuition and study hours were banked on it - the opposite of what that
            // changelog promised. The card is back as a stub whose only action, "Transfer to
            // Lecture Hall", is a ModType 3 (Destroy) DismantleAction; this Cancel-timing gate
            // moves the balance and hours onto the lectern FIRST and only lets the native Destroy
            // run once nothing of value is left on the bench (return true = cancel = bench kept).
            ActionRouter.Register(new ActionHandler
            {
                Name          = "AcademyRetiredBenchTransfer",
                CardPredicate = ctx => ctx.CardUid == RetiredBenchUid && IsBenchTransferAction(ctx),
                Timing        = ActionTiming.Cancel,
                Before        = RetiredBenchTransfer,
            });

            // Self-healing progress display — see class doc. Only does work while the
            // player is standing in the Academy interior (the lectern's live instance is
            // only findable in AllCards then — AllCards is current-env-scoped, same
            // reasoning as GraduatePerkPatch/VillageFounderPerkPatch's own env-arrival polls).
            TickEvents.Interval(5f, ReconcileCourseProgress, "AcademyCourseProgressReconcile");

            Plugin.Logger.LogDebug("[AcademyPatch] initialized.");
        }

        // ── Course-progress self-heal ──────────────────────────────────────────

        /// <summary>For each course whose graduate perk is already held, force the
        /// lectern's own progress stat up to its max if it reads below that — repairs
        /// any desync between "perk held" (authoritative) and "hours studied" (display
        /// only), from whatever cause. Idempotent and cheap; safe to run on an interval.
        ///
        /// ROOT CAUSE FOUND (2026-08-09, round 2): a duplicate 'cmcAcademyLectern' can end
        /// up in AllCards (confirmed live: one instance held the player's real, completed
        /// progress; a second, empty-stats duplicate is the one the game's own click-routing
        /// resolves the player's Study/Deposit actions to going forward — its Tuition Account
        /// balance climbed with the player's deposits while its course stats stayed at their
        /// JSON default of 0). The old single-target FindLiveLecternCard only ever grabbed the
        /// FIRST AllCards match, which happened to be the already-complete ghost instance —
        /// nothing to reconcile there — while the instance the player actually sees never got
        /// touched. Fix: reconcile EVERY 'cmcAcademyLectern' instance found on the board, not
        /// just the first, so whichever one the game renders ends up correct regardless of how
        /// many duplicates exist or which one is "live". Does not touch SpecialDurability4
        /// (Tuition Account) — that's real player-managed currency, not a completion signal.</summary>
        private static void ReconcileCourseProgress()
        {
            try
            {
                string currentEnv = GameQuery.CurrentEnvironmentUniqueId;
                if (currentEnv != AcademyInteriorEnvUid) return;

                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null)
                {
                    Plugin.Logger.LogDebug("[AcademyPatch] ReconcileCourseProgress: in Academy interior but GetGameManagerInstance() returned null.");
                    return;
                }

                var lecterns = FindAllLiveLecternCards(gm);
                if (lecterns.Count == 0)
                {
                    Plugin.Logger.LogDebug("[AcademyPatch] ReconcileCourseProgress: in Academy interior but no lectern card found via AllCards.");
                    return;
                }

                // Diagnostic (2026-08-09, demoted to LogDebug 2026-08-11) — verifies the
                // multi-instance reconcile fix lands on the duplicate the player sees.
                DumpLecternInstanceIdentity(gm, lecterns[0]);
                if (lecterns.Count > 1)
                {
                    // Demoted to LogDebug 2026-08-11 — the multi-instance reconcile fix has run
                    // stable across sessions; kept for diagnostic visibility, not startup noise.
                    Plugin.Logger.LogDebug($"[AcademyPatch] ReconcileCourseProgress: {lecterns.Count} 'cmcAcademyLectern' instances on the board — reconciling all of them.");
                }

                foreach (var lectern in lecterns)
                {
                    string hostUid = CardUtil.GetCardUniqueId(lectern);
                    foreach (var course in Courses)
                    {
                        // Defensive: every course's HostCardUid is the Lectern (the only entry
                        // in HostCardUids), but keep the guard in case a future course ever
                        // ships on a second physical card again.
                        if (course.HostCardUid != hostUid) continue;

                        bool hasCourse = AcademyCourseService.HasCourse(course.GradPerk);
                        float hours = HoursStudied(lectern, course);
                        Plugin.Logger.LogDebug($"[AcademyPatch] ReconcileCourseProgress: course={course.NameFragment} hasCourse={hasCourse} hours={hours}/{course.TotalHours}");

                        if (!hasCourse) continue;
                        if (hours < 0f || hours >= course.TotalHours - 0.01f) continue; // unreadable or already at max

                        if (CardUtil.SetDurability(lectern, course.StatName, course.TotalHours))
                        {
                            Plugin.Logger.LogInfo($"[AcademyPatch] Reconciled '{course.NameFragment}' progress to {course.TotalHours:0}h — graduate perk was already held but the lectern's own progress had desynced (was {hours:0}h).");
                            CardVisualsRefresh.RefreshDurabilityVisuals(lectern);
                            CardVisualsRefresh.RefreshOpenInventoryPopup();
                        }
                        else
                        {
                            Plugin.Logger.LogWarning($"[AcademyPatch] Could not reconcile '{course.NameFragment}' progress stat '{course.StatName}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyPatch] ReconcileCourseProgress failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // Returns every live in-game instance of the lectern on the CURRENT board (normally
        // exactly one; see the duplicate-instance note on ReconcileCourseProgress above for why
        // this doesn't stop at the first match). AllCards is current-env-scoped — same idiom as
        // GraduatePerkPatch.FindLiveCard / VillageFounderPerkPatch.FindLiveCard.
        private static List<object> FindAllLiveLecternCards(object gm)
        {
            var found = new List<object>();
            if (Reflect.GetMember(gm, "AllCards") is not IEnumerable allCards) return found;
            foreach (var card in allCards)
            {
                if (card == null) continue;
                if (IsHostCard(CardUtil.GetCardUniqueId(card))) found.Add(card);
            }
            return found;
        }

        // ── Diagnostic (2026-08-09, round 2; demoted to LogDebug 2026-08-11) ─────
        // Instance-identity check for the "popup shows 0% despite maxed data" report.
        // Logs the InstanceID + all 6 course values for every 'cmcAcademyLectern' found
        // in AllCards (catches a duplicate), and separately the InstanceID + values of
        // whatever GraphicsManager.Instance.InspectedCard currently is (catches the popup
        // reading a different object than AllCards resolves).
        private static Type _graphicsManagerType;
        private static PropertyInfo _gmInstanceProperty;
        private static PropertyInfo _inspectedCardProperty;
        private static bool _diagStaticsResolved;

        private static void DumpLecternInstanceIdentity(object gm, object resolvedLectern)
        {
            try
            {
                if (Reflect.GetMember(gm, "AllCards") is IEnumerable allCards)
                {
                    int matchCount = 0;
                    foreach (var card in allCards)
                    {
                        if (card == null) continue;
                        if (!IsHostCard(CardUtil.GetCardUniqueId(card))) continue;
                        matchCount++;
                        int iid = (card as UnityEngine.Object)?.GetInstanceID() ?? 0;
                        Plugin.Logger.LogDebug($"[AcademyPatch] DumpLecternInstanceIdentity: AllCards match #{matchCount} instanceID={iid} "
                            + $"spoilage={CardUtil.GetDurability(card, "SpoilageTime"):0} special1={CardUtil.GetDurability(card, "SpecialDurability1"):0} "
                            + $"special2={CardUtil.GetDurability(card, "SpecialDurability2"):0} special3={CardUtil.GetDurability(card, "SpecialDurability3"):0} "
                            + $"usage={CardUtil.GetDurability(card, "UsageDurability"):0} fuel={CardUtil.GetDurability(card, "FuelCapacity"):0} "
                            + $"progress={CardUtil.GetDurability(card, "Progress"):0}");
                    }
                    if (matchCount > 1)
                        Plugin.Logger.LogWarning($"[AcademyPatch] DumpLecternInstanceIdentity: DUPLICATE lectern instances in AllCards! count={matchCount}");
                }

                if (!_diagStaticsResolved)
                {
                    _diagStaticsResolved = true;
                    _graphicsManagerType = AccessTools.TypeByName("GraphicsManager");
                    _gmInstanceProperty = _graphicsManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    _inspectedCardProperty = _graphicsManagerType?.GetProperty("InspectedCard", BindingFlags.Public | BindingFlags.Instance);
                }

                var gmInstance = _gmInstanceProperty?.GetValue(null);
                var inspectedCard = gmInstance != null ? _inspectedCardProperty?.GetValue(gmInstance) : null;
                if (inspectedCard == null)
                {
                    Plugin.Logger.LogDebug("[AcademyPatch] DumpLecternInstanceIdentity: GraphicsManager.InspectedCard is null.");
                    return;
                }

                string inspectedUid = CardUtil.GetCardUniqueId(inspectedCard) ?? "null";
                int inspectedIid = (inspectedCard as UnityEngine.Object)?.GetInstanceID() ?? 0;
                int resolvedIid = (resolvedLectern as UnityEngine.Object)?.GetInstanceID() ?? 0;
                bool sameRef = ReferenceEquals(inspectedCard, resolvedLectern);
                Plugin.Logger.LogDebug($"[AcademyPatch] DumpLecternInstanceIdentity: InspectedCard uid={inspectedUid} instanceID={inspectedIid} "
                    + $"sameAsAllCardsResolved={sameRef} (resolved instanceID={resolvedIid}) "
                    + $"spoilage={CardUtil.GetDurability(inspectedCard, "SpoilageTime"):0} special1={CardUtil.GetDurability(inspectedCard, "SpecialDurability1"):0}");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyPatch] DumpLecternInstanceIdentity failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
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

        private static bool IsHostCard(string uid) =>
            uid != null && Array.IndexOf(HostCardUids, uid) >= 0;

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
                if (!IsHostCard(CardUtil.GetCardUniqueId(__0))) return;

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
            // TEMP DIAGNOSTIC (2026-08-08) — see DepositApply note above.
            Plugin.Logger.LogDebug($"[AcademyPatch] DepositGate fired: balance={balance:0} full={full}");
            return full; // return true = cancel
        }

        private static bool DepositApply(ActionContext ctx)
        {
            // TEMP DIAGNOSTIC (2026-08-08, Inn/Academy deposit does nothing) — see
            // matching note in InnPatch.DepositApply. Demote to LogDebug once confirmed.
            string givenUid = CardUtil.GetCardUniqueId(ctx.GivenCard) ?? "null";
            float givenSd4 = CardUtil.GetDurability(ctx.GivenCard, "SpecialDurability4");
            Plugin.Logger.LogDebug($"[AcademyPatch] DepositApply fired: GivenCard UID={givenUid} SD4={givenSd4}");

            float value = CurrencyValue.ValueOf(ctx.GivenCard);
            if (value <= 0f)
            {
                Plugin.Logger.LogWarning($"[AcademyPatch] Deposit triggered but the dragged card (UID={givenUid}) had no recognized currency value.");
                return true;
            }

            float balance = CardUtil.GetDurability(ctx.Card, BalanceStat);
            if (float.IsNaN(balance)) balance = 0f;
            float newBalance = Math.Min(balance + value, AccountMax);
            CardUtil.SetDurability(ctx.Card, BalanceStat, newBalance);
            CardVisualsRefresh.RefreshDurabilityVisuals(ctx.Card);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            Plugin.Logger.LogInfo($"[AcademyPatch] Deposited {value:0} into the Tuition account (balance now {newBalance:0}/{AccountMax:0}).");
            return true;
        }

        // ── Retired Carpentry Bench transfer (1.68.23) ──────────────────────────

        private const string RetiredBenchUid   = "cmcCarpentryBench";
        private const string BenchTransferKey  = "CMC_CarpentryBench_DA_Transfer";
        // The old bench banked Carpentry hours on SpecialDurability1 (max 24); on the lectern the
        // same course lives on the Progress slot (Courses[] entry "Carpentry", StatName "Progress").
        private const string BenchHoursStat    = "SpecialDurability1";

        private static bool IsBenchTransferAction(ActionContext ctx) =>
            ctx.ActionKey == BenchTransferKey
            || (ctx.ActionName != null && ctx.ActionName.Contains("Transfer"));

        /// <summary>
        /// Cancel-timing gate on the retired bench's only action. Moves the bench's Tuition
        /// Account balance (as much as the lectern's 1000-cap account has room for) and its
        /// Carpentry hours (max onto every live lectern, mirroring ReconcileCourseProgress)
        /// onto the Lecture Hall lectern. Returns FALSE (let the action's own ModType 3 Destroy
        /// remove the bench) only when nothing of value is left on it; returns TRUE (cancel,
        /// bench kept) when the lectern was full, no lectern is on this board, or anything
        /// threw - the bench must never be destroyed with unmoved balance still on it.
        /// </summary>
        private static bool RetiredBenchTransfer(ActionContext ctx)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                var lecterns = gm == null ? new List<object>() : FindAllLiveLecternCards(gm);
                if (lecterns.Count == 0)
                {
                    Plugin.Logger.LogWarning("[AcademyPatch] Retired bench transfer: no Lecture Hall lectern on this board (gm " +
                                             (gm == null ? "null" : "found") + ") - nothing moved, bench kept.");
                    return true;
                }

                float benchBalance = CardUtil.GetDurability(ctx.Card, BalanceStat);
                if (float.IsNaN(benchBalance)) benchBalance = 0f;
                float benchHours = CardUtil.GetDurability(ctx.Card, BenchHoursStat);
                if (float.IsNaN(benchHours)) benchHours = 0f;

                // Balance: one account, so it goes onto the first live lectern (normally the only one).
                object target = lecterns[0];
                float lecternBalance = CardUtil.GetDurability(target, BalanceStat);
                if (float.IsNaN(lecternBalance)) lecternBalance = 0f;
                float moved = Math.Min(benchBalance, Math.Max(AccountMax - lecternBalance, 0f));
                if (moved > 0f)
                {
                    CardUtil.SetDurability(target, BalanceStat, lecternBalance + moved);
                    CardUtil.SetDurability(ctx.Card, BalanceStat, benchBalance - moved);
                    CardVisualsRefresh.RefreshDurabilityVisuals(target);
                }
                float remaining = benchBalance - moved;

                // Hours: take the higher of the two, capped at the course length, on every lectern.
                float carpentryMax = 24f;
                string lecternHoursStat = "Progress";
                foreach (var course in Courses)
                {
                    if (course.NameFragment == "Carpentry") { carpentryMax = course.TotalHours; lecternHoursStat = course.StatName; break; }
                }
                float hoursMoved = 0f;
                if (benchHours > 0f)
                {
                    foreach (var lectern in lecterns)
                    {
                        float current = CardUtil.GetDurability(lectern, lecternHoursStat);
                        if (float.IsNaN(current)) current = 0f;
                        float merged = Math.Min(Math.Max(current, benchHours), carpentryMax);
                        if (merged > current)
                        {
                            CardUtil.SetDurability(lectern, lecternHoursStat, merged);
                            CardVisualsRefresh.RefreshDurabilityVisuals(lectern);
                            hoursMoved = merged - current;
                        }
                    }
                }

                if (remaining > 0.5f)
                {
                    CardVisualsRefresh.RefreshDurabilityVisuals(ctx.Card);
                    CardVisualsRefresh.RefreshOpenInventoryPopup();
                    Plugin.Logger.LogInfo($"[AcademyPatch] Retired bench transfer: moved {moved:0} tuition and {hoursMoved:0}h of Carpentry to the Lecture Hall; " +
                                          $"{remaining:0} stays on the bench because the lectern's account is full ({AccountMax:0}) - transfer again once it has room.");
                    return true;
                }

                CardVisualsRefresh.RefreshOpenInventoryPopup();
                Plugin.Logger.LogInfo($"[AcademyPatch] Retired bench transfer: moved {moved:0} tuition and {hoursMoved:0}h of Carpentry to the Lecture Hall; the bench is removed.");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[AcademyPatch] Retired bench transfer failed - bench kept: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return true;
            }
        }
    }
}
