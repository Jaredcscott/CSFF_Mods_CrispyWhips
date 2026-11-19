using CSFFModFramework.Util;

namespace CSFFModFramework.Animals;

/// <summary>
/// The animals module's guard for a game field it reaches by name string.
///
/// <para><c>AccessTools.Field</c> returns null, not an exception, when a game update renames the field, and a
/// <c>field?.SetValue(...)</c> on that null does nothing at all: the generated agent, duty or action is built
/// with the game's default in the slot and nothing in the log says so. Every reflected write in this module
/// goes through <see cref="Present"/> first, so a rename reads as one Error line naming the member and what
/// the player loses, and the caller skips or refuses instead of building a half-set object.</para>
///
/// <para>One line per member per process: the builders run once per species per load, and a missing field is
/// missing for all of them.</para>
/// </summary>
internal static class ReflectedField
{
    private static readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>
    /// True when <paramref name="field"/> resolved. Otherwise logs one Error for <paramref name="member"/>
    /// ("Type.Field"), with <paramref name="consequence"/> saying what is skipped, and returns false.
    /// </summary>
    internal static bool Present(FieldInfo field, string member, string consequence)
    {
        if (field != null) return true;
        if (_reported.Add(member))
            Log.Error($"Animals: {member} not found (renamed by a game update?) - {consequence}");
        return false;
    }
}
