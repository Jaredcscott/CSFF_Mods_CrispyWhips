using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Enables AlwaysUpdate ticking on mod cards so they process durability decay,
/// spoilage, stat changes, etc. while the player is on another board.
///
/// Previously only H&amp;F had this; now the framework does it for all mods.
///
/// <para><b>Scope (DG-F1, decided 2026-09-07 - option (b)).</b> The flag is NOT set on a
/// CT2 (<c>CardTypes.Location</c>) card that has nothing to process over time. Setting it
/// makes <c>CardData.IndependentFromEnv</c> true (<c>.decomp/CardData.cs:1270-1283</c>), which
/// keeps the card in <c>GameManager.AllCards</c> when the player leaves instead of writing it
/// into the leaving environment's saved card list (<c>.decomp/GameManager.cs:10244</c>/<c>:10263</c>/
/// <c>:10282</c>). That is exactly what background ticking needs - <c>ApplyRates</c> iterates
/// <c>AllCards</c> (<c>:5812</c>) and <c>InGameCardBase</c> gates background work on the flag
/// (<c>.decomp/InGameCardBase.cs:6165</c>/<c>:6214</c>/<c>:6225</c>) - but a notice board, an exit
/// card or an empty chest gains nothing from it, while still costing unbounded <c>AllCards</c>
/// growth (<c>:10301</c> never removes them) and making <c>EnvironmentsData</c> an unreliable
/// answer to "is this fixture persisted on that board".</para>
///
/// <para><b>Author escape hatch.</b> A card judged inert here is left exactly as authored - it is
/// never forced to <c>false</c>. So a mod that genuinely needs background tracking on an
/// otherwise-inert CT2 fixture opts in declaratively by shipping <c>"AlwaysUpdate": true</c> in
/// the card's own JSON.</para>
/// </summary>
internal static class AlwaysUpdateService
{
    // CardTypes enum (EA 0.67i, .decomp/CardTypes.cs): Item=0, Base=1, Location=2, Event=3,
    // Environment=4, Weather=5, Hand=6, Blueprint=7, Explorable=8, Liquid=9, EnvImprovement=10,
    // EnvDamage=11, BlueprintInLocation=12, InvisibleCard=13.
    private const int CtLocation    = 2;
    private const int CtEnvironment = 4;
    private const int CtExplorable  = 8;

    // The eight DurabilityStat blocks whose rates GameManager.ApplyRates feeds to
    // ChangeCardDurabilities every tick (.decomp/GameManager.cs:5843 - spoilage, usage, fuel,
    // consumable(Progress), evaporation, special1-4).
    private static readonly string[] DurabilityBlocks =
    {
        "SpoilageTime", "UsageDurability", "FuelCapacity", "Progress",
        "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpecialDurability4",
    };

    // Non-durability inputs to the same per-tick pass: UpdatePassiveEffectStacks /
    // UpdateTransferEffects / UpdateProducedLiquids (.decomp/GameManager.cs:5844-5849) and the
    // separate cards-with-counters loop (:5775, gated on CardModel.ActiveCounters).
    private static readonly string[] TickEffectArrays =
    {
        "PassiveStatEffects", "PassiveEffects", "RemotePassiveEffects",
        "DurabilityTransferEffects", "ActiveCounters",
    };

    private static readonly string[] RateFields          = { "RatePerDaytimePoint", "ExtraRateWhenEquipped" };
    private static readonly string[] RateClampFields     = { "MinRate", "MaxRate" };
    private static readonly string[] ThresholdActionFields = { "HasActionOnZero", "HasActionOnFull" };

