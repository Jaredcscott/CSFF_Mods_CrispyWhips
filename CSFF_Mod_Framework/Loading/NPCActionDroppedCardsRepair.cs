using CSFFModFramework.Data;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Backfills null <c>NPCAction.DroppedCards</c> (<c>CardsDropCollection[]</c>) to an empty array on
/// every mod-loaded <see cref="NPCAgent"/>, and (defensively) rebuilds a populated one from the raw
/// parsed JSON if it is ever found null despite a JSON block being present.
///
/// <para><b>Primary purpose — prevent an <c>NPCAction.ToAction()</c> NRE.</b> An NPCAgent action
/// authored with NO <c>DroppedCards</c> JSON block leaves the field hard <c>null</c> after
/// <c>JsonUtility.FromJsonOverwrite</c> (normal behavior — absent keys keep their default, and
/// <c>NPCAction.DroppedCards</c> has no field initializer). <c>NPCAction.ToAction()</c>
/// (<c>.decomp/NPCAction.cs:85</c>) then iterates <c>for (int i = 0; i &lt; DroppedCards.Length; i++)</c>
/// with zero null-guard → NRE. The crash is nastiest because it can fire <b>synchronously in the
/// middle of a different action</b>: when an action's <c>NPCStatModifications</c> decrement a stat,
/// <c>GameManager.ActionRoutine</c> starts and awaits <c>InGameNPC.CheckForActions()</c> before its
/// own produce step, so a sibling stat-gated action (e.g. an <c>ExistsZero → Spirit World</c> move
/// with no drops) fires and NREs, aborting the whole chain before the triggering action's own
/// carcass/drop ever spawns.</para>
///
/// <para><b>Confirmed EA 0.65h, 2026-07-11 (Sirus23 Wild Owl).</b> The load log read
/// <c>rebuilt 0 ... backfilled 2 empty</c>: the ONE owl action that ships a <c>DroppedCards</c>
/// block (<c>WildOwl_BloodZero_Death</c>, the carcass) was already correctly populated by JsonUtility
/// (rebuild never triggered — two-level class-array nesting does NOT defeat the deserializer here),
/// while the two genuinely-empty actions (<c>WildOwl_TrappedDeath</c>,
/// <c>WildOwl_ExistsZero_SpiritWorld</c>) were null and got backfilled.</para>
///
/// <para><b>This repair alone did NOT fully fix "owl drops no carcass on death" — correction,
/// 2026-07-11 later same day.</b> It stops the NRE, but <c>WildOwl_BloodZero_Death</c>'s own
/// <c>NPCStatModifications</c> (decrementing <c>wildowl_stat_exists</c> to 0) is what triggers the
/// nested <c>CheckForActions()</c> in the first place (<c>GameManager.cs:4149</c>, awaited before
/// <c>GameManager.cs:4265+</c>'s produce step). With the NRE gone, that nested call now successfully
/// runs <c>ExistsZero_SpiritWorld</c> (<c>MoveTiming: MoveBeforeOtherEffects</c>), which relocates the
/// owl's <c>AssociatedCard</c> to the Spirit World env <b>before</b> control returns to
/// <c>BloodZero_Death</c>'s own produce step. <c>CardsDropCollection.FillDropList</c> gates all drops
/// on <c>_FromCard.CardEnvironment.MatchesPlayerEnv</c> — now false — so the carcass drop is silently
/// skipped (no crash, no log line, just nothing). Actual fix: removed the <c>NPCStatModifications</c>
/// block from <c>WildOwl_BloodZero_Death</c> itself (<c>NPCAgent/Agent_WildOwl.json</c>) — it already
/// carries its own <c>MoveToEnvironmentWarpData</c> + <c>MoveTiming: MoveAfterOtherEffects</c>, so the
/// owl still ends up in Spirit World, just after its own carcass has dropped instead of before. This
/// repair (the null-guard) is still required — it prevents the same crash from <c>WildOwl_TrappedDeath</c>,
/// which independently decrements the same stat and has no drops of its own to lose.</para>
///
/// <para>Runs right after <see cref="JsonDataLoader.LoadAll"/> (before WarpResolver) so the
/// defensive rebuild's <c>DroppedCardWarpData</c> targets are already registered for the
/// <see cref="GameRegistry"/> lookups below.</para>
/// </summary>
internal static class NPCActionDroppedCardsRepair
{
    private static readonly FieldInfo _cardDropField = AccessTools.Field(typeof(CardsDropCollection), "DroppedCards");

