using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Enables AlwaysUpdate ticking on all mod cards so they process
/// durability decay, spoilage, stat changes, etc.
///
/// Previously only H&amp;F had this; now the framework does it for all mods.
/// </summary>
internal static class AlwaysUpdateService
{
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
        // "follow" the player) instead of saving+removing them like a vanilla env node — which for a
        // location card produces an orphan on the destination board whose travel DA loops back to the
        // current env (travel softlock). Vanilla env/explorable cards ship AlwaysUpdate=false; mod
        // ones must match. Env nodes also have no durability/spoilage that needs per-tick processing.
        // (WorldMap clone nodes are fixed at clone time in CardCloneService — they don't exist yet at
        // this phase; this skip covers mod-shipped CT4/CT8 cards such as the framework hub.)
        var cardTypeField = AccessTools.Field(typeof(CardData), "CardType");

        // For all OTHER mod cards we always want AlwaysUpdate = true.
        int updated = 0, skippedEnvNodes = 0, corrected = 0;
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
                // for CLONED env nodes at clone time (see its comment ~182-194) — this closes the
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
                             "— a mod-authored env/explorable node must not follow the player between environments (travel softlock risk).");
                }
                continue;
            }

            alwaysUpdateField.SetValue(card, true);
            updated++;
        }

        if (updated > 0 || skippedEnvNodes > 0)
            Log.Debug($"AlwaysUpdateService: enabled ticking on {updated} mod cards " +
                      $"({skippedEnvNodes} env/explorable node(s) skipped, {corrected} corrected from AlwaysUpdate=true)");
    }

    // CardTypes enum (EA 0.64f): Environment=4, Explorable=8 — see Documentation/CSFF_Reference.md.
    private static bool IsEnvNode(FieldInfo cardTypeField, CardData card)
    {
        if (cardTypeField == null) return false;
        try
        {
            int ct = Convert.ToInt32(cardTypeField.GetValue(card));
            return ct == 4 || ct == 8;
        }
        catch (Exception ex) { Log.Debug($"AlwaysUpdateService: CardType read threw for '{card?.UniqueID}': {Log.ExceptionText(ex)}"); return false; }
    }
}
