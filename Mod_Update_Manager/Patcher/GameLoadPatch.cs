using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using mod_update_manager;

namespace mod_update_manager.Patcher
{
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                var loadMainGameDataMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                
                var postfixMethod = AccessTools.Method(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
                harmony.Patch(loadMainGameDataMethod, postfix: new HarmonyMethod(postfixMethod));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to patch GameLoad.LoadMainGameData: {ex.Message}");
                Logger.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        static void LoadMainGameData_Postfix(object __instance)
        {
            try
            {
                Logger.LogInfo("Mod_Update_Manager: Game data loaded successfully!");
                
                // Notify the plugin that game data has loaded
                if (Plugin.Instance != null)
                {
                    Plugin.Instance.OnGameDataLoaded();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in LoadMainGameData_Postfix: {ex.Message}");
                Logger.LogError($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}
