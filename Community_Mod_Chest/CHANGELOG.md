# Community Mod Chest — Changelog

All notable changes to this mod are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [1.48.3] — 2026-08-13

### Fixed

- **Six village map locations renamed to stop colliding or being confusable with vanilla location names.**
  `cmcEnvHighGrove`/`cmcLocHighGrove` and `cmcEnvMossyClearing`/`cmcLocMossyClearing` are clones of the vanilla
  environments `Env_GrovePine_HighGrove` and `Env_ClearingOak_MossyClearing`, and had kept those environments' exact
  vanilla display names ("High Grove", "Mossy Clearing") — indistinguishable in menus, logs, and player conversation
  from the real vanilla locations of the same name. Renamed to **Highland Pines** and **Moss-Grown Clearing**. A
  follow-up pass also renamed four more nodes that echoed a *different* vanilla location's name closely enough to be
  confusable, even though the full text wasn't identical: **Clay Flats** → **Clay Shoal** (vanilla has "Clay Banks"),
  **Marsh Hollow** → **Sodden Hollow** (vanilla has "Heather Marshes"/"River Marsh"), **Deer Meadow** → **Stillwater
  Meadow** (its own clone source is vanilla "Deer Grove"), **Badger Warren** → **Sett Warren** (its own clone source is
  vanilla "Badger Hill"). All six: English + Chinese localization, `WorldMap/MapNodes.json` `DisplayName`, plus the
  North Snow Drift's description/action text which named Deer Meadow by name. UIDs, connections, and terrain are
  unchanged — this is a display-text-only fix. The other six village map nodes were audited against the full vanilla
  `CardName` table and confirmed to not collide with or closely echo any vanilla location.

## [1.48.2] — 2026-08-12

### Fixed

- **Village building interiors (Inn, Academy, Miller's Cottage, Weaver's Cottage, Village Hall,
  Apothecary's Cabin, Jail Cell) now self-declare as indoor environments** (`"tag_EnvIndoors"` in
  `CardTagsWarpData`). These 7 environments previously shipped with no `CardTags` at all, which
  silently broke Sirus23_Mod_Collection's Owl-companion "don't follow indoors" check (and any
  other future indoor-aware mod logic) for every one of them — the Owl kept following straight
  into the Inn/Academy/etc. despite that mod's fix. `CardData/Environment/*Interior*.json`,
  `CardData/Environment/CMC_JailCell.json`.

## [1.48.1] — 2026-08-12

### Changed

- **Village Reputation is now a visible Mental attribute in the character menu**, instead of a
  hidden tracked-only stat. It appears alongside Morale, Stress, Connection, etc. in the Mental
  tab (`GameSourceModify/Mental.json` appends `cmcStatVillageReputation` into the vanilla tab's
  `ContainedStats`, so no vanilla file is overridden). The Village Hall's Notice Board mirror and
  every existing threshold/gate are unchanged — this only adds a second place to see the number.

## [1.48.0] — 2026-08-12

### Changed

- **Captain Reeve Sterling no longer deals damage to the player under any circumstance.** Every
  one of his combat actions — blocking, grappling, holding — stops short of drawing his sword on
  you; he can no longer wound you in any encounter, including one you start yourself by using the
  Attack button on him.
- **Being caught alone by Sterling now opens with a choice instead of a forced fight.** When
  Thorne or Corrin has beaten you and sent for the Captain, and he catches up with you himself,
  the encounter offers **Attack the captain** or **Think better of it**. Choosing to think better
  of it ends the confrontation with no fight at all — you walk, and Sterling simply resumes
  pursuing you.
  - You get **three** such chances, tracked by a hidden counter.
  - The third refusal spends the leniency: the next time Sterling reaches you, the encounter opens
    with no escape option at all, and he takes you into custody without a fight and without
    hurting you.
  - You still wake up in the **Village Jail** afterward, serving the same crime-based sentence as
    before — but with **no Bruising** this time, since no blow was ever struck.
- **The whole-Watch encounter, when Guard Iris Vane's alarm brings every guard down on you at
  once, keeps its existing stakes.** That encounter opens straight into the fight with no
  leniency offer. Sterling still deals no damage in it, so you can fight through and beat him same
  as any other guard — the "best all four guards and the village drops the charges" reward path
  is unchanged.

## [1.47.1] — 2026-08-12

### Fixed

- **Allied companions (the vanilla Partner, and any future NPCAgent flagged
  `AlliedWithPlayer`) can now follow the player through the Village Inn and Village Academy
  doors, in both directions.** Building interiors were never nodes in the WorldMap graph, so
  vanilla's own follow mechanism (`NPCDuty` + `MoveDutyAction`'s A* pathfinding) could never
  route a companion through either door no matter how the duty was tuned. `PartnerIndoorFollowPatch`
  bypasses pathfinding for this boundary and directly relocates any allied NPC that was
  standing with the player in the village the moment they step through — the same direct
  `GameManager.MoveNPC` mechanism CMC's own resident schedulers (Professor, Miller, Weaver,
  Apothecary, Inn Keeper) already use for their own interior comings and goings. A companion
  who wasn't with the player is left where it was rather than teleported in.

## [1.47.0] — 2026-08-12

### Changed

- **Village Renown and Village Crime merged into a single Village Reputation stat.** The Village
  Hall's Notice Board, the Town Board's standing text, the Inn Keeper's News dialog, and the Clay
  Beads/Stone Mace reveal thresholds now all read a new signed `cmcStatVillageReputation` stat
  (civic score minus your own Village Crime notoriety), instead of the old unsigned
  `cmcStatVillageRenown`. All existing thresholds (25/50/75/100) mean the same thing as before in
  the common case (zero crime) — the only new behavior is that high Village Crime can now visibly
  suppress your civic standing, including delaying the Clay Beads/Stone Mace reveals and the
  Market Stall's full-reputation bonus if crime is high enough to offset it.
  `cmcStatVillageCrime` itself, `VillageCrimePatch`, and the entire Guards/Jail/Banishment system
  are functionally unchanged — Crime remains its own independent 0-100 incident ledger, now with
  a second, read-only consumer.
  - New Town Board notice for a net-negative Village Reputation (previously no board text
    acknowledged that state at all).
  - Fixed a latent bug where the Town Board's status line silently vanished if a read-succeeded
    sentinel check (`>= 0f`) ever saw a legitimately negative value — not reachable before this
    merge, since Renown could never go negative.
  - Fixed a mismatched threshold: the Town Board's prose tier text switched to its top tier at
    80% while the board's own declarative bands switched at 75% — both now agree at 75%.

## [1.46.7] — 2026-08-11

### Fixed

- **Academy lectern self-heal could permanently latch onto the wrong duplicate instance.**
  `GraduatePerkPatch.CheckAcademyBackfill` used a single-match `FindLiveCard` to locate the
  `cmcAcademyLectern` on the board, backfilled it, then latched a "already backfilled" flag that
  never retries — if it resolved an orphan/empty duplicate instead of the instance the player
  actually sees, the visible lectern stayed stuck at 0% forever (graduate perks still granted).
  Mirrors `AcademyPatch.FindAllLiveLecternCards`: now reconciles every live `cmcAcademyLectern`
  instance found on the board, and only latches the flag after at least one instance was written.
- **Five NPC spawn/schedule patchers failed silently when an Agent UID couldn't resolve.**
  `CottageResidentSpawnPatch`, `GuardSpawnPatch`, `AshPartnerSpawnPatch`, `ProfessorSchedulePatch`,
  and `ApothecarySchedulePatch` all polled a `GetFromID` result with zero log output on failure — a
  renamed/mistyped UID or a JSON load failure meant the affected NPC silently never spawned, with
  no diagnostic trail. Each now emits a one-shot `LogWarning` naming the unresolved UID(s).

### Changed

- Demoted 6 shipped `LogInfo` diagnostic call sites (`CompanionFollowDiagnostics`,
  `AcademyPatch.DumpLecternInstanceIdentity` + duplicate-count log) to `LogDebug` — these were
  temporary investigation logging left at Info level past their investigation, contrary to
  §Mod Logging Norms. No behavior change; log-volume hygiene only.

---

## [1.46.6] — 2026-08-11

### Fixed

- **The Homestead trait's cabin kit was too heavy to carry.** `CardData/Item/CMC_HomesteadCabinKit.json`
  shipped `ObjectWeight: 6000.0`, well above the vanilla starting-character Encumbrance cap of 4000 —
  holding the kit alone triggered "Too encumbered to move," so a Homestead run could only ever settle
  at spawn. Reduced to `2500.0` (a placeholder value, not a final balance pass — well below the cap,
  heaviest vanilla carryable is 3500).
- **The River Bridge blueprint slot could never accept a player-held forge hammer.** Both
  `CardData/EnvImprovement/Imp_RiverBridge.json`'s construction stage and
  `CardData/Blueprint/Bp_CMC_IronFishingRodFittings.json` referenced the vanilla legacy `ForgeHammer`
  card (`e118b8cd90f14b048aab78a0d37e8f61`), which self-transforms into `ToolOrWeapon_Hammer_Metal`
  the instant it spawns — no player could ever be holding the legacy card, and blueprint slots match
  by exact `CardData` reference, not name. Swapped both references to the modern hammer GUID
  (`2914e01d9af26f24d92ff61389fb0195`).
- **Bears and wolves could spawn inside village interior buildings.**
  `EncounterGuards/CMC_VillageNoWildlife.json` only guarded outdoor village-area environments and
  carried one dead UID (`cmcEnvVillageHall`, unused by any card); none of the seven enterable
  interiors were covered. Removed the dead UID and added a new
  `EncounterGuards/CMC_InteriorsNoWildlife.json` suppressing wildlife encounters in all seven
  interiors (Inn, Academy, Apothecary Cabin, Miller's Cottage, Weaver's Cottage, Village Hall, Jail
  Cell).
- **A village interior's very first visit could show the outdoor Village card instead of the
  interior's own furnishings.** The seven interior environments had no `EnvironmentsData` entry
  pre-created, so `GameManager.ChangeEnvironment`'s gate failed on first entry and the engine
  re-dropped the outdoor Village's `UniqueOnBoard` card onto the interior board. New
  `Patcher/InteriorEnvSaveDataPatch.cs` pre-creates the save-data entry for all seven interior
  environments at run start.
- **Village Pathfinder's River Bridge auto-build failed silently on an outdated framework, and could
  miss a bridge that had already spawned before the auto-complete queue armed.**
  `RiverBridgeUnlockPatch` now probes for the three `CardUtil` helpers it needs at startup and logs
  one clear, actionable `LogError` (naming the required framework version, 2.17.0+) instead of a
  swallowed `MissingMethodException` buried as a warning. It also now scans the current board for an
  already-spawned, incomplete bridge improvement card and completes it directly for perk holders,
  covering the case where a save is loaded while the player is already standing at River Clearing
  (the auto-complete queue only catches bridges spawned *after* it arms at `OnGMInitialized`).

### Changed

- **Hardening: the seven interior location cards now set `AlwaysUpdate: false`.** All seven
  (`CMC_{Inn,Academy,ApothecaryCabin,MillerCottage,WeaverCottage,VillageHall}InteriorLocation.json`,
  `CMC_JailCellLocation.json`) had shipped with `AlwaysUpdate: true`, a documented rule violation for
  CT4/CT8 environment cards. This is a rule-compliance correction, not a resolution of the reported
  professor travel softlock — that root cause remains unconfirmed.

## [1.46.5] — 2026-08-09

### Fixed

- **The River Bridge improvement could stay permanently invisible at River Clearing, most often on
  old saves, for any player without the Village Pathfinder perk.** The slot only renders once the
  engine spawns a ghost card for it, which only happens once the CT10's own `CardUnlockConditions`
  discovery gate (`CardsOnBoard: "HasPlank"`) evaluates true *while the player is standing at River
  Clearing* — being listed in the CT8's `EnvironmentImprovements` array (via
  `InjectImprovementInto.json`) is necessary but not sufficient. Nothing guaranteed a returning
  player was holding a Plank the moment the engine's periodic unlock scan ran, so the gate could
  stay unsatisfied indefinitely and the bridge would never appear, regardless of the mod working
  correctly otherwise. `VillagePathfinderBridgePatch` already bypassed this exact gate with
  `CardUtil.ForceUnlockCard`, but only for Village Pathfinder perk holders. Renamed to
  `RiverBridgeUnlockPatch` and now bypasses the gate for every player, every run start — the
  bridge slot always appears the first time River Clearing is visited that run, construction
  materials still required as normal. The perk's existing auto-build shortcut (marks it fully
  constructed, no materials needed) is unchanged and still perk-exclusive.

## [1.46.4] — 2026-08-09

### Fixed

- **The Inn Keeper was still missing from the Village Inn after the [1.46.2] fix.** That fix
  corrected the *NPC's* environment but not her *board card's* — the two are separate `EnvID`s, and
  vanilla `GameManager.MoveNPC` only carries the card across inside a block it skips entirely when
  the NPC's own environment already matches the target. The 1.46.2 boot-time reconcile runs from
  `InitializeStatsAndActions`, which is *before* vanilla builds her board card, so it moved an NPC
  that had no card yet: her environment was corrected and saved, her card was left in the orphaned
  `cmcInnInterior_cmcEnvVillage`, and from then on both reconcile paths saw a matching NPC
  environment and stood down every load. Verified against the owner's `LogOutput.log`: she restores
  from save, the player walks into `cmcInnInterior` with an exact environment match, and the mod
  logs nothing further — no error, no warning, no reconcile.
  `InnKeeperSpawnPatch` now runs a board-presence check while the player is standing in the Inn that
  asserts the *card's* environment, not just the NPC's, and repairs either — relocating a
  left-behind card (by clearing the NPC's environment first, so vanilla's early-return can't skip
  the card relocation) and requesting a new one via `AssignOrCreateNPCCards` if she has none at all.
  The check logs the state it observes on every change, is capped at five repair attempts per
  session, and is inert once she is present. Affected saves repair themselves the next time the
  player enters the Inn. **Note:** as with [1.46.2], loose items left in the Inn before the 1.44.x
  environment flip are in that same orphaned environment and are not recovered.

## [1.46.3] — 2026-08-09

### Fixed

- **The Village and Village Farm could show vanilla river-flood warnings ("Moderate Overflow" /
  "High Overflow") even though neither sits on an actual river tile.** `WaterLevel`/
  `WaterLevelVisible` is a vanilla *global* stat (one value for the whole save, driven by
  `RainValue`/weather) rather than something tied to a specific environment, so it displays
  and applies everywhere the player stands, including the Village's cloned `Green Glade`
  environment. Vanilla's own countermeasure is the `Levee` structure — a `CardType: 13`
  invisible helper (`LeveeInvisible`) carrying a passive effect that clamps `WaterLevelVisible`
  by -7665 (fully suppressing the overflow status and its "flooded" travel penalty) while the
  player is physically standing in the same environment. `WorldMap/MapNodes.json` now force-drops
  this vanilla Levee marker onto both `cmcEnvVillage` (Town Square, alongside the Inn/Academy/Jail)
  and `cmcEnvVillageFarm` (alongside the seasonal crop fields) via the existing `ConditionalDrops`
  mechanism, so both are permanently flood-protected without any player action required.

## [1.46.2] — 2026-08-09

### Fixed

- **The Inn Keeper was missing from the Village Inn in saves that had met her before 1.44.x.**
  Confirmed from a live save, not inferred: her saved environment key was
  `cmcInnInterior_cmcEnvVillage` while the player now travels into the bare `cmcInnInterior`.
  An NPC's location persists as a *string key*, and for an `InstancedEnvironment: true` env that
  key carries the parent chain. 1.44.x correctly flipped all six village interiors to
  `InstancedEnvironment: false` (to fix the entering-an-interior softlock), which changed the key
  the player travels into — but nothing rewrote keys already written into saves. On load the
  Keeper was restored into that now-orphaned environment, vanilla's `AssignOrCreateNPCCards` built
  her board card *there*, and she became permanently invisible: no error, no warning, and the
  once-a-second arrival check saw her as "already spawned" and stood down every time the player
  walked in. She was the only village NPC affected — the Professor, Apothecary, Miller and Weaver
  are all moved by their own schedulers, which rebuild the environment ID from the card each time
  and so silently self-corrected after the flip; the Keeper never moves, so nothing ever
  rewrote hers. `InnKeeperSpawnPatch` now reconciles her environment against the Inn interior card
  itself in two places — immediately after the save restore (before her board card is built) and
  again whenever the player is standing in the Inn — using vanilla `GameManager.MoveNPC`, which
  also carries an already-created card across. Affected saves repair themselves on the next load;
  the check is inert once the keys agree. **Note:** items left in the Inn before the 1.44.x flip
  are in that same orphaned environment and are not recovered by this fix.
- **Miller's and Weaver's Cottage operation blueprints now require Copper Nuggets specifically**, not
  any metal type. All nine station recipes (Grind Rye/Wheat/Acorn into Flour, Mill Logs into Planks,
  Process Hemp/Flax/Nettle into Fiber, Weave Large Cloth, Weave Rope) referenced the generic
  `MetalNugget` GUID with no metal-type gate, so Tin or Iron nuggets satisfied the requirement even
  though every recipe's flavor text says "for a fee in copper" / "keeping a copper nugget for the
  work." Added `Special4: {Active:true, FloatValue:100, MaxValue:100}` (Copper's SD4 value) to each
  recipe's nugget requirement slot, matching the vanilla `Bp_CommissionCopperNuggets` pattern.

