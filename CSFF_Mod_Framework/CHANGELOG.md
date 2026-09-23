# CSFF Mod Framework — Changelog

All notable changes to CSFFModFramework are documented here.

---

## [2.26.4] - 2026-09-23

### Fixed

- **An action run on a whole stack of cards could do its mod-added part for only the first
  card.** Since game version EA 0.68a runs a stack action's cards one after another without a
  pause, the framework read cards 2, 3 and so on as repeats of the first and skipped the extra
  effect a mod attaches to that action for them. The stack still used up every card. The
  framework now tells the cards of a stack apart, so each one gets its effect, and the same goes
  for a stack of cards dragged onto another card. It was found with the test harness: opening
  three stacked test kits at once produced one kit's contents.
- **GIF animations could never attach to a card.** The GIF service looked up a card's data as a field although the game stores it as a property, so every lookup came back empty, and its durability condition sets read values that do not exist on a card in play. Both now read the real members, and a changed member is reported once in the log instead of failing silently. No released mod ships a GIF, so no player saw a difference.

## [2.26.3] - 2026-09-22

### Changed

- **New artwork for the Portal Kit and the placed Portal.** The kit is now the dormant crystal
  itself rather than a boxed product, and a placed portal is an open vortex, so an unused kit and
  a working portal can be told apart at a glance. Artwork by Chiwei.

## [2.26.2] - 2026-09-19

### Fixed

- **Every action could stop with "I can't do two things at once..." right after an autosave.**
  It happened most often the moment a day passed in a modded village, and only a restart got the
  game moving again. The cause: when a mod removed a card (Community Mod Chest trimming extra
  trees at dawn, the framework trimming doubled terrain on an expansion tile, a Sirus23 sheep
  wandering off or a wolf eating from the ground), the framework's shared removal helper
  destroyed the card but left it on the game's master list of cards. The next autosave tripped
  over that emptied entry, and because the save runs in the middle of the action that ended the
  day, the action never finished and the game stayed "busy" for good. The helper now removes
  cards the way the game itself does, so a removed card leaves every list the game keeps (still
  without dropping any loot of its own). Thanks to Dory22 for the report.
- **A broken card entry can no longer freeze a save.** Just before every save, the framework now
  drops any entry on that master list that points at a card which no longer exists, and logs a
  warning naming it. This also covers the same mistake coming from any other mod, and stops such
  an entry from bringing a removed card back, or duplicating one, when the save is loaded.

## [2.26.1] - 2026-09-18

### Fixed

- **With WikiMod installed, the perk origin tag was missing from the character sheet.** The
  in-run perk row on the Character tab shows icons, and each perk's name is in its hover
  tooltip. WikiMod rewrites that tooltip after the framework had tagged it, so "Swimmer [CMC]"
  read "Swimmer". The framework now tags after other mods, including in WikiMod's tooltip
  layout. Players without WikiMod saw the tag already and see no change.

## [2.26.0] - 2026-09-17

### Added

- **Perk origin tag.** New config `[Perks] ShowModOriginTag`, default `true`: every perk added by
  a mod shows a short tag after its name at character creation and on the character sheet (for
  example "Swimmer [CMC]"), so perks from different mods can be told apart. Vanilla perks are
  never tagged. Display only: the perk's real name, saves and stat reports are unchanged. The tag
  comes from a mod's own `ModInfo.json` (a new optional `"ShortName"` key), or from the initials
  of the mod's name if it has none; every in-house mod's `ModInfo.json` now sets one. Set the
  config to `false` to hide the tags; needs a full quit to desktop and relaunch to take effect.

### Fixed

- **Every mod time window built on `GameQuery.HourOfDay` was running four hours late.** It
  returned hours since the in-game day started, and the in-game day starts at 04:00, not at
  midnight. It now returns the on-screen clock hour, the same one vanilla's own time checks
  use. Consequence for Community Mod Chest: the Professor, the Miller and Weaver, and the
  Apothecary now keep their posted schedules at the times they were meant to, the Shadow Cat and
  Lost Cat prowl at their intended hours, and the framework's own animal activity windows shift
  the same four hours. Nothing about any schedule changed on paper, only when it now actually
  happens.
- **A stat accessor handed a stat's definition instead of its live value used to fail silently.**
  `StatAccess`'s value accessors now log one warning per accessor, naming the mistake, instead of
  quietly returning nothing.

## [2.25.32] - 2026-09-11

### Changed - log verbosity (pre-distribution pass)
- **Three `CardCloneService` per-clone-node lines dropped from Info to Debug**: the
  `AppendExtraDrops: appended N extra drop(s)`, `StripNonLocationDrops: kept N, stripped N`, and
  `stripped N re-spawn action(s)` traces. Each fired once per clone node, so on a load with the
  full mod suite they were 24 of the framework's 68 player-visible Info lines while saying
  nothing a player can act on. `WorldMapInjector`'s existing `prepared N node(s)` line remains
  the Info-level aggregate, and **every failure path in all three methods still logs at Warn**,
  so a real problem (a zero-match strip, an unresolvable extra drop, a failed field walk) is
  unaffected by this change.
- Net effect measured against a real 2-boot `LogOutput.log`: framework Info output with
  `VerboseLogging=false` drops from 68 lines to 44, and no repeating per-item line remains. The
  only multi-line entry left is `WarpResolver: [mod] unresolved: ...`, which is deliberately kept
  at Info because it reports genuinely unresolved references.
- No behavior change. `VerboseLogging=true` still shows every one of these traces, so the
  confirmation path for any open retrospective that relies on them is intact.

## [2.25.31] - 2026-09-11

### Fixed

- **One stale WikiMod patch class was silently killing 23 of WikiMod's 52 patch classes, which is why WikiMod stopped showing card stats on EA 0.67i.** Player report: "wiki mod no longer shows the stats of card". Not a framework bug and not a data bug: `GraphicsManager.AddSlot` changed in 0.67 from `(SlotsTypes, CardData, CardData, int)` to `(SlotsTypes, CardData, CardData, InGameNPCOrPlayer, int)` and became private (`.decomp/GraphicsManager.cs:3302`), while the installed `WikiMod.dll` (3.5.1, targeting the 0.66 line, dated three hours BEFORE the 0.67i `Assembly-CSharp.dll`) still declares `[HarmonyPatch]` against the old 4-arg signature in `WikiMod.GraphicsManagerMod.AddSlotPrefix`. Harmony cannot resolve the target and throws `ArgumentException: Undefined target method` (`Player.log`, every boot).
- **The damage is never one feature, because `Harmony.PatchAll` has no try/catch.** It runs `AccessTools.GetTypesFromAssembly(assembly).Do(type => CreateClassProcessor(type).Patch())`, so the first class that cannot bind aborts the entire enumeration and propagates out of `WikiMod.Plugin.Awake`. Measured on the deployed binary with Mono.Cecil: `GraphicsManagerMod` is type 274 of 582 in metadata order, so **23 of WikiMod's 52 Harmony patch classes never applied** - `InGameCardBaseMod` (the detailed card hover tooltip, i.e. the reported symptom), `TooltipMod`, `HoverTooltipPatches`, `TooltipTextExVisibilityGate`, `InspectionPopupMod`, `NPCInspectionPopupMod`, `InGameStatMod`, `StatDetailsPopupMod`, `StatInfluenceInfoMod`, `StatStatusGraphicsMod`, `WeightBarMod`, `OptionsMenuMod`, `SaveMenuMod`, `QuestJournalElementMod` and nine more - plus every statement after `PatchAll()` in that `Awake` (`EmojiSpriteRuntimeLoader.Initialize` and seven explicit `TryPatchMethod` calls covering ExplorationPopup, TemperatureIndicator, GroupInventoryActionButton and EncounterPopup). WikiMod logs nothing, so the mod simply half-disappears.
- **New `Patching/BugFixes/WikiModPatchAllRescue.cs`:** a finalizer on Harmony's own `PatchClassProcessor.Patch` that, when the failing patch class belongs to the WikiMod assembly, logs one actionable Warn (naming the class, the bind error, how many later WikiMod patch classes the abort would have cost, and the installed WikiMod version) and swallows the exception so `PatchAll` continues to the next type. One class is lost instead of all of its successors. Scope is deliberately narrow: a bind failure in ANY other assembly, ours included, is rethrown untouched, because swallowing those would hide real breakage in code we control. Config `Compatibility/RescueStaleWikiModPatchAll` (default true) turns the rescue off and leaves only the Warn. The guard installs nothing when no `WikiMod.dll` is on disk.
- **Ordering is load-bearing and had to be measured:** BepInEx loads the framework 3rd and WikiMod last (positions 3 and 21 of 21 on this install), and WikiMod's `Awake` runs before the framework's next frame, so this is registered synchronously from `Plugin.Awake` and NOT from the deferred coroutine `WikiModQuickFindFix` uses (which exists because that fix needs WikiMod's own types to be loaded first; this one only needs 0Harmony, which always is).
- **What the rescue does NOT do:** it never re-enables `GraphicsManagerMod` itself, so WikiMod's card-stacking/slot-relocation behaviour is unchanged in both directions. Two measurements bound the risk of restoring the other 23: RefCheck over the live `WikiMod.dll` finds exactly **3 unresolved game members out of 2159** (`AddSlot`, `FindPileForCard`, `MoveCardToSlot`), whose only call sites are `DynamicLayoutSlotStackingMod.TryRelocate`, `ClingyCat.OnCardLoaded` and `EquipTabContent.HandleSlotAction` - none of them in a rescued class - and all 23 rescued classes resolve their declared patch targets against EA 0.67i. The rescue is a stopgap, not a fix: **the real fix is updating WikiMod**, which the Warn says, and which the author's own compatibility manifest supports (`wikimod-config.uuppi.com/config.json` maps mod 3.5.3 to required game 0.0.67.0).
- Gate: `Development_Tools/Tests/Framework-WikiModPatchAllRescue.Tests.ps1`. It checks that Plugin.cs installs the guard, that the finalizer rethrows for non-WikiMod containers, and that both names the guard resolves by STRING (`PatchClassProcessor.containerType` and `.Patch`) exist on the 0Harmony build the framework binds - the last of these being invisible to RefCheck by its own README's admission, since string reflection emits no MemberRef. Both source-shape checks ship a break-and-watch-red control that strips the real line from a throwaway copy of the real file and runs it through the same function the green assertion uses. What no test can see is the game launch itself, filed as `T2.234` (which carries a negative check: with `RescueStaleWikiModPatchAll` off and a full relaunch, the tooltips must disappear again, or their return had another cause). `Mod-Regression.Tests.ps1` gains two `IgnoredTargets` entries, since its scanner reads `AccessTools.Field/Method(typeof(X), ...)` as a game-API dependency and `PatchClassProcessor` is a HarmonyLib type.

## [2.25.30] - 2026-09-09

### Fixed

- **Connection gates were evaluated in the MAIN MENU, where every condition reads false, so every `HideTravelDA` gate flipped LOCKED between runs and stripped its travel DA off process-wide `CardData` (retro `river-bridge-east-click-noop`, `T2.186`).** `ConnectionGateService` re-checks gates on a 5 s `TickEvents.Interval`; that interval is driven by `Plugin.Update` and never stops, and in the menu `MBSingleton<GameManager>.Instance` is C#-null (a destroyed instance fails Unity's `(bool)` check, `FindObjectOfType` finds none), so `IsPerkEquipped` / `IsImprovementBuilt` / `StatThreshold` all returned false while the session-cached `WorldMapData` lookup kept `EvaluateAll` running. CMC's River Bridge gate (`HideTravelDA: true`, `NeighborCt8UID` = River Clearing) strips exactly one DA on that branch - River Clearing's injected East - and never Village Path's West, which is why only the eastbound crossing died. The next run start restored the DA, but AFTER `GameManager.Awake`'s synchronous `LoadCards()` had snapshotted the stripped list into the board's `InGameCardBase.DismantleActions` arrays (`.decomp/GameManager.cs:2437` vs `OnGMInitialized` at `:2674`), and the repairing resync was dropped whenever `GameQuery.IsTransitioning` was true. `ExplorationPopup` draws the compass from the CardModel LIST and dispatches clicks through the cached ARRAY, returning silently on `Length <= _Index`: a button that renders, clicks, and does nothing.
- **Four changes.** (1) `EvaluateAll` returns before evaluating anything without a live `GameManager`; `SealableGateService.OnPoll` (its own 1 s interval) gets the same guard. (2) `ResyncInGameDaCacheIfPresent` defers to a `_pendingResync` set during a transition instead of dropping the request; `EvaluateAll` drains it. (3) New `Patching.BugFixes.TravelDaCacheResync`: a prefix on `ExplorationPopup.Setup` rebuilds a CT8's cached array from its list whenever the two differ (length or element identity) and logs one `Log.Info` `[TravelDaCacheResync]` line per repair - deliberately Info, because that line is the runtime confirmation the retro is waiting on. (4) `WorldMapInjector.BuildTravelProducedCards`' fallback sets `Quantity (1,1)` on the `CardDrop` it creates from scratch (a fresh instance, never the shared template), closing the latent `EnvID.Empty` travel button that branch could emit.
- Gate: `Development_Tools/Tests/Framework-ConnectionGateEvaluation.Tests.ps1`. Every source-shape assertion carries a break-and-watch-red control on a throwaway copy of the REAL file. What no test can see is the in-game click; that stays on `T2.186`, whose recipe now reads the `[TravelDaCacheResync]` line first.

## [2.25.29] - 2026-09-08

### Fixed

- **`Api.ActionRouter` read the two cards of every card-on-card action backwards, and every drag `Cancel` gate in the fleet fired only after the dragged card had already been destroyed (T1.56).** `GameManager.CardOnCardActionRoutine` is `(_Action, _GivenCard, _ReceivingCard, ...)` - GIVEN card first - but the router resolved "first InGameCardBase parameter = receiver", so on that leg `ctx.Card` was the dragged card. That leg applies the given card's own changes first (`ApplyCardStateChange(GivenCardChanges)`; every gated drag in the fleet ships `ModType 3` = Destroy: Sell to a resident, Deposit Currency, Stock the Wood Pile, the sawmill's Cut) and only THEN tail-calls `ActionRoutine(..., _ModifiersAlreadyCollected: true, _GivenCard)` and waits for it. Receiver-keyed handlers therefore never matched on the card-on-card leg and fired on the tail-call leg, which is why 59 of the fleet's 63 handlers "worked" - with the one consequence that a `Cancel` gate refusing a drag (chest cannot afford the sale, account full, no fuel value, mill race dry) ran after the player's card was gone, so a refused Sell / Deposit / Stock / Cut destroyed the item and paid nothing. The two card-unbounded handlers (WDI `MillRaceGate`, CMC `TraitsActionHandler`) fired twice per drag, once with the cards inverted. A bare index swap was built and REVERTED on 2026-08-16 because with correct cards on both legs every receiver-keyed handler fires twice (double Inn / Academy / Copper Chest payouts, double Inn salt, double fishpond stocking, double saw damage) and the per-frame dedup cannot span two legs that yield between them.
- **The fix:** the card-on-card prefix now resolves `_GivenCard` and `_ReceivingCard` by parameter NAME and owns dispatch for those actions, and `ActionRoutine_Prefix` skips the tail-call leg whenever `_ModifiersAlreadyCollected` (also resolved by name) is `true` - the only call site in the game that passes true is that tail-call (`.decomp/GameManager.cs`, re-verified on EA 0.67i). Each drag now dispatches exactly once with the correct cards, and `Before` / `Cancel` run BEFORE the dragged card is consumed, so a refused drag keeps the player's card. `AfterWrapped` timing is unchanged (the card-on-card routine completes only when its tail-call does). If either card name or the marker fails to resolve on a future game build, the routine is left UNPATCHED and drags dispatch once on the `ActionRoutine` leg (correct cards, old timing) with a startup warning naming the unresolved index; there is no path back to a double dispatch or to the inverted read. Fleet handler surface enumerated 2026-09-08: 63 registration sites in 9 mods, 59 receiver-keyed, 2 card-unbounded, 1 hybrid, 51 side-effecting.
- Gate: `Development_Tools/Tests/Framework-ActionRouterDispatch.Tests.ps1`. It loads the built DLL from bytes and drives the router's pure resolution helpers against Add-Type replicas of the decompiled signatures (by-name resolution lands on `_ReceivingCard` = index 2 where the old positional read returned index 1 = `_GivenCard`; the tail-call check passes only for a boxed `true` at the resolved index), pins the decompile premise (exactly one `_ModifiersAlreadyCollected: true` call site, inside `CardOnCardActionRoutine`) whenever `.decomp/` is present, and refuses a stale artifact (assembly version must match `Plugin.cs`). Watched red on a planted inversion of the marker check and green after. What it cannot see is the in-game dispatch itself, which is the fleet drag pass filed as `T1.81`; T1.56 stays as recorded until that pass.

## [2.25.28] - 2026-09-07

### Changed

