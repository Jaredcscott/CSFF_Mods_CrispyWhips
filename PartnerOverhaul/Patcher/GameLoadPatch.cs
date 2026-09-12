namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// Fixes 6 confirmed vanilla data-authoring gaps by mutating existing vanilla CardData
    /// objects at boot, once WarpResolver/ForeignInstanceReconciler have already run (this
    /// mod's framework SoftDependency guarantees load order — see Plugin.cs). Every fix is
    /// idempotent (checks before mutating) and independently try/caught so one missing card
    /// on a future game update can't take the others down with it.
    ///
    /// Root-cause research + GUID citations: Documentation/Plans/PartnerOverhaul/PartnerOverhaul_Plan.md
    /// </summary>
    public static class GameLoadPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        // Vanilla CardData UIDs (Documentation/GameData/.../UniqueIDScriptableGUID/CardData.json)
        private const string OldLqStewWaterUid = "a0e1cf6d47685a741b5cd9889fb39227";
        private const string LqNightBrothUid = "688d181eb1ea12642a028b4943ec6013";
        private const string CabinUid = "82d66ecd24814a94abd8446862a041b7";
        private const string MudHutUid = "8dad01b8594c63a4883cc49170bc70eb";
        private const string EnclosureUid = "066ad822f09706e4087e2c1af6b50d26";
        private const string MudHutEnclosureUid = "77ff1cc7a0ee7d445a3ca20fca021efc";
        private const string MudHutExpansionLeftUid = "d077e3b6aee2d884dbd77059db9423cf";
        private const string GardenPlotUid = "f853a0a8ad6c64c4b81eabb8367c0c5c";
        private const string FireplaceUid = "e50543ef8a7e7d543a42e199adeee963";
        private const string TreeTopEventUid = "bd685ebfe220e5d449fd4b8eb6126fe2";

        // Vanilla NPCDuty UIDs (Documentation/GameData/.../UniqueIDScriptableGUID/NPCDuty.json)
        private const string PartnerDutyEatUid = "a9de36f279b1e6449a01d9b05e45aa5d";
        private const string PartnerDutyEatFreeUid = "a4340107e78034a4192d126d8129ec67";
        private const string PartnerDutyCleanUid = "95fb94d06ae17a442b63c1c11d812d35";
        private const string PartnerDutyFirekeepingUid = "6147eff5b0fe3014ca57364f4d075514";

        // Vanilla NPCStat UID (Documentation/GameData/.../NPCStat/Partner_Energy.json)
        private const string PartnerEnergyUid = "325d096ddda132b4d9c1aea62d37153a";

        // Above this current Hydration %, GardenPlot's "Water" CardInteraction is gated off —
        // stops a Partner from endlessly re-watering an already-saturated plot.
        private const float GardenOverwaterGatePercent = 90f;

        public static void ApplyPatch(Harmony harmony)
        {
            var targetType = AccessTools.TypeByName("GameLoad");
            var targetMethod = AccessTools.Method(targetType, "LoadMainGameData");
            var postfix = AccessTools.Method(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
            harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
            Logger.LogDebug("GameLoadPatch applied.");
        }

        // Counts duty refs appended across the whole load pass so the per-target detail can stay
        // at Debug and still be summarised in one Info line. Reset per pass: LoadMainGameData
        // runs again on every return to the main menu.
        private static int _dutyRefsAdded;

        private static void LoadMainGameData_Postfix()
        {
            _dutyRefsAdded = 0;
            try { FixStewBrothEating(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixStewBrothEating failed: {ex}"); }
            try { FixCleaningGap(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixCleaningGap failed: {ex}"); }
            try { FixGardenOverwatering(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixGardenOverwatering failed: {ex}"); }
            try { FixFireplaceFuelGap(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixFireplaceFuelGap failed: {ex}"); }
            try { FixStrayDefecateButton(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixStrayDefecateButton failed: {ex}"); }
            try { FixPartnerEnergyVisibility(); } catch (Exception ex) { Logger.LogError($"[GameLoadPatch] FixPartnerEnergyVisibility failed: {ex}"); }

            if (_dutyRefsAdded > 0)
                Logger.LogInfo($"[GameLoadPatch] attached {_dutyRefsAdded} duty ref(s) to vanilla actions (enable Debug logging for the per-target list).");
        }

        // ── Fix 1: stew/broth "Drink" is invisible to the Eat duty engine ──────────────────
        private static void FixStewBrothEating()
        {
            var eatDuty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyEatUid);
            var eatFreeDuty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyEatFreeUid);

            foreach (var (uid, label) in new[] { (OldLqStewWaterUid, "OLD_LQ_StewWater"), (LqNightBrothUid, "LQ_NightBroth") })
            {
                var card = UniqueIDScriptable.GetFromID<CardData>(uid);
                if (card == null) { Logger.LogWarning($"[GameLoadPatch] {label} not resolvable — skipping stew/broth eat fix."); continue; }

                var drink = card.DismantleActions?.FirstOrDefault(a => a.ActionName != null && a.ActionName.DefaultText == "Drink");
                if (drink == null) { Logger.LogWarning($"[GameLoadPatch] {label}: 'Drink' DismantleAction not found — skipping."); continue; }

                // Deliberately NOT PartnerDuty_DrinkFree — a thirsty Partner should still
                // target plain water, matching vanilla's own LQ_Water wiring pattern.
                AppendDutyRefIfMissing(drink, eatDuty, "PartnerDuty_Eat", $"{label}.Drink");
                AppendDutyRefIfMissing(drink, eatFreeDuty, "PartnerDuty_EatFree", $"{label}.Drink");
            }
        }

        // ── Fix 2: "Clean" on finished homes is invisible to the Clean duty engine ─────────
        private static void FixCleaningGap()
        {
            var cleanDuty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyCleanUid);
            var homes = new[]
            {
                (CabinUid, "Cabin"), (MudHutUid, "MudHut"), (EnclosureUid, "Enclosure"),
                (MudHutEnclosureUid, "MudHutEnclosure"), (MudHutExpansionLeftUid, "MudHutExpansionLeft"),
            };

            foreach (var (uid, label) in homes)
            {
                var card = UniqueIDScriptable.GetFromID<CardData>(uid);
                if (card == null) { Logger.LogWarning($"[GameLoadPatch] {label} not resolvable — skipping cleaning fix."); continue; }

                var clean = card.CardInteractions?.FirstOrDefault(a => a.ActionName != null && a.ActionName.DefaultText == "Clean");
                if (clean == null) { Logger.LogWarning($"[GameLoadPatch] {label}: 'Clean' CardInteraction not found — skipping."); continue; }

                AppendDutyRefIfMissing(clean, cleanDuty, "PartnerDuty_Clean", $"{label}.Clean");
            }
        }

        // ── Fix 3: GardenPlot "Water" never re-checks current Hydration ────────────────────
        private static void FixGardenOverwatering()
        {
            var card = UniqueIDScriptable.GetFromID<CardData>(GardenPlotUid);
            if (card == null) { Logger.LogWarning("[GameLoadPatch] GardenPlot not resolvable — skipping over-watering fix."); return; }

            var water = card.CardInteractions?.FirstOrDefault(a => a.ActionName != null && a.ActionName.DefaultText == "Water");
            if (water == null) { Logger.LogWarning("[GameLoadPatch] GardenPlot: 'Water' CardInteraction not found — skipping."); return; }

            // OptionalValue.Active is `protected` — read it via the class's own public
            // `implicit operator bool` instead of the field directly.
            if (water.RequiredReceivingDurabilities.RequiredFuelPercent)
            {
                Logger.LogDebug("[GameLoadPatch] GardenPlot.Water already gated — skipping (idempotent).");
                return;
            }

            // SetActiveWithValue(Vector2) is OptionalRangeValue's public mutator — sets
            // Active=true plus FloatValue/MaxValue from the (min,max) pair in one call.
            water.RequiredReceivingDurabilities.RequiredFuelPercent.SetActiveWithValue(new Vector2(0f, GardenOverwaterGatePercent));
            Logger.LogInfo($"[GameLoadPatch] GardenPlot.Water: armed RequiredFuelPercent gate (0-{GardenOverwaterGatePercent}%).");
        }

        // ── Fix 4: Fireplace Pine Needles/Charcoal invisible to the Firekeeping duty ───────
        private static void FixFireplaceFuelGap()
        {
            var firekeepingDuty = UniqueIDScriptable.GetFromID<NPCDuty>(PartnerDutyFirekeepingUid);
            var card = UniqueIDScriptable.GetFromID<CardData>(FireplaceUid);
            if (card == null) { Logger.LogWarning("[GameLoadPatch] Fireplace not resolvable — skipping fuel-gap fix."); return; }

            foreach (var actionName in new[] { "Feed Pine Needles", "Feed Charcoal" })
            {
                var action = card.CardInteractions?.FirstOrDefault(a => a.ActionName != null && a.ActionName.DefaultText == actionName);
                if (action == null) { Logger.LogWarning($"[GameLoadPatch] Fireplace: '{actionName}' CardInteraction not found — skipping."); continue; }

                AppendDutyRefIfMissing(action, firekeepingDuty, "PartnerDuty_Firekeeping", $"Fireplace.{actionName}");
            }
        }

        // ── Fix 5: stray "Defecate" DA on the Tree Top scenic event ────────────────────────
        private static void FixStrayDefecateButton()
        {
            var card = UniqueIDScriptable.GetFromID<CardData>(TreeTopEventUid);
            if (card == null) { Logger.LogWarning("[GameLoadPatch] Event_TreeTopBase not resolvable — skipping stray Defecate fix."); return; }
            if (card.DismantleActions == null) return;

            int removed = card.DismantleActions.RemoveAll(a => a.ActionName != null && a.ActionName.DefaultText == "Defecate");
            if (removed > 0)
                Logger.LogInfo($"[GameLoadPatch] Event_TreeTopBase: removed {removed} stray 'Defecate' DismantleAction(s).");
        }

        // ── Fix 6: Partner card shows no Energy status at all ──────────────────────────────
        // NPCStat.Statuses[] holds fully-authored threshold text ("Looks exhausted." /
        // "Looks very tired." / "Looks tired." / "Looks full of energy."), but every entry on
        // vanilla's Partner_Energy has VisibleToPlayer=false AND ShowInMainTab=false — unlike
        // every sibling need stat (Partner_Satiation/BodyTemperature/Thirst/Wakefulness), which
        // set both true on their non-neutral entries so NPCInspectionPopup's main-tab status
        // list (InGameNPC.VisibleStatuses, tab index 0) actually surfaces them. Net effect: the
        // player has zero in-game visibility into a Partner's energy level. VisibleToPlayer is
        // a private [SerializeField] on the NPCStatStatus class (not the ScriptableObject
        // itself), so it needs one reflected field set per entry; ShowInMainTab is public.
        private static void FixPartnerEnergyVisibility()
        {
            var stat = UniqueIDScriptable.GetFromID<NPCStat>(PartnerEnergyUid);
            if (stat == null) { Logger.LogWarning("[GameLoadPatch] Partner_Energy not resolvable — skipping energy visibility fix."); return; }
            if (stat.Statuses == null || stat.Statuses.Length == 0) { Logger.LogWarning("[GameLoadPatch] Partner_Energy has no Statuses — skipping."); return; }

            var visibleField = typeof(NPCStatStatus).GetField("VisibleToPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (visibleField == null) { Logger.LogWarning("[GameLoadPatch] NPCStatStatus.VisibleToPlayer field not found — layout changed?"); return; }

            if (stat.Statuses[0] != null && stat.Statuses[0].ShowInMainTab && (bool)visibleField.GetValue(stat.Statuses[0]))
            {
                Logger.LogDebug("[GameLoadPatch] Partner_Energy statuses already visible — skipping (idempotent).");
                return;
            }

            int fixedCount = 0;
            foreach (var status in stat.Statuses)
            {
                if (status == null) continue;
                visibleField.SetValue(status, true);
                status.ShowInMainTab = true;
                fixedCount++;
            }
            Logger.LogInfo($"[GameLoadPatch] Partner_Energy: made {fixedCount} status text(s) visible on the Partner card (were fully hidden).");
        }

        // ── shared helpers ──────────────────────────────────────────────────────────────
        private static void AppendDutyRefIfMissing(CardAction action, NPCDuty duty, string dutyLabel, string context)
        {
            if (duty == null) { Logger.LogWarning($"[GameLoadPatch] {context}: duty '{dutyLabel}' not resolvable — skipping."); return; }

            var existing = action.CompatibleNPCDuties ?? Array.Empty<NPCDutyOrDutyTagRef>();
            if (existing.Any(r => r.DutyTarget == duty))
            {
                Logger.LogDebug($"[GameLoadPatch] {context}: {dutyLabel} already present — skipping (idempotent).");
                return;
            }

            var newArr = new NPCDutyOrDutyTagRef[existing.Length + 1];
            Array.Copy(existing, newArr, existing.Length);
            newArr[existing.Length] = MakeDutyRef(duty);
            action.CompatibleNPCDuties = newArr;
            _dutyRefsAdded++;
            // Per-target detail is Debug (12 targets = 12 Info lines every boot); the caller
            // emits one aggregate Info line instead. Per CLAUDE.md § Mod Logging Norms.
            Logger.LogDebug($"[GameLoadPatch] {context}: added {dutyLabel} to CompatibleNPCDuties.");
        }

        private static NPCDutyOrDutyTagRef MakeDutyRef(NPCDuty duty)
        {
            var dutyRef = default(NPCDutyOrDutyTagRef);
            ReflectionHelper.SetPrivateField(ref dutyRef, "Target", duty);
            return dutyRef;
        }
    }
}
