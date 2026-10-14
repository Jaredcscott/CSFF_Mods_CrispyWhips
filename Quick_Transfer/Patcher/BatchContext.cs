using CSFFModFramework.Api;

namespace Quick_Transfer.Patcher
{
    /// <summary>
    /// The live state vanilla <c>InGameCardBase.SwapCard</c> reads to pick where an Item-row card goes:
    /// the inspected card (<c>GraphicsManager.InspectedCard</c>, the top of its inspection queue), whether
    /// the character window is active and whether the NPC trading window is active. With a card inspected
    /// and the trading window up the card goes to Trading, with a card inspected that is not a location or
    /// an NPC it goes into that card's inventory, with nothing inspected it goes to Equipment while the
    /// character window is up, and otherwise to the board. A batch re-fires that click once a frame, so
    /// each card is routed by whatever this state is on ITS frame (plan Feature 3).
    /// </summary>
    /// <remarks>
    /// <c>HasPopup</c> is deliberately not in the key: with nothing inspected the only outcomes are the
    /// board, Equipment or no destination, and no destination ends a batch through the coroutine's
    /// 3-strike no-progress stop (which, until this feature, could never fire: see
    /// <c>QuickTransferPatch.TransferCardsCoroutine</c>). All three fields held still through every frame
    /// of a normal batch and of a refused one (plan Wave 0 W0.1, KEEP-ALL). Every game read goes
    /// through <see cref="Reflect"/> string lookups. A read that
    /// fails makes the context "unreadable", which never stops a batch (fail open: the batch behaves as
    /// it did before this guard existed) and says so once per cause. Unity objects compare with
    /// <see cref="object.ReferenceEquals"/> plus <see cref="IsAlive"/>.
    /// </remarks>
    internal sealed class BatchContext
    {
        internal const string Feature = "Batch destination guard";

        // GraphicsManager is one scene singleton; kept so a running batch can re-read the same object.
        private readonly object graphics;

        /// <summary>False when a member could not be read; such a context never stops a batch.</summary>
        public bool Readable { get; }

        /// <summary>The inspected card (an <c>InGameCardBase</c>), or null when nothing is inspected.</summary>
        public object InspectedCard { get; }

        /// <summary>False when nothing is inspected, or when the inspected card has been destroyed.</summary>
        public bool InspectedAlive { get; }

        /// <summary>
        /// <c>CardModel.UniqueID</c> of the inspected card (the open container, or the NPC), or null when
        /// nothing is inspected. Plan Features 10 to 12 key their per-container state on it.
        /// </summary>
        public string InspectedUniqueId { get; }

        public bool CharacterWindowActive { get; }
        public bool TradingWindowActive { get; }

        private BatchContext(object graphics, bool readable, object inspected, bool inspectedAlive, string inspectedUid,
                             bool characterWindow, bool tradingWindow)
        {
            this.graphics = graphics;
            Readable = readable;
            InspectedCard = inspected;
            InspectedAlive = inspectedAlive;
            InspectedUniqueId = inspectedUid;
            CharacterWindowActive = characterWindow;
            TradingWindowActive = tradingWindow;
        }

        private static BatchContext Unreadable(object graphics, string cause)
        {
            QuickTransferPatch.WarnFeatureOnce(Feature, cause);
            return new BatchContext(graphics, false, null, false, null, false, false);
        }

        /// <summary>Reads the context through the clicked card's own <c>GraphicsM</c> (protected on <c>InGameCardBase</c>).</summary>
        public static BatchContext Read(object card)
        {
            if (card == null || !Reflect.TryGetMember(card, "GraphicsM", out var gm))
                return Unreadable(null, "InGameCardBase.GraphicsM");
            return ReadFrom(gm);
        }

        /// <summary>The same context read again now, from the same GraphicsManager.</summary>
        public BatchContext ReadAgain() => graphics == null ? Unreadable(null, "InGameCardBase.GraphicsM") : ReadFrom(graphics);

        private static BatchContext ReadFrom(object gm)
        {
            if (!IsAlive(gm)) return Unreadable(null, "InGameCardBase.GraphicsM (null)");

            if (!Reflect.TryGetMember(gm, "InspectedCard", out var inspected))
                return Unreadable(gm, "GraphicsManager.InspectedCard");
            bool alive = IsAlive(inspected);
            string uid = null;
            if (alive)
            {
                if (!Reflect.TryGetMember(inspected, "CardModel", out var model))
                    return Unreadable(gm, "InGameCardBase.CardModel");
                if (IsAlive(model))
                {
                    if (!Reflect.TryGetMember(model, "UniqueID", out var uidObj))
                        return Unreadable(gm, "CardData.UniqueID");
                    uid = uidObj?.ToString();
                }
            }

            if (!Reflect.TryGetMember(gm, "CharacterWindow", out var characterWindow))
                return Unreadable(gm, "GraphicsManager.CharacterWindow");
            if (!Reflect.TryGetMember(gm, "NPCPopup", out var npcPopup))
                return Unreadable(gm, "GraphicsManager.NPCPopup");
            object tradingWindow = null;
            if (IsAlive(npcPopup) && !Reflect.TryGetMember(npcPopup, "TradingWindow", out tradingWindow))
                return Unreadable(gm, "NPCInspectionPopup.TradingWindow");

            return new BatchContext(gm, true, inspected, alive, uid, ActiveInHierarchy(characterWindow), ActiveInHierarchy(tradingWindow));
        }

        // The framework's Reflect.IsAlive, written out here because this mod compiles against a framework
        // build that predates it: non-null and, for a Unity object held as object, not destroyed (a
        // destroyed UnityEngine.Object is not C#-null under an object-typed compare).
        internal static bool IsAlive(object instance)
            => instance != null && !(instance is UnityEngine.Object uo && uo == null);

        // SwapCard reads gameObject.activeInHierarchy on both windows. A missing or destroyed window reads
        // as inactive (the same for the whole batch, so it can never flicker into a stop).
        private static bool ActiveInHierarchy(object component)
            => component is Component c && c != null && c.gameObject.activeInHierarchy;

        /// <summary>
        /// Names the first field of the destination key that differs between this context and
        /// <paramref name="later"/>, or null when they match or either side is unreadable. Unity objects
        /// compare by reference AND by liveness: a destroyed inspected card is the same C# reference but
        /// fails SwapCard's own <c>(bool)GraphicsM.InspectedCard</c> test, so the destination moves.
        /// </summary>
        public string ChangedFrom(BatchContext later)
        {
            if (later == null || !Readable || !later.Readable) return null;
            if (!ReferenceEquals(InspectedCard, later.InspectedCard) || InspectedAlive != later.InspectedAlive)
                return "inspected card";
            if (!string.Equals(InspectedUniqueId, later.InspectedUniqueId, StringComparison.Ordinal))
                return "inspected card's UniqueID";
            if (CharacterWindowActive != later.CharacterWindowActive) return "character window";
            if (TradingWindowActive != later.TradingWindowActive) return "trading window";
            return null;
        }

    }
}
