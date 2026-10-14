using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Reads every loaded BepInEx plugin's DLL path, GUID and declared dependencies from
/// <c>Chainloader.PluginInfos</c> for <see cref="AuthoringLint.SetModPlugins"/> (plan section 19, W0-12:
/// the chainloader has finished before the framework's LoadMainGameData postfix runs). A dependency is
/// hard when its flags carry no SoftDependency bit. A plugin BepInEx skipped for a missing hard
/// dependency is never in <c>PluginInfos</c>, so it reads as declaring nothing.
/// </summary>
internal static class PluginDependencyReader
{
    internal static List<AuthoringLint.PluginRecord> Read()
    {
        var list = new List<AuthoringLint.PluginRecord>();
        try
        {
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                if (info == null || string.IsNullOrEmpty(info.Location) || info.Metadata == null) continue;
                var deps = new List<(string Guid, bool Hard)>();
                if (info.Dependencies != null)
                    foreach (var d in info.Dependencies)
                        if (d != null)
                            deps.Add((d.DependencyGUID, (d.Flags & BepInDependency.DependencyFlags.SoftDependency) == 0));
                list.Add(new AuthoringLint.PluginRecord(info.Location, info.Metadata.GUID, deps));
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"AuthoringLint: could not read the loaded plugins' dependencies ({ex.GetType().Name}: {ex.Message}); every cross-mod structural reference will be reported");
        }
        if (list.Count == 0)
            Log.Warn("AuthoringLint: Chainloader.PluginInfos listed no plugins; every cross-mod structural reference will be reported");
        else
            Log.Debug($"AuthoringLint: read {list.Count} loaded plugin(s) for the HardDependency allowance");
        return list;
    }
}
