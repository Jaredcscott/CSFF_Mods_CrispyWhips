using System;
using CSFFModFramework.Api;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Town Achievement Board detection — Tier A (Village_Master_Plan.md §10.9.2.4), the two
    /// kill-driven achievements: <b>Master Hunter</b> (slay all 12 huntable animals) and
    /// <b>Right to Bear Arms</b> (slay a Bear).
    ///
    /// <para><b>Why this is a C# postfix and not a <c>GameSourceModify/</c> JSON patch.</b> A GSM
    /// patch on a vanilla <c>Encounter</c> would resolve its target fine — vanilla Encounters ARE
    /// in <c>DataBase.AllData</c> at menu-load, unlike the scene-scoped <c>StatListTab</c> assets
    /// that defeated <see cref="StatTabInjectionPatch"/>'s original GSM approach. GSM is
    /// nevertheless unusable here for two independent reasons, both confirmed by reading the
    /// framework source rather than guessed:</para>
    /// <list type="number">
    /// <item><c>StatChanges</c> is a field of the NESTED <c>EncounterResultEffect</c> object
    /// (.decomp/Encounter.cs:91, .decomp/EncounterResultEffect.cs:15), not of <c>Encounter</c>
    /// itself. <c>GameSourceModifier.ApplyAppendArrays</c> does a flat
    /// <c>targetType.GetField(fieldName)</c> against the target's own type and cannot reach one
    /// level down.</item>
    /// <item>Even if it could, <c>StatChanges</c> is a fixed-size <c>StatModifier[]</c>, not a
    /// <c>List&lt;T&gt;</c>. <c>ApplyAppendArrays</c> appends via <c>list.Add()</c>, which throws
    /// <c>NotSupportedException</c> on an array — caught and logged, net effect zero.</item>
    /// </list>
    /// <para>So this is a firm technical conclusion, not a fallback of convenience. As far as this
    /// repo knows, this is the first place a CMC patch appends to a nested array field on a
    /// <em>vanilla</em> Encounter — the Guards system's own <c>EnemyDefeatedEffects.StatChanges</c>
    /// entries are authored declaratively in CMC-OWNED Encounter JSON
    /// (<c>Encounter/cmcEncounterGuardThorne.json</c>), which the WarpResolver handles normally and
    /// which is not the same problem.</para>
    ///
    /// <para><b>Append, never replace.</b> Every one of the 12 target encounters already ships 2–5
    /// vanilla <c>StatChanges</c> entries (Gratification, BloodSpilled, ViolenceTracker, FoxUrge,
    /// Pop_Squirrel, BadgerDefeated — verified against
    /// <c>Documentation/GameData/CSFF-JsonData_Current/</c>, EA 0.66h). Assigning a fresh
    /// one-element array would silently delete the player's kill rewards on every animal in the
    /// game. <see cref="AppendLatch"/> copies the existing array and grows it by one.</para>
    ///
    /// <para><b>Why <c>GameDataReady</c> rather than a local Harmony postfix on
    /// <c>GameLoad.LoadMainGameData</c>.</b> The framework raises
    /// <see cref="FrameworkEvents.GameDataReady"/> from the tail of its OWN LoadMainGameData
    /// postfix, after <c>LoadOrchestrator.Execute()</c> — i.e. after WarpResolver
    /// (CSFFModFramework/Patching/GameLoadPatch.cs:32-36). Subscribing gets that ordering by
    /// construction instead of relying on Harmony patch order between two mods on one method. It
    /// also matters for correctness here: because WarpResolver has already run, a
    /// <c>*WarpData</c> STRING written at this point would be dead (root CLAUDE.md § Post-WarpResolver
    /// SO References), so <see cref="AppendLatch"/> sets the resolved <c>StatModifier.Stat</c>
    /// object reference directly and never touches <c>StatWarpData</c>.</para>
    ///
    /// <para><b>Idempotency.</b> LoadMainGameData can run more than once per process (menu → run →
    /// menu → run). Appending unconditionally would stack a duplicate entry per boot, so
    /// <see cref="AppendLatch"/> bails out when an entry already targets that exact GameStat
    /// instance.</para>
    ///
    /// <para><b>Why a bare <c>+1</c> needs no repeat-kill guard.</b> Every latch stat is clamped
    /// <c>MinMaxValue {x:0, y:1}</c> in its own GameStat JSON, so a second Bear adds nothing. And
    /// all 79 <c>cmcStatAch*</c> stats ship <c>UsesNovelty: false</c>, so the staleness multiplier
    /// at .decomp/GameManager.cs:6372 (<c>_Stat.Stat.UsesNovelty &amp;&amp; !_Stat.IgnoreNovelty</c>)
    /// can never shrink the +1 below the 0.5 threshold the poll tests. <c>IgnoreNovelty</c> is set
    /// anyway — it is a provable no-op today and cheap insurance if one of those stats is ever
    /// given a novelty curve; vanilla's own Wolf ViolenceTracker entry uses the same flag.</para>
    ///
    /// <para>Every other field is left at vanilla's shape (all-false booleans, zero rate/min/max),
    /// matching the Gratification entry that already rides this exact effect block on 11 of the 12
    /// encounters. That entry is a proven-working path: the engine bundles
    /// <c>EnemyDefeatedEffects.StatChanges</c> into a one-tick end-of-encounter
    /// <c>DismantleCardAction</c> (.decomp/EncounterPopup.cs:2761/2777/2784), and non-instant
    /// modifiers are applied from .decomp/GameManager.cs:5670-5680 — outside the
    /// <c>if (_Amt &gt; 0)</c> guard, so they land even when that action carries no time cost.</para>
    ///
    /// <para><b>Wildlife applies here, not just NPCs.</b> Vanilla resolves
    /// <c>EnemyDefeatedEffects</c> unconditionally on an <c>EnemyDefeated</c> result
    /// (.decomp/EncounterPopup.cs:2293-2310); <c>AssociatedNPC</c> — null for ordinary wildlife —
    /// is never consulted on that path.</para>
    ///
    /// <para>This class only WRITES the member latches. <see cref="AchievementTrackerPatch"/> owns
    /// the derived <c>cmcStatAchHunterCount</c> / <c>cmcStatAchHunter</c> recompute.
    /// <c>cmcStatAchBearSlain</c> is written here directly and needs no poll logic at all.</para>
    /// </summary>
    internal static class AchievementKillEffectsPatch
    {
        /// <summary>
        /// Vanilla huntable-animal Encounter UID → the CMC latch stat(s) a kill should set.
        /// Bear carries two: its Master Hunter member latch AND the standalone Right to Bear Arms
        /// earned latch, so one Bear kill resolves both achievements in a single encounter.
        /// Maintenance surface: re-verify these UIDs against current vanilla data on game updates
        /// (root CLAUDE.md § Game-Update Reference Refresh) — a renumbered GUID degrades to a
        /// startup warning and a silently unearnable achievement, not a crash.
        /// </summary>
        private static readonly (string EncounterUid, string Label, string[] StatUids)[] KillLatches =
        {
            ("776b3d12bd9b78c408b507cf49810c8e", "Badger",    new[] { "cmcStatAchHuntBadger" }),
            ("4ce4b386231d17741aa540b11fd8e832", "Bear",      new[] { "cmcStatAchHuntBear", "cmcStatAchBearSlain" }),
            ("43b1d704f6cdc3343bbd97cbcfee41ab", "Beaver",    new[] { "cmcStatAchHuntBeaver" }),
            ("919913fdcc78e6845940f420fd273bb3", "Boar",      new[] { "cmcStatAchHuntBoar" }),
            ("a1f4d4b19a6a6e24aa4452aa7c81213b", "Doe",       new[] { "cmcStatAchHuntDoe" }),
            ("e774dab1421d04d458199c9973472c9f", "Duck",      new[] { "cmcStatAchHuntDuck" }),
            ("10d587a0f36528247aaf1326ff061c4a", "Fox",       new[] { "cmcStatAchHuntFox" }),
            ("6517c49b137c6d24b81f2557d338f195", "Hare",      new[] { "cmcStatAchHuntHare" }),
            ("72a4da5b850c60c438c78418df7d56f7", "Partridge", new[] { "cmcStatAchHuntPartridge" }),
            ("5b2362d3a31edbc4292155142607c485", "Squirrel",  new[] { "cmcStatAchHuntSquirrel" }),
            ("00c3c3af808818b41bfb20a4aefa9681", "Stag",      new[] { "cmcStatAchHuntStag" }),
            ("d47e145147d460a4c80d739af904fe8a", "Wolf",      new[] { "cmcStatAchHuntWolf" }),
        };

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            FrameworkEvents.GameDataReady += OnGameDataReady;
            Plugin.Logger.LogDebug("[AchievementKillEffectsPatch] initialized.");
        }

        private static void OnGameDataReady()
        {
            try
            {
                int encountersWired = 0;
                int latchesAppended = 0;

                foreach (var row in KillLatches)
                {
                    var encounter = UniqueIDScriptable.GetFromID<Encounter>(row.EncounterUid);
                    if (encounter == null)
                    {
                        Plugin.Logger.LogWarning(
                            $"[AchievementKillEffectsPatch] vanilla Encounter '{row.EncounterUid}' ({row.Label}) not found — " +
                            "killing one will not count toward Master Hunter.");
                        continue;
                    }

                    var effects = encounter.EnemyDefeatedEffects;
                    if (effects == null)
                    {
                        Plugin.Logger.LogWarning(
                            $"[AchievementKillEffectsPatch] {row.Label}'s Encounter has no EnemyDefeatedEffects block — " +
                            "killing one will not count toward Master Hunter.");
                        continue;
                    }

                    bool touchedThisEncounter = false;
                    foreach (var statUid in row.StatUids)
                    {
                        var stat = UniqueIDScriptable.GetFromID<GameStat>(statUid);
                        if (stat == null)
                        {
                            Plugin.Logger.LogWarning(
                                $"[AchievementKillEffectsPatch] GameStat '{statUid}' not found — {row.Label} kills will not latch it.");
                            continue;
                        }

                        if (!AppendLatch(effects, stat)) continue; // already wired this boot
                        latchesAppended++;
                        touchedThisEncounter = true;
                        Plugin.Logger.LogDebug($"[AchievementKillEffectsPatch] {row.Label} kill -> {statUid} (+1).");
                    }

                    if (touchedThisEncounter) encountersWired++;
                }

                // One Info line for the whole pass (root CLAUDE.md § Mod Logging Norms — per-item
                // loops stay at Debug). A 0/0 pass is the normal steady state on a second
                // LoadMainGameData in the same process, not a failure.
                Plugin.Logger.LogInfo(
                    $"[AchievementKillEffectsPatch] Achievement kill-effects wired onto {encountersWired} vanilla encounters " +
                    $"({latchesAppended} latches appended).");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning(
                    $"[AchievementKillEffectsPatch] OnGameDataReady failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>
        /// Appends a clamped <c>+1</c> StatModifier targeting <paramref name="stat"/> onto
        /// <paramref name="effects"/>, preserving every entry already there. Returns false when an
        /// entry for that stat is already present (idempotent re-run) — see the class doc.
        /// </summary>
        private static bool AppendLatch(EncounterResultEffect effects, GameStat stat)
        {
            var existing = effects.StatChanges ?? Array.Empty<StatModifier>();

            for (int i = 0; i < existing.Length; i++)
                if (existing[i].Stat == stat) return false;

            var grown = new StatModifier[existing.Length + 1];
            Array.Copy(existing, grown, existing.Length);
            grown[existing.Length] = new StatModifier
            {
                // Resolved SO reference, NOT StatWarpData: WarpResolver has already run by the
                // time GameDataReady fires, so a string here would never be resolved.
                Stat = stat,
                // (1,1) — a fixed +1 rather than a range; the stat's own 0..1 clamp absorbs repeats.
                ValueModifier = Vector2.one,
                // Provable no-op today (all cmcStatAch* ship UsesNovelty:false) — insurance only.
                IgnoreNovelty = true,
                // RateModifier / MinValueModifier / MaxValueModifier / CannotModifyBeyond /
                // ApplyEachTick / InstantModifier / IsInverse are deliberately left at default,
                // which is exactly the shape of the vanilla Gratification entry already riding
                // this same effect block on 11 of these 12 encounters.
            };

            effects.StatChanges = grown;
            return true;
        }
    }
}
