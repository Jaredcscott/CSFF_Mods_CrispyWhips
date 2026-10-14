namespace Quick_Transfer.Patcher
{
    /// <summary>
    /// What the last batch actually moved (plan Feature 4). One record is kept, the newest, because Feature 10's
    /// undo reverses exactly one batch and a new batch replaces it. It holds the count of each item moved
    /// (UniqueID -> cards), the type of slot the click was in (the direction), the destination key read at the
    /// click (<see cref="BatchContext"/>, so a later reader can tell whether the same container is still open),
    /// the tag a Tag Filter batch matched, and how many cards the click asked for.
    /// </summary>
    /// <remarks>
    /// A batch stopped by a destination change still records what it moved; it only skips the end-of-batch notice.
    /// A batch replaced by a newer click records nothing, because the newer click cleared the record (<see cref="Clear"/>)
    /// and the undo reverses only the newest batch. Counts come from the coroutine's own progress check (the clicked
    /// pile shrank), never from the number requested.
    /// </remarks>
    internal sealed class BatchRecord
    {
        /// <summary>The newest batch's record, or null before any batch has ended.</summary>
        public static BatchRecord Last { get; private set; }

        /// <summary>The batch generation (<c>QuickTransferPatch.batchGeneration</c>) this record belongs to.</summary>
        public int Generation { get; }

        /// <summary>Cards the click asked for, counting the clicked card's own move; <see cref="Plugin.AllSentinel"/> for All.</summary>
        public int Requested { get; }

        /// <summary>True when the request was the whole stack (the same test <see cref="Plugin.AmountLabel"/> uses for "All").</summary>
        public bool RequestedAll => Requested >= Plugin.AllSentinel;

        /// <summary>Slot type name of the clicked card's slot ("Item", "Base", "Inventory", ...): which way the batch went.</summary>
        public string ClickedSlotType { get; }

        /// <summary>Where the first card went, read at the click.</summary>
        public BatchContext Context { get; }

        /// <summary>The tag a Tag Filter batch matched, or null for a same-item batch.</summary>
        public string FilterTag { get; }

        /// <summary>UniqueID of each item moved -> cards moved. A key of "?" holds cards whose UniqueID could not be read.</summary>
        public IReadOnlyDictionary<string, int> MovedByUid => movedByUid;

        /// <summary>Every card the batch moved, including the one the game's own click moved.</summary>
        public int Moved { get; }

        private readonly Dictionary<string, int> movedByUid;

        public BatchRecord(int generation, int requested, string clickedSlotType, BatchContext context, string filterTag,
                           Dictionary<string, int> movedByUid)
        {
            Generation = generation;
            Requested = requested;
            ClickedSlotType = clickedSlotType;
            Context = context;
            FilterTag = filterTag;
            this.movedByUid = new Dictionary<string, int>(movedByUid, StringComparer.Ordinal);
            int total = 0;
            foreach (var n in this.movedByUid.Values) total += n;
            Moved = total;
        }

        /// <summary>Counts one moved card of <paramref name="uid"/> into <paramref name="movedByUid"/>.</summary>
        public static void Count(Dictionary<string, int> movedByUid, string uid)
        {
            var key = string.IsNullOrEmpty(uid) ? "?" : uid;
            movedByUid.TryGetValue(key, out var n);
            movedByUid[key] = n + 1;
        }

        // Records of a generation below this are refused (see Clear): a batch that a newer click replaced, or that
        // an undo replaced, ends after the clear and must not bring back a record the player can undo.
        private static int floorGeneration;

        /// <summary>
        /// Keeps <paramref name="record"/> as the last batch unless a newer batch's record is already held. A batch
        /// replaced by a newer click ends after that click armed; if the newer batch's coroutine happens to end
        /// first, the generation test keeps its record instead of letting the older batch overwrite it.
        /// </summary>
        public static void Store(BatchRecord record)
        {
            if (record.Generation < floorGeneration) return;
            if (Last == null || record.Generation >= Last.Generation) Last = record;
        }

        /// <summary>
        /// Forgets the last batch (plan Feature 10: one level of undo, cleared by any new batch, by a context change
        /// and by the undo itself), and refuses any later record older than <paramref name="fromGeneration"/>.
        /// </summary>
        public static void Clear(int fromGeneration)
        {
            Last = null;
            if (fromGeneration > floorGeneration) floorGeneration = fromGeneration;
        }
    }
}
