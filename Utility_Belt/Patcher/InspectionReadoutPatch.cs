using System.Globalization;

namespace Utility_Belt.Patcher;

/// <summary>
/// [Display] Show Capacity And Warmth Readouts (plan B3). Adds one short line under the description of the
/// inspection popup: a container's weight and liquid against capacity, in numbers (vanilla shows only a bar
/// and a percentage), or the warmth a bed or cover gives while its user sleeps. Off by default (plan Q9).
/// </summary>
/// <remarks>
/// <para>Hooks: a postfix on <c>InspectionPopup.SetupInventory</c> (the large method Setup calls for every
/// inspected card and <c>RefreshInventory</c> calls whenever the open card's contents change) and one on
/// <c>RefreshDescription</c> (which rewrites the whole text, for example from <c>RefreshGroupActions</c>).
/// Neither is a small helper the JIT may inline into an already-patched caller (CLAUDE.md "Harmony Patching
/// Pitfalls").</para>
/// <para>Shared object: the popup has ONE DescriptionText reused for every card. The only state this patch
/// writes on it is the text itself (never the font size, auto-size, colour or rect, so TMP's computed size
/// can not be captured wrong), and it records the exact suffix it appended. The next call strips that suffix
/// by equality before doing anything else, and any game write of the whole text (a new card's Setup, a
/// refresh) drops the record, so a line can never outlive the card it was computed for and a card the patch
/// does not apply to is never touched. The one other write is SetActive(true) when the description was empty
/// and hidden; the game's own RefreshDescription re-derives that flag from the text on the next write.</para>
/// <para>Warmth is read from the card's own data: a <c>PassiveEffect</c> on its CardData that has a
/// <c>StatModifier</c> on TemperaturePerceived and a <c>Conditions.RequiredStatValues</c> entry on SleepClock
/// (the vanilla beds, the improvised shelter and the leather covers all gate their warmth that way; worn
/// clothing does not, so it shows no line). Nothing is hard-coded per card.</para>
/// </remarks>
internal static class InspectionReadoutPatch
{
    // Vanilla GameStat GUIDs (Documentation/GameData/CSFF-JsonData_Current/UniqueIDScriptableGUID/GameStat.json).
    private const string TemperaturePerceivedUid = "0d5ec8f104b2ba149b4bb2b536bc21a4";
    private const string SleepClockUid = "ca25b2c02ece6674bae6aaba4a6b8c10";

    // The exact suffix (newline plus line) this patch last appended to the shared description text.
    private static string _appended;
    private static readonly HashSet<string> _warned = new();

    internal static void ApplyPatch(Harmony harmony)
    {
        var setup = AccessTools.Method(typeof(InspectionPopup), "SetupInventory");
        if (setup == null)
        {
            Plugin.Logger.LogWarning("[UB] InspectionPopup.SetupInventory not found; the capacity and warmth readouts are off.");
            return;
        }

        harmony.Patch(setup, postfix: new HarmonyMethod(typeof(InspectionReadoutPatch), nameof(SetupInventoryPostfix)));

        var refresh = AccessTools.Method(typeof(InspectionPopup), nameof(InspectionPopup.RefreshDescription));
        if (refresh == null)
        {
            Plugin.Logger.LogWarning("[UB] InspectionPopup.RefreshDescription not found; a readout can disappear until the popup next refreshes its contents.");
            return;
        }

        harmony.Patch(refresh, postfix: new HarmonyMethod(typeof(InspectionReadoutPatch), nameof(RefreshDescriptionPostfix)));
    }

    private static void SetupInventoryPostfix(InspectionPopup __instance, InGameCardBase _Card)
    {
        Apply(__instance, _Card);
    }

    private static void RefreshDescriptionPostfix(InspectionPopup __instance, string _WithText)
    {
        // A non-empty _WithText is the game writing a whole new text (Setup's CommonSetup passes the card's
        // description before CurrentCard is set): nothing of ours is left in it.
        if (!string.IsNullOrEmpty(_WithText))
        {
            _appended = null;
            return;
        }

        Apply(__instance, __instance.CurrentCard);
    }

    private static void Apply(InspectionPopup popup, InGameCardBase card)
    {
        try
        {
            var tmp = popup != null ? popup.DescriptionText : null;
            if (tmp == null)
            {
                WarnOnce("no-description-text", "[UB] InspectionPopup.DescriptionText is null; the capacity and warmth readouts are skipped.");
                return;
            }

            var text = tmp.text ?? string.Empty;
            var stripped = false;
            if (_appended != null && text.EndsWith(_appended, StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - _appended.Length);
                stripped = true;
            }

            _appended = null;

            var line = Plugin.ShowReadouts.Value ? BuildLine(card) : null;
            if (line == null)
            {
                if (stripped)
                {
                    tmp.text = text;
                }

                return;
            }

            var suffix = (text.Length > 0 ? "\n" : string.Empty) + line;
            tmp.text = text + suffix;
            _appended = suffix;
            if (!tmp.gameObject.activeSelf)
            {
                tmp.gameObject.SetActive(true);
            }
        }
        catch (Exception ex)
        {
            _appended = null;
            WarnOnce("apply-" + ex.GetType().Name, $"[UB] Capacity and warmth readout failed ({ex.GetType().Name}: {ex.Message}); the popup is left as the game drew it.");
        }
    }

