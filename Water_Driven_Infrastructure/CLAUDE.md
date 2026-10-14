# Water-Driven Infrastructure — Module Notes

These notes cover WDI-specific subsystems. Also read the root `CLAUDE.md` for all general rules.

## CardInteraction Action Handlers (ActionRouter)
Actual EA 0.65 signature (`.decomp/GameManager.cs`): `ActionRoutine(CardAction _Action, InGameCardBase _ReceivingCard, InGameNPCOrPlayer _User, bool _FastMode, bool _DontPlaySounds, bool _ModifiersAlreadyCollected = false, InGameCardBase _GivenCard = null)`.
- `_GivenCard` (the dragged card) is exposed as `ActionContext.GivenCard` by the framework's `CSFFModFramework.Api.ActionRouter` — WDI no longer patches `ActionRoutine`/`PerformStackActionRoutine`/`PerformActionAsEnumerator` directly; all action interception is registered via `ActionRouter.Register(new ActionHandler {...})` in `Patcher/ActionInterceptPatch.cs` (`RegisterActionHandlers`). Read `CSFFModFramework/Api/ActionRouter.cs` for exact semantics (Cancel/Before/AfterWrapped timing, per-handler frame dedup).
- The dragged card is **NOT** in the receiving card's inventory when the action fires. To consume/modify it, use `ActionContext.GivenCard`, not inventory search.
- Reference: WDI sawmill "Cut" is pure JSON (GivenCardChanges.ModType=3); the "Hammer All"/"Blast"/"Sluice All"/workshop-craft/fish-catch/iron-smelt handlers in `RegisterActionHandlers` show the ActionRouter Cancel/AfterWrapped patterns.

## Smelting (cross-reference)
See `AdvancedCopperTools/CLAUDE.md` for full smelting container tag rules. Critical: include both `tag_SmeltingContainer` AND `tag_SmeltingContainerIron` on any forge structure. Never mix SmeltingRecipeInjector and Progress-based smelting on the same item (WDI gears bug: gave 48 copper instead of 12).

## Hammer All quality (Workshop)
Vanilla smithing items carry **TWO** quality stats — `SpecialDurability2` "Metal Quality" AND `SpecialDurability3` "Quality" (the player-visible line), plus `SpecialDurability1` "Strikes". `ApplyWorkshopQualityBoost`/`IsMetalQualityTool` must boost/match BOTH via `QualityDurabilitySlots` — boosting SD2 only leaves "Quality" frozen (looks like a no-op; fixed 2026-08-23). Quality gain requires **Strikes > 0**: the "quality-only" fallback bucket EXCLUDES any Smith/`tag_Hammer`-hammerable item (`IsSmithHammerable`), so an item at 0 Strikes can't farm free quality. Full detail: memory `reference_smithing_dual_quality_stats`.

## Machine-made Metal Nuggets: never write SD1
On the vanilla Metal Nugget, `SpecialDurability1` is **Strikes**, not quality: each strike is one hand Smith (or Hammer All) hit that adds Smithing-scaled Metal Quality, and every vanilla nugget source sets it to 0. Quality lives in SD2 "Metal Quality" (the stat that carries through smelting) and SD3 "Quality". Until 1.12.3 the Sluice, Blast, iron-part and copper self-smelt paths wrote their quality number into SD1 too, so a nugget could be hammered to full quality (Nexus 2026-10-11). The 50% self-smelt floor covers only `water_sawmill_*` items; scanning all CardData caught vanilla bars and blanks.

## Partner duties need `RequiresMarkedCard: true`
`NPCDuty.RequiresMarkedCard` is what makes the engine skip a duty while no card is marked with it (`InGameNPC` duty selection, `GameManager.AnyCardMarkedWith`). A `MoveToItem` Move step ignores marking entirely, so a duty without the flag is chosen whenever its station exists anywhere. Every WDI duty JSON carries it, as vanilla's Firekeeping does; a new duty must too.
