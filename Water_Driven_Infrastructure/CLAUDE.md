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
