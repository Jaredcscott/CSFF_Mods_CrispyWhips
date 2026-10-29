using BepInEx.Configuration;

namespace CSFFModFramework.Util;

/// <summary>
/// Bilingual config descriptions: every framework setting's description is its English text, a line
/// break, then the same text in Chinese, so the .cfg file and BepInEx ConfigurationManager read in
/// both languages (BepInEx writes each description line behind its own "##"). The Chinese sits at
/// each call site beside the English it translates, so a changed description shows its translation
/// needs changing too. Requested by Chiwei (Nexus c177496068).
/// </summary>
internal static class ConfigText
{
    internal static ConfigEntry<T> Bind<T>(this ConfigFile config, string section, string key, T defaultValue,
        string english, string chinese)
        => config.Bind(section, key, defaultValue, Bilingual(english, chinese));

    internal static string Bilingual(string english, string chinese)
        => string.IsNullOrEmpty(chinese) ? english : english + "\n" + chinese;
}
