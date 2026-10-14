using System;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Wagers at the Inn Counter (round 2 feature D7), and the shared pieces the later Inn games reuse.
    ///
    /// <para><b>The dice.</b> Drag the Bone Dice or the Loaded Dice onto the counter ("Wager on the Dice", a
    /// CardInteraction that keeps the dice, ModType 1). It is offered only while a resident is at the Inn
    /// (cmcStatInnCompany 1 to 4, a JSON gate) and, here in C#, only while the Inn Account holds the stake.
    /// Bone Dice win <see cref="BoneWinChance"/> of the time; a win pays twice the stake, which is the stake back
    /// plus the same again, so the account moves by +stake on a win and -stake on a loss and an honest roll
    /// costs the player <c>1 - 2 x BoneWinChance</c> of the stake on average. Loaded Dice first risk being
    /// caught (<see cref="LoadedCaughtChance"/>: the stake is lost and <see cref="CaughtCrime"/> Village Crime is
    /// written through <see cref="VillageCrimePatch.AddCrime"/>), then win <see cref="LoadedWinChance"/>.
    /// Every number here is a tunable placeholder.</para>
    ///
    /// <para><b>Shared by D8 and D9.</b> <see cref="Stake"/>, <see cref="CanStake"/>, <see cref="HasRoomForWin"/>,
    /// <see cref="DebitAccount"/>, <see cref="CreditAccount"/>, <see cref="SettleWager"/>, <see cref="NextRoll"/>
    /// and <see cref="Notify"/>. The account is the counter's SpecialDurability1, clamped 0 to
    /// <see cref="AccountMax"/>. None of it goes through InnPatch.BalanceGate or InnPatch.DeductBalance and none
    /// of it touches VillagePricing: a stake is the same stake whatever the crime band, and the wager handlers
    /// are on VillagePricing.RestitutionHandlers so CMC-Round2.Tests.ps1 keeps it that way.</para>
    ///
    /// <para><b>RNG hook.</b> <see cref="RngSeed"/> is a public static field: -1 (the default) draws from a
    /// time-seeded generator, any value 0 or above reseeds the generator the next time a roll is drawn, so a
    /// harness scenario can write it with <c>reflect/set path=type:CommunityModChest.Patcher.InnWagerPatch.RngSeed</c>
    /// and replay a sequence. Bone Dice draw once per wager; Loaded Dice draw once for the catch and, when not
    /// caught, once more for the win.</para>
    ///
    /// <para><b>Morris and arm wrestling (D8, D9).</b> "Play Morris" (drag the Morris Board, kept) wins
    /// <see cref="MorrisWinChance"/> of the time, from Focus read before the game ran; "Arm Wrestle" (a counter action costing 8 Stamina in JSON) wins
    /// <see cref="ArmWinChance"/>, 10 points lower when Stamina read below <see cref="ExhaustedBelow"/> before the
    /// action ran. Both draw once per game from <see cref="NextRoll"/>, pay as <see cref="SettleWager"/> and are
    /// gated by <c>StakeGate</c> (stake held, room for the win). Neither ever writes Fitness: its base rate is 0, so
    /// a write would be permanent.</para>
    ///
    /// <para>Registration runs once per process from Plugin.Awake under an _initialized guard (CLAUDE.md
    /// "Per-Run Event Registration", pattern b).</para>
    /// </summary>
    internal static class InnWagerPatch
    {
        internal const string CounterUid = "cmcInnCounter";
        internal const string BalanceStat = "SpecialDurability1";
        internal const string WagerKeyPrefix = "CMC_InnCounter_Wager";
        internal const string BoneDiceUid = "cmcBoneDice";
        internal const string LoadedDiceUid = "cmcLoadedDice";

        /// <summary>The one stake every Inn game uses, in Inn Account points (half a meal). Tunable placeholder.</summary>
        internal const float Stake = 25f;
        /// <summary>The Inn Account ceiling (InnPatch's AccountMax, the counter card's SpecialDurability1 MaxValue).</summary>
        internal const float AccountMax = 500f;

        internal const float BoneWinChance = 0.45f;
        internal const float LoadedWinChance = 0.60f;
        internal const float LoadedCaughtChance = 0.15f;
        internal const float CaughtCrime = 8f;

        internal const string WinKey = "CMC_Wager_Win";
        internal const string LoseKey = "CMC_Wager_Lose";
        internal const string CaughtKey = "CMC_Wager_Caught";
        internal const string NoStakeKey = "CMC_Wager_NoStake";
        internal const string NoRoomKey = "CMC_Wager_NoRoom";

        // ── Nine Men's Morris (D8) and arm wrestling (D9) ───────────────────────────────────
        // Key prefixes must NOT start with WagerKeyPrefix, or the dice handlers would see these actions too.
        internal const string MorrisKeyPrefix = "CMC_InnCounter_Morris";
        internal const string ArmWrestleKeyPrefix = "CMC_InnCounter_ArmWrestle";
        internal const string MorrisBoardUid = "cmcMorrisBoard";
        internal const string FocusStatUid = "4c5ed70723dc40e439720acab06e6af7";
        internal const string FitnessStatUid = "693223c6c9bab2445948b379d90f5159";
        internal const string StaminaStatUid = "1cfd30cf13b69b949a0ac521f55a59a2";
        internal const string MorrisWinKey = "CMC_Morris_Win";
        internal const string MorrisLoseKey = "CMC_Morris_Lose";
        internal const string ArmWinKey = "CMC_ArmWrestle_Win";
        internal const string ArmLoseKey = "CMC_ArmWrestle_Lose";

        /// <summary>Morris win chance at Focus 0, and what a full Focus adds (W0.22 not measured: the full range 0 to
        /// <see cref="FocusMax"/> is used). Base + span = 0.50 keeps the sharpest player at break-even. Tunable placeholders.</summary>
        internal const float MorrisBaseChance = 0.30f;
        internal const float MorrisFocusSpan = 0.20f;
        internal const float FocusMax = 96f;

        /// <summary>Arm-wrestling win chance at Fitness 0, what a full Fitness adds, the Fitness ceiling, and the
        /// penalty for starting a bout Exhausted (Stamina below <see cref="ExhaustedBelow"/>). Tunable placeholders.</summary>
        internal const float ArmBaseChance = 0.30f;
        internal const float ArmFitnessSpan = 0.20f;
        internal const float FitnessMax = 3200f;
        internal const float ArmExhaustedPenalty = 0.10f;
        internal const float ExhaustedBelow = 16f;

        /// <summary>RNG hook: -1 = unseeded, 0 or above = a replayable sequence. Written by harness scenarios.</summary>
        public static int RngSeed = -1;

        private static int _seededWith = int.MinValue;
        private static Random _rng;
        private static bool _initialized;
        private static readonly System.Collections.Generic.HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnWagerDiceGate",
                CardUid         = CounterUid,
                ActionKeyPrefix = WagerKeyPrefix,
                Timing          = ActionTiming.Cancel,
                Before          = ctx => DiceGate(ctx),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnWagerDiceRoll",
                CardUid         = CounterUid,
                ActionKeyPrefix = WagerKeyPrefix,
                Timing          = ActionTiming.AfterWrapped,
                Before          = ctx => { ctx.Tag = CardUtil.GetCardUniqueId(ctx.GivenCard); return false; },
                After           = ctx => RollDice(ctx),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnWagerMorrisGate",
                CardUid         = CounterUid,
                ActionKeyPrefix = MorrisKeyPrefix,
                Timing          = ActionTiming.Cancel,
                Before          = ctx => StakeGate(ctx),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnWagerMorrisPlay",
                CardUid         = CounterUid,
                ActionKeyPrefix = MorrisKeyPrefix,
                Timing          = ActionTiming.AfterWrapped,
                Before          = ctx => { ctx.Tag = ReadGameStat(FocusStatUid); return false; },
                After           = ctx => PlayMorris(ctx),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnWagerArmGate",
                CardUid         = CounterUid,
                ActionKeyPrefix = ArmWrestleKeyPrefix,
                Timing          = ActionTiming.Cancel,
                Before          = ctx => StakeGate(ctx),
            });

            ActionRouter.Register(new ActionHandler
            {
                Name            = "InnWagerArmBout",
                CardUid         = CounterUid,
                ActionKeyPrefix = ArmWrestleKeyPrefix,
                Timing          = ActionTiming.AfterWrapped,
                Before          = ctx => { ctx.Tag = ReadGameStat(StaminaStatUid); return false; },
                After           = ctx => PlayArmBout(ctx),
            });

            Plugin.Logger.LogDebug("[InnWagerPatch] initialized.");
        }

        // ── Shared helpers (D7, D8, D9) ─────────────────────────────────────────────────────

        /// <summary>The Inn Account balance on the counter card, or -1 when it cannot be read.</summary>
        internal static float ReadBalance(object counter)
        {
            float balance = CardUtil.GetDurability(counter, BalanceStat);
            if (float.IsNaN(balance))
            {
                WarnOnce("balance-nan", "[InnWagerPatch] the Inn Account (SpecialDurability1) is not readable - no wager is offered a stake.");
                return -1f;
            }
            return balance;
        }

        /// <summary>True when the account holds at least <paramref name="stake"/>.</summary>
        internal static bool CanStake(object counter, float stake)
        {
            float balance = ReadBalance(counter);
            return balance >= 0f && balance >= stake - 0.001f;
        }

        /// <summary>True when a win (+stake) still fits under <see cref="AccountMax"/>, so no winnings are clipped.</summary>
        internal static bool HasRoomForWin(object counter, float stake)
        {
            float balance = ReadBalance(counter);
            return balance >= 0f && balance + stake <= AccountMax + 0.001f;
        }

        /// <summary>Takes <paramref name="amount"/> from the account, never below 0. Returns the new balance, -1 on failure.</summary>
        internal static float DebitAccount(object counter, float amount) => AdjustAccount(counter, -Math.Abs(amount));

        /// <summary>Adds <paramref name="amount"/> to the account, never above <see cref="AccountMax"/>. Returns the new balance, -1 on failure.</summary>
        internal static float CreditAccount(object counter, float amount) => AdjustAccount(counter, Math.Abs(amount));

        /// <summary>One settled wager: a win moves the account by +stake (twice the stake paid out, the stake itself
        /// included), a loss by -stake. Returns the new balance, -1 on failure.</summary>
        internal static float SettleWager(object counter, float stake, bool won) =>
            won ? CreditAccount(counter, stake) : DebitAccount(counter, stake);

        /// <summary>The pure clamp both account writes use.</summary>
        internal static float ClampAccount(float balance) => Math.Min(AccountMax, Math.Max(0f, balance));

        /// <summary>True when a draw in [0, 1) lands inside <paramref name="chance"/>.</summary>
        internal static bool Wins(double roll, float chance) => roll < chance;

        /// <summary>The next draw in [0, 1) from the seedable generator (see <see cref="RngSeed"/>).</summary>
        internal static double NextRoll()
        {
            if (_rng == null || _seededWith != RngSeed)
            {
                _rng = RngSeed >= 0 ? new Random(RngSeed) : new Random();
                _seededWith = RngSeed;
            }
            return _rng.NextDouble();
        }

        /// <summary>Shows a CSV line on a card the way a drop collection's message does
        /// (GraphicsManager.PlayCardNotification). Every failure is a per-cause warn-once and never throws.</summary>
        internal static void Notify(object card, string localizationKey, float amount, string resident = null)
        {
            try
            {
                string text = RoadMeetingPatch.LocalizedText(localizationKey);
                if (string.IsNullOrEmpty(text))
                {
                    WarnOnce("text-" + localizationKey, $"[InnWagerPatch] no text for {localizationKey} - the result is not shown.");
                    return;
                }
                text = text.Replace("{0}", amount.ToString("0")).Replace("{1}", resident ?? "a resident");

                var gmType = CardUtil.FindGameType("GraphicsManager");
                if (gmType == null) { WarnOnce("gfx-type", "[InnWagerPatch] GraphicsManager type not found - wager results are not shown."); return; }
                object gfx = gmType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null);
                if (gfx == null) { WarnOnce("gfx-instance", "[InnWagerPatch] GraphicsManager.Instance not readable - wager results are not shown."); return; }
                var play = gmType.GetMethod("PlayCardNotification", BindingFlags.Instance | BindingFlags.Public);
                if (play == null) { WarnOnce("gfx-play", "[InnWagerPatch] GraphicsManager.PlayCardNotification not found - wager results are not shown."); return; }
                play.Invoke(gfx, new object[] { card, text });
            }
            catch (Exception ex)
            {
                WarnOnce("notify-exception", $"[InnWagerPatch] Notify failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static float AdjustAccount(object counter, float delta)
        {
            float balance = ReadBalance(counter);
            if (balance < 0f) return -1f;
            float next = ClampAccount(balance + delta);
            CardUtil.SetDurability(counter, BalanceStat, next);
            CardVisualsRefresh.RefreshDurabilityVisuals(counter);
            CardVisualsRefresh.RefreshOpenInventoryPopup();
            return next;
        }

        // ── The dice ────────────────────────────────────────────────────────────────────────

        private static bool DiceGate(ActionContext ctx)
        {
            if (CanStake(ctx.Card, Stake)) return false; // proceed
            Plugin.Logger.LogDebug($"[InnWagerPatch] '{ctx.ActionName}' blocked - the Inn Account holds less than the stake {Stake:0}.");
            Notify(ctx.Card, NoStakeKey, Stake);
            return true; // cancel
        }

        private static void RollDice(ActionContext ctx)
        {
            try
            {
                string diceUid = ctx.Tag as string;
                bool loaded = string.Equals(diceUid, LoadedDiceUid, StringComparison.OrdinalIgnoreCase);
                if (!loaded && !string.Equals(diceUid, BoneDiceUid, StringComparison.OrdinalIgnoreCase))
                {
                    WarnOnce("dice-uid", $"[InnWagerPatch] the dragged card '{diceUid ?? "null"}' is neither dice - no wager settled.");
                    return;
                }
                if (!CanStake(ctx.Card, Stake))
                {
                    Plugin.Logger.LogDebug("[InnWagerPatch] the account fell below the stake while the action ran - no wager settled.");
                    return;
                }

                if (loaded && Wins(NextRoll(), LoadedCaughtChance))
                {
                    float afterCaught = SettleWager(ctx.Card, Stake, false);
                    float crime = VillageCrimePatch.AddCrime(CaughtCrime);
                    Notify(ctx.Card, CaughtKey, Stake);
                    Plugin.Logger.LogInfo($"[InnWagerPatch] Loaded Dice caught: -{Stake:0} stake, Village Crime +{CaughtCrime:0} (now {crime:0}), balance {afterCaught:0}.");
                    return;
                }

                bool won = Wins(NextRoll(), loaded ? LoadedWinChance : BoneWinChance);
                float after = SettleWager(ctx.Card, Stake, won);
                Notify(ctx.Card, won ? WinKey : LoseKey, Stake);
                Plugin.Logger.LogDebug($"[InnWagerPatch] {(loaded ? "Loaded" : "Bone")} Dice {(won ? "won" : "lost")} {Stake:0}, balance now {after:0}.");
            }
            catch (Exception ex)
            {
                WarnOnce("roll-exception", $"[InnWagerPatch] RollDice failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ── Nine Men's Morris and arm wrestling (D8, D9) ────────────────────────────────────

        /// <summary>Clamps a term to [0, 1].</summary>
        internal static float Clamp01(float v) => Math.Min(1f, Math.Max(0f, v));

        /// <summary>Morris win chance: <see cref="MorrisBaseChance"/> plus <see cref="MorrisFocusSpan"/> x Focus/<see cref="FocusMax"/>, the term clamped.</summary>
        internal static float MorrisWinChance(float focus) => MorrisBaseChance + MorrisFocusSpan * Clamp01(focus / FocusMax);

        /// <summary>Arm-wrestling win chance: <see cref="ArmBaseChance"/> plus <see cref="ArmFitnessSpan"/> x Fitness/<see cref="FitnessMax"/>
        /// (term clamped), <see cref="ArmExhaustedPenalty"/> lower when the bout started Exhausted.</summary>
        internal static float ArmWinChance(float fitness, bool exhausted) =>
            ArmBaseChance + ArmFitnessSpan * Clamp01(fitness / FitnessMax) - (exhausted ? ArmExhaustedPenalty : 0f);

        /// <summary>A stat's SimpleCurrentValue, or -1 when it cannot be read.</summary>
        private static float ReadGameStat(string uid)
        {
            var gm = CardUtil.GetGameManagerInstance();
            return gm == null ? -1f : InnEveningPatch.ReadStat(gm, uid);
        }

        /// <summary>The stat's value for an odds term: an unreadable stat counts as 0 (the floor of the odds), once-warned.</summary>
        private static float ReadForOdds(string uid, string what)
        {
            float v = ReadGameStat(uid);
            if (v >= 0f) return v;
            WarnOnce("odds-" + uid, $"[InnWagerPatch] {what} is not readable - its odds term counts as 0.");
            return 0f;
        }

        /// <summary>A resident at the Inn to name in the result line, picked from the roll that settled the game
        /// (no second draw, so a seeded sequence stays one draw per game). "a resident" when none can be read.</summary>
        private static string PickResident(double roll)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return null;
                var present = InnEveningPatch.PresentResidents(gm);
                if (present.Count == 0) return null;
                return present[(int)(roll * 1000.0) % present.Count].Key.Name;
            }
            catch (Exception ex)
            {
                WarnOnce("pick-resident", $"[InnWagerPatch] naming a resident failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return null;
            }
        }

        /// <summary>Cancels a game when the account lacks the stake or has no room left for a win.</summary>
        private static bool StakeGate(ActionContext ctx)
        {
            if (!CanStake(ctx.Card, Stake))
            {
                Plugin.Logger.LogDebug($"[InnWagerPatch] '{ctx.ActionName}' blocked - the Inn Account holds less than the stake {Stake:0}.");
                Notify(ctx.Card, NoStakeKey, Stake);
                return true;
            }
            if (!HasRoomForWin(ctx.Card, Stake))
            {
                Plugin.Logger.LogDebug($"[InnWagerPatch] '{ctx.ActionName}' blocked - a win would not fit under the {AccountMax:0} cap.");
                Notify(ctx.Card, NoRoomKey, Stake);
                return true;
            }
            return false;
        }

        private static void SettleGame(ActionContext ctx, float chance, string winKey, string loseKey, string label)
        {
            if (!CanStake(ctx.Card, Stake))
            {
                Plugin.Logger.LogDebug($"[InnWagerPatch] the account fell below the stake while {label} ran - no wager settled.");
                return;
            }
            double roll = NextRoll();
            bool won = Wins(roll, chance);
            float balance = SettleWager(ctx.Card, Stake, won);
            Notify(ctx.Card, won ? winKey : loseKey, Stake, PickResident(roll));
            Plugin.Logger.LogDebug($"[InnWagerPatch] {label} {(won ? "won" : "lost")} {Stake:0} at {chance:0.00}, balance now {balance:0}.");
        }

        private static void PlayMorris(ActionContext ctx)
        {
            try
            {
                // The Focus captured in the Before step, as the game started: vanilla Focus drains 2 a tick, so a read
                // here, after the 4-tick game, could never reach the 0.50 a full Focus promises (T2.934). Same capture
                // as the arm bout's Stamina. A settle-time read only when that capture failed.
                float focus = ctx.Tag is float captured && captured >= 0f ? captured : ReadForOdds(FocusStatUid, "Focus");
                SettleGame(ctx, MorrisWinChance(focus), MorrisWinKey, MorrisLoseKey, "Morris");
            }
            catch (Exception ex)
            {
                WarnOnce("morris-exception", $"[InnWagerPatch] PlayMorris failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void PlayArmBout(ActionContext ctx)
        {
            try
            {
                bool exhausted = ctx.Tag is float stamina && stamina >= 0f && stamina < ExhaustedBelow;
                float fitness = ReadForOdds(FitnessStatUid, "Fitness");
                SettleGame(ctx, ArmWinChance(fitness, exhausted), ArmWinKey, ArmLoseKey, exhausted ? "Arm wrestle (exhausted)" : "Arm wrestle");
            }
            catch (Exception ex)
            {
                WarnOnce("arm-exception", $"[InnWagerPatch] PlayArmBout failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
