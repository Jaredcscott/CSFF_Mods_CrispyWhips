using CSFFModFramework.Api;
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
    // Durability slots, indexed by DurabilityConditionDef.DurabilityType
    // -------------------------------------------------------------------------
    //
    // A slot's live value is a float on InGameCardBase; its Active flag and MaxValue sit on the
    // CardData's DurabilityStat. The liquid slot has no DurabilityStat: its maximum is
    // CardData.MaxLiquidCapacity. Before 2.26.4 this read InGameCardBase fields named after the
    // CardData stats plus an "InGameDurability" type, none of which exist, and looked CardModel up
    // as a field although it is an auto-property; every GIF lookup returned null.
    // All reads go through Reflect (property, then field).

    private static readonly string[] DurabilityStatNames = new[]
    {
        "SpoilageTime",       // 0
        "UsageDurability",    // 1
        "FuelCapacity",       // 2
        "Progress",           // 3
        null,                 // 4 liquid: CardData.MaxLiquidCapacity
        "SpecialDurability1", // 5
        "SpecialDurability2", // 6
        "SpecialDurability3", // 7
        "SpecialDurability4", // 8
    };

    private static readonly string[] CurrentValueNames = new[]
    {
        "CurrentSpoilage", "CurrentUsageDurability", "CurrentFuel", "CurrentProgress",
        "CurrentLiquidQuantity", "CurrentSpecial1", "CurrentSpecial2", "CurrentSpecial3", "CurrentSpecial4",
    };

    private static readonly HashSet<string> _warned = new();

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
            Log.Debug($"GifAnimationService.OnCardSetup: {Log.ExceptionText(ex)}");
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
            Log.Debug($"GifAnimationService.OnRefreshCookingStatus: {Log.ExceptionText(ex)}");
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
        if (idx < 0 || idx >= CurrentValueNames.Length) return false;

        if (!Reflect.TryGetMember(inGameCard, "CardModel", out var model))
        {
            WarnOnce("CardModel-missing", $"{inGameCard.GetType().Name}.CardModel not found; GIF condition sets inactive.");
            return false;
        }
        if (model == null) return false;

        float max;
        var statName = DurabilityStatNames[idx];
        if (statName == null)
        {
            max = Reflect.GetFloat(model, "MaxLiquidCapacity");
        }
        else
        {
            if (!Reflect.TryGetMember(model, statName, out var stat) || stat == null)
            {
                WarnOnce(statName + "-missing", $"{model.GetType().Name}.{statName} not found; GIF condition on slot {idx} inactive.");
                return false;
            }
            if (Reflect.GetMember(stat, "Active") is bool active && !active) return false;
            max = Reflect.GetFloat(stat, "MaxValue");
        }
        if (max <= 0f) return false;

        var currentName = CurrentValueNames[idx];
        if (!Reflect.TryGetMember(inGameCard, currentName, out var current) || current == null)
        {
            WarnOnce(currentName + "-missing", $"{inGameCard.GetType().Name}.{currentName} not found; GIF condition on slot {idx} inactive.");
            return false;
        }

        float normalized = Mathf.Clamp01(Convert.ToSingle(current) / max);
        return normalized >= dc.MinNormalized && normalized <= dc.MaxNormalized;
    }

    private static string GetCardUniqueId(object inGameCard)
    {
        if (inGameCard == null) return null;

        // CardModel is an auto-property on InGameCardBase ({ get; private set; }), so it must be
        // read as a property: a field lookup returns null for it.
        if (!Reflect.TryGetMember(inGameCard, "CardModel", out var cardData))
        {
            WarnOnce("CardModel-missing", $"{inGameCard.GetType().Name}.CardModel not found; GIF animations inactive.");
            return null;
        }
        if (cardData is UniqueIDScriptable uid) return uid.UniqueID;
        return cardData == null ? null : Reflect.GetMember(cardData, "UniqueID") as string;
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause)) Log.Warn($"GifAnimationService: {message}");
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
