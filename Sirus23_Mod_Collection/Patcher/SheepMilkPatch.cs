using System;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Sirus23ModCollection.Patcher;

/// <summary>
/// Drains the Lactating Sheep's milk (her Usage stat) by 2.5 after each "Milk" action, and turns a
/// ewe milked dry back into a Tame Sheep.
///
/// WHY C#: "Milk" (LactatingSheep.json CardInteractions[2]) fills the dragged bowl through
/// CreatedLiquidInGivenCard. It used to drain Usage itself with ReceivingCardChanges ModType 1 +
/// UsageChange -2.5, and that shape throws in WikiMod's drag tooltip: WikiMod 3.5.4
/// InGameCardBaseMod.ApplyDetailedTooltip swaps the receiving card for instance.ContainedLiquid
/// whenever the action CanGiveLiquid to the dragged bowl, the sheep holds no liquid, so
/// TooltipFormatter.FormatCardStateChange reads card.CardModel on a null card for any non-zero
/// change (NullReferenceException, caught and logged by WikiMod; the tooltip loses its card
/// changes). The same shape ACT's Tea Station hit (TeaStationPatch DrainWaterCharge). All 14 vanilla
/// CreatedLiquidInGivenCard interactions keep ModType 0 on both sides, so the JSON is ModType 0
/// now and the drain is done here. Gate: Development_Tools/Tests/Mods-LiquidFillModType.Tests.ps1.
///
/// The action's RequiredReceivingDurabilities (Usage at least 50% of 5) still gates it, so Usage
/// is at least 2.5 when this runs; the drain is clamped at 0 anyway.
///
/// DRY-OFF: the card's Usage OnZero is a Transform into sh_tame_sheep (ModType 2, TransferSpoilage).
/// A direct field write never fires OnZero (the game fires it only from ModifyDurability, on the
/// step that crosses zero), so a milking that leaves less than <see cref="DryOffMargin"/> does the
/// same swap here with the framework's CardUtil.TransformCardInPlace. The margin is there because
/// milk keeps coming during the action's own time, so a ewe milked from exactly half is left a few
/// hundredths above zero rather than at it; 0.05 is a first guess. The in-place swap keeps every
/// stat value (Nourishment as the game's TransferSpoilage would, and Wool and Poop, which the game's
/// Transform would reset to a Tame Sheep's defaults: a full fleece for free); Pregnancy, which a
/// Lactating Sheep does not have, is set to 0, a Tame Sheep's own start.
/// </summary>
internal static class SheepMilkPatch
{
    private const string LactatingSheepUid = "sh_lactating_sheep";
    internal const string TameSheepUid = "sh_tame_sheep";
    private const string MilkActionKey = "sh_lactating_sheep_CardInteractions[2].ActionName";
    private const string MilkActionName = "Milk";
    internal const float MilkUsageCost = 2.5f;
    internal const float DryOffMargin = 0.05f;

    public static void Register()
    {
        ActionRouter.Register(new ActionHandler
        {
            Name = "SheepMilk",
            CardUid = LactatingSheepUid,
            ActionKeyPrefix = MilkActionKey,
            ActionNamePrefix = MilkActionName,
            Timing = ActionTiming.AfterWrapped,
            After = ctx => DrainMilk(ctx.Card),
        });
    }

    private static void DrainMilk(object sheepCard)
    {
        try
        {
            if (sheepCard == null)
            {
                Plugin.Logger.LogWarning("[SheepMilk] Milk fired with no sheep card; Usage not drained");
                return;
            }
            object raw = CardUtil.GetMemberValue(sheepCard, "CurrentUsageDurability");
            if (raw == null)
            {
                Plugin.Logger.LogError("[SheepMilk] CurrentUsageDurability could not be read; the ewe's milk was not drained");
                return;
            }
            float before = CardUtil.ToFloat(raw);
            float delta = -Math.Min(MilkUsageCost, Math.Max(0f, before));
            if (delta == 0f) return;
            if (!CardUtil.ModifyDurabilityStat(sheepCard, "CurrentUsageDurability", delta))
            {
                Plugin.Logger.LogError("[SheepMilk] CurrentUsageDurability not found; the ewe's milk was not drained");
                return;
            }
            float after = before + delta;
            Plugin.Logger.LogDebug($"[SheepMilk] Usage {before} -> {after}");
            if (after < DryOffMargin) DryOff(sheepCard, after);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"[SheepMilk] drain failed: {ex}");
        }
    }

    private static void DryOff(object sheepCard, float usageLeft)
    {
        if (!CardUtil.SetDurability(sheepCard, "UsageDurability", 0f))
            Plugin.Logger.LogWarning("[SheepMilk] a dried-off ewe's Usage could not be zeroed before the swap; she keeps whatever the slot held");
        if (!CardUtil.TransformCardInPlace(sheepCard, TameSheepUid))
        {
            Plugin.Logger.LogError($"[SheepMilk] a ewe milked dry (Usage {usageLeft}) could not become '{TameSheepUid}'; she stays a Lactating Sheep");
            return;
        }
        if (!CardUtil.SetDurability(sheepCard, "SpecialDurability4", 0f))
            Plugin.Logger.LogWarning("[SheepMilk] the dried-off ewe's Pregnancy could not be set to 0; she keeps whatever the slot held");
        Plugin.Logger.LogInfo($"[SheepMilk] a ewe milked dry (Usage {usageLeft}) is a Tame Sheep again");
    }
}
