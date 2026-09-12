using System.Reflection.Emit;

namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// Wound-stacking consolidation (Plan Phase 4 item 3).
    ///
    /// Vanilla <c>EncounterPopup.GenerateAndApplyPlayerWound()</c> (.decomp/EncounterPopup.cs:3488-3793)
    /// builds a fresh candidate list every combat round and adds each entry of
    /// <c>CurrentEncounter.CurrentRoundPlayerWound.DroppedCards</c> to it, gated ONLY by
    /// <c>CardData.CanSpawnOnBoard</c> (.decomp/CardData.cs:2496 — UniqueOnBoard / SpawningBlockedBy /
    /// already-dropped-this-event). Wound CardData ship <c>UniqueOnBoard: False</c>, so nothing
    /// dedups against a same-type wound the player already has equipped: every round produces a
    /// wholly separate <c>InGameCardBase</c> in its own equipment slot, with its own independently
    /// ticking Recovery/Infection/Pain/Bleeding stats. "28x Minor Laceration" is 28 real instances,
    /// not a display bug.
    ///
    /// This patch skips adding a wound CardData to that per-round candidate list when an instance of
    /// the same wound is already equipped. Nothing downstream of the candidate list is touched —
    /// damage math (<c>CurrentRoundEnemyDamageReport</c>), <c>StatChanges</c>, the armor-durability
    /// loop, and the encounter log all run exactly as vanilla.
    ///
    /// TRANSPILER, NOT PREFIX — deliberate, and the only safe option:
    ///  * A prefix cannot pre-filter <c>CurrentRoundPlayerWound</c>: that field is ASSIGNED INSIDE the
    ///    method (<c>= SelectPlayerWound(_List, bodyLocations)</c>, .decomp/EncounterPopup.cs:3641).
    ///    At prefix time it still holds the PREVIOUS round's wound, or null on round 1.
    ///  * Even after assignment, <c>PlayerWound.DroppedCards</c> is a plain public <c>CardData[]</c>
    ///    field on a shared, serialized <c>PlayerWound</c> owned by the encounter's wound list —
    ///    rewriting it would permanently corrupt vanilla wound data for the rest of the session.
    ///
    /// The transpiler replaces exactly ONE instruction (the <c>List&lt;CardData&gt;.Add</c> callvirt at
    /// the wound-candidate site) by mutating that <c>CodeInstruction</c>'s opcode+operand IN PLACE, so
    /// its <c>.labels</c> and <c>.blocks</c> are carried over by construction — the strictest form of
    /// root CLAUDE.md's label/block transfer rule. If the site can't be uniquely located, the original
    /// IL is returned untouched and an error is logged; core combat resolution is never left half-patched.
    /// </summary>
    public static class WoundStackingPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        // How far back from a List<CardData>.Add call to look for the PlayerWound.DroppedCards load
        // that identifies the wound-candidate site. Real spacing is 3 instructions
        // (ldfld DroppedCards / ldloc l / ldelem.ref / callvirt Add); the two OTHER
        // List<CardData>.Add sites in this method (ExtraDurabilityChange.AffectedCards.Add and the
        // armor-model list4.Add) are hundreds of instructions away from any DroppedCards load.
        private const int MarkerLookbackWindow = 12;

        public static void ApplyPatch(Harmony harmony)
        {
            var target = AccessTools.Method(typeof(EncounterPopup), "GenerateAndApplyPlayerWound");
            if (target == null)
            {
                Logger.LogError("[WoundStackingPatch] EncounterPopup.GenerateAndApplyPlayerWound not found — wound consolidation skipped.");
                return;
            }

            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(WoundStackingPatch), nameof(Transpile)));
            Logger.LogDebug("WoundStackingPatch applied.");
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var hook = AccessTools.Method(typeof(WoundStackingPatch), nameof(AddWoundCandidate));

            int matchIndex = -1;
            int matchCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!IsListOfCardDataAdd(codes[i])) continue;
                if (!HasDroppedCardsMarkerBefore(codes, i)) continue;
                matchCount++;
                if (matchIndex < 0) matchIndex = i;
            }

            if (matchCount != 1 || hook == null)
            {
                // Fail safe: leave core combat resolution exactly as vanilla rather than guess.
                Logger.LogError($"[WoundStackingPatch] Could not uniquely locate the wound-candidate "
                    + $"List<CardData>.Add site (matches={matchCount}, hook={(hook == null ? "null" : "ok")}). "
                    + "IL left UNMODIFIED — wound consolidation is inactive this session.");
                return codes;
            }

            // In-place opcode/operand swap on the SAME CodeInstruction object: any .labels/.blocks
            // attached to this instruction travel with it untouched (root CLAUDE.md § Transpilers).
            // Stack shape is identical — [List<CardData>, CardData] in, void out.
            codes[matchIndex].opcode = OpCodes.Call;
            codes[matchIndex].operand = hook;

            Logger.LogDebug($"[WoundStackingPatch] Redirected wound-candidate Add at IL index {matchIndex}.");
            return codes;
        }

        private static bool IsListOfCardDataAdd(CodeInstruction ci)
        {
            if (!(ci.operand is MethodInfo mi) || mi.Name != "Add") return false;
            var declaring = mi.DeclaringType;
            return declaring != null
                && declaring.IsGenericType
                && declaring.GetGenericTypeDefinition() == typeof(List<>)
                && declaring.GetGenericArguments()[0] == typeof(CardData);
        }

        private static bool HasDroppedCardsMarkerBefore(List<CodeInstruction> codes, int addIndex)
        {
            int start = Math.Max(0, addIndex - MarkerLookbackWindow);
            for (int i = addIndex - 1; i >= start; i--)
            {
                if (codes[i].opcode == OpCodes.Ldfld
                    && codes[i].operand is FieldInfo fi
                    && fi.DeclaringType == typeof(PlayerWound)
                    && fi.Name == nameof(PlayerWound.DroppedCards))
                {
                    return true;
                }
            }
            return false;
        }

        // ── Replacement for `list.Add(CurrentRoundPlayerWound.DroppedCards[l])` ───────────────
        // Called from patched IL. MUST keep the exact (List<CardData>, CardData) -> void shape.
        public static void AddWoundCandidate(List<CardData> _List, CardData _Card)
        {
            try
            {
                if (Plugin.ConsolidateWounds != null && Plugin.ConsolidateWounds.Value
                    && _Card != null && IsWoundAlreadyPresent(_List, _Card))
                {
                    // Per-event, fires on every suppressed wound: Debug, not Info.
                    Logger.LogDebug($"[WoundStacking] Suppressed duplicate wound '{_Card.UniqueID}' "
                        + "— an instance of this wound is already equipped.");
                    return;
                }
            }
            catch (Exception ex)
            {
                // Breadcrumb required by root CLAUDE.md § Silent Catch Blocks. Falls through to the
                // vanilla Add below, so a failed check can never LOSE a wound.
                Logger.LogDebug($"[WoundStackingPatch] Dedup check failed for "
                    + $"'{(_Card != null ? _Card.UniqueID : "(null)")}': {ex} — applying vanilla behavior.");
            }

            _List?.Add(_Card);
        }

        /// <summary>
        /// True when <paramref name="_Card"/> is a wound AND an instance of it is already equipped
        /// (or already queued in this same round's candidate list). Every lookup below is resolved
        /// fresh on each call — no CharacterScreen / CardLine / Slots reference is cached anywhere in
        /// this class, so the equipped set read here is always the LIVE one (root CLAUDE.md
        /// § Runtime Card State Caching). Returns false on ANY missing link so a not-yet-initialized
        /// character screen degrades to plain vanilla behavior instead of throwing.
        /// </summary>
        private static bool IsWoundAlreadyPresent(List<CardData> _List, CardData _Card)
        {
            var graphics = MBSingleton<GraphicsManager>.Instance;
            if (graphics == null) return false;

            var characterScreen = graphics.CharacterWindow;
            if (characterScreen == null) return false;

            // Gate on wound-ness FIRST: armor, weapons and every other DroppedCards entry fall
            // straight through to vanilla.
            if (!characterScreen.CardIsWound(_Card)) return false;

            // Same wound already queued earlier in THIS round's candidate list (the equipped list
            // isn't updated until the produced-cards coroutine runs, so it can't catch this case).
            if (_List != null)
            {
                for (int i = 0; i < _List.Count; i++)
                {
                    if (IsSameWound(_List[i], _Card)) return true;
                }
            }

            var line = characterScreen.EquipmentSlotsLine;
            if (line == null) return false;

            var slots = line.Slots;
            if (slots == null || slots.Count == 0) return false;

            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;

                var assigned = slot.AssignedCard;
                if (assigned == null || assigned.Destroyed) continue;

                if (IsSameWound(assigned.CardModel, _Card)) return true;
            }

            return false;
        }

        private static bool IsSameWound(CardData _A, CardData _B)
        {
            if (_A == null || _B == null) return false;
            if (ReferenceEquals(_A, _B)) return true;
            // UniqueID fallback: a same-UID-but-different-instance CardData is still the same wound
            // type to the player (root CLAUDE.md § Runtime Card State Caching, manifestation 2).
            return !string.IsNullOrEmpty(_A.UniqueID) && _A.UniqueID == _B.UniqueID;
        }
    }
}
