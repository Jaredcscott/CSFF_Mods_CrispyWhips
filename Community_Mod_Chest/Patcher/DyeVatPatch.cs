using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace CommunityModChest.Patcher
{
    /// <summary>
    /// The Dye Vat (CMC round 2, plan feature H1). A storage-first station: the vat is a CT2 card with
    /// ordinary InventorySlots and three plain DismantleActions (Dye Charcoal, Dye Ochre, Dye Billberry),
    /// and it carries NO contained operation blueprints (root CLAUDE.md "Blueprints": a station whose
    /// primary use is normal storage plus action buttons uses Harmony handlers that read the station's own
    /// inventory after the timed action).
    ///
    /// Two handlers on the three action keys:
    ///   - a CANCEL gate runs before the time is spent and refuses (with a message, no time passes) when
    ///     the vat holds no plain garment or no pigment of the chosen colour;
    ///   - an AFTER handler runs once the timed action completed: every plain garment in the vat is turned
    ///     into its dyed form, one pigment each, until the pigments run out.
    ///
    /// The swap is CardUtil.TransformCardInPlace (never a destroy: root CLAUDE.md "Runtime Card Removal").
    /// SetModel does NOT reset durabilities, so each durability the garment carried is read BEFORE the swap
    /// and written back AFTER it (the drag CIs do the same with TransferUsage). The pigment is removed with
    /// CardUtil.TryRemoveCard, which drives vanilla's RemoveCard coroutine and so takes the card out of
    /// AllCards. Only the garments listed in <see cref="Garments"/> are touched, so the vat never rewrites a
    /// card it was not asked about, and a garment that is already dyed is not in the table and is skipped.
    ///
    /// Registration runs once per process from Plugin.Awake under an _initialized guard
    /// (CLAUDE.md "Per-Run Event Registration", pattern b): ActionRouter.Register is purely additive.
    /// </summary>
    internal static class DyeVatPatch
    {
        internal const string VatUid = "cmcDyeVat";

        /// <summary>The ActionName.LocalizationKey prefix shared by the three dye buttons; the colour follows it.</summary>
        internal const string ActionKeyPrefix = "CMC_DyeVat_DA_Dye";

        /// <summary>TUNABLE PLACEHOLDER: pigment cards spent per garment dyed.</summary>
        internal const int PigmentPerGarment = 1;

        internal const string NothingToDyeKey = "CMC_DyeVat_NothingToDye";
        internal const string NoPigmentKey = "CMC_DyeVat_NoPigment";

        private enum Colour { Charcoal = 0, Ochre = 1, Billberry = 2 }

        private static readonly string[] ColourSuffixes = { "Charcoal", "Ochre", "Billberry" };

        private static readonly string[] PigmentUids =
        {
            "potterysculpturepigment",
            "potterysculpturepigmentochre",
            "potterysculpturepigmentbillberry",
        };

        /// <summary>plain garment UID -> its dyed form per colour (Charcoal, Ochre, Billberry). Read from the
        /// seven garments' own pigment CardInteractions, so the vat and the drag give the same card.</summary>
        private static readonly Dictionary<string, string[]> Garments = new(StringComparer.OrdinalIgnoreCase)
        {
            ["apparelclothscarf"] = new[] { "apparelclothscarfdyed", "apparelclothscarfdyedochre", "apparelclothscarfdyedbillberry" },
            ["apparelclothcoat"] = new[] { "apparelclothcoatdyed", "apparelclothcoatdyedochre", "apparelclothcoatdyedbillberry" },
            ["apparelclothmask"] = new[] { "apparelclothmaskdyedcharcoal", "apparelclothmaskdyedochre", "apparelclothmaskdyedbillberry" },
            ["apparelchaperon"] = new[] { "apparelchaperondyed", "apparelchaperondyedochre", "apparelchaperondyedbillberry" },
            ["combatandarmorquiltedcap"] = new[] { "combatandarmorquiltedcapdyedcharcoal", "combatandarmorquiltedcapdyedochre", "combatandarmorquiltedcapdyedbillberry" },
            ["apparelstrawhat"] = new[] { "apparelstrawhatdyedcharcoal", "apparelstrawhatdyedochre", "apparelstrawhatdyedbillberry" },
            ["apparelleatherapron"] = new[] { "apparelleatheraprondyedcharcoal", "apparelleatheraprondyedochre", "apparelleatheraprondyedbillberry" },
        };

        private static bool _initialized;
        private static readonly HashSet<string> _warned = new(StringComparer.Ordinal);

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            ActionRouter.Register(new ActionHandler
            {
                Name = "DyeVatGate",
                CardPredicate = ctx => string.Equals(ctx.CardUid, VatUid, StringComparison.OrdinalIgnoreCase),
                ActionKeyPrefix = ActionKeyPrefix,
                Timing = ActionTiming.Cancel,
                Before = DyeGate,
            });
            ActionRouter.Register(new ActionHandler
            {
                Name = "DyeVatDye",
                CardPredicate = ctx => string.Equals(ctx.CardUid, VatUid, StringComparison.OrdinalIgnoreCase),
                ActionKeyPrefix = ActionKeyPrefix,
                Timing = ActionTiming.AfterWrapped,
                After = DyeAfter,
            });
            Plugin.Logger.LogDebug("[DyeVatPatch] initialized.");
        }

        /// <summary>The colour a dye button names, from its LocalizationKey suffix, or null for a key that is not one.</summary>
        private static Colour? ColourOf(string actionKey)
        {
            if (string.IsNullOrEmpty(actionKey) || !actionKey.StartsWith(ActionKeyPrefix, StringComparison.Ordinal)) return null;
            string suffix = actionKey.Substring(ActionKeyPrefix.Length);
            for (int i = 0; i < ColourSuffixes.Length; i++)
                if (string.Equals(suffix, ColourSuffixes[i], StringComparison.Ordinal)) return (Colour)i;
            return null;
        }

        /// <summary>Every card the vat holds, flattened out of its slots (a slot wraps its own card list).</summary>
        private static List<object> CardsInVat(object vat)
        {
            var cards = new List<object>();
            var slots = CardUtil.GetInventoryList(vat);
            if (slots == null) return cards;
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                var inner = CardUtil.GetInventoryList(slot);
                if (inner != null)
                {
                    foreach (var card in inner)
                        if (card != null) cards.Add(card);
                }
                else
                {
                    cards.Add(slot);
                }
            }
            return cards;
        }

        private static void Scan(object vat, Colour colour, List<object> garments, List<object> pigments)
        {
            string pigmentUid = PigmentUids[(int)colour];
            foreach (var card in CardsInVat(vat))
            {
                string uid = CardUtil.GetCardUniqueId(card);
                if (string.IsNullOrEmpty(uid)) continue;
                if (Garments.ContainsKey(uid)) garments.Add(card);
                else if (string.Equals(uid, pigmentUid, StringComparison.OrdinalIgnoreCase)) pigments.Add(card);
            }
        }

        /// <summary>Cancel gate: true refuses the action before any time is spent.</summary>
        private static bool DyeGate(ActionContext ctx)
        {
            try
            {
                var colour = ColourOf(ctx.ActionKey);
                if (colour == null || ctx.Card == null) return false;

                var garments = new List<object>();
                var pigments = new List<object>();
                Scan(ctx.Card, colour.Value, garments, pigments);

                if (garments.Count == 0)
                {
                    Plugin.Logger.LogDebug("[DyeVatPatch] refused: the vat holds no plain garment.");
                    ShowMessage(NothingToDyeKey);
                    return true;
                }
                if (pigments.Count < PigmentPerGarment)
                {
                    Plugin.Logger.LogDebug($"[DyeVatPatch] refused: the vat holds no {ColourSuffixes[(int)colour.Value]} pigment.");
                    ShowMessage(NoPigmentKey);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                // A gate that cannot read the vat lets the action through; the After handler re-checks.
                WarnOnce("gate-exception", $"[DyeVatPatch] DyeGate failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return false;
            }
        }

        private static void DyeAfter(ActionContext ctx)
        {
            try
            {
                var colour = ColourOf(ctx.ActionKey);
                if (colour == null || ctx.Card == null) return;

                var garments = new List<object>();
                var pigments = new List<object>();
                Scan(ctx.Card, colour.Value, garments, pigments);

                int dyed = 0;
                int pigmentUsed = 0;
                foreach (var garment in garments)
                {
                    if (pigments.Count - pigmentUsed < PigmentPerGarment) break;

                    string plainUid = CardUtil.GetCardUniqueId(garment);
                    if (string.IsNullOrEmpty(plainUid) || !Garments.TryGetValue(plainUid, out var forms)) continue;
                    string dyedUid = forms[(int)colour.Value];

                    // SetModel does not reset durabilities: capture, swap, write back.
                    var carried = new Dictionary<string, float>();
                    foreach (var stat in CardUtil.AllDurabilityStats)
                    {
                        float value = CardUtil.GetDurability(garment, stat);
                        if (!float.IsNaN(value) && value != 0f) carried[stat] = value;
                    }

                    if (!CardUtil.TransformCardInPlace(garment, dyedUid))
                    {
                        WarnOnce("transform-" + dyedUid, $"[DyeVatPatch] {plainUid} could not be turned into {dyedUid}, so the vat stops dyeing.");
                        break;
                    }
                    foreach (var kv in carried)
                    {
                        if (!CardUtil.SetDurability(garment, kv.Key, kv.Value))
                            WarnOnce("restore-" + kv.Key, $"[DyeVatPatch] {kv.Key} could not be written back onto {dyedUid}, so that garment keeps its default wear.");
                    }

                    for (int i = 0; i < PigmentPerGarment; i++)
                    {
                        if (!CardUtil.TryRemoveCard(pigments[pigmentUsed]))
                            WarnOnce("remove-pigment", "[DyeVatPatch] A pigment could not be removed from the vat after its garment was dyed.");
                        pigmentUsed++;
                    }
                    dyed++;
                }

                Plugin.Logger.LogDebug($"[DyeVatPatch] Dyed {dyed} garment(s) {ColourSuffixes[(int)colour.Value]}, using {pigmentUsed} pigment(s).");
            }
            catch (Exception ex)
            {
                WarnOnce("after-exception", $"[DyeVatPatch] DyeAfter failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        /// <summary>One line in the game's own message popup. Reflection, not compile-time: SimpleMessagePopup is
        /// not in every reference copy of Assembly-CSharp.</summary>
        internal static void ShowMessage(string lineKey)
        {
            string line = RoadMeetingPatch.LocalizedText(lineKey);
            if (string.IsNullOrWhiteSpace(line))
            {
                WarnOnce("line-" + lineKey, $"[DyeVatPatch] No localization row for '{lineKey}', so that message shows no line.");
                return;
            }
            var gmType = CardUtil.FindGameType("GraphicsManager");
            var instance = gmType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null);
            var popup = instance == null ? null : Reflect.GetMember(instance, "MessagePopup");
            var setup = popup?.GetType().GetMethod("Setup", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(string), typeof(string), typeof(UnityEngine.Sprite) }, null);
            if (popup == null || setup == null)
            {
                WarnOnce("popup-null", "[DyeVatPatch] GraphicsManager.Instance.MessagePopup.Setup is unavailable - the vat's refusals show no line.");
                return;
            }
            setup.Invoke(popup, new object[] { "", line.Trim(), null });
        }

        private static void WarnOnce(string cause, string message)
        {
            if (_warned.Add(cause)) Plugin.Logger.LogWarning(message);
        }
    }
}