    // Reflection on this path returns null rather than throwing (CardUtil.GetCachedField), so a
    // field rename would otherwise silently flip every CT2 card to "inert" - the exact opposite of
    // the conservative default. Every miss is keyed per CAUSE, warns once, and falls back to the
    // pre-DG-F1 behaviour (force the flag) rather than skipping.
    private static readonly HashSet<string> _warnedCauses = new();

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause)) Log.Warn(message);
    }

    public static void EnableAll(IEnumerable allData, List<ModManifest> mods)
    {
        var modUniqueIds = JsonDataLoader.AllModUniqueIds;
        if (modUniqueIds.Count == 0) return;

        var alwaysUpdateField = AccessTools.Field(typeof(CardData), "AlwaysUpdate");
        if (alwaysUpdateField == null || alwaysUpdateField.FieldType != typeof(bool))
            return;

        // Env / navigation nodes (CT4 Environment, CT8 Explorable) must NOT get AlwaysUpdate.
        // For those CardTypes, CardData.IndependentFromEnv == AlwaysUpdate (CardData.cs:1275-1289),
        // and GameManager.ChangeEnvironment re-homes IndependentFromEnv cards to the new env (they
        // "follow" the player) instead of saving+removing them like a vanilla env node - which for a
        // location card produces an orphan on the destination board whose travel DA loops back to the
        // current env (travel softlock). Vanilla env/explorable cards ship AlwaysUpdate=false; mod
        // ones must match. Env nodes also have no durability/spoilage that needs per-tick processing.
        // (WorldMap clone nodes are fixed at clone time in CardCloneService - they don't exist yet at
        // this phase; this skip covers mod-shipped CT4/CT8 cards such as the framework hub.)
        var cardTypeField = AccessTools.Field(typeof(CardData), "CardType");

        // For all OTHER mod cards we want AlwaysUpdate = true, EXCEPT inert CT2 fixtures (DG-F1).
        int updated = 0, skippedEnvNodes = 0, corrected = 0, skippedInert = 0;
        foreach (var item in allData)
        {
            if (!(item is CardData card)) continue;
            if (string.IsNullOrEmpty(card.UniqueID) || !modUniqueIds.Contains(card.UniqueID))
                continue;

            if (IsEnvNode(cardTypeField, card))
            {
                skippedEnvNodes++;

                // Actively force AlwaysUpdate=false rather than merely skipping the true-forcing
                // pass below. A mod-authored CT4/CT8 card shipped with AlwaysUpdate:true (the
                // vanilla env/explorable default, or an authoring copy-paste mistake) is the exact
                // travel-softlock precondition CardCloneService.CloneCard already force-corrects
                // for CLONED env nodes at clone time (see its comment ~182-194) - this closes the
                // same hole for mod-authored nodes that were never cloned (CMC's 7 interior CT8
                // locations shipped AlwaysUpdate:true for a month before the data was hand-fixed;
                // root CLAUDE.md §WorldMap; memory reference_alwaysupdate_env_node_follow).
                bool current;
                try { current = alwaysUpdateField.GetValue(card) is true; }
                catch (Exception ex)
                {
                    Log.Debug($"AlwaysUpdateService: AlwaysUpdate read failed for '{card.UniqueID}': {Log.ExceptionText(ex)}");
                    continue;
                }

                if (current)
                {
                    alwaysUpdateField.SetValue(card, false);
                    corrected++;
                    Log.Info($"AlwaysUpdateService: corrected '{card.UniqueID}' (CT4/CT8) from AlwaysUpdate=true to false " +
                             "- a mod-authored env/explorable node must not follow the player between environments (travel softlock risk).");
                }
                continue;
            }

            // DG-F1: an inert CT2 board fixture is left exactly as authored. NOT forced to false -
            // "AlwaysUpdate": true in the card's own JSON stays the author's opt-in.
            if (IsInertBoardFixture(cardTypeField, card))
            {
                skippedInert++;
                continue;
            }

            alwaysUpdateField.SetValue(card, true);
            updated++;
        }

        if (updated > 0 || skippedEnvNodes > 0 || skippedInert > 0)
            Log.Debug($"AlwaysUpdateService: enabled ticking on {updated} mod cards " +
                      $"({skippedEnvNodes} env/explorable node(s) skipped, {corrected} corrected from AlwaysUpdate=true, " +
                      $"{skippedInert} inert CT2 fixture(s) left as authored)");
    }

    // CardTypes enum (EA 0.64f): Environment=4, Explorable=8 - see Documentation/CSFF_Reference.md.
    private static bool IsEnvNode(FieldInfo cardTypeField, CardData card)
    {
        if (cardTypeField == null) return false;
        try
        {
            int ct = Convert.ToInt32(cardTypeField.GetValue(card));
            return ct == CtEnvironment || ct == CtExplorable;
        }
        catch (Exception ex) { Log.Debug($"AlwaysUpdateService: CardType read threw for '{card?.UniqueID}': {Log.ExceptionText(ex)}"); return false; }
    }

    /// <summary>
    /// True only for a CT2 (Location) card with nothing for a tick to process. Every uncertain
    /// answer - unreadable CardType, a missing reflected field, a throwing read - returns FALSE,
    /// i.e. keeps the pre-DG-F1 force-true behaviour, because a fixture wrongly judged inert stops
    /// ticking in the background and that is the failure mode with player-visible cost.
    /// </summary>
    private static bool IsInertBoardFixture(FieldInfo cardTypeField, CardData card)
    {
        if (cardTypeField == null) return false;

        int ct;
        try { ct = Convert.ToInt32(cardTypeField.GetValue(card)); }
        catch (Exception ex)
        {
            WarnOnce("cardtype-read",
                $"AlwaysUpdateService: CardType read threw for '{card?.UniqueID}' - forcing AlwaysUpdate as before: {Log.ExceptionText(ex)}");
            return false;
        }

        // The finding is specifically about CT2 board fixtures. Every other CardType keeps the
        // original unconditional behaviour - narrowing further would be a separate decision.
        if (ct != CtLocation) return false;

        foreach (var name in DurabilityBlocks)
        {
            var field = CardUtil.GetCachedField(typeof(CardData), name);
            if (field == null)
            {
                WarnOnce("durability-field:" + name,
                    $"AlwaysUpdateService: CardData.{name} not found (game update?) - cannot judge CT2 fixtures inert, forcing AlwaysUpdate as before.");
                return false;
            }

            object block;
            try { block = field.GetValue(card); }
            catch (Exception ex)
            {
                WarnOnce("durability-read:" + name,
                    $"AlwaysUpdateService: CardData.{name} read threw (first on '{card.UniqueID}') - forcing AlwaysUpdate as before: {Log.ExceptionText(ex)}");
                return false;
            }

            if (block == null) continue;
            if (StatTicksOrUnknown(block, name, card.UniqueID)) return false;
        }

        foreach (var name in TickEffectArrays)
        {
            var field = CardUtil.GetCachedField(typeof(CardData), name);
            if (field == null)
            {
                WarnOnce("effect-field:" + name,
                    $"AlwaysUpdateService: CardData.{name} not found (game update?) - cannot judge CT2 fixtures inert, forcing AlwaysUpdate as before.");
                return false;
            }

            try
            {
                if (field.GetValue(card) is Array arr && arr.Length > 0) return false;
            }
            catch (Exception ex)
            {
                WarnOnce("effect-read:" + name,
                    $"AlwaysUpdateService: CardData.{name} read threw (first on '{card.UniqueID}') - forcing AlwaysUpdate as before: {Log.ExceptionText(ex)}");
                return false;
            }
        }

        // A liquid container keeps its contained liquid's evaporation running through the container's
        // own tick (.decomp/InGameCardBase.cs:503-512, :1693-1697), so any capacity at all counts.
        var liquidField = CardUtil.GetCachedField(typeof(CardData), "MaxLiquidCapacity");
        if (liquidField == null)
        {
            WarnOnce("liquid-field",
                "AlwaysUpdateService: CardData.MaxLiquidCapacity not found (game update?) - cannot judge CT2 fixtures inert, forcing AlwaysUpdate as before.");
            return false;
        }

        try
        {
            if (Convert.ToSingle(liquidField.GetValue(card)) != 0f) return false;
        }
        catch (Exception ex)
        {
            WarnOnce("liquid-read",
                $"AlwaysUpdateService: MaxLiquidCapacity read threw (first on '{card.UniqueID}') - forcing AlwaysUpdate as before: {Log.ExceptionText(ex)}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// True when a DurabilityStat block participates in per-tick work, OR when we cannot tell.
    /// A block is inert only when it is switched off, or on with no rate, no rate clamp and no
    /// threshold action - an unlit lantern (FuelCapacity Active, rate 0) versus a lit one
    /// (rate -1.0, HasActionOnZero).
    /// </summary>
    private static bool StatTicksOrUnknown(object block, string blockName, string uid)
    {
        var type = block.GetType();

        var activeField = CardUtil.GetCachedField(type, "Active");   // inherited from OptionalValue
        if (activeField == null)
        {
            WarnOnce("stat-field:Active",
                $"AlwaysUpdateService: {type.Name}.Active not found (game update?) - cannot judge CT2 fixtures inert, forcing AlwaysUpdate as before.");
            return true;
        }

        try
        {
            if (!(activeField.GetValue(block) is true)) return false;   // switched off: nothing ticks
        }
        catch (Exception ex)
        {
            WarnOnce("stat-read:Active",
                $"AlwaysUpdateService: {type.Name}.Active read threw (first on '{uid}'.{blockName}) - forcing AlwaysUpdate as before: {Log.ExceptionText(ex)}");
            return true;
        }

        foreach (var rateName in RateFields)
        {
            var rateField = CardUtil.GetCachedField(type, rateName);
            if (rateField == null)
            {
                WarnOnce("stat-field:" + rateName,
                    $"AlwaysUpdateService: {type.Name}.{rateName} not found (game update?) - cannot judge CT2 fixtures inert, forcing AlwaysUpdate as before.");
                return true;
            }

            try
            {
                if (Convert.ToSingle(rateField.GetValue(block)) != 0f) return true;
            }
            catch (Exception ex)
            {
                WarnOnce("stat-read:" + rateName,
                    $"AlwaysUpdateService: {type.Name}.{rateName} read threw (first on '{uid}'.{blockName}) - forcing AlwaysUpdate as before: {Log.ExceptionText(ex)}");
                return true;
            }
        }

        // MinRate/MaxRate are OptionalFloatValue clamps - an active one means a rate is expected
        // even when the base rate is authored 0 and supplied at runtime.
        foreach (var clampName in RateClampFields)
        {
            var clampField = CardUtil.GetCachedField(type, clampName);
            if (clampField == null)
            {
                WarnOnce("stat-field:" + clampName,
                    $"AlwaysUpdateService: {type.Name}.{clampName} not found (game update?) - cannot judge CT2 fixtures inert, forcing AlwaysUpdate as before.");
                return true;
            }

            try
            {
                var clamp = clampField.GetValue(block);
                if (clamp != null)
                {
                    var clampActive = CardUtil.GetCachedField(clamp.GetType(), "Active");
                    if (clampActive == null)
                    {
                        WarnOnce("stat-field:clampActive",
                            $"AlwaysUpdateService: {clamp.GetType().Name}.Active not found (game update?) - cannot judge CT2 fixtures inert, forcing AlwaysUpdate as before.");
                        return true;
                    }
                    if (clampActive.GetValue(clamp) is true) return true;
                }
            }
            catch (Exception ex)
            {
                WarnOnce("stat-read:" + clampName,
                    $"AlwaysUpdateService: {type.Name}.{clampName} read threw (first on '{uid}'.{blockName}) - forcing AlwaysUpdate as before: {Log.ExceptionText(ex)}");
                return true;
            }
        }

        // A threshold action fires from the tick that crosses it, so an OnZero/OnFull block is
        // live even with a rate of 0 (the value can be driven by an action instead of a rate).
        foreach (var actionName in ThresholdActionFields)
        {
            var actionField = CardUtil.GetCachedField(type, actionName);
            if (actionField == null)
            {
                WarnOnce("stat-field:" + actionName,
                    $"AlwaysUpdateService: {type.Name}.{actionName} not found (game update?) - cannot judge CT2 fixtures inert, forcing AlwaysUpdate as before.");
                return true;
            }

            try
            {
                if (actionField.GetValue(block) is true) return true;
            }
            catch (Exception ex)
            {
                WarnOnce("stat-read:" + actionName,
                    $"AlwaysUpdateService: {type.Name}.{actionName} read threw (first on '{uid}'.{blockName}) - forcing AlwaysUpdate as before: {Log.ExceptionText(ex)}");
                return true;
            }
        }

        return false;
    }
}
