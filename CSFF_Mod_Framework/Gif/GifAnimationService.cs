using CSFFModFramework.Reflection;
using CSFFModFramework.Util;
using UnityEngine.UI;

namespace CSFFModFramework.Gif;

/// <summary>
/// Runtime service: resolves which GifFrameSet a card should display and drives GifPlayer.
///
/// Durability evaluation:
///   - Evaluates each ConditionSet in order; first set where ALL DurabilityConditions pass wins.
///   - Normalized value = CurrentValue / MaxValue (0..1 range).
///   - If no condition set matches, falls back to CardGif.
///
/// Called from GifAnimationPatch postfixes on CardGraphics.Setup and
/// InGameCardBase.RefreshCookingStatus.
/// </summary>
internal static class GifAnimationService
{
    // -------------------------------------------------------------------------
    // Reflection cache for InGameCardBase durability access
    // -------------------------------------------------------------------------

    private static Type _inGameCardBaseType;
    private static FieldInfo _cardDataField;
    private static Type _inGameDurabilityType;

    // Cached field accessors for the six standard durability slots
    private static readonly string[] DurabilityFieldNames = new[]
    {
        "SpoilageTime",     // 0
        "UsageDurability",  // 1
        "FuelCapacity",     // 2
        "Progress",         // 3
        "LiquidCapacity",   // 4
        "SpecialDurability1", // 5
        "SpecialDurability2", // 6
        "SpecialDurability3", // 7
        "SpecialDurability4", // 8
    };

    private static FieldInfo[] _durabilityFields;
    private static FieldInfo _currentValueField;
    private static FieldInfo _maxValueField;
    private static FieldInfo _activeField;

    private static bool _reflectionReady;

    public static bool HasDefinitions => GifLoader.CardDefinitions.Count > 0;

    // -------------------------------------------------------------------------
    // Entry points called from patches
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called from CardGraphics.Setup postfix.
    /// Finds the card Image component, attaches/updates a GifPlayer, and selects the active GIF.
    /// </summary>
    public static void OnCardSetup(object cardGraphics, object inGameCard)
    {
        if (!HasDefinitions) return;

        try
        {
            var uniqueId = GetCardUniqueId(inGameCard);
            if (uniqueId == null) return;
            if (!GifLoader.CardDefinitions.TryGetValue(uniqueId, out var def)) return;

            var frameSet = ResolveFrameSet(def, inGameCard, isCooking: false);
            if (frameSet == null) return;

            var image = FindCardImage(cardGraphics);
            if (image == null) return;

            ApplyGif(image, frameSet, def.CardGif?.Loop ?? true);
        }
        catch (Exception ex)
        {
            Log.Debug($"GifAnimationService.OnCardSetup: {ex.Message}");
        }
    }