## [1.46.1] — 2026-08-09

### Fixed

- **Confirmed the Inn Account currency deposit ("drag Salt/Nuggets onto the Inn Counter") works
  correctly** — the `TEMP DIAGNOSTIC (2026-08-08)` logging added while chasing an owner report that
  "dragging copper nuggets does nothing" caught a real play session (`LogOutput.log`, 2026-08-09) with
  three successful deposits (balance 0→50→100, with a silent `Purchase Meal` draw in between —
  everything self-consistent). No code defect found; the diagnostic `LogInfo` calls in
  `Patcher/InnPatch.cs` are demoted back to `LogDebug` per CLAUDE.md §Debugging Discipline.
- **Not yet confirmed**: the identical deposit CI on the Academy tuition account
  (`Patcher/AcademyPatch.cs`) and the Academy Lecture Hall course-progress reconciler (`[1.45.2]`)
  were never exercised in that session — their diagnostics remain armed at `LogInfo` until a session
  actually tests them.

## [1.46.0] — 2026-08-09

### Added

- **Copper Chests for all five village NPCs.** The Copper Chest — the weekly-accruing container that
  is simultaneously a merchant's savings, their spending power, and a burglary target — was previously
  the Miller's alone (1.38.0, `Village_Master_Plan.md` §10.8.3.7's "prototype it on one cottage first"
  step). It now ships for the **Weaver** (`cmcCopperChestWeaver`, in her cottage interior), the
  **Apothecary** (`cmcCopperChestApothecary`, in her cabin interior), the **Inn Keeper**
  (`cmcCopperChestInnKeeper`, inside the Inn) and the **Professor** (`cmcCopperChestProfessor`, inside
  the Academy), each with its own Sell CI, its own "Search for valuables" theft DA, and its own
  independent theft-heat and accrual-day trackers — five chests never share one counter, so robbing
  the Miller does not raise your risk at the Academy.
- **Per-NPC wealth tiering** (§10.8.3.3, placeholder values pending the tuning pass). Inn Keeper: 500
  salt-value ceiling, 5 Salt/week, and the most varied goods (acorn flatbread, firm cheese, dried meat).
  Miller and Weaver: 300, 3 Salt/week. Apothecary and Professor: 180, 2 Salt/week, but rarer goods drawn
  from their own already-curated pools — healer's moss and old growth bark for her, spirit mushrooms and
  nettle leaves for him. No new items were invented for any chest.
- **Three different accrual mechanisms, one per NPC's existing plumbing** (§10.8.3.3), so no new spawn
  mechanism was introduced anywhere. The Weaver's weekly satchel restock was **retargeted** onto her
  chest exactly as the Miller's was — not duplicated (R6); her `WeaverWeeklyRestock` action is now
  unfired. The Apothecary gets a parallel weekly drop inside her own schedule patch, which already owns
  her poll. The Inn Keeper and Professor get genuinely new weekly ticks, independent of — and not
  replacing — his 4-day pantry restock and the Professor's per-node forage, which are unchanged.
- Each chest is that NPC's **personal** savings. `cmcInnCounter`'s Inn account and `cmcAcademyLectern`'s
  tuition account are untouched and remain separate pools, as §10.8.3.2 requires.

### Fixed

- **The Miller's Copper Chest rendered as a broken/blank image in-game.** Its `CardImageWarpData` was
  `"ChestPlaced"` — a plausible-looking but non-existent sprite name, which resolves to nothing silently
  (no error, no log; root CLAUDE.md §Sprites/Images). It now uses `"Copper_Chest"`, backed by a real PNG
  shipped in this mod at `Resource/Picture/Copper_Chest.png`. All four new chests use the same sprite.
  Because CMC only soft-depends on Advanced Copper Tools, the PNG is a byte-identical copy carried by
  CMC itself rather than a reference into ACT, so the chests render correctly whether or not ACT is
  installed.

### Changed

- `CopperChestPatch` was generalised from three hardcoded Miller constants to a per-resident config
  array; the three action handlers are now registered once per chest, dispatching on each chest's own
  card UID. Its "is the owner standing right here" instant-catch check is now self-contained — it
  previously delegated to a cottage-resident-only helper that knew nothing about the Apothecary, Inn
  Keeper or Professor and would have silently reported "nobody home" (never an instant catch) for all
  three of them.
- Removed the now-unreachable satchel-restock branch from `CottageResidentSpawnPatch`: with both cottage
  residents on chests it could no longer run, and leaving a dormant `RestockActionId` field behind would
  have made the double-drop regression R6 warns about a one-line mistake away.

## [1.45.5] — 2026-08-09

### Fixed

- **Weaver, Miller, and Professor errand "thanks" dialog could never fire, so quest progress never advanced and the village boards stayed frozen on the initial ask forever.** Owner-reported: after delivering the requested materials (dried flax stems, wheat grain, etc.), talking to the NPC again never acknowledged the delivery and the Weaver's/Miller's/Professor's Board kept showing the same unfulfilled errand. Root cause: `DialogScene.GetStartingLine` opens on the first `StartingPoint:true` scene line (in array order) whose conditions pass — and each NPC's generic greeting (`*Talk_Start` / `ProfessorGreeting_Menu`) is essentially unconditioned, so it was listed *before* the conditioned `QuestThanksN` lines in `SceneLinesWarpData`. The generic greeting always won the first-match check, so the Thanks lines (and, for the Professor, several other gated greetings — `GiftValedictorian`, `GiftFirstPass`, `GiftTrust`, `TrustHigh`/`TrustMid`, the seasonal lines) were unreachable dead code; none of their `StatModifications` (incrementing `*QuestChain`, resetting `*QuestArmed`, granting trust/blueprints) ever ran. Reordered `CMC_WeaverTalk.json`, `CMC_MillerTalk.json`, and `CMC_ProfessorGreeting.json` so every conditioned `StartingPoint:true` line is checked before the generic fallback greeting, per the documented "most-restrictive first, unconditioned fallback last" rule — no other wiring changed (dialog branches reference each other by UID, not array position).

## [1.45.4] — 2026-08-09

### Fixed

- **Ash's boar-hunt trail card never actually left the village, despite the log repeatedly saying it had faded.** Owner-reported: "Ash's Trail" (`cmcAshBoarTrail`) stayed on the board indefinitely once the hunt resolved. The cleanup step wrote the trail card's `SpoilageTime` stat directly to 0 via reflection — a write that bypasses the game's own per-tick durability processing, which is the only place `HasActionOnZero`/`OnZero` (the destroy trigger) actually gets evaluated. The stat sat at 0 forever, the card was never removed, and the once-every-2-seconds poll kept re-finding it and re-logging "Ash's trail has faded from the village." `AshBoarHuntPatch` now hands the zeroed trail card to the same CT13 invisible-helper transform already used to remove the tracking card at hunt-start (`BeastTracksCardInvisible` — its own active `SpoilageTime` the real tick loop does evaluate), so the card is actually destroyed instead of just reporting that it was.

## [1.45.3] — 2026-08-09

### Fixed

- **Inn Keeper could fail to spawn inside the Village Inn.** Owner-reported: the Keeper never appeared. `InnKeeperSpawnPatch`'s once-a-second arrival check matched the player's current environment against the cached `cmcInnInterior` card by raw object reference (`ReferenceEquals`) — fragile against any path that ends up with two distinct `CardData` instances for the same UID (this install also runs a third-party ModLoader). The check (and the matching "is this NPC already spawned" check) now compares by `UniqueID` string instead, and failure paths that were previously completely silent now log a one-time warning. Root cause not confirmed from the log alone (it showed no `[InnKeeperSpawnPatch]` activity at all this session) — unverified in-game; diagnostic logging is in place to pinpoint the cause on the next test if this doesn't resolve it.

## [1.45.2] — 2026-08-09

### Fixed

- **Academy Lecture Hall could show 0% progress on a course whose degree you'd already earned.** Owner-reported: after finishing all six courses over many play sessions, every "Study ..." tooltip read "You have already completed this course" (the graduate perk was genuinely held) while the progress bar showed 0%. The lectern's own progress stat had desynced from the perk it's supposed to track — root cause unconfirmed, but the graduate perk is already the authoritative "done" signal used everywhere else (the "already completed" gate, the course-gated blueprint unlocks), so `AcademyPatch` now reconciles the lectern's displayed progress to match it: while standing in the Academy, any course whose perk is held gets its progress stat force-set to max if it reads below that. Self-healing against future desyncs from any cause, not just a one-time repair.
- **Academy course-gated blueprints (Sawmill, Grinding Mill, Copper Sheet, Iron Fishing Rod, copper/iron armor, Forge/Workshop) could get silently re-locked on every reload of a save that had already earned them.** `AcademyCourseService`'s run-start gating pass ran on `GameManager.OnGMInitialized`, which can fire before `InRunAddedPerks` finishes restoring from the save — the pass would find no graduate perks held yet and re-hide the reward blueprints. A one-shot recheck 5 seconds after boot now re-runs the same gating pass once perks have had time to settle.

## [1.45.1] — 2026-08-08

### Fixed

- **Ash's boar hunt no longer logs spurious "may not be loaded" warnings.** The hunt-movement poll treated `SpawnService.Spawn`'s return value as a spawn-success signal, but that call returns null on success in the current game version (the card is placed as a side effect). The trail and tracking-boar cards were always spawning correctly; the code now verifies placement by re-querying the board, so the state advances on the spawning tick and the warning only fires on a genuine failure. No player-visible behavior change to the hunt itself.

## [1.45.0] — 2026-08-08

### Added
- **(1.45.0) Captain Sterling now gives a one-time warning the first time you're caught, and the
  Watch reacts to a Wanted player it can see.** Talk to him (a new "Talk" option) once
  `cmcStatVillageCrime` first reaches the Suspected band and he delivers a single formal warning
  line, latched by a new hidden `cmcStatSterlingWarningGiven` marker so it never repeats. From the
  Wanted band onward (25-59 crime, short of the Banished threshold that arms a chase) any guard who
  shares your current environment reacts without fighting: their own Suspicion rises (feeding the
  existing per-guard detection tuning), and talking to Sterling in that band gets a harder,
  visibly hostile line instead of his usual greeting. Neither reaction is an Encounter or a chase —
  pursuit still only arms at Banished (60+).

### Changed
- **(1.45.0) The four Town Watch guards now have three distinct jobs instead of one identical
  hunt-and-fight duty each.** Captain Sterling drops his own local chase and only moves when
  summoned — sword drawn, not spear, and the player he catches is left Bruised on the way to the
  cell; he is now the only guard whose fight ends in a jail teleport. Guard Thorne and Guard Corrin
  relocate from their separate river/farm beats to a shared day-shift (05:00–20:00) Town Square
  patrol; their local territory-gated chase is unchanged, but beating the player no longer jails them
  directly — they hold the player in place and send for the Captain instead. Guard Vane drops the
  village-territory restriction entirely (the one guard who now crosses the river) and, once she
  reaches the player, calls the rest of the Watch in rather than fighting herself; her day-hiding gate
  now also lets her manifest outside her usual night window once summoned into a hunt. Two new
  hidden player stats (`cmcStatGuardsSummoned`, `cmcStatCaptainSummoned`) carry the summon state
  between guards across the multi-hour walk this can take — deliberately not `cmcStatArrestPending`,
  which self-clears the same tick it fires. Not fully in-game verified this release; folds into the
  next full guard-system playthrough pass.
- **(1.44.6) The Watch captain is now "Captain Reeve Sterling," not "Captain Reeve Ashdown."**
  The surname read too close to "Ash," the name of the Village Inn's stray cat — renamed to avoid
  player confusion between the two characters. Purely cosmetic: `UniqueID`s, localization keys,
  and file names were all updated in step (`cmcGuardSterlingAgent`, `cmcGuardPerkSterling`,
  `cmcEncounterGuardSterling`, `cmcStatGuardDownDaySterling`), so no save-compatibility impact.

