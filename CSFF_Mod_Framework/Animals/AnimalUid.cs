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

    public static string For(string speciesId, string part)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"csffmfw:animal:{speciesId}:{part}"));
        var sb = new StringBuilder(32);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
