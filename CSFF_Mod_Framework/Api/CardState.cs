using CSFFModFramework.CardStates;

namespace CSFFModFramework.Api;

/// <summary>How a value is combined when ingredients carry it into a blueprint's result (<see cref="CardState.DeclareCarry"/>).</summary>
public enum CardStateMerge
{
    /// <summary>Not carried through blueprints (the default for every name).</summary>
    None = 0,
    /// <summary>The first value met: the one a remembered earlier stage kept, else the first ingredient's.</summary>
    First = 1,
    /// <summary>The smallest ingredient value.</summary>
    Min = 2,
    /// <summary>The largest ingredient value.</summary>
    Max = 3,
    /// <summary>The ingredient values added together.</summary>
    Sum = 4,
    /// <summary>The mean over every ingredient that had a value, across all stages of the blueprint.</summary>
    Average = 5,
}

/// <summary>
/// Named numbers on one card in the game (one Metal Sheet, not every Metal Sheet), kept in the save
/// (framework 2.29.0, fleet plan 6.3, owner decision K5). A mod keeps a value the card's eight durabilities have no
/// room for: where its metal came from, how it was heated, what it was made from. Compare <see cref="ExtraData"/>,
/// which is per card TYPE and read-only.
///
/// <para><b>Use.</b> <c>CardState.Set(card, "mymod.origin", 2f)</c>, later <c>CardState.Get(card, "mymod.origin")</c>
/// (<see cref="float.NaN"/> when the card has no such value), <see cref="Has"/>, <see cref="Remove"/>. <c>card</c> is
/// the in-game card (<c>InGameCardBase</c>). Prefix every name with your mod's own prefix; names starting
/// <c>csff.cs.</c> are the framework's. Values are whole floats; NaN and infinity are refused. Call from a run (an
/// action handler, a tick, <c>GameManager.OnGMInitialized</c> or later); with no run in progress there is no card
/// to hold a value, and <see cref="Set"/> returns false.</para>
///
/// <para><b>Saved.</b> Each card that holds a value gets a random 64-bit id in an entry of its own
/// <c>DroppedCollections</c> named <c>csff.cs.id</c>, which the game saves, reloads and otherwise ignores. The values
/// are written beside each save file, one file per checkpoint (<c>Games/Game_N/CSFFModFramework/AutoSave.cardstate</c>
/// beside <c>Games/Game_N/AutoSave.json</c>), and read back when that save is loaded. Every save keeps only the
/// ids that save still holds, so a card that leaves the game takes its values with it. Two save slots never share
/// values (each slot has its own folder, and the ids are random). Loading a LastWeek checkpoint loads the values of
/// that checkpoint. A save made without the framework has no ids and simply starts empty; a sidecar that is missing
/// while the save still holds ids, or that cannot be read, also starts empty, with one warning in the log.</para>
///
/// <para><b>Carried (opt-in, per name).</b> <see cref="DeclareCarry"/> says whether a name survives a transform
/// (<c>CardUtil.TransformCardInPlace</c>, and the game's own transforms: cooking, an ingredient's
/// <c>EffectOnIngredient</c>, a stat-triggered change) and how ingredients pass it to a blueprint's result
/// (<see cref="CardStateMerge"/>). Undeclared names stay on the card they were set on and are dropped by a
/// transform. <see cref="DeclareBlueprintSeed"/> lets an ingredient with no value contribute one of its own
/// durabilities instead.</para>
///
/// <para><b>Not carried</b> (by design or by the game): a transform the game runs in the background on a card away from
/// the player (it makes save data, not a card); a liquid poured from one container into another; ingredients a
/// blueprint gives back (they come back as new cards); a stage the player undoes keeps the values its ingredients
/// passed to the site.</para>
/// </summary>
public static class CardState
{
    /// <summary>The value <paramref name="name"/> on <paramref name="card"/>, or <see cref="float.NaN"/> when it has none.</summary>
    public static float Get(object card, string name) => CardStateStore.Get(card, name, float.NaN);

    /// <summary>The value <paramref name="name"/> on <paramref name="card"/>, or <paramref name="fallback"/> when it has none.</summary>
    public static float Get(object card, string name, float fallback) => CardStateStore.Get(card, name, fallback);

    /// <summary>
    /// Sets <paramref name="name"/> on <paramref name="card"/>. Returns false, with one warning per cause, for an
    /// invalid name, a NaN or infinite value, something that is not a live in-game card, or no run in progress.
    /// </summary>
    public static bool Set(object card, string name, float value) => CardStateStore.Set(card, name, value);

    /// <summary>True when <paramref name="card"/> holds <paramref name="name"/>.</summary>
    public static bool Has(object card, string name) => CardStateStore.Has(card, name);

    /// <summary>Removes <paramref name="name"/> from <paramref name="card"/>; true if it was there.</summary>
    public static bool Remove(object card, string name) => CardStateStore.Remove(card, name);

    /// <summary>The names <paramref name="card"/> holds, sorted (empty when none).</summary>
    public static IReadOnlyList<string> Names(object card) => CardStateStore.Names(card);

    /// <summary>
    /// Declares how <paramref name="name"/> travels. <paramref name="throughTransform"/>: kept when the card turns
    /// into another card. <paramref name="throughBlueprint"/>: how a blueprint's ingredients combine it into the
    /// result (each ingredient card that holds the value counts once; tools and other kept cards do not count;
    /// across several stages the site keeps the running value in the save). <paramref name="onlyIntoResults"/>
    /// (UniqueIDs, optional) limits the blueprint carry to those results. Idempotent: declaring a name again
    /// replaces its policy, so calling this from a per-run event is safe. Returns false for an invalid name.
    /// </summary>
    public static bool DeclareCarry(string name, bool throughTransform, CardStateMerge throughBlueprint, params string[] onlyIntoResults)
        => CardStateStore.DeclareCarry(name, throughTransform, throughBlueprint, onlyIntoResults);

    /// <summary>
    /// For a name declared with a blueprint merge: an ingredient that holds no value of <paramref name="name"/> but
    /// is one of <paramref name="ingredientUniqueIds"/> contributes its own <paramref name="durabilityStat"/>
    /// (a JSON stat name such as <c>SpecialDurability3</c>) instead. So a value that exists today as a durability on
    /// the parts can live on as a card value of the product, whose own durabilities mean something else.
    /// Idempotent like <see cref="DeclareCarry"/>. Returns false for an invalid name, an empty stat or no UniqueID.
    /// </summary>
    public static bool DeclareBlueprintSeed(string name, string durabilityStat, params string[] ingredientUniqueIds)
        => CardStateStore.DeclareBlueprintSeed(name, durabilityStat, ingredientUniqueIds);
}
