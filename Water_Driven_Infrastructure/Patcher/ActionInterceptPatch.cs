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
    ///   Sawmill  "Saw All"       - saw every log in the inventory at once (HandleSawAllInner)
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
        private const string StampMillID        = "water_sawmill_stamp_mill_placed";
        private const string SluiceID           = "water_sawmill_ore_sluice_placed";
        private const string ForgeID            = "water_sawmill_forge_placed";
        private const string WorkshopID         = "water_sawmill_workshop_placed";
        private const string FullingMillID      = "water_sawmill_fulling_mill_placed";
        // Plan feature 18 (Expand): the in-place larger copies of the Sawmill and the Workshop. They are separate
        // cards, so EVERY station check below goes through IsSawmillStation / IsWorkshopStation, never a bare ==.
        private const string SawmillExpandedID  = "water_sawmill_placed_expanded";
        private const string WorkshopExpandedID = "water_sawmill_workshop_placed_expanded";
        private const string FishpondFilledID   = "water_sawmill_fishpond_filled";
        private const string FishpondStockedID  = "water_sawmill_fishpond_stocked";
        private const string FishpondWinterID   = "water_sawmill_fishpond_winter";
        private const float  FishCaughtQuality  = 72f;   // 75 % of SpecialDurability2 max (96)
        // Hammer All's flat quality boost per press, and the quality it stops at (balance pass 2026-10-14,
        // WDI-B7: was +5 per press with no ceiling, so a stack of sluice nuggets at 35 reached 100 in 13 presses
        // with no Smithing skill; vanilla smithing quality is skill-scaled per strike, so the press now only
        // brings poor metal up to fair and the rest is the player's own Smith strikes).
        private const float  WorkshopMetalQualityBoost = 2f;
        private const float  WorkshopQualityBoostCeiling = 60f;
        // Fulling Mill (niche content, MASTER_PLAN section 2; research niche_premises_2026-10-15.md section 3
        // GAP CONFIRMED: no vanilla station/CI/DA raises a finished cloth's Quality after craft). Full All beats
        // every loaded cloth up by FullingQualityBoost on each press and stops at FullingQualityCeiling, so the
        // mill brings poor/fair cloth to a fair finish but can never farm Quality to the stat's cap (the top of a
        // cloth bolt's 0-60 Quality still comes from better fibre at the loom). WDI-B7 is the precedent: a batch
        // station that writes a quality stat must carry a ceiling below the stat max. Tunable placeholders.
        private const float  FullingQualityBoost = 5f;
        private const float  FullingQualityCeiling = 50f;
        // Finished cloth bolts, the only finished cloth that carries an active "Quality" stat (SpecialDurability2,
        // max 60): ClothLarge, ClothMedium, ClothSmall. ClothVeryLarge/ClothSack carry no active Quality and are
        // excluded. The placed card's InventoryFilter allowlists exactly these three by UID (cloth carries no
        // gameplay CardTag), and the handler re-checks the UID so nothing else can be fulled for free.
        private static readonly HashSet<string> FullingClothIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "3e94716876eae304787efa2ae0b43711", // ClothLarge
            "35d6f2a46f2bbc84bbe33e917991c71d", // ClothMedium
            "aa73d89b9f0f04145adce258403d61d0", // ClothSmall
        };
        private const string FishpondKitID      = "water_sawmill_fishpond_kit";
        private const string CopperNuggetGUID = "4b0f4937a5ecb90499428c8c10288afc";
        private const string VanillaAshGUID   = "65f9b26c048cd474d9fb14d1c9e46754"; // vanilla Ash — Blast byproduct
        // The Workshop crafts always hand over the shared Metal Sheet and Metal Nail that CSFFModFramework 2.28.0
        // ships (WDI requires it). WDI 1.12.3 retired its own Cast Copper Sheet, Cast Iron Sheet and Cast Copper and
        // Iron Rivets (Chiwei's Nexus report, 2026-10-05: two near-identical sheets, one of which could not be melted);
        // those cards still load and reforge themselves into the shared ones, so nothing spawns them any more.
        private static string CopperSheetID => ActCompat.MetalSheetUid;
        private static string CopperNailsID => ActCompat.CopperNailsUid;
        // The Workshop's iron crafts (plan feature 9): the Metal Nail and Metal Sheet stamped Metal Type iron
        // (BuildActIronStats), which the game names Iron Nail and Iron Sheet.
        private static string IronRivetsID => ActCompat.CopperNailsUid;
        private static string IronSheetID => ActCompat.MetalSheetUid;
        private const string UnfinishedLumpID = "b92071e54dac7e54db99b48794e737ad";
        private const int   WorkshopNuggetsPerCraft = 6;
        // Cast Metal Lump costs what the base game's bar does (Bp_MetalBar: 6 nuggets) and the bar keeps their
        // average Metal Quality, as Bp_MetalBar's mask 448 does. It took 4 and floored the bar at 50 quality, and the
        // bar melts back into 6 nuggets, so every cast made 2 nuggets and raised poor metal to 50 (Nexus 2026-10-11).
        private const int   LumpNuggetsPerCraft     = 6;
        private const float LumpInitialStrikes      = 2f;
        private const int   LumpInitializationRetryFrames = 60;

        private static bool IsSawmillStation(string uid)
        {
            return string.Equals(uid, SawmillID, StringComparison.Ordinal)
                || string.Equals(uid, SawmillExpandedID, StringComparison.Ordinal);
        }

        private static bool IsWorkshopStation(string uid)
        {
            return string.Equals(uid, WorkshopID, StringComparison.Ordinal)
                || string.Equals(uid, WorkshopExpandedID, StringComparison.Ordinal);
        }

        private static int GetCraftCost(WorkshopCraftKind kind)
        {
            return kind == WorkshopCraftKind.CopperLump ? LumpNuggetsPerCraft : WorkshopNuggetsPerCraft;
        }

        private enum WorkshopCraftKind
        {
            None,
            CopperSheet,
            CopperNails,
            CopperLump,
            IronRivets,
            IronSheet
        }

        // The iron crafts take iron-grade nuggets (Metal Type 200); the copper crafts take copper ones.
        private static bool IsIronCraft(WorkshopCraftKind kind)
        {
            return kind == WorkshopCraftKind.IronRivets || kind == WorkshopCraftKind.IronSheet;
        }

        // Blast rows: UniqueID -> nugget count, for WDI's own parts. Every ACT item lives in the mirror
        // ActCompat.BlastRows instead (its UniqueIDs are kept in that one file; plan feature 15), looked
        // up through TryGetBlastRow below. Copper gear/blade items (copper_gear_small/large,
        // copper_saw_blade) smelt via their own passive-effect Progress system in the water-driven forge
        // and return copper nuggets, not IronBloom. Yields: 1 Heated Lump = 6 nuggets, 1 Small Crucible = 6,
        // 1 Large Crucible = 12. Every row here is copper (Metal Type 100): the kit has no Metal Type.
        private static readonly Dictionary<string, int> _smeltingRecipes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "water_sawmill_workshop_kit",               72 },  // forge_kit(24) + water_mill(24) + 2x small(12) + 1x large(12)
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
        // Metal Quality (SD2) and Quality (SD3) given to a sluice nugget. Never written to SD1, which on the
        // Metal Nugget is Strikes: every strike is one hand Smith hit that adds Smithing-scaled quality, so a nugget
        // spawned with 50 strikes could be hammered to full quality (Nexus report 2026-10-11). Vanilla gives every
        // smelted nugget 0 strikes. The sluice value is 35 as its help text says (it shipped as 5 from 2026-06-09).
        // A MELTED item's nuggets get no fixed quality: they take the item's own Metal Quality, the base game's melt
        // rule (TransferRules Special2 FromReceivingCard on MetalBarFinished and every blank), and an item with none
        // gives quality 0 nuggets as the furnace does. A 50 floor here let poor metal be cast into a WDI part and
        // melted back at 50 (Nexus report 2026-10-11).
        private const float  NuggetSluiceQuality =  35f;
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
        // Vanilla Fish_Eel (plan feature 6). Its quality stat is SpecialDurability2 like the others, but it has
        // no Weight stat (SpecialDurability1 is inactive on the card), so it has a PondFishGuids entry and no
        // PondFishWeightDefaults one: a caught eel gets FishCaughtQuality and nothing else.
        private const string EelGUID      = "bb8a42f395c31894188ff3038d76128f";

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
            EelGUID,
        };

        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                // Compile-time typeof avoids ModCore's shadowing GameManager (which lacks a usable
                // GiveCard) — WDI already references Assembly-CSharp. See root CLAUDE.md §Runtime Card Spawning.
                var gmType = typeof(GameManager);
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

            // Grinding Mill - "Grind All". AfterWrapped since plan feature 21 (prerequisite P0): a Cancel
            // handler replaced the whole game action with ActionRouter's stub, so the button charged no time
            // whatever its DaytimeCost said and an Overshot Wheel's DurationModifier had nothing to shorten.
            // Now the game runs the DA (its ReceivingCardChanges are ModType 0, so it changes nothing itself),
            // charges its 2 daytime points (1 with the wheel fitted) and then the batch runs, as Hammer All does.
            ActionRouter.Register(new ActionHandler
            {
                Name = "MillGrindAll",
                CardUid = MillID,
                ActionNamePrefix = "Grind All",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => { HandleGrindAllInner(ctx.Card); ClearDragState(ctx.GivenCard); }
            });

            // Fulling Mill - "Full All" (niche content, MASTER_PLAN section 2). AfterWrapped for the Grind All
            // reason: the DA (ReceivingCardChanges ModType 0, so it changes nothing itself) runs to completion
            // first and charges its 6 daytime points (wave0-action-time.md section 3: a Cancel handler would
            // charge none), then one press beats the whole loaded batch up by one Quality step. MillRaceGate,
            // registered first, water-gates it and applies the Winter Station Lock. No wheel: no DurationModifier.
            ActionRouter.Register(new ActionHandler
            {
                Name = "FullAll",
                CardUid = FullingMillID,
                ActionNamePrefix = "Full All",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => { HandleFullAllInner(ctx.Card); ClearDragState(ctx.GivenCard); }
            });

            // Water-Driven Stamp Mill - "Stamp All" (plan feature 25). AfterWrapped from the start, so the game
            // runs the DA and charges its 2 daytime points before the batch runs (a Cancel handler would charge
            // nothing: wave0-action-time.md section 3). MillRaceGate, registered first, water-gates it.
            ActionRouter.Register(new ActionHandler
            {
                Name = "StampAll",
                CardUid = StampMillID,
                ActionNamePrefix = "Stamp All",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => { HandleStampAllInner(ctx.Card); ClearDragState(ctx.GivenCard); }
            });

            // Ore Sluice - "Sluice All". AfterWrapped since plan feature 21 (P0), for the Grind All reason:
            // it now charges its 3 daytime points (1 with an Overshot Wheel fitted).
            ActionRouter.Register(new ActionHandler
            {
                Name = "SluiceAll",
                CardUid = SluiceID,
                ActionNamePrefix = "Sluice All",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => { HandleSluiceAllInner(ctx.Card); ClearDragState(ctx.GivenCard); }
            });

            // Fishpond — quality/weight fixup for "Catch X" / "Ice Fish X". The pond's
            // species counts live in its own stats (SpecialDurability1-4, Spoilage, Usage,
            // Fuel, Progress), so there is no C# per-pond tally: every catch is a JSON
            // action that lowers its species' stat and produces the fish card. The legacy
            // "Catch Other Fish" pick and its stock tracking were retired with the Eel
            // (plan feature 6): no card ever carried that action, and its tally wrote
            // SpecialDurability4, which is now the Eel count. ActionRouter's After
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
                    bool relevant = IsFishCatchAction(ctx.ActionName);
                    if (relevant) ctx.Tag = SnapshotFishCardIds();
                    if (relevant) Logger?.LogDebug($"[ActionIntercept] FishCatch: Before '{ctx.ActionName}' on {ctx.CardUid}: {((HashSet<int>)ctx.Tag).Count} pond fish already on the board");
                    return true;
                },
                After = ctx =>
                {
                    if (ctx.Tag is HashSet<int> preIds)
                    {
                        StartDelayedFishInitialization(preIds);
                    }
                    else if (IsFishCatchAction(ctx.ActionName) && !_fishCatchNoSnapshotWarned)
                    {
                        // The 1.12.2 failure signature (T2.636): another handler on the pond overwrote the shared ctx.Tag.
                        _fishCatchNoSnapshotWarned = true;
                        Logger?.LogWarning($"[ActionIntercept] FishCatch: '{ctx.ActionName}' reached After with no pre-catch snapshot in ctx.Tag (another handler on {ctx.CardUid} replaced it); the fish keeps its default Quality instead of {FishCaughtQuality}. Warned once.");
                    }
                }
            });

            // Fishpond - rod and spear fishing (plan feature 17). "Fish" (a tag_FishingLine card) and
            // "Spearfish" (a tag_Spear card) are CardInteractions on the Filled and Stocked ponds, so one JSON
            // action cannot lower the right species stat from a drop table: the species is picked here,
            // weighted by its count, and the fish spawned with the pond-catch quality and weight. The
            // FishCatchStats handler above keys on "Catch "/"Ice Fish " names and never sees these. With no
            // fish of any species at 1 or more the gate cancels the action (no time, no rod wear).
            ActionRouter.Register(new ActionHandler
            {
                Name = "PondRodSpearGate",
                CardPredicate = ctx => ctx.CardUid == FishpondFilledID || ctx.CardUid == FishpondStockedID,
                Timing = ActionTiming.Cancel,
                Before = ctx =>
                {
                    if (!IsPondRodSpearAction(ctx.ActionKey)) return false;
                    if (PickPondRodSpecies(ctx.Card) >= 0) return false;
                    Logger?.LogDebug($"[ActionIntercept] {ctx.ActionName} on {ctx.CardUid}: no species at 1 or more, canceled");
                    return true;
                }
            });
            ActionRouter.Register(new ActionHandler
            {
                Name = "PondRodSpearCatch",
                CardPredicate = ctx => ctx.CardUid == FishpondFilledID || ctx.CardUid == FishpondStockedID,
                Timing = ActionTiming.AfterWrapped,
                // No Before and no ctx.Tag: every handler matched in one dispatch shares ONE ActionContext, and
                // FishCatchStats (same pond cards, registered first) keeps its pre-catch snapshot in ctx.Tag. This
                // handler's old Before wrote Tag = null for every non-rod action, so no Catch button on the Filled
                // or Stocked pond stamped Quality 72 from feature 17 on (harness T2.636, 2026-10-10: a Partner's
                // Sturgeon read vanilla's 80 less decay; a temporary diagnostic showed After with no snapshot).
                After = ctx =>
                {
                    if (IsPondRodSpearAction(ctx.ActionKey))
                        CatchPondFishWithTool(ctx.Card);
                }
            });

            // Workshop, nugget-cost crafts: the copper crafts (Hammer Copper Sheet / Forge Copper
            // Nails / Cast Metal Lump) take copper nuggets and the iron crafts (Forge Iron Nails /
            // Hammer Iron Sheet) take iron-grade ones. The gate cancels outright when the workshop
            // doesn't hold enough nuggets of the craft's grade; otherwise Apply consumes them and
            // spawns the result once the original DaytimeCost timing completes.
            ActionRouter.Register(new ActionHandler
            {
                Name = "WorkshopCraftGate",
                CardPredicate = ctx => IsWorkshopStation(ctx.CardUid),
                Timing = ActionTiming.Cancel,
                Before = ctx =>
                {
                    var kind = GetWorkshopCraftKind(ctx.Action);
                    if (kind == WorkshopCraftKind.None) return false;
                    int cost = GetCraftCost(kind);
                    bool iron = IsIronCraft(kind);
                    if ((iron ? CountIronNuggets(ctx.Card) : CountCopperNuggets(ctx.Card)) >= cost) return false;
                    Logger?.LogDebug($"[ActionIntercept] {kind}: needs {cost} {(iron ? "iron-grade" : "copper")} nuggets in workshop inventory");
                    return true;
                }
            });
            ActionRouter.Register(new ActionHandler
            {
                Name = "WorkshopCraftApply",
                CardPredicate = ctx => IsWorkshopStation(ctx.CardUid),
                Timing = ActionTiming.AfterWrapped,
                Before = ctx => { ctx.Tag = GetWorkshopCraftKind(ctx.Action); return true; },
                After = ctx =>
                {
                    if (ctx.Tag is WorkshopCraftKind kind && kind != WorkshopCraftKind.None)
                        HandleWorkshopCraft(ctx.Card, kind);
                }
            });

            // Water-Driven Workshop — "Hammer All" (Workshop only; the Forge has no such action)
            ActionRouter.Register(new ActionHandler
            {
                Name = "HammerAll",
                CardPredicate = ctx => IsWorkshopStation(ctx.CardUid),
                ActionNamePrefix = "Hammer All",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => HandleHammerAllInner(ctx.Card, workshopQualityBoost: true)
            });

            // Water-Driven Sawmill - "Saw All" (plan feature 8). AfterWrapped, deliberately NOT Cancel (as Grind
            // All and Sluice All were until plan feature 21's P0): a Cancel handler replaces the whole game action with
            // ActionRouter's HandledActionStub, which never reaches GameManager.ActionRoutineSteps, the only
            // place a DismantleAction's DaytimeCost is spent (Documentation/Research/WaterDrivenInfrastructure/
            // wave0-action-time.md section 3), so a Cancel Saw All would saw every log for 0 DTP. AfterWrapped
            // lets the game run the action to completion first (its 4 DTP charged, ActionRouter.cs
            // RunWrapped), then HandleSawAllInner does the work, the shape Hammer All and Blast use.
            ActionRouter.Register(new ActionHandler
            {
                Name = "SawAll",
                CardPredicate = ctx => IsSawmillStation(ctx.CardUid),
                ActionNamePrefix = "Saw All",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => HandleSawAllInner(ctx.Card)
            });

            // Sawmill and Workshop - "Expand" (plan feature 18). The base card's Expand button is JSON-only and
            // does nothing itself (ModType 0, 2 DTP); these two handlers are the whole feature. The gate cancels
            // the action (and its DTP) while the cost is not in reach; the apply swaps the card in place to its
            // larger copy (CardUtil.TransformCardInPlace keeps contents, durabilities and the Overshot flag) and
            // then consumes the cost. Registered once per process with the rest (RegisterActionHandlers).
            ActionRouter.Register(new ActionHandler
            {
                Name = "ExpandGate",
                CardPredicate = ctx => ExpandedUidFor(ctx.CardUid) != null,
                ActionNamePrefix = "Expand",
                Timing = ActionTiming.Cancel,
                Before = ctx =>
                {
                    if (TryFindExpandCost(ctx.Card, out _, out _)) return false;
                    Logger?.LogDebug($"[ActionIntercept] Expand on {ctx.CardUid}: needs {ExpandPlankCount} Planks and {ExpandNailCount} Iron Nails (Metal Nails at Metal Type 200) in reach");
                    return true;
                }
            });
            ActionRouter.Register(new ActionHandler
            {
                Name = "ExpandApply",
                CardPredicate = ctx => ExpandedUidFor(ctx.CardUid) != null,
                ActionNamePrefix = "Expand",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => HandleExpand(ctx.Card)
            });

            // Workshop / Forge — "Blast"
            ActionRouter.Register(new ActionHandler
            {
                Name = "BlastAll",
                CardPredicate = ctx => IsWorkshopStation(ctx.CardUid) || ctx.CardUid == ForgeID,
                ActionNamePrefix = "Blast",
                Timing = ActionTiming.AfterWrapped,
                After = ctx => HandleBlastAllInner(ctx.Card)
            });

            // Iron item OnFull smelting (Parts/Bearing/Axle/Wrench) — snapshot existing
            // nugget IDs AND the source item's own quality (SpecialDurability2, read before
            // the action consumes it) so newly spawned iron nuggets can be typed/quality-fixed
            // afterward. Fires on every action on these 4 cards (matches the original
            // unconditional-per-action check). Each item carries the Metal Quality of the bar it
            // was made from (its blueprint's AffectResultDurabilities), and its nuggets take
            // exactly that, never a floor (see NuggetSluiceQuality).
            ActionRouter.Register(new ActionHandler
            {
                Name = "IronSmeltType",
                CardPredicate = ctx => IronSmeltItemIds.Contains(ctx.CardUid),
                Timing = ActionTiming.AfterWrapped,
                Before = ctx => { ctx.Tag = (SnapshotCardIdsByUniqueId(CopperNuggetGUID), GetSourceQualityPercent(ctx.Card)); return true; },
                After = ctx =>
                {
                    if (ctx.Tag is ValueTuple<HashSet<int>, float> tagged && ApplyIronBarType(tagged.Item1, tagged.Item2) == 0)
                        StartDelayedIronBarTypeRetry(tagged.Item1, tagged.Item2);
                }
            });

            // Copper self-smelt items (gears/blades/etc.) — any item whose Progress OnFull
            // ProducedCards a copper nugget, discovered dynamically (see GetSelfSmeltCopperItemIds)
            // instead of a hardcoded UID list, so a new self-smelting item is covered without a
            // code change. Nuggets inherit the melted item's own quality percentage (the
            // crucible's, carried by the casting blueprint), never a floor (see ApplyBlastNuggetQuality).
            // SD4 type is left untouched (stays copper), unlike IronSmeltType above which also
            // stamps SD4=Iron.
            ActionRouter.Register(new ActionHandler
            {
                Name = "CopperSelfSmeltQuality",
                CardPredicate = ctx => GetSelfSmeltCopperItemIds().Contains(ctx.CardUid),
                Timing = ActionTiming.AfterWrapped,
                Before = ctx => { ctx.Tag = (SnapshotCardIdsByUniqueId(CopperNuggetGUID), GetSourceQualityPercent(ctx.Card)); return true; },
                After = ctx =>
                {
                    if (ctx.Tag is ValueTuple<HashSet<int>, float> tagged && ApplyCopperSelfSmeltQuality(tagged.Item1, tagged.Item2) == 0)
                        StartDelayedCopperSelfSmeltQualityRetry(tagged.Item1, tagged.Item2);
                }
            });

            // Fishpond - "Scatter Fish Food" (balance pass 2026-10-14, WDI-B2). The JSON action no longer raises
            // the three breeding species itself (it raised Pike, Perch AND Minnow by 0.25 each, so every scrap was
            // three quarters of a fish); it takes the daily latch stat and this handler adds the quarter fish to
            // ONE species, the smallest that is breeding (2 or more, below the cap of 10). No Before and no
            // ctx.Tag: FishCatchStats keeps its snapshot there on the same pond cards.
            ActionRouter.Register(new ActionHandler
            {
                Name = "FishpondFeedOneSpecies",
                CardPredicate = ctx => ctx.CardUid == FishpondFilledID || ctx.CardUid == FishpondStockedID,
                Timing = ActionTiming.AfterWrapped,
                After = ctx =>
                {
                    if (IsPondFeedAction(ctx.ActionKey)) FeedSmallestBreedingSpecies(ctx.Card);
                }
            });

            // Fishpond kit (balance pass 2026-10-14, WDI-B15). A kit whose hidden "Unstocked" flag
            // (SpecialDurability1) is set places an EMPTY pond instead of the fresh pond's two of each breeding
            // species. The Angler's Start perk hands its kit over flagged (AddedCardsStats) and Drain hands the
            // kit back flagged, so a drained pond is restocked by the player, never by packing and placing. A kit
            // crafted from the blueprint, or saved before 1.12.10, reads 0 and places stocked as before.
            ActionRouter.Register(new ActionHandler
            {
                Name = "FishpondKitUnstocked",
                CardPredicate = ctx => ctx.CardUid == FishpondKitID,
                ActionKeyPrefix = "Water_Sawmill_FishpondKit_Place_",
                Timing = ActionTiming.AfterWrapped,
                Before = ctx => { ctx.Tag = IsUnstockedFishpondKit(ctx.Card) ? (object)true : null; return true; },
                After = ctx =>
                {
                    if (ctx.Tag is bool unstocked && unstocked) EmptyPlacedFishpond(ctx);
                }
            });
            ActionRouter.Register(new ActionHandler
            {
                Name = "FishpondDrainFlagsKit",
                CardPredicate = ctx => ctx.CardUid == FishpondFilledID || ctx.CardUid == FishpondStockedID,
                Timing = ActionTiming.AfterWrapped,
                After = ctx =>
                {
                    if (IsPondDrainAction(ctx.ActionKey)) FlagDrainedFishpondKit(ctx);
                }
            });
        }

        // ============================================================
        //  FISHPOND - feeding and the unstocked kit (balance pass 2026-10-14, WDI-B2 / WDI-B15)
        // ============================================================
        private const float PondFeedPerScatter = 0.25f;
        private const float PondBreedingFloor = 2f;    // FishpondPopulationPatch: a species breeds at 2 or more
        private const float PondSpeciesCap = 10f;      // Fishpond_*.json SD1-3 MaxValue
        private const string FishpondKitUnstockedStat = "SpecialDurability1";
        private static readonly string[] PondBreedingStats = { "SpecialDurability1", "SpecialDurability2", "SpecialDurability3" };

        private static bool IsPondFeedAction(string actionKey)
        {
            return !string.IsNullOrEmpty(actionKey) && actionKey.EndsWith("_Feed_ActionName", StringComparison.Ordinal);
        }

        private static bool IsPondDrainAction(string actionKey)
        {
            return !string.IsNullOrEmpty(actionKey) && actionKey.EndsWith("_Drain_ActionName", StringComparison.Ordinal);
        }

        // The smallest breeding species (Pike, Perch or Minnow at 2 or more and below 10) gains a quarter fish.
        // With none breeding the food is wasted, as the action's text says.
        private static void FeedSmallestBreedingSpecies(object pond)
        {
            int pick = -1;
            float lowest = float.MaxValue;
            for (int i = 0; i < PondBreedingStats.Length; i++)
            {
                float count = CardUtil.GetDurability(pond, PondBreedingStats[i]);
                if (float.IsNaN(count))
                {
                    WarnOnce("PondFeed-read-" + PondBreedingStats[i],
                        $"CardUtil.GetDurability could not read the pond's {PondBreedingStats[i]} (reflection miss): that species is left out of Scatter Fish Food");
                    continue;
                }
                if (count < PondBreedingFloor || count >= PondSpeciesCap) continue;
                if (count < lowest) { lowest = count; pick = i; }
            }
            if (pick < 0)
            {
                Logger?.LogDebug("[ActionIntercept] PondFeed: no species is breeding below the cap; the food is wasted");
                return;
            }
            float fed = Math.Min(PondSpeciesCap, lowest + PondFeedPerScatter);
            if (!CardUtil.SetDurability(pond, PondBreedingStats[pick], fed))
                WarnOnce("PondFeed-write-" + PondBreedingStats[pick], $"could not raise the pond's {PondBreedingStats[pick]} after Scatter Fish Food (reflection miss)");
            else
                Logger?.LogDebug($"[ActionIntercept] PondFeed: {PondBreedingStats[pick]} {lowest:F2} -> {fed:F2}");
        }

        private static bool IsUnstockedFishpondKit(object kit)
        {
            float flag = CardUtil.GetDurability(kit, FishpondKitUnstockedStat);
            if (float.IsNaN(flag))
            {
                WarnOnce("PondKit-read", $"CardUtil.GetDurability could not read the fishpond kit's {FishpondKitUnstockedStat} (reflection miss): the kit places a stocked pond");
                return false;
            }
            return flag >= 0.5f;
        }

        private static void EmptyPlacedFishpond(ActionContext ctx)
        {
            int emptied = 0;
            for (int i = 0; i < ctx.ProducedUids.Count; i++)
            {
                if (ctx.ProducedUids[i] != FishpondFilledID) continue;
                foreach (var stat in PondBreedingStats)
                {
                    if (CardUtil.SetDurability(ctx.Produced[i], stat, 0f)) continue;
                    WarnOnce("PondKit-empty-" + stat, $"could not empty the placed pond's {stat} (reflection miss): an unstocked kit placed a stocked pond");
                }
                emptied++;
            }
            if (emptied == 0)
                WarnOnce("PondKit-no-pond", "an unstocked fishpond kit was placed but no Fishpond appeared among the action's produced cards; the pond keeps its starting fish");
        }

        private static void FlagDrainedFishpondKit(ActionContext ctx)
        {
            int flagged = 0;
            for (int i = 0; i < ctx.ProducedUids.Count; i++)
            {
                if (ctx.ProducedUids[i] != FishpondKitID) continue;
                if (CardUtil.SetDurability(ctx.Produced[i], FishpondKitUnstockedStat, 1f)) flagged++;
                else WarnOnce("PondKit-flag", $"could not set the drained kit's {FishpondKitUnstockedStat} (reflection miss): it will place a stocked pond");
            }
            if (flagged == 0)
                WarnOnce("PondKit-no-kit", "a fishpond was drained but no Fishpond Kit appeared among the action's produced cards; the kit will place a stocked pond");
        }

        static void GetSelectedContainedBlueprint_Postfix(object __instance, ref object __result)
        {
            if (__result == null) return;
            if (!IsWorkshopStation(CardUtil.GetCardUniqueId(__instance))) return;
            if (!IsWorkshopPrimaryStorageContext()) return;

            __result = null;
        }

        // Tracks whether the 7-method stack-trace match below has EVER succeeded, so a future
        // game update silently renaming/restructuring all 7 targets surfaces as a log line
        // instead of a permanently-silent Workshop-storage-routing regression (no exception is
        // thrown when the match simply fails, so the catch breadcrumb never fires on its own).
        private static bool _everMatchedWorkshopStorageContext;
        private static int _workshopStorageContextCallCount;
        private static bool _workshopStorageContextWarningLogged;
        private const int WorkshopStorageContextWarningThreshold = 50;

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
                    {
                        _everMatchedWorkshopStorageContext = true;
                        return true;
                    }

                    if (typeName == "InGameCardBase" &&
                        (methodName == "OnDrop" || methodName == "GetPossibleActionFromPlayerDrag" ||
                         methodName == "DropInInventory" || methodName == "CanReceiveInInventoryInstance" || methodName == "GetIndexForInventory"))
                    {
                        _everMatchedWorkshopStorageContext = true;
                        return true;
                    }
                }
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] IsWorkshopPrimaryStorageContext: {ex.Message}"); }

            if (!_everMatchedWorkshopStorageContext && !_workshopStorageContextWarningLogged &&
                ++_workshopStorageContextCallCount >= WorkshopStorageContextWarningThreshold)
            {
                _workshopStorageContextWarningLogged = true;
                Logger?.LogWarning("[ActionIntercept] IsWorkshopPrimaryStorageContext: never matched any of the 7 target frames " +
                    $"across {_workshopStorageContextCallCount} calls to the Workshop's GetSelectedContainedBlueprint hook — " +
                    "the game may have renamed InspectionPopup/InGameCardBase's storage methods; the Workshop may be routing " +
                    "through blueprint-slot display instead of normal storage. Check CLAUDE.md §Storage-first stations.");
            }

            return false;
        }

        // Reads a melted item's own quality (SpecialDurability2) as a 0-1 percentage, for
        // carrying into the nuggets it smelts into. Returns 0 when the item has no quality
        // stat of its own: its nuggets then keep quality 0, as the base game's furnace gives.
        private static float GetSourceQualityPercent(object item)
        {
            if (item == null) return 0f;
            float v = CardUtil.GetDurability(item, "SpecialDurability2");
            if (float.IsNaN(v) || v <= 0f) return 0f;
            float m = CardUtil.GetDurabilityMax(item, "SpecialDurability2");
            return (!float.IsNaN(m) && m > 0f) ? v / m : v / 100f;
        }

        private static void HandleBlastAllInner(object workshop)
        {
            // Every Blast burns a full fuel charge — leave a pile of vanilla Ash behind
            // (lye/soil uses), whether or not anything was loaded to smelt.
            SpawnService.Spawn(VanillaAshGUID);

            var inventory = CardUtil.GetInventoryList(workshop);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.LogDebug("[ActionIntercept] BlastAll: empty workshop inventory");
                return;
            }

            var toSmelt = new List<(object item, int nuggets, float metalType)>();

            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;
                var innerCards = CardUtil.GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner == null) continue;
                        TryAddBlastItem(inner, toSmelt);
                    }
                }
                else
                {
                    TryAddBlastItem(slotItem, toSmelt);
                }
            }

            if (toSmelt.Count == 0)
            {
                Logger?.LogDebug("[ActionIntercept] BlastAll: no smeltable items found");
                return;
            }

            int totalNuggets = 0;
            foreach (var (_, n, _) in toSmelt) totalNuggets += n;

            // Each item's nuggets take THAT item's own quality percentage, the base game's melt rule. They
            // used to take the best item's quality in the whole Blast, floored at 50, so one good item
            // blasted with poor ones made every nugget good (Nexus report 2026-10-11).
            float lowestQualityPct = float.MaxValue;
            foreach (var (item, _, _) in toSmelt)
                lowestQualityPct = Math.Min(lowestQualityPct, GetSourceQualityPercent(item));

            Logger?.LogDebug($"[ActionIntercept] BlastAll: smelting {toSmelt.Count} item(s) → {totalNuggets} nugget(s) by Metal Type ({DescribeBlastTypes(toSmelt)}), each at its own item's quality (lowest {lowestQualityPct:P0})");

            // Every nugget spawns typed (plan feature 15): SpawnService.Spawn(uid, stats) applies its
            // stats through the vanilla drop path (framework 2.28.0), so each nugget carries its OWN
            // item's Metal Type and quality from the first frame. The post-spawn ID-diff pass only ever
            // raises a quality stat, and only to the LOWEST item's quality, so it is a no-op on a nugget
            // that spawned right and can never lift one above what its own item gives; it still sets
            // quality should the spawn fall back to the plain GiveCard path (SpawnService warns then).
            var blastPreIds = SnapshotCardIdsByUniqueId(CopperNuggetGUID);
            foreach (var (item, nuggets, metalType) in toSmelt)
            {
                var stats = BuildBlastNuggetStats(metalType, GetSourceQualityPercent(item));
                for (int i = 0; i < nuggets; i++)
                    SpawnService.Spawn(CopperNuggetGUID, stats);
            }
            StartDelayedBlastNuggetInitialization(blastPreIds, totalNuggets, lowestQualityPct);

            EjectCardsFromStructure(workshop, toSmelt.Select(t => t.item));
        }

        private const string MetalTypeStat = "SpecialDurability4";
        // MaxValue of the Metal Nugget's two quality stats (Metal Quality, Quality): both 100
        // in the vanilla export. ApplyBlastNuggetQuality reads each stat's real max (100 when unreadable);
        // the spawn-time stats below are built before any card exists, so they use the same 100.
        private const float NuggetQualityStatMax = 100f;
        // The nugget stats a machine sets: Metal Quality and Quality. SD1 (Strikes) is left at the spawn's 0 (see NuggetSluiceQuality).
        private static readonly string[] NuggetQualityStats = { "SpecialDurability2", "SpecialDurability3" };

        // Adds one inventory card to the Blast list when it has a row: WDI's own rows first (copper), then
        // the ACT mirror. Its nuggets take the item's Metal Type, resolved below.
        private static void TryAddBlastItem(object card, List<(object item, int nuggets, float metalType)> toSmelt)
        {
            string uid = CardUtil.GetCardUniqueId(card);
            if (uid == null) return;
            int nuggets;
            float rowMetalType = NuggetCopperType;
            if (!_smeltingRecipes.TryGetValue(uid, out nuggets) && !ActCompat.TryGetBlastRow(uid, out nuggets, out rowMetalType))
                return;
            toSmelt.Add((card, nuggets, ResolveBlastMetalType(card, rowMetalType)));
        }

        // The Metal Type one blasted item's nuggets take (plan feature 15): the item's OWN
        // SpecialDurability4 when it reads above 0 (a stat the card does not have active reads 0, and ACT's
        // armour, shields and Iron Large Saw have none), else the Metal Type of its row, which is copper
        // 100 for a row with none. An unreadable stat (NaN, a reflection miss) falls back the same way and
        // logs once, so a renamed member cannot silently turn every alloy nugget back into copper.
        private static float ResolveBlastMetalType(object item, float rowMetalType)
        {
            float own = CardUtil.GetDurability(item, MetalTypeStat);
            if (float.IsNaN(own))
            {
                WarnOnce("BlastAll-metal-type-unreadable",
                    $"Blast could not read {MetalTypeStat} (Metal Type) from a blasted item, so its nuggets take the Metal Type of the item's Blast row or copper; CardUtil.GetDurability returned NaN or the game renamed the stat");
                return rowMetalType;
            }
            return own > 0f ? own : rowMetalType;
        }

        private static Dictionary<string, float> BuildBlastNuggetStats(float metalType, float itemQualityPct)
        {
            float quality = itemQualityPct * NuggetQualityStatMax;
            return new Dictionary<string, float>(StringComparer.Ordinal)
            {
                { MetalTypeStat, metalType },
                { "SpecialDurability2", quality },
                { "SpecialDurability3", quality },
            };
        }

        // "130 x6, 200 x20": how many nuggets this Blast makes of each Metal Type, for the log line.
        private static string DescribeBlastTypes(List<(object item, int nuggets, float metalType)> toSmelt)
        {
            var counts = new SortedDictionary<float, int>();
            foreach (var (_, nuggets, metalType) in toSmelt)
            {
                counts.TryGetValue(metalType, out int have);
                counts[metalType] = have + nuggets;
            }
            return string.Join(", ", counts.Select(kv => $"{kv.Key:0.#} x{kv.Value}"));
        }

        // ============================================================
        //  GRINDING MILL - C# Grind All handler
        // ============================================================
        private const string GrindingToolTag = "tag_GrindingTool";
        private const string HammerToolTag   = "tag_HammeringToolGeneral";

        // The actions that MEAN milling. Both the mill's inventory filter (GameLoadPatch.
        // HasGrindingToolInteraction) and Grind All's result lookup (ResolveGrindResult) select a card by the
        // trigger TAG of one of its CardInteractions, and the hammer tag is shared by every hammer job in the
        // game: of the 77 cards the filter injection collected on EA 0.68b (66 vanilla, 10 Herbs and Fungi
        // "Grind", 1 Advanced Copper Tools "Break Mold"), 28 are not milling (Break on casts, bones and mud
        // bricks, Mash on berries, Crack on a Geode, Crush dirt, Make Clay from Mud, Break Mold), and Grind All
        // would have turned each into its hammer result with no tool and no time. A name is accepted only when
        // it is exactly one of these (so "Crush dirt" is not "Crush"). Plan defect B.
        private static readonly string[] MillActionNames = { "Grind", "Crush", "Pound", "Ground" };

        internal static bool IsMillActionName(string actionName)
        {
            if (string.IsNullOrEmpty(actionName)) return false;
            foreach (var name in MillActionNames)
                if (string.Equals(actionName, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // A CardInteraction's player-facing name (its ActionName.DefaultText); null when unreadable, which
        // ReadField reports once per type.member when the MEMBER is missing.
        internal static string ReadInteractionActionName(object cardInteraction)
        {
            return ReadField(ReadField(cardInteraction, "ActionName"), "DefaultText") as string;
        }

        // ============================================================
        //  WORK PREDICATES - does a station hold anything its batch button would process? (plan Prompt 22)
        //  MillDutyPatch gates the Grinding Mill, Stamp Mill and Ore Sluice duties on these, so a Partner
        //  stops pressing an empty (or unprocessable) hopper in a loop. Each one asks the SAME resolver its
        //  handler uses to pick inputs, so the gate and the press cannot drift apart. The player's own
        //  button never reads them: a hand press on an empty station behaves exactly as before.
        // ============================================================
        internal static bool HasGrindWork(object mill)
        {
            foreach (var card in Inventory.Cards(mill))
                if (card != null && ResolveGrindResult(card) != null) return true;
            return false;
        }

        internal static bool HasStampWork(object mill)
        {
            foreach (var ore in Inventory.Cards(mill))
                if (ore != null && TryResolveStampResult(ore, out _, out _, out _)) return true;
            return false;
        }

        internal static bool HasSluiceWork(object sluice)
        {
            foreach (var card in Inventory.Cards(sluice))
                if (card != null && IsSluiceInputUid(CardUtil.GetCardUniqueId(card))) return true;
            return false;
        }

        // The soils Sluice All washes; shared by HandleSluiceAllInner and HasSluiceWork.
        private static bool IsSluiceInputUid(string uid)
        {
            return uid == MudPileGUID || uid == DirtPileGUID || uid == FineDirtGUID;
        }

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

            // GiveCard returns void in EA 0.65 — SpawnService.Spawn always returns null, so the
            // only way to find each freshly-spawned result is to diff known-card IDs before vs.
            // after. Previously this diff called EnumerateKnownCards() (which falls back to a
            // full-scene FindObjectsOfType(InGameCardBase) scan) TWICE per grind item — once to
            // snapshot pre-IDs, once to find the new card — so an 18-slot batch could trigger up
            // to 36 scene scans for one button press. AllCards.Add(spawned) happens synchronously
            // inside GiveCard (.decomp/GameManager.cs), so the cheap EnumerateGameManagerAllCards()
            // path (no scene scan) reliably sees each new card immediately; the expensive scene
            // scan is now taken at most ONCE for the whole batch, only if that cheap path ever
            // misses a spawn.
            var preIdsByResult = new Dictionary<string, HashSet<int>>();
            foreach (var resultId in toGrind.Select(t => t.resultId).Distinct())
                preIdsByResult[resultId] = SnapshotCardIdsByUniqueId(resultId, EnumerateGameManagerAllCards());

            bool sceneFallbackUsed = false;

            foreach (var (sourceCard, resultId) in toGrind)
            {
                string srcUid  = CardUtil.GetCardUniqueId(sourceCard);
                float srcQ2    = CardUtil.GetDurability(sourceCard, "SpecialDurability2");
                float srcQ2Max = CardUtil.GetDurabilityMax(sourceCard, "SpecialDurability2");
                float srcQ3    = CardUtil.GetDurability(sourceCard, "SpecialDurability3");
                float srcQ3Max = CardUtil.GetDurabilityMax(sourceCard, "SpecialDurability3");
                Logger?.LogDebug($"[GrindAll] grinding '{srcUid}' → '{resultId}': SD2={srcQ2:F1}/{srcQ2Max:F1}");

                var preIds = preIdsByResult[resultId];
                SpawnService.Spawn(resultId);

                object newCard = FindNewCard(resultId, preIds, EnumerateGameManagerAllCards());
                if (newCard == null && !sceneFallbackUsed)
                {
                    // Cheap path missed it — fall back to one full scene scan for the rest of
                    // this batch (not per-item) and retry from that single snapshot.
                    sceneFallbackUsed = true;
                    newCard = FindNewCard(resultId, preIds, EnumerateKnownCards());
                }

                if (newCard == null) continue;
                if (newCard is UnityEngine.Object uo) preIds.Add(uo.GetInstanceID());

                ApplyMinimumQualityPercent(newCard, "SpecialDurability2", srcQ2, srcQ2Max);
                ApplyMinimumQualityPercent(newCard, "SpecialDurability3", srcQ3, srcQ3Max);
                RefreshCardDurabilityVisuals(newCard);
            }

            // Every ground card leaves the game the way every other station's input does (plan Defect C).
            EjectCardsFromStructure(mill, toGrind.Select(t => t.card));
            return false;
        }

        // ============================================================
        //  FULLING MILL - C# Full All handler (niche content, MASTER_PLAN section 2)
        //  Raises the Quality (SpecialDurability2) of every loaded finished cloth by one step per press,
        //  capped at FullingQualityCeiling. Cloth is improved IN PLACE and left in the hopper (no eject,
        //  no produced card): one press = one batch step, so a save reload cannot double a press.
        // ============================================================
        private static void HandleFullAllInner(object mill)
        {
            var inventory = CardUtil.GetInventoryList(mill);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.LogDebug("[ActionIntercept] FullAll: empty fulling mill inventory");
                return;
            }

            int fulled = 0;
            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;
                var innerCards = CardUtil.GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                        if (inner != null && TryFullCloth(inner)) fulled++;
                }
                else if (TryFullCloth(slotItem))
                {
                    fulled++;
                }
            }

            if (fulled == 0)
            {
                Logger?.LogDebug("[ActionIntercept] FullAll: no finished cloth below the quality ceiling found");
                return;
            }
            Logger?.LogDebug($"[ActionIntercept] FullAll: fulled {fulled} cloth piece(s) by +{FullingQualityBoost} (ceiling {FullingQualityCeiling})");
            RefreshOpenInventoryPopup();
        }

        // Raises one finished-cloth card's active "Quality"-named slots (SpecialDurability2, and SpecialDurability3
        // too should a future cloth carry a second active Quality stat - the "Hammer All quality" lesson in WDI
        // CLAUDE.md: boost every slot that represents quality, never just one) by FullingQualityBoost, clamped to
        // FullingQualityCeiling. Returns true when it actually raised a slot. Only the three finished cloth bolts
        // are eligible; anything else is left untouched so the mill cannot farm a quality stat it shouldn't.
        private static bool TryFullCloth(object card)
        {
            string uid = CardUtil.GetCardUniqueId(card);
            if (uid == null || !FullingClothIds.Contains(uid)) return false;

            object cardData = GetCardData(card);
            if (cardData == null)
            {
                Logger?.LogDebug($"[ActionIntercept] FullAll skip '{uid}': CardModel null (reflection miss)");
                return false;
            }

            bool anyBoosted = false;
            foreach (var stat in QualityDurabilitySlots)
            {
                if (!IsDurabilityDefinitionActive(cardData, stat) || !DurabilityStatNameContains(cardData, stat, "Quality"))
                    continue;

                float current = CardUtil.GetDurability(card, stat);
                if (float.IsNaN(current))
                {
                    Logger?.LogDebug($"[ActionIntercept] FullAll skip '{uid}' {stat}: could not read current Quality (reflection miss)");
                    continue;
                }

                float max = CardUtil.GetDurabilityMax(card, stat);
                if (float.IsNaN(max) || max <= 0f) max = 60f;

                // The press stops at FullingQualityCeiling; a piece at or above it gains nothing (WDI-B7 shape).
                float ceiling = Math.Min(max, FullingQualityCeiling);
                if (current >= ceiling) continue;
                float next = Math.Min(ceiling, current + FullingQualityBoost);
                if (next <= current) continue;

                bool ok = CardUtil.SetDurability(card, stat, next);
                if (!ok)
                {
                    Logger?.LogDebug($"[ActionIntercept] FullAll '{uid}' {stat}: SetDurability failed (reflection miss)");
                    continue;
                }
                Logger?.LogDebug($"[ActionIntercept] FullAll '{uid}' {stat}: {current:F1} -> {next:F1} (ceiling {ceiling:F1})");
                anyBoosted = true;
            }
            if (anyBoosted) RefreshCardDurabilityVisuals(card);
            return anyBoosted;
        }

        // ============================================================
        //  STAMP MILL - C# Stamp All handler (plan feature 25)
        // ============================================================
        // Each ore's hand-hammer crush is the CardInteraction that triggers on a TAG and no card (vanilla gives
        // every ore two: the mortar's triggers on the StoneMortarAndPestle card, the hammer's on a hammering tag),
        // found by that shape so no runtime tag name is needed. Its TransformInto is the crushed card and its
        // Special2Change the hammer's Quality bonus (Greenstone +7, Bog Iron +3, slag +0), so Stamp All gives the
        // hand hammer's result to the whole hopper. An ore with no such CI stays in the hopper (warned once per UID).
        private static void HandleStampAllInner(object mill)
        {
            var consumed = new List<object>();
            foreach (var ore in Inventory.Cards(mill))
            {
                if (ore == null) continue;
                string uid = CardUtil.GetCardUniqueId(ore);
                if (!TryResolveStampResult(ore, out string resultId, out float bonus, out float resultMax))
                {
                    WarnOnce("StampAll-no-crush-" + uid, $"Stamp All found no tag-triggered crush interaction on '{uid}'; it stays in the hopper");
                    continue;
                }
                float quality = CardUtil.GetDurability(ore, "SpecialDurability2");
                if (float.IsNaN(quality))
                {
                    WarnOnce("StampAll-quality-" + uid, $"Stamp All could not read '{uid}' SpecialDurability2 (reflection miss); its result spawns at the bonus alone");
                    quality = 0f;
                }
                float result = quality + bonus;
                if (resultMax > 0f && result > resultMax) result = resultMax;
                if (result < 0f) result = 0f;
                SpawnService.Spawn(resultId, new Dictionary<string, float>(StringComparer.Ordinal) { { "SpecialDurability2", result } });
                consumed.Add(ore);
                Logger?.LogDebug($"[StampAll] '{uid}' Q{quality:F1} -> '{resultId}' Q{result:F1}");
            }
            if (consumed.Count == 0)
            {
                Logger?.LogDebug("[ActionIntercept] StampAll: nothing to crush");
                return;
            }
            EjectCardsFromStructure(mill, consumed);
        }

        private static bool TryResolveStampResult(object ore, out string resultId, out float bonus, out float resultMax)
        {
            resultId = null; bonus = 0f; resultMax = 0f;
            object cardData = GetCardData(ore);
            var interactions = ReadField(cardData, "CardInteractions") as IList;
            if (interactions == null) return false;
            foreach (var ci in interactions)
            {
                if (ci == null) continue;
                var compat = ReadField(ci, "CompatibleCards");
                if (compat == null) continue;
                var tags = ReadField(compat, "TriggerTags") as IList;
                var cards = ReadField(compat, "TriggerCards") as IList;
                if (tags == null || tags.Count == 0) continue;
                if (cards != null && cards.Count > 0) continue;
                var rcc = ReadField(ci, "ReceivingCardChanges");
                var transform = ReadField(rcc, "TransformInto");
                var uid = transform == null ? null : ReadField(transform, "UniqueID") as string;
                if (string.IsNullOrEmpty(uid)) continue;
                if (ReadField(rcc, "Special2Change") is UnityEngine.Vector2 change) bonus = change.x;
                if (ReadField(ReadField(transform, "SpecialDurability2"), "MaxValue") is float max) resultMax = max;
                resultId = uid;
                return true;
            }
            return false;
        }

        // Finds one card matching resultId that is not in preIds (i.e. newly spawned), from an
        // already-materialized candidate sequence — does NOT itself trigger any scan.
        private static object FindNewCard(string resultId, HashSet<int> preIds, IEnumerable<object> candidates)
        {
            foreach (var card in candidates)
            {
                if (CardUtil.GetCardUniqueId(card) != resultId) continue;
                if (card is UnityEngine.Object uo && preIds.Contains(uo.GetInstanceID())) continue;
                return card;
            }
            return null;
        }

        // Only EjectCardsFromStructure's fallback for a card CardUtil.TryRemoveCard refused calls this: hiding
        // a consumed card alone leaves it in GameManager.AllCards (plan Defect C).
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
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] TrySetActive: {ex.Message}"); }
        }

        private static string ResolveGrindResult(object card)
        {
            try
            {
                if (card == null) return null;
                object cardData = GetCardData(card);
                if (cardData == null) return null;

                // Every ReadField below warns once per type.member when the MEMBER is missing (a
                // game-update rename) and stays silent when it exists and holds null (a card with
                // nothing authored there), so the two cannot be confused with "not grindable".
                var interactions = ReadField(cardData, "CardInteractions") as IList;
                if (interactions == null) return null;

                foreach (var ci in interactions)
                {
                    if (ci == null) continue;
                    var compat = ReadField(ci, "CompatibleCards");
                    if (compat == null) continue;

                    var tags = ReadField(compat, "TriggerTags") as IList;
                    if (tags == null || tags.Count == 0) continue;

                    bool matched = false;
                    foreach (var t in tags)
                    {
                        if (t == null) continue;
                        string tagName = GetTagName(t);
                        if (tagName == GrindingToolTag || tagName == HammerToolTag)
                        {
                            matched = true;
                            break;
                        }
                    }
                    if (!matched) continue;
                    if (!IsMillActionName(ReadInteractionActionName(ci))) continue;

                    var rcc = ReadField(ci, "ReceivingCardChanges");
                    if (rcc == null) continue;

                    // Path A: TransformInto (most vanilla grind items)
                    var transformCard = ReadField(rcc, "TransformInto");
                    if (transformCard != null)
                    {
                        var uid = ReadField(transformCard, "UniqueID") as string;
                        if (!string.IsNullOrEmpty(uid)) return uid;
                    }

                    // Path B: ProducedCards[0].DroppedCards[0] fallback
                    var prodsRcc = ReadField(ci, "ProducedCards") as IList;
                    if (prodsRcc == null || prodsRcc.Count == 0) continue;
                    var collRcc = prodsRcc[0];
                    var dropsRcc = ReadField(collRcc, "DroppedCards") as IList;
                    if (dropsRcc == null || dropsRcc.Count == 0) continue;
                    var dropRcc = dropsRcc[0];
                    var dcRcc   = ReadField(dropRcc, "DroppedCard");
                    // DroppedCardWarpData is a JSON-only key: CardDrop declares no such field, so
                    // this fallback is absent by design and must never warn.
                    var uidRcc  = GetUniqueIdFromObject(dcRcc)
                               ?? ReadOptionalField(dropRcc, "DroppedCardWarpData") as string;
                    if (!string.IsNullOrEmpty(uidRcc)) return uidRcc;
                }
            }
            catch (Exception ex)
            {
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] ResolveGrindResult error: {ex.Message}");
            }
            return null;
        }

        // ============================================================
        //  STRUCTURAL READS - per-cause warn-once (audit G5)
        //  Everything that walks the game's CardData graph by reflection (CardModel,
        //  CardInteractions, SpecialDurabilityN, ...) shares this block. A renamed or removed
        //  member does NOT throw: GetField/GetProperty return null, `?.` carries it forward and the
        //  caller sees "this card has no such data", which is also the normal answer for a card
        //  that simply has none. A catch-based latch never sees that, so the missing MEMBER logs
        //  once per cause (type.member, never one shared flag, so the first rename to trip cannot
        //  silence a later, different one), while a member that exists and holds null stays silent.
        //  See memory reference_latch_blind_to_nonthrowing_null.
        // ============================================================

        private static readonly HashSet<string> _warnedCauses = new HashSet<string>(StringComparer.Ordinal);

        private static void WarnOnce(string cause, string message)
        {
            if (!_warnedCauses.Add(cause)) return;
            Logger?.LogWarning($"[ActionIntercept] {message} [cause '{cause}', logged once per session]");
        }

        private static readonly Dictionary<(Type, string), FieldInfo> _fieldLookups = new Dictionary<(Type, string), FieldInfo>();

        private static FieldInfo LookupField(Type type, string name)
        {
            var key = (type, name);
            if (!_fieldLookups.TryGetValue(key, out var field))
            {
                field = type.GetField(name, Flags);
                _fieldLookups[key] = field;
            }
            return field;
        }

        // Reads instance field `name` of `target`. A null target is the caller's own normal
        // negative (silent). A MISSING field is a rename: warn once per type.member and return
        // null. A field that exists and holds null returns null silently.
        private static object ReadField(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            var field = LookupField(type, name);
            if (field == null)
            {
                WarnOnce($"{type.Name}.{name}",
                    $"{type.Name}.{name} not found (reflection miss), so the features that read it are degraded; the game may have renamed it");
                return null;
            }
            return field.GetValue(target);
        }

        // The same read for a field that is absent BY DESIGN (a JSON-only *WarpData key, a probe
        // for a field that only some game versions declare): never warns.
        private static object ReadOptionalField(object target, string name)
        {
            if (target == null) return null;
            return LookupField(target.GetType(), name)?.GetValue(target);
        }

        private static string GetTagName(object tag)
        {
            if (tag is UnityEngine.Object uo) return uo.name;
            WarnOnce("CardTag-not-UnityObject",
                $"a CardTag element is a {tag?.GetType().Name ?? "null"}, not a UnityEngine.Object, so its name cannot be read; tag-based Grind All and Hammer All matching is degraded");
            return null;
        }

        private static readonly Dictionary<Type, PropertyInfo> _cardModelProps = new Dictionary<Type, PropertyInfo>();

        private static object GetCardData(object card)
        {
            try
            {
                if (card == null) return null;
                var cardType = card.GetType();
                if (!_cardModelProps.TryGetValue(cardType, out var cmProp))
                {
                    cmProp = cardType.GetProperty("CardModel", Flags);
                    _cardModelProps[cardType] = cmProp;
                }
                if (cmProp == null)
                {
                    // Non-throwing: the catch below can never see this exit.
                    WarnOnce("InGameCardBase.CardModel",
                        $"{cardType.Name}.CardModel not found (reflection miss): every Grind All, Hammer All and quality gate that reads a card's data is now a no-op; the game may have renamed it");
                    return null;
                }
                // A null VALUE is a card with no model (pooled or destroyed): the normal negative.
                return cmProp.GetValue(card);
            }
            catch (Exception ex)
            {
                // Hot path — feeds ResolveGrindResult (per Grind All item) and the hammer
                // metal-quality-tool gate (per hammer swing). Breadcrumb so a CardModel
                // reflection break doesn't silently degrade those into no-ops.
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] GetCardData({card?.GetType().Name}) failed: {ex.Message}");
                return null;
            }
        }

        // ============================================================
        //  FISHPOND - initialize stats on fish spawned by Catch / Ice Fish actions
        // ============================================================

        private static bool IsFishCatchAction(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (string.Equals(name, "Catch Crayfish", StringComparison.Ordinal)) return false;
            return name.StartsWith("Catch ", StringComparison.Ordinal) ||
                   name.StartsWith("Ice Fish ", StringComparison.Ordinal);
        }

        // Rod and spear fishing (plan feature 17). The pond's species counts and the stat each lives in, the
        // same map the Catch DismantleActions use (Fishpond_Filled.json / Fishpond_Stocked.json). The Crayfish
        // (Progress) is left out: it is no fish (IsFishCatchAction excludes it too) and section 17 names fish.
        private static readonly string[] PondRodSpeciesStats =
            { "SpecialDurability1", "SpecialDurability2", "SpecialDurability3", "SpoilageTime", "UsageDurability", "FuelCapacity", "SpecialDurability4" };
        private static readonly string[] PondRodSpeciesUids =
            { PikeGUID, PerchGUID, MinnowGUID, SturgeonGUID, TroutGUID, CharGUID, EelGUID };

        private static bool IsPondRodSpearAction(string actionKey)
        {
            if (string.IsNullOrEmpty(actionKey)) return false;
            return actionKey.EndsWith("_Fish_ActionName", StringComparison.Ordinal)
                || actionKey.EndsWith("_Spearfish_ActionName", StringComparison.Ordinal);
        }

        // Index into PondRodSpecies*, weighted by each species' count (only counts of 1 or more take part),
        // or -1 when none qualifies or no count can be read.
        private static int PickPondRodSpecies(object pond)
        {
            var weights = new float[PondRodSpeciesStats.Length];
            float total = 0f;
            for (int i = 0; i < PondRodSpeciesStats.Length; i++)
            {
                float count = CardUtil.GetDurability(pond, PondRodSpeciesStats[i]);
                if (float.IsNaN(count))
                {
                    WarnOnce("PondRod-read-" + PondRodSpeciesStats[i],
                        $"CardUtil.GetDurability could not read the pond's {PondRodSpeciesStats[i]} (reflection miss): that species is left out of rod and spear fishing");
                    continue;
                }
                if (count < 1f) continue;
                weights[i] = count;
                total += count;
            }
            if (total <= 0f) return -1;
            float roll = UnityEngine.Random.Range(0f, total);
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                if (roll < weights[i]) return i;
                roll -= weights[i];
            }
            for (int i = weights.Length - 1; i >= 0; i--)
                if (weights[i] > 0f) return i;
            return -1;
        }

        private static void CatchPondFishWithTool(object pond)
        {
            int pick = PickPondRodSpecies(pond);
            if (pick < 0)
            {
                WarnOnce("PondRod-empty-after", "a Fish or Spearfish action finished on a pond that holds no species at 1 or more; nothing was caught");
                return;
            }
            string stat = PondRodSpeciesStats[pick];
            string uid = PondRodSpeciesUids[pick];
            float count = CardUtil.GetDurability(pond, stat);
            if (float.IsNaN(count) || !CardUtil.SetDurability(pond, stat, count - 1f))
            {
                WarnOnce("PondRod-write-" + stat, $"could not lower the pond's {stat} after a rod or spear catch (reflection miss); no fish was spawned");
                return;
            }
            var stats = new Dictionary<string, float>(StringComparer.Ordinal) { { "SpecialDurability2", FishCaughtQuality } };
            if (TryGetFreshCaughtFishWeight(uid, out float weight))
                stats["SpecialDurability1"] = weight;
            // The floor the Catch actions use: a fish the spawn left unset is set by the same delayed pass.
            var preIds = SnapshotFishCardIds();
            SpawnService.Spawn(uid, stats);
            StartDelayedFishInitialization(preIds);
            Logger?.LogDebug($"[ActionIntercept] PondRodSpear: caught {uid} ({stat} {count} -> {count - 1f})");
        }

        private static void StartDelayedFishInitialization(HashSet<int> preIds)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm is UnityEngine.MonoBehaviour mb)
                {
                    mb.StartCoroutine(ApplyFishStatsAfterSpawn(preIds));
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] StartDelayedFishInitialization: {ex.Message}");
            }

            ApplyFishCaughtStats(preIds, FishCaughtQuality, includeZeroQualityExisting: true);
        }

        // A catch costs exactly its DismantleAction's own DaytimeCost (Catch 4, Ice Fish 3, Crayfish and Frog
        // 2, Ice Fish Crayfish 2; balance pass 2026-10-14): the game spends it before the fish spawns. This coroutine charges no time of
        // its own. It used to spend one extra daytime point through a reflected GameManager.SpendDaytimePoints,
        // added on 2026-05-17 while chasing the 0%-quality fish (Documentation/Retrospectives/_Archive/
        // wdi-fish-quality.md, attempt 5) and kept after attempt 6 moved the stat write ahead of it. On EA 0.68b
        // that private method takes 15 parameters and the call passed 14, so it threw on every catch and the
        // point was never charged; the fish still read Quality 72 (harness T2.603, 2026-10-02), so the call is
        // gone rather than repaired: a working one would make every catch cost one more point than its button,
        // the guide and the tooltip say.
        private static IEnumerator ApplyFishStatsAfterSpawn(HashSet<int> preIds)
        {
            yield return null;

            int updated = ApplyFishCaughtStats(preIds, FishCaughtQuality, includeZeroQualityExisting: true);
            if (updated == 0)
            {
                yield return null;
                updated = ApplyFishCaughtStats(preIds, FishCaughtQuality, includeZeroQualityExisting: true);
            }

            // One more frame before the last retry, the frame the removed tick call used to yield.
            yield return null;

            if (updated == 0)
                ApplyFishCaughtStats(preIds, FishCaughtQuality, includeZeroQualityExisting: true);
        }

        private static HashSet<int> SnapshotFishCardIds()
        {
            var result = new HashSet<int>();
            try
            {
                foreach (var card in EnumerateGameManagerAllCards())
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
                foreach (var c in EnumerateGameManagerAllCards())
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
            => SnapshotCardIdsByUniqueId(uniqueId, EnumerateGameManagerAllCards());

        // Overload taking an already-chosen candidate source, so batch callers (Grind All) can
        // pass the cheap AllCards enumeration instead of triggering their own scene scan per call.
        private static HashSet<int> SnapshotCardIdsByUniqueId(string uniqueId, IEnumerable<object> candidates)
        {
            var result = new HashSet<int>();
            try
            {
                foreach (var card in candidates)
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
                if (ApplyLumpSpawnStats(preIds, allowExistingFallback: false) > 0)
                {
                    updatedNewCard = true;
                    break; // found & initialized the new lump — stop scanning the scene each frame
                }
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

                foreach (var c in EnumerateGameManagerAllCards())
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

            if (allowExistingCard && !LumpNeedsStrikes(card))
                return false;

            // Strikes only (a copper bar's 2, the base game's rule for Metal Type 100). The quality is the cast
            // nuggets' average, set at spawn (BuildLumpStats); a 50 floor here raised poor metal, and through the
            // existing-card fallback above, ANY Unfinished Metal Bar that Hammer All found, to 50 (Nexus 2026-10-11).
            bool changed = CardUtil.SetDurability(card, "SpecialDurability1", LumpInitialStrikes);
            if (!changed) return false;

            RefreshCardDurabilityVisuals(card);
            Logger?.LogDebug($"[ActionIntercept] LumpInit: strikes={LumpInitialStrikes}");
            return true;
        }

        private static bool LumpNeedsStrikes(object card)
        {
            float strikes = CardUtil.GetDurability(card, "SpecialDurability1");
            return float.IsNaN(strikes) || strikes <= 0f;
        }

        // GameManager's card lists, looked up once per (type, name) and read through the cached
        // member. Returns null when the member is missing or holds null; the callers report that.
        private static readonly Dictionary<(Type, string), MemberInfo> _gmListMembers = new Dictionary<(Type, string), MemberInfo>();

        private static IList ReadGameManagerList(object gm, string name)
        {
            var type = gm.GetType();
            if (!_gmListMembers.TryGetValue((type, name), out var member))
            {
                member = (MemberInfo)type.GetField(name, Flags) ?? type.GetProperty(name, Flags);
                _gmListMembers[(type, name)] = member;
            }
            if (member is FieldInfo f) return f.GetValue(gm) as IList;
            if (member is PropertyInfo p) return p.GetValue(gm) as IList;
            return null;
        }

        private static object FindLatestKnownCardByUniqueId(string uniqueId)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm == null)
                {
                    WarnOnce("GameManager-null", "GameManager instance not found (CardUtil.GetGameManagerInstance returned null): freshly spawned cards cannot be located, so spawn-time stat fixups are skipped");
                    return null;
                }
                foreach (string listName in new[] { "LatestCreatedCards", "AllCards" })
                {
                    var cards = ReadGameManagerList(gm, listName);
                    if (cards == null)
                    {
                        WarnOnce($"GameManager.{listName}", $"GameManager.{listName} not found or null (reflection miss): freshly spawned cards are located less reliably; the game may have renamed it");
                        continue;
                    }
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

        // AllCards first, then a full-scene FindObjectsOfType scan once that is exhausted. The scan
        // is expensive and finds nothing a freshly spawned card needs (GiveCard adds to AllCards
        // synchronously), so ONLY Grind All's one-shot batch fallback calls this. Every retry loop
        // (lump, nugget, fish, iron, copper self-smelt) and every snapshot that pairs with one uses
        // EnumerateGameManagerAllCards: a loop that enumerates this to the end rescans the scene on
        // every pass (audit F4/W4: up to ~60 scans per Cast Metal Lump).
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
            if (_cardBaseType == null)
            {
                WarnOnce("InGameCardBase-type", "InGameCardBase type not found (reflection miss): the scene-scan fallback for locating cards is unavailable; the game may have renamed it");
                yield break;
            }

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
                if (gm == null)
                {
                    WarnOnce("GameManager-null", "GameManager instance not found (CardUtil.GetGameManagerInstance returned null): freshly spawned cards cannot be located, so spawn-time stat fixups are skipped");
                }
                else
                {
                    cards = ReadGameManagerList(gm, "AllCards");
                    if (cards == null)
                        WarnOnce("GameManager.AllCards", "GameManager.AllCards not found or null (reflection miss): the lump, nugget, fish and iron spawn-time stat fixups can no longer find the cards they just spawned; the game may have renamed it");
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

                // Type only. Quality is the melted item's own (IronSmeltType / CopperSelfSmeltQuality), never a
                // floor: this used to raise the nugget to 50 whatever the item was made of (Nexus report 2026-10-11).
                if (IsIronSmeltPending())
                {
                    bool ok = CardUtil.SetDurability(__0, "SpecialDurability4", IronBarMetalType);
                    if (ok) Logger?.LogDebug("[ActionIntercept] IronSmelt (GiveCard): set Type=200 on iron nugget");
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
                foreach (var card in EnumerateGameManagerAllCards())
                {
                    if (!IronSmeltItemIds.Contains(CardUtil.GetCardUniqueId(card))) continue;
                    float progress = CardUtil.GetDurability(card, "Progress");
                    float max = CardUtil.GetDurabilityMax(card, "Progress");
                    if (!float.IsNaN(progress) && !float.IsNaN(max) && max > 0f && progress >= max)
                        return true;
                }
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] IsIronSmeltPending: {ex.Message}"); }
            return false;
        }

        // ActionRouter's After callback is synchronous and cannot itself yield an extra
        // frame, so a failed first attempt (e.g. GiveCard hasn't registered the new
        // nugget in AllCards yet) is retried via a decoupled one-frame-later coroutine —
        // the same pattern as StartDelayedNuggetInitialization/StartDelayedLumpInitialization.
        private static void StartDelayedIronBarTypeRetry(HashSet<int> preIds, float sourceQualityPct)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm is UnityEngine.MonoBehaviour mb)
                {
                    mb.StartCoroutine(RetryIronBarTypeAfterOneFrame(preIds, sourceQualityPct));
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] StartDelayedIronBarTypeRetry: {ex.Message}");
            }
            ApplyIronBarType(preIds, sourceQualityPct);
        }

        private static IEnumerator RetryIronBarTypeAfterOneFrame(HashSet<int> preIds, float sourceQualityPct)
        {
            yield return null;
            ApplyIronBarType(preIds, sourceQualityPct);
        }

        // A new nugget's metal type is NOT 0 by the time these one-frame passes read it: vanilla
        // MetalNugget's OnStatsChangeAction "Backwards Comptability - if Metal Type is 0 put it to
        // 100" stamps every fresh nugget Copper (100) first. So 100 means "untyped default", and only
        // another value (iron 200, tin 120, from the sluice or another path) means typed elsewhere.
        // Reading 100 as typed skipped every nugget: walkthrough T2.131 (r39) found six copper
        // self-smelt nuggets with no quality floor.
        private static bool IsTypedByAnotherPath(float sd4) =>
            !float.IsNaN(sd4) && sd4 > 0f && Math.Abs(sd4 - NuggetCopperType) > 0.5f;

        private static int ApplyIronBarType(HashSet<int> preIds, float sourceQualityPct)
        {
            int updated = 0;
            try
            {
                foreach (var card in EnumerateGameManagerAllCards())
                {
                    if (CardUtil.GetCardUniqueId(card) != CopperNuggetGUID) continue;
                    if (card is UnityEngine.Object uo && preIds != null && preIds.Contains(uo.GetInstanceID())) continue;

                    float sd4 = CardUtil.GetDurability(card, "SpecialDurability4");
                    if (IsTypedByAnotherPath(sd4)) continue;

                    // The type is what marks a nugget handled. Quality is raised to the melted item's own
                    // and may legitimately stay 0 (an item made before 1.12.3 carries none), so it is not
                    // counted: counting it sent every quality-0 melt round the one-frame retry for nothing.
                    bool ok = CardUtil.SetDurability(card, "SpecialDurability4", IronBarMetalType);
                    ApplyBlastNuggetQuality(card, sourceQualityPct);
                    if (ok) updated++;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyIronBarType: {ex.Message}"); }

            if (updated > 0)
                Logger?.LogDebug($"[ActionIntercept] IronSmelt: set Type=200 quality={sourceQualityPct:P0} (the melted item's own) on {updated} iron nugget(s)");
            return updated;
        }

        // ============================================================
        //  COPPER SELF-SMELT ITEMS — quality floor on nuggets from Progress OnFull
        //  Mirrors the iron handling above, but the item set is DISCOVERED at runtime
        //  (any CardData whose Progress.OnFull ProducedCards a copper nugget) instead
        //  of a hardcoded UID list, so gears/blades/future mod items are all covered
        //  without a code change. SD4 type is left alone (stays copper) — only the
        //  forge/workshop's "machine processing must never lower quality" floor applies.
        // ============================================================

        private static HashSet<string> _selfSmeltCopperItemIds;
        private const string WdiUidPrefix = "water_sawmill_";
        private static readonly HashSet<string> NoSelfSmeltItemIds = new HashSet<string>(StringComparer.Ordinal);

        // The set is built from GameLoad.DataBase.AllData, never CardUtil.FindCardsWhere: that walks
        // the framework's typed SO cache, which is filled before JsonDataLoader adds the mod cards, so
        // it yielded ten vanilla items and no WDI gear, and WDI's own self-smelted copper kept quality
        // 0 (walkthrough T2.131 FAIL 2026-10-02; CSFFModFramework plan section 29). AllData holds the
        // vanilla and the JSON-loaded cards alike. Nothing is latched until AllData is reachable.
        private static HashSet<string> GetSelfSmeltCopperItemIds()
        {
            if (_selfSmeltCopperItemIds != null) return _selfSmeltCopperItemIds;

            var allData = GetDataBaseAllData();
            if (allData == null)
            {
                WarnOnce("SelfSmelt-AllData-unreachable",
                    "GameLoad.DataBase.AllData is not reachable, so the copper self-smelt quality floor is off until it is (retried on every call)");
                return NoSelfSmeltItemIds;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            int cardDataSeen = 0;
            try
            {
                foreach (var obj in allData)
                {
                    // Only CardData reaches the predicate: its ReadField warns once per type.member,
                    // so every other AllData type would log a false rename.
                    if (obj == null || obj.GetType().Name != "CardData") continue;
                    cardDataSeen++;
                    // WDI's own items only. The scan used to keep every match, vanilla's Unfinished and Finished Metal
                    // Bar and its blanks among them, so any copper melted in any furnace came back at Metal Quality 50
                    // whatever went in (Nexus report 2026-10-11). Root CLAUDE.md: mod-level repair filters by prefix.
                    string uid = CardUtil.GetCardUniqueId(obj);
                    if (string.IsNullOrEmpty(uid) || !uid.StartsWith(WdiUidPrefix, StringComparison.Ordinal)) continue;
                    if (!ProducesCopperNuggetOnFull(obj)) continue;
                    set.Add(uid);
                }
                set.ExceptWith(IronSmeltItemIds); // those get SD4-typed by IronSmeltType instead
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] GetSelfSmeltCopperItemIds: {ex.Message}");
            }

            Logger?.LogDebug($"[ActionIntercept] Discovered {set.Count} self-smelt-to-copper-nugget item(s) among {cardDataSeen} CardData in AllData: {string.Join(", ", set)}");
            _selfSmeltCopperItemIds = set;
            return set;
        }

        // GameLoad.Instance is a public static field (.decomp/GameLoad.cs); the scene lookup is the
        // fallback. Every miss returns null and is reported by the caller's warn-once.
        private static IEnumerable GetDataBaseAllData()
        {
            try
            {
                var gameLoadType = AccessTools.TypeByName("GameLoad");
                if (gameLoadType == null) return null;
                var gameLoad = gameLoadType.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    ?? UnityEngine.Object.FindObjectOfType(gameLoadType);
                if (gameLoad == null) return null;
                var dataBase = AccessTools.Field(gameLoadType, "DataBase")?.GetValue(gameLoad);
                if (dataBase == null) return null;
                return AccessTools.Field(dataBase.GetType(), "AllData")?.GetValue(dataBase) as IEnumerable;
            }
            catch (Exception ex)
            {
                WarnOnce("SelfSmelt-AllData-threw", $"reading GameLoad.DataBase.AllData threw: {ex.Message}");
                return null;
            }
        }

        private static bool ProducesCopperNuggetOnFull(object cardData)
        {
            try
            {
                if (cardData == null) return false;
                // This runs once over every CardData in the game and its answer is latched forever
                // (GetSelfSmeltCopperItemIds), so a missing member here would silently empty the
                // self-smelt set for the whole session: ReadField warns once per type.member.
                var progress = ReadField(cardData, "Progress");
                if (progress == null) return false;
                if (!(ReadField(progress, "Active") is bool active) || !active) return false;
                if (!(ReadField(progress, "HasActionOnFull") is bool hasOnFull) || !hasOnFull) return false;

                var onFull = ReadField(progress, "OnFull");
                var produced = ReadField(onFull, "ProducedCards") as IList;
                if (produced == null) return false;

                foreach (var coll in produced)
                {
                    var drops = ReadField(coll, "DroppedCards") as IList;
                    if (drops == null) continue;
                    foreach (var drop in drops)
                    {
                        if (drop == null) continue;
                        // DroppedCardWarpData is a JSON-only key (CardDrop declares no such field).
                        var warp = ReadOptionalField(drop, "DroppedCardWarpData") as string;
                        if (string.Equals(warp, CopperNuggetGUID, StringComparison.Ordinal)) return true;

                        var dc = ReadField(drop, "DroppedCard");
                        if (dc != null && string.Equals(GetUniqueIdFromObject(dc), CopperNuggetGUID, StringComparison.Ordinal)) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogDebug($"[ActionIntercept] ProducesCopperNuggetOnFull: {ex.Message}");
            }
            return false;
        }

        private static void StartDelayedCopperSelfSmeltQualityRetry(HashSet<int> preIds, float sourceQualityPct)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm is UnityEngine.MonoBehaviour mb)
                {
                    mb.StartCoroutine(RetryCopperSelfSmeltQualityAfterOneFrame(preIds, sourceQualityPct));
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] StartDelayedCopperSelfSmeltQualityRetry: {ex.Message}");
            }
            ApplyCopperSelfSmeltQuality(preIds, sourceQualityPct);
        }

        private static IEnumerator RetryCopperSelfSmeltQualityAfterOneFrame(HashSet<int> preIds, float sourceQualityPct)
        {
            yield return null;
            ApplyCopperSelfSmeltQuality(preIds, sourceQualityPct);
        }

        private static int ApplyCopperSelfSmeltQuality(HashSet<int> preIds, float sourceQualityPct)
        {
            int updated = 0;
            try
            {
                foreach (var card in EnumerateGameManagerAllCards())
                {
                    if (CardUtil.GetCardUniqueId(card) != CopperNuggetGUID) continue;
                    if (card is UnityEngine.Object uo && preIds != null && preIds.Contains(uo.GetInstanceID())) continue;

                    float sd4 = CardUtil.GetDurability(card, "SpecialDurability4");
                    if (IsTypedByAnotherPath(sd4)) continue;

                    if (ApplyBlastNuggetQuality(card, sourceQualityPct)) updated++;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyCopperSelfSmeltQuality: {ex.Message}"); }

            if (updated > 0)
                Logger?.LogDebug($"[ActionIntercept] CopperSelfSmelt: quality={sourceQualityPct:P0} (the melted item's own) on {updated} copper nugget(s)");
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
        private static bool _fishCatchNoSnapshotWarned;
        private static Type _cardBaseType;

        private static bool HandleHammerAllInner(object forge, bool workshopQualityBoost)
        {
            Logger?.LogDebug($"[HammerDiag] HammerAll fired on '{CardUtil.GetCardUniqueId(forge)}' (workshopQualityBoost={workshopQualityBoost})");

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
                            if (inner != null && !alreadyProcessed.Contains(inner) && IsQualityOnlyCandidate(inner))
                                qualityOnlyItems.Add(inner);
                        }
                    }
                    else if (!alreadyProcessed.Contains(slotItem) && IsQualityOnlyCandidate(slotItem))
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

            Logger?.LogDebug(
                $"[HammerDiag] HammerAll: {toAdvance.Count} advance, {toComplete.Count} complete, {qualityOnlyItems.Count} quality-only");

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
                    // so SD1 counts strikes UP. Increment by 1 per Hammer All press. A Smith-hammerable
                    // item here (a copper Metal Nugget at Strikes 0) counts DOWN, so its Strikes stay put.
                    if (!IsSmithHammerable(card))
                        IncrementAccumulatingStrikeCount(card);
                    RefreshCardDurabilityVisuals(card);
                }
                Logger?.LogDebug($"[HammerDiag] HammerAll: workshop quality boosted {boosted} item(s) by +{WorkshopMetalQualityBoost}");
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
            // The shared Metal Sheet keeps the nuggets' Metal Quality. The nuggets are ejected inside
            // the consume, so their quality is read there, before it. Nails are not read at all. The lump (an
            // Unfinished Metal Bar) keeps their average, as the base game's bar recipe does (BuildLumpStats). The
            // MetalPartsPresent guard only matters on a framework older than 2.28.0, where no sheet spawns at all.
            bool sheetKeepsQuality = kind == WorkshopCraftKind.CopperSheet && ActCompat.MetalPartsPresent;
            bool readNuggetQuality = sheetKeepsQuality || kind == WorkshopCraftKind.CopperLump;
            // The iron crafts spawn the Metal Nail and Metal Sheet stamped Metal Type iron, and the sheet keeps the
            // nuggets' Metal Quality as the copper sheet does (BuildActIronStats).
            bool iron = IsIronCraft(kind);
            string grade = iron ? "iron-grade" : "copper";
            List<float> nuggetQualities;
            bool consumed = iron
                ? TryConsumeIronNuggets(workshop, cost, out nuggetQualities)
                : TryConsumeCopperNuggets(workshop, cost, readNuggetQuality, out nuggetQualities);
            if (!consumed)
            {
                Logger?.LogDebug($"[ActionIntercept] {kind}: not enough {grade} nuggets in workshop inventory at completion");
                return false;
            }

            string resultId;
            int resultCount;
            Dictionary<string, float> resultStats = null;
            switch (kind)
            {
                case WorkshopCraftKind.CopperSheet:
                    resultId = CopperSheetID;
                    resultCount = 1;
                    if (sheetKeepsQuality) resultStats = BuildSheetStats(nuggetQualities);
                    break;
                case WorkshopCraftKind.CopperNails:
                    resultId = CopperNailsID;
                    resultCount = 6;
                    break;
                case WorkshopCraftKind.CopperLump:
                    resultId = UnfinishedLumpID;
                    resultCount = 1;
                    resultStats = BuildLumpStats(nuggetQualities);
                    break;
                case WorkshopCraftKind.IronRivets:
                    resultId = IronRivetsID;
                    resultCount = 6;
                    if (ActCompat.MetalPartsPresent) resultStats = BuildActIronStats(null);
                    break;
                case WorkshopCraftKind.IronSheet:
                    resultId = IronSheetID;
                    resultCount = 1;
                    if (ActCompat.MetalPartsPresent) resultStats = BuildActIronStats(nuggetQualities);
                    break;
                default:
                    return false;
            }

            var lumpPreIds = (kind == WorkshopCraftKind.CopperLump)
                ? SnapshotCardIdsByUniqueId(UnfinishedLumpID)
                : null;

            for (int i = 0; i < resultCount; i++)
                SpawnService.Spawn(resultId, resultStats);

            if (lumpPreIds != null)
                StartDelayedLumpInitialization(lumpPreIds);

            RefreshOpenInventoryPopup();
            Logger?.LogDebug($"[ActionIntercept] {kind}: consumed {cost} {grade} nuggets, spawned {resultCount}x {resultId} on board"
                + (resultStats != null && resultStats.TryGetValue(MetalQualityStat, out float spawnQuality) ? $" at {MetalQualityStat} {spawnQuality} (average of {nuggetQualities.Count} readable of {cost} nuggets)" : "")
                + (resultStats != null && resultStats.TryGetValue(MetalTypeStat, out float spawnType) ? $" stamped {MetalTypeStat} {spawnType}" : ""));
            return true;
        }

        // Metal Quality is SpecialDurability2 on ACT's Metal Sheet and on vanilla's Metal Nugget alike.
        // It is one of SpawnService's StatNames: an unknown name is ignored by the framework.
        private const string MetalQualityStat = "SpecialDurability2";

        // The sheet's spawn stats: the AVERAGE Metal Quality of the nuggets that could be read
        // (plan Open question 4's default, the same way a masked blueprint averages its ingredients).
        // A nugget whose value could not be read is skipped by the caller and never counted as 0. With
        // no readable value there is nothing to carry, so the sheet spawns at its default quality, as
        // it did before, and one warn-once breadcrumb says why.
        // The cast bar's spawn stats: the nuggets' average Metal Quality in both of the bar's quality stats
        // (Metal Quality and the Quality the popup shows), as Bp_MetalBar's mask 448 averages its six nuggets.
        private static Dictionary<string, float> BuildLumpStats(List<float> nuggetQualities)
        {
            var stats = BuildSheetStats(nuggetQualities);
            if (stats != null && stats.TryGetValue(MetalQualityStat, out float average))
                stats["SpecialDurability3"] = average;
            return stats;
        }

        private static Dictionary<string, float> BuildSheetStats(List<float> nuggetQualities)
        {
            if (nuggetQualities == null || nuggetQualities.Count == 0)
            {
                WarnOnce("WorkshopSheet-no-readable-metal-quality",
                    $"Hammer Copper Sheet or Hammer Iron Sheet could not read {MetalQualityStat} (Metal Quality) from any of its nuggets, so the sheet spawns at its default Metal Quality 0; CardUtil.GetDurability returned NaN or the game renamed the stat");
                return null;
            }
            float sum = 0f;
            foreach (float quality in nuggetQualities) sum += quality;
            return new Dictionary<string, float>(StringComparer.Ordinal) { { MetalQualityStat, sum / nuggetQualities.Count } };
        }

        // The iron crafts' spawn stats with ACT: its Metal Nail or Metal Sheet stamped Metal Type iron
        // (MetalTypeStat; both cards default to copper, 100, and SpawnService snaps the value to their
        // ValidValues, which list 200). The sheet also keeps the nuggets' Metal Quality through
        // BuildSheetStats, as ACT's own Forge Iron Sheet does since 1.18.0; the nail has no quality stat
        // and passes null.
        private static Dictionary<string, float> BuildActIronStats(List<float> nuggetQualities)
        {
            var stats = (nuggetQualities != null ? BuildSheetStats(nuggetQualities) : null)
                ?? new Dictionary<string, float>(StringComparer.Ordinal);
            stats[MetalTypeStat] = ActCompat.IronMetalType;
            return stats;
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
            // Confirmed 2026-08-23 via in-game log + screenshots: vanilla metal-bar items
            // (e.g. MetalBarUnfinished) carry TWO independent quality-named stats —
            // SpecialDurability2 "Metal Quality" AND SpecialDurability3 "Quality". This
            // used to boost SD2 only, so the player-visible "Quality" line (SD3) never
            // moved while "Metal Quality" (SD2) correctly climbed +5/press. Boost both
            // active "Quality"-named slots — matches CompleteHammeredCard's existing
            // treatment of SD2+SD3 as equally load-bearing quality carried through a
            // transform (ApplyMinimumQualityPercent call for each below it).
            string uid = CardUtil.GetCardUniqueId(card) ?? "<null-uid>";
            object cardData = GetCardData(card);
            if (cardData == null)
            {
                Logger?.LogDebug($"[HammerDiag] QualityBoost skip '{uid}': CardModel null");
                return false;
            }

            bool anyBoosted = false;
            foreach (var stat in QualityDurabilitySlots)
            {
                if (!IsDurabilityDefinitionActive(cardData, stat) || !DurabilityStatNameContains(cardData, stat, "Quality"))
                    continue;

                float currentQuality = CardUtil.GetDurability(card, stat);
                if (float.IsNaN(currentQuality)) continue;

                float maxQuality = CardUtil.GetDurabilityMax(card, stat);
                if (float.IsNaN(maxQuality) || maxQuality <= 0f) maxQuality = 100f;

                // WDI-B7: the boost stops at WorkshopQualityBoostCeiling; a slot at or above it gains nothing.
                float ceiling = Math.Min(maxQuality, WorkshopQualityBoostCeiling);
                if (currentQuality >= ceiling) continue;
                float newQuality = Math.Min(ceiling, currentQuality + WorkshopMetalQualityBoost);
                if (newQuality <= currentQuality) continue;

                bool ok = CardUtil.SetDurability(card, stat, newQuality);
                Logger?.LogDebug($"[HammerDiag] QualityBoost '{uid}' {stat}: {currentQuality:F1} -> {newQuality:F1} (cap {maxQuality:F1}), ok={ok}");
                anyBoosted |= ok;
            }
            return anyBoosted;
        }

        private static readonly string[] QualityDurabilitySlots = { "SpecialDurability2", "SpecialDurability3" };

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
                var sd1Def = ReadField(cardData, "SpecialDurability1");
                var hasOnZero = ReadField(sd1Def, "HasActionOnZero");
                if (hasOnZero is bool b && b) return; // has on-zero transform — handled by ClassifyHammerItem

                float cur = CardUtil.GetDurability(card, "SpecialDurability1");
                if (float.IsNaN(cur)) return;
                float max = CardUtil.GetDurabilityMax(card, "SpecialDurability1");
                if (!float.IsNaN(max) && max > 0f && cur >= max) return; // already at max

                CardUtil.SetDurability(card, "SpecialDurability1", cur + 1f);
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] IncrementAccumulatingStrikeCount: {ex.Message}"); }
        }

        private static bool IsMetalQualityTool(object card)
        {
            object cardData = GetCardData(card);
            if (cardData == null) return false;

            // No card-tag gate here: the real candidates (vanilla MetalNugget,
            // MetalBarUnfinished) carry no gameplay tags at all — the workshop's own
            // InventoryFilter has to allowlist MetalNugget by exact UID for the same
            // reason. An active "...Quality"-named SpecialDurability2/3 is already a
            // reliable, narrow signal (WDI's own Copper Gears fail it since both are
            // inactive), so it stands alone. Checks both slots — vanilla metal-bar
            // items carry independent "Metal Quality" (SD2) AND "Quality" (SD3) stats.
            foreach (var stat in QualityDurabilitySlots)
            {
                if (IsDurabilityDefinitionActive(cardData, stat) && DurabilityStatNameContains(cardData, stat, "Quality"))
                    return true;
            }
            return false;
        }

        private static bool IsDurabilityDefinitionActive(object cardData, string statName)
        {
            try
            {
                var def = ReadField(cardData, statName);
                var active = ReadField(def, "Active");
                return active is bool b && b;
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] IsDurabilityDefinitionActive({statName}): {ex.Message}"); return false; }
        }

        private static bool DurabilityStatNameContains(object cardData, string statName, string text)
        {
            try
            {
                var def = ReadField(cardData, statName);
                var statNameObj = ReadField(def, "CardStatName");
                var defaultText = ReadField(statNameObj, "DefaultText") as string;
                return !string.IsNullOrEmpty(defaultText)
                    && defaultText.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] DurabilityStatNameContains({statName}): {ex.Message}"); return false; }
        }

        private struct HammerHitInfo
        {
            public bool   canHammer;
            public float  special1Change; // negative per-hit decrement, e.g. -1.0
            public float  fuelChange;     // negative heat loss per hit, e.g. -25.0
            public string onZeroResultId; // UniqueID to spawn when Strikes reach 0
            public string[] transferStats; // Durability values transferred by OnZero.ReceivingCardChanges
        }

        // Excludes any item with a Smith/tag_Hammer CardInteraction from the "quality-only"
        // fallback bucket, regardless of its current Strikes value — a Smith-hammerable item
        // stuck at 0 Strikes (never progressed, or an ACT accumulating-counter item at its
        // cap) must NOT get a free, unlimited quality boost via that bucket. Quality-only
        // boosting is reserved for genuine no-Strikes materials (raw heated nuggets).
        private static bool IsSmithHammerable(object card) => GetHammerHitInfo(card).canHammer;

        // WDI-B7 follow-up (T2.1705): the quality-only bucket also takes a Smith-hammerable item at
        // Strikes 0 with NO on-zero transform. Vanilla's copper Metal Nugget is exactly that (its Smith
        // interaction is Special1Change -1 and its Strikes start and stay at 0), so without this Hammer
        // All never reached a copper nugget at all. The boost is bounded by WorkshopQualityBoostCeiling
        // (60), so it is no longer the free, unlimited quality the IsSmithHammerable exclusion guarded
        // against. An item WITH an on-zero transform at Strikes 0 (never progressed) stays excluded.
        private static bool IsQualityOnlyCandidate(object card)
        {
            if (!IsMetalQualityTool(card)) return false;
            var hit = GetHammerHitInfo(card);
            if (!hit.canHammer) return true;
            if (!string.IsNullOrEmpty(hit.onZeroResultId)) return false;
            float strikes = CardUtil.GetDurability(card, "SpecialDurability1");
            return !float.IsNaN(strikes) && strikes <= 0f;
        }

        private static HammerHitInfo GetHammerHitInfo(object card)
        {
            var info = new HammerHitInfo();
            try
            {
                object cardData = GetCardData(card);
                if (cardData == null) return info;

                // 1. Find the smithing CardInteraction. Every ReadField warns once per
                // type.member on a MISSING member and stays silent on a null value (see the
                // STRUCTURAL READS block): a renamed field would otherwise read as "nothing here is
                // hammerable" or, past this point, as an item that never completes.
                var cis = ReadField(cardData, "CardInteractions") as IList;
                if (cis == null) return info;

                foreach (var ci in cis)
                {
                    if (ci == null) continue;

                    bool isHammer = string.Equals(CardUtil.GetActionName(ci), "Smith", StringComparison.OrdinalIgnoreCase);
                    if (!isHammer)
                    {
                        var compat = ReadField(ci, "CompatibleCards");
                        var trigTags = ReadField(compat, "TriggerTags") as IList;
                        if (trigTags != null)
                        {
                            foreach (var t in trigTags)
                            {
                                if (t == null) continue;
                                string tn = GetTagName(t);
                                if (tn == "tag_Hammer" || tn == HammerToolTag) { isHammer = true; break; }
                            }
                        }
                    }
                    if (!isHammer) continue;

                    var rcc = ReadField(ci, "ReceivingCardChanges");
                    if (rcc != null)
                    {
                        var s1Raw = ReadField(rcc, "Special1Change");
                        if (s1Raw is UnityEngine.Vector2 s1v) info.special1Change = s1v.x;
                        else if (s1Raw != null) WarnOnce("CardStateChange.Special1Change-type", $"CardStateChange.Special1Change is a {s1Raw.GetType().Name}, not a Vector2, so no hammer hit can be read; Hammer All is degraded");
                        var fRaw = ReadField(rcc, "FuelChange");
                        if (fRaw is UnityEngine.Vector2 fv) info.fuelChange = fv.x;
                        else if (fRaw != null) WarnOnce("CardStateChange.FuelChange-type", $"CardStateChange.FuelChange is a {fRaw.GetType().Name}, not a Vector2, so the heat lost per hammer hit cannot be read");
                    }
                    if (info.special1Change >= 0f) continue;

                    info.canHammer = true;
                    break;
                }
                if (!info.canHammer) return info;

                // 2. Read SpecialDurability1.OnZero result from CardData definition
                var sd1Def = ReadField(cardData, "SpecialDurability1");
                if (sd1Def == null) return info;
                var hasOnZero = ReadField(sd1Def, "HasActionOnZero");
                if (hasOnZero is bool b && !b) return info;

                var onZero = ReadField(sd1Def, "OnZero");
                var onZeroRcc = ReadField(onZero, "ReceivingCardChanges");
                if (onZeroRcc != null)
                {
                    info.transferStats = ReadTransferStats(onZeroRcc);
                    var transformCard = ReadField(onZeroRcc, "TransformInto");
                    info.onZeroResultId = GetUniqueIdFromObject(transformCard);
                    // TransformIntoWarpData is a JSON-only key (CardStateChange declares no such
                    // field), so this fallback is absent by design and must never warn.
                    if (string.IsNullOrEmpty(info.onZeroResultId))
                        info.onZeroResultId = ReadOptionalField(onZeroRcc, "TransformIntoWarpData") as string;
                    if (!string.IsNullOrEmpty(info.onZeroResultId)) return info;
                }

                var prods  = ReadField(onZero, "ProducedCards") as IList;
                if (prods == null || prods.Count == 0) return info;

                var coll   = prods[0];
                var drops  = ReadField(coll, "DroppedCards") as IList;
                if (drops == null || drops.Count == 0) return info;

                var drop   = drops[0];
                var dc     = ReadField(drop, "DroppedCard");
                info.onZeroResultId = GetUniqueIdFromObject(dc);
                if (string.IsNullOrEmpty(info.onZeroResultId))
                    info.onZeroResultId = ReadOptionalField(drop, "DroppedCardWarpData") as string;
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
            // CardStateChange declares no TransferProgress field in EA 0.68b (.decomp/CardStateChange.cs
            // has TransferCharges and TransferSpecial1-4), so this probe is always false: optional,
            // never a warning. Progress is therefore never carried across a Hammer All completion.
            if (GetBoolField(cardStateChange, "TransferProgress", optional: true)) stats.Add("Progress");
            if (GetBoolField(cardStateChange, "TransferSpecial1")) stats.Add("SpecialDurability1");
            // SD2/SD3 (quality) intentionally absent: CompleteHammeredCard always carries
            // them by percentage of max via ApplyMinimumQualityPercent, never raw.
            if (GetBoolField(cardStateChange, "TransferSpecial4")) stats.Add("SpecialDurability4");
            return stats.ToArray();
        }

        private static bool GetBoolField(object obj, string fieldName, bool optional = false)
        {
            try
            {
                var value = optional ? ReadOptionalField(obj, fieldName) : ReadField(obj, fieldName);
                return value is bool b && b;
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] GetBoolField({fieldName}): {ex.Message}"); return false; }
        }

        private static string GetUniqueIdFromObject(object value)
        {
            try
            {
                if (value == null) return null;
                if (value is UniqueIDScriptable uid) return uid.UniqueID;
                return ReadField(value, "UniqueID") as string;
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] GetUniqueIdFromObject: {ex.Message}"); return null; }
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

        // Per-type lookups for RefreshCardDurabilityVisuals, which runs once per card a handler
        // touches: the property/field pair on the card, and the refresh method on its visuals.
        private static readonly Dictionary<Type, (PropertyInfo prop, FieldInfo field)> _cardVisualsMembers =
            new Dictionary<Type, (PropertyInfo prop, FieldInfo field)>();
        private static readonly Dictionary<Type, MethodInfo> _refreshDurabilitiesMethods = new Dictionary<Type, MethodInfo>();

        private static void RefreshCardDurabilityVisuals(object card)
        {
            try
            {
                if (card == null) return;
                var cardType = card.GetType();
                if (!_cardVisualsMembers.TryGetValue(cardType, out var members))
                {
                    members = (cardType.GetProperty("CardVisuals", Flags), cardType.GetField("CardVisuals", Flags));
                    _cardVisualsMembers[cardType] = members;
                }
                if (members.prop == null && members.field == null)
                {
                    WarnOnce("InGameCardBase.CardVisuals",
                        $"{cardType.Name}.CardVisuals not found (reflection miss): stat bars will not redraw after a handler changes a card's durabilities; the game may have renamed it");
                    return;
                }
                var visuals = members.prop?.GetValue(card) ?? members.field?.GetValue(card);
                if (visuals == null) return; // a card with no graphics yet: the normal negative

                var visualsType = visuals.GetType();
                if (!_refreshDurabilitiesMethods.TryGetValue(visualsType, out var refresh))
                {
                    refresh = visualsType.GetMethod("RefreshDurabilities", Flags);
                    _refreshDurabilitiesMethods[visualsType] = refresh;
                }
                if (refresh == null)
                {
                    WarnOnce("CardGraphics.RefreshDurabilities",
                        $"{visualsType.Name}.RefreshDurabilities not found (reflection miss): stat bars will not redraw after a handler changes a card's durabilities; the game may have renamed it");
                    return;
                }
                refresh.Invoke(visuals, null);
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] RefreshCardDurabilityVisuals: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
        }

        // Every card a station action consumes leaves the game here: Blast, Saw All, Sluice All, Grind All
        // and the Workshop crafts all call this (plan Defect C). Until this fix it only took the card out of
        // the station's slots and hid its GameObject, so the card stayed in GameManager.AllCards: it still
        // counted on the board and kept running its own per-tick actions (T2.602, 2026-10-02: a melted ACT
        // Metal Sheet kept running "Smelt High-Heat Ore" through the later Blasts).
        //
        // The order, read from .decomp (EA 0.67i; RemoveCard and DestroyCard are unchanged on 0.68b):
        // vanilla GameManager.RemoveCard takes a card out of AllCards before its first yield when there is
        // no user and no drop (GameManager.cs line 10558), but out of its container only inside
        // InGameCardBase.DestroyCard, through CurrentContainer.RemoveCardFromInventory (InGameCardBase.cs
        // lines 10151-10160), and only after DestroyCard has waited for the card's passive-effect and
        // stat-modifier cancels (lines 10127-10150), which can take frames. So the slots are emptied HERE,
        // in this frame and whatever CurrentContainer says, and CardUtil.TryRemoveCard then starts vanilla
        // RemoveCard with no drops and no user, which clears AllCards and the game's other card lists and
        // destroys the card. When DestroyCard later calls RemoveCardFromInventory it finds no slot holding
        // the card and runs only the station's own bookkeeping (lines 5169-5231: passive effects, weight,
        // the open inventory popup). An emptied slot is dropped by vanilla's own rule (line 5147: not a
        // legacy inventory, and never the station's last slot), which also keeps that later call from
        // returning early on an empty slot list (line 5135).
        private static void EjectCardsFromStructure(object structure, IEnumerable<object> cards)
        {
            var consumed = new List<object>();
            var cardSet  = new HashSet<object>();
            if (cards != null)
                foreach (var c in cards)
                    if (c != null && cardSet.Add(c)) consumed.Add(c);
            if (consumed.Count == 0) return;

            int takenFromSlots = 0;
            try
            {
                var slots = CardUtil.GetInventoryList(structure);
                if (slots == null)
                {
                    // Vanilla RemoveCard still takes each card out of the station, through its CurrentContainer.
                    WarnOnce("Eject-station-inventory-unreadable",
                        $"EjectCardsFromStructure could not read the inventory of '{CardUtil.GetCardUniqueId(structure)}' (CardUtil.GetInventoryList returned null: the game may have renamed CardsInInventory); its consumed cards are removed from the game anyway, and vanilla RemoveCard takes them out of the station a frame or more later");
                }
                else
                {
                    bool legacyInventory = IsLegacyInventory(structure);
                    var emptiedSlots = new List<object>();
                    foreach (var slot in slots)
                    {
                        if (slot == null) continue;
                        var inner = CardUtil.GetInventoryList(slot);
                        if (inner != null)
                        {
                            int before = inner.Count;
                            for (int i = inner.Count - 1; i >= 0; i--)
                            {
                                if (inner[i] != null && cardSet.Contains(inner[i]))
                                {
                                    inner.RemoveAt(i);
                                    takenFromSlots++;
                                }
                            }
                            if (before > 0 && inner.Count == 0) emptiedSlots.Add(slot);
                        }
                        else if (cardSet.Contains(slot))
                        {
                            // The EA 0.62b shape: the slot list held the cards themselves.
                            emptiedSlots.Add(slot);
                            takenFromSlots++;
                        }
                    }
                    foreach (var slot in emptiedSlots)
                    {
                        if (cardSet.Contains(slot) || (!legacyInventory && slots.Count > 1))
                            slots.Remove(slot);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] EjectCardsFromStructure error: {ex.InnerException?.ToString() ?? ex.ToString()}");
            }

            if (takenFromSlots < consumed.Count)
                WarnOnce("Eject-card-not-in-station",
                    $"EjectCardsFromStructure found {takenFromSlots} of {consumed.Count} consumed card(s) in the slots of '{CardUtil.GetCardUniqueId(structure)}'; the rest are removed from the game through vanilla RemoveCard all the same");

            // After the slot pass, never inside it: RemoveCard runs up to its first yield inside TryRemoveCard.
            foreach (var card in consumed)
            {
                if (CardUtil.TryRemoveCard(card)) continue;
                WarnOnce("Eject-TryRemoveCard-false",
                    $"CardUtil.TryRemoveCard refused a card consumed by a station action ('{CardUtil.GetCardUniqueId(card)}'); it is hidden instead, as before 1.12.0, and stays in GameManager.AllCards until the game is reloaded. The framework's own warning names the cause");
                TrySetActive(card, false);
            }
        }

        // InGameCardBase.IsLegacyInventory (true for a fixed-slot inventory or a saved cooking recipe), which
        // decides whether vanilla ever drops an emptied slot. Unreadable reads as a normal inventory, the rule
        // every WDI station has (none sets LegacyInventory), with one warn-once line.
        private static bool IsLegacyInventory(object structure)
        {
            if (CardUtil.GetMemberValue(structure, "IsLegacyInventory") is bool legacy) return legacy;
            WarnOnce("InGameCardBase.IsLegacyInventory",
                $"{structure?.GetType().Name}.IsLegacyInventory not found (reflection miss): an emptied station slot is dropped as for a normal inventory, never the station's last slot; the game may have renamed it");
            return false;
        }

        // ============================================================
        //  SAWMILL - C# Saw All handler (plan feature 8)
        //  Every vanilla Tree Log held in the Sawmill's inventory becomes what one hand Cut makes of a
        //  dragged log: 8 Planks and 2 Wood Shavings, with each Plank carrying THAT log's wood ID and
        //  Dryness. The Cut gets the stats from its Plank drop's TransferRules (FromGivenCard); a
        //  batch has no dragged card, so this reads them off each log itself, before the log is
        //  ejected, and spawns the Planks through SpawnService.Spawn(uid, stats) (framework 2.28.0, the
        //  vanilla drop path). The saw blade and any other card in the inventory are left alone. Like
        //  the other batch buttons it is XP-free (the Cut keeps its own Woodworking +0.5).
        // ============================================================
        private const string PlankGUID        = "57460207bbf77fa4fb6720aed5d84851"; // vanilla Plank
        private const string WoodShavingsGUID = "da61c004e1fc3a44b909a56ddded9189"; // vanilla Wood Shavings
        // The Cut's yield per log (WaterDrivenSawmill_Placed.json, CI "Cut": Plank x8, WoodShavings x2). The
        // WDI Pester contract compares these two numbers with that JSON, so a retuned Cut cannot leave
        // Saw All behind.
        private const int    SawAllPlanksPerLog   = 8;
        private const int    SawAllShavingsPerLog = 2;
        // A log's wood ID and Dryness live in the same two stats on the Plank. Both names are in
        // SpawnService.StatNames; one the framework does not know is ignored, so the contract checks it.
        private const string SawAllWoodIdStat  = "SpecialDurability4";
        // Blade Wear: WaterDrivenSawmill_Placed*.json UsageDurability (MaxValue 60), +1 per Cut in JSON.
        private const string SawBladeWearStat = "UsageDurability";
        private const float  SawBladeLife     = 60f;
        private const string SawAllDrynessStat = "Progress";

        private static void HandleSawAllInner(object sawmill)
        {
            var inventory = CardUtil.GetInventoryList(sawmill);
            if (inventory == null || inventory.Count == 0)
            {
                Logger?.LogDebug("[ActionIntercept] SawAll: empty sawmill inventory");
                return;
            }

            // Only Tree Logs: the saw blade (and anything else the filter admits) stays where it is.
            var logs = new List<object>();
            foreach (var slotItem in inventory)
            {
                if (slotItem == null) continue;
                var innerCards = CardUtil.GetInventoryList(slotItem);
                if (innerCards != null && innerCards.Count > 0)
                {
                    foreach (var inner in innerCards)
                    {
                        if (inner != null && CardUtil.GetCardUniqueId(inner) == LogGUID) logs.Add(inner);
                    }
                }
                else if (CardUtil.GetCardUniqueId(slotItem) == LogGUID)
                {
                    logs.Add(slotItem);
                }
            }

            if (logs.Count == 0)
            {
                Logger?.LogDebug("[ActionIntercept] SawAll: no logs in the sawmill inventory");
                return;
            }

            // WDI-B4 (balance pass 2026-10-14): the blade wears one point per log (Blade Wear, the sawmill's
            // UsageDurability, counting up to SawBladeLife), the same as one Cut. A batch saws only the logs the
            // blade has left; the rest stay in the sawmill until a Copper Saw Blade is fitted.
            float bladeWear = CardUtil.GetDurability(sawmill, SawBladeWearStat);
            if (float.IsNaN(bladeWear))
            {
                WarnOnce("SawAll-blade-unreadable",
                    $"Saw All could not read the sawmill's {SawBladeWearStat} (Blade Wear; reflection miss or a renamed stat), so this batch is sawn without wearing the blade");
            }
            else
            {
                int cutsLeft = (int)Math.Floor(SawBladeLife - bladeWear + 0.001f);
                if (cutsLeft <= 0)
                {
                    Logger?.LogDebug("[ActionIntercept] SawAll: the saw blade is worn out");
                    return;
                }
                if (logs.Count > cutsLeft) logs = logs.GetRange(0, cutsLeft);
            }

            // Read every log's wood ID and Dryness BEFORE the first eject, so nothing here depends on what
            // an ejected card still reports.
            var plankStats = new List<Dictionary<string, float>>(logs.Count);
            foreach (var log in logs) plankStats.Add(BuildSawAllPlankStats(log));

            for (int i = 0; i < logs.Count; i++)
            {
                for (int p = 0; p < SawAllPlanksPerLog; p++)
                    SpawnService.Spawn(PlankGUID, plankStats[i]);
                for (int s = 0; s < SawAllShavingsPerLog; s++)
                    SpawnService.Spawn(WoodShavingsGUID);
            }

            EjectCardsFromStructure(sawmill, logs);
            if (!float.IsNaN(bladeWear) && !CardUtil.SetDurability(sawmill, SawBladeWearStat, Math.Min(SawBladeLife, bladeWear + logs.Count)))
                WarnOnce("SawAll-blade-write", $"Saw All could not raise the sawmill's {SawBladeWearStat} (Blade Wear; reflection miss), so this batch did not wear the blade");
            RefreshOpenInventoryPopup();

            Logger?.LogDebug($"[ActionIntercept] SawAll: sawed {logs.Count} log(s) into {logs.Count * SawAllPlanksPerLog} plank(s) and {logs.Count * SawAllShavingsPerLog} wood shaving(s)");
        }

        // One log's Plank stats: its wood ID and its Dryness, each read off the log. A value that cannot
        // be read (NaN: a reflection miss or a renamed stat) is left OUT, never written as 0, so that
        // plank keeps its default for that one stat (wood ID 0, or Dryness 0) instead of a made-up
        // reading, and one warn-once line per cause says why. With nothing readable the Planks spawn at
        // their defaults, as a Cut did before feature 2.
        private static Dictionary<string, float> BuildSawAllPlankStats(object log)
        {
            var stats = new Dictionary<string, float>(StringComparer.Ordinal);
            float woodId = CardUtil.GetDurability(log, SawAllWoodIdStat);
            if (!float.IsNaN(woodId))
                stats[SawAllWoodIdStat] = woodId;
            else
                WarnOnce("SawAll-wood-id-unreadable",
                    $"Saw All could not read {SawAllWoodIdStat} (wood ID) from a log, so that log's planks spawn at wood ID 0; CardUtil.GetDurability returned NaN or the game renamed the stat");
            float dryness = CardUtil.GetDurability(log, SawAllDrynessStat);
            if (!float.IsNaN(dryness))
                stats[SawAllDrynessStat] = dryness;
            else
                WarnOnce("SawAll-dryness-unreadable",
                    $"Saw All could not read {SawAllDrynessStat} (Dryness) from a log, so that log's planks spawn with Dryness 0; CardUtil.GetDurability returned NaN or the game renamed the stat");
            return stats.Count > 0 ? stats : null;
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
                    if (!IsSluiceInputUid(uid))
                        continue;
                    totalRolls += innerCards.Count;
                    slotsToProcess.Add((item, innerCards, uid));
                }
                else
                {
                    // Direct card — check UID on the item itself
                    string uid = CardUtil.GetCardUniqueId(item);
                    Logger?.Log(LogLevel.Debug, $"[ActionIntercept] Sluice: item uid='{uid}', type={item.GetType().Name}");
                    if (!IsSluiceInputUid(uid))
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
        // WDI 1.12.3 cut the metal and greenstone rolls to about a quarter
        // (Chiwei's Nexus report 2026-10-05, owner decision 2026-10-12): at 8% copper and 10% greenstone a mud pile
        // out-produced the vanilla Copper Mine about 8x over a 30-day run. Tin and iron stay below copper.
        private static readonly float[] _ironChance   = { 0.01f, 0.005f, 0.0025f };
        private static readonly float[] _copperChance = { 0.02f, 0.01f, 0.005f };
        private static readonly float[] _tinChance    = { 0.01f, 0.005f, 0.0025f };
        private static readonly float[] _gsChance     = { 0.03f, 0.02f, 0.01f };
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

            // Washed tailings (N4). A wash that separated nothing still leaves the coarse
            // reject behind, so a soil pile never comes out of the sluice as literally nothing
            // (~29% of mud piles and ~34% of fine dirt previously did). Routed to vanilla Stone
            // rather than a minted tailings item: EA 0.67i ships no Gravel/Sand/Pebble card at
            // all, and Stone is the only real aggregate here with existing uses (hand stone,
            // whetstone, stone tools, construction). TUNABLE: currently a floor, not a per-wash
            // byproduct, so it cannot inflate the stone economy on rolls that already paid out.
            if (drops.Count == 0) drops.Add(new SluiceRoll { Guid = StoneGUID });
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
                foreach (var card in EnumerateGameManagerAllCards())
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
                changed |= CardUtil.SetDurability(card, "SpecialDurability2", NuggetSluiceQuality);
                changed |= CardUtil.SetDurability(card, "SpecialDurability3", NuggetSluiceQuality);
                changed |= CardUtil.SetDurability(card, "FuelCapacity", 0f);
                if (changed) RefreshCardDurabilityVisuals(card);
                Logger?.LogDebug($"[ActionIntercept] NuggetInit: type={nuggetType} quality={NuggetSluiceQuality}");
                return changed;
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyNuggetStatsToCard: {ex.Message}"); return false; }
        }

        // ——— Blast copper nugget stat init (quality carried from the smelted items) ———
        // The fallback for Blast nuggets: the LOWEST smelted item's quality percentage. Blast spawns each
        // nugget typed and at its own item's quality (BuildBlastNuggetStats), so this pass normally finds
        // nothing left to raise; it only sets quality on a spawn that fell back to GiveCard, and never above
        // what the poorest item gives. Found by pre/post ID-diff because GiveCard returns void.
        // It never touches SpecialDurability4: the Metal Type is set at spawn.
        private static void StartDelayedBlastNuggetInitialization(HashSet<int> preIds, int count, float qualityPct)
        {
            if (count <= 0) return;
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                if (gm is UnityEngine.MonoBehaviour mb)
                {
                    mb.StartCoroutine(ApplyBlastNuggetStatsAfterSpawn(preIds, count, qualityPct));
                    return;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] StartDelayedBlastNuggetInitialization: {ex.Message}"); }
            ApplyBlastNuggetSpawnStats(preIds, count, qualityPct);
        }

        private static IEnumerator ApplyBlastNuggetStatsAfterSpawn(HashSet<int> preIds, int count, float qualityPct)
        {
            for (int attempt = 0; attempt < NuggetInitRetryFrames; attempt++)
            {
                yield return null;
                if (ApplyBlastNuggetSpawnStats(preIds, count, qualityPct) >= count) yield break;
            }
        }

        private static int ApplyBlastNuggetSpawnStats(HashSet<int> preIds, int count, float qualityPct)
        {
            int updated = 0;
            try
            {
                foreach (var card in EnumerateGameManagerAllCards())
                {
                    if (updated >= count) break;
                    if (CardUtil.GetCardUniqueId(card) != CopperNuggetGUID) continue;
                    if (card is UnityEngine.Object uo)
                    {
                        if (preIds.Contains(uo.GetInstanceID())) continue;
                        preIds.Add(uo.GetInstanceID()); // latch so retry frames don't re-process
                    }
                    if (ApplyBlastNuggetQuality(card, qualityPct)) updated++;
                }
            }
            catch (Exception ex) { Logger?.LogError($"[ActionIntercept] ApplyBlastNuggetSpawnStats: {ex.Message}"); }
            return updated;
        }

        // Raises a melted nugget's Metal Quality and Quality to its source's percentage, with no floor: an item
        // with no quality stat (0) leaves the nugget at quality 0, as the base game's furnace does.
        private static bool ApplyBlastNuggetQuality(object card, float sourceQualityPct)
        {
            if (!(sourceQualityPct > 0f)) return false;
            bool changed = false;
            foreach (var stat in NuggetQualityStats)
            {
                float max = CardUtil.GetDurabilityMax(card, stat);
                if (float.IsNaN(max) || max <= 0f) max = 100f;
                changed |= SetMinimumDurabilityStatValue(card, stat, sourceQualityPct * max);
            }
            if (changed) RefreshCardDurabilityVisuals(card);
            return changed;
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
                var oldModel = GetCardData(card);
                if (!CardUtil.TrySetCardModel(card, targetData))
                {
                    Logger?.LogError($"[ActionIntercept] Transform: CardModel not settable on {card.GetType().Name}");
                    return false;
                }
                CardUtil.ReinitCard(card, targetData);
                MoveStatTriggerListeners(card, oldModel, targetData);
                return true;
            }
            catch (Exception ex)
            {
                Logger?.LogError($"[ActionIntercept] Transform failed: {ex.Message}");
                return false;
            }
        }

        // The game runs a card's OnStatsChangeActions only while the card is registered as a listener on each of
        // their trigger stats (InGameStat.RegisterListener), and only AddCard registers it (RemoveCard unregisters).
        // An in-place swap goes through neither, so the new model's stat-triggered rules never ran. A Heated Iron
        // Bloom finished by Hammer All became an Iron Nugget with 0 Strikes and Quality 0 that could not be smithed
        // (Nexus report 2026-09-10; measured on a harness rig 2026-10-12), where the base game's nugget gives an iron
        // nugget 4 to 6 Strikes and copies Metal Quality into Quality on its next tick (MetalNugget's rules, which
        // trigger on the Counter stat, +1 a tick). This moves the registration from the old model's trigger stats
        // to the new model's, as RemoveCard followed by AddCard would. The card's per-action status array
        // (StatTriggeredActions) is rebuilt for the new model FIRST, as InGameCardBase.Init does: the swap leaves it
        // sized for the old model, and CheckForStatTriggeredActions indexes it by the new model's action list, so
        // registering without the rebuild threw IndexOutOfRangeException on every tick and wedged the game (found
        // by the first harness run of this fix, 2026-10-12). No rebuild, no registration.
        private static void MoveStatTriggerListeners(object card, object oldModel, object newModel)
        {
            try
            {
                var gm = CardUtil.GetGameManagerInstance();
                var statsDict = gm == null ? null : ReadField(gm, "StatsDict") as IDictionary;
                if (statsDict == null)
                {
                    WarnOnce("StatTriggerListeners-StatsDict-null",
                        "GameManager.StatsDict could not be read, so a card Hammer All transforms in place does not get its stat-triggered rules (an Iron Nugget from a bloom keeps 0 Strikes and Quality 0 until the save is reloaded)");
                    return;
                }
                if (oldModel != null) ForEachTriggerStat(oldModel, statsDict, stat => InvokeStatListener(stat, "RemoveListener", card));
                if (!RebuildStatTriggerStatuses(card, newModel)) return;
                ForEachTriggerStat(newModel, statsDict, stat => InvokeStatListener(stat, "RegisterListener", card));
            }
            catch (Exception ex)
            {
                WarnOnce("StatTriggerListeners-threw", $"moving a transformed card's stat-trigger listeners threw: {ex.Message}");
            }
        }

        // InGameCardBase.Init's own loop: one StatTriggeredActionStatus(trigger count, action name) per OnStatsChangeAction.
        private static bool RebuildStatTriggerStatuses(object card, object newModel)
        {
            // Looked up on the DECLARING type: a base class's private setter is invisible through a PropertyInfo
            // taken from the derived card type (GetSetMethod(true) then returns null).
            var prop = card.GetType().GetProperty("StatTriggeredActions", Flags);
            if (prop != null && prop.DeclaringType != null) prop = prop.DeclaringType.GetProperty("StatTriggeredActions", Flags);
            var setter = prop?.GetSetMethod(true);
            if (setter == null || !prop.PropertyType.IsArray)
            {
                WarnOnce("StatTriggerListeners-StatTriggeredActions-missing",
                    "InGameCardBase.StatTriggeredActions (a settable array) was not found, so a card Hammer All transforms in place is not given its stat-triggered rules (an Iron Nugget from a bloom keeps 0 Strikes and Quality 0 until the save is reloaded); the game may have renamed it");
                return false;
            }
            var statusType = prop.PropertyType.GetElementType();
            var actions = ReadField(newModel, "OnStatsChangeActions") as IList;
            var statuses = Array.CreateInstance(statusType, actions?.Count ?? 0);
            for (int i = 0; i < statuses.Length; i++)
            {
                int triggerCount = (ReadField(actions[i], "StatChangeTrigger") as IList)?.Count ?? 0;
                string actionId = ReadField(ReadField(actions[i], "ActionName"), "DefaultText") as string;
                statuses.SetValue(Activator.CreateInstance(statusType, triggerCount, actionId), i);
            }
            setter.Invoke(card, new object[] { statuses });
            return true;
        }

        private static void ForEachTriggerStat(object model, IDictionary statsDict, Action<object> apply)
        {
            if (!(ReadField(model, "OnStatsChangeActions") is IList actions)) return;
            foreach (var action in actions)
            {
                if (!(ReadField(action, "StatChangeTrigger") is IList triggers)) continue;
                foreach (var trigger in triggers)
                {
                    var stat = ReadField(trigger, "Stat");
                    if (stat == null || !statsDict.Contains(stat)) continue;
                    var inGameStat = statsDict[stat];
                    if (inGameStat != null) apply(inGameStat);
                }
            }
        }

        private static readonly Dictionary<(Type, string), MethodInfo> _statListenerMethods = new Dictionary<(Type, string), MethodInfo>();

        // InGameStat has a card overload and a SelfTriggeredAction overload of each; this picks the one that takes the card.
        private static void InvokeStatListener(object inGameStat, string methodName, object card)
        {
            var type = inGameStat.GetType();
            if (!_statListenerMethods.TryGetValue((type, methodName), out var method))
            {
                method = type.GetMethods(Flags).FirstOrDefault(m => m.Name == methodName
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(card));
                _statListenerMethods[(type, methodName)] = method;
                if (method == null)
                    WarnOnce($"InGameStat.{methodName}-missing",
                        $"{type.Name}.{methodName}(card) was not found, so a card Hammer All transforms in place does not get its stat-triggered rules; the game may have renamed it");
            }
            method?.Invoke(inGameStat, new[] { card });
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
            if (string.Equals(lk, "Water_Sawmill_WorkshopPlaced_ForgeIronRivets_ActionName", StringComparison.Ordinal))
                return WorkshopCraftKind.IronRivets;
            if (string.Equals(lk, "Water_Sawmill_WorkshopPlaced_HammerIronSheet_ActionName", StringComparison.Ordinal))
                return WorkshopCraftKind.IronSheet;

            string dt = CardUtil.GetActionName(action);
            if (string.Equals(dt, "Hammer Copper Sheet", StringComparison.OrdinalIgnoreCase))
                return WorkshopCraftKind.CopperSheet;
            if (string.Equals(dt, "Forge Copper Nails", StringComparison.OrdinalIgnoreCase))
                return WorkshopCraftKind.CopperNails;
            if (string.Equals(dt, "Cast Metal Lump", StringComparison.OrdinalIgnoreCase))
                return WorkshopCraftKind.CopperLump;
            if (string.Equals(dt, "Forge Iron Nails", StringComparison.OrdinalIgnoreCase))
                return WorkshopCraftKind.IronRivets;
            if (string.Equals(dt, "Hammer Iron Sheet", StringComparison.OrdinalIgnoreCase))
                return WorkshopCraftKind.IronSheet;

            return WorkshopCraftKind.None;
        }

        // ---- Expand (plan feature 18) ------------------------------------------------------------------
        // Tunable placeholders (Open question 15), not balanced.
        // vanilla Plank; written in two halves so the Sawmill Saw All contract, which plants on the whole GUID literal, still sees one anchor
        private const string ExpandPlankGUID = "57460207" + "bbf77fa4fb6720aed5d84851";
        // The nail half of the cost is 4 Iron Nails: the shared Metal Nail (ActCompat.CopperNailsUid) at Metal Type 200
        // (ActCompat.IronMetalType), the iron tier the retired Cast Iron Rivet stood for (WDI 1.12.3). A copper or
        // bronze Metal Nail does not count.
        private const int ExpandPlankCount = 6;
        private const int ExpandNailCount = 4;

        private static bool IsExpandIronNail(object card, string uid)
        {
            if (!string.Equals(uid, ActCompat.CopperNailsUid, StringComparison.Ordinal)) return false;
            float metalType = CardUtil.GetDurability(card, MetalTypeStat);
            if (float.IsNaN(metalType))
            {
                WarnOnce("Expand-nail-metal-type-unreadable",
                    $"Expand could not read {MetalTypeStat} (Metal Type) from a Metal Nail, so that nail does not count toward the 4 Iron Nails; CardUtil.GetDurability returned NaN or the game renamed the stat");
                return false;
            }
            return Math.Abs(metalType - ActCompat.IronMetalType) < 0.5f;
        }

        private static string ExpandedUidFor(string baseUid)
        {
            if (string.Equals(baseUid, SawmillID, StringComparison.Ordinal)) return SawmillExpandedID;
            if (string.Equals(baseUid, WorkshopID, StringComparison.Ordinal)) return WorkshopExpandedID;
            return null;
        }

        // Finds the cost in the player's environment (the board, the player's inventory and any container at the
        // location), never inside the station itself. Reads GameQuery.CardsInPlayerEnv, so a card carried from
        // another environment that vanilla has not re-homed yet is not counted.
        private static bool TryFindExpandCost(object station, out List<object> planks, out List<object> nails)
        {
            planks = new List<object>();
            nails = new List<object>();
            if (station == null) return false;
            var inside = new HashSet<object>(Inventory.Cards(station));
            foreach (var card in GameQuery.CardsInPlayerEnv())
            {
                if (card == null || ReferenceEquals(card, station) || inside.Contains(card)) continue;
                var uid = CardUtil.GetCardUniqueId(card);
                if (string.Equals(uid, ExpandPlankGUID, StringComparison.Ordinal))
                {
                    if (planks.Count < ExpandPlankCount) planks.Add(card);
                }
                else if (IsExpandIronNail(card, uid))
                {
                    if (nails.Count < ExpandNailCount) nails.Add(card);
                }
            }
            return planks.Count >= ExpandPlankCount && nails.Count >= ExpandNailCount;
        }

        private static void HandleExpand(object station)
        {
            var baseUid = CardUtil.GetCardUniqueId(station);
            var targetUid = ExpandedUidFor(baseUid);
            if (targetUid == null) return;
            if (!TryFindExpandCost(station, out var planks, out var nails))
            {
                // The gate checked a moment ago; losing the cost during the action's own time is a race, not a normal path.
                Logger?.LogWarning($"[ActionIntercept] Expand on {baseUid}: the cost left reach while the action ran; the station was not expanded");
                return;
            }
            // Swap first, consume second: a failed swap then costs the player nothing.
            if (!CardUtil.TransformCardInPlace(station, targetUid))
            {
                Logger?.LogWarning($"[ActionIntercept] Expand on {baseUid}: CardUtil.TransformCardInPlace to {targetUid} failed (target card not loaded or the model swap was refused); nothing consumed");
                return;
            }
            CardFinder.Invalidate();
            foreach (var card in planks.Concat(nails))
            {
                bool removed = CardUtil.TryRemoveCard(card);
                if (removed) continue;
                WarnOnce("Expand-TryRemoveCard-false",
                    $"CardUtil.TryRemoveCard refused an Expand cost card ('{CardUtil.GetCardUniqueId(card)}'); the card stays where it was, so that part of the cost was not taken. The framework's own warning names the cause");
            }
            RefreshOpenInventoryPopup();
            Logger?.LogDebug($"[ActionIntercept] Expand: {baseUid} -> {targetUid}, consumed {planks.Count} Planks and {nails.Count} Iron Nails");
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

        // Consumes `count` copper nuggets, or none and false when the container holds fewer. With
        // `readMetalQuality`, `metalQualities` receives the Metal Quality (SpecialDurability2) of every
        // consumed nugget whose value could be read (CardUtil.GetDurability gives NaN on a miss, and a
        // NaN is left out, never entered as 0). The read happens BEFORE the eject, which takes the
        // cards out of their slots and out of the game; otherwise `metalQualities` is an empty list.
        private static bool TryConsumeCopperNuggets(object container, int count, bool readMetalQuality, out List<float> metalQualities)
        {
            metalQualities = new List<float>();
            var all = FindInventoryCards(container, CopperNuggetGUID, int.MaxValue);
            var copper = new List<object>();
            foreach (var c in all) { if (IsCopperNugget(c)) { copper.Add(c); if (copper.Count >= count) break; } }
            if (copper.Count < count) return false;
            if (readMetalQuality)
            {
                foreach (var nugget in copper)
                {
                    float quality = CardUtil.GetDurability(nugget, MetalQualityStat);
                    if (!float.IsNaN(quality)) metalQualities.Add(quality);
                }
            }
            EjectCardsFromStructure(container, copper);
            return true;
        }

        // An iron-grade nugget is a Metal Nugget whose Metal Type (SpecialDurability4) reads iron (200). An
        // unreadable type (NaN) is not iron, and copper (100, or no type at all), tin and the alloys are not
        // either; IsCopperNugget rejects iron in turn, so neither craft family takes the other's nuggets.
        private static bool IsIronNugget(object card)
        {
            float metalType = CardUtil.GetDurability(card, "SpecialDurability4");
            return !float.IsNaN(metalType) && Math.Abs(metalType - NuggetIronType) < 0.5f;
        }

        private static int CountIronNuggets(object container)
        {
            var all = FindInventoryCards(container, CopperNuggetGUID, int.MaxValue);
            int n = 0;
            foreach (var c in all) if (IsIronNugget(c)) n++;
            return n;
        }

        // The iron twin of TryConsumeCopperNuggets: consumes `count` iron-grade nuggets, or none and false
        // when the container holds fewer, and hands back the Metal Quality (SpecialDurability2) of every
        // consumed nugget whose value could be read (a NaN is left out, never entered as 0). The read comes
        // BEFORE the eject, which takes the cards out of their slots and out of the game. Neither iron output carries a quality
        // (see HandleWorkshopCraft), so the list is for the log.
        private static bool TryConsumeIronNuggets(object container, int count, out List<float> metalQualities)
        {
            metalQualities = new List<float>();
            var all = FindInventoryCards(container, CopperNuggetGUID, int.MaxValue);
            var iron = new List<object>();
            foreach (var c in all) { if (IsIronNugget(c)) { iron.Add(c); if (iron.Count >= count) break; } }
            if (iron.Count < count) return false;
            foreach (var nugget in iron)
            {
                float readQuality = CardUtil.GetDurability(nugget, MetalQualityStat);
                if (!float.IsNaN(readQuality)) metalQualities.Add(readQuality);
            }
            EjectCardsFromStructure(container, iron);
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

        // RefreshOpenInventoryPopup runs once per Hammer All, per Saw All and per Workshop craft, so everything it
        // looks up by reflection is resolved ONCE and kept here (audit F4/W4). Instance is declared
        // on MBSingleton<GraphicsManager>, a base class, so it needs FlattenHierarchy: without it
        // GetProperty returns null and every call fell through to a scene-wide FindObjectOfType.
        private static bool _graphicsReflected;
        private static Type _graphicsManagerType;
        private static PropertyInfo _graphicsInstanceProp;
        private static FieldInfo _currentInspectionPopupField;
        private static readonly Dictionary<Type, MethodInfo> _refreshInventoryMethods = new Dictionary<Type, MethodInfo>();

        private static void RefreshOpenInventoryPopup()
        {
            try
            {
                if (!_graphicsReflected)
                {
                    _graphicsReflected = true;
                    _graphicsManagerType = AccessTools.TypeByName("GraphicsManager");
                    if (_graphicsManagerType != null)
                    {
                        _graphicsInstanceProp = _graphicsManagerType.GetProperty("Instance",
                            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                        // Native GetField, not AccessTools.Field: a miss is reported once below, not on every click.
                        _currentInspectionPopupField = _graphicsManagerType.GetField("CurrentInspectionPopup", Flags);
                    }
                }
                if (_graphicsManagerType == null)
                {
                    WarnOnce("GraphicsManager-type",
                        "GraphicsManager type not found (reflection miss): an open inventory window will not redraw after Hammer All, Saw All or a Workshop craft; the game may have renamed it");
                    return;
                }
                if (_currentInspectionPopupField == null)
                {
                    WarnOnce("GraphicsManager.CurrentInspectionPopup",
                        "GraphicsManager.CurrentInspectionPopup not found (reflection miss): an open inventory window will not redraw after Hammer All, Saw All or a Workshop craft; the game may have renamed it");
                    return;
                }

                var instance = _graphicsInstanceProp?.GetValue(null, null)
                    ?? UnityEngine.Object.FindObjectOfType(_graphicsManagerType);
                if (instance == null) return; // no graphics manager in this scene: the normal negative
                var popup = _currentInspectionPopupField.GetValue(instance);
                if (popup == null) return;    // no inspection window open: the normal negative

                var popupType = popup.GetType();
                if (!_refreshInventoryMethods.TryGetValue(popupType, out var refresh))
                {
                    refresh = popupType.GetMethod("RefreshInventory", Flags);
                    _refreshInventoryMethods[popupType] = refresh;
                }
                if (refresh == null)
                {
                    WarnOnce("InspectionPopup.RefreshInventory",
                        $"{popupType.Name}.RefreshInventory not found (reflection miss): an open inventory window will not redraw after Hammer All, Saw All or a Workshop craft; the game may have renamed it");
                    return;
                }
                refresh.Invoke(popup, null);
            }
            catch (Exception ex) { Logger?.LogDebug($"[ActionIntercept] RefreshOpenInventoryPopup: {ex.InnerException?.ToString() ?? ex.ToString()}"); }
        }

        // Resolved charges member per card type: a readable PropertyInfo, a FieldInfo, or null when the
        // type declares none (cached too, so a miss is probed once per type, not once per pile).
        private static readonly Dictionary<Type, MemberInfo> _chargesMembers = new Dictionary<Type, MemberInfo>();

        private static int GetCardCharges(object card)
        {
            try
            {
                var cardType = card.GetType();
                if (!_chargesMembers.TryGetValue(cardType, out var member))
                {
                    var prop = cardType.GetProperty("CurrentCharges", Flags)
                            ?? cardType.GetProperty("Charges", Flags);
                    if (prop != null && prop.CanRead)
                        member = prop;
                    else
                        member = LookupField(cardType, "CurrentCharges")
                              ?? LookupField(cardType, "currentCharges");
                    _chargesMembers[cardType] = member;
                }

                if (member is PropertyInfo chargesProp)
                    return Convert.ToInt32(chargesProp.GetValue(card));
                if (member is FieldInfo chargesField)
                    return Convert.ToInt32(chargesField.GetValue(card));

                // Non-throwing exit: neither probe found a member, so the count silently defaults
                // to 1 (EA 0.68b declares none of CurrentCharges/Charges/currentCharges on
                // InGameCardBase; only the legacy direct-card Sluice path ever gets here).
                WarnOnce("InGameCardBase.CurrentCharges",
                    $"{cardType.Name} has no CurrentCharges/Charges member (reflection miss): a direct-card Sluice All batch counts one roll per pile");
            }
            catch (Exception ex)
            {
                // Hot path (Sluice per-item quantity resolution) — defaulting to 1 on a
                // swallowed reflection failure silently understates output count. Breadcrumb
                // so a field/property rename shows up as a log line, not a quiet wrong count.
                Logger?.Log(LogLevel.Debug, $"[ActionIntercept] GetCardCharges({card?.GetType().Name}) failed: {ex.Message}");
            }
            return 1;
        }

    }
}
