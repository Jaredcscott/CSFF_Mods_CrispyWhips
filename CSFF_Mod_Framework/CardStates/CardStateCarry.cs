using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CSFFModFramework.CardStates;

/// <summary>
/// Carries card values through the game's own transforms and through blueprints (the hooks of
/// <c>Documentation/Research/Fleet/APE_Port/W0.5_ingredient_memory.md</c>, as built: see its "As built" section).
///
/// <para><b>Game transforms.</b> <c>GameManager.ApplyCardStateChange</c> (<c>case Transform</c>) sets
/// <c>WillTransform</c>, starts <c>RemoveCard</c> on the old card, starts <c>AddCard</c> for the new one and only then
/// sets <c>old.TransformedInto</c> (<c>.decomp/GameManager.cs</c> 11853-12069, no yield in between). With no user and no
/// destroy drops, <c>RemoveCard</c> reaches <c>OnCardDestroyed</c> (10943) before its first yield, so the event sees the
/// old card while <c>TransformedInto</c> is still null. The old card's carried values are captured there and handed to
/// <c>TransformedInto</c> on a later frame (<see cref="PollUpdate"/>), which also covers a pooled old card reused as
/// the new one.</para>
///
/// <para><b>Blueprints.</b> A prefix on the first step of <c>InGameCardBase.PerformBlueprintBuild</c>'s compiled
/// <c>MoveNext</c> runs while the stage's ingredients still sit in the site's slots: each ingredient card (not a kept
/// tool: a <c>DontDestroy</c> element) gives its value of every name with a blueprint merge, or its seed durability,
/// and the merge with the site's own running value is computed. A postfix on the same step keeps the merge on the
/// site once the stage really built (its <c>BlueprintData</c> stage moved). On the final stage, cards spawned while
/// the build runs whose model is one of the blueprint's <c>BlueprintResult</c> cards receive the merged values.</para>
/// </summary>
internal static class CardStateCarry
{
    private sealed class PendingTransform
    {
        public InGameCardBase Old;
        public Dictionary<string, float> Values;
        public int Frame;
    }

    private sealed class Build
    {
        public object Key;
        public InGameCardBase Site;
        public BlueprintSaveData Data;
        public int StageBefore;
        public bool Final;
        public bool Committed;
        public Dictionary<string, (float value, int count, HashSet<string> onlyInto)> Merged;
        public HashSet<CardData> Results;
        public float OpenedAt;
        public int Applied;
    }

    private const int TransformWaitFrames = 300;
    private const float BuildWindowSeconds = 120f;

    private static readonly List<PendingTransform> _pending = new();
    private static readonly List<Build> _builds = new();

    internal static bool HasOpenBuilds => _builds.Count > 0;

    internal static void ResetRun()
    {
        _pending.Clear();
        _builds.Clear();
    }

    // ── Game transforms ──────────────────────────────────────────────────────

    /// <summary><c>GameManager.OnCardDestroyed</c>: capture the carried values of a card the game is transforming.</summary>
    internal static void OnCardDestroyed(InGameCardBase card)
    {
        try
        {
            if (!CardStateStore.AnyTransformCarry || !card || !card.WillTransform) return;
            long id = CardStateStore.ReadId(card);
            if (id == 0 || !CardStateStore.EnsureRun(out _)) return;
            var vals = CardStateStore.ValuesOf(id);
            if (vals == null) return;
            Dictionary<string, float> carried = null;
            foreach (var kv in vals)
            {
                if (CardStateCodec.IsCountName(kv.Key) || !CardStateStore.CarriedThroughTransform(kv.Key)) continue;
                (carried ??= new Dictionary<string, float>(StringComparer.Ordinal))[kv.Key] = kv.Value;
            }
            if (carried == null) return;
            _pending.Add(new PendingTransform { Old = card, Values = carried, Frame = Time.frameCount });
        }
        catch (Exception ex) { CardStateStore.WarnOnce("destroyed:" + Log.CauseKey(ex), $"CardState: capturing values of a transforming card threw: {Log.ExceptionText(ex)}"); }
    }

