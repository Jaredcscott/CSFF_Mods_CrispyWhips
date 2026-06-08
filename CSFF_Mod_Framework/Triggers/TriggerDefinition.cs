namespace CSFFModFramework.Triggers;

/// <summary>
/// A parsed spawn trigger from a mod's <c>CardData/Trigger/*.json</c> file.
/// Compatible with the ModCore spawn-trigger schema used by SheepHusbandry and similar mods.
/// </summary>
internal class TriggerDefinition
{
    public string UniqueID;
    public string DisplayName;

    /// <summary>UniqueID of the <c>CardData</c> to spawn (from <c>TriggerCardWarpData</c>).</summary>
    public string TriggerCardUID;

    /// <summary>Spawn probability as a percent integer (0–100).</summary>
    public int SpawnChancePercent;

    /// <summary>1 = spawn on the board in the player's current environment.</summary>
    public int SpawnLocation;

    /// <summary>How often to attempt a spawn, in DTP units (96 DTP = 1 in-game day).</summary>
    public int TriggerFrequency;

    /// <summary>Suppress spawning when this many matching cards are already on the player's board.</summary>
    public int MaxOnBoard;

    /// <summary>Additional card UIDs that count toward the <see cref="MaxOnBoard"/> cap.</summary>
    public List<string> MaxOnBoardUIDs = new();

    /// <summary>The mod that owns this trigger definition (for log output).</summary>
    public string ModName;

    // ── Runtime state (not persisted; resets on process restart) ──

    /// <summary>Day rollovers accumulated since this trigger last fired.</summary>
    public float DaysAccumulated;

    /// <summary>Fires every N in-game days: <c>TriggerFrequency / 96f</c>.</summary>
    public float DaysBetweenFires => TriggerFrequency > 0 ? TriggerFrequency / 96f : float.MaxValue;
}
