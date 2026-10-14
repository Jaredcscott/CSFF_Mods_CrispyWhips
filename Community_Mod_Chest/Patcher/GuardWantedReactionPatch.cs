using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using UnityEngine;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// Wanted-tier escalate-on-sight (Village_Master_Plan.md §10.8.2 Wanted band, 25-59) — one of
    /// two follow-ups the 2026-08-06 pass explicitly deferred (the other is Captain Sterling's
    /// one-time warning line, pure JSON: GameStat/CMC_SterlingWarningGiven.json +
    /// ScriptableObject/DialogScene/CMC_SterlingTalk.json).
    ///
    /// While the player's crime is in the Wanted band, any of our four guards who shares the
    /// player's CURRENT environment reacts short of pursuit: that guard's own Suspicion NPCStat
    /// (cmcNpcStatSuspicion, §10.8.1's per-guard detection tuning knob) rises, feeding the
    /// existing detection modifiers. This is deliberately NOT an Encounter and NOT the chase duty
    /// — those stay gated at Banished (>=60), GuardDutyPatch.CrimeGateCondition. Captain
    /// Sterling's dialog additionally carries a player-visible hostile line at this same band
    /// (CMC_SterlingTalk_Wanted) — this class owns only the mechanical, silent half.
    ///
    /// Reuses GuardWitnessPatch.FindGuardsAt (built for the witnessed-attack check, §10.8.4.2)
    /// rather than re-scanning AllNPCs a second way — that class already owns "which of our four
    /// guards is standing in environment X."
    ///
    /// Polled, not event-driven: unlike an attack there is no discrete "incident" moment for
    /// merely standing near a guard, so this reuses the same TickEvents.Interval 5s poll idiom as
    /// VillageChroniclePatch/VillageReputationPatch.
    ///
    /// Per-guard, per-sighting latch, not per-tick: a guard's Suspicion bumps once when the
    /// player is first seen in the Wanted band, then waits until either the player leaves that
    /// guard's environment or the player drops out of the Wanted band before reacting again —
    /// otherwise a player standing still would be bumped every 5s indefinitely.
    ///
    /// Suspicion is written the same way every other IEnumerator-backed stat change in this
    /// codebase is (root CLAUDE.md "Runtime Stat Change Hook"): InGameNPC.ChangeStat(
    /// NPCStatInstantModifier, ...) returns an IEnumerator that must be STARTED, never
    /// synchronously drained (reference_synchronous_coroutine_drain_freeze) — on the guard's own
    /// MonoBehaviour, same idiom GuardDutyPatch.OnAttackWitnessed already uses to force an
    /// immediate CheckForDuties re-check.
    /// </summary>
    internal static class GuardWantedReactionPatch
    {
        private const float SuspicionBumpAmount = 5f;

        /// <summary>D2: the CMC tag the Cloth Mask, its dyed variants and the Cloth Hood carry. Referenced by
        /// NAME in each card's CardTagsWarpData (no ScriptableObject/CardTag file ships for it).</summary>
        internal const string DisguiseTagName = "tag_CMCDisguise";

        /// <summary>TUNABLE PLACEHOLDER (D2): the share of a sighting's Suspicion bump that gets through while an
        /// equipped card carries <see cref="DisguiseTagName"/>. 0.5 halves it (5 becomes 2.5).</summary>
        internal const float DisguiseBumpFactor = 0.5f;

        /// <summary>Pure: the Suspicion a sighting adds, given whether a disguise is worn. Independent of the
        /// Burglar's Kit, which acts on the chest roll and never on Suspicion (open question 7).</summary>
        internal static float BumpAmountFor(bool disguised)
        {
            return disguised ? SuspicionBumpAmount * DisguiseBumpFactor : SuspicionBumpAmount;
        }

        private static readonly HashSet<string> _disguiseWarned = new HashSet<string>();
        private static PropertyInfo _equippedProp;

        private static void DisguiseWarnOnce(string cause, string message)
        {
            if (_disguiseWarned.Add(cause)) Plugin.Logger.LogWarning("[GuardWantedReactionPatch] " + message);
        }

        /// <summary>
        /// Whether any card the player is wearing right now carries <see cref="DisguiseTagName"/>. Read through
        /// <see cref="GameQuery.EquippedCards"/> (which warns once per cause when the equipment cannot be read and
        /// returns an empty list). Every non-throwing miss here leaves its own warn-once line, so a renamed
        /// CardData field reads as a logged cause and never as "nobody is disguised".
        /// </summary>
        internal static bool PlayerWearsDisguise()
        {
            try
            {
                // Read by reflection: this mod compiles against HerbsAndFungi/lib's framework copy, which predates
                // GameQuery.EquippedCards (framework 2.27+), while the framework installed with the game has it.
                _equippedProp ??= typeof(GameQuery).GetProperty("EquippedCards", BindingFlags.Public | BindingFlags.Static);
                if (_equippedProp == null)
                {
                    DisguiseWarnOnce("equippedcards-missing", "PlayerWearsDisguise: GameQuery.EquippedCards is not in the installed framework (needs a newer CSFFModFramework), so no disguise is ever read.");
                    return false;
                }
                if (_equippedProp.GetValue(null, null) is not IEnumerable equipped)
                {
                    DisguiseWarnOnce("equippedcards-unreadable", "PlayerWearsDisguise: GameQuery.EquippedCards returned nothing readable.");
                    return false;
                }
                foreach (object card in equipped)
                {
                    if (card == null) continue;
                    var model = CardUtil.GetCardData(card);
                    if (model == null) { DisguiseWarnOnce("model-null", "PlayerWearsDisguise: an equipped card has no CardData, so it cannot count as a disguise."); continue; }
                    var tagsField = CardUtil.GetCachedField(model.GetType(), "CardTags");
                    if (tagsField == null) { DisguiseWarnOnce("cardtags-field-missing", "PlayerWearsDisguise: CardData.CardTags was not found, so no disguise can be read."); return false; }
                    if (tagsField.GetValue(model) is not IList tags) continue;
                    foreach (var tag in tags)
                        if (tag is UnityEngine.Object uo && uo != null && uo.name == DisguiseTagName) return true;
                }
            }
            catch (Exception ex)
            {
                DisguiseWarnOnce("exception", "PlayerWearsDisguise failed: " + (ex.InnerException?.ToString() ?? ex.ToString()));
            }
            return false;
        }

        private static bool _initialized;
        private static Type _inGameNPCType, _npcStatInstantModifierType, _uidType;
        private static MethodInfo _changeStatMethod, _getFromIdMethod;
        private static object _suspicionStat;
        private static bool _loggedTypeFailure;

        /// <summary>Guards currently latched as "already reacted to this sighting."</summary>
        private static readonly HashSet<string> _reactedGuardUids = new(StringComparer.OrdinalIgnoreCase);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            TickEvents.Interval(5f, RunTick, "GuardWantedReaction");
            Plugin.Logger.LogDebug("[GuardWantedReactionPatch] initialized.");
        }

        private static void RunTick()
        {
            try
            {
                if (VillageCrimePatch.CurrentBand() != VillageCrimePatch.CrimeBand.Wanted)
                {
                    _reactedGuardUids.Clear();
                    return;
                }

                var playerEnv = GameQuery.CurrentEnvironmentUniqueId;
                if (string.IsNullOrEmpty(playerEnv)) return;

                var witnesses = GuardWitnessPatch.FindGuardsAt(playerEnv);
                if (witnesses.Count == 0) return;

                if (!ResolveTypes()) return;
                if (!ResolveSuspicionStat()) return;

                var stillPresent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var witness in witnesses)
                {
                    stillPresent.Add(witness.GuardAgentUid);
                    if (_reactedGuardUids.Contains(witness.GuardAgentUid)) continue;
                    if (BumpSuspicion(witness)) _reactedGuardUids.Add(witness.GuardAgentUid);
                }
                // Drop the latch for any guard no longer co-located, so a later re-sighting reacts again.
                _reactedGuardUids.RemoveWhere(uid => !stillPresent.Contains(uid));
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardWantedReactionPatch] RunTick failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static bool ResolveTypes()
        {
            if (_changeStatMethod != null) return true;

            _inGameNPCType = CardUtil.FindGameType("InGameNPC");
            _npcStatInstantModifierType = CardUtil.FindGameType("NPCStatInstantModifier");
            _uidType = CardUtil.FindGameType("UniqueIDScriptable");
            if (_inGameNPCType == null || _npcStatInstantModifierType == null || _uidType == null)
            {
                if (!_loggedTypeFailure)
                {
                    _loggedTypeFailure = true;
                    Plugin.Logger.LogWarning("[GuardWantedReactionPatch] Could not resolve InGameNPC/NPCStatInstantModifier/UniqueIDScriptable — Wanted-tier reaction inactive.");
                }
                return false;
            }

            _changeStatMethod = _inGameNPCType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "ChangeStat" && m.GetParameters().Length == 4
                    && m.GetParameters()[0].ParameterType == _npcStatInstantModifierType);
            _getFromIdMethod = _uidType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetFromID" && !m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

            if (_changeStatMethod == null || _getFromIdMethod == null)
            {
                if (!_loggedTypeFailure)
                {
                    _loggedTypeFailure = true;
                    Plugin.Logger.LogWarning("[GuardWantedReactionPatch] InGameNPC.ChangeStat(NPCStatInstantModifier,...) or UniqueIDScriptable.GetFromID not found — Wanted-tier reaction inactive.");
                }
                return false;
            }
            return true;
        }

        private static bool ResolveSuspicionStat()
        {
            if (_suspicionStat != null) return true;
            _suspicionStat = _getFromIdMethod.Invoke(null, new object[] { GuardDutyPatch.GuardSuspicionStatUid });
            return _suspicionStat != null;
        }

        /// <summary>
        /// Starts (never synchronously drains) the guard's own ChangeStat coroutine with an instant
        /// Suspicion modifier of <see cref="BumpAmountFor"/> (+5, or +2.5 while the player wears a disguise).
        /// Returns true only once the coroutine is actually started, so a failed cast/invoke does not
        /// falsely latch the per-sighting cooldown.
        /// </summary>
        private static bool BumpSuspicion(GuardWitnessPatch.Witness witness)
        {
            float amount = BumpAmountFor(PlayerWearsDisguise());
            if (!ChangeGuardSuspicion(witness?.GuardNpc, witness?.GuardLabel, amount, "GuardWantedReaction")) return false;
            Plugin.Logger.LogInfo($"[GuardWantedReactionPatch] {witness.GuardLabel} spots a Wanted face nearby - Suspicion +{amount:0.##}{(amount < SuspicionBumpAmount ? " (disguised)" : "")}.");
            return true;
        }

        /// <summary>
        /// The one write to a live guard's Suspicion: an instant NPCStat modifier of <paramref name="delta"/>
        /// (negative lowers it, and the stat's own floor at the guard's starting value still binds) started on
        /// the guard's own MonoBehaviour. Shared by the sighting bump and D3's bribes. Returns true only once
        /// the coroutine is started.
        /// </summary>
        internal static bool ChangeGuardSuspicion(object guardNpc, string label, float delta, string source)
        {
            if (guardNpc is not MonoBehaviour npcBehaviour) return false;
            if (!ResolveTypes()) return false;
            if (!ResolveSuspicionStat()) return false;
            try
            {
                object modifier = Activator.CreateInstance(_npcStatInstantModifierType);
                Reflect.SetMember(modifier, "TargetStat", _suspicionStat);
                Reflect.SetMember(modifier, "ValueChange", new Vector2(delta, delta));
                Reflect.SetMember(modifier, "IgnoreNovelty", true);

                int tick = 0;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm != null && Reflect.GetMember(gm, "CurrentTickInfo") is Vector3Int tickInfo) tick = tickInfo.z;

                if (_changeStatMethod.Invoke(guardNpc, new object[] { modifier, null, source, tick })
                    is not IEnumerator routine)
                    return false;

                npcBehaviour.StartCoroutine(routine);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning($"[GuardWantedReactionPatch] ChangeGuardSuspicion failed for {label}: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }
    }
}