    public static void RepairAll()
    {
        int actionsRebuilt = 0, actionsBackfilled = 0, collectionsBuilt = 0;

        foreach (var obj in JsonDataLoader.LoadedObjectsByUniqueId.Values)
        {
            if (obj is not NPCAgent agent || agent.AgentActions == null || agent.AgentActions.Length == 0)
                continue;

            JsonDataLoader.ParsedJsonByUniqueId.TryGetValue(agent.UniqueID, out var root);
            var actionList = root != null && root.TryGetValue("AgentActions", out var rawActions)
                ? rawActions as List<object>
                : null;

            for (int i = 0; i < agent.AgentActions.Length; i++)
            {
                var action = agent.AgentActions[i];
                if (action == null || action.DroppedCards != null) continue;

                var actionDict = (actionList != null && i < actionList.Count) ? actionList[i] as Dictionary<string, object> : null;
                var dropList = (actionDict != null && actionDict.TryGetValue("DroppedCards", out var rawDrops))
                    ? rawDrops as List<object>
                    : null;

                if (dropList == null || dropList.Count == 0)
                {
                    action.DroppedCards = Array.Empty<CardsDropCollection>();
                    actionsBackfilled++;
                    continue;
                }

                var built = new CardsDropCollection[dropList.Count];
                for (int c = 0; c < dropList.Count; c++)
                {
                    built[c] = BuildCollection(dropList[c] as Dictionary<string, object>, agent.UniqueID, action.ActionID);
                    collectionsBuilt++;
                }
                action.DroppedCards = built;
                actionsRebuilt++;
            }
        }

        if (actionsRebuilt > 0 || actionsBackfilled > 0)
            Log.Info($"NPCActionDroppedCardsRepair: rebuilt {collectionsBuilt} drop collection(s) across {actionsRebuilt} NPCAction(s), backfilled {actionsBackfilled} empty (prevents NPCAction.ToAction NRE on actions with no DroppedCards block)");
    }

    private static CardsDropCollection BuildCollection(Dictionary<string, object> dict, string agentUid, string actionId)
    {
        var collection = new CardsDropCollection();
        if (dict == null) return collection;

        if (dict.TryGetValue("CollectionName", out var name) && name is string s)
            collection.CollectionName = s;
        if (dict.TryGetValue("CollectionWeight", out var w))
            collection.CollectionWeight = ToInt(w);

        if (dict.TryGetValue("DroppedCards", out var rawCards) && rawCards is List<object> cardList)
        {
            var drops = new CardDrop[cardList.Count];
            for (int i = 0; i < cardList.Count; i++)
                drops[i] = cardList[i] is Dictionary<string, object> cardDict
                    ? BuildDrop(cardDict, agentUid, actionId)
                    : default;

            if (_cardDropField != null)
                _cardDropField.SetValue(collection, drops);
            else
                Log.Error("NPCActionDroppedCardsRepair: CardsDropCollection.DroppedCards field not found — drop rebuild unavailable");
        }

        return collection;
    }

    private static CardDrop BuildDrop(Dictionary<string, object> dict, string agentUid, string actionId)
    {
        CardData card = null;
        if (dict.TryGetValue("DroppedCardWarpData", out var uidVal) && uidVal is string uid)
        {
            card = GameRegistry.GetByUid<CardData>(uid);
            if (card == null)
                Log.Warn($"NPCActionDroppedCardsRepair: '{agentUid}' action '{actionId}' — DroppedCardWarpData '{uid}' not found");
        }

        int qx = 1, qy = 1;
        if (dict.TryGetValue("Quantity", out var qv) && qv is Dictionary<string, object> qd)
        {
            if (qd.TryGetValue("x", out var xv)) qx = ToInt(xv);
            if (qd.TryGetValue("y", out var yv)) qy = ToInt(yv);
        }

        return new CardDrop(card, new Vector2Int(qx, qy));
    }

    private static int ToInt(object v) => v switch
    {
        double d => (int)d,
        long l => (int)l,
        int i => i,
        _ => 0,
    };
}
