// GameLoadPatch.cs — Main runtime data injection point.
//
// This file is optional. If your mod is purely JSON-based (items, blueprints,
// perks with no runtime C# logic), you can DELETE this file and remove the
// GameLoadPatch.ApplyPatch() call from Plugin.cs.
//
// When you DO need C# runtime patches (e.g., injecting forage drops into
// vanilla locations, modifying vanilla cards, spawning cards on events),
// this is where they go — as a postfix to GameLoad.LoadMainGameData.

using System;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;

namespace TODO_ModName.Patcher
{
    public static class GameLoadPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                if (gameLoadType == null)
                {
                    Logger.LogError("GameLoad type not found — load patches were not applied.");
                    return;
                }

                var loadMainGameDataMethod = AccessTools.Method(gameLoadType, "LoadMainGameData");
                if (loadMainGameDataMethod == null)
                {
                    Logger.LogError("GameLoad.LoadMainGameData not found — load patches were not applied.");
                    return;
                }

                var postfixMethod = AccessTools.Method(typeof(GameLoadPatch), nameof(LoadMainGameData_Postfix));
                harmony.Patch(loadMainGameDataMethod, postfix: new HarmonyMethod(postfixMethod));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to patch GameLoad.LoadMainGameData: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        private static void LoadMainGameData_Postfix()
        {
            // TODO: Add runtime data modifications here.
            //
            // IMPORTANT: WarpResolver has already run at this point. If you set
            // a *WarpData string field here, it won't resolve — you must also set
            // the corresponding resolved SO field directly.
            //
            // EXAMPLE: Injecting a forage-dig action into a vanilla location.
            // The full pattern with reflection for DismantleActions List injection
            // is shown in the ebook (Part 10 — Harmony Patching).
            //
            // Logger.LogInfo("TODO_ModName: LoadMainGameData postfix running.");

            // Remove this file and its ApplyPatch call in Plugin.cs if unused.
        }
    }
}