    /// <summary>Plugin.Update: hand captured values to the card each transform produced. Free when nothing waits.</summary>
    internal static void PollUpdate()
    {
        if (_pending.Count == 0) return;
        try
        {
            if (!CardStateStore.EnsureRun(out _)) { _pending.Clear(); return; }
            int now = Time.frameCount;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                var target = p.Old ? p.Old.TransformedInto : null;
                if (target && !target.Destroyed)
                {
                    long id = CardStateStore.EnsureId(target);
                    foreach (var kv in p.Values) CardStateStore.SetRaw(id, kv.Key, kv.Value);
                    _pending.RemoveAt(i);
                    Log.Debug($"CardState: {p.Values.Count} value(s) carried through a transform into '{target.CardModel?.UniqueID}'.");
                    continue;
                }
                if (now - p.Frame > TransformWaitFrames)
                {
                    _pending.RemoveAt(i);
                    Log.Debug($"CardState: a transform produced no card on the board within {TransformWaitFrames} frames (a background or event transform); its {p.Values.Count} value(s) are not carried.");
                }
            }
        }
        catch (Exception ex)
        {
            _pending.Clear();
            CardStateStore.WarnOnce("poll:" + Log.CauseKey(ex), $"CardState: carrying values through a transform threw: {Log.ExceptionText(ex)}");
        }
    }

    // ── Blueprints ───────────────────────────────────────────────────────────

    /// <summary>First step of a build, before the game's code runs: gather and merge this stage's values.</summary>
    internal static void BeginBuild(object key, InGameCardBase site)
    {
        try
        {
            if (!site || site.CardModel == null || site.BlueprintData == null) return;
            var stages = site.CardModel.BlueprintStages;
            int stage = site.BlueprintData.CurrentStage;
            if (stages == null || stage < 0 || stage >= stages.Length) return;
            if (!CardStateStore.EnsureRun(out _)) return;
            var elements = stages[stage].RequiredElements;

            // The stage's consumed ingredient cards, slot by slot (slot k is element k, as the game reads them).
            var ingredients = new List<InGameCardBase>();
            var slots = site.CardsInInventory;
            if (slots != null)
            {
                for (int k = 0; k < slots.Count; k++)
                {
                    var slot = slots[k];
                    if (slot == null || !slot.CardModel) continue;
                    if (elements != null && k < elements.Length && elements[k].DontDestroy) continue;
                    var cards = slot.AllCards;
                    if (cards == null) continue;
                    for (int l = 0; l < slot.CardAmt && l < cards.Count; l++)
                        if (cards[l]) ingredients.Add(cards[l]);
                }
            }

            long siteId = CardStateStore.ReadId(site);
            var siteVals = CardStateStore.ValuesOf(siteId);
            Dictionary<string, (float, int, HashSet<string>)> merged = null;
            var inputs = new List<float>();
            foreach (var (name, merge, onlyInto, seedStat, seedCards) in CardStateStore.BlueprintPolicies())
            {
                inputs.Clear();
                foreach (var c in ingredients)
                {
                    long id = CardStateStore.ReadId(c);
                    var vals = CardStateStore.ValuesOf(id);
                    if (vals != null && vals.TryGetValue(name, out var v)) { inputs.Add(v); continue; }
                    if (seedStat == null || seedCards == null || c.CardModel == null || !seedCards.Contains(c.CardModel.UniqueID)) continue;
                    float s = CardUtil.GetDurability(c, seedStat);
                    if (!float.IsNaN(s) && !float.IsInfinity(s)) inputs.Add(s);
                }
                bool hasSite = siteVals != null && siteVals.TryGetValue(name, out _);
                float siteValue = hasSite ? siteVals[name] : 0f;
                int siteCount = siteVals != null && siteVals.TryGetValue(CardStateCodec.CountName(name), out var cnt) ? (int)cnt : 1;
                if (!CardStateCodec.TryMerge(merge, hasSite, siteValue, siteCount, inputs, out float value, out int count)) continue;
                (merged ??= new Dictionary<string, (float, int, HashSet<string>)>(StringComparer.Ordinal))[name] = (value, count, onlyInto);
            }
            if (merged == null) return;

            var build = new Build
            {
                Key = key,
                Site = site,
                Data = site.BlueprintData,
                StageBefore = stage,
                Final = stage == stages.Length - 1,
                Merged = merged,
                OpenedAt = Time.realtimeSinceStartup,
            };
            if (build.Final)
            {
                build.Results = new HashSet<CardData>();
                var results = site.CardModel.BlueprintResult;
                if (results != null)
                    foreach (var r in results)
                        if (r.DroppedCard) build.Results.Add(r.DroppedCard);
            }
            ExpireBuilds();
            _builds.Add(build);
        }
        catch (Exception ex) { CardStateStore.WarnOnce("bp-begin:" + Log.CauseKey(ex), $"CardState: gathering a blueprint's ingredient values threw: {Log.ExceptionText(ex)}"); }
    }

    /// <summary>After each step of a build: keep a built stage's merge on the site; forget the build when it ends.</summary>
    internal static void StepBuild(object key, bool running)
    {
        try
        {
            for (int i = _builds.Count - 1; i >= 0; i--)
            {
                var b = _builds[i];
                if (!ReferenceEquals(b.Key, key)) continue;
                if (!b.Committed)
                {
                    b.Committed = true;
                    bool built = !b.Site || b.Site.BlueprintData != b.Data || b.Data.CurrentStage != b.StageBefore;
                    if (built && !b.Final && b.Site)
                    {
                        long siteId = CardStateStore.EnsureId(b.Site);
                        foreach (var kv in b.Merged)
                        {
                            CardStateStore.SetRaw(siteId, kv.Key, kv.Value.value);
                            CardStateStore.SetRaw(siteId, CardStateCodec.CountName(kv.Key), kv.Value.count);
                        }
                    }
                    if (!built) { _builds.RemoveAt(i); continue; }
                }
                if (!running)
                {
                    if (b.Final)
                        Log.Debug($"CardState: blueprint '{b.Site?.CardModel?.UniqueID}' finished; {b.Applied} result card(s) received {b.Merged.Count} value(s).");
                    _builds.RemoveAt(i);
                }
            }
        }
        catch (Exception ex)
        {
            _builds.Clear();
            CardStateStore.WarnOnce("bp-step:" + Log.CauseKey(ex), $"CardState: keeping a blueprint stage's values threw: {Log.ExceptionText(ex)}");
        }
    }

    /// <summary><c>SpawnService.CardSpawned</c>: a result card of a final stage in progress receives the merged values.</summary>
    internal static void OnCardSpawned(object card, string uid)
    {
        if (_builds.Count == 0 || card is not InGameCardBase c || !c || c.CardModel == null) return;
        try
        {
            for (int i = _builds.Count - 1; i >= 0; i--)
            {
                var b = _builds[i];
                // Spawns land inside the first step too (before StepBuild commits), so a final build is open from
                // BeginBuild on; an aborted one makes no spawn and is closed by its first StepBuild.
                if (!b.Final || b.Results == null || ReferenceEquals(c, b.Site)) continue;
                if (!b.Results.Contains(c.CardModel)) continue;
                long id = 0;
                foreach (var kv in b.Merged)
                {
                    if (kv.Value.onlyInto != null && !kv.Value.onlyInto.Contains(c.CardModel.UniqueID)) continue;
                    if (id == 0) id = CardStateStore.EnsureId(c);
                    CardStateStore.SetRaw(id, kv.Key, kv.Value.value);
                }
                if (id != 0) b.Applied++;
                return;   // the most recent build that makes this card takes it
            }
        }
        catch (Exception ex) { CardStateStore.WarnOnce("bp-spawn:" + Log.CauseKey(ex), $"CardState: giving a blueprint result its values threw: {Log.ExceptionText(ex)}"); }
    }

    // A build whose coroutine the game abandoned (a scene change, an exception in the game's own code) never reports
    // its end; drop it after a while so it can not stamp values on an unrelated later spawn of the same card.
    private static void ExpireBuilds()
    {
        float now = Time.realtimeSinceStartup;
        for (int i = _builds.Count - 1; i >= 0; i--)
            if (now - _builds[i].OpenedAt > BuildWindowSeconds) _builds.RemoveAt(i);
    }

    internal static void ExpireStaleBuilds()
    {
        if (_builds.Count > 0) ExpireBuilds();
    }
}