    /// <summary>The readout for this card, or null when it has none.</summary>
    internal static string BuildLine(InGameCardBase card)
    {
        if (card == null || card.CardModel == null || card.NPCModel != null)
        {
            return null;
        }

        var parts = new List<string>(2);
        var type = card.CardModel.CardType;
        if (type == CardTypes.Item || type == CardTypes.Base || type == CardTypes.Location)
        {
            var maxWeight = card.MaxWeightCapacity;
            if (maxWeight > 0f)
            {
                parts.Add(OverlayText.Format(OverlayText.ReadoutWeightKey, OverlayText.ReadoutWeightDefault,
                    Num(card.InventoryWeight(true)), Num(maxWeight)));
            }

            // MaxLiquidCapacity > 0 is also set on every garment, tool and arrow (a coating they soak up, drawn
            // as a name suffix and never poured), and on a cow's udder. Only a card a player fills by pouring
            // gets a liquid figure.
            var model = card.CardModel;
            var maxLiquid = model.MaxLiquidCapacity;
            if (maxLiquid > 0f && !model.ShowLiquidAsSuffix && !model.HideLiquidDurabilities && !model.CannotReceiveLiquidWithDragNDrop)
            {
                var current = card.ContainedLiquid != null ? card.ContainedLiquid.CurrentLiquidQuantity : 0f;
                parts.Add(OverlayText.Format(OverlayText.ReadoutLiquidKey, OverlayText.ReadoutLiquidDefault,
                    Num(current), Num(maxLiquid)));
            }
        }

        if (type != CardTypes.Liquid && TrySleepWarmth(card.CardModel, out var warmth))
        {
            parts.Add(OverlayText.Format(OverlayText.ReadoutSleepWarmthKey, OverlayText.ReadoutSleepWarmthDefault, warmth));
        }

        return parts.Count == 0 ? null : string.Join("   ", parts);
    }

    /// <summary>
    /// The warmth the card gives while its user sleeps: the strongest TemperaturePerceived modifier among its
    /// PassiveEffects that are gated on SleepClock. A card with several (one per leather grade, say) shows the
    /// largest; the effect that applies at a given moment can be smaller when its other conditions are not met.
    /// </summary>
    internal static bool TrySleepWarmth(CardData model, out string warmth)
    {
        warmth = null;
        var effects = model.PassiveEffects;
        if (effects == null)
        {
            return false;
        }

        var found = false;
        float lo = 0f, hi = 0f;
        for (var i = 0; i < effects.Length; i++)
        {
            var effect = effects[i];
            if (effect.StatModifiers == null || !IsGatedOnSleep(effect.Conditions))
            {
                continue;
            }

            for (var j = 0; j < effect.StatModifiers.Length; j++)
            {
                var mod = effect.StatModifiers[j];
                if (mod.Stat == null || mod.Stat.UniqueID != TemperaturePerceivedUid)
                {
                    continue;
                }

                var a = Math.Min(mod.ValueModifier.x, mod.ValueModifier.y);
                var b = Math.Max(mod.ValueModifier.x, mod.ValueModifier.y);
                if (Math.Abs(a) < 0.0001f && Math.Abs(b) < 0.0001f)
                {
                    continue;
                }

                if (!found || Math.Abs(b) + Math.Abs(a) > Math.Abs(hi) + Math.Abs(lo))
                {
                    lo = a;
                    hi = b;
                }

                found = true;
            }
        }

        if (!found)
        {
            return false;
        }

        warmth = Math.Abs(hi - lo) < 0.0001f ? Signed(hi) : Signed(lo) + " - " + Signed(hi);
        return true;
    }

    private static bool IsGatedOnSleep(GeneralCondition conditions)
    {
        var needs = conditions.RequiredStatValues;
        if (needs == null)
        {
            return false;
        }

        for (var i = 0; i < needs.Length; i++)
        {
            if (needs[i].Stat != null && needs[i].Stat.UniqueID == SleepClockUid)
            {
                return true;
            }
        }

        return false;
    }

    private static string Num(float value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string Signed(float value)
    {
        return (value > 0f ? "+" : string.Empty) + value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