### Fixed
- **(1.45.0) The Jail's Ration Tray and Meager Prison Ration bowl both rendered as blank cards.**
  Same anti-pattern as the Water Jug entry below, caught this time by a full audit-chain sweep
  rather than manual inspection: `CMC_JailRationTray.json`'s `CardImageWarpData` was `"ClayPlate"`
  (the vanilla `ClayPlate` card's own UniqueID, not a sprite name) and `CMC_JailRationFood.json`'s
  was `"ClayBowl"` (same mistake, vanilla `ClayBowl` card). Real sprite names are `"Plate_Clay"` and
  `"Bowl_Clay"` respectively — confirmed against the vanilla `Bp_ClayPlate`/`Bp_ClayBowl` exports,
  and `"Plate_Clay"` is already used correctly elsewhere in this mod (`GlazedClayPlate.json`).
- **(1.45.0) A retry of the village-territory guard-pursuit boundary tag could leave stale,
  destroyed-object tag references stamped on already-tagged map cards.** `GuardDutyPatch.cs`'s
  `EnsureVillageTerritoryTag()` stamped its `CardTag` onto each village node as it walked the list;
  if a later node in the same pass failed to resolve, the shared tag instance was destroyed while
  earlier nodes in that same pass kept holding a reference to it, and every retry created a new tag
  and repeated the same partial-stamp risk without ever clearing the stale one. Rewritten as a true
  two-pass commit: every node is resolved and staged first, and the tag is only written onto any
  card once the entire batch resolves cleanly (with a rollback path if an individual write
  unexpectedly fails mid-commit) — a failed attempt now leaves zero cards holding a reference to the
  discarded tag, so a retry starts clean. Found by `/code-quality`, not observed as a player-facing
  symptom in this release.
- **(1.45.0) Five NPC-spawn paths (Ash's Partner spike, the Town Guards, the Inn Keeper, both
  Cottage residents, and the Professor) had no log output if `CreateNPC` succeeded but the
  follow-up identity lookup in `GameManager.AllNPCs` came back empty** — a silent no-spawn
  indistinguishable from "nothing went wrong." Each of the five `SpawnAndReturn` helpers now logs a
  warning naming the NPC when this happens, so a future framework/game-version change to NPC
  identity semantics is diagnosable instead of invisible. Found by `/code-quality`, not observed as
  a player-facing symptom in this release.
- **(1.45.0) The Jail Cell's Water Jug (`cmcJailWaterJug`) rendered as a blank card.** Its
  `CardImageWarpData` was `"WaterPouch"` — the human-readable name of the vanilla Water Pouch card,
  not its actual sprite name. Confirmed against the vanilla `Bp_WaterPouch` export that the real
  sprite name is `"Waterskin"` (`WaterPouch` appears nowhere as a `CardImageWarpData` value in
  vanilla data). Fixed by pointing the jug at `"Waterskin"`.
- **(1.44.4) The "Exit" action on all 6 village interior doors (Inn, Academy, Apothecary's Cabin,
  Miller's Cottage, Village Hall, Weaver's Cottage) was still invisible after the 1.44.x
  `AlwaysShow` fix below — confirmed in-game (player permanently trapped inside the Miller's
  Cottage interior with no usable action on the door).** Root cause was misdiagnosed the first
  time: `AlwaysShow: true` only bypasses `CanAppear()`; it does **not** bypass
  `CardAction.WillHaveAnEffect()`, and `WillHaveAnEffect()` DOES evaluate `TravelToPreviousEnv`
  (via `WillProduceCards()`, confirmed by decompile) — but only returns true when
  `EnvID.GetPrevEnv()` resolves to a real card. `EnvID.GetPrevEnv()` reads `EnvID.ParentEnvs`,
  which the engine populates **only for `InstancedEnvironment: true` environments**
  (`EnvID(CardData, EnvID, int)` ctor). All 6 interiors are `InstancedEnvironment: false` (correctly
  — see the entry below on why flipping that flag is worse), so `ParentEnvs` is always empty and
  `TravelToPreviousEnv` can never produce a destination for these doors — the Exit button was
  permanently un-renderable, `AlwaysShow` or not. Fixed for real this time by dropping
  `TravelToPreviousEnv` entirely and giving each door's Exit action an explicit `ProducedCards`
  drop of the specific outdoor environment it should return to (`cmcEnvVillage` for five of the
  six; `cmcEnvForagingForest` for the Apothecary's Cabin) — the same explicit-destination pattern
  the Village Jail Cell door already used successfully. Because this doesn't depend on any
  per-session parent-env history, it also un-sticks saves where a player is already trapped inside
  one of these interiors — no `InstancedEnvironment` flag or save data needs to change.
- **(1.44.x) The "Exit" action on all 6 village interior doors (Inn, Academy, Apothecary's Cabin,
  Miller's Cottage, Village Hall, Weaver's Cottage) was invisible, trapping the player inside with no way
  out.** Each door's `Exit` `DismantleAction` relied only on `TravelToPreviousEnv: true` to leave —
  but the game's `WillHaveAnEffect()` visibility gate (which decides whether a `DismantleAction`
  renders as a button at all) does not check `TravelToPreviousEnv`. With `ReceivingCardChanges.
  ModType: 0` and `DaytimeCost: 0` and nothing else set, every one of these six actions evaluated
  to "no effect" and never rendered, leaving the door's action panel completely empty. Fixed by
  adding `"AlwaysShow": true` to each door's Exit action, matching the pattern already used
  correctly by the Village Jail Cell door. **⚠ Superseded by the 1.44.4 entry above — this
  diagnosis was incomplete and the fix did not actually resolve the softlock.**
- **Entering the Inn (and 5 other singular interiors) permanently softlocked the game
  ("I can't do two things at once..." on every subsequent action).** The Academy, Apothecary's
  Cabin, Inn, Miller's Cottage, Village Hall, and Weaver's Cottage interiors were all flagged
  `InstancedEnvironment: true`. That flag makes vanilla's `WorldMapData.AddInstancedEnv` register
  the interior into `CoordsDict` keyed by its (unassigned) `Coords`, which defaults to
  `(0,0,0,0)` for a singular interior with no sibling instances — so the *second* one of these six
  interiors entered in a session collided on that key, threw inside the `ChangeEnvironment`
  coroutine, and left the action-lock permanently held. All 6 are now `InstancedEnvironment: false`,
  matching the Village Jail Cell (which never hit this because it already shipped with the flag
  off). `InstancedEnvironment` is only correct for templates the engine spawns multiple
  independent instances of (e.g. vanilla `Env_Cabin`); these are unique, persistent, named
  locations. A save that already has two of these envs recorded at `(0,0,0,0)` may still crash on
  load — test from a save taken before either was first entered, or a fresh game.

### Added
- **Homestead perk now grants portable Cabin/Rain Cistern kits instead of auto-placing the
  structures.** Previously `Perk_Homestead.json`'s `AddedCardsWarpData` referenced the real CT2
  structure UIDs directly (`7042e3f52e632a2408319de344a3aa0c` Cabin, `536f722edbb5e9e4b959b1f3ad25f648`
  Rain Cistern ×2) — the same mechanism vanilla's `Pk_5_12_CabinStart` uses — which spawns them
  straight onto the starting board, since `GameManager.GetStartingCardsFromArray` falls back to a
  direct board-drop for `CardType.Location` cards passed through a perk's `AddedCards`. Added two
  new CT0 item cards, `CMC_HomesteadCabinKit` and `CMC_HomesteadRainCisternKit` (`DoesNotPile`,
  single-use), each carrying a `Place` `DismantleAction` (`ModType: 2` Transform,
  `SpawnTransformAtSource: true`) that turns the kit in-place into the real structure — the same
  kit-then-transform pattern already used by `CMC_MarketStallKit`/`HuntingStandKit` in this mod.
  The kits land in the player's starting inventory (CT0 items resolve through the same
  `_UseDefaultInventory: true` path the perk's building materials already use) so they can be
  carried to any site before being placed, and each is consumed on placement — one placement per
  kit. `Perk_Homestead.json` now points `AddedCardsWarpData` at the two kit UIDs instead of the
  structure UIDs; description text updated to match.
- **The Village Jail** (Village Guards system, `Village_Master_Plan.md` §10.8.7) — losing to the
  Watch now has somewhere to put you.
  - **Arrest is a real transition, not a flag.** Each guard's Encounter now sets
    `PlayerDemoralizedEffects.MovePlayer = MoveToSpecificArea` at the new Jail Cell with a 1-hour
    `MoveDuration`, so vanilla's own post-encounter travel path carries the player into the cell on
    the same "Continue" click that ends any other fight. No Harmony patch, no coroutine.
  - **A sentence you have to sit out.** Roughly one day per 8 points of Village Crime at the moment
    of capture, floored at 1 day and capped at 8. The cell door's "Walk out" action is hidden
    outright until the sentence reaches 0, and eight read-out actions on the same door show how many
    days are left. Each day served decrements the sentence and pays down the crime score at the same
    8-points-per-day rate; release clears the record outright.
  - **A warden rotation.** Every guard now carries a `Duty_JailWarden` alongside their patrol and
    chase duties, with a fixed non-overlapping shift block each (Sterling 05:00-11:00, Thorne
    11:00-17:00, Corrin 17:00-23:00, Vane 23:00-05:00 — inside her own night window) so the post is
    always rostered. Vanilla's weighted-duty engine does the switching; CMC does not hand-drive it.
    The warden stands on the jail's step in the village square rather than inside the cell: duty
    movement resolves through the WorldMap graph, and the cell is deliberately not a map node.
  - **A rare gap.** Each hour there is an 8% chance the whole Watch leaves the jail step for two
    hours. This is enforced on the duties themselves — a hidden `cmcStatJailWardenGap` countdown
    that fails every warden duty's `DutyConditions` — so the guards genuinely walk off rather than a
    flag merely claiming they did. `cmcStatJailUnguarded` is then read back from what the guards are
    actually doing. The hidden escape tunnel this window exists for is the next bullet.
  - **Daily rations and a starvation safety net (§10.8.7.4, Risk R9 — release-blocking).** A
    "Meager Prison Ration" bowl and a Water Jug are placed in the cell once per in-game day while a
    sentence is being served, each an explicit `ModType:3` Eat/Drink action. Verified against real
    vanilla `GameStat` decay data (not estimated): Weight decays -12 to -23 per 15-minute tick
    depending on its own band (independent of Satiation, contrary to the original plan's
    assumption); Hydration — a *different* stat from Thirst, and the one that actually carries a
    GameOver floor ("Dead of Thirst" at 0) — decays at a flat -1/tick. Across the worst-case 8-day
    sentence the ration keeps Weight net-positive and Hydration net-positive under baseline decay.
    Beyond the ration, a disableable safety net (`EnableJailSafetyNet` config, default on) polls the
    player's actual Weight/Hydration/Satiation every tick and force-tops-up any of them that cross a
    threshold with a wide margin above their floor — the real guarantee against R9, since Weight's
    decay can stack with cold/fever/parasites the ration math alone doesn't model.
  - **Ways to pass the time.** "Meditate" (2-hour, no material effect — an optional Stress tie-in
    was considered and deliberately left out, unresolved design choice) and two flavor actions
    ("Count the stones," building to a 100-count milestone payoff) on the cell's own room card. The
    cell's bed also carries vanilla's own Nap/Sleep actions verbatim, reused as-is rather than
    building a bespoke rest mechanic.
  - **Known gaps, stated plainly.** The Jail and its cell ship with blank placeholder art
    (`CMC_Jail.png`, `CMC_JailCellInterior.png`) — real art drops over the same filenames with no
    JSON change. None of this has been verified in-game.
- **The Hidden Tunnel** (Village Jail escape, `Village_Master_Plan.md` §10.8.8) — a way out of a
  cell sentence besides waiting it out.
  - **Move the bed, find a tunnel.** The cell's bed can be shoved aside and back in place at will,
    swapping in place for a Tunnel card on the same tile (`CardUtil.TransformCardInPlace`, the first
    REPEATED player-driven toggle use of that primitive in this codebase rather than a one-shot
    upgrade/process transform — flagged as risk R10 and requiring its own repeated-toggle,
    across-a-reload verification pass before being trusted).
  - **Digging only works during the same guard-absence window the warden rotation already tracks** —
    no separate detection roll. Each Dig advances a hidden `cmcStatJailTunnelProgress` counter and
    costs 30 minutes; the Dig button is hidden outright (not merely greyed) whenever a guard is
    actually on the step.
  - **Getting caught.** If the Watch returns while the tunnel is still exposed, the tunnel is caved
    back in (progress reset to 0, card force-transformed back to Bed), the sentence grows by 50% of
    its ORIGINAL length, and a small crime penalty is added for the attempt. Re-hiding the tunnel
    before the window closes avoids all of that and keeps whatever progress was already dug.
  - **A finished tunnel** adds a "Crawl through the tunnel" action that clears the remaining
    sentence, drops the player at the Village Path outside the walls, and adds its own (separate,
    harsher-reads) crime bump for "escaped custody" — an escaped convict caught again is not treated
    like a first-time arrest. Recapture-penalty and escape-crime-bump sizes are both open-question
    placeholders (§10.8.10 Q12), not tuned values. Not verified in-game.
- **8 perishable items and placed decor now carry `tag_Preservable`** (Apothecary Healing Potion,
  Herb Paste, Heather Wreath, Woven/Tricolor Wall Hangings, Straw Mat, Garden Trellis, Bedroll). This
  is a vanilla CardTag with no shipped vanilla users; third-party mods that bulk-match spoilage-rate
  effects onto it (e.g. freshness/preservation perk mods) now apply correctly to these items. Ash's
  Trail, Shadow Cat, Inn Cat, and the Academy Lectern were deliberately excluded — their `SpoilageTime`
  channel is either relabeled for a different mechanic (Hunger, a Fishing progress counter) or has a
  zero decay rate, so tagging them would be either semantically wrong or a no-op.
- **The Miller's Copper Chest** (Village Guards system, `Village_Master_Plan.md` §10.8.3) — a
  storage chest inside the Miller's cottage interior that is his savings, his spending power, and
  a burglary target all at once, all read off the one physical inventory.
  - **Weekly accrual** — once the Miller has moved in he adds ~3 Salt and 2 Wheat Flour to the
    chest about once a week, applied the first time you enter the cottage on or after the due day
    (the chest lives in an instanced environment, so it is only reachable while you are standing
    in it). Currency pauses at 300 in value, goods at 10 cards, each independently.
  - **Sell to the Miller** — drag a valuable item onto the chest to sell it, priced on the same
    `ObjectWeight / 10` scale the Market Stall uses. The sale is refused outright, with nothing
    consumed, when the price exceeds what is physically in the chest — so a freshly settled Miller
    genuinely cannot afford an expensive item, and the same Miller weeks later can. He pays from
    his own hoard smallest-coin-first, and overpays rather than blocking a sale he cannot make
    exact change for. No crime cost — this is an ordinary trade.
  - **Search for valuables** — empties the chest to you and rolls for detection: instant catch if
    the Miller is home, otherwise 15% base + 5% per earlier undetected theft this season (heat
    resets on a season change and on being caught) + 20% while Iris Vane's night watch is out.
    Detected costs 10 Village Crime — the **first mechanic in the mod that actually raises that
    stat**. Undetected leaves no record at all.
  - **Miller only this release** — the Weaver, Apothecary, Inn Keeper and Professor chests are a
    later chunk, per the plan's own one-cottage-first sequencing.

- **Village Crime foundation** (Village Guards system, `Village_Master_Plan.md` §10.8.2) — a new
  hidden `cmcStatVillageCrime` stat (0-100), its four bands (Clean / Suspected / Wanted /
  Enemy-Banished), a 1-per-day passive decay for the two middle bands, and the declarative
  banishment travel lock on the Village Path connection gate. The Copper Chest's theft roll (above)
  is the first and so far only thing that raises it (+10 per detected burglary); attack detection
  is a later chunk. Requires framework 2.20.2+ for the travel lock to be read at all.
- **The Town Watch** (Village Guards system, `Village_Master_Plan.md` §10.8.1) — four guards now
  live on the village map and walk their own beats: **Captain Reeve Sterling** holds the village
  square, **Guard Nella Thorne** works the river-bridge junction on the Village Path, **Guard Old
  Corrin** walks the farm and clay-flats round, and **Guard Iris Vane** takes the night watch,
  appearing only between dusk (20:00) and dawn (05:00) and staying out of sight the rest of the
  day. Each guard carries their own hidden Suspicion rating — the tuning knob the later theft and
  attack-detection systems read — with Iris noticeably sharper-eyed than the rest and Corrin the
  slowest to suspect anyone. Portraits are placeholders reusing existing art. Requires framework
  2.20.0+.
- **You can attack the Watch** (Village Guards system, `Village_Master_Plan.md` §10.8.4) — each of
  the four guards now has an **Attack** button on their inspection panel. It opens a confirmation
  first: back out and it costs you nothing, commit and you take **+35 Village Crime the moment the
  fight starts**, win or lose. That is enough on its own to put a clean player in the Wanted band.
  - **Guards usually break and run rather than die.** Each guard has their own fight, and morale
    drains about twice as fast as blood, so a beaten guard normally routs — hurt, alive, and now
    with a very good reason to remember you. Killing one is possible but takes real determination,
    and it pins Village Crime at its 100 ceiling: instantly and permanently Banished.
  - **Each guard fights differently.** Old Corrin gives up early and takes a lot of punishment to
    actually kill; Nella Thorne holds her nerve far longer than anyone else, which makes her the
    one most likely to die if you push. Sterling and Vane sit between them.
  - **A guard standing in the same place when you start a fight notices immediately** rather than
    on their next patrol beat — the witnessing guard's own pursuit check re-runs on the spot
    instead of waiting for the normal patrol tick, and Nella Thorne now acts on the resulting
    Crime score without that lag (see the pursuit entry below).
  - **Guards only.** The Miller, Weaver, Apothecary, Inn Keeper and Professor cannot be attacked;
    proving the pipeline on the Watch first is deliberate.
- **Guard Nella Thorne hunts you once you are Banished** (Village Guards system,
  `Village_Master_Plan.md` §10.8.5) — the first guard pursuit. Once your hidden Village Crime
  reaches **60 or more** (the Enemy/Banished band — two attacks on the Watch will do it), Thorne
  abandons her river beat and walks the map toward wherever you actually are, one node per move,
  re-routing as you run. Stand still and she catches up; when she reaches your location she starts
  a real fight on the spot.
  - **She will not cross the river.** The pursuit only holds while Thorne is standing on
    village-side ground; step onto the vanilla map and she breaks off and heads back to her beat.
    She may follow one node past the boundary before turning around — she is checked when she picks
    her next move, not mid-stride.
  - **Drop back below 60 Crime and she goes back to patrolling**, immediately.
  - Only Thorne pursues for now — the other three guards keep to their beats until this one is
    proven in play. Losing the fight does not yet jail you; the jail is a later chunk.
- **`ForceClearVillageCrime` debug config** — admin override that holds the crime stat at 0, so a
  tester locked out of the village by a future banishment can recover without editing a save.
- **`EnableGuardDiagnostics` debug config** — keeps re-logging each guard's duty weights and the
  engine's own duty-selection reason string while patrol behavior is being tuned.

### Changed
- **The Miller's weekly satchel restock has been RETARGETED onto his Copper Chest, not duplicated
  into it** — his personal carried inventory is no longer topped up every week; his trade stock is
  now his starting inventory plus whatever is in the chest. This is deliberate (the design calls
  for one accrual, not two); a build that kept both would double-drop every week.
- `VillageCrimePatch` gains `ReduceCrime(amount, reason)` — a partial pay-down that, unlike the
  daily decay, deliberately works inside the Banished band, so days served in the jail count.

### Fixed
- **README understated the live Village Crime system.** The Requirements section still claimed
  "nothing in the mod raises the crime stat yet... this currently has no effect in play," which was
  stale and directly contradicted the README's own Town Watch section — attacking a guard (+35),
  killing one (+100, instant Banished), getting caught robbing the Miller's Copper Chest (+10), and
  getting caught mid-dig in the jail escape tunnel all already raise it, on any framework version.
  Only the banishment travel-lock *enforcement* needs framework 2.20.2+; Crime itself was never
  framework-gated. Documentation-only correction, no behavior change (found by `/critical-analysis`).
- **`CMC_JailRationTray` was missing both its localization keys** (`CardName`, `CardDescription`)
  from `SimpEn.csv`/`SimpCn.csv`. English displayed correctly via the JSON `DefaultText` fallback, so
  this was invisible in an English playtest; Chinese players saw raw English. Both rows added with a
  translated Chinese row.

## [1.44.1] — 2026-08-07

### Added
- **Simplified Chinese localization** — `Localization/SimpCn.csv` now covers all 1,771 player-visible
  keys (full parity with `SimpEn.csv`: 0 missing, 0 untranslated, 0 stale). Terminology follows a
  dedicated glossary (`Documentation/Design/Chinese_Glossary_CMC.md`) built from vanilla's own
  Chinese strings and established ACT/WDI/H&F house style, with per-NPC register notes so dialog
  voice (Inn Keeper's warmth, the Professor's formality, etc.) survives translation.

## [1.37.1] — 2026-08-06

### Fixed
- **The Professor no longer forces a throwaway "Go on." click before showing his topic menu.**
  His greeting was still split into two steps — a flavor line with a single "Go on." answer, then
  the real menu — left over from the 1.37.0 consolidation of his seven duplicate entry lines into
  one canonical menu. Every other village NPC (Inn Keeper, Miller, Weaver, Apothecary) already opens
  straight into their topic list; the Professor's greeting now does the same, folding his opening
  line directly into the menu so the first thing you see is real conversation options.

## [1.37.0] — 2026-08-06

### Changed
- **Inn Keeper and Professor dialogue reworked for less repetition and more meaningful conversations.**
  The Professor's greeting used to duplicate his entire ~25-option topic menu across seven separate
  entry lines (default greeting, two trust tiers, four seasonal greetings) — hand-copied text that had
  quietly drifted out of sync with itself, so the "What is this place?" answer only correctly described
  your actual location if you happened to greet him through the one variant that had been fixed. All
  seven now share one canonical topic menu, so every fix and addition applies everywhere at once.
  The Inn Keeper's five seasonal fireside tales (Wolves, Miller's Wager, the Fish Tale, the Mushroom
  Peddler, the Ferryman) were each told across four short, mostly single-line "go on..." exchanges —
  they're now told in two longer, fuller passages apiece.
