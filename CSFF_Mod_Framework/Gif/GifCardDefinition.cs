namespace CSFFModFramework.Gif;

/// <summary>
/// Describes a single GIF animation reference (file name + loop flag).
/// Resolved by name against the framework's GIF index built from <c>Resource/GIF/</c>.
/// </summary>
public class GifPlayDef
{
    public string GifName = "";
    public bool Loop = true;
}

/// <summary>
/// Normalized percentage condition on a durability slot.
/// DurabilityType values:
///   0=Spoilage, 1=Usage, 2=Fuel, 3=Progress, 4=Liquid,
///   5=Special1, 6=Special2, 7=Special3, 8=Special4
/// </summary>
public class DurabilityConditionDef
{
    public int DurabilityType;
    public float MinNormalized = 0f;
    public float MaxNormalized = 1f;
}

/// <summary>
/// A conditional GIF override: play Gif when ALL conditions are met.
/// </summary>
public class ConditionSetDef
{
    public GifPlayDef Gif;
    public List<DurabilityConditionDef> DurabilityConditions = new();
}

/// <summary>
/// Full GIF definition for one card, loaded from CardData/Gif/*.json.
/// Identifies the target card by UniqueID (not Unity object ref).
/// </summary>
public class GifCardDefinition
{
    /// <summary>UniqueID of the CardData this definition applies to.</summary>
    public string CardUniqueId = "";

    /// <summary>Main card face animation.</summary>
    public GifPlayDef CardGif;

    /// <summary>Card background panel animation.</summary>
    public GifPlayDef CardBackgroundGif;

    /// <summary>Animation shown while the card is cooking.</summary>
    public GifPlayDef CookingGif;

    /// <summary>Default liquid slot animation.</summary>
    public GifPlayDef DefaultLiquidGif;

    /// <summary>Per-liquid-slot animation overrides (index → GifPlayDef).</summary>
    public Dictionary<int, GifPlayDef> LiquidGifs = new();

    /// <summary>Conditional overrides evaluated in order; first match wins.</summary>
    public List<ConditionSetDef> ConditionSets = new();
}