- **`AlwaysUpdateService` no longer force-sets `AlwaysUpdate = true` on CT2 board fixtures that have nothing to process over time (DG-F1, option (b)).** The flag makes `CardData.IndependentFromEnv` true for any CardType (`.decomp/CardData.cs:1270-1283`), so `ChangeEnvironment` skips the card when the leaving environment's saved card list is rebuilt (`.decomp/GameManager.cs:10244`/`:10263`/`:10282`) and it lives in the global `AllCards` instead. For a processing station that IS the delivery mechanism for background ticking - `ApplyRates` iterates `AllCards` (`:5812`) and `InGameCardBase` gates background work on the flag (`.decomp/InGameCardBase.cs:6165`/`:6214`/`:6225`) - but for a notice board, an exit card or an empty chest it bought nothing while costing unbounded `AllCards` growth (`:10301` never removes them) and making `EnvironmentsData` an unreliable answer to "is this fixture persisted on that board". A card now counts as ticking if any of the eight `DurabilityStat` blocks is Active with a non-zero rate, an active `MinRate`/`MaxRate` clamp or an OnZero/OnFull threshold action; or it declares `PassiveStatEffects`/`PassiveEffects`/`RemotePassiveEffects`/`DurabilityTransferEffects`/`ActiveCounters`; or it has non-zero `MaxLiquidCapacity`. **Measured: 57 of 123 mod CT2 cards keep the flag, 66 no longer get it** - the 66 being exit cards, notice boards, chests, lecterns, beds and trays. Every uncertain read (unreadable CardType, missing reflected field, throwing read) keeps the previous force-true behaviour and warns once per cause, because a station wrongly judged inert is the failure direction with player-visible cost. **Author opt-in, no new schema:** an inert card is left exactly as authored and never forced to `false`, so `"AlwaysUpdate": true` in the card's own JSON keeps background tracking for a fixture that genuinely needs it. Only CT2 is in scope. Existing saves move those 66 cards back into per-environment save data on first load. Gate: `Development_Tools/Tests/AlwaysUpdate-Ct2Scoping.Tests.ps1`, whose data half pins ten real load-bearing ticking cards AND four real inert ones (a predicate that accepted everything would pass the ticking half alone), with five self-demonstrations watched going red. In-game confirmation is tracked as `T2.185`, not yet done.
- **Fixed the deploy pipeline dropping two declarative markers, which is why `FlavourMatrixInjector` and `ModifierPackageInjector` had never executed in any play session.** `Development_Tools/Deploy-Mods.ps1` had no copy step for `FlavourMatrix/` at all and omitted `Modifiers.json` from its root-manifest list - the only two of the seventeen top-level markers `ModDiscovery` detects that no deploy step copied - so a mod shipping either had it silently dropped from every canonical deploy. `Pack-Suite.ps1` was missing `FlavourMatrix/` too (`Modifiers.json` rides its blanket root-`*.json` copy). The existing coverage test derives its expectation from `JsonDataLoader.DirToTypeName`, and neither is a `DirToTypeName` folder, so it was structurally blind to the family and passed throughout. New `Deploy-Mods.Tests.ps1` case derives the expected set from `ModDiscovery.cs` itself, strips comments before matching so a marker named only in a comment cannot satisfy it, and requires a quote/backslash-delimited token so `NotModifiers.json` cannot pass for `Modifiers.json`; three self-demonstrations ship with it. Also added a `-MasterTesting` deploy switch (explicit only, never `-All`/`-Published`) and a `-NoReleaseZip` flag, so the dev-only fixture mod deploys through the canonical script and is still never packaged for distribution.
- **Verification fixtures for N1/N3 now exist** in the dev-only `MasterTesting` mod (`FlavourTag/` x2 + `FlavourMatrix/`, and `GameModifierPackage/` + `Modifiers.json`), with in-game checks filed as `T2.183`/`T2.184`. Two corrections to the prescription they were built from: vanilla's `FlavourMatrix` already defines all 136 pairs of its 17 tags, so a vanilla-only pair is skipped by `HasPair` and would make a working injector look inert; and vanilla's two `GameModifierPackage`s (`EasyMode`, `TestEasyPackage`) both ship `AddedCards: []` and `StartingStatModifiers: []`, so auto-applying either changes nothing observable and the N3 check could never have passed against them.

### Fixed

- **`AnimalValidator` now rejects an `Encounter.Aggression.BaseWeight` that the species' own movement duty always outranks (silent dead attack duty).** Duty selection is winner-take-all, not weighted-random: `InGameNPC.SelectDuty` sorts the eligible duties by weight descending and expands its random-pick group only while the next weight is EQUAL to the top one (`.decomp/InGameNPC.cs` lines 2316-2320), and `MoveDutyAction.CanBePerformed` returns `true` unconditionally for `MovementTypes.Teleport` (`.decomp/MoveDutyAction.cs` lines 252-253), so a teleporting movement duty never yields its slot by becoming undoable either. An aggression duty weighted below it is therefore generated, attached, and eligible on every tick of its window, and is never once selected - with no error, no log, and the duty plainly present in the agent's duty list. M4 hit the identical defect on the feed duty and fixed it by tying `Traps.Bait.DutyWeight` to the movement weight, but the guard was written for that one field only; the aggression check beside it tested `> 0` and nothing else. Both now share `AnimalValidator.HeaviestMovementDutyWeight`, and a tie is accepted because a tie is genuinely selectable.
- **Consequence for Sirus23's owl (fixed in Sirus23 1.20.7):** the owl shipped `Aggression.BaseWeight` 600 against `Movement.PlayerAttraction.BaseWeight` 1e9 from 2.25.0 onward, with the 22-04 attack window sitting entirely inside the 20-06 activity window and no per-day cap on the seek duty. Its night attack could not fire at all, which made the Animal System plan's M5 exit criterion ("owl night attack fires from a *generated* duty into a *generated* encounter") impossible to pass regardless of how the playtest went. The manifest comment beside the value read "will almost never win selection"; the decompile says never.
- Gate: `Development_Tools/Tests/Animals-ManifestHygiene.Tests.ps1` CHECK C. It was watched going red on the real `Sirus23_Mod_Collection/Animals/Owl.json` as it stood at HEAD (via a `git show HEAD:` copy under the scratchpad, so nothing tracked was mutated to stage the failure) and green on the repaired file, plus a `Fixtures/Animals_Hygiene_Broken/Animals/AggressionOutranked.json` reproducing the shipped weights and a `Fixtures/Animals_Hygiene_Clean/Animals/AggressionTied.json` proving an equal weight is not flagged.

## [2.25.27] - 2026-09-06

### Changed

- **Clone-env duplicate-terrain ("second set of trees") investigation: replaced the ambiguous live-trim breadcrumb with a discriminating census. No behavior change; diagnostics only.** New player report 2026-09-06 ("there are starting trees then when you travel to and then from a location a second set of trees spawns") against 2.25.26, i.e. after all three previously-identified links in the chain were fixed. This session deliberately did NOT ship a sixth blind fix: the live log shows the 2.25.22 arrival breadcrumb firing correctly and scoping correctly (`env='cmcEnvVillage'`) but reporting `trimmed=0`, and the player's active save is a fresh day-1 game that has never visited a CMC clone tile (`ChartedEnvironments: []`), so no reproduction exists in any evidence on disk. Root problem with the existing instrumentation: `trimmed=0` reads identically whether (a) no duplicate exists, (b) a duplicate exists but `BuildExpectedMax`'s UID keys never matched the live cards' UIDs, or (c) they matched but `TryRemoveCard` failed - and attempts 1 through 5 were each evaluated against exactly that ambiguous signal, with attempt 5 discovering the removal primitive had been silently inert the entire time. The breadcrumb now appends `envIdx=<UniqueIDIndex> vanillaGate=<...> board=<n> drops=[Name obs/max ...]`, plus `UID-MISMATCH=[...]` and `REMOVE-FAILED=n/m` when either occurs. The highest-value field is `vanillaGate`: vanilla's re-add loop runs **only** when `GameManager.GetExplorableCard(CurrentEnvironment)` returns null (`.decomp/GameManager.cs:10444` returns early otherwise), so that one boolean separates "our trim missed a duplicate" from "the duplicate did not come from `CheckForMissingDefaultCardsInEnv` at all" - the two branches the retrospective's next-attempt plan could not previously tell apart.
- **Mechanism findings from this session's audit (recorded because they narrow the search, not because they produced a fix):** (1) `AlwaysDropUniqueDefaultEnvDrops` is `False` on all 10 vanilla envs CMC clones, so the "guard is bypassed on every arrival" hypothesis is **ruled out** - the re-add is genuinely gated behind `GetExplorableCard` returning null. (2) That gate, and `CardIsOnBoard`'s env filter, both reduce to `EnvID.MatchesEnv` -> `EnvDictKey.Equals`, which compares `CardData.UniqueIDIndex` (an **int**) cached in a `[NonSerialized]` struct field - this is the concrete mechanism behind the retrospective's longstanding but unexplained claim that "EnvID matching is fragile for clone envs", and it means any drift or late assignment of a mod card's `UniqueIDIndex` desynchronizes environment identity wholesale. `CardIsOnBoard` additionally compares `BaseCards[i].CardModel != _Card` by **reference**, so any second CardData instance for the same UID defeats it. (3) The `__envlocal` UID-split hypothesis was independently re-derived and again found sound rather than broken: `CardCloneService.GetEnvLocalVariant` memoizes one variant per original UID per session and re-uses any already registered under that UID, so `BuildExpectedMax`'s keys and live cards' UIDs agree within a run. The census now asserts that at runtime instead of relying on the code reading.

### Fixed (existing saves)

- **The save-data heal pass is now verified rather than assumed, and reports on every boot even when it heals nothing.** Requirement from the user: the fix has to cover saves that already carry doubled terrain, not just prevent new cases. Traced the whole existing-save path against the decompile first, and it holds up: `LoadCards()` (`Awake`, `GameManager.cs:2437`) populates `EnvironmentsData` from the save BEFORE `StartCoroutine(FinishInitializing())` at 2496 fires `OnGMInitialized` at 2674, so `PreCreateCloneEnvSaveData`'s trim genuinely sees real saved boards; and `GetRegularCards` is declared `=> AllRegularCards` (`EnvironmentSaveDataByReference.cs:45`), the backing `List` itself rather than a projection, so `RemoveAt` mutates the entry the game will serialize. **This overturns a suspicion recorded earlier in the same session** that the boot trim ran before save data existed and therefore could never heal anything: line order across two different methods is not execution order, and checking the call sites refuted it. Two real defects remained in how it *reported*: `TrimExcessDefaultDrops` claimed success purely from "`RemoveAt` was called N times" (the identical unverified-claim shape that let the live trim report removals for three releases while `CardUtil.TryRemoveCard` was inert), and the aggregate Info line only fired when `trimmedCards > 0`, so "examined 12 saved boards, all clean" and "never saw this save at all" produced byte-identical logs. Added a post-removal recount off the same mutated list (`STILL-OVER` names any card that survived) and an unconditional `existing-save heal pass: N clone node(s), M with a saved board, trimmed X ...` line, where `M` distinguishes a genuinely-examined save from a boot where `EnvironmentsData` was empty.
- **Measured the player's actual existing saves, and they contain no duplicates: this is the observation that would disprove the data-duplication theory, and it showed up.** Resolved each of the 12 CMC clone nodes' real declared drops (MapNodes.json -> `CloneOfEnvironmentUID` -> the vanilla env card's `DefaultEnvCardDrops`, skipping CT8 exactly as `BuildExpectedMax` does) and compared per-UID counts in every save on disk. Game_0 (day 5) and Game_2 (day 102) each have **all 12 clone tiles visited and saved with zero declared-drop over-counts**; Game_1 (day 408) predates the expansion and has none saved. Earlier in the session only the day-1 throwaway Game_3 had been checked, which was a real gap, and that slot has since been deleted. Consequence: **every diagnostic and every fix across all six attempts counts board cards, so none of them can see a duplicate that is not in the board data** - which is the CLAUDE.md "a check that cannot fail in the way you need it to fail" rule landing on this bug's entire investigation history.
- **Competing explanation now on the table, not yet confirmed: the duplication may be visual slot placement rather than board data.** `Player.log` shows WikiMod 3.5.1 failing to apply a Harmony **prefix on `GraphicsManager.AddSlot`** every boot: `AccessTools.DeclaredMethod: Could not find method for type GraphicsManager and name AddSlot and parameters (SlotsTypes, CardData, CardData, int)` followed by `ArgumentException: Undefined target method for patch method static void WikiMod.GraphicsManagerMod::AddSlotPrefix(GraphicsManager, SlotsTypes, CardData, CardData, Int32&)`. That is a distinct call site from the three `TryRelocate` callers 2.25.25/2.25.26 finalized, it takes `ref int _Index` (i.e. it decides which slot a card lands in), and it is dead on the current game version. A card rendered into two slots would look exactly like "a second set of trees", persist visually for the rest of the session, and be invisible to every count-based check - and it would explain why five mechanically-correct fixes each failed to change what the player saw. **Discriminating test, cheap for the player: reload the save.** The saved boards are provably clean, so a visual duplicate disappears on reload while a data duplicate does not. Not fixed here - filed with evidence rather than patched, since it is a different bug class in a third-party binary and no confirmed manifestation ties it to the trees yet.
- Verified this build rather than trusting "Build succeeded": deployed DLL md5 `4d5ea17c...` matches `bin/Release` byte-for-byte, all new method names resolve in `#Strings` (UTF-8) and all new log literals in `#US` (UTF-16-LE) per the encoding split in root `CLAUDE.md` #6 - including `census=no-declared-drops`, which a peer session reported as absent from the binary and which is in fact present in both the build and the deploy. `Development_Tools/RefCheck` passes: 383 typed game references, 0 unresolved, exit 0, so the new typed calls (`CardData.UniqueIDIndex`, `GameManager.GetExplorableCard`, `MBSingleton<GameManager>.Instance`) all bind against the live EA 0.67i assembly.

## [2.25.26] — 2026-09-06

### Fixed

- **2.25.25's WikiMod fix stopped the save-load crash but boards/containers rendered completely empty afterward — player-confirmed via live testing ("everything is empty, nothing to transfer, nowhere to go").** Investigation (live `Player.log`, not just BepInEx's `LogOutput.log`) found WikiMod's `DynamicLayoutSlotStackingMod` has **three** call sites hitting the same stale `GraphicsManager.AddSlot(SlotsTypes,CardData,CardData,int)` signature, and 2.25.25 only patched one (`OnCardLoaded`). The dominant one — `AssignCardPostfix`, a genuine Harmony postfix WikiMod applies to vanilla's own `DynamicLayoutSlot.AssignCard` — threw **801 unhandled `MissingMethodException`s in a single session** (vs. a handful for `OnCardLoaded`). Vanilla's own `AddSlot()`/`AssignCard()` calls (`.decomp/GraphicsManager.cs` lines 2204/2310/2353/2361/2376 etc.) already succeed before this postfix runs, but its own broken call throws unhandled and — being a real Harmony postfix on a vanilla method rather than one of our finalizer-wrapped methods — propagates out of `AssignCard` back into whatever vanilla loop is restoring multiple saved cards' slot assignments, aborting it after the first card. That's the actual mechanism behind the empty boards: the loop that visually (and likely data-wise, depending on what else that loop does per iteration) places cards into their slots never gets past card #1. A third call site, `OnCardSpawned`, fires for newly spawned/created cards during ordinary play (not just load), so it's an ongoing hazard for the rest of a session, not a one-time load artifact. Extended `WikiModQuickFindFix` with finalizer patches on both `AssignCardPostfix` and `OnCardSpawned`, matching the existing `OnCardLoaded` treatment (swallow `MissingMethodException`, log capped at 5). Built clean; not yet re-verified in-game — needs a fresh load of the same affected save to confirm boards/containers now populate. **Mechanism correction (same day, via a peer session's Mono.Cecil IL scan of the live WikiMod 3.5.1 DLL):** none of the three methods calls `GraphicsManager.AddSlot` directly — all three are callers of one shared private helper, `DynamicLayoutSlotStackingMod.TryRelocate`, which contains the single actual stale call site. Mono's own runtime stack traces never showed this (consistent with `TryRelocate` being inlined), so this session's own investigation described it as "three call sites" rather than "three callers of one call site" — the fix and its boundary (patch the callers, not the inlined callee) are unaffected, only the explanation was imprecise. The same scan found exactly these three callers and no fourth entry point on the deployed DLL, and separately flagged two more of WikiMod's stale-`GraphicsManager`-overload call sites (`ClingyCat.OnCardLoaded` → `MoveCardToSlot`, `PlayerInfoUI.EquipTabContent.HandleSlotAction` → `FindPileForCard`) with zero observed occurrences in `Player.log` — documented in the retro, not patched, since there's no confirmed manifestation to fix against yet.
- **Diagnostic note for future sessions**: BepInEx's `LogOutput.log` alone under-reported this bug — it only shows exceptions that pass through our own `Util.Log` calls or through Unity's own `[Error : Unity Log]` prefix for genuinely unhandled exceptions bubbling all the way to the engine, and in this case actually did NOT show the `AssignCardPostfix` exceptions at all in the copy first inspected. The raw Unity `Player.log` (`%USERPROFILE%\AppData\LocalLow\WinterSpring Games\Card Survival - Fantasy Forest\Player.log`) carries full stack traces for every unhandled exception including ones BepInEx's own log formatting doesn't surface, and was the only way this second call site was found. Also useful this session: the game's own save-slot metadata (`Games\Game_N\GameInfo.json`, `HasData`/`CharacterName` per slot) let the actual on-disk save state be checked directly — confirmed the player's real saves (Game_0/1/2) were untouched and intact throughout, ruling out on-disk data loss as a competing explanation before writing this fix.

### Changed

- **Game-update refresh for EA 0.67i.** Vanilla JSON delta is 0 against 0.67h (not one game data
  file changed), but the game code did change, so `lib/Assembly-CSharp.dll` was refreshed from the
  live binary, the `Api.VanillaIds` registry regenerated (no card added, removed or renamed), and
  the `CSFF-JsonData_Current` junction plus `CURRENT_VERSION.txt` repointed to
  `CSFF-JsonData_EA_0-67i`.
- **Every in-house project rebuilt from clean against the refreshed reference: 16/16 Release
  builds, 0 errors, 0 warnings.** Forced `-t:Rebuild` rather than an incremental build: an
  incremental `dotnet build` can skip the compile step entirely and still report success, so the
  compile-time signature check the game-update rule asks for never actually runs.
- **The NSTRIP GAP was measured rather than assumed, and it is narrower than previously recorded.**
  Only 4 projects bind the plain `Assembly-CSharp.dll`, so a rebuild cannot signature-check the 9
  that bind the months-stale `Assembly-CSharp-nstrip.dll`. Those 9 were checked from the opposite
  direction, by reading each built DLL's `AssemblyRef`/`MemberRef` metadata: the same table the
  runtime resolves against, and the one whose failure raises `MissingMethodException`. 547 typed
  game references across 6 mods all resolve against live 0.67i, and the other 8 mods emit no typed
  game reference whatsoever (they reach the game only via this framework and string-based Harmony
  reflection), so a stale NStrip reference cannot break them at runtime. Note the NStrip DLLs carry
  the assembly identity `Assembly-CSharp` despite the different filename, so a typed call in one of
  those mods WOULD have surfaced as such a reference; none did. The gap constrains the API surface
  visible while authoring those mods, not runtime behaviour on this version. Tool:
  `Development_Tools/RefCheck/`.
- **The same scan over the deployed plugin folder independently reproduced this version's WikiMod
  finding.** Of 18 deployed plugin DLLs, WikiMod is the only one carrying unresolved game
  references, and it carries exactly three: `GraphicsManager::AddSlot(SlotsTypes,CardData,CardData,int)`
  (the crash the finalizers above cover) plus `FindPileForCard` and `MoveCardToSlot`, the same two
  additional stale sites the peer session's Mono.Cecil IL scan flagged, reached here from PE
  metadata by an unrelated tool. Every other plugin, in-house and third-party, resolves clean
  against 0.67i. This doubles as the gate demonstration required before trusting a new check: run
  against WikiMod the check goes red on a known-real break, so its green on our own mods is
  meaningful rather than vacuous.

## [2.25.25] — 2026-09-06

### Fixed

- **Loading an old save with WikiMod installed threw `MissingMethodException: DynamicLayoutSlot GraphicsManager.AddSlot(SlotsTypes,CardData,CardData,int)` on every single card, breaking the load entirely — player report, "old save is completely broken on load."** WikiMod's `DynamicLayoutSlotStackingMod.OnCardLoaded` (hooked into `GameManager.AddCard`'s deserialization coroutine via `WikiMod.GameManagerMod.HandleCardLoaded`, so it runs once per card in the save) has a compiled call site targeting a `GraphicsManager.AddSlot(SlotsTypes,CardData,CardData,int)` overload that no longer exists on this game version — a later game update inserted an `InGameNPCOrPlayer` parameter and made `AddSlot` private (decompile-confirmed, `GraphicsManager.cs:3302`). This is the same "compiled against a stale `Assembly-CSharp.dll`" bug class documented in root `CLAUDE.md`'s Game-Update Reference Refresh rule, except the stale binary is WikiMod's own third-party DLL and can't be rebuilt from this repo. Extended `WikiModQuickFindFix` with a fourth defensive patch: a Harmony finalizer on `WikiMod.DynamicLayoutSlotStackingMod.OnCardLoaded` that swallows `MissingMethodException` (logged, capped at 5 messages) so WikiMod just skips its dynamic-layout-slot-stacking hook for that card instead of aborting the surrounding coroutine — save load now proceeds normally. Built clean; not yet verified in-game (needs the reporting player's save + WikiMod installed to confirm the load completes and no other WikiMod feature depends on this hook having run).

