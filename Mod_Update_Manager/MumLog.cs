using System;

namespace mod_update_manager
{
    /// <summary>
    /// Log sink for the classes that must run outside the game as well as inside it
    /// (SuiteInstaller, ModMappingManager, CompatibilityChip, ConflictDetector's GUID grouping).
    /// Plugin.Awake points these at its BepInEx ManualLogSource before constructing anything;
    /// until then, and in the Pester gates that compile and call those classes directly
    /// (Development_Tools/Tests/MUM-*.Tests.ps1), they are no-ops or whatever the test assigns.
    /// Nothing here may reference BepInEx or UnityEngine, or those gates cannot load the class.
    /// </summary>
    public static class MumLog
    {
        public static Action<string> Debug = _ => { };
        public static Action<string> Info = _ => { };
        public static Action<string> Warning = _ => { };
        public static Action<string> Error = _ => { };
    }
}
