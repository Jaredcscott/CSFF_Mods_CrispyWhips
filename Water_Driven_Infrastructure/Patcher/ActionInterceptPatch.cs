using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using CSFFModFramework.Api;
using CSFFModFramework.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    /// <summary>
    /// Intercepts DismantleAction execution on water-driven structures to consume
    /// inventory items that pure JSON DismantleActions cannot handle.
    /// Action interception (drag/DismantleAction dispatch) is registered on the
    /// framework's ActionRouter (see RegisterActionHandlers) rather than direct
    /// Harmony patches on ActionRoutine/PerformStackActionRoutine/PerformActionAsEnumerator.
    ///
    /// Handles:
    ///   Sawmill  "Cut"           - consume one log from inventory (pure JSON, no C# needed)
    ///   Mill     "Grind All"     - let JSON Progress + Mill effect handle conversion
    /// </summary>
    public static class ActionInterceptPatch
    {
        private static ManualLogSource Logger => Plugin.Logger;
        private static readonly BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // UniqueIDs
        private const string SawmillID          = "water_sawmill_placed";
        private const string MillID             = "water_sawmill_grinding_mill_placed";
        private const string SluiceID           = "water_sawmill_ore_sluice_placed";
        private const string ForgeID            = "water_sawmill_forge_placed";
        private const string WorkshopID         = "water_sawmill_workshop_placed";
        private const string FishpondFilledID   = "water_sawmill_fishpond_filled";
        private const string FishpondStockedID  = "water_sawmill_fishpond_stocked";
        private const string FishpondWinterID   = "water_sawmill_fishpond_winter";
        private const float  FishCaughtQuality  = 72f;   // 75 % of SpecialDurability2 max (96)
        private const int    FishCatchPostSpawnTicks = 1;
        private const float  WorkshopMetalQualityBoost = 5f;
        private const string CopperNuggetGUID = "4b0f4937a5ecb90499428c8c10288afc";
        // Prefers ACT's real items when AdvancedCopperTools is installed (feeds its economy);
        // falls back to WDI-native items otherwise. ActCompat.IsInstalled is detected once at
        // load (GameLoadPatch), not re-scanned per click. See root CLAUDE.md §WDI/ACT Decoupling.
        private static string CopperSheetID => ActCompat.IsInstalled ? ActCompat.MetalSheetUid : "water_sawmill_cast_metal_sheet";
        private static string CopperNailsID => ActCompat.IsInstalled ? ActCompat.CopperNailsUid : "water_sawmill_copper_rivet";
        private const string UnfinishedLumpID = "b92071e54dac7e54db99b48794e737ad";
        private const int   WorkshopNuggetsPerCraft = 6;
        private const int   LumpNuggetsPerCraft     = 4;
        private const float LumpInitialStrikes      = 2f;
        private const float LumpMinimumQuality      = 50f;  // At least 50 % starting quality (SD2 + SD3)
        private const int   LumpInitializationRetryFrames = 60;

        private static int GetCraftCost(WorkshopCraftKind kind)
        {
            return kind == WorkshopCraftKind.CopperLump ? LumpNuggetsPerCraft : WorkshopNuggetsPerCraft;
        }

        private enum WorkshopCraftKind
        {
            None,
            CopperSheet,
            CopperNails,
            CopperLump
        }

        // SmeltingRecipes: UniqueID → copper nugget count
        // Mirrors WaterDrivenInfrastructure/SmeltingRecipes.json + AdvancedCopperTools/SmeltingRecipes.json.
        // ACT entries are harmless if ACT is not installed (items simply won't appear in inventory).
        // Copper gear/blade items (copper_gear_small/large, copper_saw_blade) smelt via their own passive-effect
        // Progress system in the water-driven forge and return copper nuggets — not IronBloom.
        // Yields based on: 1 Heated Lump = 6 nuggets, 1 Small Crucible = 6, 1 Large Crucible = 12.
        private static readonly Dictionary<string, int> _smeltingRecipes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            // WDI parts
            { "water_sawmill_workshop_kit",               72 },  // forge_kit(24) + water_mill(24) + 2×small(12) + 1×large(12)
            // ACT copper items (from AdvancedCopperTools/SmeltingRecipes.json)
            { "advanced_copper_tools_copper_nails",        1  },  // 1 nugget direct
            { "advanced_copper_tools_wheel_hub",           6  },  // 1 small crucible
            { "advanced_copper_tools_shape_metal_pan",     4  },  // 4 nuggets direct
            { "advanced_copper_tools_metal_sheet",         6  },  // 1 heated lump
            { "advanced_copper_tools_wheel_rim",           6  },  // 1 heated lump
            { "advanced_copper_tools_stove_top_mold",      6  },  // 1 small crucible
            { "advanced_copper_tools_cast_stove_top",      6  },  // 1 small crucible (from mold)
            { "advanced_copper_tools_large_saw",           16 },  // 2 sheets(12) + 4 nails(4)
            { "advanced_copper_tools_wearable_metal_pan",  5  },  // pan(4) + nail(1)
            { "advanced_copper_tools_lantern_oilwell",     6  },  // 1 sheet
            { "advanced_copper_tools_metal_lantern",       18 },  // oilwell(6) + 2 sheets(12)
            { "advanced_copper_tools_metal_lantern_lit",   18 },  // same as unlit
            { "advanced_copper_tools_copper_tea_kettle",   18 },  // 3 sheets
            { "advanced_copper_tools_copper_cauldron",     34 },  // 5 sheets(30) + 4 nails(4)
            { "advanced_copper_tools_copper_pantry",       30 },  // 4 sheets(24) + 6 nails(6)
            { "advanced_copper_tools_copper_stove",        30 },  // 4 sheets(24) + stove_top(6)
            { "advanced_copper_tools_wheel_assembly",      12 },  // rim(6) + hub(6)
            { "advanced_copper_tools_bucket",              48 },  // 8 sheets
            { "advanced_copper_tools_tea_station_kit",      48 },  // kettle(18) + stove(30)
            { "advanced_copper_tools_copper_bathtub_empty",   78 }, // bucket(48) + stove(30)
            { "advanced_copper_tools_copper_helmet",        15 },
            { "advanced_copper_tools_copper_gauntlets",     11 },
            { "advanced_copper_tools_copper_greaves",       15 },
            { "advanced_copper_tools_copper_breastplate",   23 },
            { "advanced_copper_tools_copper_brazier_kit",   22 },
        };

        // Vanilla GUIDs

        private const string LogGUID        = "0ab556ab6af1efc47a2cba5cdf4ace04";
        private const string MudPileGUID    = "22427beeefed8a9469a997bdce087332";
        private const string DirtPileGUID   = "6d47888db6018d04ea562476cde60440";
        private const string FineDirtGUID   = "3f131012e4586224c86da02a5fa50d26";
        private const string GreenstoneGUID = "bcc7d7a764978e447bc38b36dcca2055";
        private const string FlintGUID      = "bef002cfd45b3e8459864746f403cf73";
        private const string StoneGUID      = "a7384e5147b23a642809451cc4ef24fb";
        private const string ClayGUID       = "68c14d265ea6c874ba79444d2e1ef7b3";
        // Sluice nugget types — all use MetalNugget (CopperNuggetGUID); SpecialDurability4 sets display type (EA 0.64+)
        private const float  NuggetIronType      = 200f; // Iron Nugget
        private const float  NuggetCopperType    = 100f; // Copper Nugget
        private const float  NuggetTinType       = 120f; // Tin Nugget
        private const float  NuggetSluiceQuality =   5f; // SD1/SD2/SD3 quality on spawned nuggets
        private const float  NuggetSmeltQuality  =  50f; // SD1/SD2/SD3 quality on iron smelt nuggets
        private const int    NuggetInitRetryFrames = 5;

        // Iron item OnFull smelting (WDI iron items → 4x iron MetalNugget)
        private const string MetalBarFinishedGUID = "14f7ddfe60496b54881d5a25de8abb20"; // kept for reference
        private const float  IronBarMetalType     = 200f; // SpecialDurability4 value for iron type
        private static readonly HashSet<string> IronSmeltItemIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "water_sawmill_iron_parts",
            "water_sawmill_iron_bearing",
            "water_sawmill_iron_axle",
            "water_sawmill_iron_wrench",
        };

        // Fish caught from ponds should spawn at 75% quality (SpecialDurability2 = 72/96)
        // and with vanilla fresh-caught weight (SpecialDurability1), which controls gutting portions.
        private const string PikeGUID     = "70df5b800c3fd56499fd412bb9ce7523";
        private const string PerchGUID    = "e64237c9a44ea3945bc75b8b9f4735c0";
        private const string MinnowGUID   = "992cc6a7e92f4c5438c766327a3cbfcb";
        private const string SturgeonGUID = "de00abf9d0d186b42bed6807ba49a4cb";
        private const string TroutGUID    = "f4972170adc8b3d4f9d4bc09c6e7f760";
        private const string CharGUID     = "a966c94277465f741857b994202791f3";

        private struct FishWeightDefaults
        {
            public float BaseWeight;
            public float RandomOffset;
            public float MaxWeight;

            public FishWeightDefaults(float baseWeight, float randomOffset, float maxWeight)
            {
                BaseWeight = baseWeight;
                RandomOffset = randomOffset;
                MaxWeight = maxWeight;
            }
        }

        private static readonly Dictionary<string, FishWeightDefaults> PondFishWeightDefaults =
            new Dictionary<string, FishWeightDefaults>(StringComparer.Ordinal)
            {
                { PikeGUID,     new FishWeightDefaults(700f,  2100f, 2800f) },
                { PerchGUID,    new FishWeightDefaults(700f,     0f,  700f) },
                { TroutGUID,    new FishWeightDefaults(700f,  1400f, 2100f) },
                { CharGUID,     new FishWeightDefaults(700f,  1400f, 2100f) },
                { SturgeonGUID, new FishWeightDefaults(1400f, 4200f, 5600f) },
            };

        private static readonly HashSet<string> PondFishGuids = new HashSet<string>(StringComparer.Ordinal)
        {
            PikeGUID,
            PerchGUID,
            MinnowGUID,
            SturgeonGUID,
            TroutGUID,
            CharGUID,
        };

        // Per-pond stocked-species counts (session-only; resets on save reload).
        // Keyed by InGameCardBase.GetInstanceID(). When tracking is unavailable,
        // Catch Other Fish falls back to the JSON's uniform 33/33/33 produced cards.
        private struct OtherFishCounts { public int Sturgeon, Trout, Charr; }
        private static readonly Dictionary<int, OtherFishCounts> _otherFish = new Dictionary<int, OtherFishCounts>();



        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                var gmType = AccessTools.TypeByName("GameManager");
                var cardType = AccessTools.TypeByName("InGameCardBase");

                var selectedContainedBlueprint = AccessTools.Method(cardType, "GetSelectedContainedBlueprint");
                if (selectedContainedBlueprint != null)
                {
                    harmony.Patch(selectedContainedBlueprint,
                        postfix: new HarmonyMethod(typeof(ActionInterceptPatch), nameof(GetSelectedContainedBlueprint_Postfix)));
                }
                else
                {
                    Logger?.LogDebug("[ActionIntercept] InGameCardBase.GetSelectedContainedBlueprint not found; workshop storage routing patch unavailable");
                }

                // GiveCard postfix — set Metal Type=200 on MetalBarFinished from iron item OnFull
                var giveCardForPatch = gmType?.GetMethods(Flags).FirstOrDefault(m =>
                    m.Name == "GiveCard" && !m.IsGenericMethod &&
                    m.GetParameters().Length >= 1 &&
                    m.GetParameters()[0].ParameterType.Name.Contains("CardData"));
                if (giveCardForPatch != null)
                {
                    harmony.Patch(giveCardForPatch,
                        postfix: new HarmonyMethod(typeof(ActionInterceptPatch), nameof(GiveCard_Postfix)));
                }
                else
                {
                    Logger?.LogDebug("[ActionIntercept] GiveCard not found at patch time; iron bar metal type uses fallback");
                }

                // CardInteractions (drag) + DismantleActions (buttons) are now dispatched via
                // CSFFModFramework's ActionRouter — ONE Harmony patch set on ActionRoutine /
                // PerformStackActionRoutine / PerformActionAsEnumerator shared across mods,
                // instead of WDI's own direct patches on those coroutines.
                RegisterActionHandlers();
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] Patch failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }
        }

        // ============================================================
        //  ACTION ROUTER REGISTRATION
        //  Replaces the former direct Harmony patches + prefix/postfix pending-card
        //  bookkeeping (ActionRoutine_Prefix/Postfix, PerformStackAction_Prefix/Postfix,
        //  PerformActionAsEnum_Prefix) with declarative ActionRouter handlers. The
        //  framework owns the single IEnumerator wrap point and per-handler frame
        //  dedup, so the old _pendingXxxCard / _xxxDrainDepth / _lastXxxFrame fields
        //  are gone — see CLAUDE.md §Harmony Patching Pitfalls (iterator composition).
        // ============================================================
        private static void RegisterActionHandlers()
        {
            // Mill race water-connectivity gate. Registered FIRST so it can cancel an
            // action before any handler below runs — ActionRouter evaluates matching
            // handlers for a dispatch in registration order.
            ActionRouter.Register(new ActionHandler
            {
                Name = "MillRaceGate",
                CardPredicate = _ => true,
                Timing = ActionTiming.Cancel,
                Before = ctx =>
                {
                    if (!MillRaceNetwork.ShouldBlockAction(ctx.CardUid, ctx.ActionName, ctx.Card)) return false;
                    Logger?.LogDebug($"[ActionIntercept] Blocked '{ctx.ActionName}' on {ctx.CardUid}: mill race water connection unavailable");
                    ClearDragState(ctx.GivenCard);
                    return true;
                }
            });

            // Grinding Mill — "Grind All"
            ActionRouter.Register(new ActionHandler
            {
                Name = "MillGrindAll",
                CardUid = MillID,
                ActionNamePrefix = "Grind All",
                Timing = ActionTiming.Cancel,
                Before = ctx => { HandleGrindAllInner(ctx.Card); ClearDragState(ctx.GivenCard); return true; }
            });

            // Ore Sluice — "Sluice All"
            ActionRouter.Register(new ActionHandler
            {
                Name = "SluiceAll",
                CardUid = SluiceID,
                ActionNamePrefix = "Sluice All",
                Timing = ActionTiming.Cancel,
                Before = ctx => { HandleSluiceAllInner(ctx.Card); ClearDragState(ctx.GivenCard); return true; }
            });

            // Fishpond — record which species was stocked so "Catch Other Fish" can
            // produce proportionally. JSON still handles Special4Change and destroying
            // the given fish card.
            ActionRouter.Register(new ActionHandler
            {
                Name = "FishpondStockTracking",
                CardPredicate = ctx => ctx.CardUid == FishpondFilledID || ctx.CardUid == FishpondStockedID,
                Timing = ActionTiming.Before,
                Before = ctx =>
                {
                    if (ctx.ActionName == "Stock Sturgeon") IncrementOtherFish(ctx.Card, 1, 0, 0);
                    else if (ctx.ActionName == "Stock Trout") IncrementOtherFish(ctx.Card, 0, 1, 0);
                    else if (ctx.ActionName == "Stock Char") IncrementOtherFish(ctx.Card, 0, 0, 1);
                    return false;
                }
            });

            // Fishpond — "Catch Other Fish" picks a result weighted by what was
            // actually stocked. Cancels the JSON 33/33/33 fallback only when tracking
            // data is available; otherwise lets JSON run (the FishCatchStats handler
            // below still fixes up quality/weight afterward).
            ActionRouter.Register(new ActionHandler
            {
                Name = "CatchOtherFishPick",
                CardPredicate = ctx => ctx.CardUid == FishpondFilledID || ctx.CardUid == FishpondStockedID,
                ActionNamePrefix = "Catch Other Fish",
                Timing = ActionTiming.Cancel,
                Before = ctx => !HandleCatchOtherFish(ctx.Card)
            });

            // Fishpond — quality/weight fixup for "Catch X" / "Ice Fish X" (and the
            // vanilla-fallback path of "Catch Other Fish"). ActionRouter's After
            // callback is synchronous (cannot itself yield an extra frame), so the fix
            // is applied via the same decoupled StartCoroutine helper already used for
            // nugget/lump stat initialization.
            ActionRouter.Register(new ActionHandler
            {
                Name = "FishCatchStats",
                CardPredicate = ctx => ctx.CardUid == FishpondFilledID || ctx.CardUid == FishpondStockedID || ctx.CardUid == FishpondWinterID,
                Timing = ActionTiming.AfterWrapped,
                Before = ctx =>
                {
                    bool relevant = IsFishCatchAction(ctx.ActionName)
                        || string.Equals(ctx.ActionName, "Catch Other Fish", StringComparison.Ordinal);
                    if (relevant) ctx.Tag = SnapshotFishCardIds();
                    return true;
                },
                After = ctx =>
                {
                    if (ctx.Tag is HashSet<int> preIds)
                        StartDelayedFishInitialization(preIds, ctx.Card);
                }
            });

            // Workshop — copper-nugget-cost crafts (Hammer Copper Sheet / Forge Copper
            // Nails / Cast Metal Lump). The gate cancels outright when the workshop
            // doesn't hold enough nuggets; otherwise Apply consumes them and spawns the
            // result once the original DaytimeCost timing completes.
            ActionRouter.Register(new ActionHandler
            {
                Name = "WorkshopCraftGate",
                CardUid = WorkshopID,
                Timing = ActionTiming.Cancel,
                Before = ctx =>
                {
                    var kind = GetWorkshopCraftKind(ctx.Action);
                    if (kind == WorkshopCraftKind.None) return false;
                    int cost = GetCraftCost(kind);
                    if (CountCopperNuggets(ctx.Card) >= cost) return false;
                    Logger?.LogDebug($"[ActionIntercept] {kind}: needs {cost} copper nuggets in workshop inventory");
                    return true;
                }
            });
            ActionRouter.Register(new ActionHandler
            {
                Name = "WorkshopCraftApply",
                CardUid = WorkshopID,
                Timing = ActionTiming.AfterWrapped,
                Before = ctx => { ctx.Tag = GetWorkshopCraftKind(ctx.Action); return true; },
                After = ctx =>
                {
                    if (ctx.Tag is WorkshopCraftKind kind && kind != WorkshopCraftKind.None)
                        HandleWorkshopCraft(ctx.Card, kind);
                }
            });

            // Water-Driven Forge / Workshop — "Hammer All"
            ActionRouter.Register(new ActionHandler
            {
                Name = "HammerAll",
                CardUid = WorkshopID,
                ActionNamePrefix = "Hammer All",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => HandleHammerAllInner(ctx.Card, workshopQualityBoost: true)
            });

            // Workshop / Forge — "Blast"
            ActionRouter.Register(new ActionHandler
            {
                Name = "BlastAll",
                CardPredicate = ctx => ctx.CardUid == WorkshopID || ctx.CardUid == ForgeID,
                ActionNamePrefix = "Blast",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => HandleBlastAllInner(ctx.Card)
            });

            // Iron item OnFull smelting (Parts/Bearing/Axle/Wrench) — snapshot existing
            // nugget IDs before the action runs so newly spawned iron nuggets can be
            // identified and typed/quality-fixed afterward. Fires on every action on
            // these 4 cards (matches the original unconditional-per-action check).
            ActionRouter.Register(new ActionHandler
            {
                Name = "IronSmeltType",
                CardPredicate = ctx => IronSmeltItemIds.Contains(ctx.CardUid),
                Timing = ActionTiming.AfterWrapped,
                Before = ctx => { ctx.Tag = SnapshotCardIdsByUniqueId(CopperNuggetGUID); return true; },
                After = ctx =>
                {
                    if (ctx.Tag is HashSet<int> preIds && ApplyIronBarType(preIds) == 0)
                        StartDelayedIronBarTypeRetry(preIds);
                }
            });
        }

        static void GetSelectedContainedBlueprint_Postfix(object __instance, ref object __result)
        {
            if (__result == null) return;
            if (!string.Equals(CardUtil.GetCardUniqueId(__instance), WorkshopID, StringComparison.Ordinal)) return;
            if (!IsWorkshopPrimaryStorageContext()) return;

            __result = null;
        }

        private static bool IsWorkshopPrimaryStorageContext()
        {
            try
            {
                var frames = new StackTrace().GetFrames();
                if (frames == null) return false;

                foreach (var frame in frames)
                {
                    var method = frame?.GetMethod();
                    if (method == null) continue;

                    var typeName = method.DeclaringType?.Name ?? string.Empty;
                    var methodName = method.Name ?? string.Empty;

                    if (typeName == "InspectionPopup" &&
                        (methodName == "SetupInventory" || methodName == "RefreshInventory" || methodName == "RefreshVisibleSlots"))
                        return true;

                    if (typeName == "InGameCardBase" &&
                        (methodName == "OnDrop" || methodName == "GetPossibleActionFromPlayerDrag" ||
                         methodName == "DropInInventory" || methodName == "CanReceiveInInventoryInstance" || methodName == "GetIndexForInventory"))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static void HandleBlastAllInner(object workshop)
        {
            var inventory = CardUtil.GetInventoryList(workshop);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.LogDebug("[ActionIntercept] BlastAll: empty workshop inventory");
                return;
            }

            var toSmelt = new List<(object item, int nuggets)>();

            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;
                var innerCards = CardUtil.GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner == null) continue;
                        string uid = CardUtil.GetCardUniqueId(inner);
                        if (uid != null && _smeltingRecipes.TryGetValue(uid, out int n))
                            toSmelt.Add((inner, n));
                    }
                }
                else
                {
                    string uid = CardUtil.GetCardUniqueId(slotItem);
                    if (uid != null && _smeltingRecipes.TryGetValue(uid, out int n))
                        toSmelt.Add((slotItem, n));
                }
            }

            if (toSmelt.Count == 0)
            {
                Logger?.LogDebug("[ActionIntercept] BlastAll: no smeltable copper items found");
                return;
            }

            int totalNuggets = 0;
            foreach (var (_, n) in toSmelt) totalNuggets += n;

            // Nuggets inherit the best smelted item's quality percentage (never below
            // NuggetSmeltQuality) — machine processing must never lower quality.
            float bestQualityPct = 0f;
            foreach (var (item, _) in toSmelt)
            {
                float v = CardUtil.GetDurability(item, "SpecialDurability2");
                if (float.IsNaN(v) || v <= 0f) continue;
                float m = CardUtil.GetDurabilityMax(item, "SpecialDurability2");
                float pct = (!float.IsNaN(m) && m > 0f) ? v / m : v / 100f;
                if (pct > bestQualityPct) bestQualityPct = pct;
            }

            Logger?.LogDebug($"[ActionIntercept] BlastAll: smelting {toSmelt.Count} item(s) → {totalNuggets} copper nugget(s), quality {bestQualityPct:P0}");

            for (int i = 0; i < totalNuggets; i++)
            {
                var nugget = SpawnService.Spawn(CopperNuggetGUID);
                if (nugget == null) continue;
                foreach (var stat in new[] { "SpecialDurability1", "SpecialDurability2", "SpecialDurability3" })
                {
                    float max = CardUtil.GetDurabilityMax(nugget, stat);
                    if (float.IsNaN(max) || max <= 0f) max = 100f;
                    SetMinimumDurabilityStatValue(nugget, stat, Math.Max(bestQualityPct * max, NuggetSmeltQuality));
                }
                RefreshCardDurabilityVisuals(nugget);
            }

            EjectCardsFromStructure(workshop, toSmelt.Select(t => t.item));
        }

        // ============================================================
        //  GRINDING MILL - C# Grind All handler
        // ============================================================
        private const string GrindingToolTag = "tag_GrindingTool";
        private const string HammerToolTag   = "tag_HammeringToolGeneral";

        private static bool HandleGrindAllInner(object mill)
        {
            var inventory = CardUtil.GetInventoryList(mill);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] GrindAll: empty mill inventory");
                return false;
            }

            var toGrind = new List<(object card, string resultId)>();
            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;
                var innerCards = CardUtil.GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner == null) continue;
                        string resultId = ResolveGrindResult(inner);
                        if (resultId != null)
                            toGrind.Add((inner, resultId));
                    }
                }
                else
                {
                    string resultId = ResolveGrindResult(slotItem);
                    if (resultId != null)
                        toGrind.Add((slotItem, resultId));
                }
            }

            if (toGrind.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] GrindAll: no grindable items found");
                return false;
            }

            Logger?.Log(LogLevel.Debug, $"[ActionIntercept] GrindAll: grinding {toGrind.Count} item(s)");

            foreach (var (sourceCard, resultId) in toGrind)
            {
                string srcUid  = CardUtil.GetCardUniqueId(sourceCard);
                float srcQ2    = CardUtil.GetDurability(sourceCard, "SpecialDurability2");
                float srcQ2Max = CardUtil.GetDurabilityMax(sourceCard, "SpecialDurability2");
                float srcQ3    = CardUtil.GetDurability(sourceCard, "SpecialDurability3");
                float srcQ3Max = CardUtil.GetDurabilityMax(sourceCard, "SpecialDurability3");
                Logger?.LogDebug($"[GrindAll] grinding '{srcUid}' → '{resultId}': SD2={srcQ2:F1}/{srcQ2Max:F1}");

                // GiveCard returns void in EA 0.65 — SpawnService.Spawn always returns null.
                // Snapshot before spawn, then scan for new cards to transfer quality.
                var preIds = SnapshotCardIdsByUniqueId(resultId);
                SpawnService.Spawn(resultId);
                foreach (var newCard in EnumerateKnownCards())
                {
                    if (CardUtil.GetCardUniqueId(newCard) != resultId) continue;
                    if (newCard is UnityEngine.Object uo && preIds.Contains(uo.GetInstanceID())) continue;
                    ApplyMinimumQualityPercent(newCard, "SpecialDurability2", srcQ2, srcQ2Max);
                    ApplyMinimumQualityPercent(newCard, "SpecialDurability3", srcQ3, srcQ3Max);
                    RefreshCardDurabilityVisuals(newCard);
                }
            }

            EjectSourceCardsFromMill(mill, toGrind);
            return false;
        }

        private static void EjectSourceCardsFromMill(object mill, List<(object card, string resultId)> toGrind)
        {
            try
            {
                var cardSet = new HashSet<object>();
                foreach (var (card, _) in toGrind) cardSet.Add(card);

                var millSlots = CardUtil.GetInventoryList(mill);
                if (millSlots == null) return;

                var emptySlots = new List<object>();
                foreach (var slot in millSlots)
                {
                    if (slot == null) continue;
                    var innerCards = CardUtil.GetInventoryList(slot);
                    if (innerCards == null) continue;

                    var toRemove = new List<object>();
                    foreach (var inner in innerCards)
                    {
                        if (inner != null && cardSet.Contains(inner))
                            toRemove.Add(inner);
                    }
                    foreach (var card in toRemove)
                    {
                        innerCards.Remove(card);
                        TrySetActive(card, false);
                    }
                    if (innerCards.Count == 0)
                        emptySlots.Add(slot);
                }
                foreach (var slot in emptySlots)
                    millSlots.Remove(slot);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] EjectSourceCards error: {ex.Message}");
            }
        }

        private static void TrySetActive(object card, bool active)
        {
            try
            {
                if (card is UnityEngine.Object uo)
                {
                    var go = (uo as UnityEngine.Component)?.gameObject ?? uo as UnityEngine.GameObject;
                    go?.SetActive(active);
                }
            }
            catch { }
        }

        private static string ResolveGrindResult(object card)
        {
            try
            {
                if (card == null) return null;
                object cardData = GetCardData(card);
                if (cardData == null) return null;

                var ciField = cardData.GetType().GetField("CardInteractions", Flags);
                if (ciField == null) return null;
                var interactions = ciField.GetValue(cardData) as IList;
                if (interactions == null) return null;

                foreach (var ci in interactions)
                {
                    if (ci == null) continue;
                    var compatField = ci.GetType().GetField("CompatibleCards", Flags);
                    var compat = compatField?.GetValue(ci);
                    if (compat == null) continue;

                    var tagsField = compat.GetType().GetField("TriggerTags", Flags);
                    var tags = tagsField?.GetValue(compat) as IList;
                    if (tags == null || tags.Count == 0) continue;

                    bool matched = false;
                    foreach (var t in tags)
                    {
                        if (t == null) continue;
                        string tagName = (t is UnityEngine.Object uo) ? uo.name : null;
                        if (tagName == GrindingToolTag || tagName == HammerToolTag)
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched) continue;

                    var rccField = ci.GetType().GetField("ReceivingCardChanges", Flags);
                    var rcc = rccField?.GetValue(ci);
                    if (rcc == null) continue;

                    // Path A: TransformInto (most vanilla grind items)
                    var transformField = rcc.GetType().GetField("TransformInto", Flags);
                    var transformCard  = transformField?.GetValue(rcc);
                    if (transformCard != null)
                    {
                        var uidField = transformCard.GetType().GetField("UniqueID", Flags);
                        var uid = uidField?.GetValue(transformCard) as string;
                        if (!string.IsNullOrEmpty(uid)) return uid;
                    }

                    // Path B: ProducedCards[0].DroppedCards[0] fallback
                    var prodsFieldRcc = ci.GetType().GetField("ProducedCards", Flags);
                    var prodsRcc = prodsFieldRcc?.GetValue(ci) as IList;
                    if (prodsRcc == null || prodsRcc.Count == 0) continue;
                    var collRcc = prodsRcc[0];
                    var dropsRcc = collRcc?.GetType().GetField("DroppedCards", Flags)?.GetValue(collRcc) as IList;
                    if (dropsRcc == null || dropsRcc.Count == 0) continue;
                    var dropRcc = dropsRcc[0];
                    var dcRcc   = dropRcc?.GetType().GetField("DroppedCard", Flags)?.GetValue(dropRcc);
                    var uidRcc  = GetUniqueIdFromObject(dcRcc)
                               ?? dropRcc?.GetType().GetField("DroppedCardWarpData", Flags)?.GetValue(dropRcc) as string;
                    if (!string.IsNullOrEmpty(uidRcc)) return uidRcc;
                }
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] ResolveGrindResult error: {ex.Message}");
            }
            return null;
        }

        private static object GetCardData(object card)
        {
            try
            {
                if (card == null) return null;
                var cmProp = card.GetType().GetProperty("CardModel", Flags);
                return cmProp?.GetValue(card);
            }
            catch { return null; }
        }

        // ============================================================
        //  FISHPOND - "Other Fish" proportional catch (sturgeon/trout/char)
        // ============================================================
        // Stock Sturgeon/Trout/Char records the species in a per-pond dict, then
        // Catch Other Fish picks a result weighted by what was actually stocked.
        // Tracking is session-only — survives Filled↔Stocked swaps (same Unity
        // instance), but not save/load. After a load, dict is empty and the JSON
        // 33/33/33 fallback fires until the player stocks again.

        private static int GetPondInstanceId(object pond)
        {
            return (pond is UnityEngine.Object uo) ? uo.GetInstanceID() : 0;
        }

        private static void IncrementOtherFish(object pond, int s, int t, int c)
        {
            int id = GetPondInstanceId(pond);
            if (id == 0) return;
            _otherFish.TryGetValue(id, out var counts);
            counts.Sturgeon += s;
            counts.Trout    += t;
            counts.Charr    += c;
            _otherFish[id] = counts;
        }

        private static bool HandleCatchOtherFish(object pond)
        {
            try
            {
                int id = GetPondInstanceId(pond);
                if (id == 0) return true;
                if (!_otherFish.TryGetValue(id, out var counts)) return true;

                int total = counts.Sturgeon + counts.Trout + counts.Charr;
                if (total <= 0) return true;

                int roll = UnityEngine.Random.Range(0, total);
                string pickedGuid;
                if (roll < counts.Sturgeon)
                {
                    counts.Sturgeon--;
                    pickedGuid = SturgeonGUID;
                }
                else if (roll < counts.Sturgeon + counts.Trout)
                {
                    counts.Trout--;
                    pickedGuid = TroutGUID;
                }
                else
                {
                    counts.Charr--;
                    pickedGuid = CharGUID;
                }
                _otherFish[id] = counts;

                // Decrement the visible Special4 total so the pond stat matches the dict
                float cur = CardUtil.GetDurability(pond, "SpecialDurability4");
                if (!float.IsNaN(cur))
                    CardUtil.SetDurability(pond, "SpecialDurability4", Math.Max(0f, cur - 1f));

                // Spawn the picked fish on the board; the handled-action wrapper advances time
                // once after spawn, then initializes the finalized runtime fish card.
                var preIds = SnapshotFishCardIds();
                SpawnService.Spawn(pickedGuid);
                StartDelayedFishInitialization(preIds, pond);

                Logger?.Log(LogLevel.Debug,
                    $"[ActionIntercept] CatchOtherFish: picked {pickedGuid} (s={counts.Sturgeon} t={counts.Trout} c={counts.Charr})");

                return false; // skip JSON 33/33/33 ProducedCards
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] CatchOtherFish failed: {ex.Message}");
                return true; // fall back to JSON behavior on error
            }
        }

        // ============================================================
        //  FISHPOND - initialize stats on fish spawned by Catch / Ice Fish actions
        // ============================================================

        private static bool IsFishCatchAction(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "Catch Other Fish") return false;
            if (string.Equals(name, "Catch Crayfish", StringComparison.Ordinal)) return false;
            return name.StartsWith("Catch ", StringComparison.Ordinal) ||
                   name.StartsWith("Ice Fish ", StringComparison.Ordinal);
        }

        private static void StartDelayedFishInitialization(HashSet<int> preIds, object pond)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm is UnityEngine.MonoBehaviour mb)
                {
                    mb.StartCoroutine(ApplyFishStatsAfterSpawn(preIds, pond));
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] StartDelayedFishInitialization: {ex.Message}");
            }

            ApplyFishCaughtStats(preIds, FishCaughtQuality, includeZeroQualityExisting: true);
        }

        private static IEnumerator ApplyFishStatsAfterSpawn(HashSet<int> preIds, object pond)
        {
            yield return null;

            int updated = ApplyFishCaughtStats(preIds, FishCaughtQuality, includeZeroQualityExisting: true);
            if (updated == 0)
            {
                yield return null;
                updated = ApplyFishCaughtStats(preIds, FishCaughtQuality, includeZeroQualityExisting: true);
            }

            yield return SpendFishCatchPostSpawnTime(pond);

            if (updated == 0)
                ApplyFishCaughtStats(preIds, FishCaughtQuality, includeZeroQualityExisting: true);
        }

        private static HashSet<int> SnapshotFishCardIds()
        {
            var result = new HashSet<int>();
            try
            {
                foreach (var card in EnumerateKnownCards())
                {
                    if (!IsPondFishCard(card)) continue;
                    if (card is UnityEngine.Object uo) result.Add(uo.GetInstanceID());
                }
            }
            catch (Exception ex) { Logger?.LogWarning($"[ActionIntercept] SnapshotFishCardIds: {ex.Message}"); }
            return result;
        }

        private static int ApplyFishCaughtStats(HashSet<int> preIds, float quality, bool includeZeroQualityExisting = false)
        {
            int updated = 0;
            try
            {
                foreach (var c in EnumerateKnownCards())
                {
                    if (!(c is UnityEngine.Object uo)) continue;
                    if (!IsPondFishCard(c)) continue;

                    bool isPreExisting = preIds.Contains(uo.GetInstanceID());
                    if (isPreExisting && !(includeZeroQualityExisting && IsUninitializedPondFish(c))) continue;
                    if (_initializedFishCatchIds.Contains(uo.GetInstanceID()) && !IsUninitializedPondFish(c)) continue;

                    if (ApplyFishCaughtStatsToCard(c, quality))
                        updated++;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyFishCaughtStats: {ex.Message}"); }
            return updated;
        }

        private static bool IsUninitializedPondFish(object card)
        {
            string uniqueId = CardUtil.GetCardUniqueId(card);
            float quality = CardUtil.GetDurability(card, "SpecialDurability2");
            if (!float.IsNaN(quality) && quality <= 0.1f) return true;

            if (TryGetFreshCaughtFishWeight(uniqueId, out _))
            {
                float weight = CardUtil.GetDurability(card, "SpecialDurability1");
                if (!float.IsNaN(weight) && weight <= 0.1f) return true;
            }

            return false;
        }

        private static IEnumerator SpendFishCatchPostSpawnTime(object pond)
        {
            var spent = InvokeSpendDaytimePoints(FishCatchPostSpawnTicks, pond);
            if (spent == null)
            {
                yield return null;
                yield break;
            }

            while (spent.MoveNext())
                yield return spent.Current;
        }

        private static IEnumerator InvokeSpendDaytimePoints(int ticks, object fromCard)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return null;

                var method = _spendDaytimePointsMethod ?? gm.GetType().GetMethod("SpendDaytimePoints", Flags);
                if (method == null) return null;
                _spendDaytimePointsMethod = method;

                var args = new object[]
                {
                    ticks,
                    true,
                    true,
                    false,
                    fromCard,
                    Enum.ToObject(method.GetParameters()[5].ParameterType, 0),
                    string.Empty,
                    false,
                    false,
                    null,
                    null,
                    null,
                    true,
                    false
                };

                return method.Invoke(gm, args) as IEnumerator;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] FishCatch post-spawn time failed: {ex.InnerException?.Message ?? ex.Message}");
                return null;
            }
        }

        private static bool ApplyFishCaughtStatsToCard(object card, float quality)
        {
            string uniqueId = CardUtil.GetCardUniqueId(card);
            bool changed = CardUtil.SetDurability(card, "SpecialDurability2", quality);
            bool hasWeight = TryGetFreshCaughtFishWeight(uniqueId, out float weight);
            if (hasWeight)
                changed |= CardUtil.SetDurability(card, "SpecialDurability1", weight);

            if (!changed) return false;

            if (card is UnityEngine.Object uo)
                _initializedFishCatchIds.Add(uo.GetInstanceID());

            Logger?.LogDebug($"[ActionIntercept] FishCatch: initialized {uniqueId} quality={quality} weight={(hasWeight ? weight.ToString("F0") : "default")}");
            return true;
        }

        private static bool TryGetFreshCaughtFishWeight(string uniqueId, out float weight)
        {
            weight = 0f;
            if (string.IsNullOrEmpty(uniqueId)) return false;
            if (!PondFishWeightDefaults.TryGetValue(uniqueId, out var defaults)) return false;

            weight = defaults.BaseWeight;
            if (defaults.RandomOffset > 0f)
                weight += UnityEngine.Random.Range(0f, defaults.RandomOffset);
            if (defaults.MaxWeight > 0f)
                weight = Math.Min(weight, defaults.MaxWeight);

            return weight > 0f;
        }

        private static bool IsPondFishCard(object card)
        {
            string uid = CardUtil.GetCardUniqueId(card);
            return uid != null && PondFishGuids.Contains(uid);
        }

        // ============================================================
        //  WORKSHOP LUMP - initialize stats on lumps spawned by Cast Metal Lump
        // ============================================================

        private static HashSet<int> SnapshotCardIdsByUniqueId(string uniqueId)
        {
            var result = new HashSet<int>();
            try
            {
                foreach (var card in EnumerateKnownCards())
                {
                    if (CardUtil.GetCardUniqueId(card) != uniqueId) continue;
                    if (card is UnityEngine.Object uo) result.Add(uo.GetInstanceID());
                }
            }
            catch (Exception ex) { Logger?.LogWarning($"[ActionIntercept] SnapshotCardIdsByUniqueId: {ex.Message}"); }
            return result;
        }

        private static void StartDelayedLumpInitialization(HashSet<int> preIds)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm is UnityEngine.MonoBehaviour mb)
                {
                    mb.StartCoroutine(ApplyLumpStatsAfterSpawn(preIds));
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] StartDelayedLumpInitialization: {ex.Message}");
            }
            ApplyLumpSpawnStats(preIds);
        }

        private static IEnumerator ApplyLumpStatsAfterSpawn(HashSet<int> preIds)
        {
            bool updatedNewCard = false;
            for (int attempt = 0; attempt < LumpInitializationRetryFrames; attempt++)
            {
                yield return null;
                updatedNewCard |= ApplyLumpSpawnStats(preIds, allowExistingFallback: false) > 0;
            }

            if (!updatedNewCard)
                ApplyLumpSpawnStats(preIds, allowExistingFallback: true);
        }

        private static int ApplyLumpSpawnStats(HashSet<int> preIds, bool allowExistingFallback = true)
        {
            int updated = 0;
            try
            {
                var latest = FindLatestKnownCardByUniqueId(UnfinishedLumpID);
                bool latestIsPreExisting = latest is UnityEngine.Object latestObject && preIds.Contains(latestObject.GetInstanceID());
                if (latest != null && (!latestIsPreExisting || allowExistingFallback) && ApplyLumpSpawnStatsToCard(latest, allowExistingCard: latestIsPreExisting))
                    updated++;

                foreach (var c in EnumerateKnownCards())
                {
                    if (!(c is UnityEngine.Object uo)) continue;
                    if (preIds.Contains(uo.GetInstanceID())) continue;
                    if (CardUtil.GetCardUniqueId(c) != UnfinishedLumpID) continue;

                    if (ApplyLumpSpawnStatsToCard(c, allowExistingCard: false))
                        updated++;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyLumpSpawnStats: {ex.Message}"); }
            return updated;
        }

        private static bool ApplyLumpSpawnStatsToCard(object card, bool allowExistingCard)
        {
            if (CardUtil.GetCardUniqueId(card) != UnfinishedLumpID) return false;

            if (allowExistingCard && !LumpNeedsQualityInitialization(card))
                return false;

            bool changed = CardUtil.SetDurability(card, "SpecialDurability1", LumpInitialStrikes);
            changed |= SetMinimumDurabilityStatValue(card, "SpecialDurability2", LumpMinimumQuality);
            changed |= SetMinimumDurabilityStatValue(card, "SpecialDurability3", LumpMinimumQuality);
            if (!changed) return false;

            RefreshCardDurabilityVisuals(card);
            Logger?.LogDebug($"[ActionIntercept] LumpInit: strikes={LumpInitialStrikes} metal_quality>={LumpMinimumQuality} quality>={LumpMinimumQuality}");
            return true;
        }

        private static bool LumpNeedsQualityInitialization(object card)
        {
            float metalQuality = CardUtil.GetDurability(card, "SpecialDurability2");
            float quality = CardUtil.GetDurability(card, "SpecialDurability3");
            return float.IsNaN(metalQuality) || metalQuality < LumpMinimumQuality
                || float.IsNaN(quality) || quality < LumpMinimumQuality;
        }

        private static object FindLatestKnownCardByUniqueId(string uniqueId)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return null;
                var gmType = gm.GetType();
                foreach (string listName in new[] { "LatestCreatedCards", "AllCards" })
                {
                    var cards = gmType.GetField(listName, Flags)?.GetValue(gm) as IList
                             ?? gmType.GetProperty(listName, Flags)?.GetValue(gm) as IList;
                    if (cards == null) continue;
                    for (int i = cards.Count - 1; i >= 0; i--)
                    {
                        var card = cards[i];
                        if (card != null && CardUtil.GetCardUniqueId(card) == uniqueId)
                            return card;
                    }
                }
            }
            catch (Exception ex) { Logger?.LogWarning($"[ActionIntercept] FindLatestKnownCardByUniqueId: {ex.Message}"); }
            return null;
        }

        private static IEnumerable<object> EnumerateKnownCards()
        {
            var seen = new HashSet<int>();

            foreach (var card in EnumerateGameManagerAllCards())
            {
                if (card == null) continue;
                if (card is UnityEngine.Object uo && !seen.Add(uo.GetInstanceID())) continue;
                yield return card;
            }

            if (_cardBaseType == null)
                _cardBaseType = AccessTools.TypeByName("InGameCardBase");
            if (_cardBaseType == null) yield break;

            UnityEngine.Object[] sceneCards = null;
            try { sceneCards = UnityEngine.Object.FindObjectsOfType(_cardBaseType); }
            catch (Exception ex) { Logger?.LogWarning($"[ActionIntercept] EnumerateKnownCards scene fallback: {ex.Message}"); }
            if (sceneCards == null) yield break;

            foreach (var card in sceneCards)
            {
                if (card == null) continue;
                if (!seen.Add(card.GetInstanceID())) continue;
                yield return card;
            }
        }

        private static IEnumerable<object> EnumerateGameManagerAllCards()
        {
            IList cards = null;
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm != null)
                {
                    var gmType = gm.GetType();
                    cards = gmType.GetField("AllCards", Flags)?.GetValue(gm) as IList
                         ?? gmType.GetProperty("AllCards", Flags)?.GetValue(gm) as IList;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogWarning($"[ActionIntercept] EnumerateGameManagerAllCards: {ex.Message}");
            }

            if (cards == null) yield break;

            foreach (var card in cards)
                if (card != null) yield return card;
        }

        // ============================================================
        //  IRON ITEM SMELTING — set Metal Type=200 + quality on spawned iron nuggets
        //  Iron items (Parts/Bearing/Axle/Wrench) use progress-based OnFull to smelt.
        //  ProducedCards spawns MetalNugget x4 at SD4=0; we fix type and quality here.
        // ============================================================

        static void GiveCard_Postfix(object __0)  // __0 = CardData; GiveCard returns void, __result is always null
        {
            try
            {
                if (__0 == null) return;
                if (CardUtil.GetCardUniqueId(__0) != CopperNuggetGUID) return;

                float sd4 = CardUtil.GetDurability(__0, "SpecialDurability4");
                if (!float.IsNaN(sd4) && sd4 > 0f) return; // already typed by sluice or other path

                if (IsIronSmeltPending())
                {
                    bool ok = CardUtil.SetDurability(__0, "SpecialDurability4", IronBarMetalType);
                    ok &= SetMinimumDurabilityStatValue(__0, "SpecialDurability1", NuggetSmeltQuality);
                    ok &= SetMinimumDurabilityStatValue(__0, "SpecialDurability2", NuggetSmeltQuality);
                    ok &= SetMinimumDurabilityStatValue(__0, "SpecialDurability3", NuggetSmeltQuality);
                    if (ok) Logger?.LogDebug("[ActionIntercept] IronSmelt (GiveCard): set Type=200 quality=50 on iron nugget");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] GiveCard_Postfix: {ex.Message}");
            }
        }

        private static bool IsIronSmeltPending()
        {
            try
            {
                foreach (var card in EnumerateKnownCards())
                {
                    if (!IronSmeltItemIds.Contains(CardUtil.GetCardUniqueId(card))) continue;
                    float progress = CardUtil.GetDurability(card, "Progress");
                    float max = CardUtil.GetDurabilityMax(card, "Progress");
                    if (!float.IsNaN(progress) && !float.IsNaN(max) && max > 0f && progress >= max)
                        return true;
                }
            }
            catch { }
            return false;
        }

        // ActionRouter's After callback is synchronous and cannot itself yield an extra
        // frame, so a failed first attempt (e.g. GiveCard hasn't registered the new
        // nugget in AllCards yet) is retried via a decoupled one-frame-later coroutine —
        // the same pattern as StartDelayedNuggetInitialization/StartDelayedLumpInitialization.
        private static void StartDelayedIronBarTypeRetry(HashSet<int> preIds)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm is UnityEngine.MonoBehaviour mb)
                {
                    mb.StartCoroutine(RetryIronBarTypeAfterOneFrame(preIds));
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] StartDelayedIronBarTypeRetry: {ex.Message}");
            }
            ApplyIronBarType(preIds);
        }

        private static IEnumerator RetryIronBarTypeAfterOneFrame(HashSet<int> preIds)
        {
            yield return null;
            ApplyIronBarType(preIds);
        }

        private static int ApplyIronBarType(HashSet<int> preIds)
        {
            int updated = 0;
            try
            {
                foreach (var card in EnumerateKnownCards())
                {
                    if (CardUtil.GetCardUniqueId(card) != CopperNuggetGUID) continue;
                    if (card is UnityEngine.Object uo && preIds != null && preIds.Contains(uo.GetInstanceID())) continue;

                    float sd4 = CardUtil.GetDurability(card, "SpecialDurability4");
                    if (!float.IsNaN(sd4) && sd4 > 0f) continue; // already typed

                    bool ok = CardUtil.SetDurability(card, "SpecialDurability4", IronBarMetalType);
                    ok &= SetMinimumDurabilityStatValue(card, "SpecialDurability1", NuggetSmeltQuality);
                    ok &= SetMinimumDurabilityStatValue(card, "SpecialDurability2", NuggetSmeltQuality);
                    ok &= SetMinimumDurabilityStatValue(card, "SpecialDurability3", NuggetSmeltQuality);
                    if (ok) updated++;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyIronBarType: {ex.Message}"); }

            if (updated > 0)
                Logger?.LogDebug($"[ActionIntercept] IronSmelt: set Type=200 quality=50 on {updated} iron nugget(s)");
            return updated;
        }

        // ============================================================
        //  WATER-DRIVEN FORGE - apply one hammer strike to all inventory items
        // ============================================================
        // Frame-level dedup for these action handlers is now owned by ActionRouter
        // (ActionHandler.LastDispatchFrame) — the same button press firing through
        // both PerformStackActionRoutine and ActionRoutine no longer needs a local
        // _xxxDrainDepth / _lastXxxFrame guard here.
        private static readonly HashSet<int> _initializedFishCatchIds = new HashSet<int>();
        private static MethodInfo _spendDaytimePointsMethod;
        private static Type _cardBaseType;

        private static bool HandleHammerAllInner(object forge, bool workshopQualityBoost)
        {
            var inventory = CardUtil.GetInventoryList(forge);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] HammerAll: empty forge inventory");
                return false;
            }

            var toComplete = new List<(object card, string resultId, float special1Change, float fuelChange, string[] transferStats)>();
            var toAdvance  = new List<(object card, float special1Change, float fuelChange)>();

            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;
                var innerCards = CardUtil.GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner == null) continue;
                        ClassifyHammerItem(inner, toComplete, toAdvance);
                    }
                }
                else
                {
                    ClassifyHammerItem(slotItem, toComplete, toAdvance);
                }
            }

            // Quality-only pass: items with Metal Quality (SD2) but no Smith CI
            // (e.g., heated copper/tin/bronze nuggets placed in the workshop for quality work)
            var qualityOnlyItems = new List<object>();
            if (workshopQualityBoost)
            {
                var alreadyProcessed = new HashSet<object>();
                foreach (var (card, _, _) in toAdvance) alreadyProcessed.Add(card);
                foreach (var (card, _, _, _, _) in toComplete) alreadyProcessed.Add(card);

                foreach (var slotItem in inventory)
                {
                    if (slotItem == null) continue;
                    var innerCards = CardUtil.GetInventoryList(slotItem);
                    if (innerCards != null && innerCards.Count > 0)
                    {
                        foreach (var inner in innerCards)
                        {
                            if (inner != null && !alreadyProcessed.Contains(inner) && IsMetalQualityTool(inner))
                                qualityOnlyItems.Add(inner);
                        }
                    }
                    else if (!alreadyProcessed.Contains(slotItem) && IsMetalQualityTool(slotItem))
                    {
                        qualityOnlyItems.Add(slotItem);
                    }
                }
            }

            if (toComplete.Count == 0 && toAdvance.Count == 0 && qualityOnlyItems.Count == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] HammerAll: no hammerable items found");
                return false;
            }

            Logger?.Log(LogLevel.Debug,
                $"[ActionIntercept] HammerAll: {toAdvance.Count} advance, {toComplete.Count} complete, {qualityOnlyItems.Count} quality-only");

            if (workshopQualityBoost)
            {
                int boosted = 0;
                foreach (var (card, _, _) in toAdvance)
                    if (ApplyWorkshopQualityBoost(card)) boosted++;
                foreach (var (card, _, _, _, _) in toComplete)
                    if (ApplyWorkshopQualityBoost(card)) boosted++;
                foreach (var card in qualityOnlyItems)
                {
                    if (ApplyWorkshopQualityBoost(card)) boosted++;
                    // Accumulating-counter items (e.g. ACT shaped metal): no on-zero transform,
                    // so SD1 counts strikes UP. Increment by 1 per Hammer All press.
                    IncrementAccumulatingStrikeCount(card);
                    RefreshCardDurabilityVisuals(card);
                }
                if (boosted > 0)
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] HammerAll: workshop quality boosted {boosted} item(s)");
            }

            // Apply one hit to items that don't finish yet
            foreach (var (card, s1Change, fuelChange) in toAdvance)
            {
                float cur = CardUtil.GetDurability(card, "SpecialDurability1");
                if (!float.IsNaN(cur))
                {
                    CardUtil.SetDurability(card, "SpecialDurability1", Math.Max(0f, cur + s1Change));
                    Logger?.Log(LogLevel.Debug,
                        $"[ActionIntercept] HammerAll: Strikes {cur} -> {Math.Max(0f, cur + s1Change)}");
                }
                if (fuelChange != 0f)
                {
                    float curFuel = CardUtil.GetDurability(card, "FuelCapacity");
                    if (!float.IsNaN(curFuel))
                        CardUtil.SetDurability(card, "FuelCapacity", Math.Max(0f, curFuel + fuelChange));
                }
                RefreshCardDurabilityVisuals(card);
            }

            foreach (var (card, resultId, s1Change, fuelChange, transferStats) in toComplete)
                CompleteHammeredCard(card, resultId, s1Change, fuelChange, transferStats);

            RefreshOpenInventoryPopup();
            return false;
        }

        private static bool HandleWorkshopCraft(object workshop, WorkshopCraftKind kind)
        {
            if (kind == WorkshopCraftKind.None) return false;

            int cost = GetCraftCost(kind);
            if (!TryConsumeCopperNuggets(workshop, cost))
            {
                Logger?.LogDebug($"[ActionIntercept] {kind}: not enough copper nuggets in workshop inventory at completion");
                return false;
            }

            string resultId;
            int resultCount;
            switch (kind)
            {
                case WorkshopCraftKind.CopperSheet:
                    resultId = CopperSheetID;
                    resultCount = 1;
                    break;
                case WorkshopCraftKind.CopperNails:
                    resultId = CopperNailsID;
                    resultCount = 6;
                    break;
                case WorkshopCraftKind.CopperLump:
                    resultId = UnfinishedLumpID;
                    resultCount = 1;
                    break;
                default:
                    return false;
            }

            var lumpPreIds = (kind == WorkshopCraftKind.CopperLump)
                ? SnapshotCardIdsByUniqueId(UnfinishedLumpID)
                : null;

            for (int i = 0; i < resultCount; i++)
                SpawnService.Spawn(resultId);

            if (lumpPreIds != null)
                StartDelayedLumpInitialization(lumpPreIds);

            RefreshOpenInventoryPopup();
            Logger?.LogDebug($"[ActionIntercept] {kind}: consumed {cost} copper nuggets, spawned {resultCount} {resultId}");
            return true;
        }

        private static void ClassifyHammerItem(
            object card,
            List<(object, string, float, float, string[])> toComplete,
            List<(object, float, float)> toAdvance)
        {
            var hit = GetHammerHitInfo(card);
            if (!hit.canHammer) return;

            float curS1 = CardUtil.GetDurability(card, "SpecialDurability1");
            if (float.IsNaN(curS1)) return;

            // Skip items whose SD1 is at 0 — these are spawned with default FloatValue=0.
            // Exception: UnfinishedLumps may not have been initialized yet (async coroutine).
            // Eagerly initialize them on the spot so Hammer All works immediately after Cast.
            if (curS1 <= 0f)
            {
                if (CardUtil.GetCardUniqueId(card) == UnfinishedLumpID)
                {
                    ApplyLumpSpawnStatsToCard(card, allowExistingCard: true);
                    curS1 = CardUtil.GetDurability(card, "SpecialDurability1");
                    if (curS1 <= 0f) return;
                }
                else
                    return;
            }

            float newS1 = curS1 + hit.special1Change; // special1Change is negative (e.g., -1)
            if (newS1 <= 0f && hit.onZeroResultId != null)
                toComplete.Add((card, hit.onZeroResultId, hit.special1Change, hit.fuelChange, hit.transferStats));
            else
                toAdvance.Add((card, Math.Max(hit.special1Change, -curS1), hit.fuelChange));
        }

        private static void CompleteHammeredCard(object card, string resultId, float special1Change, float fuelChange, string[] transferStats)
        {
            try
            {
                float currentStrikes = CardUtil.GetDurability(card, "SpecialDurability1");
                if (!float.IsNaN(currentStrikes))
                    CardUtil.SetDurability(card, "SpecialDurability1", Math.Max(0f, currentStrikes + special1Change));

                if (fuelChange != 0f)
                {
                    float currentFuel = CardUtil.GetDurability(card, "FuelCapacity");
                    if (!float.IsNaN(currentFuel))
                        CardUtil.SetDurability(card, "FuelCapacity", Math.Max(0f, currentFuel + fuelChange));
                }

                var transferred = CaptureDurabilityValues(card, transferStats);
                // Quality (SD2/SD3) is always carried by percentage of max — the
                // transformed CardData's stat scale can differ from the source's,
                // and machine processing must never lower quality.
                float q2Val = CardUtil.GetDurability(card, "SpecialDurability2");
                float q2Max = CardUtil.GetDurabilityMax(card, "SpecialDurability2");
                float q3Val = CardUtil.GetDurability(card, "SpecialDurability3");
                float q3Max = CardUtil.GetDurabilityMax(card, "SpecialDurability3");
                Logger?.LogDebug($"[HammerDiag] completing '{CardUtil.GetCardUniqueId(card)}' → '{resultId}': q2={q2Val:F1}/{q2Max:F1} q3={q3Val:F1}/{q3Max:F1}");
                if (!TransformCardInPlace(card, resultId))
                {
                    Logger?.LogError($"[ActionIntercept] HammerAll: failed to transform completed item into '{resultId}'");
                    return;
                }
                RestoreDurabilityValues(card, transferred);
                ApplyMinimumQualityPercent(card, "SpecialDurability2", q2Val, q2Max);
                ApplyMinimumQualityPercent(card, "SpecialDurability3", q3Val, q3Max);
                Logger?.LogDebug($"[HammerDiag] post-restore: SD2={CardUtil.GetDurability(card, "SpecialDurability2"):F1} SD3={CardUtil.GetDurability(card, "SpecialDurability3"):F1}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] HammerAll: completion failed: {ex.Message}");
            }
        }

        private static Dictionary<string, float> CaptureDurabilityValues(object card, string[] statNames)
        {
            var values = new Dictionary<string, float>(StringComparer.Ordinal);
            if (statNames == null) return values;
            foreach (var statName in statNames)
            {
                float value = CardUtil.GetDurability(card, statName);
                if (!float.IsNaN(value)) values[statName] = value;
            }
            return values;
        }

        private static void RestoreDurabilityValues(object card, Dictionary<string, float> values)
        {
            foreach (var kvp in values)
                CardUtil.SetDurability(card, kvp.Key, kvp.Value);
        }

        private static bool ApplyWorkshopQualityBoost(object card)
        {
            if (!IsMetalQualityTool(card)) return false;

            float currentQuality = CardUtil.GetDurability(card, "SpecialDurability2");
            if (float.IsNaN(currentQuality)) return false;

            float maxQuality = CardUtil.GetDurabilityMax(card, "SpecialDurability2");
            if (float.IsNaN(maxQuality) || maxQuality <= 0f) maxQuality = 100f;

            float newQuality = Math.Min(maxQuality, currentQuality + WorkshopMetalQualityBoost);
            if (newQuality <= currentQuality) return false;

            return CardUtil.SetDurability(card, "SpecialDurability2", newQuality);
        }

        // Increments SD1 "Strikes" by +1 for ACT items that accumulate strikes upward
        // (HasActionOnZero = false means there's no transform — just a running counter).
        private static void IncrementAccumulatingStrikeCount(object card)
        {
            try
            {
                object cardData = GetCardData(card);
                if (cardData == null) return;

                if (!IsDurabilityDefinitionActive(cardData, "SpecialDurability1")) return;
                if (!DurabilityStatNameContains(cardData, "SpecialDurability1", "Strike")) return;

                // Only accumulating-counter items (no on-zero transform)
                var sd1Def = cardData.GetType().GetField("SpecialDurability1", Flags)?.GetValue(cardData);
                var hasOnZero = sd1Def?.GetType().GetField("HasActionOnZero", Flags)?.GetValue(sd1Def);
                if (hasOnZero is bool b && b) return; // has on-zero transform — handled by ClassifyHammerItem

                float cur = CardUtil.GetDurability(card, "SpecialDurability1");
                if (float.IsNaN(cur)) return;
                float max = CardUtil.GetDurabilityMax(card, "SpecialDurability1");
                if (!float.IsNaN(max) && max > 0f && cur >= max) return; // already at max

                CardUtil.SetDurability(card, "SpecialDurability1", cur + 1f);
            }
            catch { }
        }

        private static bool IsMetalQualityTool(object card)
        {
            object cardData = GetCardData(card);
            if (cardData == null) return false;

            return IsDurabilityDefinitionActive(cardData, "SpecialDurability2")
                && DurabilityStatNameContains(cardData, "SpecialDurability2", "Quality")
                && HasAnyCardTag(cardData, "tag_Metal", "tag_ToolBlank", "tag_CopperSmall", "tag_CopperBig");
        }

        private static bool IsDurabilityDefinitionActive(object cardData, string statName)
        {
            try
            {
                var def = cardData?.GetType().GetField(statName, Flags)?.GetValue(cardData);
                var active = def?.GetType().GetField("Active", Flags)?.GetValue(def);
                return active is bool b && b;
            }
            catch { return false; }
        }

        private static bool DurabilityStatNameContains(object cardData, string statName, string text)
        {
            try
            {
                var def = cardData?.GetType().GetField(statName, Flags)?.GetValue(cardData);
                var statNameObj = def?.GetType().GetField("CardStatName", Flags)?.GetValue(def);
                var defaultText = statNameObj?.GetType().GetField("DefaultText", Flags)?.GetValue(statNameObj) as string;
                return !string.IsNullOrEmpty(defaultText)
                    && defaultText.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static bool HasAnyCardTag(object cardData, params string[] tagNames)
        {
            try
            {
                var wanted = new HashSet<string>(tagNames, StringComparer.OrdinalIgnoreCase);
                if (HasAnyTagInList(cardData?.GetType().GetField("CardTags", Flags)?.GetValue(cardData) as IList, wanted))
                    return true;
                if (HasAnyTagInList(cardData?.GetType().GetField("CardTagsWarpData", Flags)?.GetValue(cardData) as IList, wanted))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool HasAnyTagInList(IList tags, HashSet<string> wanted)
        {
            if (tags == null) return false;
            foreach (var tag in tags)
            {
                string tagName = GetTagName(tag);
                if (tagName != null && wanted.Contains(tagName)) return true;
            }
            return false;
        }

        private static string GetTagName(object tag)
        {
            if (tag == null) return null;
            if (tag is string s) return s;
            if (tag is UnityEngine.Object uo) return uo.name;
            return tag.GetType().GetField("UniqueID", Flags)?.GetValue(tag) as string
                ?? tag.GetType().GetField("name", Flags)?.GetValue(tag) as string
                ?? tag.GetType().GetProperty("name", Flags)?.GetValue(tag, null) as string;
        }

        private struct HammerHitInfo
        {
            public bool   canHammer;
            public float  special1Change; // negative per-hit decrement, e.g. -1.0
            public float  fuelChange;     // negative heat loss per hit, e.g. -25.0
            public string onZeroResultId; // UniqueID to spawn when Strikes reach 0
            public string[] transferStats; // Durability values transferred by OnZero.ReceivingCardChanges
        }

        private static HammerHitInfo GetHammerHitInfo(object card)
        {
            var info = new HammerHitInfo();
            try
            {
                object cardData = GetCardData(card);
                if (cardData == null) return info;
                Type cdType = cardData.GetType();

                // 1. Find the smithing CardInteraction.
                var ciField = cdType.GetField("CardInteractions", Flags);
                var cis = ciField?.GetValue(cardData) as IList;
                if (cis == null) return info;

                foreach (var ci in cis)
                {
                    if (ci == null) continue;

                    bool isHammer = string.Equals(CardUtil.GetActionName(ci), "Smith", StringComparison.OrdinalIgnoreCase);
                    if (!isHammer)
                    {
                        var compat = ci.GetType().GetField("CompatibleCards", Flags)?.GetValue(ci);
                        var trigTags = compat?.GetType().GetField("TriggerTags", Flags)?.GetValue(compat) as IList;
                        if (trigTags != null)
                        {
                            foreach (var t in trigTags)
                            {
                                string tn = (t is UnityEngine.Object uo) ? uo.name : null;
                                if (tn == "tag_Hammer" || tn == HammerToolTag) { isHammer = true; break; }
                            }
                        }
                    }
                    if (!isHammer) continue;

                    var rcc = ci.GetType().GetField("ReceivingCardChanges", Flags)?.GetValue(ci);
                    if (rcc != null)
                    {
                        var s1Raw = rcc.GetType().GetField("Special1Change", Flags)?.GetValue(rcc);
                        if (s1Raw is UnityEngine.Vector2 s1v) info.special1Change = s1v.x;
                        var fRaw = rcc.GetType().GetField("FuelChange", Flags)?.GetValue(rcc);
                        if (fRaw is UnityEngine.Vector2 fv) info.fuelChange = fv.x;
                    }
                    if (info.special1Change >= 0f) continue;

                    info.canHammer = true;
                    break;
                }
                if (!info.canHammer) return info;

                // 2. Read SpecialDurability1.OnZero result from CardData definition
                var sd1Def = cdType.GetField("SpecialDurability1", Flags)?.GetValue(cardData);
                if (sd1Def == null) return info;
                var hasOnZero = sd1Def.GetType().GetField("HasActionOnZero", Flags)?.GetValue(sd1Def);
                if (hasOnZero is bool b && !b) return info;

                var onZero = sd1Def.GetType().GetField("OnZero", Flags)?.GetValue(sd1Def);
                var onZeroRcc = onZero?.GetType().GetField("ReceivingCardChanges", Flags)?.GetValue(onZero);
                if (onZeroRcc != null)
                {
                    info.transferStats = ReadTransferStats(onZeroRcc);
                    var transformCard = onZeroRcc.GetType().GetField("TransformInto", Flags)?.GetValue(onZeroRcc);
                    info.onZeroResultId = GetUniqueIdFromObject(transformCard);
                    if (string.IsNullOrEmpty(info.onZeroResultId))
                        info.onZeroResultId = onZeroRcc.GetType().GetField("TransformIntoWarpData", Flags)?.GetValue(onZeroRcc) as string;
                    if (!string.IsNullOrEmpty(info.onZeroResultId)) return info;
                }

                var prods  = onZero?.GetType().GetField("ProducedCards", Flags)?.GetValue(onZero) as IList;
                if (prods == null || prods.Count == 0) return info;

                var coll   = prods[0];
                var drops  = coll?.GetType().GetField("DroppedCards", Flags)?.GetValue(coll) as IList;
                if (drops == null || drops.Count == 0) return info;

                var drop   = drops[0];
                var dc     = drop?.GetType().GetField("DroppedCard", Flags)?.GetValue(drop);
                info.onZeroResultId = GetUniqueIdFromObject(dc);
                if (string.IsNullOrEmpty(info.onZeroResultId))
                    info.onZeroResultId = drop?.GetType().GetField("DroppedCardWarpData", Flags)?.GetValue(drop) as string;
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] GetHammerHitInfo error: {ex.Message}");
            }
            return info;
        }

        private static string[] ReadTransferStats(object cardStateChange)
        {
            var stats = new List<string>();
            if (GetBoolField(cardStateChange, "TransferSpoilage")) stats.Add("SpoilageTime");
            if (GetBoolField(cardStateChange, "TransferUsage")) stats.Add("UsageDurability");
            if (GetBoolField(cardStateChange, "TransferFuel")) stats.Add("FuelCapacity");
            if (GetBoolField(cardStateChange, "TransferProgress")) stats.Add("Progress");
            if (GetBoolField(cardStateChange, "TransferSpecial1")) stats.Add("SpecialDurability1");
            // SD2/SD3 (quality) intentionally absent: CompleteHammeredCard always carries
            // them by percentage of max via ApplyMinimumQualityPercent, never raw.
            if (GetBoolField(cardStateChange, "TransferSpecial4")) stats.Add("SpecialDurability4");
            return stats.ToArray();
        }

        private static bool GetBoolField(object obj, string fieldName)
        {
            try
            {
                var value = obj?.GetType().GetField(fieldName, Flags)?.GetValue(obj);
                return value is bool b && b;
            }
            catch { return false; }
        }

        private static string GetUniqueIdFromObject(object value)
        {
            try
            {
                if (value == null) return null;
                if (value is UniqueIDScriptable uid) return uid.UniqueID;
                return value.GetType().GetField("UniqueID", Flags)?.GetValue(value) as string;
            }
            catch { return null; }
        }

        private static bool SetMinimumDurabilityStatValue(object card, string statName, float minimumValue)
        {
            float current = CardUtil.GetDurability(card, statName);
            if (!float.IsNaN(current) && current >= minimumValue) return false;
            return CardUtil.SetDurability(card, statName, minimumValue);
        }

        // Machine processing must never lower quality. Stat MaxValues differ between
        // source and output cards (e.g. dried herb Quality max=144 vs powder max=192),
        // so a raw value copy silently drops the percentage — always map by percentage
        // of max and only ever raise the target's value.
        private static void ApplyMinimumQualityPercent(object target, string statName, float srcVal, float srcMax)
        {
            if (float.IsNaN(srcVal) || srcVal <= 0f) return;
            float pct = (!float.IsNaN(srcMax) && srcMax > 0f) ? srcVal / srcMax : srcVal / 100f;
            float dstMax = CardUtil.GetDurabilityMax(target, statName);
            if (float.IsNaN(dstMax) || dstMax <= 0f)
                dstMax = (!float.IsNaN(srcMax) && srcMax > 0f) ? srcMax : 100f;
            SetMinimumDurabilityStatValue(target, statName, pct * dstMax);
        }

        private static void TransferQualityPercent(object source, object target, string statName)
        {
            ApplyMinimumQualityPercent(target, statName,
                CardUtil.GetDurability(source, statName),
                CardUtil.GetDurabilityMax(source, statName));
        }

        private static void RefreshCardDurabilityVisuals(object card)
        {
            try
            {
                var visuals = card?.GetType().GetProperty("CardVisuals", Flags)?.GetValue(card)
                           ?? card?.GetType().GetField("CardVisuals", Flags)?.GetValue(card);
                var refresh = visuals?.GetType().GetMethod("RefreshDurabilities", Flags);
                refresh?.Invoke(visuals, null);
            }
            catch { }
        }

        private static void EjectCardsFromStructure(object structure, IEnumerable<object> cards)
        {
            try
            {
                var cardSet   = new HashSet<object>(cards);
                var slots     = CardUtil.GetInventoryList(structure);
                if (slots == null) return;
                var emptySlots = new List<object>();

                foreach (var slot in slots)
                {
                    if (slot == null) continue;
                    var inner = CardUtil.GetInventoryList(slot);
                    if (inner != null)
                    {
                        var toRemove = new List<object>();
                        foreach (var c in inner)
                            if (c != null && cardSet.Contains(c)) toRemove.Add(c);
                        foreach (var c in toRemove) { inner.Remove(c); TrySetActive(c, false); }
                        if (inner.Count == 0) emptySlots.Add(slot);
                    }
                    else if (cardSet.Contains(slot))
                    {
                        emptySlots.Add(slot);
                        TrySetActive(slot, false);
                    }
                }
                foreach (var slot in emptySlots) slots.Remove(slot);
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] EjectCardsFromStructure error: {ex.Message}");
            }
        }

        // ============================================================
        //  ORE SLUICE - process mud piles for greenstone, flint, and stone
        // ============================================================
        private static readonly System.Random _sluiceRng = new System.Random();

        private static bool HandleSluiceAllInner(object sluice)
        {
            Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: HandleSluiceAll called, sluice type={sluice?.GetType().Name}");
            var inventory = CardUtil.GetInventoryList(sluice);
            if (inventory == null)
            {
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: inventory is NULL for type={sluice?.GetType().Name}");
                return false;
            }

            Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: inventory count={inventory.Count}");

            // Collect processable slots.  Inventory items are InventorySlots
            // that wrap a stack of N cards (displayed as "x6" in UI).
            // We track each slot + its inner card list for proper removal.
            var slotsToProcess = new List<(object slot, IList innerCards, string sourceUid)>();
            int totalRolls = 0;

            foreach (var item in inventory)
            {
                if (item == null) continue;

                // EA 0.63+: inventory returns InventorySlot wrappers — drill into inner cards first.
                // EA 0.62b: inventory returns InGameCardBase directly — innerCards will be null.
                var innerCards = CardUtil.GetInventoryList(item);
                if (innerCards != null && innerCards.Count > 0)
                {
                    // Check UID from first inner card (all cards in a stack share the same type)
                    string uid = CardUtil.GetCardUniqueId(innerCards[0]);
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: slot uid='{uid}' ({innerCards.Count} inner), slotType={item.GetType().Name}");
                    if (uid != MudPileGUID && uid != DirtPileGUID && uid != FineDirtGUID)
                        continue;
                    totalRolls += innerCards.Count;
                    slotsToProcess.Add((item, innerCards, uid));
                }
                else
                {
                    // Direct card — check UID on the item itself
                    string uid = CardUtil.GetCardUniqueId(item);
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: item uid='{uid}', type={item.GetType().Name}");
                    if (uid != MudPileGUID && uid != DirtPileGUID && uid != FineDirtGUID)
                        continue;
                    int count = Math.Max(1, GetCardCharges(item));
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: direct card, count={count}");
                    totalRolls += count;
                    slotsToProcess.Add((item, null, uid));
                }
            }

            if (totalRolls == 0)
            {
                Logger?.Log(LogLevel.Debug, "[ActionIntercept] Sluice: no mud piles found in inventory");
                return false;
            }

            Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: processing {totalRolls} mud pile rolls from {slotsToProcess.Count} slot(s)");

            // Each drop chance is evaluated independently — a single soil pile can yield
            // multiple items (or nothing).  Nuggets are batched and stat-initialized after spawn.

            int greenstoneCount = 0;
            int flintCount = 0;
            int stoneCount = 0;
            int clayCount = 0;
            var nuggetQueue = new List<float>(); // NuggetType values for deferred stat init
            var toEject = new List<object>();

            foreach (var (slot, innerCards, sourceUid) in slotsToProcess)
            {
                if (innerCards == null || innerCards.Count == 0)
                {
                    // Direct card (EA 0.62b path)
                    foreach (var drop in RollSluiceDrops(sourceUid))
                    {
                        if (drop.NuggetType > 0f) nuggetQueue.Add(drop.NuggetType);
                        else { SpawnService.Spawn(drop.Guid); IncrResult(drop.Guid, ref greenstoneCount, ref flintCount, ref stoneCount, ref clayCount); }
                    }
                    toEject.Add(slot);
                    continue;
                }

                foreach (object inner in innerCards)
                {
                    if (inner == null) continue;
                    foreach (var drop in RollSluiceDrops(sourceUid))
                    {
                        if (drop.NuggetType > 0f) nuggetQueue.Add(drop.NuggetType);
                        else { SpawnService.Spawn(drop.Guid); IncrResult(drop.Guid, ref greenstoneCount, ref flintCount, ref stoneCount, ref clayCount); }
                    }
                    toEject.Add(inner);
                }
            }

            // Spawn nuggets last; capture pre-snapshot so deferred init can find the new cards
            int ironCount = 0, copperCount = 0, tinCount = 0;
            if (nuggetQueue.Count > 0)
            {
                var preIds = SnapshotCardIdsByUniqueId(CopperNuggetGUID);
                foreach (float t in nuggetQueue)
                {
                    SpawnService.Spawn(CopperNuggetGUID);
                    if (t == NuggetIronType)        ironCount++;
                    else if (t == NuggetCopperType) copperCount++;
                    else                            tinCount++;
                }
                StartDelayedNuggetInitialization(preIds, nuggetQueue);
            }

            EjectCardsFromStructure(sluice, toEject);

            Logger?.Log(LogLevel.Debug,
                $"[ActionIntercept] Sluice: spawned {greenstoneCount} GS, {flintCount} FL, {stoneCount} ST, {clayCount} clay" +
                (nuggetQueue.Count > 0 ? $", {ironCount} iron/{copperCount} copper/{tinCount} tin nugget(s)" : "") +
                $" on board; ejected {toEject.Count} source(s)");

            return false; // block JSON action, we handled everything
        }

        private struct SluiceRoll { public string Guid; public float NuggetType; }

        // Per-soil independent drop chances: [0]=Mud, [1]=Dirt, [2]=FineDirt
        private static readonly float[] _ironChance   = { 0.03f, 0.02f, 0.01f };
        private static readonly float[] _copperChance = { 0.08f, 0.04f, 0.01f };
        private static readonly float[] _tinChance    = { 0.03f, 0.02f, 0.01f };
        private static readonly float[] _gsChance     = { 0.10f, 0.05f, 0.03f };
        private static readonly float[] _flChance     = { 0.30f, 0.20f, 0.10f };
        private static readonly float[] _stChance     = { 0.40f, 0.20f, 0.10f };
        private static readonly float[] _clayChance   = { 0.10f, 0.20f, 0.55f };

        private static int SoilIndex(string uid)
        {
            if (uid == MudPileGUID)  return 0;
            if (uid == FineDirtGUID) return 2;
            return 1;
        }

        private static List<SluiceRoll> RollSluiceDrops(string sourceUid)
        {
            int i = SoilIndex(sourceUid);
            var drops = new List<SluiceRoll>();
            if (_sluiceRng.NextDouble() < _ironChance[i])   drops.Add(new SluiceRoll { Guid = CopperNuggetGUID, NuggetType = NuggetIronType });
            if (_sluiceRng.NextDouble() < _copperChance[i]) drops.Add(new SluiceRoll { Guid = CopperNuggetGUID, NuggetType = NuggetCopperType });
            if (_sluiceRng.NextDouble() < _tinChance[i])    drops.Add(new SluiceRoll { Guid = CopperNuggetGUID, NuggetType = NuggetTinType });
            if (_sluiceRng.NextDouble() < _gsChance[i])     drops.Add(new SluiceRoll { Guid = GreenstoneGUID });
            if (_sluiceRng.NextDouble() < _flChance[i])     drops.Add(new SluiceRoll { Guid = FlintGUID });
            if (_sluiceRng.NextDouble() < _stChance[i])     drops.Add(new SluiceRoll { Guid = StoneGUID });
            if (_sluiceRng.NextDouble() < _clayChance[i])   drops.Add(new SluiceRoll { Guid = ClayGUID });
            return drops;
        }

        private static void IncrResult(string guid, ref int g, ref int f, ref int s, ref int c)
        {
            if (guid == GreenstoneGUID) g++;
            else if (guid == FlintGUID) f++;
            else if (guid == ClayGUID)  c++;
            else s++;
        }

        // ——— Nugget stat initialization (type + quality applied after GiveCard) ———

        private static void StartDelayedNuggetInitialization(HashSet<int> preIds, List<float> nuggetTypes)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm is UnityEngine.MonoBehaviour mb)
                {
                    mb.StartCoroutine(ApplyNuggetStatsAfterSpawn(preIds, nuggetTypes));
                    return;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] StartDelayedNuggetInitialization: {ex.Message}"); }
            ApplyNuggetSpawnStats(preIds, nuggetTypes);
        }

        private static IEnumerator ApplyNuggetStatsAfterSpawn(HashSet<int> preIds, List<float> nuggetTypes)
        {
            for (int attempt = 0; attempt < NuggetInitRetryFrames; attempt++)
            {
                yield return null;
                if (ApplyNuggetSpawnStats(preIds, nuggetTypes) >= nuggetTypes.Count) yield break;
            }
            ApplyNuggetSpawnStats(preIds, nuggetTypes, allowExistingFallback: true);
        }

        private static int ApplyNuggetSpawnStats(HashSet<int> preIds, List<float> nuggetTypes, bool allowExistingFallback = false)
        {
            int updated = 0;
            try
            {
                var newCards = new List<object>();
                foreach (var card in EnumerateKnownCards())
                {
                    if (CardUtil.GetCardUniqueId(card) != CopperNuggetGUID) continue;
                    if (card is UnityEngine.Object uo && preIds.Contains(uo.GetInstanceID())) continue;
                    newCards.Add(card);
                }
                for (int i = 0; i < newCards.Count && i < nuggetTypes.Count; i++)
                    if (ApplyNuggetStatsToCard(newCards[i], nuggetTypes[i])) updated++;
                if (updated == 0 && allowExistingFallback && nuggetTypes.Count > 0)
                {
                    var latest = FindLatestKnownCardByUniqueId(CopperNuggetGUID);
                    if (latest != null && ApplyNuggetStatsToCard(latest, nuggetTypes[0])) updated++;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyNuggetSpawnStats: {ex.Message}"); }
            return updated;
        }

        private static bool ApplyNuggetStatsToCard(object card, float nuggetType)
        {
            try
            {
                bool changed = CardUtil.SetDurability(card, "SpecialDurability4", nuggetType);
                changed |= CardUtil.SetDurability(card, "SpecialDurability1", NuggetSluiceQuality);
                changed |= CardUtil.SetDurability(card, "SpecialDurability2", NuggetSluiceQuality);
                changed |= CardUtil.SetDurability(card, "SpecialDurability3", NuggetSluiceQuality);
                changed |= CardUtil.SetDurability(card, "FuelCapacity", 0f);
                if (changed) RefreshCardDurabilityVisuals(card);
                Logger?.LogDebug($"[ActionIntercept] NuggetInit: type={nuggetType} quality={NuggetSluiceQuality}");
                return changed;
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyNuggetStatsToCard: {ex.Message}"); return false; }
        }

        /// <summary>
        /// Replaces a card's CardData (CardModel) with the target in-place.
        /// Avoids destroying the card — which triggers OnDestroy relocation — by
        /// mutating the existing in-world card object instead. Returns true on
        /// success.
        /// </summary>
        private static bool TransformCardInPlace(object card, string targetGuid)
        {
            if (card == null || string.IsNullOrEmpty(targetGuid)) return false;
            try
            {
                var targetData = CardUtil.GetCardDataById(targetGuid);
                if (targetData == null)
                {
                    Logger?.LogError($"[ActionIntercept] Transform: CardData not found for {targetGuid}");
                    return false;
                }
                if (!CardUtil.TrySetCardModel(card, targetData))
                {
                    Logger?.LogError($"[ActionIntercept] Transform: CardModel not settable on {card.GetType().Name}");
                    return false;
                }
                CardUtil.ReinitCard(card, targetData);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] Transform failed: {ex.Message}");
                return false;
            }
        }

        // ============================================================
        //  DRAG STATE CLEANUP
        //  Vanilla EA 0.62b added a drag-stuck fix inside ActionRoutine.  Our prefix
        //  returning false skips that fix for WDI actions.  We proactively reset the
        //  GameManager drag state here before every early return so the dragged card
        //  is released even when we own the action.
        // ============================================================
        private static bool _dragReflected;
        private static Type _gmTypeForDrag;
        private static PropertyInfo _dragStateProp;
        private static FieldInfo _dragStateField;
        private static readonly string[] _dragCandidateProps = { "CurrentDraggedCard", "DraggedCard" };
        private static readonly string[] _dragCandidateFields = {
            "CurrentDraggedCard", "_currentDraggedCard", "DraggedCard", "_draggedCard",
            "m_DraggedCard", "currentDraggedCard"
        };

        // Only clears drag state when givenCard != null (i.e., this was a drag action).
        private static void ClearDragState(object givenCard)
        {
            if (givenCard == null) return;
            try
            {
                if (!_dragReflected)
                {
                    _dragReflected = true;
                    _gmTypeForDrag = CardUtil.FindGameType("GameManager");
                    if (_gmTypeForDrag != null)
                    {
                        foreach (var n in _dragCandidateProps)
                        {
                            var p = _gmTypeForDrag.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (p != null && p.CanWrite) { _dragStateProp = p; break; }
                        }
                        if (_dragStateProp == null)
                        {
                            foreach (var n in _dragCandidateFields)
                            {
                                var f = _gmTypeForDrag.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (f != null) { _dragStateField = f; break; }
                            }
                        }
                        Logger?.Log(LogLevel.Debug,
                            $"[ActionIntercept] DragState: prop={_dragStateProp?.Name ?? "none"}, field={_dragStateField?.Name ?? "none"}");
                    }
                }
                if (_dragStateProp == null && _dragStateField == null) return;
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null) return;
                _dragStateProp?.SetValue(gm, null);
                _dragStateField?.SetValue(gm, null);
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ClearDragState failed: {ex.Message}"); }
        }

        private static WorkshopCraftKind GetWorkshopCraftKind(object action)
        {
            if (action == null) return WorkshopCraftKind.None;

            string lk = CardUtil.GetActionLocalizationKey(action);
            if (string.Equals(lk, "Water_Sawmill_WorkshopPlaced_HammerCopperSheet_ActionName", StringComparison.Ordinal))
                return WorkshopCraftKind.CopperSheet;
            if (string.Equals(lk, "Water_Sawmill_WorkshopPlaced_ForgeCopperNails_ActionName", StringComparison.Ordinal))
                return WorkshopCraftKind.CopperNails;
            if (string.Equals(lk, "Water_Sawmill_WorkshopPlaced_CastMetalLump_ActionName", StringComparison.Ordinal))
                return WorkshopCraftKind.CopperLump;

            string dt = CardUtil.GetActionName(action);
            if (string.Equals(dt, "Hammer Copper Sheet", StringComparison.OrdinalIgnoreCase))
                return WorkshopCraftKind.CopperSheet;
            if (string.Equals(dt, "Forge Copper Nails", StringComparison.OrdinalIgnoreCase))
                return WorkshopCraftKind.CopperNails;
            if (string.Equals(dt, "Cast Metal Lump", StringComparison.OrdinalIgnoreCase))
                return WorkshopCraftKind.CopperLump;

            return WorkshopCraftKind.None;
        }

        private static int CountInventoryCards(object container, string uniqueId)
        {
            return FindInventoryCards(container, uniqueId, int.MaxValue).Count;
        }

        // A MetalNugget is "copper" if it has no WDI type tag (SD4==0, vanilla-smelted)
        // or is explicitly tagged as copper by the sluice (SD4==NuggetCopperType).
        // Iron (200) and Tin (120) nuggets are rejected.
        private static bool IsCopperNugget(object card)
        {
            float sd4 = CardUtil.GetDurability(card, "SpecialDurability4");
            return float.IsNaN(sd4) || sd4 == 0f || sd4 == NuggetCopperType;
        }

        private static int CountCopperNuggets(object container)
        {
            var all = FindInventoryCards(container, CopperNuggetGUID, int.MaxValue);
            int n = 0;
            foreach (var c in all) if (IsCopperNugget(c)) n++;
            return n;
        }

        private static bool TryConsumeCopperNuggets(object container, int count)
        {
            var all = FindInventoryCards(container, CopperNuggetGUID, int.MaxValue);
            var copper = new List<object>();
            foreach (var c in all) { if (IsCopperNugget(c)) { copper.Add(c); if (copper.Count >= count) break; } }
            if (copper.Count < count) return false;
            EjectCardsFromStructure(container, copper);
            return true;
        }

        private static bool TryConsumeInventoryCards(object container, string uniqueId, int count)
        {
            var cards = FindInventoryCards(container, uniqueId, count);
            if (cards.Count < count) return false;

            EjectCardsFromStructure(container, cards);
            return true;
        }

        private static List<object> FindInventoryCards(object container, string uniqueId, int maxCount)
        {
            var result = new List<object>();
            var slots = CardUtil.GetInventoryList(container);
            if (slots == null) return result;

            foreach (var slot in slots)
            {
                if (slot == null) continue;

                var innerCards = CardUtil.GetInventoryList(slot);
                if (innerCards != null)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner != null && string.Equals(CardUtil.GetCardUniqueId(inner), uniqueId, StringComparison.Ordinal))
                        {
                            result.Add(inner);
                            if (result.Count >= maxCount) return result;
                        }
                    }
                    continue;
                }

                if (string.Equals(CardUtil.GetCardUniqueId(slot), uniqueId, StringComparison.Ordinal))
                {
                    result.Add(slot);
                    if (result.Count >= maxCount) return result;
                }
            }

            return result;
        }

        private static void RefreshOpenInventoryPopup()
        {
            try
            {
                var graphicsManagerType = AccessTools.TypeByName("GraphicsManager");
                var instance = graphicsManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    ?? (graphicsManagerType != null ? UnityEngine.Object.FindObjectOfType(graphicsManagerType) : null);
                var popup = instance == null ? null : AccessTools.Field(graphicsManagerType, "CurrentInspectionPopup")?.GetValue(instance);
                var refresh = popup?.GetType().GetMethod("RefreshInventory", Flags);
                refresh?.Invoke(popup, null);
            }
            catch { }
        }

        private static int GetCardCharges(object card)
        {
            try
            {
                var cardType = card.GetType();
                var prop = cardType.GetProperty("CurrentCharges", Flags)
                        ?? cardType.GetProperty("Charges", Flags);
                if (prop != null && prop.CanRead)
                    return Convert.ToInt32(prop.GetValue(card));

                var field = cardType.GetField("CurrentCharges", Flags)
                         ?? cardType.GetField("currentCharges", Flags);
                if (field != null)
                    return Convert.ToInt32(field.GetValue(card));
            }
            catch { }
            return 1;
        }

    }
}
