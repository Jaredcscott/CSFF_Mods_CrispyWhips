using System;
using System.Collections.Generic;
using CSFFModFramework.Api;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The reason a button is greyed out: a localization key, its English text and the values it shows.
    ///
    /// <para>Vanilla's <c>DismantleActionButton</c> reads <c>CardAction.ActionBlockedMessage</c> through
    /// <c>GameManager.CheckActionBlockers</c> and puts it in the button's tooltip. CMC used to write
    /// hard-coded English there (the Inn's "You already have a room", its balance line, the Academy's
    /// three study refusals and the jail warden's price), so a Chinese player read English: none of
    /// those sentences had a row in <c>Localization/SimpEn.csv</c> or <c>SimpCn.csv</c> (inn W2,
    /// 2026-11-12). Every such write now goes through this type, which is the ONLY place in the mod
    /// that names the game member, and which always resolves the text through the framework's
    /// <c>CSFFModFramework.Api.Localized</c> (current language, English when the key has no row).</para>
    ///
    /// <para>The English text is the fallback AND the CSV row's English column: keep the two equal.
    /// A message with values uses <c>{0}</c>, <c>{1}</c> placeholders, and its Chinese row must carry
    /// the same ones (a malformed template falls back to English with one framework warning).
    /// Gate: <c>Development_Tools/Tests/CMC-CodeWarningSweep-2026-11.Tests.ps1</c>.</para>
    /// </summary>
    internal readonly struct BlockedMessage
    {
        private const string GameMember = "ActionBlockedMessage";
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Localization key, <c>CMC_&lt;Area&gt;_&lt;Thing&gt;_BlockedMessage</c>. Null on <c>default</c>.</summary>
        internal readonly string Key;

        /// <summary>English text: the fallback, and what the log prints.</summary>
        internal readonly string English;

        private readonly object[] _args;

        internal BlockedMessage(string key, string english, params object[] args)
        {
            Key = key;
            English = english;
            _args = args;
        }

        /// <summary>True for <c>default(BlockedMessage)</c>: nothing blocks the action.</summary>
        internal bool IsNone => Key == null;

        private bool HasArgs => _args != null && _args.Length > 0;

        /// <summary>The text in the player's language.</summary>
        internal string Text => IsNone ? null
            : HasArgs ? Localized.Format(Key, English, _args)
            : Localized.Text(Key, English);

        /// <summary>The English text with its values filled in, for a log line.</summary>
        internal string EnglishText => IsNone ? null : HasArgs ? string.Format(English, _args) : English;

        /// <summary>True when the action already carries a blocked message this pass (do not clobber it).</summary>
        internal static bool AlreadySetOn(object cardAction) =>
            Reflect.GetMember(cardAction, GameMember) is string existing && !string.IsNullOrEmpty(existing);

        /// <summary>Writes this message onto the action. A refused write (the member was renamed by a game
        /// update) leaves the button without its tooltip reason and warns once; the Cancel gates still refuse
        /// the action itself.</summary>
        internal void WriteTo(object cardAction)
        {
            if (IsNone || cardAction == null) return;
            if (Reflect.SetMember(cardAction, GameMember, Text)) return;
            if (_warned.Add("write-refused"))
                Plugin.Logger.LogWarning("[BlockedMessage] CardAction." + GameMember + " could not be written (member missing or read-only after a game update?): greyed-out buttons show no reason. First key: " + Key + ". Logged once.");
        }
    }
}
