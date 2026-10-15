using System;
using System.Collections.Generic;
using CSFFModFramework.Api;
using UnityEngine;

namespace FarTerritory.Patcher;

/// <summary>
/// The climate of Far Territory's lands, with no cards. It replaces the four placed "climate" cards (Island Air,
/// Mountain Air, Fjord Chill, Volcanic Heat), which sat on every board as visible cards and did not reliably
/// move the stat (owner report, 2026-10-11: "these climate cards are not working and we really do not want cards
/// for the climate").
///
/// <para>Mechanism: the game applies a card's <c>PassiveStatEffects</c> as a <c>StatModification.GlobalModifier</c>
/// through <c>GameManager.ApplyStatModifier</c> (<c>InGameCardBase.ApplyStatModifiers</c>), which only adds to
/// <c>InGameStat.GlobalModifiedValue</c>. This does exactly that, straight from the current environment, so the
/// stat bands, body temperature and snow all follow as before. Only the DELTA to the wanted offset is applied,
/// so entering, leaving and crossing between lands each cost one small modifier.</para>
///
/// <para>Per-run state: <c>GlobalModifiedValue</c> is never saved (<c>InGameStat.Save</c> omits it, and every run
/// builds fresh <c>InGameStat</c> objects), so what this has applied is remembered against the InGameStat object
/// it was applied to. A new run (save-reload, main-menu round trip) hands out new objects, the remembered amount
/// is discarded and the offset is applied again from zero. That keeps it correct without subscribing to a
/// per-run event (CLAUDE.md "Per-Run Event Registration").</para>
/// </summary>
internal static class ClimateService
{
    private const string TemperaturePerceivedUid = "0d5ec8f104b2ba149b4bb2b536bc21a4";
    private const string SnowVisibleUid = "ec625eaefee93cc4c8112d8c9046a81c";

    /// <summary>Snow is a 0-480 stat; a modifier this size pins it at its floor, as the old cards did.</summary>
    private const float NoSnow = -480f;

    private readonly struct Climate
    {
        public readonly float Temperature;
        public readonly float Snow;
        /// <summary>True for the island: the temperature follows the season and the hour (see IslandTemperature).</summary>
        public readonly bool Seasonal;
        public Climate(float temperature, float snow, bool seasonal = false) { Temperature = temperature; Snow = snow; Seasonal = seasonal; }
    }

    // Warm island: milder than the forest and no snow. Measured on the first build that 30 made a winter body
    // sweat, so 9. Since 0.1.7 (vanilla-balance pass, FT-B6) it follows the season: +9 in spring and autumn,
    // +5 in winter, and in summer +9 with +4 more from 11:00 to 16:00, the midday heat, so shade and water matter.
    // Vanilla gives location warmth only indoors, at most +6 (a cabin room), so +9 outdoors is already generous.
    private const float IslandMild = 9f;
    private const float IslandWinter = 5f;
    private const float IslandMiddayExtra = 4f;
    private const float MiddayFrom = 11f;
    private const float MiddayTo = 16f;
    private static readonly Climate Island = new(IslandMild, NoSnow, seasonal: true);
    // Volcano: dry, steady heat, no snow. +10 since 0.1.7 (FT-B6), a burning Campfire's warmth; it was +16.
    private static readonly Climate Volcano = new(10f, NoSnow);
    // Mountains and fjords are colder than the forest at the same hour; the weather and snow stay the forest's own.
    private static readonly Climate Mountain = new(-6f, 0f);
    private static readonly Climate Fjord = new(-8f, 0f);

    private static readonly Dictionary<string, Climate> ByEnv = BuildTable();

    private sealed class Applied
    {
        public InGameStat Stat;
        public float Amount;
    }

    private static readonly Dictionary<string, Applied> _applied = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private static bool _initialized;
    private static GameStat _temperatureStat;
    private static GameStat _snowStat;