    /// <summary>
    /// Called from InGameCardBase.RefreshCookingStatus postfix.
    /// Switches between CardGif and CookingGif based on cooking state.
    /// </summary>
    public static void OnRefreshCookingStatus(object inGameCard, bool isCooking)
    {
        if (!HasDefinitions) return;

        try
        {
            var uniqueId = GetCardUniqueId(inGameCard);
            if (uniqueId == null) return;
            if (!GifLoader.CardDefinitions.TryGetValue(uniqueId, out var def)) return;

            var targetDef = isCooking ? def.CookingGif : def.CardGif;
            if (targetDef == null) return;

            if (!GifLoader.GifFrameSets.TryGetValue(targetDef.GifName, out var frameSet)) return;
            frameSet.Loop = targetDef.Loop;

            var image = FindCardImage(inGameCard);
            if (image == null) return;

            ApplyGif(image, frameSet, targetDef.Loop);
        }
        catch (Exception ex)
        {
            Log.Debug($"GifAnimationService.OnRefreshCookingStatus: {ex.Message}");
        }
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private static GifFrameSet ResolveFrameSet(GifCardDefinition def, object inGameCard, bool isCooking)
    {
        // Cooking override takes highest priority
        if (isCooking && def.CookingGif != null &&
            GifLoader.GifFrameSets.TryGetValue(def.CookingGif.GifName, out var cookFs))
        {
            cookFs.Loop = def.CookingGif.Loop;
            return cookFs;
        }

        // Evaluate condition sets in order — first matching set wins
        foreach (var cs in def.ConditionSets)
        {
            if (cs.Gif == null || !GifLoader.GifFrameSets.TryGetValue(cs.Gif.GifName, out var csFs)) continue;
            if (ConditionsPass(cs, inGameCard))
            {
                csFs.Loop = cs.Gif.Loop;
                return csFs;
            }
        }

        // Fall back to base CardGif
        if (def.CardGif != null && GifLoader.GifFrameSets.TryGetValue(def.CardGif.GifName, out var baseFs))
        {
            baseFs.Loop = def.CardGif.Loop;
            return baseFs;
        }

        return null;
    }

    private static bool ConditionsPass(ConditionSetDef cs, object inGameCard)
    {
        if (cs.DurabilityConditions.Count == 0) return true;

        EnsureReflection(inGameCard);
        if (!_reflectionReady) return false;

        foreach (var dc in cs.DurabilityConditions)
        {
            if (!DurabilityConditionPasses(dc, inGameCard))
                return false;
        }

        return true;
    }

    private static bool DurabilityConditionPasses(DurabilityConditionDef dc, object inGameCard)
    {
        int idx = dc.DurabilityType;
        if (_durabilityFields == null || idx < 0 || idx >= _durabilityFields.Length) return false;
        if (_durabilityFields[idx] == null) return false;

        var durability = _durabilityFields[idx].GetValue(inGameCard);
        if (durability == null) return false;

        // Check Active flag
        if (_activeField != null)
        {
            var active = _activeField.GetValue(durability);
            if (active is bool b && !b) return false;
        }

        if (_currentValueField == null || _maxValueField == null) return false;

        float current = Convert.ToSingle(_currentValueField.GetValue(durability));
        float max     = Convert.ToSingle(_maxValueField.GetValue(durability));
        if (max <= 0f) return false;

        float normalized = Mathf.Clamp01(current / max);
        return normalized >= dc.MinNormalized && normalized <= dc.MaxNormalized;
    }

    private static void EnsureReflection(object inGameCard)
    {
        if (_reflectionReady) return;

        _inGameCardBaseType = inGameCard?.GetType();
        if (_inGameCardBaseType == null) return;

        // Walk up to InGameCardBase if we have a subclass
        var t = _inGameCardBaseType;
        while (t != null && t.Name != "InGameCardBase") t = t.BaseType;
        if (t != null) _inGameCardBaseType = t;

        // Find CardData field
        _cardDataField = AccessTools.Field(_inGameCardBaseType, "CardModel")
                      ?? AccessTools.Field(_inGameCardBaseType, "CardData")
                      ?? AccessTools.Field(_inGameCardBaseType, "_cardData");

        // Find durability fields (InGameDurability instances on InGameCardBase)
        _durabilityFields = new FieldInfo[DurabilityFieldNames.Length];
        for (int i = 0; i < DurabilityFieldNames.Length; i++)
            _durabilityFields[i] = AccessTools.Field(_inGameCardBaseType, DurabilityFieldNames[i]);

        // Find InGameDurability's CurrentValue and MaxValue fields via first found slot
        FieldInfo sample = _durabilityFields.FirstOrDefault(f => f != null);
        if (sample != null)
        {
            _inGameDurabilityType = sample.FieldType;
            _currentValueField = AccessTools.Field(_inGameDurabilityType, "CurrentValue")
                              ?? AccessTools.Field(_inGameDurabilityType, "currentValue");
            _maxValueField     = AccessTools.Field(_inGameDurabilityType, "MaxValue")
                              ?? AccessTools.Field(_inGameDurabilityType, "maxValue");
            _activeField       = AccessTools.Field(_inGameDurabilityType, "Active")
                              ?? AccessTools.Field(_inGameDurabilityType, "active");
        }

        _reflectionReady = true;
    }

    private static string GetCardUniqueId(object inGameCard)
    {
        if (inGameCard == null) return null;

        EnsureReflection(inGameCard);

        // Try to get CardData.UniqueID via the CardModel/CardData field
        if (_cardDataField != null)
        {
            var cardData = _cardDataField.GetValue(inGameCard);
            if (cardData is UniqueIDScriptable uid)
                return uid.UniqueID;
            if (cardData != null)
            {
                var uidProp = AccessTools.Property(cardData.GetType(), "UniqueID");
                return uidProp?.GetValue(cardData) as string;
            }
        }

        // Fallback: look for a UniqueID property/field directly
        var uidField = AccessTools.Field(inGameCard.GetType(), "UniqueID");
        return uidField?.GetValue(inGameCard) as string;
    }

    // -------------------------------------------------------------------------
    // Image discovery — searches CardGraphics and its hierarchy for the card art Image
    // -------------------------------------------------------------------------

    // Candidate field names for the main card art Image on CardGraphics
    private static readonly string[] CardImageFieldNames = new[]
    {
        "CardImage", "CardArt", "UseSprite", "ArtImage", "MainImage",
        "CardGraphicsImage", "spriteImage", "Image",
    };

    private static Image FindCardImage(object obj)
    {
        if (obj == null) return null;

        var t = obj.GetType();

        // 1. Try named fields on the object
        foreach (var name in CardImageFieldNames)
        {
            var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi == null) continue;
            var val = fi.GetValue(obj);
            if (val is Image img) return img;
            // Could be a Component — check if it has an Image
            if (val is Component comp)
            {
                var ci = comp.GetComponent<Image>();
                if (ci != null) return ci;
            }
        }

        // 2. If obj is a Component, search its own GameObject and children
        if (obj is Component c)
        {
            // Self
            var selfImg = c.GetComponent<Image>();
            if (selfImg != null) return selfImg;
            // Children — pick the first one named "CardImage" or the very first Image child
            Image firstChild = null;
            foreach (var img in c.GetComponentsInChildren<Image>(includeInactive: true))
            {
                if (img.gameObject.name.Equals("CardImage", StringComparison.OrdinalIgnoreCase))
                    return img;
                firstChild ??= img;
            }
            return firstChild;
        }

        return null;
    }

    private static void ApplyGif(Image image, GifFrameSet frameSet, bool loop)
    {
        if (image == null || frameSet == null) return;

        var player = image.GetComponent<GifPlayer>();
        if (player == null) player = image.gameObject.AddComponent<GifPlayer>();

        frameSet.Loop = loop;
        player.SetFrameSet(frameSet);
    }
}
