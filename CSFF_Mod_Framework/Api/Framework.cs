namespace CSFFModFramework.Api;

/// <summary>
/// Framework identity and feature detection for downstream mods and tooling.
///
/// <para>Content-type support varies by framework version (2.1.0 added the NPC /
/// Encounter / SelfTriggeredAction / Quest / PlayerCharacter family). A mod that
/// ships content in one of those folders should feature-detect rather than fail
/// silently on an older framework:</para>
///
/// <code>
/// if (!Framework.SupportsContentType("NPCAgent"))
///     Log.LogWarning("MyMod requires CSFFModFramework 2.1.0+ for NPCAgent content.");
/// </code>
///
/// <para>Activation status by type family:</para>
/// <list type="bullet">
/// <item><description><b>SelfTriggeredAction</b> — fully active since 2.2.0. GameManager
/// auto-discovers STAs from AllData at run start; see <c>Injection.StaActivationService</c>.</description></item>
/// <item><description><b>NPCAgent / NPCDuty / NPCStat / NPCHidingGroup</b> — loaded since 2.1.0.
/// Since 2.3.0, <c>Injection.NPCAgentActivationService</c> validates mod NPCAgents at load time
/// and logs [DIAGNOSTICS] lines at OnGMInitialized to reveal the injection point. The actual
/// NPCAgentInjector is a follow-up once the diagnostics confirm how GameManager discovers agents.</description></item>
/// <item><description><b>WorldMap (WorldMapData)</b> — mod environment nodes injected since 2.3.0
/// via <c>WorldMap/MapNodes.json</c> + <c>Injection.WorldMapInjector</c>. WorldMapData is a plain
/// ScriptableObject (not UniqueIDScriptable); nodes are appended at load time to the singleton
/// WorldMapData.Environments[] with automatic bidirectional links.</description></item>
/// <item><description>All other loaded types (Encounter, Objective, QuestLog, PlayerCharacter, etc.)
/// — loaded and registered; activation/injection in later phases.</description></item>
/// </list>
/// </summary>
public static class Framework
{
    /// <summary>Framework semantic version. Matches the BepInEx plugin version.</summary>
    public const string Version = Plugin.PluginVersion;

    /// <summary>
    /// Type names the framework loads from top-level mod content directories
    /// (folder name == type name, e.g. <c>NPCStat/*.json</c> → <c>NPCStat</c> SOs).
    /// Types under <c>ScriptableObject/&lt;TypeName&gt;/</c> are loaded generically
    /// and are not listed here.
    /// </summary>
    public static IReadOnlyCollection<string> SupportedContentTypes
        => Loading.JsonDataLoader.SupportedContentTypes;

    /// <summary>True if this framework version loads the given content type from mod JSON. Case-insensitive.</summary>
    public static bool SupportsContentType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        foreach (var t in Loading.JsonDataLoader.SupportedContentTypes)
            if (string.Equals(t, typeName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
