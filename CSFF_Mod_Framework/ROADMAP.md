# Roadmap: CSFF Mod Framework
Version at time of writing: 2.26.0
Date: 2026-09-17 (refreshed by `/consolidate-audit` after source commit `8881c18cb`)
Audit score: 9/10 - PASS

> **This is the framework, not a content mod.** The content-mod roadmap template (Tier 2 migration,
> theme expansion, art) mostly does not apply: the framework DEFINES Tier 2, and its own content is a
> single minimal Portal Hub set that exists only to exercise the engine. So "expansion" here means new
> ENGINE surfaces that downstream mods consume, and "polish" means removing the last dead API and
> boilerplate. Player-facing content, recipes and balance belong in downstream mods, not here
> (`Documentation/Ideas/CSFFModFramework/IDEAS.md` guardrails).

## Current State

**Theme**: The standalone engine every CSFF suite mod depends on - mod discovery, data loading, WarpData
resolution, sprite loading, blueprint/perk injection, localization, STA activation, NPC-agent
diagnostics, WorldMap node + travel-DA injection, the Portal Hub cross-world system, the declarative
Animal System (tame/companion, traps, tracks, encounters from a JSON manifest with no mod-side C#), and
standalone GameModifierPackage / flavour-synergy injection.

**Content**: 1 item / 1 blueprint / 2 structures / 1 perk / 2 custom images (the Portal Hub reference set
only - Wayfinder perk -> Portal Kit item -> placed Portal Hub CT2 -> Hub Exit CT8). ~147 `.cs` engine
files.

**Stability**: 9/10 - PASS. 0 preflight CRITICAL, 0 design gaps, 0 broken promises. Clean Release build
at 2.26.0 (0 errors / 0 warnings), localization parity clean (13 EN / 13 CN), bin/Release sync clean.
Carrying 1 runtime-verification caveat (C1 river-bridge, fix shipped 2.25.30) and 2 tracked warnings (W1
retro/open-plan bucket, W2 unused `Api.ContainerSort` - owner-decided LEAVE on 2026-09-17). W3
(code-quality F1) was RESOLVED 2026-09-17 in commit `8881c18cb`.

**Open work** (all 🟡 pending-verification / open-plan; none 🔴 open, none a current static defect):
- `river-bridge-east-click-noop` (T2.186) - fix shipped 2.25.30, present/unmodified at 2.26.0; awaits one in-game click check.
- `wikimod-old-save-load-crash` - fix shipped 2.25.25/2.25.26; a 2026-09-06 live log already showed it working; needs formal retro graduation.
- `worldmap-clone-duplicate-terrain` - data-duplication premise measured FALSE 2026-09-11; remaining check is presentation-layer, build-free.
- `cmc-village-conditional-drop-fixtures-missing` - CMC-scoped symptom; framework ships the 2.25.4 diagnostics that will pinpoint it.
- `questinjector-blueprint-reset-risk` (+ `RETRO_CLOSURE_PLAN_2026-07-23`) - shipped, hard-gated OFF since 2.17.0, owes root-cause diagnostics before any re-enable.
- `gate-red-baseline-rediscovery` - fix is to the TEST, queued as fleet mission `csff-gate-debt-preexisting` (not framework code).

**Framework compliance**: This mod IS the framework - it DEFINES Tier 2 (`Api.ActionRouter`,
`Api.SpawnService`, `Api.TickEvents`, `Api.ContentModPlugin`, `Api.EncounterGuards`, `Api.GameQuery`,
`Api.StatAccess`). No deprecated pattern present: no `DropCollectionGuardPatch`, no unfiltered hot-path
prefix, no manual perk/blueprint injection, no `ModLoaderVerison` in its own manifest. Process-lifetime
tick guards (`GameManager`-null early-outs) and per-run handler-registration discipline are in place.

---

## Phase 0: Stabilize

> Audit score is 9/10 (>= 8), but 🟡 retrospectives remain open, so this phase stays - it is light and
> mostly runtime-verification, not code.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **C1 `river-bridge-east-click-noop`** in-game close on `T2.186`: quit-to-desktop relaunch, load save, reach River Clearing (bridge built, Pathfinder held), click East, grep `LogOutput.log` for `[TravelDaCacheResync]` FIRST | Retro close (runtime) | P0 | Quick (human play) |
| ~~**W3 / F1** - add `ReflectionCache.HasParameterlessCtor(fieldType)` probe before the `Activator.CreateInstance` at `Util/ReflectionHelpers.cs:252`~~ - **DONE 2026-09-17, commit `8881c18cb`** (+2/-1, explicit-path; all three call sites now identical; Release rebuild 0 warnings / 0 errors) | Code consistency fix | ✅ Resolved | - |
| `wikimod-old-save-load-crash` - formal retro graduation (evidence already in hand, 2026-09-06 log) | Retro close (evidence-in-hand) | P1 | Quick |
| `worldmap-clone-duplicate-terrain` - run the two build-free discriminators (reload-in-place = visual vs data; pull WikiMod.dll = its dead `AddSlot` prefix); do NOT ship another board-data fix | Retro close (runtime) | P1 | Quick (human play) |

---

## Phase 1: Foundation

> Table-stakes hygiene; all currently GREEN, listed so a regression is caught on the next pass.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Versions synced across `ModInfo.json` / `Plugin.cs` / `README.md` (all 2.26.0) | Version hygiene | P1 | Quick (done) |
| Chinese localization parity (13 EN / 13 CN clean) | Localization | P1 | Quick (done) |
| bin/Release sync clean; refresh `lib/Assembly-CSharp.dll` on every game update and rebuild all mods | Game-version hygiene | P1 | Medium (per update) |
| Regenerate the 9 `Assembly-CSharp-nstrip.dll` binders with the external NStrip tool (authoring-surface only, not a runtime hazard on 0.67i per `RefCheck/`) | Game-version hygiene | P1 | Medium (owner tool) |

---

## Phase 2: Core Engine Surfaces

> New ENGINE seams downstream mods would consume. Each is blocked on a consumer, a design decision, or a
> root-cause, NOT on missing engine plumbing (see `.audit/ideas.md` for the 2026-09-11 premise
> corrections - several "dormant type needs an injector" ideas were refuted; the game self-enumerates
> those types from `DataBase.AllData`).

### Generalize `WildlifeRaidService` to `Api.Raid` / `Raids.json`
**What**: keep the shipped engine but expose trigger / target-tag / effect through a registration seam or a declarative `Raids.json`, replacing the single hardcoded bear-spoils-food rule.
**Why**: the only raid rule today is baked in; a seam lets Sirus23 and future hostile-encounter mods add raids without C#.
**Requires**: none (engine already exists).
**Complexity**: Medium.

### Author-time gate-misconfig static check
**What**: fold a `LockConditions` / `GateConditions` mutual-exclusion + `HideTravelDA:true` + `RestoreDAOnUnlock:false` permanent-red-X detector into the `F21-F27` WorldMap validators (`/audit-environments` + preflight).
**Why**: this is the exact author-time shape behind C1's class of defect; catching it in the linter prevents the next one.
**Requires**: none.
**Complexity**: Medium.

### Fold the ctor-probe into `InitializeSerializableDefaults` itself (from W3/F1)
**What**: rather than re-implementing `HasParameterlessCtor` at each of the three call sites, move the guard INSIDE the helper so no future caller can omit it.
**Why**: W3/F1 is exactly a caller that omitted the guard; centralizing removes the whole class.
**Requires**: none - W3/F1 shipped 2026-09-17 (`8881c18cb`), so this is now a pure centralization refactor over three identical call sites rather than a fix that supersedes a pending one.
**Complexity**: Quick.

---

## Phase 3: Integration & Depth

> Cross-mod surfaces and the shipped-but-unexercised paths.

### `QuestInjector` re-enable path
**What**: diagnose the blueprint-research-reset root cause that got `QuestInjector` hard-gated OFF (2.17.0), then a save-compat harness (add-content -> save -> remove-mod -> load) before any re-enable.
**Why**: unblocks quest-chain consumer mods (TradersAndNPCs, MagicAndSpirits); currently a documented dead path.
**Requires**: re-author the deleted `_FwVerificationHarness/` fixture; owner play session.
**Complexity**: Complex (spans sessions, needs runtime evidence).

### `NPCCharacterPerk` / `ConstructionCardGroup` / `CookingRecipeGroup` consumers
**What**: these types self-activate from `AllData` (no injector needed - premise corrected 2026-09-11); the missing half is a consumer mod that ships the content.
**Why**: turns already-working engine surfaces into player-visible content.
**Requires**: a downstream mod (DecorationAndComfort / CookingExpanded ideas). `NPCCharacterPerk` already has one (CMC Village Guards); its acceptance is `T2.235`.
**Complexity**: Medium (in the consumer, not here).

### General `PatchAll`-isolation seam
**What**: promote the WikiMod-specific `WikiModPatchAllRescue` into an `Api`-level "isolate a foreign plugin's PatchAll failure" seam if a second third-party plugin ever shows the same abort-cascade.
**Why**: reuse the 2.25.31 hardening generically.
**Requires**: a second real occurrence (do not build speculatively).
**Complexity**: Medium.

---

## Phase 4: Polish

> Remove the last dead weight and boilerplate.

| Item | What | Complexity |
|------|------|------------|
| W2 - `Api.ContainerSort` | **Owner-decided 2026-09-17: LEAVE AS-IS** as documented-unused API. This is settled, not an open question - do NOT re-raise it as a finding needing resolution, and do NOT delete `Api/ContainerSort.cs` on the reasoning that it is unreferenced. Listed here as a standing disclosure so the next audit recognises it rather than rediscovering it. | None (settled) |
| M1 - `csffmfwportalkit` boilerplate | Optionally fill the 11 omitted standard fields (no runtime impact; loader defaults them) | Quick |
| M2 - `csffmfw_perk_wayfinder` | Optionally declare `NoSafetyMode` explicitly (defaults false, non-functional today) | Quick |
| Refresh stale sub-reports | Re-run the four 2026-06-27 category audits + `karpathy-plan` at the next `/full-mod-audit-chain` (critical-analysis and code-quality are already current) | Quick |

---

## Long-term Vision

> Where the framework should be at v3.0.

The framework's endpoint is a fully declarative modding surface: every subsystem a content mod needs
(drops, improvements, blueprints, perks, worldmap, portals, animals, modifiers, flavour, quests, raids)
authorable from JSON with zero mod-side C#, and every shipped engine path exercised by at least one
consumer or explicitly retired. The two structural debts to retire before v3.0 are (a) the shipped-but-
unexercised injection paths (`QuestInjector`, `CharacterRosterInjector`, `SealableGates.ResealCondition`)
- either proven via a save-compat harness or removed. The second former debt, the last unused public API
(`Api.ContainerSort`), is CLOSED as of 2026-09-17: the owner decided it stays as documented-unused API,
so it is a standing disclosure rather than something to retire before v3.0.

**Potential major additions** (not yet justified - revisit after Phase 3):
- `Api.Raid` / `Raids.json` declarative hostile-encounter engine - fits the existing `WildlifeRaidService` and the animal/encounter theme.
- A scripted save-compat test harness as a first-class dev tool - closes every "never exercised in-game" path at once and is the precondition for re-enabling `QuestInjector`.
- `LocalTickCounter` activation investigation - the one genuinely uninvestigated `DirToTypeName` type (all others self-activate).

These live in `Documentation/Ideas/CSFFModFramework/IDEAS.md`; the injector-refuted rows already carry
their corrected "blocked on a consumer, do not write an injector" reasoning.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any engine-surface phase | Run `/audit-mod CSFFModFramework` and `/code-quality CSFFModFramework`, update this roadmap |
| Game version update | Refresh `lib/Assembly-CSharp.dll` on EVERY mod, regenerate the nstrip binders, `/decompile-assembly`, re-run `Development_Tools/RefCheck/`, re-run `/update-mod-version` |
| After fixing a defect on a runtime-gated path | Do NOT certify from source - collect the in-game log evidence per the retro's gate (CLAUDE.md "the log says it worked but in-game it did not") |
| After a framework change that content mods link | Rebuild every content mod clean (`-t:Rebuild`) to signature-check against the new reference |

---

## Skill Cheatsheet for This Mod

```
/audit-mod CSFFModFramework         - full health check, updates .audit/
/code-quality CSFFModFramework      - C# reliability/hazard scan
/critical-analysis CSFFModFramework - adversarial review
/build-mod CSFFModFramework         - build Release DLL
/deploy-mods CSFFMFW                - build + deploy framework to game (deploy FIRST in any batch)
/consolidate-audit CSFFModFramework - merge .audit/ reports + refresh this roadmap
/decompile-assembly                 - regenerate .decomp/ after a game update
```

---

## Plan Reconciliation Log

> Append-only: never reword, reorder or delete an entry. `/roadmap` Step 7 preserves
> this section verbatim when it overwrites the rest of this file. Record only what
> was ground-truthed.

### 2026-09-08 - Fleet plans consolidated into Master_Plan (Master_Plan, Duties_Ownership_Plan, Playthrough_R33_Failure_Remediation_Plan)
- **Scope:** Fleet. This same entry is recorded in the ROADMAP of CSFFModFramework, Community_Mod_Chest,
  Sirus23_Mod_Collection, WaterDrivenInfrastructure and AdvancedCopperTools. PartnerOverhaul, named by the
  R33 plan for T3.39, has no ROADMAP.md, so its rows are carried by the tracker and by
  `Documentation/Plans/Fleet/Master_Plan.md` only.
- **Verdict:** Master_Plan (2026-08-16 revision): 3 of 3 open items closed in-game (T2.63 and T2.77 PASS r33;
  T2.75 and T2.68 PASS r31); its 3-prompt pack is 3 of 3 VERIFIED-DONE; 1 loose end (fleet legacy-card
  GUID scan) still unactioned and carried. Duties_Ownership_Plan: M0, M0b, M2 and 4 of 5 M1 checks
  confirmed; M3 2 of 4 stations PASS (T2.87 Ore Sluice, T2.88 Sawmill) and 2 FAIL-confounded (T2.89 Forge,
  T2.90 Workshop); M3.5 root cause A fixed in WDI 1.10.13 and validated by r33 (three station duties ran),
  root cause B still confounded with an unbuilt or cold station; the 5th M1 check (ACT Brazier/Lantern
  ownership rows) had no tracker row and was backfilled as T2.212; M4 Fishpond is an owner decision with no
  sequencing intent; the pack's T1.56 ActionRouter note is still an open framework defect. 0 code rows
  remain in the Duties plan itself. Playthrough_R33_Failure_Remediation_Plan (14 rows, written 2026-09-06):
  re-derived today; 2 rows dissolved (T3.39 precondition unmet, T1.71 mis-scoped), 1 certain code fix still
  unwritten (CopperChestPatch GetSlotForCard arity, T4.40 and T4.38), 1 gated on a tester answer (T2.99, now
  behind the T2.207 vanilla discriminator), 1 design decision (T2.101), 6 human-gated re-runs, 2 needing a
  symptom; of its 5 Wave-0 actions 3 have since landed (Disk LogLevels includes Debug, VerboseLogging =
  true, CMC 1.68.19 once-per-change hearth verdict logging) and 2 remain (T3.39 re-arm, T1.71 re-scope).
- **Disposition:** Master_Plan KEEP, rewritten in place as the single fleet plan with a regenerated pack (6
  prompts, 1 open decision). Duties_Ownership_Plan ARCHIVE to
  `Documentation/Design/Duties_Ownership_As_Built.md` (plan verbatim, pack appended as an appendix) because
  the Blast/bellows automation rejection, the one-duty-per-card finding and the M3.5 diagnosis exist
  nowhere else; its pack DELETED from Plans; M4 moved to
  `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md` carrying its reason.
  Playthrough_R33_Failure_Remediation_Plan DELETE, folded into Master_Plan (per-row evidence already lives
  verbatim in the tracker notes; last full text at commit f4bc0f9d5); the T2.101 design question moved to
  `Documentation/Ideas/Sirus23_Mod_Collection/SHEEP_PEN.md` with its reasoning.
- **Pruned:** Master pack Prompts 1-3 and the plan's three Open Work sections (all PASS); Duties sections M0,
  M0b, M1 (4 of 5 checks), M2, M3 Ore Sluice and Sawmill, M3.5 root cause A; R33 Class A rows as defects,
  the Class D instrumentation asks now shipped in CMC 1.68.19, and the two Wave-0 config preconditions now
  armed in the deployed install.
- **Evidence:** tracker read by id across items/confirmedItems/droppedItems (124/229/20 rows at read time):
  T2.63, T2.77, T2.87, T2.88 carry `r33 PASS`; T2.75, T2.68 carry `PASS via r31`; T2.89, T2.90, T2.99,
  T2.101, T2.149, T2.137, T2.147, T2.128, T1.51, T2.113, T3.39, T1.71, T4.40, T4.38 all read `fail`;
  T1.56's note still reads `OPEN FRAMEWORK DEFECT - NOT YET FIXED`. Code re-read, not inherited:
  `Community_Mod_Chest/Patcher/CopperChestPatch.cs` still invokes `GetSlotForCard` with 4 arguments while
  `.decomp/GraphicsManager.cs:2587` declares 5; `MillDutyPatch.StationDutyBaseWeight = 850`, and all five
  station UID constants match their CardData `UniqueID`s; the forge and workshop `CompatibleDutiesWarpData`
  arrays carry both the Firekeeping GUID and their station duty UID; `CSFFModFramework/Api/ActionRouter.cs`
  still resolves `_cocReceiverIdx` as the first `InGameCardBase` parameter;
  `Sirus23_Mod_Collection/GameSourceModify/VanillaFox_Agent1.json` `TriggerCardsWarpData` still holds only
  the three dried-berry GUIDs; `SheepPenPatch.cs` still returns before the roll when a wolf is present;
  `VillageFireplacePatch.LogVerdict` (Info, once per change) is present, added in 1.68.19; all four ACT
  Brazier/Lantern CardData halves carry `UsesOwnershipSystem: true`. Deployed config read directly:
  `BepInEx.cfg` `[Logging.Disk] LogLevels` includes Debug, `crispywhips.CSFFModFramework.cfg`
  `VerboseLogging = true`, `crispywhips.partner_overhaul.cfg` `Reserved Fuel/Wood UIDs` empty.
  `ls */.audit/legacy-card-refs*` returned nothing (control: the same shell listed 13 `.audit/` folders).
  Citation sweep: 3 live pointers repointed (`.claude/commands/plan-to-prompts.md`,
  `.claude/commands/audit-plan.md`, `Playthrough_Test/README.md`) plus 3 memory files; two dated records
  (`CSFFModFramework/CHANGELOG.md` [2.22.5] and the `MillDutyPatch.cs` doc comment) left as written, and
  the archive's header names the old path so a grep for it still lands.
- **Verification debt:** T2.89, T2.90, T2.212 (new), T2.207 then T2.99, T2.101, T2.149, T2.137, T2.147,
  T2.128, T1.51, T2.113, T3.39, T1.71, T4.40, T4.38, all present in `.claude/playthrough-test-status.json`.

### 2026-09-09 - Fleet Master_Plan (Scope: Fleet)
- **Scope:** Fleet. The same entry is recorded in AdvancedCopperTools, CSFFModFramework,
  Community_Mod_Chest, HerbsAndFungi, Sirus23_Mod_Collection and WaterDrivenInfrastructure
  (PartnerOverhaul, also named by the plan, has no ROADMAP.md).
- **Verdict:** 0 of the 7 prompts and 1 decision carried by the 2026-09-08 rewrite remain as code.
  Prompts 1-4 BUILT, awaiting in-game verification (CMC 1.68.22, Sirus23 1.21.1, PartnerOverhaul
  1.0.4 + tracker re-arm, CSFFModFramework 2.25.29); Prompts 5 and 6 verified done 2026-09-09;
  Prompt 7 REFUTED (its premise was false, nothing built); the OPEN-DECISION (T2.101) taken as
  delegated and shipped as Sirus23 1.21.2 (partial wolf guard). 5 items awaiting verification, all
  with tracker rows.
- **Disposition:** ARCHIVE. Plan moved to `Documentation/Design/Fleet_Master_Plan_As_Built.md`
  with the refreshed pack appended as an appendix; the pack file deleted from
  `Documentation/Plans/Fleet/`. Inbound citations (tracker row sources, two earlier As_Built notes,
  three memory files) repointed or annotated in the same commit.
- **Pruned:** section 1.6 / Prompt 7 (closed as refuted, recorded in
  `Community_Mod_Chest/.audit/legacy-card-refs-2026-09-08.md` Correction 2); section 2.1 /
  OPEN-DECISION (decided; reasoning kept in
  `Documentation/Ideas/Sirus23_Mod_Collection/SHEEP_PEN.md`). Sections 1.1-1.4 were already
  collapsed to awaiting-verification pointers by the 2026-09-09 refresh and are unchanged.
- **Evidence:** Prompt 7: `git blame -L369,369 -- Community_Mod_Chest/TradingValues.json` returns
  `fa3dffd6f` (2026-07-27) for the row pricing `85f90db88eaa1804186ea7c3e561dd92` at 1500;
  `git log --oneline -- Community_Mod_Chest/TradingValues.json` returns that single commit; the
  export's `CardData/LeatherGloves.json` re-read as UniqueID `85f90db88eaa1804186ea7c3e561dd92`,
  `TradingValue` 0.0 (so the listed row is what prices it); grep for that GUID across every
  `TradingValues.json` and `GameSourceModify/` file returns only the CMC row (no later override).
  T2.101: r33 log quoted in the tracker row (five nights of `stood guard`, zero losses, 7/7/7/7/3
  unpenned); `SheepPenPatch.cs` early return replaced by `GuardedPredationChance` 0.01f; Release
  build 0 warnings / 0 errors; `Deploy-Mods.ps1 -Sirus23ModCollection` deployed 84 CardData, DLL
  byte-identical to bin/Release, the new literal `wolf on guard` present in the DLL's #US heap
  (UTF-16-LE); zip `Sirus23_Mod_Collection_1-21-2.zip`. Pack header at archive: 0 open, 5
  awaiting verification.
- **Verification debt:** T4.40, T4.38 (CMC Copper Chest); T2.207 then T2.99 (Sirus23 fox bait);
  T3.39, T1.71, T2.147, T2.128 (tracker hygiene, PartnerOverhaul count line, clone-tile live
  trim); T1.56 via T1.81 (framework card-on-card dispatch); T2.101 and T2.232 (Sheep Pen, without
  and with a wolf); plus the section 3 rows T2.89, T2.90, T2.212, T2.149, T2.137, T1.51, T2.113.
  All present in `.claude/playthrough-test-status.json`.

### 2026-09-11 - Audit_Remediation_Plan (2026-09-09 generation) retired; three premises refuted
- **Scope:** CSFFModFramework only. An ad-hoc pass answering "is this plan fully implemented?", not a
  `/cleanup-plans` sweep: no other mod's plans were read or touched, and this entry is recorded in no
  other ROADMAP.
- **Verdict:** 4 rows in, 0 code rows left. F1 (CRITICAL, `river-bridge-east-click-noop`) had already
  shipped in framework 2.25.30 (`c6973b394`); its in-game closure `T2.186` still reads `pending`. N3
  (`NPCCharacterPerk` consumer + cookbook doc) CLOSED, but not as written: its "the injector is
  shipped but has no consumer" claim was stale, because Community_Mod_Chest has shipped 4 wired
  bundles since 1.48.0; the genuinely missing half was the cookbook entry, now written. N1
  (`ConstructionCardGroup`) and N2 (`FlavourTag` / `CookingRecipeGroup` / `BookmarkGroup`) were NOT
  built, because the premise all three rows shared was refuted: 4 of those 5 types need no injector at
  all and `FlavourTag` needed nothing whatsoever. 1 new tracker row filed (`T2.235`); 2 IDEAS.md rows
  found already shipped.
- **Disposition:** DELETE from `Documentation/Plans/`. Analysis preserved as a second closure section
  appended to `Documentation/Design/CSFFModFramework_Audit_Remediation_As_Built.md`, which already held
  the 2026-09-07 generation of the same plan. N1 and N2 moved to
  `Documentation/Ideas/CSFFModFramework/IDEAS.md` Medium-Term carrying their corrected reason (blocked
  on a consumer mod, not on engine work, and explicitly "do not write an injector"); the matching rows
  in the gitignored `CSFFModFramework/.audit/ideas.md` were rewritten in place so `/audit-to-plan`
  cannot re-promote the refuted prescription. Because that `.audit/` tree is gitignored, THIS entry and
  the as-built doc are the durable record.
- **Pruned:** N3's "no consumer" claim; N2's `FlavourTag` half (closed outright, never dormant); the
  "thin injector mirroring `PerkInjector`" prescription on all three feature rows; and two IDEAS.md
  Near-Term rows that had already shipped (Animal subsystem cookbook docs, `GameModifierPackage`
  standalone activation), which leaves that section empty.
- **Evidence:** Consuming side read in `.decomp/`, which is what both prior passes skipped:
  `GameManager.InitializeStatsAndActions` self-populates `AllCookingRecipeGroups` (:2735),
  `AllNPCPerks` (:2743), `GameGraphics.AllBookmarkGroups` (:2747) and `ConstructionGroups` +
  `ConstructionGroupsDict` (:2813-2830) by exact-type match over `dataBase.AllData`, and
  `ExplorationPopup.cs:816` reads that dict rather than building its own. Registration side:
  `CSFFModFramework/Loading/JsonDataLoader.cs:262` calls `GameRegistry.TryAddToAllData` for every
  loaded UID object. Ordering: `LoadMainGameData` is called at `.decomp/GameLoad.cs:405` (pre-menu,
  where the framework's postfix runs) and `InitializeStatsAndActions` at `.decomp/GameManager.cs:2389`
  from `Awake` (per run boot). A `Grep` over `CSFFModFramework/**/*.cs` for all five type names
  returned ONLY `DirToTypeName` registration entries (`JsonDataLoader.cs` lines 38, 45, 52, 53, 54)
  plus `FlavourMatrixInjector`'s own `FlavourTag` references: zero injector files, which is the same
  zero both prior passes got and misread. `FlavourMatrixInjector.cs:12-14` states individual
  FlavourTags self-activate. Consumer check: `Community_Mod_Chest/NPCCharacterPerk/` holds 4 files and
  `NPCAgent/Agent_GuardCorrin.json:17` references `cmcGuardPerkCorrin`. Doc gap confirmed by
  `grep -n NPCCharacterPerk Documentation/CSFF_Patterns.md` returning 0 before the entry was appended
  (+91 lines after, em-dash count unchanged at 357). Tracker read by parsing all three buckets (379
  dict rows): 0 rows mentioned `NPCCharacterPerk` or personality bundles, `T2.186` read `pending`, max
  allocated id was T2.234 so `T2.235` was taken; the insert was text-spliced and measured +9/-0 with
  `droppedItems`/`confirmedItems` byte-identical and every pre-existing `items` row unchanged. F1
  artifacts confirmed present on disk (`Patching/BugFixes/TravelDaCacheResync.cs`,
  `Development_Tools/Tests/Framework-ConnectionGateEvaluation.Tests.ps1`) and `c6973b394` resolves.
  Deploy coverage for all five content folders is present in `Deploy-Mods.ps1` and `Pack-Suite.ps1`;
  `deploy.sh` matched none of them, which is a NON-finding because it copies `bin/Release` wholesale
  via `find "$release_dir" -type f` and carries no per-folder list. Nothing in Correction 1 was
  observed in a running game; `T2.235` is the first observation that will bear on it.

### 2026-09-17 - Trait_Effect_Repair_Plan (closeout)
- **Scope:** Fleet. The same entry is in `Community_Mod_Chest/ROADMAP.md`, `HerbsAndFungi/ROADMAP.md`, `Sirus23_Mod_Collection/ROADMAP.md` and `CSFFModFramework/ROADMAP.md`, the four mods the plan names.
- **Verdict:** every phase built: P1 (CMC 1.68.25), P1b (1.68.26, `1f71be4a7`), P1c (1.68.27, `b5d93a885`), P7 (CMC 1.68.28 and HerbsAndFungi 1.13.1, `e4fad82ab`), P2-P6 (CMC 1.68.30, Sirus23 1.21.3, CSFFModFramework 2.26.0, `7b1c68f64`), and the pack's last open prompt, Prompt 1 (delete the temporary `Community_Mod_Chest/Patcher/TraitDiagnostics.cs` tracer), in `8d25ed810` with no version bump. Prompt 1 was gated on playthrough row T2.239, which is still pending; the owner lifted that gate on 2026-09-17 ("We need to be able to proceed with code work without being blocked on a full playthrough"). The plan doc's own row inventory was compared against the pack rather than trusting its 0-open count, and three plan items with no recorded outcome were settled this pass, none needing code: section 3.6's pre-release check (no `TriggerRange` on the four infection stats Deadly Disease rate-modifies), section 3.2's Swimmer 5 Sun re-check (kept), and section 3.1's "drop madness from Lunacy's text" (already done in 1.68.30). 0 plan rows unbuilt.
- **Disposition:** ARCHIVE to `Documentation/Design/Trait_Effect_Repair_As_Built.md`: the plan with an archive banner, dated notes for Prompt 1 and the three checks above, and the drained pack appended as an appendix with Prompt 1 removed. Pack `Documentation/Plans/Fleet/Trait_Effect_Repair_Plan_Implementation_Prompts.md` deleted. The plan holds the only record of the ground truth behind the rework (R1-R20, D1-D9) and of every deviation from its starting values, so it is kept, not deleted. Not published: the public releases are still CMC 1.68.24, HerbsAndFungi 1.13.0, Sirus23 1.21.2 and CSFFModFramework 2.25.32 (`.claude/mod-publish-status.json` commits read back through each `ModInfo.json`), and the export is the owner's call. R14/R16's reading of the player's "+1.2 Speed" as the +1.2 Aid rate is still unconfirmed with the player; it is recorded in the archive's section 1.
- **Pruned:** Prompt 1 from the pack before the pack was folded into the archive and deleted; nothing else. The plan left `Documentation/Plans/Fleet/` whole.
- **Evidence:** Prompt 1: `dotnet build -c Release` 0 warnings 0 errors; the built `bin/Release/Community_Mod_Chest.dll` holds `TraitDiagnostics` (UTF-8) 0 times and `[TRAITDIAG]` (UTF-16-LE) 0 times, against controls `RiverSwimPatch` 1 and `[RiverSwimPatch]` 9; `grep -rn TraitDiagnostics --include=*.cs Community_Mod_Chest` 0 lines (control `RiverSwimPatch` 15); no file under `Documentation/Retrospectives/` cites `TRAITDIAG` or `EnableTraitDiagnostics`. Plan rows against code: `Community_Mod_Chest/GameStat/` holds all nine `CMC_Trait*.json` (eight trait stats plus `CMC_TraitSkinShelter.json`); `TraitsTickHandler.cs`, `TraitsActionHandler.cs` and `TraitDiagnostics.cs` absent from `Patcher/`; gates (a)-(e) plus the extended river swim test present as `TraitStat-CompositeBands`, `Perk-HeldTestNotAllPerks`, `PerkAidRate-HoldsTier`, `PerkStatClamp-Reachability`, `StatBase-NoVanillaSource` and `CMC-RiverSwim` `.Tests.ps1`; `CSFFModFramework/Patching/PerkOriginTagPatch.cs` and `Discovery/ModTag.cs` present, `[Perks] ShowModOriginTag` bound at `CSFFModFramework/Plugin.cs` and documented in its README config table; 12 `ModInfo.json` files carry `ShortName`; 21 Sirus23 JSON files reference `SaturationDairy` (by name or its UID `f4b08d0250e6099419e010a83578b9db`); `ModInfo.json` versions CMC 1.68.30, HerbsAndFungi 1.13.2, Sirus23 1.21.3, CSFFModFramework 2.26.0. Section 3.6 check: a JSON walk of 29,435 files (the vanilla EA 0.67i UniqueID and ScriptableObject exports plus every mod folder, 0 unparseable) found 0 `TriggerRange` objects whose `StatWarpData` is Infection_Gastrointestinal `1a8d37787d69c9b4aa05d332921f3763`, Infection_UpperRespiratory `3407bfc804966194e9e369a7cce6d07d`, Infection_Systemic `dc3cae53109fd5945b5279ec6291caae` or Infection_LowerRespiratory `fa47d156a14dac842bcdcbdbf8504e35` (by UID or name), with the same walk finding the control, vanilla `Tgr_Anxiety` on Stress at 240; `.decomp/` (949 `.cs`) names no infection stat (controls `HourOfTheDayValue` 4 files, `StatValueTrigger` 13), and no mod `.cs` references the four UIDs. Section 3.2: vanilla `CharacterPerk` SunsCost is only ever 0, 15 or 30 (71/38/20 of 129), and Swimmer's 5 matches the six other five-Sun CMC perks (CMC's SunsCost counts: 0 x20, 1 x21, 5 x7, 15 x5, and 10, 20, 25, 30, 100 once each), which include Abundant Growth's +1.2 Aid rate and Wide Hands' +15 skill offset; Swimmer now delivers what its price was set for. Section 3.1: `madness`, `insan` and `mania` absent from `Pk_Lunacy.json`, `CMC_TraitLunacy.json` and its CSV rows. Tracker: T2.239 and T1.84-T1.114 all present in `.claude/playthrough-test-status.json`, all `pending`, so lifecycle gate 2 holds; the 33 plan and pack path citations in it and the one in `Playthrough_Test/Playthrough_Checklist.html` were repointed at the archive in the same commit.
- **Verification debt:** T2.239 (Nyctophobia and the tracer's log lines), T1.84-T1.88 (P1b), T1.89-T1.97 (P1c), T1.98-T1.101 (P7 medicine), T1.102-T1.109 (P2 condition traits), T1.110-T1.111 (P3 swim and Aid), T1.112 (P4 Sirus23 dairy), T1.113-T1.114 (P5 origin tag and clock hour), all `pending`. T2.239 part (5), T1.114 part (4) and the optional evidence in T1.102-T1.108 read `[TRAITDIAG]` lines that CMC 1.68.30 on the dev install still prints until the next CMC deploy and no later build prints; each of those rows carries a dated 2026-09-17 note saying so.