## [2.25.24] — 2026-09-01

### Changed

- **Game-update refresh for EA 0.67e** (game skipped 0.67d). Regenerated the embedded `Api.VanillaIds` registry from the `CSFF-JsonData_EA_0-67e` extraction: one new card entry (`Bp_YarnFromTwine`, the new Spindle yarn-from-twine blueprint), zero removals or renames; all 6 curated groups resolved unchanged. Refreshed `lib/Assembly-CSharp.dll` from the updated game binary (game code changed in this patch) and rebuilt clean — no signature breaks in the framework's direct typed calls. Mod-compat audit of the vanilla data diff (90 semantically changed objects out of 29,248 files) found no mod-referenced GUID removed or repointed in a breaking way: `Flint` still exists (only removed from swamp clearing drop tables), and the Primeval Wolf enchant / Water Spirit blessing GUID changes are vanilla bug fixes to references no mod shares.

## [2.25.23] — 2026-08-28

### Fixed

- **Traveling via a Portal Hub could land the player in the wrong environment and strand a following companion behind, if the player had been through a save-reload (e.g. entering/exiting an instanced construction interior like a cabin build site) earlier that session — player report 2026-08-27, "traveling from within a cabin causes the map to break."** Root cause: `PortalService.RegisterHubTravelHandlers()`/`RegisterHubExitHandler()` run on every `GameManager.OnGMInitialized` fire, which — per `SealableGateService.Initialize`'s already-documented note — is NOT a once-per-process event; it re-fires on every run start within the process, observed here as three `AutoSave.json` reloads in one session. Unlike every other `OnGMInitialized`-driven framework service (`SealableGateService`, `CompanionService`), these two methods registered a fresh `ActionRouter` handler on each fire with no deregistration of the previous batch — `ActionRouter.Register` is purely additive, so after 3 fires the shared Portal Hub card carried 3 duplicate handlers per destination button. One click then invoked `StartEnvironmentTravel` 3 times synchronously within the same dispatch, each call reassigning `GameManager.NextEnvironment` and starting its own `AddCard` coroutine — the exact "two maps overlap" corruption class `PrepareNextEnvironment`'s doc comment already describes (2026-08-24 portal-arrival board-overlap fix), observed here as the board showing a different environment's cards while the location card still read the clicked destination, with a followed companion left behind in the environment the player departed from. Fixed by tracking each call's registered handlers and unregistering the previous batch before adding new ones, the same pattern `CompanionService.Reset()` already uses for its own re-registration hazard. Built clean; not yet verified in-game.

## [2.25.22] — 2026-08-27

### Fixed

- **`CardUtil.TryRemoveCard` silently removed nothing — the actual remaining cause of "still doubling up" after 2.25.21.** `DestroyCard(bool)` is the only card-removal method that exists on this game version (`RemoveFromGame`/`DestroyCardFromInventory`, also probed, are absent) and it is a Unity coroutine (`IEnumerator DestroyCard(bool _NoDelay)` on `InGameCardBase`). `TryRemoveCard` located it correctly but invoked it with a bare `MethodInfo.Invoke` — for a compiler-generated coroutine method, that only constructs the state machine object and returns it immediately; none of the method's body (unslotting via `CurrentSlot.RemoveSpecificCard`, `CardVisuals.OnLogicDestroyed`, passive-effect/stat-modifier cancellation) runs until something drives it with `MoveNext()`. Every real vanilla call site wraps it in `StartCoroutine`/`StartCoroutineEx` (decompile-confirmed, `GameManager.cs` ~8445, ~9866) — `TryRemoveCard`'s naked `Invoke` discarded the returned enumerator, so the call always returned `true` (no exception) while the card silently stayed exactly where it was. This made `TryRemoveCard` a no-op everywhere it's used: `WorldMapInjector.TrimExcessDefaultDropsLive` (the 2.25.21 live-trim fix for doubled clone-env terrain — this is why it never visibly worked even once wired to the correct live collection), CMC's `TreeRespawnPatch` excess-tree trim, and Sirus23's `SheepPenPatch` (predation/escape removal) and `WolfTickPatch` (starvation death, morale departure, auto-feed consumption) — all four call sites were silently non-functional. Fixed by starting any `IEnumerator` result as a real coroutine on the GameManager singleton (a `MonoBehaviour` via `MBSingleton<GameManager>`), matching vanilla's own usage; `StartCoroutine` runs synchronously up to the method's first `yield`, which in `DestroyCard` is after the unslot/visual-destroy step, so the card disappears immediately from the caller's perspective. Built clean; not yet verified in-game. See `Documentation/Retrospectives/worldmap-clone-duplicate-terrain.md`.
- **Added an Info-level breadcrumb** (`WorldMapInjector: live trim arrival check — env='<uid>' trimmed=<n>`) on every arrival at one of a mod's clone envs (not just when a trim occurred), so a live session can directly confirm the `CheckForMissingDefaultCardsInEnv` postfix fires and scopes correctly, addressing Open Unknown #1 in the same retrospective. Scoped to clone-env arrivals only to avoid log spam on ordinary vanilla travel.

## [2.25.21] — 2026-08-27

### Fixed

- **The 2.25.19 live-trim postfix for doubled clone-env terrain (Ponds/Pine Trees/etc.) didn't actually fix it — player report 2026-08-27: "still doubling up... one set then when you enter a tile and leave a tile a second set appears."** Root cause: the postfix called `TrimExcessDefaultDrops`, the same function `PreCreateCloneEnvSaveData` uses at boot time — but that function reads/writes the env's SAVED `EnvironmentSaveDataByReference` entry via `GetEnvSaveData`. That's correct for the boot-time caller (the player isn't standing in the env yet, so the saved entry is what `LoadCardSet` restores on next visit), but wrong for a postfix firing while the player is CURRENTLY in that env: `CheckForMissingDefaultCardsInEnv`'s over-add runs through `GameManager.AddCard`, which mutates the LIVE board (`GameManager.AllCards`) directly (decompile-confirmed, `GameManager.cs` ~8303-8320) — the current env's saved entry is a stale snapshot that's only resynced FROM the live board when `ChangeEnvironment` saves it on leave. So the old live-trim postfix silently edited a copy that never contained the new duplicate, and that stale edit was then overwritten (duplicate included) the moment the player left anyway — the visible extra tree on screen was never touched. Fixed by adding `WorldMapInjector.TrimExcessDefaultDropsLive`, which trims straight off `Api.GameQuery.CardsInPlayerEnv()` (the same live collection `AddCard` populates) using `CardUtil.TryRemoveCard` — the same live-card idiom already used elsewhere in the framework (`SealableGateService`, `ConditionalDropService`, `PortalService`) and in CMC's own `TreeRespawnPatch`. The boot-time `PreCreateCloneEnvSaveData` trim is unchanged (still correct for its own save-data context). Built clean; not yet verified in-game.

## [2.25.20] — 2026-08-25

### Added

- **`WildlifeStuckDiagnostics` (opt-in, off by default) — `[Diagnostics] LogWildlifeStuck = true`.** Watches live vanilla Bear/Wolf/WolfPack/PrimevalWolf agents and logs how many consecutive DTP ticks each stays in the same environment, to confirm and localize player reports of wildlife getting permanently stuck on mod-injected WorldMap trail nodes with no way back to a vanilla environment. Root cause is unconfirmed between `WorldMapData.MapDict` pathfinding staleness (see `WorldMapInjector.RebuildPathfindingLookup`'s doc comment — that fix targets NPCDuty's A* pathfinding specifically; whether vanilla wildlife AI shares the same MapDict-backed call is unverified) and a possible edge/exit-seeding gap on modded trail nodes. Logs raw facts only; does not change any behavior. Confirmed there is no fire/torch-aversion mechanic anywhere in the codebase — a player's torch workaround has no basis in current code.

## [2.25.19] — 2026-08-25

### Fixed

- **WorldMap clone environments (e.g. CMC's map expansion tiles) could still show doubled forage terrain — two Ponds, two Pine Trees, two Small Pine Trees — after a player's first visit, even after the 2.25.10 fix.** Root cause: `WorldMapInjector.TrimExcessDefaultDrops` (2.25.10) only runs from `PreCreateCloneEnvSaveData`, which fires once per boot (`OnGMInitialized`). Most players don't restart the game the instant they first step onto a new expansion tile, so the vanilla `CheckForMissingDefaultCardsInEnv` over-add (documented in `TrimExcessDefaultDrops`'s doc comment) stayed doubled on the board for the rest of that play session — the boot-time trim could only clean it up on the NEXT launch (player report 2026-08-25: "when you travel from a tile for the first time a second set of trees spawns"). Added a postfix on `GameManager.CheckForMissingDefaultCardsInEnv` (`WorldMapInjector.ApplyLiveTrimPatch`, registered in `Plugin.Awake`) that re-runs the same trim immediately after the vanilla coroutine finishes, scoped to whichever clone env the player just arrived in — the duplicate is now corrected within the same visit it appeared, not just retroactively on the next boot. No-ops (near-zero cost, one `_prepared` scan) for every non-clone env and skips the wrap entirely for the whole session when no mod ships any clone nodes. Built clean; not yet verified in-game.

## [2.25.18] — 2026-08-25

### Added

- **Catch-up-tick performance, Phase 3 of `Documentation/Plans/CSFFModFramework/CatchUp_Performance_Plan.md`: K-chunked tick batching — the biggest lever in the plan.** New `[Performance] CatchUpBatchTicks` (default 16, `CatchUpBatchMinTicks=96` floor below which batching doesn't bother arming) groups a catch-up replay's elapsed ticks into chunks of up to K ticks each instead of replaying every tick individually, and scales/repeats the per-tick work to match — composes with the existing `CatchUpTickCap`/chain-discount/budget-clamp extensions from Phases 1-2. Expected: a capped 1344-tick replay measured at 4.4-6.2s drops to an estimated ~0.8-1.1s; a 15-hop severely-stale chain drops from ~60s toward ~12-16s. New file: `CatchUpTickBatching.cs`, eight `SafePatcher` hooks, all double-gated on an armed-this-hop flag plus `GameManager.IsCatchingUp` so scaled/repeated work cannot leak into ordinary gameplay ticks or action-driven durability changes:
  - `ChangeEnvironment` hand-off (via `CatchUpTickCap.ChangeEnvironment_Prefix`, after its own cap/chain-discount decision): clamps `LastUpdatedTick` to `now − numChunks` so vanilla's own catch-up loop runs `numChunks` times instead of the full elapsed-tick count, with per-chunk sizes precomputed (remainder-distributed so they sum exactly to the replayed window).
  - `ApplyRates` prefix advances a per-hop chunk cursor; postfix tops up non-`Updated` env tick-counters by the chunk's extra ticks (vanilla's own per-call `+1` under-crawls by `K−1` per chunk otherwise — card-attached counters are unaffected, they already jump straight to their live value).
  - `ChangeCardDurabilities` prefix scales all 9 rate arguments by the chunk size, gated on the catch-up call site's unique flag combination (`_Feedback:false, _SortSlot:false, _LiquidConcentrationPreservation:true` — every other call site in `GameManager` passes `true,true`) — durability decay is linear+clamp with edge-triggered `OnZero`/`OnFull` flags, so this is arithmetically exact including boundary-crossing events, just quantized to the chunk boundary instead of the exact sub-tick.
  - `UpdateCookingRecipes` prefix forwards the chunk size as `_TimePoints` — cooking already has a native per-tick-fidelity batch loop (`UpdateCardCooking`), so this preserves exact completion counts and even saves the per-tick scratch-card churn.
  - `UpdateTransferEffects` prefix-skip + re-entrancy-guarded wrapper that re-invokes the method once per real tick in the chunk (its `_TimePoints` parameter does NOT batch named transfer effects — an invocation-scoped dedup list means one call with K would yield only one transfer instead of K) — yields through every inner enumerator, never a bare drain (memory: `reference_synchronous_coroutine_drain_freeze`).
  - `FadeFlavours`/`FadeSpices`/`UpdateProducedLiquids` postfixes repeat the call `K−1` extra times, re-entrancy guarded — free for the ~99% of cards with no flavours/spices/produced liquids thanks to each method's own early-out guard. **`UpdateProducedLiquids` needed one more discriminator than the other two**: unlike `FadeFlavours`/`FadeSpices` (each exactly one call site), it has three reachable call sites within a single per-card catch-up pass — the direct end-of-block call plus two nested calls via `ApplyPassiveEffect` (reachable through `ChangeCardDurabilities`'s internal `UpdatePassiveEffects` call, and through `UpdatePassiveEffectStacks`'s durability-scaling rescale branch). Multiplying all three independently would over-produce liquid by up to K× on any card with both `CurrentProducedLiquids` and a rescaling passive effect. Fixed before shipping (caught by a concurrent-session cross-check, independently re-verified against the decompile) by bracketing both nested sources with a shared flag spanning their full execution (an `IEnumerator`-wrapping postfix for the coroutine-based `ChangeCardDurabilities`, a plain prefix/postfix pair for the synchronous `UpdatePassiveEffectStacks`) — the repeat-multiplier only applies when neither nested source's window is active, which is always true by the time the direct call fires (both nested sources complete synchronously before it, in the catch-up no-real-yield case this whole design already relies on).
  - `CatchUpBudgetClamp`'s tick-floor (`CatchUpMinTicks`, from Phase 1) now counts real-tick-equivalents instead of raw `ApplyRates` call counts — moved from a prefix to a postfix on `ApplyRates` (Harmony guarantees all prefixes on a method run before any postfix, so this reliably observes the chunk size `CatchUpTickBatching`'s own prefix just set, with no ordering race between the two classes' patches) and increments by `CatchUpTickBatching.CurrentChunkTicks` (1 when batching is off). Without this, K=16 batching would silently make the 384-tick floor unreachable (~84 calls per capped hop instead of ~1344), and the budget clamp would quietly stop doing anything the moment both phases were active together.
  - **Fidelity cost, bounded and documented:** mid-window rate recomputation (a fire dying mid-chunk, a cooker running past fuel exhaustion) becomes piecewise-constant at K-tick granularity — up to `K−1` ticks (4 in-game hours at the default K=16) of timing slop per transition, strictly smaller than the 34–405 days `CatchUpTickCap` already discards wholesale on any capped hop, and not player-detectable per the plan's empirical census (`RepeatActionOnZero`/`OnFull` and `RestrictToSpecificValues`-with-nonzero-rate usage are both effectively zero across vanilla + fleet content). `CatchUpBatchTicks=0` disables batching entirely (exact today's per-tick behavior); every hook soft-fails via `SafePatcher` (a failed patch just leaves batching permanently unarmed, not a crash).
  - Not yet build-verified in-game — built clean (0 warnings/errors) against the framework's own compile-time `Assembly-CSharp.dll`; needs a play session per the plan's own Phase 3 verification steps (compare-mode durability diff at `CatchUpBatchTicks=0` vs `16`, and a `LogTrackTiming` capture confirming the measured capped-hop CPU drop) before treating the estimated gains as confirmed.

