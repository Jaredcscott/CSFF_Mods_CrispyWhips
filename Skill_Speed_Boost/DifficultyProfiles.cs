using System;
using System.Collections.Generic;

namespace Skill_Speed_Boost;

/// <summary>
/// Named difficulty presets that flip SkillExpMultiplier + EnableSkillStaleness together.
/// Applied via the ActiveProfile config entry; changes take effect after save reload.
/// </summary>
internal static class DifficultyProfiles
{
    // profile name → (globalExpMultiplier, enableStaleness)
    private static readonly Dictionary<string, (int expMult, bool staleness)> _profiles
        = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VanillaPlus"] = (2,  true),   // slight boost, vanilla feel
        ["Casual"]      = (3,  false),  // relaxed grinding, no staleness penalties
        ["Hardcore"]    = (1,  true),   // vanilla difficulty
        ["Grinder"]     = (10, false),  // testing / sandbox
        ["Balanced"]    = (2,  true),   // recommended: modest boost + natural decay
        ["Legacy"]      = (1,  true),   // original vanilla (same as Hardcore alias)
    };

    public static IEnumerable<string> ProfileNames => _profiles.Keys;

    public static void ApplyProfileSettings(string profileName)
    {
        if (string.IsNullOrEmpty(profileName) || profileName == "None") return;
        if (!_profiles.TryGetValue(profileName, out var settings)) return;

        var (expMult, staleness) = settings;
        Plugin.SetGlobalExpMultiplier(expMult);
        Plugin.SetEnableSkillStaleness(staleness);
        Plugin.Logger.LogInfo(
            $"[DifficultyProfiles] Applied '{profileName}': ExpMultiplier={expMult}x, Staleness={staleness}. " +
            "Reload save to take effect."
        );
    }
}
