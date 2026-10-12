using System;
using System.Reflection;
using CSFFModFramework.Util;

namespace FarTerritory.Patcher;

/// <summary>
/// Pre-creates the <c>GameManager.EnvironmentsData</c> entry of every Far Territory NON-CLONE board at run
/// start, so the player's first visit finds the board's default cards (its CT8 and the cards listed in
/// the CT4's <c>DefaultEnvCardDrops</c>).
///
/// <para>Why (Documentation/Research/FarTerritory/wave0-map.md section 1.2, plan section 1.7): the game seeds a
/// never-visited board only through <c>GetEnvSaveData(env, _CreateIfNull: true)</c>, and nothing calls that for
/// a mod CT4 that is not a framework clone (<c>WorldMapInjector.PreCreateCloneEnvSaveData</c> covers clone
/// nodes only). Without an entry, <c>ChangeEnvironment</c> skips the whole board load and the player lands on
/// the previous board's cards. Community Mod Chest's interiors failed exactly so; this is the same fix as
/// <c>Community_Mod_Chest/Patcher/InteriorEnvSaveDataPatch.cs</c>.</para>
///
/// <para><c>GetEnvSaveData</c> returns an existing entry untouched, so running this on every run start is
/// harmless, and a board's first seed is frozen at the version that first created it. <c>OnGMInitialized</c>
/// fires on every run start in one process (CLAUDE.md "Per-Run Event Registration"), so the subscription is
/// made once per process (the <c>_subscribed</c> guard), and the work it does is idempotent. The EnvID is
/// built at the moment of use (memory reference_envid_key_stale_uniqueidindex): by OnGMInitialized the
/// UniqueIDIndex sort has run. The <c>EnvID(string)</c> constructor is safe because these UIDs carry no
/// underscore.</para>
/// </summary>
internal static class EnvSaveDataPreCreate
{
    /// <summary>Every non-clone environment this mod ships. A clone node (the River Mouth) is NOT listed: the
    /// framework pre-creates clone entries itself.</summary>
    internal static readonly string[] NonCloneEnvUids =
    {
        "ftEnvShore",
        "ftEnvLandingBeach",
        "ftEnvLandfallStrand",
        "ftEnvCoralInlet",
        "ftEnvBoulderShore",
        "ftEnvRootwaterThicket",
        "ftEnvBleakStrand",
        "ftEnvPalmFringe",
        "ftEnvCliffboundCove",
        "ftEnvGullStack",
        "ftEnvVineWoods",
        "ftEnvWestgrassPlain",
        "ftEnvEastgrassPlain",
        "ftEnvReedMarsh",
        "ftEnvGreenDeep",
        "ftEnvMistyRidge",
        "ftEnvHiddenVale",
        "ftEnvWesternUplands",
        "ftEnvEasternUplands",
        "ftEnvFireMountain",
        "ftEnvSourCrater",
        "ftEnvWreckHollow",
        "ftEnvFallenDwelling",
        "ftEnvSeaGrotto",
        "ftEnvWaveGrotto",
        "ftEnvPlainGrotto",
        "ftEnvBatRoost",
        "ftEnvMonkeyDen",
        "ftEnvBlackGrotto",
        "ftEnvSinkhole",
        "ftEnvLavaTube",
        "ftEnvDrippingHall",
        "ftEnvSqueeze",
        "ftEnvDrownedHall",
        "ftEnvGlitteringHall",
        "ftEnvLowerHall",
        "ftEnvLightlessHall",
        "ftEnvUpperHall",
        "ftEnvMiddleHall",
        "ftEnvWildryePlain",
        "ftEnvSedgeFen",
        "ftEnvScreeSlope",
        "ftEnvSteamingShelf",
        "ftEnvWindgapCol",
        "ftEnvGreenstoneGrotto",
        "ftEnvRustGrotto",
        "ftEnvGlimmerGrotto",
        "ftEnvFjordHead",
        "ftEnvSealSkerry",
        "ftEnvPuffinCliffs",
    };

    private const BindingFlags InstanceAll = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool _subscribed;

    public static void Initialize()
    {
        if (_subscribed) return;

        var gmType = CardUtil.FindGameType("GameManager");
        if (gmType == null)
        {
            Plugin.ModLog.LogWarning("[EnvSaveDataPreCreate] GameManager type not found - inactive; Far Territory boards will be empty on a first visit.");
            return;
        }

        var field = gmType.GetField("OnGMInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(Action))
        {
            Plugin.ModLog.LogWarning("[EnvSaveDataPreCreate] GameManager.OnGMInitialized not found - inactive; Far Territory boards will be empty on a first visit.");
            return;
        }

        var current = (Action)field.GetValue(null);
        field.SetValue(null, (Action)Delegate.Combine(current, (Action)OnRunStart));
        _subscribed = true;
    }

    private static void OnRunStart()
    {
        try
        {
            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null)
            {
                Plugin.ModLog.LogWarning("[EnvSaveDataPreCreate] GameManager.Instance is null at OnGMInitialized - skipping this run.");
                return;
            }

            var envIdType = CardUtil.FindGameType("EnvID");
            if (envIdType == null)
            {
                Plugin.ModLog.LogWarning("[EnvSaveDataPreCreate] EnvID type not found - skipping this run.");
                return;
            }

            var getEnvSaveData = gm.GetType().GetMethod("GetEnvSaveData", InstanceAll);
            if (getEnvSaveData == null)
            {
                Plugin.ModLog.LogWarning("[EnvSaveDataPreCreate] GameManager.GetEnvSaveData(EnvID,bool) not found - skipping this run.");
                return;
            }

            int ok = 0;
            foreach (var uid in NonCloneEnvUids)
            {
                try
                {
                    var envId = Activator.CreateInstance(envIdType, new object[] { uid });
                    var entry = getEnvSaveData.Invoke(gm, new object[] { envId, true });
                    if (entry != null)
                        ok++;
                    else
                        Plugin.ModLog.LogWarning($"[EnvSaveDataPreCreate] GetEnvSaveData returned no entry for '{uid}'.");
                }
                catch (Exception ex)
                {
                    Plugin.ModLog.LogWarning($"[EnvSaveDataPreCreate] pre-create failed for '{uid}': {ex.InnerException?.ToString() ?? ex.ToString()}");
                }
            }

            Plugin.ModLog.LogDebug($"[EnvSaveDataPreCreate] pre-created/verified EnvironmentsData for {ok}/{NonCloneEnvUids.Length} board(s).");
        }
        catch (Exception ex)
        {
            Plugin.ModLog.LogWarning($"[EnvSaveDataPreCreate] failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }
}
