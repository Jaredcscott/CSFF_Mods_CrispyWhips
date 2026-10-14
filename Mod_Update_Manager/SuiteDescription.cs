// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
// compiles this file (with SimpleJson and JsonText, which it reads through) and drives it.
namespace mod_update_manager
{
    /// <summary>
    /// The text under a suite row's "?" button (round 2, plan feature 9). It is the bundled mod's own
    /// ModInfo.json Description, read out of the embedded ZIP, so it is right after every repack; the
    /// hand-typed line in SuiteModRegistry is only the fallback for a bundled copy that has none.
    ///
    /// The text is decoded by SimpleJson, which reads every JSON escape (a \" or a \uXXXX standing for a
    /// curly apostrophe) through JsonText.Unescape, and cut at a character cap because the longest
    /// bundled Description (Community Mod Chest's) is over ten thousand characters and IMGUI draws a
    /// label as one text mesh, whose size limit has not been measured here. A cut text ends in
    /// CutNote, which points the player at the Nexus page for the rest.
    /// </summary>
    public static class SuiteDescription
    {
        /// <summary>The DetailsMaxChars setting's default. A starting value, not a measured limit.</summary>
        public const int DefaultMaxChars = 2500;

        /// <summary>What follows a cut text: an ellipsis and where to read the rest.</summary>
        public const string CutNote = "... (full text on the Nexus page)";

        /// <summary>
        /// The panel's text: modInfoJson's Description when it has one with something in it, otherwise
        /// fallback (null reads as empty), trimmed and cut to cap characters. A cap of zero or less
        /// means no cut. modInfoJson is the text of the embedded ModInfo.json, or null when the bundled
        /// copy could not be read.
        /// </summary>
        public static string Resolve(string modInfoJson, string fallback, int cap)
        {
            string text = null;
            if (!string.IsNullOrEmpty(modInfoJson))
                text = SimpleJson.DeserializeModInfo(modInfoJson).Description;
            if (string.IsNullOrWhiteSpace(text))
                text = fallback;
            return Limit(text == null ? "" : text.Trim(), cap);
        }

        /// <summary>text cut to cap characters, then CutNote. A cut never leaves half of a surrogate pair.</summary>
        public static string Limit(string text, int cap)
        {
            if (cap <= 0 || text.Length <= cap) return text;
            int cut = cap;
            if (char.IsHighSurrogate(text[cut - 1])) cut--;
            return text.Substring(0, cut) + CutNote;
        }
    }
}
