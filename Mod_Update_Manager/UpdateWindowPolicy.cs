namespace mod_update_manager
{
    /// <summary>
    /// Decides whether a finished update check may open the Mod Update Manager window by itself.
    /// The window opens at most once per session, after the startup check finds an update;
    /// scheduled checks and the Re-check button refresh the counts and never open it.
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it.
    /// </summary>
    public static class UpdateWindowPolicy
    {
        /// <param name="enabled">The OpenWindowOnUpdates option.</param>
        /// <param name="startupCheck">True only for the check Plugin.OnGameDataLoaded started.</param>
        /// <param name="alreadyOpened">True once this session's automatic open has happened.</param>
        /// <param name="updateCount">Mods the finished check found with an update available.</param>
        public static bool ShouldAutoOpen(bool enabled, bool startupCheck, bool alreadyOpened, int updateCount)
        {
            return enabled && startupCheck && !alreadyOpened && updateCount > 0;
        }
    }
}
