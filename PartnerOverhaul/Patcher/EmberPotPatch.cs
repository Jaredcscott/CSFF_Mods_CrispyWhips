namespace PartnerOverhaul.Patcher
{
    /// <summary>
    /// 1.1.5 (plan section 2, "Partner bedroll or carried ember pot"): the Ember Pot is opt-in, default OFF
    /// (owner tile 7, 2026-10-14: PO's design changes default off). The cards themselves are framework-loaded
    /// JSON (CardData/Item/EmberPot.json, EmberPotLit.json, CardData/Blueprint/Bp_EmberPot.json, tab in
    /// BlueprintTabs.json). BlueprintTabs.json has no conditional form and the framework injects the tab at UI
    /// time, so this file is the switch, and it only acts when the option is OFF:
    ///
    /// (1) GameLoad.LoadMainGameData postfix (pre-menu, after the framework loaded our cards): the lit pot's
    ///     warmth PassiveEffect is removed from OUR OWN CardData, so a pot already in a save warms nobody.
    /// (2) GameManager.OnGMInitialized (fires at the end of run initialisation, after the save's blueprint
    ///     states were replayed; the lever and timing CMC's AcademyCourseService uses): the blueprint is set
    ///     Hidden and its UnlockableCards entry Disabled, unless this save already researched it (an Available
    ///     blueprint is left alone). Both dictionaries are rebuilt every run, so turning the option on brings the
    ///     blueprint back at the next load. The pot is an end product, never an ingredient (CLAUDE.md
    ///     "A C# blueprint lock may only ever hide an END PRODUCT").
    ///
    /// With the option ON nothing here is patched or subscribed: the vanilla equip loop does the work.
    /// Mechanism (static read, 2026-10-18): InGameNPC.FindCardToEquip runs every Late tick over the Partner's
    /// inventory and equips any card whose CardData.IsEquipment (EquipmentTags non-empty) passes CanEquip and
    /// scores >= 0; a PassiveEffect with OnlyWhenEquipped then applies to the NPC owner (PassiveEffect
    /// .ShouldApplyToNPC), and its TemperaturePerceived StatModifier is converted to Partner_PerceivedTemperature
    /// by GameStatsToNPCStats (_PartnerStatEquivalencies), the same path a worn Fur Coat uses.
    /// </summary>
    public static class EmberPotPatch
    {
        private static BepInEx.Logging.ManualLogSource Logger => Plugin.Logger;

        internal const string LitPotUid = "partner_overhaul_ember_pot_lit";
        internal const string BlueprintUid = "partner_overhaul_bp_ember_pot";

        private static bool _subscribed;
        private static bool _warnedStrip;
        private static bool _warnedHide;

        public static void ApplyPatch(Harmony harmony)
        {
            if (Plugin.PartnerEmberPot != null && Plugin.PartnerEmberPot.Value)
            {
                Logger.LogDebug("EmberPotPatch: option on, the Ember Pot recipe and warmth are left as shipped.");
                return;
            }
            var target = AccessTools.Method(AccessTools.TypeByName("GameLoad"), "LoadMainGameData");
            if (target == null)
                Logger.LogWarning("[EmberPot] GameLoad.LoadMainGameData not found; an Ember Pot already in a save keeps its warmth while the option is off (game update?).");
            else
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(EmberPotPatch), nameof(LoadMainGameData_Postfix)));
            Subscribe();
        }

        private static void Subscribe()
        {
            if (_subscribed) return; // once per process: OnGMInitialized re-fires on every run boot
            _subscribed = true;
            GameManager.OnGMInitialized += OnRunInitialized;
        }

        internal static void Unsubscribe()
        {
            if (!_subscribed) return;
            GameManager.OnGMInitialized -= OnRunInitialized;
            _subscribed = false;
        }

        private static void LoadMainGameData_Postfix()
        {
            try
            {
                var lit = UniqueIDScriptable.GetFromID<CardData>(LitPotUid);
                if (lit == null)
                {
                    Logger.LogDebug("[EmberPot] the lit Ember Pot is not loaded (framework absent?); nothing to strip.");
                    return;
                }
                if (lit.PassiveEffects != null && lit.PassiveEffects.Length > 0)
                {
                    lit.PassiveEffects = new PassiveEffect[0];
                    Logger.LogDebug("[EmberPot] option off: the lit Ember Pot's warmth removed.");
                }
            }
            catch (Exception ex)
            {
                if (!_warnedStrip)
                {
                    _warnedStrip = true;
                    Logger.LogWarning($"[EmberPot] removing the pot's warmth failed (it keeps warming while the option is off): {ex}");
                }
            }
        }

        private static void OnRunInitialized()
        {
            try
            {
                if (Plugin.PartnerEmberPot != null && Plugin.PartnerEmberPot.Value) return;
                var gm = MBSingleton<GameManager>.Instance;
                if (gm == null || gm.BlueprintModelStates == null) return;
                var bp = UniqueIDScriptable.GetFromID<CardData>(BlueprintUid);
                if (bp == null || !gm.BlueprintModelStates.ContainsKey(bp)) return;
                if (gm.BlueprintModelStates[bp] == BlueprintModelState.Available)
                {
                    Logger.LogDebug("[EmberPot] option off, but this save already researched the Ember Pot: left available.");
                    return;
                }
                gm.BlueprintModelStates[bp] = BlueprintModelState.Hidden;
                if (gm.UnlockableCards != null)
                {
                    for (int i = 0; i < gm.UnlockableCards.Count; i++)
                    {
                        var entry = gm.UnlockableCards[i];
                        if (entry != null && entry.UnlockedCard == bp) entry.Disabled = true;
                    }
                }
                Logger.LogDebug("[EmberPot] option off: Ember Pot blueprint hidden for this run.");
            }
            catch (Exception ex)
            {
                if (!_warnedHide)
                {
                    _warnedHide = true;
                    Logger.LogWarning($"[EmberPot] hiding the Ember Pot blueprint failed (it may show while the option is off): {ex}");
                }
            }
        }
    }
}
