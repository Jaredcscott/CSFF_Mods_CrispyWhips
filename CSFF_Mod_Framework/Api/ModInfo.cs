using CSFFModFramework.Discovery;

namespace CSFFModFramework.Api;

/// <summary>
/// Read-only descriptor for a mod discovered by the framework.
/// Returned from <see cref="ModRegistry"/>. The fields here are guaranteed
/// stable across minor framework versions.
/// </summary>
public sealed class ModInfo
{
    private readonly ModManifest _inner;

    internal ModInfo(ModManifest inner) { _inner = inner; }

    /// <summary>Display name of the mod (from ModInfo.json's <c>Name</c> field, or folder name fallback).</summary>
    public string Name => _inner.Name;

    /// <summary>Mod author (from ModInfo.json's <c>Author</c>).</summary>
    public string Author => _inner.Author;

    /// <summary>Mod version (from ModInfo.json's <c>Version</c>).</summary>
    public string Version => _inner.Version;

    /// <summary>Player-facing description (from ModInfo.json's <c>Description</c>).</summary>
    public string Description => _inner.Description;

    /// <summary>Absolute path to the mod's installation directory (under <c>BepInEx/plugins/</c>).</summary>
    public string DirectoryPath => _inner.DirectoryPath;
}
