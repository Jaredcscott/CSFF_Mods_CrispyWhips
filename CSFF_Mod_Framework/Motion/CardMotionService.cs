using CSFFModFramework.Util;
using UnityEngine.UI;

namespace CSFFModFramework.Motion;

/// <summary>
/// Puts a <see cref="CardMotionEffect"/> on a card visual's art images when the sprite they show
/// has a motion entry. Called from <c>CardGraphics.Setup</c> (every card a visual shows, pooled
/// visuals included) and <c>InGameNPC.RefreshPortrait</c> (an NPC's portrait is written onto its
/// card's images after Setup). An image that already carries the effect is only refreshed, so a
/// pooled visual that moves on to a still card stops moving.
/// </summary>
internal static class CardMotionService
{
    public static bool Enabled = true;

    public static bool HasDefinitions => CardMotionLoader.BySprite.Count > 0;

    private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>CardGraphics.Setup postfix body.</summary>
    public static void OnCardSetup(object cardGraphics)
    {
        if (!Enabled || cardGraphics is not CardGraphics g || !g) return;
        Apply(g.CardImage);
        Apply(g.AboveTitleCardImage);
    }

    /// <summary>InGameNPC.RefreshPortrait postfix body.</summary>
    public static void OnPortraitRefreshed(object npc)
    {
        if (!Enabled || npc is not InGameNPC n || !n) return;
        var card = n.AssociatedCard;
        if (!card || !card.CardVisuals) return;
        Apply(card.CardVisuals.CardImage);
        Apply(card.CardVisuals.AboveTitleCardImage);
    }

    // EncounterPopup.EncounterIllustration is private (.decomp/EncounterPopup.cs); StartEncounter writes
    // the encounter's picture onto it. Looked up once; a miss is logged once and the popup stays still.
    private static FieldInfo _illustrationField;
    private static bool _illustrationLookedUp;

    /// <summary>EncounterPopup.StartEncounter postfix body: the picture of the animal or person met.</summary>
    public static void OnEncounterStarted(object popup)
    {
        if (!Enabled || popup == null) return;
        if (!_illustrationLookedUp)
        {
            _illustrationLookedUp = true;
            _illustrationField = AccessTools.Field(popup.GetType(), "EncounterIllustration");
            if (_illustrationField == null)
                Log.Warn("CardMotionService: EncounterPopup.EncounterIllustration not found; encounter pictures will not move.");
        }
        if (_illustrationField?.GetValue(popup) is Image image) Apply(image);
    }

    private static void Apply(Image image)
    {
        if (!image) return;
        var effect = image.GetComponent<CardMotionEffect>();
        if (effect == null)
        {
            var s = image.overrideSprite;
            if (s == null || !CardMotionLoader.BySprite.ContainsKey(s.name)) return;
            try
            {
                effect = image.gameObject.AddComponent<CardMotionEffect>();
            }
            catch (Exception ex)
            {
                if (Warned.Add("add")) Log.Warn($"CardMotionService: could not add the motion effect to a card image; card motion off for it: {ex.Message}");
                return;
            }
        }
        effect.Refresh();
    }
}