    private static Dictionary<string, Climate> BuildTable()
    {
        var t = new Dictionary<string, Climate>(StringComparer.Ordinal);
        foreach (var uid in new[]
        {
            "ftEnvBleakStrand", "ftEnvBoulderShore", "ftEnvCliffboundCove", "ftEnvCoralInlet", "ftEnvEasternUplands",
            "ftEnvEastgrassPlain", "ftEnvFallenDwelling", "ftEnvGreenDeep", "ftEnvGullStack", "ftEnvHiddenVale",
            "ftEnvLandfallStrand", "ftEnvMistyRidge", "ftEnvPalmFringe", "ftEnvReedMarsh", "ftEnvRootwaterThicket",
            "ftEnvVineWoods", "ftEnvWesternUplands", "ftEnvWestgrassPlain", "ftEnvWreckHollow",
        })
            t[uid] = Island;
        t["ftEnvFireMountain"] = Volcano;
        t["ftEnvSourCrater"] = Volcano;
        t["ftEnvScreeSlope"] = Mountain;
        t["ftEnvSteamingShelf"] = Mountain;
        t["ftEnvWindgapCol"] = Mountain;
        t["ftEnvFjordHead"] = Fjord;
        t["ftEnvPuffinCliffs"] = Fjord;
        t["ftEnvSealSkerry"] = Fjord;
        return t;
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        TickEvents.Interval(1f, Reconcile, "FarTerritoryClimate");
    }

    private static void Reconcile()
    {
        try
        {
            var gm = MBSingleton<GameManager>.Instance;
            if (gm == null || gm.StatsDict == null || gm.StatsDict.Count == 0) return;

            Climate want = default;
            string envUid = GameQuery.CurrentEnvironmentUniqueId;
            if (envUid != null) ByEnv.TryGetValue(envUid, out want);

            _temperatureStat ??= UniqueIDScriptable.GetFromID<GameStat>(TemperaturePerceivedUid);
            _snowStat ??= UniqueIDScriptable.GetFromID<GameStat>(SnowVisibleUid);

            Apply(gm, TemperaturePerceivedUid, _temperatureStat, want.Seasonal ? IslandTemperature() : want.Temperature);
            Apply(gm, SnowVisibleUid, _snowStat, want.Snow);
        }
        catch (Exception ex)
        {
            WarnOnce("exception-" + ex.GetType().Name,
                $"[ClimateService] Reconcile threw, so the climate offset was not applied this pass: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    /// <summary>The island's warmth for the current season and clock hour. GameQuery.CurrentSeason reads the
    /// game's own season name and GameQuery.HourOfDay the clock hour (framework 2.26.0+, which this mod already
    /// requires). An unknown season (null) reads as mild, the pre-0.1.7 value.</summary>
    private static float IslandTemperature()
    {
        if (GameQuery.IsWinter) return IslandWinter;
        if (GameQuery.IsSummer)
        {
            float hour = GameQuery.HourOfDay;
            if (hour >= MiddayFrom && hour < MiddayTo) return IslandMild + IslandMiddayExtra;
        }
        return IslandMild;
    }

    private static void Apply(GameManager gm, string key, GameStat model, float wanted)
    {
        if (model == null)
        {
            WarnOnce("stat-missing-" + key, $"[ClimateService] GameStat {key} did not resolve, so its climate offset is not applied.");
            return;
        }
        if (!gm.StatsDict.TryGetValue(model, out var live) || live == null) return;

        if (!_applied.TryGetValue(key, out var rec)) _applied[key] = rec = new Applied();
        // A different (or destroyed) InGameStat means a new run: its GlobalModifiedValue started at zero.
        if (rec.Stat != live)
        {
            rec.Stat = live;
            rec.Amount = 0f;
        }

        float delta = wanted - rec.Amount;
        if (Mathf.Approximately(delta, 0f)) return;

        var modifier = new StatModifier
        {
            Stat = model,
            ValueModifier = Vector2.one * delta,
        };
        gm.ApplyStatModifier(modifier, StatModification.GlobalModifier, "Far Territory climate", null);
        rec.Amount = wanted;
        Plugin.ModLog.LogInfo($"[ClimateService] {model.name}: climate offset now {wanted:0.##} (changed by {delta:0.##}).");
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Plugin.ModLog.LogWarning(message);
    }
}
