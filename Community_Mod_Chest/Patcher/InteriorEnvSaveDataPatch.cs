using System;
using System.Reflection;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher;

/// <summary>
/// Pre-creates the <c>GameManager.EnvironmentsData</c> entry for each of CMC's 7 non-instanced
/// interior CT4 environments at run start, so a player's FIRST entry into any of them succeeds.
///
/// <para>Root cause (Documentation/Plans/Fleet/Village_Player_Report_Remediation_Plan.md §W2.2;
/// Community_Mod_Chest/.audit/player-report-triage-2026-08-10.md finding 4): these interiors are
/// neither vanilla nodes nor MapNodes.json clone nodes, so nothing pre-creates their
/// EnvironmentsData entry the way <c>WorldMapInjector.PreCreateCloneEnvSaveData</c> does for clone
/// envs. On first entry, <c>GameManager.ChangeEnvironment</c>'s gate
/// (<c>if (EnvironmentsData.ContainsKey(envKey))</c>, .decomp/GameManager.cs:10186) fails, so the
/// entire card-load block is skipped: <c>CurrentEnvironment</c> never advances to the interior,
/// and the immediately following <c>CheckForMissingDefaultCardsInEnv()</c> call
/// (.decomp/GameManager.cs:10254) re-drops the STILL-CURRENT Village env's own UniqueOnBoard CT8
/// (<c>cmcLocVillage</c>) onto the board the player is now looking at — the reported "outdoors
/// inside every building."</para>
///
/// <para>Fix: call <c>GameManager.GetEnvSaveData(EnvID, _CreateIfNull:true)</c> for each interior
/// UID before the player can ever visit. That call is idempotent by design
/// (.decomp/GameManager.cs:1134-1169) — an existing entry is returned unmodified, so re-running
/// this every run start is harmless on saves that already visited an interior.</para>
/// </summary>
internal static class InteriorEnvSaveDataPatch
{
    private static readonly string[] InteriorEnvUids =
    {
        "cmcInnInterior",
        "cmcAcademyInterior",
        "cmcApothecaryCabinInterior",
        "cmcMillerCottageInterior",
        "cmcWeaverCottageInterior",
        "cmcVillageHallInterior",
        "cmcEnvJailCell",
    };

    private const BindingFlags InstanceAll =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool _subscribed;
    private static Action _gmInitializedHandler;

    public static void Initialize()
    {
        if (_subscribed) return;

        var gmType = CardUtil.FindGameType("GameManager");
        if (gmType == null)
        {
            Plugin.Logger.LogWarning("[InteriorEnvSaveData] GameManager type not found — inactive.");
            return;
        }

        // Must run at/after OnGMInitialized — EnvironmentsData is only fully restored from the
        // save by then (same load-order reasoning as RiverBridgeUnlockPatch).
        var field = gmType.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(Action))
        {
            Plugin.Logger.LogWarning("[InteriorEnvSaveData] GameManager.OnGMInitialized not found — inactive.");
            return;
        }

        _gmInitializedHandler = OnRunStart;
        var current = (Action)field.GetValue(null);
        field.SetValue(null, (Action)Delegate.Combine(current, _gmInitializedHandler));
        _subscribed = true;
    }

    private static void OnRunStart()
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null)
            {
                Plugin.Logger.LogWarning("[InteriorEnvSaveData] GameManager.Instance is null at OnGMInitialized — skipping this run.");
                return;
            }

            var envIdType = CardUtil.FindGameType("EnvID");
            if (envIdType == null)
            {
                Plugin.Logger.LogWarning("[InteriorEnvSaveData] EnvID type not found — skipping this run.");
                return;
            }

            var getEnvSaveData = gm.GetType().GetMethod("GetEnvSaveData", InstanceAll);
            if (getEnvSaveData == null)
            {
                Plugin.Logger.LogWarning("[InteriorEnvSaveData] GameManager.GetEnvSaveData(EnvID,bool) not found — skipping this run.");
                return;
            }

            int ok = 0;
            foreach (var uid in InteriorEnvUids)
            {
                try
                {
                    // EnvID(string) ctor — same idiom as CardUtil.MarkImprovementBuilt's
                    // never-visited-env fallback. Safe here because none of these 7 UIDs contain
                    // '_' (InterpretStringDictionnaryKey splits on it for instanced-env parent
                    // chains — root CLAUDE.md WorldMap underscore rule).
                    var envId = Activator.CreateInstance(envIdType, new object[] { uid });
                    var entry = getEnvSaveData.Invoke(gm, new object[] { envId, true });
                    if (entry != null) ok++;
                    Plugin.Logger.LogDebug($"[InteriorEnvSaveData] pre-created/verified EnvironmentsData for '{uid}'.");
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogWarning($"[InteriorEnvSaveData] pre-create failed for '{uid}': {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }

            Plugin.Logger.LogInfo($"[InteriorEnvSaveData] pre-created/verified EnvironmentsData for {ok}/{InteriorEnvUids.Length} interior environment(s).");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[InteriorEnvSaveData] failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }
}
