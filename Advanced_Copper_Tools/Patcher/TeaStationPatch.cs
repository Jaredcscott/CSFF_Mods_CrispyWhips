using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace Advanced_Copper_Tools.Patcher
{
    /// <summary>
    /// Handles the Tea Blending Station's "Grind All" DismantleAction.
    /// Every card in the station's inventory that carries a Grind CardInteraction
    /// (tag_GrindingTool / tag_HammeringToolGeneral trigger, or named "Grind"; no
    /// tag_Millable gate) is transformed in-place to its ground variant, keeping the
    /// spoilage and quality that CI's Transfer* flags carry, as a hand grind does.
    /// Uses in-place CardModel swap (WDI sluice pattern) to avoid OnDestroy
    /// relocation that occurs with DeactivateAndDestroy. Runs after the action's own
    /// DaytimeCost has been spent (ActionRouter AfterWrapped).
    /// </summary>
    public static class TeaStationPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private const string StationUnlitID = "advanced_copper_tools_tea_blending_station";
        private const string StationLitID   = "advanced_copper_tools_tea_blending_station_lit";
        private const string GrindAllName   = "Grind All";
        private const string DrawBoiledWaterName = "Draw Boiled Water";
        private const float  BoiledWaterTemperature = 200f;

        private const string GrindingToolTag = "tag_GrindingTool";
        private const string HammerToolTag   = "tag_HammeringToolGeneral";

        private static readonly string[] _durabilityCurrentFields =
        {
            "CurrentSpoilage", "CurrentUsageDurability", "CurrentFuel", "CurrentProgress",
            "CurrentSpecial1", "CurrentSpecial2", "CurrentSpecial3", "CurrentSpecial4"
        };

        private static readonly string[] _durabilityModelFields =
        {
            "SpoilageTime", "UsageDurability", "FuelCapacity", "Progress",
            "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpecialDurability4"
        };

        private static readonly string[] _durabilityRateFields =
        {
            "BaseSpoilageRate", "BaseUsageRate", "BaseFuelRate", "BaseConsumableRate",
            "BaseSpecial1Rate", "BaseSpecial2Rate", "BaseSpecial3Rate", "BaseSpecial4Rate"
        };

        private static readonly string[] _durabilityLatchFields =
        {
            "SpoilFull", "SpoilEmpty", "UsageFull", "UsageEmpty", "FuelFull", "FuelEmpty",
            "ProgressFull", "ProgressEmpty", "Special1Full", "Special1Empty", "Special2Full",
            "Special2Empty", "Special3Full", "Special3Empty", "Special4Full", "Special4Empty",
            "LiquidEmpty"
        };

        public static void ApplyPatch(Harmony _)
        {
            try
            {
                // AfterWrapped, not Cancel (ACT 1.18.0, plan A5 / W0-9): a Cancel handler swaps
                // in the framework's "handled" stub, so the JSON action's DaytimeCost (2) was
                // never charged and Grind All took no time at all (measured: the clock stayed
                // put). Wrapped, the game runs the DA to completion (time, sounds, its no-op
                // ReceivingCardChanges) and the grind runs once it has finished, which is also
                // vanilla's order: ActionRoutineSteps spends the daytime points BEFORE it applies
                // the action's card changes.
                //
                // One grind per click: Before marks the station pending and After consumes the
                // mark, so a second router dispatch of the same click (a nested route the
                // per-frame ClaimDispatch dedup did not catch) finds nothing pending and skips.
                ActionRouter.Register(new ActionHandler
                {
                    Name = "TeaGrindAll",
                    CardPredicate = ctx => ctx.CardUid == StationUnlitID || ctx.CardUid == StationLitID,
                    ActionNamePrefix = GrindAllName,
                    Timing = ActionTiming.AfterWrapped,
                    Before = ctx => { MarkGrindPending(ctx.Card); return true; },
                    After = ctx =>
                    {
                        if (TakeGrindPending(ctx.Card))
                            HandleGrindAll(ctx.Card);
                        else
                            Logger?.Log(LogLevel.Debug, $"[TeaStation] GrindAll: second dispatch of one click on route '{ctx.Route}' skipped (already ground)");
                    }
                });

                ActionRouter.Register(new ActionHandler
                {
                    Name = "TeaDrawBoiledWater",
                    CardPredicate = ctx => ctx.CardUid == StationUnlitID || ctx.CardUid == StationLitID,
                    ActionNamePrefix = DrawBoiledWaterName,
                    Timing = ActionTiming.AfterWrapped,
                    After = ctx => HandleDrawBoiledWater(ctx.Action, ctx.Card, ctx.GivenCard)
                });
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] Patch failed: {FullException(ex)}");
            }
        }

        // ============================================================
        //  GRIND ALL HANDLER
        // ============================================================
        private static void HandleGrindAll(object station)
        {
            if (station == null)
            {
                WarnOnce("station-null", "GrindAll: the router handed over a null station card; nothing ground");
                return;
            }

            // GetInventoryList returns null (it does not throw) when the station's inventory
            // member cannot be read, which is a fault; an empty list is a player who loaded
            // nothing, which is not.
            var inventory = CardUtil.GetInventoryList(station);
            if (inventory == null)
            {
                WarnOnce("station-inventory-null", "GrindAll: the station's inventory could not be read (reflection returned null); nothing ground");
                return;
            }
            if (inventory.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[TeaStation] GrindAll: empty station inventory");
                return;
            }

            // Collect individual cards that are millable, paired with the grind CI that makes them.
            // The station holds InventorySlot wrappers; each slot holds 1+ stacked herb cards.
            var toGrind = new List<(object card, GrindMatch match)>();

            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;

                // Try to get inner cards from the slot (InventorySlot wraps stacked cards)
                var innerCards = CardUtil.GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner == null) continue;
                        var match = ResolveGrindResult(inner);
                        if (match != null)
                            toGrind.Add((inner, match));
                    }
                }
                else
                {
                    // slotItem might itself be a card (no wrapper layer)
                    var match = ResolveGrindResult(slotItem);
                    if (match != null)
                        toGrind.Add((slotItem, match));
                }
            }

            if (toGrind.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[TeaStation] GrindAll: no millable items in inventory");
                return;
            }

            int transformed = 0;
            foreach (var (card, match) in toGrind)
            {
                if (TransformCardInPlace(card, match))
                    transformed++;
            }

            if (transformed < toGrind.Count)
                WarnOnce("grind-partial", $"GrindAll: only {transformed} of {toGrind.Count} held card(s) were ground; the earlier warning names why");
            Logger?.LogDebug($"[TeaStation] GrindAll: transformed {transformed}/{toGrind.Count} held card(s)");
        }

        // One pending mark per station between the router's Before (prefix) and After (wrap).
        private static readonly HashSet<object> _grindPending = new HashSet<object>(ReferenceComparer.Instance);

        private static void MarkGrindPending(object station)
        {
            if (station == null) return;
            lock (_grindPending) _grindPending.Add(station);
        }

        private static bool TakeGrindPending(object station)
        {
            if (station == null) return false;
            lock (_grindPending) return _grindPending.Remove(station);
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // ============================================================
        //  IN-PLACE CARD TRANSFORM
        // ============================================================
        private static bool TransformCardInPlace(object card, GrindMatch match)
        {
            if (card == null || match == null || string.IsNullOrEmpty(match.TargetUid))
            {
                WarnOnce("transform-bad-args", "Transform: called with no card or no grind target; skipped");
                return false;
            }
            string targetUniqueId = match.TargetUid;
            try
            {
                var targetData = CardUtil.GetCardDataById(targetUniqueId);
                if (targetData == null)
                {
                    WarnOnce("target-missing:" + targetUniqueId, $"Transform: CardData not found for '{targetUniqueId}'; that herb is not ground");
                    return false;
                }

                LogPileState("Transform BEFORE", card);

                // Read the herb's live durabilities BEFORE the model swap: they are the values
                // vanilla's ApplyCardStateChange carries onto the result (_Card.CurrentX).
                var snapshot = SnapshotDurabilities(card);

                if (!CardUtil.TrySetCardModel(card, targetData))
                {
                    WarnOnce("model-not-settable:" + card.GetType().Name, $"Transform: CardModel not settable on {card.GetType().Name}; nothing ground");
                    return false;
                }

                var placement = CapturePlacement(card);
                CardUtil.ReinitCard(card, targetData);
                ResetRuntimeStateForNewModel(card, targetData);
                ApplyGrindTransfers(card, targetData, match, snapshot);
                RestorePlacementIfNeeded(card, placement);

                LogPileState("Transform AFTER", card);
                Logger?.LogDebug($"[TeaStation] Transformed → {targetUniqueId} " +
                    $"(spoilage {ReadCurrent(card, 0)}, quality {ReadCurrent(card, 5)}," +
                    $"pour qty {CardUtil.ToFloat(CardUtil.GetMemberValue(card, "CurrentLiquidQuantity"))})");
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] Transform failed: {FullException(ex)}");
                return false;
            }
        }

        // ============================================================
        //  DURABILITY CARRY-OVER (mirrors vanilla's grind transform)
        //
        //  Vanilla grind (.decomp/GameManager.cs ApplyCardStateChange, case Transform): for
        //  each stat the CI's ReceivingCardChanges marks Transfer*, the result is created with
        //  the source's CURRENT value (times *TransferScale when that OptionalFloatValue is
        //  active; or the change's .x when DurabilityChangesSetTheValueOfTransformResult is
        //  set), which InGameCardBase.Init clamps to [0, Max] and snaps to a valid value, but
        //  only when the RESULT's stat is active. Every other stat starts at the result's own
        //  starting value, which is what ResetRuntimeStateForNewModel already wrote. Then, when
        //  ModifyDurability is set, the CI's own *Change ranges are applied to the result
        //  through ChangeCardDurabilities -> InGameCardBase.ModifyDurability:
        //  ApproveValue(old, Clamp(old + amt, 0, Max)), skipped for an inactive stat. The
        //  liquid scale is 1 here: DurabilitiesLiquidScalingValue is -1 (treated as 1) for any
        //  change without a liquid draw, and a herb carries no liquid.
        //  Not mirrored: AverageGivenAndReceivingCards and Get*FromOtherCard read the OTHER card
        //  (the mortar), which a station grind has none of; a CI using them warns once.
        //
        //  Dry Frostleaf, by hand with a stone mortar: TransferSpoilage + TransferSpecial2,
        //  ModifyDurability with Special2Change +50, so quality 100 comes out 150 (of 192) and
        //  spoilage carries over unchanged (W0-9, wave0-harness.md section 5).
        // ============================================================
        private static readonly string[] _transferFlagFields =
        {
            "TransferSpoilage", "TransferUsage", "TransferFuel", "TransferCharges",
            "TransferSpecial1", "TransferSpecial2", "TransferSpecial3", "TransferSpecial4"
        };

        private static readonly string[] _transferScaleFields =
        {
            "SpoilageTransferScale", "UsageTransferScale", "FuelTransferScale", "ProgressTransferScale",
            "Special1TransferScale", "Special2TransferScale", "Special3TransferScale", "Special4TransferScale"
        };

        private static readonly string[] _changeFields =
        {
            "SpoilageChange", "UsageChange", "FuelChange", "ChargesChange",
            "Special1Change", "Special2Change", "Special3Change", "Special4Change"
        };

        private static float?[] SnapshotDurabilities(object card)
        {
            var values = new float?[_durabilityCurrentFields.Length];
            for (int i = 0; i < values.Length; i++)
                values[i] = ReadCurrent(card, i);
            return values;
        }

        // Null when the member could not be read: GetMemberValue returns null on a miss rather
        // than throwing, and a silent 0 here would hand back a fresh powder from an old herb.
        private static float? ReadCurrent(object card, int index)
        {
            object v = CardUtil.GetMemberValue(card, _durabilityCurrentFields[index]);
            if (v is float f) return f;
            if (v is double d) return (float)d;
            if (v is int n) return n;
            return null;
        }

        private static void ApplyGrindTransfers(object card, object targetData, GrindMatch match, float?[] snapshot)
        {
            object rcc = match.ReceivingChanges;
            if (rcc == null)
            {
                WarnOnce("rcc-null", "Transfer: the grind CI's ReceivingCardChanges could not be read; the powder keeps its fresh values");
                return;
            }

            try
            {
                bool setValueFromChange = ReadBool(rcc, "DurabilityChangesSetTheValueOfTransformResult") ?? false;
                if (ReadBool(rcc, "AverageGivenAndReceivingCards") == true)
                    WarnOnce("average-unsupported:" + match.TargetUid, $"Transfer: the grind CI for '{match.TargetUid}' averages with the grinding tool, which a station grind has none of; the herb's own value is used");

                for (int i = 0; i < _durabilityCurrentFields.Length; i++)
                {
                    object stat = CardUtil.GetMemberValue(targetData, _durabilityModelFields[i]);
                    if (!IsDurabilityActive(stat)) continue; // vanilla Init: (bool)CardModel.X

                    bool? transfer = ReadBool(rcc, _transferFlagFields[i]);
                    if (transfer == null)
                    {
                        WarnOnce("flag-missing:" + _transferFlagFields[i], $"Transfer: CardStateChange.{_transferFlagFields[i]} could not be read; that stat keeps its fresh value");
                        continue;
                    }
                    if (!transfer.Value) continue;

                    float value;
                    if (setValueFromChange)
                    {
                        var range = ReadVector2(rcc, _changeFields[i]);
                        if (range == null)
                        {
                            WarnOnce("change-missing:" + _changeFields[i], $"Transfer: CardStateChange.{_changeFields[i]} could not be read; that stat keeps its fresh value");
                            continue;
                        }
                        value = range.Value.x;
                    }
                    else
                    {
                        if (snapshot[i] == null)
                        {
                            WarnOnce("source-unreadable:" + _durabilityCurrentFields[i], $"Transfer: the herb's {_durabilityCurrentFields[i]} could not be read; that stat keeps its fresh value");
                            continue;
                        }
                        value = snapshot[i].Value;
                        float? scale = ReadOptionalFloat(rcc, _transferScaleFields[i]);
                        if (scale.HasValue) value *= scale.Value;
                    }

                    float max = StatMax(stat);
                    float written = Snap(stat, Clamp(value, 0f, max));
                    if (!CardUtil.SetMemberValue(card, _durabilityCurrentFields[i], written))
                        WarnOnce("write-failed:" + _durabilityCurrentFields[i], $"Transfer: could not write {_durabilityCurrentFields[i]}; that stat keeps its fresh value");
                }

                if (ReadBool(rcc, "ModifyDurability") != true) return;

                for (int i = 0; i < _durabilityCurrentFields.Length; i++)
                {
                    object stat = CardUtil.GetMemberValue(targetData, _durabilityModelFields[i]);
                    if (!IsDurabilityActive(stat)) continue; // vanilla ModifyDurability returns on an inactive stat

                    var range = ReadVector2(rcc, _changeFields[i]);
                    if (range == null)
                    {
                        WarnOnce("change-missing:" + _changeFields[i], $"Modify: CardStateChange.{_changeFields[i]} could not be read; that change is not applied");
                        continue;
                    }
                    float amt = UnityEngine.Random.Range(range.Value.x, range.Value.y);
                    if (amt == 0f) continue;

                    float? old = ReadCurrent(card, i);
                    if (old == null)
                    {
                        WarnOnce("result-unreadable:" + _durabilityCurrentFields[i], $"Modify: the powder's {_durabilityCurrentFields[i]} could not be read; that change is not applied");
                        continue;
                    }
                    float next = Approve(stat, old.Value, Clamp(old.Value + amt, 0f, StatMax(stat)));
                    if (!CardUtil.SetMemberValue(card, _durabilityCurrentFields[i], next))
                        WarnOnce("write-failed:" + _durabilityCurrentFields[i], $"Modify: could not write {_durabilityCurrentFields[i]}; that change is not applied");
                }
            }
            catch (Exception ex)
            {
                WarnOnce("transfer-threw", $"Transfer: carrying the herb's values over threw, so the powder may keep fresh values: {FullException(ex)}");
            }
        }

        private static float Clamp(float v, float min, float max)
        {
            if (max < min) max = min;
            return v < min ? min : (v > max ? max : v);
        }

        // DurabilityStat.Max: MaxValue, or FloatValue when MaxValue <= 0 (.decomp/DurabilityStat.cs).
        private static float StatMax(object stat)
        {
            object v = CardUtil.GetMemberValue(stat, "Max");
            if (v is float f) return f;
            WarnOnce("stat-max-unreadable", "Transfer: DurabilityStat.Max could not be read; using MaxValue");
            float maxValue = GetFloatMember(stat, "MaxValue");
            return maxValue > 0f ? maxValue : GetFloatMember(stat, "FloatValue");
        }

        private static float Snap(object stat, float value) => InvokeFloat(stat, "SnapToNearestValidValue", new object[] { value }, value);

        private static float Approve(object stat, float from, float to) => InvokeFloat(stat, "ApproveValue", new object[] { from, to }, to);

        private static float InvokeFloat(object stat, string methodName, object[] args, float fallback)
        {
            var types = new Type[args.Length];
            for (int i = 0; i < types.Length; i++) types[i] = typeof(float);
            var method = stat?.GetType().GetMethod(methodName, Flags, null, types, null);
            if (method == null)
            {
                WarnOnce("method-missing:" + methodName, $"Transfer: DurabilityStat.{methodName} not found; values are clamped but not snapped to the stat's valid values");
                return fallback;
            }
            return method.Invoke(stat, args) is float f ? f : fallback;
        }

        private static bool? ReadBool(object instance, string name)
            => CardUtil.GetMemberValue(instance, name) is bool b ? b : (bool?)null;

        private static UnityEngine.Vector2? ReadVector2(object instance, string name)
            => CardUtil.GetMemberValue(instance, name) is UnityEngine.Vector2 v ? v : (UnityEngine.Vector2?)null;

        // An OptionalFloatValue's scale, or null when it is inactive (vanilla: (bool)scale).
        private static float? ReadOptionalFloat(object instance, string name)
        {
            object opt = CardUtil.GetMemberValue(instance, name);
            if (opt == null) return null; // a null optional is inactive in vanilla too
            if (!IsDurabilityActive(opt)) return null;
            return GetFloatMember(opt, "FloatValue");
        }

        // Warn-once breadcrumbs, one per cause (CLAUDE.md "Silent Catch Blocks").
        private static readonly HashSet<string> _warnedCauses = new HashSet<string>();

        private static void WarnOnce(string cause, string message)
        {
            bool first;
            lock (_warnedCauses) first = _warnedCauses.Add(cause);
            if (first) Logger?.LogWarning($"[TeaStation] {message}");
        }

        private sealed class PlacementSnapshot
        {
            public object Container;
            public object Slot;
            public object SlotInfo;
        }

        private static PlacementSnapshot CapturePlacement(object card)
        {
            return new PlacementSnapshot
            {
                Container = CardUtil.GetMemberValue(card, "CurrentContainer"),
                Slot = CardUtil.GetMemberValue(card, "CurrentSlot"),
                SlotInfo = CardUtil.GetMemberValue(card, "CurrentSlotInfo")
            };
        }

        private static void RestorePlacementIfNeeded(object card, PlacementSnapshot placement)
        {
            if (card == null || placement == null) return;

            try
            {
                if (placement.Container != null && CardUtil.GetMemberValue(card, "CurrentContainer") == null)
                {
                    var setContainer = card.GetType().GetMethod("SetCurrentContainer", Flags);
                    if (setContainer != null)
                        setContainer.Invoke(card, new[] { placement.Container });
                    else
                        CardUtil.SetMemberValue(card, "<CurrentContainer>k__BackingField", placement.Container);
                }

                if (placement.Slot != null && CardUtil.GetMemberValue(card, "CurrentSlot") == null)
                    CardUtil.SetMemberValue(card, "CurrentSlot", placement.Slot);

                if (placement.SlotInfo != null && CardUtil.GetMemberValue(card, "CurrentSlotInfo") == null)
                    CardUtil.SetMemberValue(card, "CurrentSlotInfo", placement.SlotInfo);
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] RestorePlacement failed: {FullException(ex)}");
            }
        }

        // ============================================================
        //  DIAGNOSTIC — pile/container membership around the transform.
        //  Originally added chasing the "ground powder vanishes when put into
        //  bottles/cloth bags" report as a pile-desync hypothesis. Root cause
        //  turned out to be ResetRuntimeStateForNewModel zeroing
        //  CurrentLiquidQuantity (see comment there) — pile linkage was never
        //  at fault. Kept at Debug level for future station debugging.
        // ============================================================
        private static void LogPileState(string label, object card)
        {
            try
            {
                if (card == null) { Logger?.Log(LogLevel.Debug, $"[TeaStation] {label}: card is null"); return; }

                var cardData = CardUtil.GetCardData(card);
                string uid = cardData != null ? (CardUtil.GetMemberValue(cardData, "UniqueID") as string) : null;

                var container = CardUtil.GetMemberValue(card, "CurrentContainer");
                var slot = CardUtil.GetMemberValue(card, "CurrentSlot");

                int pileCount = -1;
                bool pileContainsCard = false;
                if (slot != null)
                {
                    var getCardPile = slot.GetType().GetMethod("GetCardPile", Flags);
                    if (getCardPile != null)
                    {
                        var ps = getCardPile.GetParameters();
                        var args = new object[ps.Length];
                        for (int i = 0; i < ps.Length; i++)
                            args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : false;
                        if (getCardPile.Invoke(slot, args) is IList pile)
                        {
                            pileCount = pile.Count;
                            foreach (var entry in pile)
                            {
                                if (ReferenceEquals(entry, card)) { pileContainsCard = true; break; }
                            }
                        }
                    }
                }

                Logger?.Log(LogLevel.Debug,
                    $"[TeaStation] {label}: uid={uid ?? "<null>"} hash={card.GetHashCode()} " +
                    $"container={(container != null ? "set" : "null")} slot={(slot != null ? "set" : "null")} " +
                    $"pileCount={pileCount} pileContainsCard={pileContainsCard}");
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] {label}: LogPileState threw: {FullException(ex)}");
            }
        }

        private static void ResetRuntimeStateForNewModel(object card, object targetData)
        {
            if (card == null || targetData == null) return;

            try
            {
                for (int i = 0; i < _durabilityCurrentFields.Length; i++)
                {
                    object stat = CardUtil.GetMemberValue(targetData, _durabilityModelFields[i]);
                    bool active = IsDurabilityActive(stat);
                    if (active && !HasMember(stat, "RatePerDaytimePoint"))
                        WarnOnce("rate-missing", $"{stat.GetType().Name}.RatePerDaytimePoint not found (renamed by a game update?); a ground card's stats would not tick");
                    SetOrWarn(card, _durabilityCurrentFields[i], active ? GenerateStartingValue(stat) : 0f);
                    SetOrWarn(card, _durabilityRateFields[i], active ? GetFloatMember(stat, "RatePerDaytimePoint") : 0f);
                }

                // Powders (solids with a pourable liquid form: flour, medicine powder)
                // carry their pour quantity in CurrentLiquidQuantity — vanilla fresh-card
                // init sets it to SolidToLiquidInfo.LiquidQuantity. The game's generated
                // solid→liquid pour action (CardData.GenerateSolidToLiquidTransferAction)
                // ALWAYS destroys the given solid (GivenCardChanges.ModType = Destroy) and
                // adds CurrentLiquidQuantity of liquid to the container — leaving this at 0
                // made station-ground powder vanish when dragged onto bottles / cloth bags /
                // barrels (Nexus report 2026-08-10). ReinitCard→SetModel has already
                // populated SolidToLiquidInfo for the NEW model when this runs.
                SetOrWarn(card, "CurrentLiquidQuantity", InitialLiquidQuantityFor(card));
                SetOrWarn(card, "BaseEvaporationRate", 0f);
                SetOrWarn(card, "IgnoreTickDurabilityChanges", false);

                foreach (var fieldName in _durabilityLatchFields)
                    SetOrWarn(card, fieldName, false);
            }
            catch (Exception ex)
            {
                WarnOnce("reset-threw", $"ResetRuntimeState failed, so a ground powder may keep stale runtime state: {FullException(ex)}");
            }
        }

        // CardUtil.SetMemberValue returns false rather than throwing when the member is gone, so a
        // field renamed by a game update would leave stale runtime state with no log line (the 1.15.8
        // vanished-powder bug, if it is CurrentLiquidQuantity). One warning per field.
        private static void SetOrWarn(object card, string field, object value)
        {
            if (!CardUtil.SetMemberValue(card, field, value))
                WarnOnce("reset-set:" + field, $"ResetRuntimeState could not write {field} on {card.GetType().Name} (renamed by a game update?); a ground card may keep stale runtime state");
        }

        private static float InitialLiquidQuantityFor(object card)
        {
            try
            {
                // A null read is normal for a card that does not pour, but a MISSING member would
                // read the same way and silently zero every powder, so tell the two apart.
                object conv = CardUtil.GetMemberValue(card, "SolidToLiquidInfo");
                if (conv == null)
                {
                    if (!HasMember(card, "SolidToLiquidInfo"))
                        WarnOnce("solidtoliquid-missing", $"{card.GetType().Name}.SolidToLiquidInfo not found (renamed by a game update?); a ground powder would pour as 0 and vanish");
                    return 0f;
                }
                object toLiquid = CardUtil.GetMemberValue(conv, "ToLiquidCard");
                if (!(toLiquid is UnityEngine.Object uo) || uo == null)
                {
                    if (!HasMember(conv, "ToLiquidCard"))
                        WarnOnce("toliquidcard-missing", $"{conv.GetType().Name}.ToLiquidCard not found (renamed by a game update?); a ground powder would pour as 0 and vanish");
                    return 0f;
                }
                // A powder that pours (it has a ToLiquidCard) must have a quantity: 0 here makes the
                // pour destroy it for nothing, so an unreadable LiquidQuantity warns.
                if (CardUtil.GetMemberValue(conv, "LiquidQuantity") == null)
                    WarnOnce("liquidquantity-unreadable", "SolidToLiquidInfo.LiquidQuantity could not be read (renamed by a game update?); a ground powder would pour as 0 and vanish");
                return GetFloatMember(conv, "LiquidQuantity");
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] InitialLiquidQuantityFor failed: {FullException(ex)}");
                return 0f;
            }
        }

        private static bool IsDurabilityActive(object stat)
        {
            if (stat == null) return false;
            object active = CardUtil.GetMemberValue(stat, "Active");
            if (active is bool b) return b;
            // Non-throwing miss: OptionalValue.Active is a protected field, and a renamed one
            // would read every stat as inactive (all zero, nothing carried over).
            WarnOnce("active-unreadable:" + stat.GetType().Name, $"{stat.GetType().Name}.Active could not be read; treating the stat as inactive");
            return false;
        }

        private static float GenerateStartingValue(object stat)
        {
            if (stat == null) return 0f;
            try
            {
                // EA 0.68b: GenerateStartingValue(bool _Random = true). An optional parameter
                // is still a parameter to reflection, so an EmptyTypes lookup never matched it
                // and every reset fell back to FloatValue without the random starting offset.
                object value = null;
                var method = stat.GetType().GetMethod("GenerateStartingValue", Flags, null, new[] { typeof(bool) }, null);
                if (method != null)
                    value = method.Invoke(stat, new object[] { true });
                else
                {
                    method = stat.GetType().GetMethod("GenerateStartingValue", Flags, null, Type.EmptyTypes, null);
                    if (method != null)
                        value = method.Invoke(stat, null);
                    else
                        WarnOnce("generatestartingvalue-missing", "DurabilityStat.GenerateStartingValue not found; using FloatValue as the starting value");
                }
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[TeaStation] GenerateStartingValue failed: {FullException(ex)}");
            }
            return GetFloatMember(stat, "FloatValue");
        }

        private static bool HasMember(object instance, string name)
        {
            if (instance == null) return false;
            var type = instance.GetType();
            return CardUtil.FindField(type, name) != null || CardUtil.FindProperty(type, name) != null;
        }

        private static float GetFloatMember(object instance, string name)
        {
            object value = CardUtil.GetMemberValue(instance, name);
            if (value is float f) return f;
            if (value is double d) return (float)d;
            if (value is int i) return i;
            return 0f;
        }

        // ============================================================
        //  GRIND RESULT RESOLUTION
        // ============================================================

        /// <summary>
        /// Finds the card's Grind CardInteraction (trigger tag = tag_GrindingTool or
        /// tag_HammeringToolGeneral, or a CI named "Grind") and returns its transform
        /// target with the CI's ReceivingCardChanges (for the Transfer* carry-over), or
        /// null if the card has no such interaction.
        ///
        /// We do NOT require tag_Millable: vanilla grindable herbs (Dry Frostleaf,
        /// Dry Appleweed, Wheat Grains, Edible Acorns, etc.) lack that tag — they're
        /// identified solely by carrying a CardInteraction with the grinding-tool
        /// trigger. The slot's InventoryFilter already pre-screens what gets in.
        /// </summary>
        private sealed class GrindMatch
        {
            public string TargetUid;
            public object ReceivingChanges; // boxed CardStateChange of the matched CI
        }

        private static GrindMatch ResolveGrindResult(object card)
        {
            try
            {
                if (card == null) return null; // callers skip nulls; nothing to warn about

                object cardData = CardUtil.GetCardData(card);
                if (cardData == null)
                {
                    WarnOnce("card-data-null", "ResolveGrind: a held card's CardData could not be read; it is not ground");
                    return null;
                }

                var ciField = cardData.GetType().GetField("CardInteractions", Flags);
                if (ciField == null)
                {
                    WarnOnce("ci-field-missing", "ResolveGrind: CardData.CardInteractions not found (reflection miss); Grind All grinds nothing");
                    return null;
                }
                var interactions = ciField.GetValue(cardData) as IList;
                if (interactions == null)
                {
                    WarnOnce("ci-null:" + CardUtil.GetMemberValue(cardData, "UniqueID"), $"ResolveGrind: '{CardUtil.GetMemberValue(cardData, "UniqueID")}' has a null CardInteractions list; it is not ground");
                    return null;
                }

                foreach (var ci in interactions)
                {
                    if (ci == null) continue;

                    var compatField = ci.GetType().GetField("CompatibleCards", Flags);
                    if (compatField == null)
                    {
                        WarnOnce("compat-field-missing", "ResolveGrind: CardInteraction.CompatibleCards not found (reflection miss); Grind All grinds nothing");
                        return null;
                    }
                    var compat = compatField.GetValue(ci);
                    if (compat == null) continue;

                    // Read resolved TriggerTags (populated by WarpResolver from TriggerTagsWarpData)
                    var tagsField = compat.GetType().GetField("TriggerTags", Flags);
                    if (tagsField == null)
                        WarnOnce("triggertags-field-missing", "ResolveGrind: CardsDropCollection.TriggerTags not found (reflection miss); only CIs named \"Grind\" are matched");
                    var tags = tagsField?.GetValue(compat) as IList;

                    bool matched = false;
                    if (tags != null)
                    {
                        foreach (var t in tags)
                        {
                            if (t == null) continue;
                            // CardTag is not UniqueIDScriptable — use SO .name (tag identifier)
                            string tagName = (t is UnityEngine.Object uo) ? uo.name : null;
                            if (tagName == GrindingToolTag || tagName == HammerToolTag)
                            {
                                matched = true;
                                break;
                            }
                        }
                    }

                    string actionName = CardUtil.GetActionName(ci);
                    if (!matched && !string.Equals(actionName, "Grind", StringComparison.Ordinal))
                        continue;

                    // The FIRST matching CI wins, in the card's own order. For vanilla dried
                    // herbs that is the stone mortar's "Grind" (a TriggerCards CI, matched by
                    // name) ahead of the hammer-tag one, so the station grinds as the mortar does.
                    var rccField = ci.GetType().GetField("ReceivingCardChanges", Flags);
                    if (rccField == null)
                    {
                        WarnOnce("rcc-field-missing", "ResolveGrind: CardInteraction.ReceivingCardChanges not found (reflection miss); Grind All grinds nothing");
                        return null;
                    }
                    var rcc = rccField.GetValue(ci);
                    var uid = GetTransformIntoUniqueId(rcc);
                    if (!string.IsNullOrEmpty(uid))
                        return new GrindMatch { TargetUid = uid, ReceivingChanges = rcc };
                }
            }
            catch (Exception ex)
            {
                WarnOnce("resolve-threw", $"ResolveGrindResult threw, so a held card is not ground: {FullException(ex)}");
            }
            return null;
        }

        private static string GetTransformIntoUniqueId(object rcc)
        {
            if (rcc == null) return null;

            var transformField = rcc.GetType().GetField("TransformInto", Flags);
            if (transformField == null)
                WarnOnce("transforminto-field-missing", "ResolveGrind: CardStateChange.TransformInto not found (reflection miss); falling back to TransformIntoWarpData");
            var transformCard  = transformField?.GetValue(rcc);
            if (transformCard is UnityEngine.Object uo && uo != null)
            {
                var uidField = transformCard.GetType().GetField("UniqueID", Flags);
                var uid = uidField?.GetValue(transformCard) as string;
                if (!string.IsNullOrEmpty(uid)) return uid;
                WarnOnce("transforminto-uid-unreadable", "ResolveGrind: the grind result's UniqueID could not be read; falling back to TransformIntoWarpData");
            }

            // A "Grind" CI with no transform target (a CI that produces cards instead) is not
            // an in-place grind: the caller moves on to the card's next CI.
            var warpField = rcc.GetType().GetField("TransformIntoWarpData", Flags);
            return warpField?.GetValue(rcc) as string;
        }

        // ============================================================
        //  DRAW BOILED WATER — TEMPERATURE FIX
        // ============================================================
        private static void HandleDrawBoiledWater(object action, object receivingCard, object givenCard)
        {
            if (action == null || receivingCard == null || givenCard == null)
                return;

            Logger?.LogDebug("[TeaStation] DrawBoiled fired");
            ApplyBoiledWaterTemperature(givenCard);
            DrainWaterCharge(receivingCard);
        }

        // Outside the iterator so it can use try/catch around reflection.
        private static void ApplyBoiledWaterTemperature(object givenCard)
        {
            try
            {
                if (givenCard == null) return;
                var givenType = givenCard.GetType();
                var containedLiquidField = givenType.GetField("ContainedLiquid", Flags);
                if (containedLiquidField == null)
                {
                    Logger?.LogError("[TeaStation] DrawBoiled: ContainedLiquid field missing");
                    return;
                }
                var liquid = containedLiquidField.GetValue(givenCard);
                if (liquid == null)
                {
                    Logger?.LogDebug("[TeaStation] DrawBoiled: given card has no ContainedLiquid");
                    return;
                }

                var liquidType = liquid.GetType();
                var fuelField = liquidType.GetField("CurrentFuel", Flags);
                if (fuelField == null)
                {
                    Logger?.LogError("[TeaStation] DrawBoiled: CurrentFuel field missing");
                    return;
                }

                // Cap to the liquid card's actual MaxValue if we can read it; otherwise use 200.
                float target = BoiledWaterTemperature;
                try
                {
                    var cardModelProp = liquidType.GetProperty("CardModel", Flags);
                    var cardModel = cardModelProp?.GetValue(liquid);
                    if (cardModel != null)
                    {
                        var fuelCapField = cardModel.GetType().GetField("FuelCapacity", Flags);
                        var fuelCap = fuelCapField?.GetValue(cardModel);
                        if (fuelCap != null)
                        {
                            var maxField = fuelCap.GetType().GetField("MaxValue", Flags);
                            if (maxField != null)
                            {
                                var maxObj = maxField.GetValue(fuelCap);
                                if (maxObj is float fmax && fmax > 0f) target = fmax;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogDebug($"[TeaStation] DrawBoiled: FuelCapacity.MaxValue probe failed, falling back to default {BoiledWaterTemperature}: {ex.Message}");
                }

                fuelField.SetValue(liquid, target);
                Logger?.LogDebug(
                    $"[TeaStation] DrawBoiled: set ContainedLiquid.CurrentFuel = {target}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] DrawBoiled temperature fix failed: {FullException(ex)}");
            }
        }

        // Drain one Water Charge (CurrentSpecial4) from the tea station after dispensing
        // boiled water. RCC.ModType was changed from 1→0 to fix a WikiMod NullRef crash
        // (FormatCardStateChange hits null when ModType=1 + Special4Change + CreatedLiquidInGivenCard
        // are combined — all 14 vanilla CIs with CreatedLiquidInGivenCard use ModType=0 on both sides).
        private static void DrainWaterCharge(object stationCard)
        {
            try
            {
                if (stationCard == null) return;
                float before = CardUtil.ToFloat(CardUtil.GetMemberValue(stationCard, "CurrentSpecial4"));
                if (!CardUtil.ModifyDurabilityStat(stationCard, "CurrentSpecial4", -1f))
                {
                    Logger?.LogError("[TeaStation] DrainWaterCharge: CurrentSpecial4 not found");
                    return;
                }
                float after = Math.Max(0f, before - 1f);
                Logger?.LogDebug($"[TeaStation] DrainWaterCharge: WaterCharges {before} -> {after}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[TeaStation] DrainWaterCharge failed: {FullException(ex)}");
            }
        }

        private static string FullException(Exception ex)
        {
            return ex.InnerException?.ToString() ?? ex.ToString();
        }
    }
}
