using System.Collections.Generic;

namespace mod_update_manager
{
    /// <summary>
    /// The headers that say what is asking, sent on every request Mod Update Manager makes to Nexus:
    /// the per-mod reads, the changelog read, the key check and the one public query
    /// (NexusApiClient.SetIdentityHeaders applies them). Nexus's API Acceptable Use Policy asks every
    /// application to send Application-Name and Application-Version
    /// (Documentation/Research/Mod_Update_Manager/W0-1_Nexus_Read_Paths.md section 5.1 item 2, and
    /// section 6 item 1); before 2.1.63 only the User-Agent was sent. None of the three carries the
    /// API key or anything about the player or the install.
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it.
    /// </summary>
    public static class NexusRequestHeaders
    {
        /// <summary>The name sent as Application-Name and in the User-Agent; Plugin.PluginName holds the
        /// same text, and MUM-CompiledLogic.Tests.ps1 compares the two.</summary>
        public const string ApplicationName = "Mod_Update_Manager";

        /// <summary>The identity headers in the order they are set. The caller passes Plugin.PluginVersion.</summary>
        public static KeyValuePair<string, string>[] Identity(string version)
        {
            return new[]
            {
                new KeyValuePair<string, string>("User-Agent", ApplicationName + "/" + version),
                new KeyValuePair<string, string>("Application-Name", ApplicationName),
                new KeyValuePair<string, string>("Application-Version", version),
            };
        }
    }
}
