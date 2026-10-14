using CSFFModFramework.Util;
using HarmonyLib;

namespace CSFFModFramework.Loading;

/// <summary>
/// Gives every registered <see cref="UniqueIDScriptable"/> the <c>UniqueIDIndex</c> that matches its
/// position in <c>UniqueIDScriptable.AllUniqueObjectsAsInts</c>.
///
/// <para>The game numbers objects once, in <c>SortUniqueObjectList()</c>, whose only caller is
/// <c>GameLoad.AwakeWithLoadingScreen</c>, and that runs BEFORE <c>LoadMainGameData()</c>, whose postfix
/// is where the framework registers mod cards. <c>RegisterID</c> appends each mod card to the list and
/// nothing numbers it again, so every JSON-loaded mod card keeps the default index 0.</para>
///
/// <para>Why that matters: an environment's save slot is <c>EnvDictKey.EnvIndex</c>, which IS that
/// index, and <c>EnvID.InterpretDictionnaryKey</c> maps it back with <c>GetFromIndex</c>. At index 0 every
/// mod room (CMC's interiors, the jail cell, Far Territory's places) shared ONE board, and shared it
/// with vanilla's <c>Env_BearCave</c>, the object at position 0, which is how a bear walked into the
/// Inn (harness run 2026-10-04, report section 2; the game's own sort, run by hand, separated them).
/// Clones already got their own index (<see cref="CardCloneService"/>); JSON-loaded cards never did.</para>
///
/// <para>Setting index = position never renumbers a vanilla object: after the game's sort every vanilla
/// entry already matches its position, so only appended (mod) entries change. The int key is
/// runtime-only: saves store environments under the UID string key
/// (<c>EnvironmentSaveDataByReference.DictionaryKey</c>), so an index that depends on load order is
/// save-safe.</para>
/// </summary>
internal static class UniqueIdIndexRepair
{
    /// <summary>
    /// A third pass at run start, as a <c>GameManager.Awake</c> prefix: content mods' own
    /// <c>LoadMainGameData</c> postfixes run after the framework's and may register objects too, and
    /// <c>Awake</c> builds the environment keys (<c>LoadCards</c>) before anything else reads them.
    /// </summary>
    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            var awake = AccessTools.Method(typeof(GameManager), "Awake");
            if (awake == null)
            {
                Log.Warn("UniqueIdIndexRepair: GameManager.Awake not found; objects registered after the framework's load keep index 0");
                return;
            }
            harmony.Patch(awake, prefix: new HarmonyMethod(typeof(UniqueIdIndexRepair), nameof(Awake_Prefix)));
        }
        catch (System.Exception ex)
        {
            Log.Warn($"UniqueIdIndexRepair: failed to patch GameManager.Awake: {Log.ExceptionText(ex)}");
        }
    }

    private static void Awake_Prefix()
    {
        try { RepairAll("run start"); }
        catch (System.Exception ex) { Log.Warn($"UniqueIdIndexRepair: run-start pass failed: {Log.ExceptionText(ex)}"); }
    }

    public static void RepairAll(string phase)
    {
        var list = UniqueIDScriptable.AllUniqueObjectsAsInts;
        if (list == null)
        {
            Log.Warn($"UniqueIdIndexRepair ({phase}): AllUniqueObjectsAsInts unavailable, mod environments may share a save board");
            return;
        }

        // Stable feature (shipped 2.28.0): the per-object detail is only useful while
        // debugging the repair itself, so sample the first few under VerboseLogging and let
        // the aggregate Info line below carry the full count. Avoids ~7k Debug lines/boot.
        const int SampleLimit = 20;
        int fixedCount = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var obj = list[i];
            if (obj == null || obj.UniqueIDIndex == i) continue;
            if (fixedCount < SampleLimit)
                Log.Debug($"UniqueIdIndexRepair: '{obj.UniqueID}' ({obj.name}) index {obj.UniqueIDIndex} -> {i}");
            else if (fixedCount == SampleLimit)
                Log.Debug($"UniqueIdIndexRepair: ... (further per-object lines suppressed; see the count below)");
            obj.UniqueIDIndex = i;
            fixedCount++;
        }

        if (fixedCount > 0)
            Log.Info($"UniqueIdIndexRepair ({phase}): assigned {fixedCount} index(es) to objects registered after the game's sort");
    }
}
