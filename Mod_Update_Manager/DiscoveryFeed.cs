// Host-free: no BepInEx or UnityEngine reference, and nothing here reads or writes a file (the New and
// updated list is held in memory only). Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles
// this file (with NexusPublicQuery and KnownModRegistry, which it reads through) and drives it.
using System;
using System.Collections.Generic;
using System.Text;

namespace mod_update_manager
{
    /// <summary>
    /// What the New and updated filter shows from one answer, and the counts behind its header line:
    /// every mod the answer listed (up to the size asked for) is exactly one of shown, already
    /// installed or mapped, or adult or not rated, so Shown + AlreadyHave + AdultOrUnrated == Listed.
    /// </summary>
    public sealed class DiscoveryFeedView
    {
        public readonly List<NexusPublicMod> Rows = new List<NexusPublicMod>();
        /// <summary>The mods of the answer looked at: the answer's own first Size of them, newest update first.</summary>
        public int Listed;
        /// <summary>Left out: the player has the mod installed or mapped, or it is Mod Update Manager itself.</summary>
        public int AlreadyHave;
        /// <summary>Left out: Nexus does not say the mod is not adult content (adultContent true, null or absent).</summary>
        public int AdultOrUnrated;

        public int Shown { get { return Rows.Count; } }
    }

    /// <summary>
    /// The rules of the New and updated filter on My Mods (plan feature 16), behind the existing
    /// EnableNexusDiscovery opt-in: how big its one request is, when that request may go out, which listed
    /// mods the filter shows, and how a row's summary is cut to one line. The request itself is
    /// NexusPublicQuery's and NexusPublicClient's; what draws the rows is the UI's.
    ///
    /// The list is one request for the newest releases and is never sent at startup or by a scheduled
    /// check: it goes out when the player first opens the filter in a game session, and again only when
    /// they press its Refresh button. Nothing is downloaded, and nothing here touches a file.
    /// </summary>
    public static class DiscoveryFeedPolicy
    {
        /// <summary>The DiscoveryFeedSize setting's default. A starting value, not a measured one.</summary>
        public const int DefaultSize = 10;
        public const int MinSize = 1;
        public const int MaxSize = 50;

        /// <summary>The longest summary line a row shows, the same cut the My Mods rows make.</summary>
        public const int SummaryMaxChars = 120;

        /// <summary>
        /// The size one list request asks for and the filter shows at most: what was asked, held to
        /// MinSize..MaxSize so a hand-edited setting cannot ask for an unbounded list.
        /// </summary>
        public static int ClampSize(int requested)
        {
            return Math.Min(MaxSize, Math.Max(MinSize, requested));
        }

        /// <summary>True when the list request may go out right now: discovery is on, a check may run (a key,
        /// or checks without a key on), and no list request is already in flight.</summary>
        public static bool MaySend(bool discoveryOn, bool canCheck, bool inFlight)
        {
            return discoveryOn && canCheck && !inFlight;
        }

        /// <summary>True when opening the filter sends the request: it may go out, and none has gone out yet
        /// in this game session (a failed one is retried only by Refresh, never by drawing the filter again).</summary>
        public static bool ShouldSendOnOpen(bool discoveryOn, bool canCheck, bool requestedThisSession, bool inFlight)
        {
            return MaySend(discoveryOn, canCheck, inFlight) && !requestedThisSession;
        }

        /// <summary>
        /// The rows to show from an answer. Only the answer's first ClampSize(size) mods are looked at, in the
        /// order Nexus gave them; of those a mod whose Nexus id is in ownedIds (installed or mapped), Mod
        /// Update Manager's own page, and any mod whose adultContent is not false (true, null or absent) are
        /// left out and counted. A failed answer shows nothing.
        /// </summary>
        public static DiscoveryFeedView Select(NexusPublicAnswer answer, IEnumerable<string> ownedIds, int size)
        {
            var view = new DiscoveryFeedView();
            if (answer == null || !answer.Ok) return view;

            int cap = ClampSize(size);
            var owned = new HashSet<int>();
            if (NexusPublicQuery.TryReadId(KnownModRegistry.SelfNexusModId, out int selfId)) owned.Add(selfId);
            if (ownedIds != null)
            {
                foreach (var raw in ownedIds)
                    if (NexusPublicQuery.TryReadId(raw, out int id)) owned.Add(id);
            }

            foreach (var mod in answer.Mods)
            {
                if (view.Listed >= cap) break;
                view.Listed++;
                if (owned.Contains(mod.ModId)) { view.AlreadyHave++; continue; }
                if (mod.AdultContent != false) { view.AdultOrUnrated++; continue; }
                view.Rows.Add(mod);
            }
            return view;
        }

        /// <summary>The address a row's Open Nexus Page button hands to the browser: the mod's page, built as
        /// every other Open Nexus Page button builds it (KnownModRegistry.GetNexusUrl) from the listed id.</summary>
        public static string PageUrl(NexusPublicMod mod)
        {
            return KnownModRegistry.GetNexusUrl(mod.ModId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>The name a row shows: the listed name trimmed, or #id when Nexus gave none.</summary>
        public static string DisplayName(NexusPublicMod mod)
        {
            return string.IsNullOrWhiteSpace(mod.Name) ? "#" + mod.ModId.ToString(System.Globalization.CultureInfo.InvariantCulture) : mod.Name.Trim();
        }

        /// <summary>
        /// A summary as one line: every run of whitespace or control characters (a line break, a tab) becomes
        /// one space, the ends are trimmed, and a line longer than maxChars is cut to maxChars with a
        /// trailing "..." (never between the two halves of a surrogate pair). A maxChars of zero or less
        /// cuts nothing; null reads as empty.
        /// </summary>
        public static string OneLine(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            bool pendingSpace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) { pendingSpace = sb.Length > 0; continue; }
                if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
                sb.Append(c);
            }
            var line = sb.ToString();
            if (maxChars <= 0 || line.Length <= maxChars) return line;
            int keep = Math.Max(0, maxChars - 3);
            if (keep > 0 && char.IsHighSurrogate(line[keep - 1])) keep--;
            return line.Substring(0, keep) + "...";
        }
    }
}
