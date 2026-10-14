namespace CSFFModFramework.Motion;

/// <summary>
/// How one sprite moves. Every amplitude is a fraction of the drawn art's size, so a value means
/// the same on a tight-cropped animal and on a full 512x768 painting. Zero switches that motion off.
/// <list type="bullet">
/// <item><b>Breathe</b>: the art grows upward from its bottom edge and the middle widens a little,
/// then settles back (never shrinks below its painted size, so no gap opens at the base).</item>
/// <item><b>Sway</b>: the top leans sideways while the bottom stays put (grass, a tail, a head turn).</item>
/// <item><b>Drift</b>: a slow zoom and pan INSIDE the art (texture coordinates only), so the picture
/// never leaves its frame. Meant for full-bleed location paintings.</item>
/// </list>
/// </summary>
internal sealed class CardMotionProfile
{
    public string Name = "";
    public float Breathe;
    public float Sway;
    public float Drift;
    /// <summary>Seconds for one breath or one zoom cycle; sway runs a little slower so the two never lock.</summary>
    public float Period = 4f;

    /// <summary>Breathe and Sway bend the art, so a plain quad is split into a grid first.</summary>
    public bool NeedsGrid => Breathe > 0f || Sway > 0f;
    public bool IsStill => Breathe <= 0f && Sway <= 0f && Drift <= 0f;

    public CardMotionProfile Copy(string name) => new CardMotionProfile
    {
        Name = name, Breathe = Breathe, Sway = Sway, Drift = Drift, Period = Period,
    };

    /// <summary>
    /// The built-in profiles a definition can name instead of spelling the numbers out.
    /// Subtle by intent (owner, 2026-10-14: "subtle annimations", "minimize the over head").
    /// </summary>
    public static CardMotionProfile BuiltIn(string name)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "npc":      return new CardMotionProfile { Name = "npc", Breathe = 0.012f, Sway = 0.006f, Period = 4.4f };
            case "animal":   return new CardMotionProfile { Name = "animal", Breathe = 0.018f, Sway = 0.004f, Period = 3.0f };
            case "location": return new CardMotionProfile { Name = "location", Drift = 0.03f, Period = 30f };
            case "plant":    return new CardMotionProfile { Name = "plant", Sway = 0.015f, Period = 5.0f };
            case "still":    return new CardMotionProfile { Name = "still" };
            default:         return null;
        }
    }
}
