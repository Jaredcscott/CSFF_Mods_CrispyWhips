using System.Security.Cryptography;
using System.Text;

namespace CSFFModFramework.Animals;

/// <summary>
/// Deterministic UniqueID derivation for framework-generated animal SOs.
///
/// <para>Every generated object gets MD5("csffmfw:animal:&lt;SpeciesId&gt;:&lt;part&gt;") as its
/// 32-hex UniqueID. Stable UIDs make rebuilds save-safe (the engine persists duty by UniqueID +
/// action by index) and make GameSourceModify the field-level override surface for generated
/// objects. The part-name registry is documented in Documentation/Design/Animals_Schema.md —
/// changing a part name is a save-breaking change for that object.</para>
/// </summary>
internal static class AnimalUid
{
    public const string PartAgent = "agent";

    // Generated NPCStats (internal agent stats; the world-visible GameStat mirrors land in M5).
    public const string PartStatExists = "npcstat_exists";
    public const string PartStatRespawn = "npcstat_respawn";
    public const string PartStatSuppressRespawn = "npcstat_suppress_respawn";

    // Generated NPCDuties. CustomDuties use PartDutyCustomPrefix + the entry's Name
    // (renaming a CustomDuty = a new UID; the engine tolerates the dropped in-flight duty).
    public const string PartDutySeekPlayer = "duty_seekplayer";
    public const string PartDutyWander = "duty_wander";
    public const string PartDutyRoost = "duty_roost";
    public const string PartDutyCustomPrefix = "duty_custom_";

    public static string For(string speciesId, string part)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"csffmfw:animal:{speciesId}:{part}"));
        var sb = new StringBuilder(32);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