## [2.25.17] — 2026-08-25

### Added

- **Catch-up-tick performance, Phase 2 of `Documentation/Plans/CSFFModFramework/CatchUp_Performance_Plan.md`: `HasCardEquipped` catch-up verify pass (log-only, diagnostic — not the short-circuit itself).** Every card the catch-up loop processes already passed `ApplyRates`'s `!IndependentFromEnv` guard, and an equipped card is `IndependentFromEnv` by definition — so `CharacterScreen.HasCardEquipped` should provably return `false` for every card queried during `GameManager.IsCatchingUp`, and short-circuiting it there would remove ~27,000 linear equipment-slot scans per catch-up tick on the measured heavy save. Per the plan's own explicit gate (and CLAUDE.md's "diagnostics before fixes" rule), this ships the verify pass FIRST, not the short-circuit: new `[Performance] LogCatchUpEquipmentScanHits` (default `true`) postfixes `HasCardEquipped` and logs an Info line only on the case that would make a short-circuit wrong — a `true` result observed while `IsCatchingUp` is true. One play session with zero hits is the ship gate for the actual short-circuit in a follow-up build. New file: `CatchUpEquipmentScanSkip.cs`.

## [2.25.16] — 2026-08-25

### Added

- **Catch-up-tick performance, Phase 1 of `Documentation/Plans/CSFFModFramework/CatchUp_Performance_Plan.md`: chain discount + wall-clock budget clamp on `CatchUpTickCap`.** Both compose with the existing tick cap and touch no per-tick cost by themselves.
  - **Chain discount** (`[Performance] CatchUpChainCapTicks=672`, `CatchUpChainWindowSeconds=180`): when `ChangeEnvironment`'s catch-up cap clamps twice within 180 real seconds (a burst of hops through long-unvisited environments), the second and later hops in that burst use a smaller 672-tick cap instead of the full 1344 — directly targets the "~60s to walk a 15-hop stale chain" complaint. Session-local only (a static real-time timestamp); never touches save data. Set either to 0 to disable.
  - **Wall-clock budget clamp** (`[Performance] CatchUpBudgetMs=2000`, `CatchUpMinTicks=384`): bounds a single catch-up replay by elapsed real time instead of always paying the same fixed tick-count worst case — a plain (non-`IEnumerator`) prefix on the private `GameManager.ApplyRates` stub raises `EnvironmentsData[env].LastUpdatedTick` once the replay has run past the budget AND simulated at least `CatchUpMinTicks`, which the loop's own tick-bound re-read (`.decomp/GameManager.cs:10238/10240`) then reads on its next iteration to stop the loop after the in-flight tick. The 384-tick floor guarantees raw meat/fish/fruit/milk/cooked meat (all ≤300-tick saturation windows) always fully rot regardless of machine load. `CatchUpBudgetMs=0` disables.
  - `CatchUpTickCap.ChangeEnvironment_Prefix` now installs even when `CatchUpTickCap` itself is set to 0 (previously it skipped patching entirely in that case) — both new extensions above are independently toggleable via their own configs and need to keep receiving the per-hop hand-off regardless of whether the hard tick cap is enabled. New file: `CatchUpBudgetClamp.cs`.
  - Never lowers `LastUpdatedTick` (would repeat a tick number and double-apply cooking via `UpdateCookingRecipes`'s equality-dedup guard) — same hard rule the existing cap already follows.

## [2.25.15] — 2026-08-25

### Added

- **Catch-up-tick performance diagnostics (Phase 0 of `Documentation/Plans/CSFFModFramework/CatchUp_Performance_Plan.md`).** `TrackingTimingDiagnostics` (opt-in via `[Diagnostics] LogTrackTiming`) now also: times `GameManager.LoadCardSet` and `GameManager.UpdatePassiveEffects` per `ChangeEnvironment` call (both can fire more than once per travel) and logs an `AllCards` census broken down by owning mod, with `AlwaysUpdate`/`IndependentFromEnv` counts per mod — built to test whether `AlwaysUpdateService`'s blanket `AlwaysUpdate=true` on every mod card is inflating `AllCards` on heavily-modded saves (the leading candidate for the "removing mods restores travel speed" player report).

### Fixed

- **`CatchUpTickCap.cs`'s config description and header comment overclaimed "spoilage saturates well within 14 days."** True for 70% of vanilla spoilage carriers (raw food ≤3.1d, cooked ≤7d, dried meat 8.8d, bread/cave timers 10.4-11.2d — all fully saturate inside the 1344-tick default), false for the other 30%: long-shelf-life preserved goods (hard cheese 60d, hardtack/grains/wine 120d) advance only partially by design, which no finite cap could avoid. Rewrote both to state the actual saturation bands with concrete examples, and to explicitly document why the default should not be lowered or made staleness-tiered (both evaluated and rejected — see the plan doc's cap-tuning findings).
- **`SpriteTextureCache` held a ~700MB `bundle.bin` buffer in memory for the entire play session with no consumer after load.** `_bundleBuffer`/`_bundleIndex` are now released (`ReleaseBundleBuffer`) once `TryWriteBundle` reaches any of its three exit paths — `SpriteLoader.LoadAll` (the sole `TryLoad` caller) has always finished by the time that phase runs, and any in-flight bundle write already captured its own independent reference to the buffer via a method parameter, so releasing the static field doesn't affect it. Per-file `.sc` cache remains as a graceful fallback for any hypothetical future `TryLoad` call.

## [2.25.14] — 2026-08-25

### Fixed

- **Portal Hub "Return to Portal" could re-corrupt the same environment-key class the 2.25.6 `PrepareNextEnvironment` fix closed, if the Portal Hub was used from INSIDE an instanced interior** (Cabin/Cellar/Coop/Enclosure, mines, attics — anywhere `InstancedEnvironment:true`). `StartEnvironmentTravel(recordReturn:true)` only recorded the departure environment's bare UID; on return, `StartReturnTravel` rebuilt the destination via `new EnvID(CardData)`, whose ctor guard nulls `MainEnvCard` outright for an instanced target with no accompanying parent chain (`EnvID.cs`: "Cannot create env ID for an instanced environment without more information!") — `AddCard` then keyed the interior with no parent chain, the same malformed-`EnvironmentsData`-key corruption as the original report, just on the way back in instead of on the way out.
  - Added `GameQuery.CurrentEnvironmentStringDictionaryKey`, reading `EnvID.StringDictionnaryKey` (not just the bare `MainEnvCard.UniqueID` `CurrentEnvironmentUniqueId` already reads) — identical to the UID for a non-instanced environment, but additionally encodes the `ParentEnvs` chain for an instanced one.
  - `StartEnvironmentTravel` now records this full key (`_returnEnvKeyByArrival`, renamed from `_returnEnvByArrival`) instead of the bare UID.
  - `StartReturnTravel` reconstructs the departure `EnvID` via `new EnvID(recordedKey)` — the exact string-key round-trip vanilla itself uses for `CurrentEnvironmentKey` at save/load — instead of looking up a UID and rebuilding from scratch, and bails via the existing "can't return yet" popup if the key fails to resolve.
  - `PrepareNextEnvironment` gained an optional `presetEnvId` parameter so this already-correct `EnvID` (complete with its restored `ParentEnvs` chain) is applied to `GameManager.NextEnvironment` verbatim on the return leg, instead of being rebuilt from the bare card (which is what nulled it in the first place). The outbound leg (traveling TO a registered mod world, always non-instanced) is unaffected — it still builds a fresh `EnvID` per-destination exactly as before.
  - Built clean (0 warnings/errors); not yet verified in-game (no post-2.25.14 portal trip from an instanced interior has been logged).

## [2.25.13] — 2026-08-24

### Fixed

- **The Portal Hub System's "Return to Portal" exit card (`csffmfw_hub_exit`) could appear in a save that never built or used a Portal Hub at all** (player report 2026-08-24: "the exit appeared in a save where I never had or used a portal"). Root cause: `PortalService.InjectExitCardsIntoModHubs` unconditionally seeded the exit card into EVERY registered mod world's `DefaultEnvCardDrops` at load time (baked into a board's first-ever generation regardless of how the player got there), and the mid-game safety net `EnsureHubExitOnArrival` re-spawned it on ANY visit to a registered world — including a fresh save loaded already standing in a mod's home-base world, or simply walking in normally. Neither path checked whether the player had actually traveled there via a Portal Hub.
  - Removed `InjectExitCardsIntoModHubs` and its `DefaultEnvCardDrops`/`HubPortalInjector` seeding entirely — the exit is no longer baked into any world's default board content.
  - `EnsureHubExitOnArrival` now only spawns the exit when the current environment has a recorded entry in the same session-scoped `_returnEnvByArrival` dictionary that `StartEnvironmentTravel(recordReturn:true)` writes the instant the player clicks an outbound "Travel to [World]" button — i.e. only when this specific arrival was the actual result of a portal trip this session. This mirrors the gate `StartReturnTravel` already used to decide where the button sends the player back, so the exit now only ever exists where clicking it would actually do something.
  - The exit remains a temporary, decaying card (unchanged from 2.25.9): `SpoilageTime` despawns it 24 in-game hours after it spawns. Any exit already baked into an existing save from a prior version will decay away on its own via that same timer — no forced removal needed.
  - Removed the now-dead `WorldMapInjector.IsCloneEnvNode`/`CloneNodeHasOwnExit` helpers and `Injection/HubPortalInjector.cs`, which existed solely to support the removed unconditional seeding. Built clean (0 warnings/errors); not yet re-verified in-game.

## [2.25.11] — 2026-08-24

### Fixed

- **`TrackingTimingDiagnostics` (`[Diagnostics] LogTrackTiming`, the tool armed to investigate `thicketpine-north-loadtimes-2026-08-24`) never once logged a `CheckForTracks` line, despite reporting itself "enabled" and despite `ChangeEnvironment` logging normally on every travel.** Root cause: `GameManager.EnvironmentsData` is `Dictionary<EnvDictKey, EnvironmentSaveDataByReference>` — the diagnostic patched `EnvironmentSaveData.CheckForTracks` instead, a same-named, same-signature but otherwise unrelated class (no shared base type) that live gameplay never calls. `SafePatcher.TryPatch` reported success because it found and patched a real method — just the wrong class — so the gap was completely silent (no warning, no error) until a fresh 2026-08-24 diagnostic session logged 10 `ChangeEnvironment` entries and 0 `CheckForTracks` entries back to back. Retargeted the patch at `EnvironmentSaveDataByReference`. Also tagged both `CheckForTracks` and `ChangeEnvironment` log lines with the destination environment's `EnvDictKey`/`DictionaryKey` (mirroring `CatchUpTickCap`'s proven `__instance.NextEnvironment` read) — the prior log format had no way to attribute any entry to a specific environment unless it happened to also trip `CatchUpTickCap`'s own separate log line, which was the reason the first diagnostic capture couldn't be conclusively read. Built clean (0 warnings/errors); not yet re-verified against a fresh play session.

## [2.25.10] — 2026-08-24

### Fixed

- **WorldMap clone environments (e.g. CMC's map tiles) could show doubled forage terrain — two Ponds, two Pine Trees, two Small Pine Trees, etc. on the same board** (player report: "across all of the CMC map tiles we have double trees and ponds"). Root cause: all three of those vanilla cards are `UniqueOnBoard:true`, and vanilla's own `GameManager.CheckForMissingDefaultCardsInEnv` (fires on every environment arrival) re-adds any `UniqueOnBoard` default-drop card whose location check reads as "missing" — its early-out relies on `GetExplorableCard` matching the CT8 by `EnvID`, the same environment-identity machinery already documented elsewhere in this class as fragile for clone envs. When that early-out mis-fires once (first visit), a second, independently-tagged copy of every `UniqueOnBoard` default drop gets added; because the fresh copy is correctly tagged, later visits pass the check and no further copies accumulate, leaving the board permanently at exactly double. Added `WorldMapInjector.TrimExcessDefaultDrops`, run once per boot for every clone node: caps each of the node's own `DefaultEnvCardDrops` UIDs (post-clone, post-`ExtraDropUIDs`-append) at its declared max quantity, removing only the excess copies and leaving everything else on the board (improvements, player-dropped items, other terrain) untouched — heals existing contaminated saves and self-corrects on every future boot regardless of what triggers the over-count. Built clean; not yet verified in-game.

## [2.25.9] — 2026-08-24

### Added

- **The Portal Hub System's "Return to Portal" exit card (`csffmfw_hub_exit`) now despawns 24 in-game hours (96 daytime points) after it appears, instead of sitting on the board indefinitely.** Added a `SpoilageTime` decay stat (`RatePerDaytimePoint: -1`, `HasActionOnZero` → destroy) to the card's JSON, covering the normal `DefaultEnvCardDrops` board-seed spawn path. The mid-game recovery spawn path (`PortalService.EnsureHubExitOnArrival` → `SpawnService.Spawn`) goes through `GameManager.GiveCard`, which is confirmed in this codebase to sometimes leave a spawned card's durability stats at 0 regardless of its JSON default (companion/perk-spawn precedent) — a 0-init here would have self-destructed the exit on the very next decay tick instead of after 24 hours. Fixed by explicitly re-initializing `SpoilageTime` to 96 immediately after spawn, synchronously (before any DTP tick can evaluate the zero-check), via `CardUtil.SetDurability`. A diagnostic log line was also added for the "already present" branch to confirm the board-seed path is behaving correctly. Built clean; not yet verified in-game.

## [2.25.8] — 2026-08-24

### Fixed

- **The "Return to Portal" exit card silently did nothing when clicked with no player-facing feedback of any kind.** `PortalService._returnEnvByArrival` is a session-scoped dictionary that only records a return trip when the player clicks an outbound "Travel to [World]" button from a physically-built Portal Hub; it is never persisted and starts empty every session. A player standing in a registered mod world (e.g. their home base) who had NOT yet used an outbound Portal Hub trip this session — including immediately after loading a save while already there — would see the "Return to Portal" exit card do nothing at all on click: the no-op was previously logged only via `Log.Warn`, invisible in-game (2026-08-24 player report: "tried to use the portal exit but it won't work"). Fixed: both no-op branches of `StartReturnTravel` now also show `GraphicsManager.Instance.MessagePopup` — the same "can't do that right now" mechanism vanilla itself uses — explaining that the exit only remembers a return trip after traveling OUT through a placed Portal Hub this session. Does not change the underlying session-only tracking (that remains the documented, deliberate design), only closes the silent-failure gap.

## [2.25.7] — 2026-08-24

### Added

- **`Reflect.IsAlive(object)`** — validates a cached reflection-held reference (e.g. a cached `InGameNPC`) without the Unity-destroyed-object pitfall (a destroyed `UnityEngine.Object` is NOT C#-null under a bare `object`-typed `!= null` check — see CLAUDE.md §Harmony Patching Pitfalls). Added to support content mods caching a resolved NPC reference across ticks instead of re-scanning `GameManager.AllNPCs` every poll; first consumer: `Community_Mod_Chest` 1.68.1's NPC scheduler performance pass.

## [2.25.6] — 2026-08-24

### Fixed

- **Portal Hub travel (both the outbound "Travel to [World]" buttons and the "Return to Portal" exit) could leave two environments' boards rendered simultaneously ("map overlap" in the top-left corner) and, in one report, left unrelated board state corrupted after a later save/reload (a player's own home structure vanished, and the Village became unenterable again).** Confirmed via decompile: every vanilla travel call site (`GameManager.ProduceCards`'s `TravelToPreviousEnv`/`TravelTarget` handling, and the `ExplicitTravelToEnv` path) wholesale-reassigns `GameManager.NextEnvironment = travel` — a freshly built `EnvID` — immediately before adding the destination environment card. `PortalService`'s two reflection-based travel paths (`TryStartAddCardFromSource`, and the `GiveCard` fallback it shares the bug with) skipped that step and relied entirely on `AddCard`'s own internal `NextEnvironment.SetMainEnvCard(_Data)`, which only overwrites the `MainEnvCard` half of the struct — `NextEnvironment.ParentEnvs` (and its cached `EnvDictKey`, which factors `ParentEnvs` into the lookup key regardless of whether the destination is itself instanced) was left stale from whatever environment the player had most recently really transitioned through. A stale parent-chain riding into the new `NextEnvironment` produces a lookup key that doesn't match the destination's real persisted board, which is consistent with both the transient double-render and the deeper corruption. Fixed: both reflection paths now reset `GameManager.NextEnvironment` to a fresh `EnvID` for the destination (mirroring vanilla) before invoking `AddCard`/`GiveCard`. Built clean; not yet re-verified in-game — see `Documentation/Retrospectives/portal-hub-env-overlap-2026-08-24.md`.

## [2.25.5] — 2026-08-24

### Fixed

- **`BlueprintContainerSaveLoadFix.RestoreModBlueprintStates` mapped researched/purchasable blueprint state backwards.** `BlueprintModelState` has no `Purchased`/`Researched` member (vanilla: `Available, Purchasable, Locked, Hidden` — confirmed via `GameManager.SetBpAvailable`/the `PurchasableBlueprintCards` branch). The fallback code tried to parse those nonexistent names and always fell through to an int fallback of `2` = `Locked` for UIDs found in `FinishedBlueprintResearch`/`ResearchedBlueprintCards` (i.e., blueprints the player had actually finished researching), while UIDs only in `PurchasableBlueprintCards`/`AvailableBlueprintCards` (visible but not yet researched) got mapped to the real `Available` value. Net effect, whenever this retroactive backfill pass actually had to write a state (primary `FinishInitializing` restore missed it): a genuinely-researched mod blueprint would re-lock on load, and an unresearched-but-visible one would appear fully unlocked — exactly the "blueprint research resets" failure class this subsystem exists to prevent. Latent in the reproduction log that surfaced it (0 UIDs hit the researched branch that session), but a live bug in shared framework code affecting every mod with blueprints. Fixed: map researched UIDs to `Available` and purchasable-only UIDs to `Purchasable`, using the real enum member names directly.
- **Third-party mods iterating `NPCAction.NPCStatModifications` unguarded could NRE.** Same defect class as the existing `DroppedCards` fix (no C# field initializer + no JSON block → hard `null` after `JsonUtility.FromJsonOverwrite`), different field, different consumer: WikiMod 3.3.1's `EnvironmentManager.ApplyNPCRespawnMultiplier` does `action.NPCStatModifications.Length` with no null-guard, caught by WikiMod's own safe-call wrapper (non-fatal) but logging a `[Error : WikiMod]` for every affected NPCAgent on every load. Confirmed via `ilspycmd` decompile of the installed `WikiMod.dll` against a player's `LogOutput.log`, which showed the crash for CMC's Apothecary/InnKeeper/Miller/Professor/Weaver and Sirus23's WildOwl — all 33 of their `AgentActions` entries genuinely omit `NPCStatModifications` (nothing to lose; a plain empty-array backfill is correct). `NPCActionDroppedCardsRepair` now also backfills this field to `Array.Empty<NPCStatInstantModifier>()` when null.

## [2.25.4] — 2026-08-23

### Fixed — Animal Modding System: two `EncounterBuilder` bugs reported by a Sirus23 player

- **A Ref-path species with no manifest `Encounter` section had its own hand-authored Approach/combat encounter silently replaced by the vanilla duck.** `EncounterBuilder.Resolve`'s last-resort fallback (`Combat_EncounterDuck`) triggered any time `ApproachButton` was at its schema default (`true`) and no `Encounter.Ref`/generation fields were authored — including for a `Agent.Ref` species that already has its own correct encounter wired at the agent-JSON level. `AnimalAssetFactory.ApplyApproachButton` then unconditionally overwrote the agent's real Approach button with this duck fallback. Symptom (reported by a player, confirmed against Sirus23's `Animals/Fox.json`, which has no `Encounter` section): fighting the tamable Wild Fox showed the vanilla duck, and the fox could never actually be killed — the duck encounter's `EnemyDefeatedEffects` targets the vanilla duck's own NPCStat, not `wildfox_stat_blood`, so the real agent's Blood/Exists never reached zero. Fixed: `Resolve` now leaves a Ref-path agent's own encounter untouched (returns `null`) when the manifest authors no `Encounter.Ref`/generation — the duck fallback is reserved for the fully-generated-agent path, which has no encounter of its own to fall back on.
- **The generated-Encounter path never set `Encounter.EncounterImage`**, unlike a hand-authored Encounter JSON (which always carries `EncounterImageWarpData`) — the combat popup rendered a blank enemy icon for any species on the generated path. Symptom (reported by a player): Sirus23's Wild Owl (the only species currently using generation) is missing its combat icon. Fixed: `EncounterBuilder.BuildGenerated` now resolves an `EncounterImage`, reusing the species' own portrait sprite (`m.Sprite` on the fully-generated path, or the Ref-path agent's already-resolved `AgentImage`).

## [2.25.3] — 2026-08-22

### Fixed

- **`SealableGateService.IsChallengeCardCleared` permanently softlocked any MultiHit durability gate whose final hit didn't land on an exact `0.0` float.** Repeated `-1.0` `UsageChange` hits (e.g. 3.0 → 2.0 → 1.0 → 0.0 over three separate action calls) accumulate floating-point rounding error — confirmed via diagnostic logging on a real player's Snow Drift dig: the third hit left `CurrentUsageDurability` at `1.735985E-06`, a tiny positive residue, not exactly zero. The check was `<= 0f`, so the gate never registered the challenge card as cleared even though it was visually/functionally empty — the associated road/passage stayed locked forever, no matter how many times the player dug/chopped. Changed to a small epsilon tolerance (`<= 0.01f`). This is a shared helper — every MultiHit `SealableGates` entry in the fleet (ACT's collapsed-wall salt/copper/iron/tin/quarry gates, CMC's Deadfall and Snow Drift gates) carried the same latent bug; this fixes all of them at once, not just the one that got caught.
- **Added a poll-driven self-healing backstop for MultiHit marker gates** so a card already stuck at a near-zero (but not `<= 0f`) durability reading from the bug above reopens its gate automatically on the next 1-second poll tick, without requiring the player to perform one more action on it. Deliberately does not treat "challenge card not found" as cleared during the poll (only an actual low-durability reading counts) — `GameQuery.CardsInPlayerEnv()` only sees the player's current board, so "not found" during an unrelated tick usually just means the player walked away, not that the card was destroyed.

## [2.25.2] — 2026-08-17

### Fixed — CRITICAL: Animal System `Encounter`/`Aggression` manifest section was never parsed

`Animals/AnimalSchema.cs`'s `Encounter` block only ever read `Ref`/`ApproachButton` from the JSON —
`BodyTemplate`, `Blood`, `Size`, `Awareness`/`Cover`/`Stealth`, `Passive`, `ForceFight`, and the
entire `Aggression` sub-block were silently discarded regardless of what a manifest specified (9
`CS0649` "field never assigned" compiler warnings on a clean rebuild confirmed zero read sites for
any of them anywhere in the framework). Net effect: `Encounter` *generation* could never activate
from any manifest, and the `Aggression` scheduled-attack duty could never attach — both silently,
because the log lines documenting each "skip" case are themselves gated on the same always-false
flags. Every manifest requesting encounter generation (the Owl, as of its M5 conversion) fell back
to the vanilla `Combat_EncounterDuck` instead. Added the missing parse block; framework rebuilds
clean (0 warnings, was 9). Found via a routine rebuild during the M3–M6 acceptance/polish pass, before
any in-game testing — see `Documentation/Plans/CSFFModFramework/Animal_System_Plan.md` § M5.

### Changed — Animal System log-level rebalancing

Diagnostics now track verification status again: promoted M5/M6 attempt-time lines
(`CompanionService`, `TameInteractionBuilder`, `EncounterBuilder`) from `LogDebug` to `LogInfo` (these
milestones remain unverified in-game — matches the convention M3/M4 already followed); demoted
confirmed-in-game M0/M2 lines (`SpawnRegistrar`, `AnimalLifecycleTicker`) from `LogInfo` to `LogDebug`
(verified since 2026-07-11/21).

### Added

- `Documentation/CSFF_Patterns.md` § Adding a Roaming Animal — schema overview, the `Ref`/
  `CustomDuties` escape hatches, and cross-milestone debugging-cycle invariants, closing the
  cookbook's remaining front-matter gap (M3–M6 subsections already existed).
- README.md § Declarative Animal System — the framework's own README previously had no section
  describing this capability at all despite it being complete through M6.

### Tooling

- `Development_Tools/Audit-Mod-Preflight.ps1` now scans a content mod's `Animals/*.json` for
  `Card`/`GiveCard` producer references (trap catch results, carcass drops, tame companions) — closes
  a false-positive class where the Animal System's framework-injected, cross-mod acquisition paths
  (e.g. a snare catch card mirrored into a vanilla trap's inventory at load time) were invisible to
  static per-mod JSON scanning and flagged CRITICAL "no acquisition path".

---

## [2.25.1] — 2026-08-16

### Fixed — Animal System M4 (traps), from an adversarial review of 2.24.0

Six defects, four of which made the trap loop non-functional rather than merely wrong. None would
have produced an error or a log line in play; the symptom would have been "nothing happened".

- **The generated feed duty could never be selected, so no trap could ever fire.** Duty selection
  is NOT weighted-random: `InGameNPC` sorts duties by weight descending and picks uniformly only
  among those tied at the very top (`InGameNPC.cs:2121-2144`). A feed duty weighted below a
  concurrently-selectable movement duty is therefore never chosen at all. `AnimalValidator` now
  rejects `Traps.Bait.DutyWeight` below the species' heaviest movement duty, and the shipped owl
  ties at 1e9 instead of sitting at 9e8.
- **A standalone trap-type reset action zeroed the stamp before the catch action read it.** On the
  springing tick the engine runs `CheckForActions()` twice — once right after the stat stamp
  (`GameManager.cs:4364`, reached because `InGameNPCStat.ApplyInstantModifier` schedules a sweep
  for any non-zero delta) and again only after `CurrentActionRequest` is assigned
  (`GameManager.cs:4396`). Any `Repeat` action is eligible in that first pass, so the reset won the
  race and every trap-type gate evaluated out of range. Vanilla ships the same pattern and has the
  same race, so mirroring it was not an option. The catch action now clears its own stamp inline,
  and a stamp left by a FAILED roll is cleared by `AnimalLifecycleTicker` on the framework tick,
  outside the engine's action sweep.
- **An all-zero-weight collection set degrades to a uniform lottery, not to "no drop".**
  `GameManager.cs:7594-7599` short-circuits `TotalValue == 0` to `Random.Range(0, length)`,
  ignoring all weights — so a missed gate silently randomised the catch card across every trap
  type. The fallback collection now carries a non-zero base weight (vanilla's carcass-base-100
  layout), making the out-of-range read deterministic.
- **`MoveTiming` defaulted to `MoveBeforeOtherEffects` on the catch action.** The agent was
  relocated to the Spirit World before the drop was computed, so the catch card was registered
  against the wrong environment while its container was still the trap. Now
  `MoveAfterOtherEffects`, matching every vanilla agent action that both drops and moves.
- **The derived trap-container tag set included a generic storage tag** carried by ~30 vanilla
  cards (Basket, ClayJar, ClayStoragePot, ClothSack, CookingPot, Shelf, …). Because the feed
  action destroys what it selects, a hungry animal would have deleted food out of the player's
  storage. The derivation now intersects the triggered traps as well and drops any tag carried by
  a non-trap card.
- **The catch action's respawn-timer refill was a structural no-op.** `AnimalLifecycleTicker`'s
  kill-respawn path keys on blood, which a trapped agent never loses, and forces the timer back to
  0 on the next tick. Removed; `AnimalValidator` now requires `Spawn.SuppressWhileCardOnBoard` on
  a trappable species, which is the only respawn path that keys on `exists`.

### Also

- `CannotPerformWhileInCombat` on the catch action set to false (vanilla's value) — true silently
  consumed the bait and produced nothing; `CollectionUses` matched to vanilla's `(0,0)`;
  `TriggerInteractors` joining now filters to the action whose `TriggerActionInAgent` actually
  routes the catch; catch cards are mirrored only after the species has successfully joined a trap
  and only for trap types it opted into; the `AgentTrapType` by-name lookup is cross-checked
  against the by-GUID instance the trap cards stamp; an author-declared `AgentTrapType` whose range
  is too small to hold the stamp is now an Error instead of a silent clamp; a feed duty running
  ungated by hunger is now a Warn instead of a suppressed Debug.

---

## [2.25.0] — 2026-08-16

### Added

- **`Animals/TameInteractionBuilder.cs` + `Animals/CompanionService.cs` (Animal System M6)** — a
  declarative `Interactions` section in an `Animals/<Species>.json` manifest now generates a
  skill-gated attempt-interaction (e.g. Tame) with no mod-side C#: `BaitCards`/`BaitTags` present
  compiles a drag-bait `CardOnCardAction` (`NPCAgent.DragAndDropActions`), otherwise a click-button
  `DismantleCardAction` (`NPCAgent.DismantleActions`, `AlwaysShow: true` set automatically). Success
  and fail are resolved entirely by the engine's own native weighted `CardsDropCollection` selection
  over `ProducedCards` — a Success collection with an optional skill-scaled
  `StatsDropChanceModifiers` bonus (saturating, not collapsing, past the configured skill cap) vs.
  Fail collections, one of which can carry `DroppedEncounter` to turn `OnFail.AttackChance`% of
  fails into a real fight. No custom RNG anywhere in the builder.
- **`Companion` schema section** — paired with `Interactions[].OnSuccess.GiveCard`, drives a new
  `CompanionService` that watches for a successful roll (via `Api.ActionRouter`
  `AfterWrapped`, `CardPredicate`-matched against the species' live `InGameNPC.AssociatedCard`) to
  retire the wild agent (synchronous `GameManager.MoveNPC`, closing the Attempt-12
  interactivity gap) and init the freshly-spawned companion's zeroed durability stats to full
  (`GameManager.GiveCard` is `void`, so no spawn-time override can otherwise apply). An optional
  `Flee` reaction relocates the wild agent on any failed attempt. Author guide:
  `Documentation/CSFF_Patterns.md` § Adding a Roaming Animal → Tame + companion.
- Sirus23's Wild Owl now drives its entire tame/companion flow from `Animals/Owl.json` — the
  hand-authored "Attempt to Tame" `DragAndDropAction` in `NPCAgent/Agent_WildOwl.json` and the
  owl-specific `WildOwlLifecyclePatch.cs`/`CompanionHuntPatch.cs` C# (tame retirement + stat init)
  are gone; zero owl-specific mod C# remains for tame/companion.

---

## [2.24.0] — 2026-08-16

### Added

- **`Animals/TrapIntegrator.cs` (Animal System M4)** — a declarative `Traps` section in an
  `Animals/<Species>.json` manifest now makes a modded animal catchable by the four vanilla land
  traps, with no mod-side C#. The framework stamps the six trap NPCStats onto the agent, generates
  its `"Interact with a Trap"` catch action (alive-vs-carcass chosen by weighted collections gated
  on the `AgentTrapType` stamp) plus a paired trap-type reset action, mirrors the catch cards into
  the Triggered traps' inventory filters, and generates a feed duty so the animal actually takes
  bait. Author guide: `Documentation/CSFF_Patterns.md` § Adding a Roaming Animal → Traps + bait.
- **`AffectItems` duty-action type** — un-blocked in `AnimalValidator` and compiled by
  `DutyBuilder` into a real `AffectItemsDutyAction` (`SimpleCardChange` + `Destroy`, v1). Destroying
  bait is what raises the `RemoveItemFromInventory` trigger the vanilla traps listen for.

### Notes on the shared-vanilla mutation

`TrapIntegrator` is the only place the animal system writes to shared vanilla data. Every write is
append-only and idempotent by object identity, scoped to just the trap types a manifest opts into
(an unlisted or immune type is never touched), and fail-soft per trap. Tag identities — the
trap-container tag and the bait pool — are derived off the LIVE trap cards at load rather than by
name: obfuscated export names are not stable across game versions (the container tag recorded as
`Image_7173` under EA 0.65h does not exist in 0.66h). If derivation fails the phase logs one Error
and skips traps rather than throwing.

### Corrections to prior research (verified against EA 0.66h)

- There are **six** trap NPCStats, not seven: `AgentTrapType`, `AgentTrapCunning`, and four
  per-type variants.
- `AgentTrapType`'s own in-game doc string is wrong for two traps — the cards stamp LogTrap **30**
  and PitTrap **40**, the reverse of what the stat describes. Card values are authoritative.
- The catch-card filter mirror is **required, not precautionary**: the drop path checks the
  destination container (`NPCAction.ToAction` → `GameManager.AddCard` → `GetIndexForInventory` →
  `CanReceiveInInventory` → `CompleteInventoryFilter.SupportsCard`). An unmirrored catch card
  silently lands on the board instead of in the trap — vanilla itself has this bug with
  `PartridgeTiedMale`.

### Known gap

Feeding consumes bait but does not reset `AgentSatiation`: the `SimpleCardChange` path builds a
throwaway "Consume" `CardAction` carrying only `ReceivingCardChanges`, so it has no NPC-stat hook.
Deferred design decision — see the M4 section of
`Documentation/Plans/CSFFModFramework/Animal_System_Plan.md`.

---

## [2.23.7] — 2026-08-16

### Added

- **`ModifierPackageInjector`** (`Injection/ModifierPackageInjector.cs`) — standalone,
  character-independent activation for `GameModifierPackage`. A mod ships `Modifiers.json` in its
  root (`{ "AutoApplyPackages": ["<GameModifierPackage UID>"] }`) and the listed packages are
  applied to EVERY new game regardless of which character the player selects — the missing path for
  challenge / total-conversion mods, which previously had to ship a whole `PlayerCharacter` just to
  carry an `EasyPackage`. Implemented as a Harmony postfix on **`MainMenu.StartGame(int)`**, which
  is where vanilla creates and fills `GameManager.CurrentModifierPackages`
  (`.decomp/MainMenu.cs:1362-1363`) immediately before the new-game scene load; appending there
  rides vanilla's own one-shot application of `StartingStatModifiers` (inside
  `InitializeStatsAndActions()`, `GameManager.cs:2984`, called from `Awake` at `:2385` — not
  directly in `Awake`) and `AddedCards` (`InitializeModifierPackages`, `GameManager.cs:3466`,
  reached directly from `Awake`), plus the existing save
  round-trip, with no reimplementation. A paired prefix captures the list reference so the postfix
  fires exactly once — `StartGame` is a multi-step state machine that returns early on every pass
  but the last. Packages already present (e.g. the character's own `EasyPackage`) are never
  double-added. Adds the `HasModifiers` mod-manifest flag (also folded into
  `HasFrameworkOnlyMarkers` for ModLoader coexistence detection, consistent with every other
  framework-exclusive declarative file). Self-no-ops (zero patch installed) when no mod ships a
  resolvable `Modifiers.json`. **In-game unverified** — no mod ships `Modifiers.json` yet.

### Fixed (documentation)

- **Cookbook coverage for both `GameModifierPackage` activation paths** —
  `Documentation/CSFF_Patterns.md` gained "GameModifierPackage — character-linked (existing, no code
  needed)" and "GameModifierPackage — standalone auto-apply (`Modifiers.json`)". The character-linked
  path (`PlayerCharacter.EasyPackageWarpData` → Easy Package toggle at character creation) was fully
  functional before this release and needed no framework code — the gap was purely that it had never
  been documented or exercised. The new sections trace both paths end to end (load → selection →
  `MainMenu.StartGame` → `GameManager.Awake`'s one-shot apply → save round-trip) and state the
  auto-apply path's boundaries: new games only, no player opt-in/opt-out UI, additive with the
  character-linked path.
- **Adversarial review pass (same day)** caught and fixed: an unresolvable `"Stone"` vanilla card
  reference in the cookbook's only worked example (root CLAUDE.md §Vanilla Item References — vanilla
  refs need the GUID, not a human-readable name; corrected to the real `StoneSmall` GUID), a
  mis-cited `MainMenu` method name, an incomplete `StartingStatModifiers` gate description, and a
  README section self-contradicting the new cookbook content. See
  `Documentation/Plans/CSFFModFramework/Audit_Remediation_Plan.md`'s Promotion Log for the full list.
  (That plan was retired 2026-09-11; its closure record is now
  `Documentation/Design/CSFFModFramework_Audit_Remediation_As_Built.md`, and the full original text
  stays reachable via `git log --follow` on the old path.)

---

## [2.23.6] — 2026-08-16

### Added

- **`FlavourMatrixInjector`** (`Injection/FlavourMatrixInjector.cs`) — activates
  `FlavourSystemRules.FlavourMatrix`, the vanilla flavour-synergy pairwise table, which was
  unreachable from JSON: `FlavourSystemRules` is a plain scene-scoped `ScriptableObject` (not
  `UniqueIDScriptable`, not in `DataBase.AllData` at load time), reached only through the single
  Inspector-wired `GameManager.FlavourRules` field — the same class of problem as `StatListTab`
  (root CLAUDE.md §GameSourceModify). A new `GameManager.InitializeStatsAndActions` Harmony
  postfix (mirroring `Community_Mod_Chest/Patcher/StatTabInjectionPatch.cs`) resolves declarative
  `<ModFolder>/FlavourMatrix/*.json` files (one synergy pair per file: `TagA`/`TagB` FlavourTag
  UIDs + a `Synergy` value) once at load time and appends each pair idempotently every boot.
  Self-no-ops (zero patch installed) when no mod ships `FlavourMatrix/*.json`. Adds the
  `HasFlavourMatrix` mod-manifest flag (also folded into `HasFrameworkOnlyMarkers` for ModLoader
  coexistence detection, consistent with every other framework-exclusive declarative file).

### Fixed (documentation)

- **Cookbook coverage for four previously-undocumented loaded-but-dormant SO types** —
  `Documentation/CSFF_Patterns.md` gained "Shipping a CookingRecipeGroup", "Shipping a
  BookmarkGroup", "Shipping a ConstructionCardGroup", and "Shipping a FlavourTag" (+ the
  FlavourMatrix synergy-pair subsection above). A ground-truth decomp trace found three of these
  four types (`CookingRecipeGroup`, `BookmarkGroup`, `ConstructionCardGroup`) **self-activate with
  zero injector code** — vanilla's own `GameManager.InitializeStatsAndActions()` already collects
  every loaded instance from `DataBase.AllData` (`.decomp/GameManager.cs:2693-2820`); only the
  FlavourMatrix synergy table (above) needed engine work. Closes the N1/N2 Near-Term ideas
  (`.audit/ideas.md` "From Deferred Specs" #2/#3) — see the dated resolution note there for the
  full finding.

---

## [2.23.5] — 2026-08-16

No dedicated entry was recorded when this version shipped (a concurrent session's Animal-system
work landed the same day, per `.audit/ideas.md`'s "v2.23.5 landed the same day" note) — left as a
placeholder rather than silently skipping the version number in this file's history.

---

## [2.23.4] — 2026-08-16

### Changed

- **Portal Hub diagnostics promoted `Log.Debug` → `Log.Info`** (investigating the still-open
  T2.63/T2.77 portal-return failures — two prior fix rounds retested FAIL in-game). Now visible
  in a normal player log: `PortalService.InjectExitCardsIntoModHubs` per-world skip/outcome
  lines, the `_returnEnvByArrival` record-on-travel write, hub-travel click + travel-path lines,
  exit-handler registration, and `HubPortalInjector.AppendCardDrop`'s idempotent-skip reason.
  The "Return to Portal" no-op warning now names the current env UID and the recorded arrival
  keys. `HubPortalInjector` also gained `Log.Warn` breadcrumbs on its three previously fully
  silent false-return paths (CardData type unresolved, `CardDrop.DroppedCard` field unresolved,
  `AppendCardDrop` reflection-guard). No behavior change — instrumentation only.

## [2.23.2] — 2026-08-15

### Added

- **Opt-in `BlueprintSearchDiagnostic`** (`[Diagnostics] LogBlueprintSearchMisses`, off by
  default) — logs, once per UID, why a mod blueprint that appears fine under its own
  crafting-journal tab is missing from the journal's Search box: no
  `GameManager.BlueprintModelStates` entry for the card instance, vs. not present in any
  `BlueprintTabs[].IncludedCards`. Added to investigate a 2026-08-15 fleet-wide report of this
  symptom; no root cause identified yet.

### Fixed

- **CardPresence `SealableGates` (H&F's forest-trail model) could permanently softlock a player
  who entered the gated area through the Portal Hub.** Sealed/open state was evaluated per
  `DirectionalGates` side against a single watch env, and the challenge card only seeded on the
  envs listed in `SeedOnEnvUIDs` — a portal player who landed behind the gate without ever
  visiting the seed env found every exit sealed and nothing anywhere to clear (unseeded
  deliberately defaults to sealed). Cleared state is now **gate-wide**: the gate is open while
  the `ClearedTransformInto` card is present at ANY of its watch/seed envs (owner env included),
  so one clearance from either side opens every side at once. Seeding now also skips while the
  gate is cleared anywhere, so a blocker card can no longer spawn next to an already-open path;
  once the cleared card's native regrowth timer transforms it back, the gate re-seals and
  seeding re-arms as before. Mods should list BOTH sides of the gated connection in
  `SeedOnEnvUIDs` so a player on either side always has a physical card to clear.

### Technical

- **Refreshed vanilla reference data to EA 0.66h.** Extracted
  `Documentation/GameData/CSFF-JsonData_EA_0-66h/` (28,859 files, +96 vs. 0.66g), regenerated
  `Resources/VanillaIds.json` from it, and repointed the `CSFF-JsonData_Current` alias.
  `lib/Assembly-CSharp.dll` refreshed to the live 0.66h binary and rebuilt clean (0 errors/0
  warnings). Patch-note review (Rain Cistern demolish, NPC sleep/travel UI fix, NPC clothing
  weight fix, NPC-worn indicator, Partner Enclosure cleaning, Cave Clean Duty, Quiver blueprint
  tab move) confirmed zero required mod JSON/C# changes.

## [2.23.1] — 2026-08-14

### Fixed

- **NPCDuty pathfinding (`MoveDutyAction`'s `MoveToPlayer`/`MoveToSpecificEnvironment`) could
  never route an NPC into, out of, or through ANY mod-injected WorldMap node, for the entire
  session** — root cause of CMC's long-open "recruited Partner never crosses into a mod WorldMap
  node" report (e.g. refusing to cross a freshly-built river bridge into village territory), and
  equally affecting every other mod's Duty-driven movement onto a modded node (e.g. WDI's
  Grinding Mill duty). `WorldMapData.MapDict` — the dictionary `WorldMapData.GetPathNonAlloc`'s
  A* search actually queries — is built once by `GameManager.FinishInitializing` calling
  `InitializeMapDictionnary()` EARLY (immediately after save data loads), purely from
  `WorldMapData.Environments` as it stood at that moment. `WorldMapInjector.InjectIntoWorldMap`
  (this framework's own node/edge injection) only runs LATE, at `GameManager.OnGMInitialized` —
  `WorldMapData` isn't loaded into memory any earlier — so every mod node landed in `Environments`
  AFTER the pathfinding lookup was already snapshotted without it. Player-driven travel was never
  affected (a travel DA click reads `CardData.DismantleActions`/`GetTravelDestination` directly,
  no A* involved), which is why the bug was invisible to every travel-DA-based playtest.
  `WorldMapInjector.RebuildPathfindingLookup` now re-runs `InitializeMapDictionnary()` immediately
  after node injection completes, then replays `LoadInstancedEnvironments` with the current save's
  data (instanced envs load earlier and aren't part of the base `Environments` list, so the rebuild
  would otherwise silently drop them — `LoadInstancedEnvironment` is itself idempotent, so the
  replay is side-effect-free). Runs once per process, exactly when needed: subsequent same-process
  game loads re-run vanilla's own `InitializeMapDictionnary()` against an `Environments` list that
  already carries the mod nodes permanently, so no further rebuild is necessary.

## [2.23.0] — 2026-08-14

### Fixed

- **A hand-built environment improvement now opens its `ImprovementBuilt`-gated map connection
  the moment the final construction stage completes** — no more walking to another tile and
  back (or waiting) before the new travel direction becomes clickable. Root cause was
  two-layered (Documentation/Retrospectives/river-bridge.md — CMC River Bridge → Village Path):
  1. `ConnectionGatePatch` only hooked `InGameCardBase.CompleteImprovement()`, which is the
     insta-complete path (perk pre-builds, the SpawnCard auto-complete queue). A player-built
     improvement advances through `InGameCardBase.SetBlueprintStage(int)`
     (`BlueprintConstructionPopup` → `IncreaseBlueprintStage`) and never calls
     `CompleteImprovement`, so no gate re-evaluation ever fired at hand-build completion. The
     patch now postfixes BOTH paths; the stage postfix also replays vanilla's own idempotent
     `GM.StartBuildingImprovement` registration so `CurrentlyBuiltImprovements` is persisted
     correctly at the exact completion moment (survives an immediate save/quit).
  2. `CardUtil.IsImprovementBuilt` read only the persisted `CurrentlyBuiltImprovements` list,
     whose vanilla semantics is "present / being built" (registered at construction START,
     re-synced only at `InGameCardBase.Init()` on env re-entry) — wrong in both directions:
     it could open a gate before construction finished, or keep it locked until the player
     left and re-entered. While the player is standing in the queried env, the live
     improvement card's `BlueprintData.CurrentStage >= BlueprintSteps` is now authoritative
     (every matching instance is checked, per the UniqueOnBoard duplicate-instance rule); the
     persisted list remains the fallback for non-current envs. Save-safe: improvements are
     exempt from `BlueprintSaveData`'s stage clamp, so a completed stage survives reload.
  Consumers inherit the corrected semantics: `ConnectionGateService`/`ConditionalDropService`
  `ImprovementBuilt` conditions and CMC's `ProfessorSchedulePatch` bridge check now mean
  "fully built", not "construction started".

## [2.22.6] — 2026-08-14

### Fixed

- **"Return to Portal" could send the player to the wrong environment after visiting two
  different mod hubs in the same session.** `PortalService`'s return-trip mechanism tracked the
  departure environment in a single shared `_returnEnvUid` static field, written on every
  outbound Portal Hub trip regardless of which mod hub the player was traveling to. A player who
  visited mod hub A, then mod hub B, then clicked the "Exit" card back in hub A got routed to
  hub B's departure point (whichever trip was most recent), not hub A's own departure point.
  Replaced with a `Dictionary<string, string>` keyed by arrival environment UID, so each mod
  hub's exit card now looks up its own recorded departure point independently. Found via a
  2026-08-13 playthrough report ("portal hub return button did not work as expected").
  Walk-in arrivals and post-reload sessions still show the pre-existing "no return recorded"
  no-op — that limitation is unchanged and by design (see `PortalService.cs` class doc).

## [2.22.5] — 2026-08-14

### Fixed

- **Load-time field init no longer throws ~8,000 caught `MissingMethodException`s per load.**
  `JsonDataLoader.CreateInstanceSafe` and `PassiveEffectNormalizer.InitializeNullFields` blindly
  called `Activator.CreateInstance` on every serializable class field, including game types with
  no parameterless constructor (`DurabilityStat`, `OptionalIntValue`, `DynamicLayoutSlot`, all
  `Optional*`) — each call threw, was caught, and left the field null, which is exactly what
  skipping does. Both init plans now probe for a parameterless ctor once per type
  (`ReflectionCache.HasParameterlessCtor`) and skip the impossible ones. Behavior is identical;
  the throw storm and its ~8,600 VerboseLogging breadcrumb lines (a measurable share of the
  `JsonDataLoader` phase time — ~1ms per logged line observed 2026-08-14) are gone.
- **`WorldMapInjector.ResolveDeferredCloneRefs` no longer costs seconds on large installs**
  (8.5s observed with the full mod suite, 2026-08-14). The clone-UID prefilter scanned the entire
  mod JSON corpus with `IndexOf(..., OrdinalIgnoreCase)` per clone UID — Mono's OrdinalIgnoreCase
  IndexOf is char-by-char, ~20× slower than Ordinal. Both sides are now lowered once and scanned
  Ordinal (UIDs are ASCII, so results are identical).
- **`WikiModQuickFindFix` no longer burns its 120-frame deferred retry on full all-assembly type
  scans when WikiMod isn't installed** (~240 scans + 240 VerboseLogging lines per load). Retries
  now short-circuit unless a new assembly has loaded since the last attempt — type lookups can
  only change when one does.

- **`WarpResolver.Lookup` could not resolve a `*WarpData` GUID into a field declared as a shared
  base type that holds a `UniqueIDScriptable` instance polymorphically at runtime** — e.g.
  vanilla `NPCDutyOrDutyTagRef.Target` is declared bare `ScriptableObject` (a union of
  `NPCDuty`/`NPCDutyTag`), not `NPCDuty` directly. The GUID-lookup branch was gated on
  `typeof(UniqueIDScriptable).IsAssignableFrom(targetType)`, which is false when `targetType` is
  the *parent* class rather than `UniqueIDScriptable` or a subtype — so a `CompatibleNPCDuties[].
  TargetWarpData` GUID fell straight through to the name-keyed lookup path and silently never
  resolved (zero log output beyond the generic unresolved-refs summary). `Lookup` now also tries
  `GameRegistry.GetByUid` for any `ScriptableObject`/`UnityEngine.Object`-typed field that isn't
  itself a `UniqueIDScriptable` subtype, before falling back to name-based resolution — a 32-char
  hex GUID never collides with a real SO `.name`, so this is purely additive. First surfaced by
  WaterDrivenInfrastructure 1.10.7's Forge/Workshop `PartnerDuty_Firekeeping` marking (see
  `Documentation/Plans/Fleet/Duties_Ownership_Plan.md`), caught by adversarial review before ship.

## [2.22.4] — 2026-08-14

### Fixed

- **Placing/building a card could still permanently lock every action with "I can't do two
  things at once...", even with the 2.20.6 `ChangeEnvironmentCrashGuard` in place.** That fix
  only covered `GameManager.ChangeEnvironment`'s call into `WorldMapData.AddInstancedEnv`, but
  vanilla has several other unguarded call sites — confirmed in the wild via
  `GameManager.ProduceCards` (the coroutine that spawns a card's crafted/built output),
  triggered by placing a Rain Cistern Kit. Same underlying vanilla bug: `AddInstancedEnv` throws
  an unhandled `ArgumentException` when two independently-registered instanced environments both
  compute the default map Coordinates `(0,0,0,0)` in one session, aborting whichever coroutine
  called it mid-flight and leaving `GameManager.RootAction` stuck forever. Added
  `Patching/BugFixes/AddInstancedEnvCrashGuard.cs`, a Harmony finalizer on `AddInstancedEnv`
  itself instead of on any one caller — covers `ChangeEnvironment`, `ProduceCards`, and every
  other current or future call path in one place, with the same graceful-skip outcome as the
  2.20.6 fix (the colliding env just doesn't get pre-registered that time).

## [2.22.3] — 2026-08-13

### Changed

- **Hardening: `Api.ActionRouter`'s wrapped-action coroutine can no longer strand the game in a
  permanent action-lock.** `RunWrapped` drove the wrapped game coroutine with a bare
  `while (original.MoveNext()) yield return ...` — if the underlying game action threw partway
  through, the exception propagated out of the wrapper uncaught, the same failure shape already
  fixed once for `GameManager.ChangeEnvironment` (`ChangeEnvironmentCrashGuard`, 2.20.6): the
  coroutine never reaches the point where the game clears `RootAction`, so `PerformingAction`
  stays true forever and every later action shows "I can't do two things at once..." with no
  recovery short of quitting. `RunWrapped` now steps `MoveNext()` manually with a try/catch
  outside the `yield` (required — C# iterators cannot `yield` inside a try block that has a
  `catch`), logs the route/action/card at `Error` level, and ends the coroutine gracefully
  instead of propagating. `After` handlers are skipped when the wrapped action didn't complete
  cleanly, since their contract assumes success. No known reproduction yet — added as defensive
  hardening while investigating a CMC player report of an unexplained "can't go anywhere" lock.

## [2.22.2] — 2026-08-12

### Fixed

- **`SpawnLocation > 0` ("outdoor only") spawn triggers still fired inside caves and building
  interiors.** `TriggerService`'s outdoor gating checked only `GameQuery.IsInInstancedEnvironment`,
  which covers player-built instanced structures (cabin, mud hut, cellar, enclosure, mine, coop)
  but not plain (non-instanced) CT4/CT8 boards tagged as caves or indoor spaces — walk-in caves and
  village-style building interiors. Reported via Sirus23's wild sheep/ram spawn triggers
  (`sh_tgr_wild_sheep_spawn`, `sh_tgr_wild_male_sheep_spawn`) still spawning underground and indoors.
  New `GameQuery.IsInIndoorOrCaveEnvironment` checks the current environment's `CardTags` against
  the known indoor/cave tag set (`tag_Cave`, `tag_EnvCaveSystem`, `tag_EnvIndoors`,
  `tag_Env_BearCave`, `tag_Env_WolfCave`); new `GameQuery.IsOutdoors` combines both checks.
  `TriggerService` now gates on `IsOutdoors` instead of `!IsInInstancedEnvironment` alone — fixes
  every mod's outdoor-only `CardData/Trigger/*.json` entry, not just Sirus23's.

## [2.22.1] — 2026-08-11

### Fixed

- **`ImprovementBuilt` connection gates (and SealableGates marker reads) silently stopped matching
  an environment once the player had left it.** `GameManager` re-adds an env's `EnvironmentsData`
  entry with a names-annotated `DictionaryKey` (`AddNamesToEnvKey` — e.g.
  `"2b19b942…(Env_River_ClearingOak_RiverClearing)"`) when the player leaves the env, but
  `CardUtil.IsImprovementBuilt`, `CardUtil.MarkImprovementBuilt`, and
  `SealableGateService.IsTargetEnvEntry` compared the bare env UID ordinally against that field
  (the entry's other match paths never fire: `EnvironmentID` doesn't exist on
  `EnvironmentSaveDataByReference`, and the dictionary is keyed by `EnvDictKey` structs, not
  strings). Result: a hand-built River Bridge recorded correctly in `CurrentlyBuiltImprovements`
  never unlocked the Community Mod Chest River Clearing → Village Path connection — the East
  compass slot stayed a red X with zero log output. The same mismatch made SealableGates marker
  *reads* (`IsMarkerSet`/`ReadMarkerDay`/`FindMarkerEntry`) miss save-loaded entries, a plausible
  contributor to the recurring "dug-open gates re-seal after reload" family. New
  `CardUtil.EnvKeyMatchesUid` normalizes the entry key with the engine's own
  `UniqueIDScriptable.RemoveNamesFromEnvKey` before comparing; all three call sites now use it.
  Perk-gated (`PerkEquipped`) connections were never affected, which is why the Village Pathfinder
  travel path passed earlier playthrough testing while the hand-build path had never worked.

## [2.22.0] — 2026-08-11

### Fixed

- **`AlwaysUpdateService` only skipped forcing `AlwaysUpdate=true` on mod-authored CT4/CT8 (env/
  explorable) cards; it never corrected one shipped with `AlwaysUpdate=true` in the first place.**
  A mod-authored env node with that flag true is `IndependentFromEnv`, so `ChangeEnvironment`
  re-homes it onto the player instead of leaving it behind — the same travel-softlock precondition
  `CardCloneService` already force-corrects for cloned nodes at clone time (Community Mod Chest
  shipped 7 interior CT8 locations with `AlwaysUpdate:true` for a month before the data was
  hand-fixed). `AlwaysUpdateService.EnableAll` now actively forces `AlwaysUpdate=false` on any
  mod-authored CT4/CT8 card found with it true, logging one `LogInfo` per corrected card so a
  mis-authored env node is visible at load. Vanilla cards and non-env mod cards are unaffected.
- **`PortalService` could strand a player with no way back after teleporting to a clone-env world
  that is also a registered portal destination.** The hub-exit skip for clone-env worlds (added so
  ACT's mining caves, which carry their own map-travel exit, don't get a redundant return card) applied
  to every clone env, including Community Mod Chest's `cmcEnvVillage` — a `MapMod.json` portal
  destination whose only route back toward vanilla is gated behind the river bridge or a trait perk.
  New `WorldMapInjector.CloneNodeHasOwnExit` distinguishes clone envs that already have their own
  authored way out (a `VanillaExits` compass exit, or a `Connections` entry to a non-clone
  environment) from clone envs whose only connections lead to sibling clone nodes. `PortalService`
  now seeds the `csffmfw_hub_exit` return card only into the latter. ACT's mining-cave portal
  behavior is unchanged — it already has its own exit and is unaffected by the narrowed skip.

## [2.21.3] — 2026-08-09

### Fixed

- **`WorldMapInjector.PreCreateCloneEnvSaveData`'s old-save reseed logic no longer discards
  `CurrentlyBuiltImprovements` when it force-removes a clone env's `EnvironmentsData` entry.**
  That field is where `SealableGateService`'s Marker model stores "cleared" state for every gate
  whose `MarkerEnvUID` is the env being reseeded (e.g. ACT's Tin Cave hub, which owns the marker
  for the Copper/Iron/Tin/Quarry cave-in walls). Three of the function's four contamination
  heuristics — the zero-card check, the stale-Exit-card check, and the `StripLegacyBoardUIDs`
  check — removed and recreated the entry outright with no awareness of that field, silently
  un-marking every already-dug passage sharing the hub and reproducing "cave connections/tunnels
  collapse again after reload" even on saves where the 2.21.1-era `GetSealableGateSlack` guard
  (which only covered the fourth, count-based heuristic) had never fired. Fix snapshots
  `CurrentlyBuiltImprovements` before any removal and restores it onto the recreated entry,
  independent of which heuristic triggered the reseed.

## [2.21.2] — 2026-08-09

### Fixed

- **`ModManifest.HasFrameworkOnlyMarkers` now recognizes a `GameSourceModify/` bulk-match patch
  (`MatchTagWarpData`/`MatchTypeWarpData`) as framework-exclusive content.** A `ModLoaderVerison`-tagged
  mod using this framework extension — real Pikachu ModLoader/ModCore's own GameSourceModify only
  supports single-UID targeting — was being skipped by `ModDiscovery`'s coexistence check whenever an
  actual Pikachu loader was also installed, on the assumption that loader owned it. Reported via a
  Nexus comment: a third-party freshness/spoilage-rate mod's `tag_Preservable`-wide patch only ever
  affected vanilla items, never any content-mod item, because it was never loaded by the framework at
  all in that configuration. The detection flag (`HasGSMTagOrTypeMatch`) already existed for an
  unrelated load-order optimization but was never wired into the reclaim check.

## [2.21.1] — 2026-08-09

### Added

- **New `SealTrigger`/`GateConditions` type `"Always"`** — unconditionally true, for a `SealableGates`
  entry that must be sealed by default for every player rather than only once a perk is equipped.
  Added because ACT's cave walls and H&F's forest trail were gated on `PerkEquipped`, which meant
  the passage was silently wide open for any player (including on an existing save that installs
  the mod) who never took the associated perk — the perk was never actually required to dig through
  the wall (that's tool-tag gated on the `CardInteraction` itself), only to make the wall exist at
  all. `Documentation/CSFF_Reference.md` §WarpData/gate condition table unaffected — see
  `Documentation/CSFF_Map_Travel_System.md`.

## [2.21.0] — 2026-08-09

### Added

- **`CatchUpTickCap` performance patch — fixes the long "Not Responding" freeze when traveling
  to a location not visited in a long time on old saves.** Vanilla `GameManager.ChangeEnvironment`
  replays its per-game-tick simulation step (`ApplyRates`) once for every tick (15 in-game
  minutes) elapsed since the destination environment was last visited, with no upper bound, in a
  single synchronous frame. Measured on a Year-4 save via `TrackingTimingDiagnostics`: a location
  ~405 in-game days stale replayed 38,891 catch-up ticks at ~1.9 ms each — a 73-second hard
  freeze on one travel (CPU scaled linearly with tick count; card count was flat across fast and
  slow travels, ruling out the load/classification passes). New Harmony prefix on
  `ChangeEnvironment` clamps the destination's `LastUpdatedTick` so at most
  `[Performance] CatchUpTickCap` ticks are re-simulated (default `1344` = 14 in-game days;
  `0` restores vanilla unbounded behavior). Elapsed time beyond the cap is skipped, not
  simulated — plant/tree growth is unaffected (vanilla jumps card-attached counters straight to
  the live value on the first catch-up tick); only rate-driven decay beyond the 14-day window is
  lost, and perishables fully spoil well within it. Logs one Info line whenever the cap engages.

## [2.20.7] — 2026-08-08

### Fixed

- **The Portal Hub System's "Return to Portal" button never appeared inside any registered mod
  hub** (CMC Village, ACT Metal Mines, H&F Foraging Forest). `csffmfw_hub_exit.json`'s Exit DA
  used vanilla `TravelToPreviousEnv: true`, which only produces cards when the CURRENT
  environment has a populated `ParentEnvs` chain (i.e. is an instanced env reached by an actual
  travel transition) — every registered hub destination is a normal non-instanced WorldMap node,
  so `CardAction.WillProduceCards()` always evaluated false there, and with `DaytimeCost: 0` and
  no other qualifying field, `WillHaveAnEffect()` also returned false — the button was silently
  invisible, zero log output, on every single portal trip since the feature shipped. Fixed by
  tracking the player's environment explicitly: `PortalService.StartEnvironmentTravel` now
  records the departure env UID before each outbound "Travel to [WorldName]" trip
  (`_returnEnvUid`, session-scoped), and a new `RegisterHubExitHandler` / `StartReturnTravel`
  drives the return trip through the same env-travel mechanism when "Return to Portal" is
  clicked. `csffmfw_hub_exit.json` now uses `AlwaysShow: true` instead of the non-functional
  `TravelToPreviousEnv`.

## [2.20.6] — 2026-08-08

### Fixed

- **Entering/exiting a building could permanently lock every action with "I can't do two
  things at once..." until the game was fully restarted.** Vanilla `WorldMapData.AddInstancedEnv`
  throws an unhandled `ArgumentException` when two independently-registered instanced
  environments (e.g. a player-built cabin/mine/mud-hut construction site) both compute the
  default map Coordinates `(0,0,0,0)` in the same session. `GameManager.ChangeEnvironment`
  calls `AddInstancedEnv` synchronously from its own coroutine, so the unhandled throw aborted
  `ChangeEnvironment` mid-flight and never reached the code that clears `GameManager.RootAction`
  — leaving the action-lock (`GameManager.PerformingAction`) stuck `true` forever. Added a
  Harmony finalizer (`Patching/BugFixes/ChangeEnvironmentCrashGuard.cs`) on
  `ChangeEnvironment`'s MoveNext that swallows this specific exception and logs full diagnostics,
  so the coroutine ends gracefully (the colliding env just doesn't get pre-registered that time,
  same graceful-skip vanilla already uses elsewhere in `WorldMapData`) instead of permanently
  locking the player out of every action.

## [2.20.5] — 2026-08-07

### Fixed

- **Mod-injected localization (CMC, etc.) stayed in Chinese even after switching the game's
  Language option to English.** `LocalizationLoader.GetLanguageSuffix()` detected the active
  language by reflecting `LocalizationManager.CurrentLanguage` and checking whether its
  `.ToString()` contained `"Chinese"`/`"Cn"` — but that field/property is an **`int` index** into
  `Languages[]` (0=English, 1=简体中文 in vanilla EA 0.66b), so a value's string form (`"0"`, `"1"`)
  can never contain those substrings. Detection silently fell through every time to a
  `CheckOptionsJson()` fallback that reads `Options.json` off disk — which the game only rewrites
  when the Options menu closes (`OptionsMenu.OnDisable → GameLoad.SaveOptions`), not when
  `ApplyLanguage()` fires `LocalizationManager.SetLanguage`. So mod strings kept loading from
  whatever language was last *saved* to disk instead of the live selection, while vanilla UI text
  (read directly from the game's own live `CurrentTexts` dict) switched correctly — the mismatch
  reported as "options say English but mod cards/dialog show Chinese." Fixed by comparing the
  reflected `int` value directly (`langIndex == 1 ? "Cn" : "En"`) instead of string-matching its
  `ToString()`, so `LocalizationLoader.ReloadForLanguage()` (postfixed onto
  `LocalizationManager.LoadLanguage`) now agrees with the live in-game language on every switch,
  with no scene-reload or Options.json save required.

---

## [2.20.4] — 2026-08-07

### Fixed

- **P4 (recurrence, now closed): `lib/Assembly-CSharp.dll` was still the EA 0.66 binary while the
  installed game had moved to EA 0.66b** (2026-08-06 23:20 update; confirmed via the game's own
  displayed version string as a lettered patch, not the major 0.67 bump first suspected — see
  memory `project_game_version`). Refreshed `lib/Assembly-CSharp.dll` to the live 0.66b binary
  (MD5 `197c590e6279de914dd68c76fc61d69d`) and regenerated `.decomp/` (939 files). Clean rebuild,
  0 errors/0 warnings — every compile-time-bound `GameManager` member the framework calls directly
  (`Awake`, `InitializeStatsAndActions`, `AllBlueprintModels`) still compiles, and every
  string-targeted reflection/`SafePatcher` patch target (`GameLoad.LoadMainGameData`,
  `BlueprintModelsScreen.Show`/`Toggle`, `ExplorationPopup.Setup`, `LocalizationManager.LoadLanguage`,
  `NPCInspectionPopup.SetupActions`, `GameManager.ChangeEnvironment`/`GiveCard`, etc.) was confirmed
  present by name in the fresh decompile. Consistent with 0.66b's small +21-file delta (no
  schema/GUID churn) — no signature breaks found this time, unlike the 0.65→0.66 jump (v2.19.1).

---

## [2.20.3] — 2026-08-07

### Fixed

- **Chinese localization never shipped for the framework's own strings** (Portal Hub, Portal Kit, Arcane Wayfinder perk). Created `Localization/SimpCn.csv` with all 13 rows.
- **`CSFFMFW_BpPortalKit_CardDescription` had an unquoted comma in `SimpEn.csv`**, so the CSV parser split it into three columns — the extra fragment (" sealed with animal fat. Assembles a portable Portal Hub kit.") was silently dropped, truncating the description shown to English-mode players. Quoted the field.

---

## [2.20.2] — 2026-08-06

### Fixed
- **`ConnectionGates.LockConditions` was never read from JSON.** 2.20.0 added the
  `LockConditions` field to `ConnectionGateDefinition` and the any-met-forces-LOCKED evaluation
  in `ConnectionGateService.BuildCondition`, but `WorldMapLoader.ParseConnectionGates` only ever
  parsed `"GateConditions"` — so a gate authored with `LockConditions` in `WorldMap/MapNodes.json`
  loaded with an empty list and the lock silently never applied. `ParseConnectionGates` now reads
  both keys through a shared `ParseGateCondition` helper (also reused by `SealTrigger`, which had
  a third copy of the same five-field read). First consumer: CMC's `cmcStatVillageCrime`
  banishment gate.

## [2.20.1] — 2026-08-06

### Fixed
- **`WorldMapInjector.ResolveDeferredCloneRefs()` now also re-walks non-UID ScriptableObjects**
  (`DialogLine`, `DialogScene`, `WeaponMove`, ...). Previously it only re-resolved `*WarpData`
  references on UID-keyed `CardData` cards after WorldMap clone nodes were created, so a dialog
  `Conditions.RequiredEnvironmentWarpData` pointing at a clone env UID (e.g.
  `cmcEnvVillageFarm`/`cmcEnvVillage`/`cmcEnvForagingForest`/`cmcEnvVillagePath`) never resolved —
  `RequiredEnvironment` stayed null, and `GeneralCondition.ConditionsValid` treats a null
  `RequiredEnvironment` as an unconditional pass. Symptom: multiple env-gated dialog answers
  sharing near-identical text (CMC Professor's "What is this place?", one per map node) all
  appeared simultaneously regardless of the player's actual location. See root `CLAUDE.md`
  §Debugging Discipline and memory `reference_worldmap_clone_ref_deferred_resolve`.

## [2.20.0] — 2026-08-06

### Added
- **`NPCCharacterPerk` JSON loading** (`NPCCharacterPerk/*.json`). The type is a
  `UniqueIDScriptable` via `CompletableObject` (same chain as `CharacterPerk`/`Objective`) and
  registers through the standard path. Unblocks shared-chassis NPC designs — one `NPCAgent`
  plus per-variant personality perk bundles, mirroring vanilla's Partner presets (first consumer:
  CMC Village Guards; closes that plan's R12).
- **`ConnectionGates.LockConditions`** (`WorldMap/MapNodes.json`): conditions that force a gate
  LOCKED when met (any one → locked), overriding `GateConditions`. Lets a negative axis (e.g. a
  crime/notoriety `StatThreshold`) close a connection an improvement/perk gate would otherwise
  hold open, without registering a second, conflicting gate on the same `ConnectionUID`.
- **Mid-run connection-gate re-evaluation**: gates now also re-evaluate on a 5 s
  `TickEvents.Interval` (state-change-guarded — no work unless a gate actually flips).
  Previously gates were only evaluated at run start and on improvement completion, so a
  `StatThreshold`/`Season`/`PerkEquipped` change mid-run did not take effect until reload.

## [2.19.1] — 2026-08-06

### Fixed
- **Recompiled against the actual EA 0.66 game assembly** (`lib/Assembly-CSharp.dll` was still the
  EA 0.65-era binary — the "Prepping for EA-0.66" commit only bumped version strings and shipped
  unrelated fixes, it never replaced the compile-time reference DLL). Rebuilding against the real
  EA 0.66 assembly surfaced two genuine signature breaks in the Animal system:
  - **`InGameNPCStat.SetStatValue(float)` is no longer public.** EA 0.66 made it a private
    `IEnumerator SetStatValue(float, NPCStatModifierTypes)`. `AnimalLifecycleTicker.cs`'s 11 direct
    stat writes (exists/blood/respawn-timer sets) now go through the public
    `SetStatValueFromEditor(float)` wrapper instead — despite the name, it carries no editor-only
    behavior (confirmed by decompile: it's a one-line `StartCoroutine(SetStatValue(value,
    NPCStatModifierTypes.Permanent))` forwarder), so it's the correct runtime replacement.
  - **`NPCAgentSpawnSettings.SpawnedAgent` is no longer a public field.** `SpawnRegistrar.cs`'s
    spawn-queue injection (dedup check + new-entry construction) now goes through reflection
    (`AccessTools.Field`) on the boxed struct instead of direct field access — the public `GetAgent`
    property can't be used as a substitute since it has no setter and falls back to a preset's
    `TemplateAgent`, which isn't the semantics the dedup check needs.
  - Both were confirmed by an actual `dotnet build` failure against the correct EA 0.66 DLL (13
    compile errors), not by static signature diffing alone. Release build now succeeds with 0
    errors, 0 warnings against the real EA 0.66 assembly.
  - **Runtime verification still pending** — a clean compile confirms these two call sites, not
    every Harmony `[HarmonyPatch(typeof(...))]` target elsewhere in the framework. Launch the game
    with this build and check `LogOutput.log`/`Player.log` for `HarmonyLib` patch-apply exceptions
    or `MissingMethodException` during plugin `Awake` before treating the framework as fully
    EA-0.66-verified.

---

## [2.19.0] — 2026-08-05

### Added
- **`SealableGates`/`ConnectionGates` gain a `"Season"` condition type.** `GateConditions[].Type`/
  `SealTrigger.Type` now accepts `"Season"` (`UID` = a season name, "Spring"|"Summer"|"Autumn"|
  "Winter", case-insensitive), compared against `GameQuery.CurrentSeason`. Built for Community
  Mod Chest's winter-sealed Village roads, but usable by any mod wanting a connection or
  challenge gate to key off the current season.

### Fixed
- **A `SealableGates` gate whose `SealTrigger` goes false could stay showing LOCKED forever.**
  Every trigger shipped before now was monotonic (`PerkEquipped`/`ImprovementBuilt` only ever go
  false→true once in normal play), so `SealableGateService.OnPoll` skipping a gate's entire body
  once its trigger read false never mattered — nothing else was still calling
  `ConnectionGateService.EvaluateAll()` to pick up the change. A `"Season"` trigger legitimately
  flips true→false→true every year, and without a fix, a road nobody dug through before the
  season ended would never reopen (short of a full game restart). `OnPoll` now tracks each gate's
  trigger-active state across ticks and forces one final `EvaluateAll()` on a true→false
  transition. No behavior change for existing monotonic-trigger gates.
- **`SealableGates`' `ResealCondition: {"Type":"TimerRegrowth"}` could never actually reseal
  within one continuous play session.** `CheckResealTimer` early-returned on
  `state.ClearedThisSession` before reaching its own elapsed-day math — and that flag is only
  ever reset on a full game restart, making the reseal permanently unreachable after the first
  clear unless the player reloaded a save. (This path had zero production usage until this
  release, which is why it was never caught.) Removed the redundant early-return (the elapsed-day
  check already covers "too soon to reseal" on its own) and reset `ClearedThisSession` plus the
  gate's `SeedRequestedEnvUIDs` entries when a reseal fires, so the challenge card can be
  reseeded next time the trigger reactivates.

---

## [2.18.2] — 2026-08-02

### Fixed
- **Cleared cave passages no longer re-collapse after all veins are depleted.** The old-save
  cleanup in `WorldMapInjector.PreCreateCloneEnvSaveData` ("stale Exit-card fix") wiped a clone
  env's entire `EnvironmentsData` entry — including `CurrentlyBuiltImprovements` where
  `SealableGateService` stores permanent "wall cleared" markers — whenever no expected
  ExtraDrops (veins) were found in the saved board. This correctly handled pre-strip old saves
  that contained inherited Exit cards but no vein cards; however it also fired for legitimately
  fully-depleted caves (all veins mined out), erasing the cleared-passage markers and causing
  all collapsed rock walls to respawn on the next load. Fix: the stale-Exit wipe now additionally
  requires at least one `StripLegacyBoardUIDs` card to be present in the entry before removing
  it, confirming it is genuinely a contaminated old-save rather than a depleted-but-valid cave.

---

## [2.18.1] — 2026-08-02

### Fixed
- **`EncounterGuards/*.json` now supports environment-based suppression (`GuardEnvironmentUids`).**
  The loader previously recognized only `GuardCardUids` and silently skipped any guard file
  lacking that field (logging `missing GuardCardUids — skipped`). Community Mod Chest's
  `CMC_VillageNoWildlife.json` uses environment UIDs to suppress wildlife inside village
  environments, so its guard was never registered — village wildlife suppression was inert.
  A guard is now valid with either `GuardCardUids` or `GuardEnvironmentUids` (or both, evaluated
  as OR); the predicate suppresses a wildlife encounter when the player stands in a listed
  environment (via `GameQuery.CurrentEnvironmentUniqueId`), still respecting the `EncounterUids`
  filter and `SuppressChance`.

---

## [2.18.0] — 2026-07-27

### Added
- **Declarative bulk trading-value repricing (`TradingValues.json`)** — a mod may ship a flat
  JSON object map of `CardData` UniqueID → number in its mod root; the new
  `Injection/TradingValueInjector` (LoadOrchestrator phase 5i-a3) writes each value onto
  `CardData.TradingValue` at load. Built because vanilla leaves ~65% of items/liquids at
  `TradingValue: 0`, which NPCs trade as "free". Values apply unconditionally (listed cards are
  retuned even if already priced); keys starting with `_` are ignored (comments); negative
  values are rejected with a Warn; a UID priced by two mods logs a Warn and the later mod in
  load order wins; UIDs not found in the registry are Debug-logged and skipped (a fleet price
  table may cover an optional sibling mod). Runs before `GameSourceModifier`, so a targeted
  `GameSourceModify/` patch still overrides a bulk price. `TradingValues.json` also counts as a
  framework-only marker for `ModManifest.HasFrameworkOnlyMarkers` (mistagged-mod reclaim).

## [2.17.2] — 2026-07-27

### Fixed
- **NPC-interaction button text overflowing its border** (Talk/Trade/Commissions row in
  `NPCInspectionPopup`, and `DialogsPopup` answer buttons). These buttons ship with TextMeshPro
  auto-sizing disabled, so any label wider than the button's authored width clipped past the
  border instead of shrinking to fit. New `Patching.BugFixes.NPCButtonTextFit` postfixes
  `TooltipButton.Setup`, `DialogAnswerButton.Setup`, and `NPCInspectionPopup.SetupActions` to
  enable TMP shrink-to-fit auto-sizing the first time each button's text component is seen
  (`fontSizeMax` = the button's original authored size, so already-fitting text is visually
  unchanged). `TooltipButton.Setup` underlies every `IndexButton`-family button in the game, so
  this covers the same overflow class fleet-wide, not just the two NPC popups. Requires a new
  `Unity.TextMeshPro.dll` reference (already shipped in `lib/`, now wired into the csproj).

---

## [2.17.1] — 2026-07-21

### Changed
- **`ConnectionGateService` now logs a `Warn` when a gate with `HideTravelDA: true` flips to
  unlocked while stripped travel DAs sit in its restore cache but `RestoreDAOnUnlock` is false.**
  This configuration shows the map connection but leaves the compass slot as a permanent red X
  (slot exists, no travel action) for the rest of the game process — almost always a
  `MapNodes.json` authoring error, and previously completely silent. Found via CMC's Village
  Path gate (River Clearing → East red X despite built bridge + Pathfinder perk). The doc
  example in the service header no longer models `"RestoreDAOnUnlock": false`.

---

## [2.17.0] — 2026-07-19

### Fixed
- **Game froze solid (Not Responding) when loading a save that contains a blueprint container with a missing contained-blueprint card** (e.g. CMC 1.21.0 Miller/Alchemist content). `BlueprintContainerSaveLoadFix.ProcessOneCard` reflect-invoked vanilla `GameManager.SpawnDefaultContainedBlueprints` and drained the returned `IEnumerator` with a synchronous `while (iter.MoveNext()) {}`. That vanilla coroutine is not a bounded computation — when a contained blueprint is actually missing it schedules a real Unity coroutine (`StartCoroutineEx(AddCard(...))`) and then `while (CoroutineController.WaitForControllerList(...)) yield return null;`, a wait that can only resolve across real frames. A synchronous drain never yields to Unity, so `AddCard` never advances and `MoveNext()` returns true forever — a deterministic infinite spin, not a race. Rewrote `ProcessOneCard` to yield through the enumerator step-by-step (`yield return iter.Current`) so Unity processes frames between steps. Confirmed fixed in-game by the user 2026-07-19 (same save, same content, loads and responds normally). Retrospective: `Documentation/Retrospectives/blueprint-container-save-load-freeze.md`; memory `reference_synchronous_coroutine_drain_freeze`.

### Changed
- **Vanilla QuestLog auto-injection (`QuestInjector`) is now hard-gated OFF by default.** A mod shipping `Quests.json` will NOT attach any `QuestLog` to `PlayerCharacter.Quests` unless the player explicitly sets `Quests/EnableQuestInjection = true` in the framework's BepInEx config. This exact path caused a user-confirmed blueprint research reset on save load in CMC 1.7.0 and the root cause was never diagnosed (`Documentation/Retrospectives/questinjector-blueprint-reset-risk.md`). Previously the injector ran and attached whenever a manifest was present, emitting only a warning; it now refuses to attach and logs why. `CharacterRosterInjector` (a separate, lower-risk surface with no incident history) is unchanged.

---

## [2.16.4] — 2026-07-16

### Fixed
- **Clone-node location cards whose JSON referenced other clone UIDs (blueprint gates, contained blueprints, improvements) resolved those refs to null**, because the clone env/location pair is created during `WorldMapInjector.PrepareAll` — after `WarpResolver` has already walked all JSON. Symptom: a clone location card showing an empty "Have :" tooltip line (a blueprint availability gate that never resolved) and clone-referencing fields silently staying null. Added a `WorldMapInjector.ResolveDeferredCloneRefs` LoadOrchestrator phase that runs immediately after `WorldMapInjector` and re-walks the deferred JSON refs for cards keyed by clone UIDs registered this run, filling the reference fields WarpResolver couldn't. Reference-token (WarpType 3) only — Add cases are handled by the dedicated `ImprovementInjector`. Memory: `reference_worldmap_clone_ref_deferred_resolve`.

---

## [2.16.3] — 2026-07-16

### Fixed
- **`InjectImprovementInto.json` could never target a mod map node's own location card**: the
  ImprovementInjector load phase ran before `WorldMapInjector.PrepareAll` created the clone
  env/location pairs, so a `TargetEnvUID` naming a clone CT8 (e.g. CMC's `cmcLocVillage`) was
  always "not found in registry — skipped". The phase now runs after map-node preparation
  (5i-a2); vanilla CT8 targets are order-insensitive and unaffected.

---

## [2.15.2] — 2026-07-16

### Fixed
- **`CardUtil.GetDurability`/`SetDurability` never worked for `UsageDurability`**: the JSON stat name mapped to a nonexistent `CurrentUsage` runtime member (the real `InGameCardBase` field is `CurrentUsageDurability`), so reads returned NaN and writes silently failed. Player-visible fallout fixed by this: the CMC Academy's Armorer course never charged its 100 tuition enrollment fee (its progress lives on `UsageDurability`), and Sirus companion thirst initialization on spawn was a no-op.

---

## [2.14.1] — 2026-07-12
*(Covers framework releases since the last published release on 2026-06-23.)*

### Added
- **Declarative animal system foundation**: mods can now ship `Animals/*.json` manifests that the framework validates and turns into generated NPC agents, with config gating and run-start spawn registration. This is the first milestone of the animal pipeline: schema loading, validation, generated agents, lifecycle templates, model-card inventory safety, and deferred-section warnings for not-yet-implemented animal features.
- **JSON-only non-UID ScriptableObject support**: `ScriptableObject/<Type>/*.json` assets such as `WeaponMove`, `DamageType`, and `CardTag` are now registered by name so WarpData can resolve them. This unblocks JSON-authored custom attacks and other non-UID assets.
- **`GameSourceModify` support for non-UID targets**: JSON patches can now modify existing vanilla or modded non-UID objects by name, allowing in-place edits to shared `WeaponMove`, `DamageType`, `CardTag`, and similar assets.
- **Shared utility APIs**: added `Api.BlueprintAlternates`, `Api.CardFinder`, `Api.StatAccess`, and `Api.RecipeInjector` so content mods no longer need to duplicate reflection-heavy helpers for alternate ingredients, runtime card lookup, stat access, or station recipe injection.
- **Perk group opt-out**: `"CharacterPerkPerkGroup": "None"` keeps runtime-only perks out of character creation instead of forcing them into the Situational tab.

### Changed
- **Portal Hub flow redesigned**: the Portal Kit is now portable and can be placed anywhere in the vanilla world; mod worlds use isolated portal environments with auto-injected exit cards instead of a fixed River Clearing entrance.
- **WorldMap gate handling hardened**: connection gates now support precise edge toggling, travel DA strip/restore, run-start re-evaluation, and framework-owned declarative gates used by ACT and H&F.
- **Mod discovery is more forgiving**: framework-format mods that accidentally carry Pikachu `ModLoaderVerison` fields are reclaimed when they also ship framework-only marker files such as `BlueprintTabs.json`, `MapMod.json`, `WorldMap/MapNodes.json`, or `Animals/*.json`.
- **Loader coexistence logging improved**: non-UID name collisions now distinguish benign external-loader duplicates from real same-pass mod collisions.

### Fixed
- **NPC actions with no drops no longer crash** when converted to game actions; null `DroppedCards` arrays are backfilled before WarpResolver.
- **Blueprint container save/load handling no longer synchronously drains coroutines**, avoiding load freezes around station-contained blueprints.
- **WorldMap node injection no longer double-seeds or loses run-start location cards** in the covered gate and portal scenarios.
- **Framework-format third-party mods no longer silently lose blueprint tabs** just because Pikachu ModLoader or ModCore is installed.

### Technical
- Load orchestration now includes the animal phase, NPCAction drop repair, declarative improvement injection, sealable gates, shared recipe injection, and expanded vanilla ID resources.
- ACT, H&F, WDI, CMC, and Sirus integrations were progressively moved onto shared framework services, reducing duplicated mod-local Harmony and reflection code.

---

## [2.11.1] — 2026-07-05

### Fixed
- **Framework-format mods mistagged with Pikachu `ModLoaderVerison` are no longer skipped.** `ModDiscovery.DiscoverMods` now checks whether a `ModLoaderVerison`-tagged mod ships framework-exclusive declarative content (`BlueprintTabs.json`, `SmeltingRecipes.json`, `DropInjections.json`, `InjectImprovementInto.json`, `WorldMap/MapNodes.json`, `EncounterGuards/*.json`, `Quests.json`, `Characters.json`, `MapMod.json`) before deciding to skip it in favor of an installed Pikachu ModLoader/ModCore. Mods with framework-only markers are loaded through the framework's own pipeline instead — `ForeignInstanceReconciler` neutralizes the resulting duplicate `UniqueIDScriptable` instances ModLoader creates for them. Fixes third-party mods (e.g. DurosCoinage's `BlueprintTabs.json`) whose blueprint tabs silently never appeared because the mod was entirely skipped by the framework despite being authored for it.

---

## [2.8.0] — 2026-06-21
*(Covers versions 2.2.0 → 2.6.0 → 2.7.x → 2.8.0, since v2.0.8)*

### Added
- **Portal Hub System**: A new **Portal Kit** item can be placed anywhere to erect a Portal Hub — a standing stone that opens a gateway to worlds added by installed mods. Pack it up and move it at any time.
- **Arcane Wayfinder perk**: A free starting perk that grants a Portal Kit at run start. Available immediately for all runs.
- **WorldMap node injection**: Mods can now add fully functional new locations to the world map with their own environments, resources, and travel connections — appearing alongside vanilla map nodes.
- **Clone-based map environments**: Mods can clone vanilla biomes (oak groves, pine clearings, caves, etc.) as new locations, inheriting trees, resources, and ambience.
- **Quest support**: Mods can now ship quest lines that appear in the journal and integrate with the standard objective/reward system.
- **Custom character support**: Mods can add selectable player characters that appear in the character-select screen.
- **SelfTriggeredAction support** *(v2.2.0)*: Mods can ship stat-gated events, seasonal triggers, blueprint unlocks, and perk grants without any C# code — activated automatically each run.
- **Encounter guards**: Mods can now suppress specific wildlife encounters in designated areas (e.g. no bear attacks inside a protected grove).
- **SpawnStatDefaults**: Mod items can declare initial stat values (starting durability, metal type, etc.) applied every time they are spawned — no per-mod C# postfix needed.

### Fixed
- **Blueprint research no longer resets on save/load** when Pikachu ModLoader or ModCore is installed alongside framework mods. ModLoader was creating duplicate card instances that caused the game to lose track of researched blueprints — the ForeignInstanceReconciler now guarantees the framework's instances are canonical.
- **Travel popup no longer crashes with WikiMod installed** alongside mods that add WorldMap locations. WikiMod's internal error is now caught so travel buttons stay functional.
- **Clone-environment location cards no longer follow the player** between maps when the cloned template had `AlwaysUpdate: true`.
- **Mod-added travel direction buttons no longer show a red ✗ all night** and activate only at dawn — inherited light/stamina stat gates are now stripped from injected travel actions.
- **Modded map node environments no longer cause the world map to break** when node UIDs contained underscores — UIDs now use camelCase, fixing silent save-data match failures.
- **DefaultEnvCardDrops from clone templates no longer re-spawn every entry** into a modded area — follower drops from the template are neutralized so board state persists correctly.

### Technical
- Tier 1 utility API (`Api.Reflect`, `Collections`, `Inventory`, `Gate`, `LocalizedStringBuilder`, `VanillaIds`) available to mod authors for common reflection and data-access patterns.
- Tier 2 runtime services (`Api.ActionRouter`, `SpawnService`, `TickEvents`, `EncounterGuards`, `ContentModPlugin`) — mod actions, spawns, and timed events no longer require per-mod Harmony patches on game coroutines.
- Type loading hardened against `ReflectionTypeLoadException` from third-party assemblies — a bad DLL no longer aborts framework startup *(v2.2.0)*.
