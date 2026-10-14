using System;
using System.Collections.Generic;
using System.Linq;

namespace mod_update_manager
{
    /// <summary>One BepInEx plugin GUID registered by two different DLLs.</summary>
    public class PluginGuidCollision
    {
        public string Guid { get; set; }
        public string DllA { get; set; }
        public string DllB { get; set; }
    }

    /// <summary>
    /// Finds plugin DLLs that register the same [BepInPlugin] GUID, which BepInEx treats as one
    /// plugin and loads only one copy of. This is identity by the value that decides it, unlike the
    /// name patterns in ConflictDetector. Seen on the reference install (2026-09-26):
    /// Pikachu's CardSizeReduce folder ships its own 5,632-byte plugins/ModCore.dll registering
    /// Pikachu.CSFF.ModCore, the GUID of the separately installed ModCore.
    ///
    /// Host-free: Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1 compiles and drives it.
    /// </summary>
    public static class PluginGuidCollisions
    {
        /// <summary>
        /// guidsByDll maps a DLL's display name to the GUIDs its [BepInPlugin] attributes declare
        /// (a DLL may declare several). GUIDs compare as exact strings. One collision per GUID per
        /// DLL pair: a DLL naming the same GUID twice is still one DLL, never a pair with itself.
        /// </summary>
        public static List<PluginGuidCollision> Find(IDictionary<string, List<string>> guidsByDll)
        {
            var dllsByGuid = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kvp in guidsByDll)
            {
                if (kvp.Value == null) continue;
                foreach (var guid in kvp.Value.Where(g => !string.IsNullOrEmpty(g)))
                {
                    if (!dllsByGuid.TryGetValue(guid, out var dlls))
                        dllsByGuid[guid] = dlls = new List<string>();
                    if (!dlls.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                        dlls.Add(kvp.Key);
                }
            }

            var collisions = new List<PluginGuidCollision>();
            foreach (var kvp in dllsByGuid.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var dlls = kvp.Value.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
                for (int i = 0; i < dlls.Count - 1; i++)
                    for (int j = i + 1; j < dlls.Count; j++)
                        collisions.Add(new PluginGuidCollision { Guid = kvp.Key, DllA = dlls[i], DllB = dlls[j] });
            }
            return collisions;
        }
    }
}
