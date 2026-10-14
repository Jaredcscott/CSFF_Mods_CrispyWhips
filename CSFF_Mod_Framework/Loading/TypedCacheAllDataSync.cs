using CSFFModFramework.Util;
using HarmonyLib;

namespace CSFFModFramework.Loading;

/// <summary>
/// Keeps the framework's typed ScriptableObject cache (<c>Database.GetAllOfType</c>,
/// <c>CardUtil.FindCardsWhere</c>) equal to the game's AllData. Objects added through
/// <c>GameRegistry.TryAddToAllData</c> join the cache as they are added; this pass lists the rest,
/// which other code appended to AllData itself (<see cref="Data.Database.RegisterAllDataStragglers"/>).
///
/// <para>Runs at the framework's end of load and again as a <c>GameManager.Awake</c> prefix, because
/// content mods' own <c>LoadMainGameData</c> postfixes run after the framework's: WDI's mill-race clones
/// enter AllData there.</para>
/// </summary>
internal static class TypedCacheAllDataSync
{
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var awake = AccessTools.Method(typeof(GameManager), "Awake");
            if (awake == null)
            {
                Log.Warn("TypedCacheAllDataSync: GameManager.Awake not found; objects other mods add to AllData stay invisible to FindCardsWhere");
                return;
            }
            harmony.Patch(awake, prefix: new HarmonyMethod(typeof(TypedCacheAllDataSync), nameof(Awake_Prefix)));
        }
        catch (System.Exception ex)
        {
            Log.Warn($"TypedCacheAllDataSync: failed to patch GameManager.Awake: {Log.ExceptionText(ex)}");
        }
    }

    private static void Awake_Prefix()
    {
        try { Sync("run start"); }
        catch (System.Exception ex) { Log.Warn($"TypedCacheAllDataSync: run-start pass failed: {Log.ExceptionText(ex)}"); }
    }

    public static void Sync(string phase)
    {
        int added = Data.Database.RegisterAllDataStragglers();
        if (added > 0)
            Log.Info($"TypedCacheAllDataSync ({phase}): listed {added} AllData object(s) added outside the framework");
    }
}
