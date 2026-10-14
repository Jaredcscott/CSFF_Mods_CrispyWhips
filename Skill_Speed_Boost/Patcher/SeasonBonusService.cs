using BepInEx.Logging;
using CSFFModFramework.Api;

namespace Skill_Speed_Boost.Patcher;

/// <summary>
/// The season SeasonBonusMap is looked up by (plan C1). GameQuery.CurrentSeason reads
/// GameManager.CurrentSeason.SeasonID, one of Spring, Summer, Autumn or Winter (SeasonsSettings,
/// EA 0.68b), and returns null rather than throwing when it cannot, so every failure here is a
/// non-throwing return that warns once per cause (root CLAUDE.md "Silent Catch Blocks"): a dead
/// season bonus would otherwise read exactly like a season the map does not list.
/// </summary>
internal static class SeasonBonusService
{
    private static ManualLogSource Logger => Plugin.Logger;

    // The caller runs on every skill XP gain and the season changes once in many in-game days, so a
    // one-second cache costs nothing in accuracy and keeps the hot path off reflection.
    private const float CacheSeconds = 1.0f;
    private static float _readAtRealtime = float.NegativeInfinity;
    private static string _season;

    private static readonly HashSet<string> _warnedCauses = new(StringComparer.Ordinal);

    /// <summary>The current season's name, or null when it cannot be read (then no season bonus applies).</summary>
    internal static string CurrentSeason()
    {
        float now = Time.realtimeSinceStartup;
        if (now - _readAtRealtime < CacheSeconds) return _season;
        _readAtRealtime = now;
        _season = ReadSeason();
        return _season;
    }

    private static string ReadSeason()
    {
        string season;
        try
        {
            season = GameQuery.CurrentSeason;
        }
        catch (Exception ex)
        {
            WarnOnce("throw", $"Reading the current season failed: {ex.InnerException?.Message ?? ex.Message}");
            return null;
        }

        if (string.IsNullOrEmpty(season))
        {
            WarnOnce("null", "The current season could not be read while a skill gained XP (GameQuery.CurrentSeason returned nothing). " +
                "The game has probably renamed GameManager.CurrentSeason or Season.SeasonID.");
            return null;
        }

        if (!ScopedBonusConfig.IsSeasonName(season))
            WarnOnce("unknown:" + season, $"The game reports the season as '{season}', which is not Spring, Summer, Autumn or Winter, so no SeasonBonusMap entry can match it.");

        return season;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warnedCauses.Add(cause))
            Logger?.LogWarning($"[SeasonBonus] {message} No season bonus applies.");
    }
}