- **Both NPCs' weekly quest chains got a proper acceptance beat** — accepting a request no longer
  jumps straight to a fade-out; there's now a short, in-character reaction before the conversation
  closes, and the delivery "thank you" lines are longer and more specific about what you brought.

### Added
- **The Professor now reacts, once, the first time you pass each of his six Academy courses** —
  Metallurgy, Herbalism, Medicine, Fishing, Architecture, and Armorer each get their own short,
  subject-specific congratulations the next time you talk to him, instead of only ever seeing a generic
  "how many courses have you finished" summary.
- **Both NPCs now react to village construction milestones.** The Professor comments, once, when the
  Apothecary's Cabin and the Village Hall are finished; the Inn Keeper comments, once each, when the
  Miller's Cottage, the Weaver's Cottage, and the Village Hall are finished, and again when Village
  Renown crosses the halfway and full-renown marks.

## [1.36.2] — 2026-08-06

### Fixed
- **The Inn Keeper, Professor, Miller, Weaver, and Apothecary stopped appearing in the village at all** —
  the game's EA 0.66 update changed the internal method the game uses to create an NPC, and every one of
  this mod's village-resident spawn routines was still calling the old version. Nothing indicated an error
  to the player; the village just stayed empty aside from Ash (who isn't spawned the same way). All five
  residents now spawn correctly again.

## [1.36.1] — 2026-08-06

### Fixed
- **Removed an unfinished, unreleased test companion agent that could unexpectedly appear in your game.** An
  in-progress experiment for a future "Ash as a hands-free companion" feature was accidentally left switched on:
  it spawned a second character also named "Ash," using the same portrait as your tamed Inn cat, with
  placeholder debug buttons visible in its card popup. This was never finished or intended to ship — it's now
  disabled again. Nothing about your existing tamed Ash is affected.
- **README's framework requirement now correctly lists 2.19.0+ for the winter road blockage.** The winter
  snow-drift feature (1.35.0) needs a newer `CSFFModFramework` than the mod's previously-listed minimum; on an
  older framework the roads simply never sealed for winter, with no indication why. Documentation only — no
  behavior change.
- **`Powerful Healing Potion`'s carry weight corrected from 0.5 to 50** — a scale error left it registering as
  nearly weightless compared to every other item in the mod.
- Minor schema cleanup: 3 items' `CardImage` field now uses the correct placeholder object instead of a bare
  sprite name (cosmetic only — the actual artwork was never affected).

## [1.36.0] — 2026-08-06

### Added
- **New starting perk: Homestead (100 Suns).** Start with a Cabin and two Rain Cisterns — all
  fully portable, so you choose where to place them — plus a stockpile of building materials:
  30 Wooden Planks, 30 Mud Bricks, 30 Heavy Stones, 50 Stones, 10 Tree Logs, 50 Treenails, and
  10 Rope. Uses the same vanilla `AddedCards` grant the base game's own "Cabin Start" perk relies
  on, so the Cabin and Cisterns behave exactly like their vanilla counterparts (pick up, carry,
  drop anywhere to place).

## [1.35.0] — 2026-08-05

### Added
- **The Village gets snowed in every winter.** All three roads out of the Village — north to Deer
  Meadow, south to Pine Trail, and east to the Village Farm — now drift over with snow for the
  duration of winter, blocking travel until dug clear. Each drift takes 3 shovel strikes to clear
  (dropping Snow with each strike) and, once cleared, stays open for the rest of that winter — but
  the snow returns to block the road again the following winter. Requires CSFFModFramework 2.19.0
  (new `"Season"` gate-trigger support).

### Fixed
- **Inspecting a Village Hall board could leave the vanilla Time Options ("T" key) menu's description text permanently shrunk/truncated.** The board-status feature below only restored the shared description text object's font sizing on 4 of its 5 vanilla writers — the Time Options screen used the 5th, uninstrumented one, so it inherited the boards' truncated sizing instead of its own. Added the missing patch; Time Options now always renders at its normal size regardless of which board you looked at last.
- **Self-serve drinking from a Rain Cistern had no Care cost at all**, despite being advertised (1.34.1, "at the cost of your bond") as lowering a cat's Care with every automatic drink. The auto-drink code restored Thirst and drained the cistern but never touched the Care stat — a fully cistern-reliant cat's bond never actually degraded. Auto-drink now costs 25 Care per drink, zeroing a fresh cat's Care in about 4 drinks (~9-10 in-game days at the auto-drink trigger threshold), matching the original design. Also corrected the auto-drink trigger threshold in this document: it's Thirst below **20%**, not 30% as previously stated.
- **Telling the Inn Keeper "he wandered off" about Ash could permanently retire that check-in conversation.** Picking that answer while Ash was alive and present at the Inn decremented the same hidden progress stat used to detect a genuine wander-off, without checking whether he was actually gone — since the spawn logic for his replacement never fires while the original cat still exists, the stat could never climb back to the value needed to re-open the conversation. The answer now only fires its stat change when Ash isn't currently on the Inn's board.

## [1.34.1] — 2026-08-03

### Added
- **Village notice boards now show your standing with each villager.** Every Village Hall notice board (Inn Keeper, Miller, Weaver, Apothecary, Professor) now displays a live status readout beneath its notices: your **friendship** with that villager as a descriptive tier plus the raw value in parentheses (e.g. "The Inn Keeper considers you a good friend. (18 / 30)"), and your **quest progress** with them (e.g. "You are working through the Miller's errands. (2 / 3)"). The two **Village Standing** boards likewise show current **Village Renown** and whether the village has been properly introduced. Values update every time you open a board — no more guessing at hidden trackers.
- **Companion cats now self-serve water from a Rain Cistern — at the cost of your bond.** Ash and Shadow each gained a visible **Care** stat (their relationship with you). Whenever a cat's Thirst drops below 20% and a **Rain Cistern with water** is on the same board, it will automatically drink to refill its Thirst — but every self-serve drink lowers its Care a little, because you weren't the one who tended it. Left entirely to the cistern with no personal attention, a cat's Care runs dry after roughly **9 in-game days** and it wanders off (recoverable through its usual return path). **Petting, Feeding, or personally Giving Water** all restore Care, so a hands-on owner keeps a happy cat indefinitely. Implemented as a small tick-driven patch (`AshCatTickPatch.cs`) rather than vanilla's native `DurabilityTransferEffects`, since Care's decay needed to be tied to the auto-drink event itself. An empty cistern gives no water, so a fully neglected cat can still die of thirst on the old ~3-day clock.

### Fixed
- **The "Village Founder" perk no longer fast-forwarded the Miller's grain quest or the Weaver's flax quest.** A prior update moved those two quests onto the shared quest-chain system, but the Village Founder perk's fast-forward list still wrote the old, now-unused quest stats. Equipping the perk now correctly marks both quests as already completed, matching every other village beat it fast-forwards.
- **Completing the boar encounter with Ash did not drop a boar carcass.** The `AshBoarHuntPatch` resolved the fight (transforming Ash back to `cmcInnCat`, setting epilogue stats) but never spawned loot. Now spawns a vanilla `Carcass_BoarCarcass` at the current board (Hunter's Crossing) when the encounter ends in a player victory.
- **Ash the Cat's quest line soft-locked permanently if Ash ever wandered off (starved of food or water).** When Ash's Hunger or Thirst hit zero he was removed from the game, but nothing reset the hidden lost-cat quest stat (`cmcStatLostCat`) or the boar-hunt state (`cmcStatAshBoarHuntSpot`). The stray never respawned (it only reappears while the search is armed), the boar hunt never fired (Ash was gone before his 5-day timer), and the Inn Keeper was stuck forever asking "how's Ash?" with no way to recover. Added a recovery answer to that check-in greeting — telling the Keeper "he wandered off" re-arms the search, so Ash reappears in the Foraging Forest at dusk and can be re-adopted (which also restarts the boar-hunt clock). Fixes existing stuck saves and any future wander-off.
- **Wind effects persisted indoors in the Village Inn, Academy, and other village building interiors.** The "Wind Affinity" passive effect from outdoor grove environments was not suppressed when standing inside the village's closed indoor environments (Inn, Academy, Apothecary Cabin, Miller Cottage, Village Hall, Weaver Cottage). Added an inverse wind resistance modifier (RateModifier -0.5 on the wind affinity stat GUID) to the "Sheltered from the Elements" passive effect already protecting those interiors from rain, sun, and snow — wind effects no longer affect the player indoors.

## [1.33.8] — 2026-07-30

### Fixed
- **Trees never grew back once chopped down, in the Village, Village Farm, Village Path, and by extension every other CMC map location.** Every vanilla explorable location carries "Create Small/Large/Birch Tree" actions that silently re-plant a chopped tree once it's missing from the board. WorldMap/MapNodes.json's `StripLegacyBoardUIDs` (used on Village/Village Farm/Village Path to keep wild Nettle/Clover/Meadowgrass patches from sprouting in the finished settlement) matches and deletes any action that *produces* a stripped card UID — which caught the tree-respawn actions too, since they produce the same tree cards. Added a small env-scoped daily check (`TreeRespawnPatch`) that replaces the missing behavior across all twelve CMC map locations: once per in-game day, while standing in that environment, any of its native tree species missing from the board has a chance to grow back, keyed to the correct species per location (oak groves regrow oak, pine groves regrow pine, etc.).

## [1.33.7] — 2026-07-30

### Fixed
- **Rotten Remains piled up permanently in NPC inventories.** Food NPCs are carrying (deliveries, foraged stock, restock items) spoils the same as it does for the player, but no NPC action ever cleared the resulting Rotten Remains back out. Added a periodic sweep (every 15 in-game minutes) that removes any Rotten Remains found in an NPC's own inventory.

## [1.33.6] — 2026-07-29

### Fixed
- **Every card's Info-tab description text was locked to a shrunken font (worse after 1.33.5).** The Town Hall board sizing patch remembered the description font size from the first card inspected and then force-wrote it — with auto-sizing disabled — onto every non-board card's popup. Because the game's own text box auto-sizes natively, the remembered value was often an already-shrunk computed size, so every card inherited it permanently. The patch now snapshots the complete vanilla text state (auto-sizing flag, size, min/max, overflow) only at the moment a Town Hall board first modifies it, restores that exact state when a non-board card is next inspected, and leaves the text completely untouched otherwise. Only the 7 Town Hall boards' descriptions auto-shrink now.

## [1.33.5] — 2026-07-29

### Fixed
- **Font auto-shrinking meant for the Village Town Hall boards was shrinking every card's hover tooltip in the game.** The board description sizing (added in 1.33.2) is correctly scoped to just the 7 tracked boards, but a second, unrelated patch also force-enabled auto-sizing on the game's single shared hover-tooltip text object — since that object is reused for every card's hover box, the effect applied globally instead of just to the boards. Removed the unrelated global patch; only the Town Hall boards' description text resizes now.

## [1.33.4] — 2026-07-29

### Changed
- **The Village Well now shows its custom art immediately on construction**, instead of the vanilla "Well" sprite (a leftover tropical-island image that never matched the village). Previously the well was built with that vanilla placeholder and only got the intended art if you used the Well Plans sketch's "Re-face the Well" action afterward — but that swap wasn't reliably refreshing the on-screen image. Removed the two-stage indirection entirely: the well card itself now points at the correct art from the start, so there's nothing left to swap. The "Re-face the Well" action and the card it swapped to are gone; the sketch's "Discard the Old Sketch" action still works as before.

## [1.33.3] — 2026-07-29

### Fixed
- **Drawing water from the Village Well only filled a Rain Cistern by about 1% per drag.** The well's "Draw Water" action added a flat 300 units of water to whatever container was dragged onto it — plenty for a Waterskin or Cooking Pot, but a sliver of the Rain Cistern's 21,600-unit capacity. Changed to the same oversized flat quantity (100,000) vanilla water sources (River, Pond, Lake, etc.) use for their own "Fill" action — the game always caps the actual amount added to whatever room is left in the container, so this now tops off any container, including a Cistern, in one drag.

## [1.33.2] — 2026-07-29

### Fixed
- **Lecture Hall description overflowed onto the discipline icons and Study buttons below it.** The card description crammed in a per-subject breakdown of every course's hour length and every degree's unlock effect, making it nearly 2.5x longer than any other location card's description in the mod and long enough to run past the popup's text box. Trimmed to the funding mechanic only; each subject's course length and unlock effect were already restated in that subject's own Study/Final Exam action description, so no information was lost.

## [1.33.1] — 2026-07-29

### Fixed
- **Re-faced Village Well showed a blank card image.** The re-faced well (`cmcimpwellcustom`) pointed at a placeholder sprite that was never replaced with real art. It now reuses the Well Plans artwork (the same stone-well sketch shown on the Inn Keeper's plans) instead of a blank image.

## [1.33.0] — 2026-07-28

### Added
- **Matching Inn/Academy card art for the Miller, the Weaver, and the Apothecary.** Each now shows a dedicated portrait while standing inside the Village Inn (`CMC_Miller_Inn`/`CMC_Weaver_Inn`/`CMC_Alchemist_Inn`) or the Village Academy (`CMC_Miller_Academy`/`CMC_Weaver_Academy`/`CMC_Alchemist_Academy`), falling back to their existing Village portrait everywhere else — the same envCard-driven portrait sync the Professor and Apothecary already used, extended to cover these two new locations.

### Changed
- **The Miller and the Weaver now visit the Inn every evening and the Academy once a week, guaranteed** (previously an independent 13%/12% daily roll for each, so either visit could go missed for a stretch of days). Every evening (18:00–21:00) both residents head to the Inn; each also has one fixed day per week (staggered so they don't both go the same day) for an Academy afternoon (13:00–17:00). Occasional outdoor wandering on non-Academy days is unchanged, still a roll.
- **The Apothecary now also visits the Inn every evening and the Academy once a week.** Her stall hours end at 19:15 as before, but she now stops at the Inn (19:15–21:00) before walking home instead of leaving immediately, and one day a week she spends 13:00–17:00 at the Academy instead of the stall. Both legs share the same instanced-env visit-once-to-unlock prerequisite as her cabin homing, and neither fires while she's actively brewing a healing potion.

## [1.32.1] — 2026-07-28

### Fixed
- **Town Hall board descriptions and the 7 village boards' action visibility now actually work.** Since the Town Hall Boards feature shipped (PR-3, 1.x), the Harmony patch that renders each board's gated status lines and hides its inspection-popup action buttons was silently failing to register at all — a reflection lookup for the game's `InspectionPopup.Setup` method didn't specify which of its two overloads to target, and the resulting ambiguity exception aborted patch setup before any of the four board-related patches applied. Boards affected: Town, Town Construction, Inn Keeper, Miller, Weaver, Apothecary, Professor. No player-facing symptom beyond the boards never showing their intended extra text — the mod otherwise loaded and ran normally, which is why this went unnoticed.

## [1.32.0] — 2026-07-28

### Added
- **Village Well art choice.** Once the Well is built, the Well Plans sketch now offers two one-time, mutually-exclusive options while you stand at the finished well: the existing **Discard the Old Sketch** (keeps the well's original vanilla stonework), or the new **Re-face the Well**, which re-skins the well with custom forest-cut stone art in place of the legacy "Well" sprite (a holdover asset that reads as a tropical-island well rather than a Fantasy Forest one). Both options consume the sketch either way. Implemented via an in-place `CardModel` swap (`WellArtPatch.cs` + `CardUtil.TransformCardInPlace`, never `Object.Destroy`), retargeting the on-board well card to an otherwise-identical structure (`cmcimpwellcustom`) that differs only in its art. Real custom art is pending delivery — ships for now as a blank placeholder PNG.

### Changed
- **Village NPCs now walk their commutes instead of teleporting.** The Professor, the Miller, the Weaver, and the Apothecary previously jumped straight from their current spot to their next scheduled destination in a single hop. They now visibly step across each intermediate map tile along the way (paced at roughly 45 in-game minutes per tile), so a long commute reads as an actual walk rather than a teleport. Purely a presentation change — schedules, destinations, and timing windows are unchanged.

## [1.31.1] — 2026-07-27

### Fixed
- **The Village Hall's "Renown" score, and the Market Stall's 25% sales bonus it unlocks, are now correct no matter where you're standing.** Renown used to be recomputed from a live scan of whatever's physically on the board around you, so the moment you walked away from the Village, Miller's Cottage/Weaver's Cottage/the Well/the River Bridge/the Market Stall would all read as "not built" and Renown would drop — silently resetting the Market Stall's sales bonus back to nothing well before its once-a-day unattended sale ever had a chance to use it. The Village Hall's own Notice Board still looked correct whenever you actually checked it (you have to be standing at the Hall to see it, and everything else is right there with you at that moment too), which is exactly why this went unnoticed. Village construction now counts toward Renown permanently once built, and the Market Stall's sales-milestone bonus is remembered for good the first time it's earned — both regardless of where you currently are.

## [1.31.0] — 2026-07-27

### Added
- **Vanilla trading-value table (`TradingValues.json`)** — NPC trading is no longer full of
  0-cost goods. 653 vanilla items/liquids that shipped with `TradingValue: 0` (planks, rope,
  berries, meat, hides, pottery, tools, carcasses, brews, …) now have prices calibrated to
  vanilla's own scale (bugs/salt 5, herb powders ~17, dried meat 40, flint/metal nugget 250,
  cloth clothing 1500). Deliberately left at 0: truly abundant materials (stones, leaves,
  needles, twigs, grass, snow, water), waste (ash, manure, urine, rotten things), field/planted
  cards, wounds, spirits, and debug/unused cards. Applied via the framework's new declarative
  `TradingValues.json` loader (requires framework ≥ 2.18.0; without it the file is inert and
  trading simply stays vanilla). Full review table: repo `Documentation/TradingValues_Review_2026-07-27.csv`.

### Changed
- **CMC tradeable items priced** — Rare Herb Mixture 200, Powerful Healing Potion 350, Market
  Stall Kit 400, and the 8 apparel pieces (Cloth Coat 1500, Leather Apron 1200, Chaperon 800,
  Leather Sandals 800, Cloth Scarf 600, Straw Hat 200, Foot/Hand Wraps 150). Ash, Shadow, the
  stray-cat cards, and quest items (Well Plans, "Ash, On the Hunt") intentionally stay 0 —
  companions and quest state are not market goods.

## [1.30.1] — 2026-07-27

### Fixed
- **The Well Plans no longer haunt your pack after the well is built.** The dig now consumes the plans as one of the listed build materials, so for new wells the card retires itself. For a well that was already standing before this version, the plans card gains a **Discard the Old Sketch** action that appears only while you stand at the finished well — so the plans can never be thrown away while they are still needed. Before the well is built the card stays protected from trashing, since the Inn Keeper only ever hands it over once.

## [1.30.0] — 2026-07-27

### Changed
- **The Village Well is now a real card on the village board.** It used to be a village-tile improvement: built from the tile's improvements panel, and once finished the well lived inside that panel, where it was awkward to reach and easy to miss. It is now a standard construction blueprint like the cottages and the Village Hall — hold the Inn Keeper's **Well Plans** while at the Village and the blueprint appears in the crafting journal; research it, then build it with the same materials and build time as before. The finished well stands on the village board like any other structure: drag an empty water container onto it to draw water.
- **The Well Plans card now visibly matters.** Keeping it in hand is what reveals the Village Well blueprint — it is no longer an inert keepsake after the Inn Keeper hands it over.

### Notes
- The well keeps its old card identity, so Village Renown, the Village Projects board, the Inn Keeper's well remark and village toast, and Ash's storyline gate all track the new structure unchanged. A well already finished on an existing save is expected to carry over as the new structure card; if it does not appear on the village board after updating, the blueprint offers the rebuild path.

## [1.29.5] — 2026-07-27

### Fixed
- **Players got wet from rain inside every village building.** The Inn, Academy, Apothecary's Cabin, Village Hall, Miller's Cottage, and Weaver's Cottage interiors never granted the vanilla indoor-shelter stat effects (Rain Protection, Sun Protection, Sheltered, hidden snow FX), so standing inside them was mechanically identical to standing outdoors — rain wetness accrued normally. Added the same "Sheltered from the Elements" stat grant vanilla indoor rooms (e.g. the player Cabin) carry, keyed to each building's interior environment card via `Conditions.NotInBackground` (the same mechanism already used by the existing Hearth-Warmed Inn/Academy/Village Hall warmth effects).

## [1.29.4] — 2026-07-28

### Added
- **Miller & Weaver daily schedule.** Both cottages are now enterable ("Enter the Cottage" / "Exit", mirroring the Apothecary's Cabin). Each resident now keeps a daily routine instead of standing at the Village around the clock: home inside their own cottage overnight (22:00–6:00), at the Village the rest of the day, with occasional half-days spent wandering the outdoor map (Village Path/Farm, Foraging Forest, Pine Trail, High Grove, Mossy Clearing), visiting the Inn in the evening, or visiting the Academy in the afternoon. The schedule never moves a resident away mid-conversation, and a resident won't travel to the Inn/Academy/their own cottage interior until you've visited that interior yourself at least once this session (falls back to standing at the Village until then).

## [1.29.3] — 2026-07-27

### Fixed
- **Inn Keeper's fireside tales booted you out after every line.** Each seasonal story arc (the wolves, the mushroom peddler, the fish that got away, the miller's wager, the ghostly ferryman, and the cat's second life) advanced with a step type that meant "end the conversation" instead of "go to the next line," so you had to re-open Talk for every single sentence. All 18 mid-story chapters now flow straight through to their conclusion.
- **NPC "≥ 1" quest/errand gates could never be satisfied — the garlic errand loop.** Any dialog answer, drag-and-drop action, or interaction gated on a stat being "1 or more" used the range `[0.5, 1000000000.0]`, which overflows the game's range math (a fractional lower bound forces a ×10 decimal pass, and `1e9 × 10` overflows a 32-bit integer into a large negative number, making the range impossible to satisfy). This is why delivering **Dried Wild Garlic** to the Inn Keeper never registered even with the correct item in hand — the delivery action was permanently hidden after the first errand. Same defect also silently blocked several "after they moved in" villager interactions (Miller flour hand-off, Weaver/Apothecary pigment steps). Replaced the broken upper bound with a safe large value across all affected gates.

### Changed
- **Village Hall boards cleanup.** The Boards room now opens each villager board directly without a redundant "Read ... Board" action, and the two civic boards were relabeled to **Village Standing** and **Village Projects** to make their roles clearer at a glance.
- **Village Hall boards now render notices as prose instead of button labels.** The Boards room keeps using the existing gated notice data, but the current board entries are now written into the description panel and removed from the action strip so long lines are readable.

## [1.29.2] — 2026-07-23

### Fixed
- **Village Founder + "already completed this course" softlock.** Village Founder granted all
  6 Academy graduate perks directly (bypassing the Lecture Hall), but never backfilled the
  lectern's course-progress stats to match — `AcademyCourseService.HasCourse` reported every
  course as done while the progress bars stayed at 0%, so every "Study ..." button permanently
  showed "You have already completed this course" instead of allowing (harmless, already-passed)
  study sessions to proceed. Fixed by backfilling each course's progress stat to its max hours
  the first time the player stands in the Academy interior.

### Changed
- **Split the Academy grant out of Village Founder into a new standalone "Graduate" character
  perk** (`cmcperkgraduate`, `CharacterPerk/Pk_Graduate.json`, `Patcher/GraduatePerkPatch.cs`).
  Village Founder (3 Stars) now grants only its village-building content (structures, residents,
  NPC first errands); Graduate (3 Stars) grants only the 6 Academy graduate perks + progress
  backfill. The two are independent and can be taken together or apart. `Pk_HigherEducation`
  (already retired) is untouched.

## [1.29.1] — 2026-07-23

### Fixed
- **Portal Hub "Travel to Village" landed at Village Path instead of the Village.**
  `MapMod.json` registered `EnvironmentUID: "cmcEnvVillagePath"` (the road node connecting
  Village Path/High Grove/Clay Flats/Hunter's Crossing), not the actual Village node
  (`cmcEnvVillage`, home to the Inn and Academy). Since `PortalService.RegisterHubTravelHandlers`
  prefers `EnvironmentUID` over `SacredSiteUID` and both fields pointed at the Path variant,
  every "Travel to Village" click from any registered mod's portal hub dropped the player one
  map node short of the village proper. Repointed both fields to `cmcEnvVillage`/`cmcLocVillage`.

## [1.29.0] — 2026-07-23

### Added — Shadow the Cat + Apothecary's Cabin themed finishing stage
- **Shadow the Cat**: a second, wholly independent companion-cat chain (`cmcShadowCat` →
  `cmcShadowCatTamed`, `Patcher/ShadowCatPatch.cs` templated directly on `LostCatPatch.cs`)
  sharing zero UIDs/files/state with Ash. Spawn-eligible in the Foraging Forest between dusk
  and dawn (17:00–06:00) only once Herbalism is graduated (`AcademyCourseService.HasCourse`),
  with no hidden GameStat gating the spawn — the dupe-guard (a live `GameManager.AllCards`
  scan for either cat form) is the sole thing preventing repeat spawns, since the perk check
  stays true forever once earned.
- **Taming is deliberately different from Ash's feed-to-tame arc**: drag any of the three
  vanilla musical instruments (Wooden Flute, Bone Flute, Frame Drum) onto her and she tames —
  none of the three are consumed (`GivenCardChanges: {"ModType": 1}` with every change field
  left at zero/false, the confirmed non-destructive no-op shape, precedented by vanilla
  Fibers.json's "Comb" interaction).
- **Apothecary's Cabin finishing stage** (7th stage, appended to the existing 6-stage vanilla
  backbone): Dried Hemp Flower ×2, Dried Ginseng ×2, Dried Reishi ×2 (all Herbs & Fungi items),
  Dried Nettle Leaves ×3 (vanilla), and one tamed Shadow — `DontSpend: true`, present but never
  destroyed. Ingredient list is a direct, verbatim reuse of the existing Healing Mixture
  blueprint's own rare-herb list, not a new invention.
- **H&F is now a hard dependency for finishing the Apothecary's Cabin** — three of the four
  finishing-stage ingredients have no Community Mod Chest substitute. Documented in README
  (Contents + a new Requirements bullet) and ModInfo `Description` in this same commit; H&F was
  already a declared `SoftDependency` in `Plugin.cs` (BepInEx load order only — this doesn't
  change loadability, only what's needed to finish one specific structure).
- Placeholder white card art for both Shadow forms (`CMC_ShadowCat.png`,
  `CMC_ShadowCatTamed.png`); real art still owed. 15 new SimpEn.csv rows (SimpCn not adopted for
  this file, matching the rest of the village content).
- Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §3.6/§10.7/§7 (Cottage Rework "Independent track")
  updated: Shadow the Cat + Apothecary Cabin stage 6 move from planning-only to CODE BUILT; no
  in-game acid test has run yet (spawn gate, taming CI, stage-6 presence-check with
  `DontSpend: true`, and narrative differentiation from Ash all still need a playtest pass).

## [1.28.0] — 2026-07-23

### Added — Professor Phases P4–P6: event-triggered tasks, a third milestone gift, polish
- **Field Samples** (Phase P4): a new one-shot commission task, offerable once village week 10
  arrives (bridged into a hidden NPCStat, `cmcStatProfFieldSamplesReady`, the same one-shot-latch
  idiom `SyncNarrativeStats` already uses for the river-bridge news flag) — bring 5 fresh
  Billberries, paid in 2 Salt, +6 trust. Discoverable via a new "Any work for me?" dialog branch
  once armed.
- **Applied Herbalism** (Phase P4): a second one-shot commission task, gated on graduating the
  Herbalism course (reuses the existing `cmcStatProfEligibleHerbalism` eligibility stat directly —
  no new subject-bridge needed) — bring 4 Dried Nettle Leaves, paid in 1 Metal Nugget + 2 Salt,
  +8 trust. Also reachable via "Any work for me?".
- Both tasks are genuinely one-shot: each has its own "claimed" NPCStat gate
  (`cmcStatProfFieldSamplesClaimed` / `cmcStatProfAppliedHerbalismClaimed`) so, unlike the weekly
  specimen commission, they never re-offer once completed. Each also contributes +10 to Village
  Renown exactly once (`cmcStatQuestRenownProfFieldSamples` / `cmcStatQuestRenownProfAppliedHerbalism`,
  wired into the existing `VillageRenownPatch` tick alongside the other villagers' one-shot flags).
- **Trust-milestone gift** (Phase P5, the third of the plan's 2–3 milestone gifts — the first two,
  first-degree and Valedictorian, were already shipped in Phase P3): at Trust 80+ the Professor
  gifts 4 Stone Tiles from "the Academy's old teaching kilns," self-disarming via a new
  `cmcStatProfGiftTrust` flag, the same pattern as the existing gift-ladder lines. Chose Stone
  Tiles over an armor-tier reward specifically to avoid the zero-durability-on-spawn risk described
  in CLAUDE.md's GiveCard-postfix-stat-init rule — no CMC code currently initializes durability
  stats on `DroppedCards`-spawned gear, so a shield-tier reward would have handed the player a
  broken 0/80-durability item.
- **Phase P6 polish:** 17 new SimpEn.csv rows for all of the above (SimpCn still not adopted for
  this file, matching the rest of the Professor content); README and ModInfo `Description` updated
  to name the two new tasks and the third gift (docs-honesty pass, same commit).
- Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md §10.2 phase log updated: P4/P5/P6 moved from
  UNBUILT to CODE BUILT, pending in-game acid tests (fresh commission offer/turn-in, one-shot
  claimed-guard survives save/reload, trust-gift fires exactly once) — no in-game verification has
  run yet for this batch.

## [1.27.0] — 2026-07-23

### Added — Town Hall Boards now show real quest status (PR-3)
- The six notice boards inside the Village Hall (shipped flavor-only in 1.25.0) now display
  live, gated content driven by each villager's quest chain:
  - Each of the five villager boards (Inn Keeper, Miller, Weaver, Apothecary, Professor) shows
    the villager's currently active errand and a softer hint once it's been offered, a
    non-spoiling teaser for their next likely errand before it's offered (names the theme, never
    the exact item or the reward), a relationship-standing blurb (skipped for the Apothecary, who
    has no trust/friendship stat yet), and a short log of their most recently completed errands
    (auto-expiring after 3 more are finished).
  - The five cross-villager side quests (Tricolor Wall Hanging, Cloth Mask, Painted Plate,
    Quilted Vest, Incense Burner) get a matching teaser on both participating villagers' boards.
  - The Town Board now shows the village's actual Renown standing (in prose, not just a number)
    and which structures are contributing to it.
  - A new seventh board, the **Construction Board**, tracks all 7 village structures (both
    cottages, the Apothecary's Cabin, the Village Hall, the Well, the River Bridge, and the
    Market Stall) as Not Yet Begun / Complete — split out from the Town Board so the Hall's board
    list doesn't overflow past a reasonable length.
- New hidden GameStats (`cmcStatChronicle<Structure>Day`, one per tracked structure) stamp the
  day each structure was completed, written by a new `VillageChroniclePatch` (one new 5s poll).
- **Known simplification:** construction status is two-state (not begun / complete) rather than
  three-state (not begun / under construction / complete) — a cheap "construction in progress"
  signal wasn't available without extra per-blueprint bookkeeping; flagged in the design doc as
  an accepted simplification.
- **Known gap carried over from PR-2:** the Inn Keeper's quest chain is designed for 8 steps but
  only 5 have dedicated offer/hint/teaser content on his board — the remaining 3 (his fireside
  story, Ash's return, and the boar-hunt epilogue) are pre-existing narrative beats that advance
  the same counter without dedicated quest-offer dialog of their own, so the board's content
  caps at step 5 rather than showing a phantom errand with nothing behind it.

## [1.26.0] — 2026-07-23

### Added — Village quest-chain reveal system (PR-2)
- 37 previously-freely-available decor, clothing, and combat item blueprints are now revealed one
  at a time by helping the village's residents, instead of all sitting in the crafting journal
  from a fresh game. Each of the five villagers (Inn Keeper, Miller, Weaver, Apothecary, Professor)
  now runs an independent quest chain: talk to them once a village-week has passed since their
  last errand, accept a fetch request, hand over the item, then talk again to hear their thanks
  and see the next blueprint appear (still costs research time — this reveals it, it doesn't
  unlock it for free). Chains run in order and never skip ahead.
  - Inn Keeper (8 quests: odd jobs, a hot stew, ale, wine, dried foragables, a fireside story, Ash's
    return, the boar-hunt epilogue) reveals painted dishware, wind chimes, a fire-hardened spear,
    and a wooden shield. Two friendship milestones (15/30) separately reveal a Comfortable Bed and
    Copper Bed Frame.
  - Miller (3 quests: grain, leather for the grindstone belts, planks) reveals the Garden Trellis,
    Hunting Stand, and Stone Tiles; a trust milestone (25) reveals a Bear Figurine.
  - Weaver (7 quests spanning fiber, cloth, and leather) reveals the Straw Mat, Woven Wall Hanging,
    Chaperon, Long Johns, Cloth Coat, Leather Apron, and Leather Sandals; a trust milestone (25)
    reveals the Cloth Scarf blueprint itself.
  - Apothecary (2 quests: fresh herbs, then charcoal and dried flowers for pigment) reveals Herb
    Paste and Pigment. The Stimulant/Anti-Nausea Tea reveals (Herbs & Fungi side) are unchanged.
  - Professor (4 quests: his existing weekly specimen commission, then clay, metal nuggets, and
    bones/antler) reveals a Quilted Cap, Castle Figurine, Dragon Figurine, and Bone Lamellar Armor.
  - Five cross-villager side quests reveal one item each once both participants' prerequisites are
    met (never blocking a main chain): Tricolor Wall Hanging (Weaver + Apothecary), Cloth Mask
    (Weaver + Apothecary), Painted Plate (Miller + Inn Keeper), Quilted Vest (Weaver + Professor),
    Incense Burner (Apothecary + Inn Keeper).
  - Village Renown milestones (25/50) reveal Clay Beads and a Stone Mace.
- New hidden GameStats drive the chain chassis (`cmcStat<NPC>QuestChain/QuestArmed/LastQuestWeek`
  per villager) plus three player-side mirrors of NPC trust values, all ticked by a new
  `QuestChainSchedulePatch` (one new 5s poll covering all five villagers).

### Known issues / needs in-game confirmation
- The Inn Keeper's "bring a hot cooked stew" quest currently accepts any water-filled container
  (`LQ_Water`), not specifically a thickened stew — the game has no Progress/thickness check
  available on a drag-and-drop delivery action, only liquid identity and quantity. Will likely be
  tightened or re-flavored in a follow-up once playtested.
- The ale and wine delivery actions (Inn Keeper quests 3 and 4) accept liquid-type cards
  (`LQ_RyeAle`/`LQ_WheatAle`, three wine varieties) via drag-and-drop — this mod has no prior
  precedent of a liquid card being accepted this way, so it needs an in-game click-test before
  being considered confirmed working.

## [1.25.0] — 2026-07-22

### Added — Village Hall is now enterable, with a Boards room
- The Village Hall now has an **"Enter the Village Hall"** action leading to an interior room,
  temporarily reusing the Village Academy's interior artwork as a placeholder
  (`cmcVillageHallInterior` / `cmcVillageHallInteriorLocation`) until dedicated Town Hall art is
  ready to swap in.
- Inside, six notice boards are on display: a **Town Board** (general village renown/standing,
  using the Village Hall's own image) and one board each for the **Inn Keeper, Miller, Weaver,
  Apothecary, and Professor**, each showing that NPC's own portrait artwork. Each board is a
  simple flavor-text notice (0-cost "Read" action) — no quest-chain gating yet; that lands with
  the planned quest-chain chassis (`Village_Master_Plan.md` §10.6).
- The Village Hall's original outdoor "Read Notice Board" action (tracking the village's overall
  Renown stat) is unchanged and still lives on the Village Hall's own card, representing the
  town's general reputation.

## [1.24.2] — 2026-07-22

### Fixed — 7 truncated / malformed item & NPC descriptions
- Seven `Localization/SimpEn.csv` rows held an unquoted English value with an internal comma,
  which the loader truncates at the first comma (the remainder is misread as the Chinese
  column) — the card/NPC showed only a half-sentence. Down Mattress (item + blueprint), Straw
  Mat (placed), Painted Plate, the Professor's day-gated greeting, and a hidden stat
  description are now fully shown; the Inn Keeper's description also had a stray `, a bed`
  fragment left outside its quotes, now removed. All values are now double-quoted.

## [1.24.1] — 2026-07-22

### Fixed — Village Founder buildings spawned at the wrong location
- The perk's 4 pre-built buildings spawned on whatever board the player stood on when the
  perk applied (the run's STARTING location, e.g. River Clearing) — `SpawnService.Spawn`
  only targets the current board. Placement is now deferred per building to the first time
  the player stands in its home environment: Miller's Cottage, Weaver's Cottage, and the
  Village Hall appear at the **Village** (`cmcEnvVillage`), the Apothecary's Cabin at the
  **Foraging Forest** (`cmcEnvForagingForest`). Latched per env by two new hidden GameStats
  (`cmcStatFounderVillagePlaced`, `cmcStatFounderForestPlaced`) so a deliberately
  deconstructed building is not force-respawned on later visits.
- Saves already affected: the misplaced copies on the starting board are not auto-removed
  (deconstruct them manually, or start a fresh run); correct copies appear at the proper
  locations on next visit.

### Changed — the 4 village building blueprints are now location-locked
- `BpCMCCottageMiller`, `BpCMCCottageWeaver`, `BpCMCVillageHall` can now ONLY be built at
  the Village; `BpCMCApothecaryCabin` can ONLY be built in the Foraging Forest. Enforced via
  `BlueprintCardConditions` (gates the "Start Building" model placement) + matching
  `BuildingCardConditions` (gates every construction stage), both requiring the location's
  CT8 card on the board. Blueprint descriptions now state the build location.

## [1.24.0] — 2026-07-22

### Added — Village Founder perk (cheat/head-start perk)
- New optional character creation perk, **Village Founder** (`cmcperkvillagefounder`, 3 Stars):
  instantly fast-forwards every currently-shipped village beat at game start. Places all 4
  village structures (Miller's Cottage, Weaver's Cottage, Village Hall, Apothecary's Cabin)
  pre-built on their boards; moves the Miller, Weaver, and Apothecary in immediately instead of
  the usual 7-day wait; marks the Inn Keeper/Professor intro done and all 4 single-shot NPC
  quests (Miller Grain, Weaver Flax, Apothecary Herbs, Professor Specimen) thanked, granting
  their blueprint unlocks (`BpCMCGardenTrellis`, Hunting Stand, all 3 comfort blueprints,
  Herb Paste, Market Stall); and grants all 6 Academy graduate perks.
- Does **not** touch anything from the still-unbuilt design layer (Town Hall Boards, Shadow
  the Cat, the 37-item NPC-quest-chain reveal sweep, Trust/Renown-gated item reveals) since
  none of that exists as spawnable content yet — see `Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md`.
- New file: `Patcher/VillageFounderPerkPatch.cs` (gated one-shot apply via
  `cmcStatVillageFounderApplied`, using the same StatsDict/CurrentBaseValue GameStat-write
  idiom as `VillageClock`/`CottageResidentSpawnPatch`, and the InRunAddedPerks-list-append +
  `ApplyPerk` pattern proven in `Sirus23_Mod_Collection/Patcher/CompanionHuntPatch`).

## [1.23.0] — 2026-07-21

### Changed — shared village construction backbone (Cottage Rework §3.7)
- **Miller's Cottage, Weaver's Cottage, and the Village Hall are now built the same way every
  village residence will be**: six shared stages (foundation/walls ×4 — Heavy Stone ×6 + Tree
  Log ×6 each; frame — Tree Log ×8; roof — Plank ×10) plus one themed finishing stage per
  building — Miller: Rope ×8 + Wood ×12; Weaver: Cloth ×14 + Fibers ×10 + Rope ×12; Village
  Hall (civic): Plaster ×20 + Clay ×16 + Heavy Stone ×10. This is the same backbone the
  Apothecary's Cabin already uses (verified in-game 2026-07-21).
- **Balance callout (deliberate, large):** cottage research time rises 12 → 192 ticks (16×) and
  total build time 6–7 → 84 daytime units (~12–14×, 12 per stage across 7 stages); the Village
  Hall's research rises 48 → 192 (4×) and build 36 → 84 (~2.3×), while its per-stage material
  bill drops from the old 30/30/30 walls stage to the shared backbone's steadier pace. These
  are landmark civic projects meant to be paced across village weeks, not an afternoon.
- Weaver's Cottage and Village Hall blueprint descriptions rewritten to match the new
  construction (the Hall's old text described a three-stage build; the Weaver's claimed it
  wasn't stone-built).

### Diagnostics
- **Fixed the 1.22.3 `BlueprintVisibilityDiagnostic` so it actually runs.** It targeted
  `GameManager.LoadMainGameData` — a method that lives on `GameLoad`, and which runs before
  `BlueprintModelStates` exists at all. It now postfixes `GameManager.InitializeStatsAndActions`
  (the method that actually populates the dictionary, at run start), corrects the Garden
  Trellis UID casing (`BpCMCGardenTrellis`), adds the Hunting Stand
  (`hunterscachebphuntingstand`), and logs `BlueprintPurchasing` plus the dictionary size.
  Note: re-analysis of the T1.6 "still visible" report found no runtime evidence the 1.22.2
  hide fix ever failed — the observation was made 8 minutes after the deploy, likely against a
  game process started before it. The decompiled Hidden/Locked branch confirms the shipped
  JSON recipe is correct; the diagnostic stays in until a fresh-run journal check settles it.

## [1.22.4] — 2026-07-21

### Changed
- **Village location cards showed a wall of flavor text that pushed the Trees/Overgrowth/Foraging
  capacity bars and the Forage/Clear buttons off screen.** All twelve village map locations (Village
  Path, Village, Village Farm, Pine Trail, High Grove, Foraging Forest, Deer Meadow, Hunter's
  Crossing, Badger Warren, Clay Flats, Marsh Hollow, Mossy Clearing) carried a 3–5 sentence
  `CardDescription` where vanilla location cards use one short sentence. Trimmed each
  `CardDescription` to a single-sentence blurb matching vanilla scale; the full Conditions/Flora/
  Fauna detail already lived in each location's `CardHelpSection` (the Help tab) and is unchanged.
  Added the missing `CardDescription` rows to `Localization/SimpEn.csv` (previously JSON
  `DefaultText`-only, silently falling back since no CSV row existed).

## [1.22.3] — 2026-07-21

### Diagnostics
- **The 1.22.2 quest-reward blueprint hide fix did not hold up in playtest** — Straw Mat, Woven
  Wall Hanging, Tricolor Wall Hanging, and Garden Trellis were still visible in the crafting
  journal from a fresh run despite the JSON matching the documented `Hidden`-state recipe exactly.
  Added a temporary startup diagnostic (`BlueprintVisibilityDiagnostic.cs`) that logs each item's
  actual resolved `BlueprintModelState` and `UnlockConditionsDesc` contents right after
  `GameManager.LoadMainGameData` — needed to see what the game actually resolved before attempting
  another fix. Remove once root-caused. See `Documentation/Plans/Community_Mod_Chest/Village_Master_Plan.md` §3.4
  ("Spike 1 result: FAILED").

### Fixed
- **East travel from River Clearing showed a red X even with the River Bridge built and/or the
  Village Pathfinder perk.** The Village Path connection gate used `"RestoreDAOnUnlock": false`,
  so any run start where the gate evaluated locked (e.g. loading a character without the perk
  first, or perk state not yet readable) stripped the East travel action off River Clearing for
  the whole game process — and unlocking the gate later re-showed the map connection but never
  gave the travel button back. The gate now restores the travel action when it unlocks
  (`"RestoreDAOnUnlock": true`). Confirmed from the live load log: gate locked at run start,
  bridge/perk recognized later in the same process, gate flipped unlocked, button never returned.

## [1.22.2] — 2026-07-21

### Fixed
- **Quest-reward blueprints were visible in the crafting journal before their quest was done.**
  The Weaver's flax-errand rewards (Woven Wall Hanging, Straw Mat, Tricolor Wall Hanging) and the
  Miller's grain-errand rewards (Garden Trellis, Hunting Stand) are meant to be learned only by
  helping those villagers — but four of the five were listed in a crafting-journal tab, and all
  five carried a "Help the Weaver/Miller with their errand" unlock hint, so they showed up as
  greyed, teased entries from the start. They are now fully hidden — no card, no teaser — until
  the errand is completed and thanked, at which point the reward conversation teaches the recipe
  and it appears in its normal tab. (Removed the four tab registrations; blanked the unlock-hint
  text so the blueprints load in the game's `Hidden` state rather than `Locked`. Herb Paste is
  unchanged — it stays a materials teaser, since it's learnable by foraging Wild Garlic + Old
  Growth Bark, not only by the Apothecary quest.)

## [1.22.1] — 2026-07-20

### Fixed
- **Village Inn trait: first-ever conversation was the wild-garlic errand, with no welcome at all.**
  The trait backfills `cmcStatVillagePhase` to 1 in the background within seconds of loading (so
  the intro conversation never plays), but nothing filled that gap — a trait character's first
  `Talk` could land directly on whatever week/errand beat the phase gate had already opened,
  cold. Added a one-time `CMC_InnKeeperTalk_TraitWelcome` line (new `cmcStatInnTraitWelcomeGiven`
  latch) that now plays first for exactly that path: acknowledges the head start, gives the same
  "inn/projects/Academy are open" recap the normal intro conversation ends on. Players who take
  the normal intro conversation, or whose save is backfilled from pre-rework village progress,
  never see it (the latch is set for them too, since they already got an equivalent welcome).

## [1.22.0] — 2026-07-20

### Added — Village Rework PR-1: phase gate, Village Clock, intro conversation
- **The village now opens in phases, at the player's pace.** A fresh character sees a quiet
  village: the Inn Keeper offers only a new 4-line **introduction conversation** (the valley's
  money system — salt/nuggets/coins, the Inn account, Academy tuition — and the three construction
  projects the village dreams of), the Professor redirects to the Inn ("introductions first"),
  neither NPC trades, Academy study is politely refused with a tooltip, and the Miller's Cottage /
  Weaver's Cottage / Village Hall blueprints stay out of the crafting journal. Finishing the intro
  sets the new hidden `cmcStatVillagePhase` to 1 and opens all of it at once (blueprints still
  require research). A "Remind me how the village works" answer on his normal greeting replays a
  condensed recap any time.
- **Village Clock** (`Patcher/VillageClock.cs`, ticked inside the existing 5 s Inn Keeper poll —
  no new polls): `cmcStatVillageEpochDay` is stamped once when phase 1 is first observed, and
  `cmcStatVillageWeek` is recomputed from it every poll (derived, reload-safe). **No Inn Keeper
  beat reads the absolute world calendar anymore**: the settled-in line paces to village week 2,
  the backstory to week 3, the odd-jobs errand to week 2, the seasonal fireside story pool to
  week 4, and Ash's disappearance arms no earlier than week 6 (still requiring the Well). A new
  `cmcStatInnOnboardStep` sequencer delivers at most one onboarding beat per conversation, so a
  late-arriving player binges them one visit at a time instead of collapsing three weeks of
  dialog into one greeting.
- **Save back-compat:** pre-rework saves with real village progress (the Well Plans, or any
  completed villager quest) are detected within seconds of loading and skip the intro
  automatically; a save that already has the Well Plans also skips both onboarding beats.
  Inn friendship alone deliberately does NOT waive the intro — the Chat action stays available
  pre-intro and builds friendship, so counting it would let one Chat skip the whole onboarding;
  a lightly-engaged old save simply gets the short intro conversation once.

### Changed
- **The Village Inn and Village Academy are now permanent village fixtures** — their map spawns no
  longer require character traits (`ConditionalDrops` re-keyed from `HasPerk` to `AlwaysTrue`).
- **Village Inn trait repurposed** — now a head start instead of a gate: +5 starting Inn
  friendship, and the Inn Keeper waives the formal introductions (the village timeline starts
  open for that character).
- **Higher Education trait retired from character creation** (`CharacterPerkPerkGroup: "None"`;
  the asset ships so existing saves stay valid). Academy course gating now applies to **every**
  character: the Water-Driven Sawmill/Grinding Mill/Forge/Workshop, Copper Sheet, Iron Fishing
  Rod, and copper/iron armor blueprints are locked until the matching course is graduated,
  trait or no trait.
- **Cottage/Hall blueprints re-gated** from "Visit the Village" (`CardsOnBoard: cmcLocVillage`)
  to the intro (`StatValues: cmcStatVillagePhase >= 1`), with unlock descriptions rewritten to
  "Hear the Inn Keeper's plans for the village".

## [1.21.0] — 2026-07-19

### Added (backfilled entry — released as "Bug fixes")
- **The Miller and Weaver now work their cottages.** Miller: mill 3 logs into 24 Planks or grind a
  30-count sack of wheat/rye/edible acorns into flour, each for a copper nugget, via
  station-contained operation blueprints; trades planks and flour, restocking weekly. Weaver:
  weave rope or large cloth at a fiber discount, process dried nettle/flax (and hemp with H&F)
  into fiber batches, trades weaving tools and cordage, also restocking weekly
  (`CottageResidentSpawnPatch` restock chassis + operation blueprints on both cottages).

## [1.20.0] — 2026-07-17

### Added
- **Ash's "Boar Hunt"** — five days after taming Ash the Cat, he slips out after dusk to track a
  boar through the underbrush and doesn't come home (`cmcInnCat` transforms in place into
  `cmcAshTrackingBoar`, art `CMC_InnCat_Wary`, via a new hidden `SpecialDurability2` timer — same
  idiom as his existing Hunger/Thirst "Wanders Off"). He's away — no Feed/Water/Pet/Play — until
  the player wins any real vanilla wild boar encounter (`Combat_EncounterBoar`), at which point a
  new Harmony patch (`Patcher/AshBoarHuntPatch.cs`, postfixing `EncounterPopup.ApplyEncounterResult`)
  transforms him back to `cmcInnCat` with Hunger/Thirst restored to full. The hunt recurs roughly
  every five days after each return. Resolution isn't tied to a specific boar instance — any
  vanilla boar-encounter win while Ash is away resolves it, since no vanilla data links a specific
  wildlife encounter back to a companion card; this keeps the fight itself 100% vanilla rather than
  spawning/tracking a custom NPCAgent boar.

---

## [1.19.1] — 2026-07-17

### Fixed
- **All twelve village-area map locations now show accurate flora/fauna in their in-game Help
  popup**, instead of the vanilla clone template's original text. `CardCloneService.CloneCard`
  only ever refreshes a clone's `CardName` — `CardHelpSection` is left untouched, so every
  `WorldMap/MapNodes.json` clone node was silently displaying its unrelated vanilla template's
  help text (e.g. Village Farm showing its parent template's blank-fauna description instead of
  the seasonal crop fields actually seeded there). Added twelve `GameSourceModify/<LocationUID>.json`
  patches (framework's `GameSourceModifier` phase runs after `WorldMapInjector.PrepareAll` clones
  and registers the location cards, so these resolve cleanly) rewriting `CardHelpSection` for
  Village Path, High Grove, Pine Trail, Village, Village Farm, Clay Flats, Marsh Hollow, Mossy
  Clearing, Foraging Forest, Hunter's Crossing, Deer Meadow, and Badger Warren — each reflecting
  that tile's real vanilla-template flora/fauna plus any mod-specific additions (seasonal fields
  and wild garlic at Village Farm, the Clay Barrow at Clay Flats, the forage bonanza and Ash's
  dusk prowl at Foraging Forest, badger setts at Hunter's Crossing/Badger Warren).

---

## [1.19.0] — 2026-07-17

### Added
- **Three new hunting-terrain map locations**, wired via `WorldMap/MapNodes.json`:
  - **Hunter's Crossing** — fills the gap between Village Path and Pine Trail with a direct
    shortcut (clone of vanilla Badger Hill); badgers den here.
  - **Deer Meadow** — extends the map north past the Village (clone of vanilla Deer Grove); deer
    frequent these woods.
  - **Badger Warren** — the northernmost stop past Deer Meadow (clone of vanilla Badger Hill).
- **Secondary one-way connection to the vanilla Greenfalls location**, opened from Badger Warren
  via `VanillaExits` — a forward-only shortcut out of the village area (no return route back into
  the village from Greenfalls).

---

## [1.18.0] — 2026-07-17

### Added
- **Comfortable Bed** — a new top-tier sleeping structure assembled from two separately
  craftable components: a **Down Mattress** (Feathers ×100, Fibers ×50, Large Cloth ×4, Twine
  ×20, needle) and a **Copper Bed Frame** (Copper Sheet ×3, Wood ×6, Plank ×2, hammering tool —
  requires Advanced Copper Tools). A third blueprint assembles both components into the placed
  Comfortable Bed, which has its own Nap/Sleep actions (stronger rest recovery than the Bedroll)
  and a passive perceived-temperature bonus. A Take Apart action splits the bed back into its
  Down Mattress and Copper Bed Frame so it can be moved to a new camp without re-spending raw
  materials.

---

## [1.17.4] — 2026-07-17

### Fixed
- **Professor trade is now always available**, matching the Inn Keeper. Trade was previously
  gated behind his "Resident" phase (satchel filled to 10 foraged items) — which he could never
  reach while his forage was landing on the ground — so the Trade button never appeared. Removed
  the phase gate and gave him a starting stock of six foraged specimens so there is something to
  buy from the first meeting.
- **Foraged items now go into the Professor's satchel instead of dropping on the ground.** His
  forage and specialty-stock actions now fire from the mod (the same `PerformAction` path the Inn
  Keeper's restock uses, the only one proven to route `DropCardsInsideInventory` into an NPC's own
  inventory) rather than through the native NPC action loop, which was depositing them on the
  environment board.
- **Portrait now matches where you meet him.** The scheduler no longer moves him — or flips his
  card art — while you are in the same location, so he no longer wanders off mid-conversation or
  shows his outdoor portrait while standing indoors, and the dialog/trade portrait resolves
  correctly when you talk to him.

---

## [1.17.3] — 2026-07-17

### Changed
- **World map district re-laid out** — Village and High Grove swapped cells: the Village now
  sits at the northern head of the east spine (20,20) with High Grove taking the middle spot
  (20,0) directly east of Village Path. Village Farm moved to (30,20), directly east of the
  Village. Resulting spine: Village Path → High Grove → Pine Trail → Village → Village Farm.
  The southern wetlands (Clay Flats, Marsh Hollow, Mossy Clearing, Foraging Forest) reconnect
  to the spine through Clay Flats↔Village Path and Mossy Clearing↔High Grove, forming a loop so
  no wing dead-ends. All 11 edges validated: unique cells, cardinal adjacency, travel directions
  match geometry, no collisions with vanilla/ACT/H&F.
- **Clay Flats now seeds the vanilla Clay Barrow** instead of the custom `cmcClayMound`. The
  bespoke CT2 clay-mound card, its placeholder art, and its localization rows were retired — the
  vanilla Clay Barrow's own "Dig Up Clay" interaction covers the same purpose.

---

## [1.17.2] — 2026-07-16

### Fixed
- **World map rearranged into a coherent village district** — the previous layout stretched the
  village into a single column running north along the river (parallel to River Confluence and
  the Metal Mine caves), and **Village Path was permanently hidden from the paper map**
  (`HideFromMap: true`), so the bridge crossing from River Clearing and every path into the
  village cluster was never drawn — the village nodes floated disconnected next to the river.
  Village Path is now visible (still fog-hidden until first visited, like any location) and the
  nine locations form a compact 2×5 block east of the river: wetlands south (Clay Flats, Marsh
  Hollow), village core center (Path, Village, Farm), pine highlands north (Pine Trail, High
  Grove beside the mine caves). Two loop connections added (Farm↔Pine Trail,
  Marsh Hollow↔Foraging Forest) so neither wing dead-ends through the Village Path hub.
- **Village Well was unbuildable** — `InjectImprovementInto.json` targeted the CT4 environment
  UID (`cmcEnvVillage`) instead of the CT8 location card (`cmcLocVillage`), so the injection was
  skipped at every load ("not found in registry" warning). Also requires framework **2.16.3**,
  which moves improvement injection after map-node clone creation so mod map locations can be
  targeted at all.

---

## [1.17.0] — 2026-07-16

### Added

#### Village Professor — location-matched portraits
The Professor's card art and dialog/trading portrait now track where he actually is:
- **Village Academy** → `Professor_Indoors` (study interior).
- **Outdoor forage nodes** (Village Path / Village / Farm / Foraging Forest) → `Professor_Outdoors`.
- **Inn (at night)** → `Professor_Inn` — this PNG does not exist yet; until it ships, the
  Academy art is used at the Inn (one Info log line notes the fallback). Dropping a
  `Professor_Inn.png` into `Resource/Picture/` is the only step needed to activate it.

Implementation: the P2 scheduler (`ProfessorSchedulePatch.SyncPortrait`) swaps
`NPCAgent.AgentImage` (dialog + trade popups read it live), restamps both NPC model cards
(their image is copied from `AgentImage` once at creation), and refreshes any on-board card
graphics so the art flips the same tick he moves. Requires framework **2.16.0**
(`GameContent.Find<Sprite>`).

### Fixed
- **`Agent_Professor.json` pointed at a sprite name that no longer exists** — the P1
  placeholder `Professor.png` was replaced by the `Professor_Indoors`/`Professor_Outdoors`
  art in an earlier commit, but `AgentImageWarpData` still said `"Professor"` (unresolvable →
  blank portrait). Now defaults to `Professor_Indoors`.
- **`SyncNarrativeStats` was never called** — 1.15.0's changelog shipped it (grad-count
  mirror, River-Bridge news flag, weekly specimen-commission re-arm) and the method body was
  present, but the scheduler call site was still the pre-1.15.0 `// TODO` comment, so none of
  that state ever updated. The call is now wired in.

## [1.16.0] — 2026-07-16

### Added — deferred-items revival batch (recovered from the deleted 2026-05-20 standalone mods)
- **Club** (Hunting › Close Combat): a budget blunt weapon carved from a tree trunk — 20–80 blunt damage, 60 durability, carves down into a long stick.
- **Bone Helmet** (Tailoring › Equipment): 18 head armor from 2 Bones + small leather + sinew; armor scales with durability (50%–120%); dismantles back into its materials.
- **Cloth Mask** (Tailoring › Cloth): mask-slot face covering (+0.5 perceived temperature); wears out slowly while worn; rips back into cloth.
- **Long Johns** (Tailoring › Cloth): underwear-slot cloth leggings (+1.5 perceived temperature, +4 comfort, 2/2 leg armor); rips back into 2 cloth.
- **Glazed Clay Plate** (Metal & Clay › Utensils): refire a clay plate with ash glaze — 600 durability tableware (vs 500 painted), stacks to 3.
- **Hunting Stand** (Hunting › Trapping): build a kit (3 Planks + 4 Long Sticks + 2 Rope), set it up at a location, and wild-animal encounters there have a **~45% chance to be averted** (declarative framework `EncounterGuards` JSON — no C#). Take Down returns the kit; Dismantle returns planks and sticks.

### Not revived (with reasons)
- **Sling + Sling Stones** — vanilla EA 0.65 has no moddable ranged-ammunition combat path; shipping a non-functional weapon would be advertised dead content.
- **Grinding Slab, Stone Block, Stone Tile (singular)** — no consumer exists in CMC yet (stone construction blueprints / grinding CI design pending).

## [1.15.0] — 2026-07-16

### Added

#### Village Professor — Trade, Trust & Errands (enhancement pack)
Builds Phase P3 (Trade) plus a dialog/reward layer on the P2 Foraging Arc scheduler
(`Documentation/Design/Village_Professor_Plan.md`). All state lives in the Professor's own
NPCStats (native save path); dialog reads them via explicit-agent `RequiredNPCStatValues`
(decomp-verified: `UseAssociatedAgent` cannot work in dialog conditions — they evaluate with a
null card — but an explicit `TargetAgent` resolves through `GameManager.AllNPCs`).

- **Trade enabled** (`CannotTrade: false`), gated to his Resident phase via `TradingConditions`.
  Baseline barter: sells at 1.5×, buys at 0.5×.
- **Trust** (`cmcStatProfessorTrust`, 0–100) — vanilla `AgentTrust` idiom: every trade feeds it
  via `ModifyNPCStatsPerTradeValue` (both directions, clamped +6/side/trade); the quiz, gifts,
  and commission completions add more.
- **Price break at high trust** — trust-scaled `NPCStatModifiers` on both trade directions:
  at trust 100 he sells at 1.1× and pays 0.75× (linear from 1.5×/0.5× at trust 0).
- **Trust-tiered greetings** (≥25 friendly, ≥60 personal) plus two trust-gated branches:
  field-scholar advice (≥25) and a two-part backstory (≥60).
- **Seasonal greetings** — four season-gated starting lines (`InGameTimeCondition.SeasonIs`).
- **"Where were you today?"** — the P2 scheduler now records each wander destination in
  `cmcStatProfLastNode`; a menu answer surfaces one of four node-flavored responses.
- **Milestone-reactive dialog** — scheduler mirrors graduated-course count into
  `cmcStatProfGradCount` (studies check-in at 0 / 1–5 / 6) and flags the River Bridge via
  `CardUtil.IsImprovementBuilt` (`cmcStatProfBridgeNews`, one-time news line); a one-time
  Ash-the-cat line gates on the Inn Keeper's `cmcStatLostCat` quest stat.
- **Milestone gift ladder** — one-time gifts as self-disarming priority greetings (CatThanks
  pattern): first degree = 2× Metal Nugget (+5 trust); all six degrees ("Valedictorian") =
  4× Metal Nugget + 6× Salt (+10 trust).
- **Entrance-exam quiz** — three-choice question; the correct answer pays 2× Salt (+5 trust),
  once; wrong answers allow retakes.
- **Weekly specimen commission** (`bpcmcprofspecimen`, first commission on `CommissionsBp`) —
  bring 3 Nettle Leaves for 1 Metal Nugget (+8 trust); completion disarms it and the scheduler
  re-arms it 7 days later (`cmcStatProfSpecimenReady`/`cmcStatProfSpecimenNextDay`). Commission
  blueprints are deliberately NOT in `BlueprintTabs.json`.
- 10 new `NPCStat/*.json`, 25 new/updated `DialogScene`/`DialogLine` assets, 41 new
  localization rows; scheduler gains `SyncNarrativeStats` (grad count, bridge news, weekly
  re-arm) with once-true caches reset per run.

#### Cottage Residents — the Miller & the Weaver move in
Resident phase of `Documentation/Design/Village_Construction_Projects_Plan.md`: each cottage
gains a resident NPC who **moves in one week (7 in-game days) after the cottage is finished**.
No interiors — the resident stands on the Village board beside their home.

- **The Miller** (`cmcMillerAgent`) and **the Weaver** (`cmcWeaverAgent`) — stationary NPCAgents
  on the Inn Keeper/Professor chassis: a Talk dialog (`CMC_MillerTalk` / `CMC_WeaverTalk`, three
  lines each — greeting, craft talk, village talk) plus a Chat `DismantleAction` with the same
  social payoff as the Inn Keeper's Chat (Loneliness/Stress relief, Comfort, 1-hour cost).
  No trade (`CannotTrade: true`) — deliberately smaller in scope than the Inn Keeper.
- **Move-in timer** — new hidden GameStats `cmcStatMillerMoveIn` / `cmcStatWeaverMoveIn` store
  the absolute arrival day. `CottageResidentSpawnPatch` arms each to (completion day + 7) the
  first tick the built cottage exists (a `GameManager.AllCards` scan — note `AllCards` is
  current-environment-scoped, so this fires while the player stands at the Village, which in
  practice is completion time since construction happens on that board), then spawns the
  resident the first time the player stands at the Village on/after the due day
  (`CreateNPC` + `Init(live EnvID)` + `AssignOrCreateNPCCards`), with an
  `InitializeStatsAndActions` postfix restoring placed residents on save load — a structural
  copy of `InnKeeperSpawnPatch` minus the restock machinery. GameStats persist natively, so
  the countdown survives save/reload.
- Ships with solid-white placeholder portraits (`CMC_Miller.png`, `CMC_Weaver.png`) pending
  real art, matching the cottages' own placeholder card art.

### Requires
- CSFF Mod Framework **2.15.1+** (non-UID SO body WarpData resolution — dialog assets).

---

## [1.14.0] — 2026-07-16

### Added

#### Village Construction Projects — Miller's Cottage, Weaver's Cottage, Village Hall
Phase 1 of `Documentation/Design/Village_Construction_Projects_Plan.md` — three buildable civic
structures at the Village node, no interiors, no new NPCs (deliberately deferred to a future Phase 2).

- **Miller's Cottage** (`cmcCottageMiller` CT2 / `BpCMCCottageMiller` CT7) — single-stage build
  (Stone ×30, Plank ×16, Rope ×8, Wood ×12), gated on having visited the Village. Comfort +14 passive.
  No `DismantleActions` — permanent civic growth, matching the Well/Bridge precedent, not the packable
  Market Stall kit.
- **Weaver's Cottage** (`cmcCottageWeaver` CT2 / `BpCMCCottageWeaver` CT7) — same shape, cloth/rope
  material flavor instead of stone (Cloth ×14, Rope ×12, Plank ×14, Fibers ×10). Comfort +10 passive —
  deliberately different from Miller's so the two don't feel like reskins.
- **Village Hall** (`cmcVillageHall` CT2 / `BpCMCVillageHall` CT7) — the capstone, a 3-stage build
  (foundation: Stone/Wood/Twine; walls: Heavy Stone/Clay/Plaster; roof: Plank/Rope — ~235 items total,
  ~1.75× the Well's build cost) each stage at `BuildingDaytimeCost: 12`, the framework's per-stage cap.
- **Village Renown** (`cmcStatVillageRenown`, hidden `GameStat`, Visibility 2) — new `VillageRenownPatch`
  live-recomputes each tick from which civic structures currently exist (both cottages, the Well, the
  River Bridge, and a Market Stall revenue milestone), rather than incrementing once per completion.
  Recomputing avoids a real multi-stage pitfall: `CardData.BlueprintStatModifications` feeds
  `CurrentBuildAction.StatModifications` for *every* stage of a blueprint's build action
  (`BlueprintConstructionPopup`), so a naive "tick once on completion" via that field would have fired
  three times on Village Hall alone.
- Village Hall's Notice Board mirrors current Renown onto its own `SpecialDurability1` bar (a visible
  progress readout) and a flavor "Read Notice Board" `DismantleAction`. At full Renown, Market Stall
  sales get a 25% revenue bonus (`MarketStallPatch.RevenueMultiplier`) — a real, working payoff rather
  than the speculative `RatePerDaytimePoint` nudge floated in the design doc (Market Stall's revenue is
  event-driven via `MarketStallPatch.OnDayChanged`, not rate-based, so that field is inert for it).
- Registered in `BlueprintTabs.json` under the vanilla `Tab_2_Construction_Subtab_5_HouseBuilding_TabName`
  tab (verified against its actual `TabName.LocalizationKey`, not the tab file's name).
- Ships with solid-white placeholder card art (`CMC_CottageMiller.png`, `CMC_CottageWeaver.png`,
  `CMC_VillageHall.png`) pending real art.
- Phase 2 (interiors + resident NPCs for any of the three) is explicitly out of scope for this release —
  see the plan doc §1 and §8 for the sequencing/risk rationale.

#### Inn Keeper — Friendship, seasonal wares, a gift, an errand, and village-progress banter
Phase 7 of `Documentation/Design/Village_InnKeeper_Plan.md` — deepens the existing Inn Keeper NPC
without new subsystems; reuses proven hidden-`GameStat` state-machine and `DragAndDropActions`
patterns throughout.

- **Friendship** (`cmcStatInnFriendship`, hidden, 0-30): rises with Chat/Gift/errand delivery.
  At 15+, unlocks a warmer greeting variant and the one-time **Inn Regular** perk
  (`Pk_InnRegular.json`, small passive Loneliness/Stress rate reduction, granted via a new "Feel at
  Home" `DismantleAction`). Also feeds a Trade discount via `NPCTradingValueModifier.
  GlobalStatModifiers` on the existing markup/buyback modifiers — markup falls 1.5x -> 1.2x, buyback
  rises 0.5x -> 0.65x as Friendship approaches 20.
- **Progress-reactive dialog**: one-shot remarks when the Village Well or River Bridge is finished
  (`CardsOnBoard`-gated `StartingPoint` lines), followed by a one-shot Well "housewarming" dialog beat
  (an invite + toast, narrated only — see below for why an actual NPC departure wasn't built).
- **Seasonal pantry**: four new seasonal restock `AgentActions` swap in season-flavored stock (fresh
  Billberries in summer, Roasted Acorns in autumn, Fermented Billberries + Dried Bird Meat in winter,
  etc.) alongside the base pantry.
- **Gift**: drag foraged Wild Garlic onto the Keeper (new `DragAndDropActions` entry) for a one-time
  `BlueprintsFullUnlock` of the Market Stall blueprint.
- **Odd-jobs errand**: an occasional "bring me Dried Wild Garlic" request (from day 8), delivered via
  drag-and-drop for a small reward; capped at once per season.
- **Cross-NPC banter**: new dialog answers let you ask the Inn Keeper about the Professor and vice
  versa.
- **Scope note**: a genuine "Keeper leaves the Inn for the Well celebration" mechanic (physical
  `MoveToEnvironment`) was investigated and NOT built — `NPCAction.ToAction()` converts to a plain
  `CardAction` that drops movement data entirely, so this mod's existing direct-invoke restock/errand
  pattern (chosen specifically to bypass native NPC action scheduling) cannot move the Keeper; doing so
  safely would mean re-enabling native scheduling for one action, risking the documented
  sibling-action race. The narrative payoff ships via dialog only; physical departure remains a future,
  separately-scoped effort. "Ash living at the Inn" (Stay Here/Follow Me) was also investigated and
  dropped as unnecessary — Ash has no `AlwaysUpdate` flag (matching vanilla `DogFriend`), so leaving him
  behind or bringing him along via ordinary inventory drag already works with zero new code.

---

## [1.13.0] — 2026-07-16

### Added

#### Village map expansion — north/south trails, relocated Foraging Forest, Clay Mound
- **Northern trail** — three new `WorldMap/MapNodes.json` clone nodes beyond the Village (not Village
  Farm — see collision note below): **Mossy Clearing** → **Pine Trail** → **High Grove**, at
  `(20,0,10)`/`(20,0,20)`/`(20,0,30)`, 10 units apart. Pure JSON, no C# — each node clones a distinct
  vanilla outdoor template (`Env_ClearingOak_MossyClearing`, `Env_GrovePine_PineGrove`,
  `Env_GrovePine_HighGrove`) so it arrives with correct vanilla biome tags, ambience, and forage
  tables out of the box.
- **Coordinate collision caught in review** — the northern trail was originally planned at
  `x:10,z:20/30/40` (continuing straight out from Village Farm), which turned out to exactly collide
  with `AdvancedCopperTools/WorldMap/MapNodes.json`'s `actIronCaveEnv`/`actTinCaveEnv`/`actCopperCaveEnv`
  (same three cells). An independent QA pass caught this — the original collision check only compared
  against vanilla `DefaultWorldMap.json`, not sibling mods' own `MapNodes.json`. Moved to Village's own
  `x:20` column instead, confirmed clear against vanilla, ACT (`x:10` only), and HerbsAndFungi
  (`x:-70/-80` only). Lesson for future map-expansion work in this repo: always check ALL mods'
  `WorldMap/MapNodes.json`/`FullMap.json` files for the target cells, not just vanilla.
- **Southern trail** — two new nodes between Village Path and the Foraging Forest: **Clay Flats**
  (cloned from vanilla `Env_River_ClearingAlder_BoggyMeadows`, a riverside marsh) and **Marsh Hollow**
  (cloned from vanilla `Env_ClearingAlder_SwampHill`).
- **Foraging Forest relocated** — moved two steps further out along the southern trail (past Clay
  Flats and Marsh Hollow) and re-templated from the vanilla Flower Glade to the vanilla **Deer Grove**
  (`Env_GrovePine_DeerGrove`) so deer are thematically at home in these woods. `EnvironmentUID`/
  `LocationUID` are unchanged, so the Ash lost-cat prowl gate (`LostCatPatch.TargetEnvUid`), the
  Professor's outdoor schedule (`ProfessorSchedulePatch.OutdoorNodeUids`), and the Wild
  Garlic/Old Growth Bark/H&F forage drop table (`DropInjections.json`, keyed on
  `cmcLocForagingForest`) all continue to work unchanged — none of them key off coordinates or the
  clone template. Existing saves that already visited the old Foraging Forest keep whatever board
  content they already generated there; only new/not-yet-visited games see the Deer Grove reseed.
- **Clay Mound** (`cmcClayMound`, new `CardData/Location/`) — a `CardType 2` natural clay deposit
  seeded onto Clay Flats via `ExtraDropUIDs`. Two `CardInteractions`, mirroring Advanced Copper
  Tools' ore-vein pattern: **Dig Clay** (Shovel only; 2 Clay guaranteed + 40% chance of a 3rd) and
  **Scoop Clay** (any other digging/chopping tool; 1 Clay, slower). 12 `UsageDurability` charges
  total before the mound is spent (`ReceivingCardChanges.ModType: 3` on zero, matching
  `AdvancedCopperTools/CardData/Location/CopperVein.json`). Ships with a solid-white placeholder
  PNG pending real art (prompt drafted in `.audit/image-prompt-claymound.md`).
- Village area location count: 4 → 9. `ModInfo.json`/`README.md` updated for feature honesty.

---

## [1.12.0] — 2026-07-16

### Added

#### Village Inn Keeper — weekly dialog, the Village Well, and Ash's return
- **Weekly onboarding arc** — the Keeper's greeting now changes over the first three weeks: week 1
  greets the player and offers a foraging tip or points them to a bed/odd jobs; week 2 nods to the
  turning season and opens Trade directly from the greeting; week 3 shares his backstory (arriving
  in the valley as a boy, his father digging the cellar, the homestead growing into the Inn) and
  offers **Well Plans** if you agree to help the village grow. All new lines are additional
  `StartingPoint` `DialogLine` entries gated by `RequiredInGameTimes` (`DayIs >= 8/21`) — no C#
  dialog-routing needed, the engine's own `DialogScene.GetStartingLine` gate list handles it.
- **The Village Well** — a new buildable `CardType 10` improvement (`cmcimpwell`), unlocked by the
  Well Plans item from the week-3 conversation. Twice the Stone/Heavy Stone cost of the vanilla
  root cellar, plus Plaster/Clay/Twine, at the maximum single-stage construction time (12). Once
  built, drag any empty water container to it to fill (`CreatedLiquidInGivenCard`, same idiom as
  vanilla ponds/rivers). Injected into the Village map via `InjectImprovementInto.json`.
- **A seasonal story pool** — five independent folk tales (a hard winter with wolves, a
  mushroom-addled night, a tall fish tale, a miller's wager, and a ferryman's uncanny crossing),
  each told across 4 visits over the span of a season. One story is randomly selected whenever the
  season changes (`Patcher/InnKeeperDialogSchedulePatch.cs`); progress within a story advances via
  the same self-advancing `StatModifications` idiom as the lost-cat arc.
- **Ash's disappearance, take two** — starting day 42, once the Well is built, the Keeper's very
  next greeting becomes a special reveal about Ash going missing (replacing whatever week/story
  greeting would otherwise show), arming the existing lost-cat search. Once Ash is found and
  thanked, the Keeper occasionally checks in asking how Ash is settling in, with several possible
  replies.

### Fixed

#### Village Inn Keeper — empty trade pantry on first meeting
- The Keeper's pantry could show empty if Trade was opened within the first ~30 seconds of
  meeting him — `StartingInventory`'s `ItemWarpData` resolution path is unproven for
  `NPCInventoryElement`, and the periodic restock (the confirmed-working mechanism) only ran on
  its own 30-second poll. The pantry is now force-restocked immediately the moment the Keeper's
  board card is created (arrival spawn or save-reload restore), reusing the same proven
  `DropCardsInsideInventory` restock action rather than waiting on the poll.

### Changed

- Em dashes removed from all Inn Keeper dialog and related card text (Ash the Cat, Ash the Stray
  Cat, the Keeper's own description) per a wording pass — replaced with commas/colons.

---

## [1.11.5] — 2026-07-15

### Added

#### Village Professor — Foraging Arc (Phase P2)
- The Professor now has a daily routine instead of standing still in the Academy: by day he
  wanders the four Village-area map nodes (Village Path, Village Farm, Village, Foraging Forest)
  foraging small amounts of vanilla goods into his own satchel; every night, regardless of phase,
  he returns to the Inn. Once his satchel holds 10 items he settles in at the Academy by day
  instead — and heads back out foraging again if his stock drops to 2 or fewer (repeating cycle).
- **Specialty stock** — once a course is graduated at the Academy lectern, the Professor
  occasionally turns up one themed advanced item (a copper tea kettle for Metallurgy, a rotary
  quern for Herbalism, and similarly for Medicine/Fishing/Architecture/Armorer) alongside his
  foraged goods, up to 2 held at a time. Items from mods you don't have installed simply never
  appear — no error, no missing-dependency warning.
- All new movement and state (`Patcher/ProfessorSchedulePatch.cs`, renamed from
  `ProfessorSpawnPatch.cs`) is driven by `GameManager.MoveNPC`; new-item creation is delegated
  entirely to native `NPCAction.DroppedCards` JSON on `Agent_Professor.json`, gated by seven new
  hidden `NPCStat`s (`NPCStat/CMC_Prof*.json`) — no reflection into the game's card-spawn
  coroutine. Trade (exposing the accumulated stock to the player) is Phase P3.

---

## [1.11.4] — 2026-07-15

### Added

#### Village Inn Keeper — Trade, fireside tales, and the lost cat
- **Trade** — the Inn Keeper now buys and sells (sells at 1.5× value, pays 0.5×). The pantry stocks Salt, Acorn Flatbread, Cooked/Dried Meat, Boiled Eggs, a Firm Cheese Wedge, Dried Springberries, and dried spices (Wild Garlic, Garlic Powder, Forest Caps), and restocks a few staples every morning (6–8 AM).
- **Talk** — a new fireside dialog (`CMC_InnKeeperTalk`): the Keeper tells a tale of the forest's real places and legends (River Clearing, Green Glade, the Old Woods and Primeval Woods, Moon Hills, the Black Mire; the Wandering Oak, the Hag, the Humming Widow, the Primeval Wolf). A "Let's see your pantry" answer opens Trade directly.
- **Ash, the lost cat** — the tale turns to the inn's missing mouser. Agree to look for him and Ash begins prowling the **Foraging Forest between dusk and dawn (17:00–06:00)**. Offer him meat or fish and he becomes **Ash the Cat**, a keepable pet with Hunger/Thirst (Feed / Give Water), Pet and Play actions, and a passive Loneliness-reducing aura — but starve him and he wanders off. Report back to the Inn Keeper for a thank-you meal and salt. Quest state is tracked in a hidden GameStat (`cmcStatLostCat`); the spawn check is `Patcher/LostCatPatch.cs`.

### Changed
- Inn Counter description now refers to the visible Inn Keeper.
- Framework requirement raised to **2.13.0+** (non-UID ScriptableObject registration used by the dialog scenes).

---

## [1.11.3] — 2026-07-14

### Added

#### Village Professor
- **Professor** — a stationary NPC now spawns inside the Village Academy on first visit and persists across save/reload. Built as a raw `NPCAgent` (`Agent_Professor.json`); spawn placement/restoration handled by `ProfessorSpawnPatch.cs` (same proven pattern as the Inn Keeper).
- **Talk** — opens a greeting dialog scene with flavor about the Academy and a branch that only appears after day 3, proving the day-gated trigger mechanism. Trade, tasks, and milestone gifts are planned for later phases — see `Documentation/Design/Village_Professor_Plan.md`.

---

## [1.11.2] — 2026-07-14

### Added

#### Village Inn Keeper
- **Inn Keeper** — a stationary NPC now spawns inside the Village Inn on first visit and persists across save/reload. Built as a raw `NPCAgent` (`Agent_InnKeeper.json`); spawn placement/restoration handled by `InnKeeperSpawnPatch.cs`.
- **Chat** — talk with the Inn Keeper for a small boost to Loneliness, Wellbeing, Isolation, Comfort, Morale, Skill_Socials, and Stress (costs 1 in-game hour). Trade and further polish are planned for later phases — see `Documentation/Design/Village_InnKeeper_Plan.md`.

---

## [1.11.1] — 2026-07-14

### Changed

#### Village Academy
- **Armorer course** now also gates **Advanced Copper Tools' Iron armor** (Breastplate, Helmet, Greaves, Gauntlets), matching the existing Copper armor gating — both metal tiers stay locked until the Armorer course's Final Exam is passed. Perk and course descriptions (`Higher Education`, `Armorer Graduate`, lectern Final Exam text) updated to mention iron armor alongside copper.

---

## [1.11.0] — 2026-07-14

### Added

#### Stone Tile Floor
- **Stone Tile Floor** — new environment improvement buildable inside cabins and mud huts (main rooms, expansions, and the free-build construction rooms — 7 interiors total). Lay **Stone Tiles ×12 + Clay ×4** (2-hour build) for a permanent **Comfort +12** and **Insulation** (+1 perceived temperature) in that room. Appears in a room's improvements panel once you have at least one Stone Tile.
- **Stone Tiles** now have a real downstream use: item and blueprint descriptions updated from "trade good only" to reflect their flooring role (closes the `STONE_TILES_FLOORING` deferred idea).

---

## [1.10.3] — 2026-07-12

### Added

#### Village Academy
- **Village Academy** — unlock with the **Higher Education** character trait. Enter from the Village like a vanilla cabin. Drop Salt, metal Nuggets, or a Duros Coinage Metal Coin onto the Lectern to fund a **Tuition Account** (holds up to 1,000). Six courses run as 2-hour study sessions, each with a one-time 100-currency enrollment fee drawn on the first session:
  - **Architecture** (72 h total) — unlocks the Water-Driven Sawmill and Grinding Mill blueprints; together with Metallurgy also unlocks the Water-Driven Forge and Workshop Kit
  - **Metallurgy** (60 h) — unlocks the Copper Sheet recipe
  - **Herbalism** (30 h) — doubles forage yields at all forage locations
  - **Fishing** (24 h) — unlocks the Iron Fishing Rod recipe; improves Iron Rod catch odds
  - **Armorer** (24 h) — unlocks copper armor recipes (course hidden if Advanced Copper Tools is not installed)
  - **Medicine** (24 h) — speeds wound clotting and pain recovery
- Passing each course's Final Exam grants a hidden **Graduate perk** (`CharacterPerkPerkGroup: "None"`): invisible at character creation, accessible only by completing the course in-run.

#### Weapons
- **Fire-Hardened Spear** — craft from a Straight Branch + any open flame. Full vanilla spear moveset plus a signature **Charred-Tip Strike** attack dealing bonus `Char` damage. Blueprint in the Weapons tab; no tools required.

#### Apparel
- **Cloth Coat** — new clothing item. Blueprint in the Apparel tab.

#### Village Inn overhaul
- **Inn Counter** — new functional interaction card inside the enterable inn. Drop Salt, metal Nuggets, or a Duros Coinage Metal Coin to fund an **Inn Account** (holds up to 500 currency). **Purchase Meal** and **Stay the Night** each draw 50 from the balance. **Work Odd Jobs** (4-hour shift, no balance required) pays 1–2 copper nuggets at 50% quality.

#### Village area
- **Village Crop Fields** — three standalone location cards inside the Village with per-season active harvests: **Flax Field** (spring), **Turnroot Field** (summer), **Rye Field** (autumn). Each field yields a limited number of harvests before it is spent for the season.
- Interior environment cards for the Academy (`CMC_AcademyInterior`) and Inn (`CMC_InnInterior`); dedicated exit cards for each building.
- **Iron Fishing Rod** mechanics split into a dedicated `IronRodFishingPatch`; self-contained and no longer part of the general forage patch.

#### Artwork
- New images: Academy exterior, interior, lectern; per-discipline rooms (Architecture, Armorer, Fishing, Herbalism, Metallurgy, Medicine); Inn desk and interior; village door interior.

#### Framework / data
- `DropInjections.json` — Wild Garlic and Old Growth Bark forage now handled declaratively by the framework.
- `InjectImprovementInto.json` — improvement injection for village improvements now handled declaratively.

### Changed

- `ForageInjectionPatch.cs` heavily trimmed; Wild Garlic, Old Growth Bark, and herbalism bonuses extracted to `HerbalismForagePatch.cs` and `DropInjections.json`.
- `VillageFarmSeasonalCropPatch.cs` removed — replaced by the three standalone seasonal location cards above.
- `RiverBridgeImprovementPatch.cs` removed — improvement injection now handled by `InjectImprovementInto.json`.
- Academy and Inn buildings now each have a dedicated CT4 interior + CT8 exit pair, matching the enterable-cabin pattern used by vanilla.
- `CardVisualsRefresh` helper added — keeps the displayed balance on lecterns and counters accurate after an account balance changes.
- `CurrencyValue` helper introduced — centralises currency amount calculation (Salt, metal Nuggets, Duros coin) in one place.

---

*Previous release: v1.8.1 (2026-06-23)*
