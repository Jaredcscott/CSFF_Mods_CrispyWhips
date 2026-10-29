# Roadmap: CSFF Mod Framework
Version at time of writing: 2.28.10
Date: 2026-10-27 (refreshed by `/consolidate-audit` after the 2.28.10 release)
Audit score: 10/10 - PASS (2026-10-27 core/assets/portal module re-audits all returned SOLID / 10-10; whole-mod critical-analysis SOLID, code-quality 10/10, mod-report PASS, 0 CRITICAL; two portal-module MINOR polish items, non-blocking)

> **This is the framework, not a content mod.** The content-mod roadmap template (Tier 2 migration,
> theme expansion, art) mostly does not apply: the framework DEFINES Tier 2, and its own content is a
> minimal Portal Hub reference set plus the shared Metal Sheet / Metal Nail / Tin Solder parts that
> downstream mods build with. "Expansion" here means new ENGINE surfaces that downstream mods consume,
> and "polish" means removing the last dead API and boilerplate. Player-facing content, recipes and
> balance belong in downstream mods, not here. The `Documentation/Plans/CSFFModFramework/MASTER_PLAN.md`
> drained and was removed on 2026-10-12 (its round-2 pack closed by Prompt 33); the framework is
> feature-complete. The owner chose 2026-10-12 to keep it at the repo root rather than move it to `Stable/`.

## Current State

**Theme**: The shared engine for Card Survival: Fantasy Forest mods - JSON-driven content loading, WarpData
resolution, sprite/localization loading, perk/blueprint/improvement/drop injection, declarative WorldMap,
Portal Hub, the declarative Animal System, encounter guards, GameModifierPackage and flavour-synergy
injection, a shared GpTag vocabulary (`TagGroups.json`, GpTag_FW_Oil/OilIngredient), plus a set of
base-game bug fixes. 17 content mods (2,622 UIDs) load through it.

**Content**: 4 internal items / 8 blueprints / 2 structures (Portal Hub) / 1 perk (Wayfinder) / 19 custom
images / 3 shared parts (Metal Sheet, Metal Nail, Tin Solder) + their blueprints. 178 `.cs` source files.

**Stability**: 10/10 - PASS. Build clean (Release, 0/0), versions synchronized at 2.28.10, localization and
Chinese parity CLEAN (62/62), bin/Release in sync, 0 cross-mod structural refs or tag collisions across the
17 dependent mods. No advertised dead code. New since 2.28.7 and audited clean: `TagGroupService`
(TagGroups.json), `DragStartCrashGuard`, GIF PNG-frame sequences - all wired, documented, consumed live and gated green.
The 2026-10-27 portal module re-audit surfaced two maintainability items (hoist a load-time `GetFields()` out of a loop;
delete a dead private method), both non-blocking - see Phase 4.

**Open work**: 0 red (Open) retrospectives. 2 yellow debts: `wikimod-old-save-load-crash` (2.25.25/26 rescue
patches not re-verified in-game) and `questinjector-blueprint-reset-risk` / `RETRO_CLOSURE_PLAN_2026-07-23`
(`QuestInjector` dormant behind `EnableQuestInjection=false`, blueprint-reset root cause undiagnosed). Three
framework retros graduated 2026-10-12 (river-bridge-east-click-noop, worldmap-clone-duplicate-terrain,
cmc-village-conditional-drop-fixtures-missing); vanilla-pen-coop-null-tag-wedge graduated.

**Framework compliance**: N/A - this IS the framework. It defines Tier 2 (`Api.ActionRouter`,
`Api.SpawnService`, `Api.TickEvents`, `Api.EncounterGuards`, `Api.ContentModPlugin`) and is the dependency
target for every content mod.

---

## Phase 0: Stabilize

> Skipped - audit score is 10/10 and there are no red retrospectives. The two yellow retro debts are
> carried as verification work in Phase 1, not blockers.

---

## Phase 1: Foundation / Close-out

> The framework is feature-complete. "Foundation" here is the release tail and the two open verification debts.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Nexus release batch: framework 2.28.10 is ahead of Nexus 2.26.12; ship with the dependent-mod batch (`/nexus-publish upload --behind`) | Release tail | P1 | Medium |
| `/package-mod-suite` + Mod_Update_Manager repack per the MUM-last rule | Release tail | P1 | Quick |
| Verify `wikimod-old-save-load-crash` fix in-game on an old WikiMod save, then `/resolve-retro` | Retro close | P2 | Medium |
| Decide `OffEnvNpcAiThrottle` ship/keep: feature-17 measurement showed ~310/385 ms per off-location tick is vanilla agent actions (16% is a lower bound vs the 20% bar); owner kept it default-off 2026-10-12 | Decision (settled) | P3 | Quick |

---

## Phase 2: Core Expansion (engine surfaces)

> Only built when a downstream consumer needs the surface - the framework does not grow speculative engine
> features. Each item below is blocked on a consumer mod, not on engine work.

### Generalize `WildlifeRaidService` (Api.Raid / Raids.json)
**What**: Promote the one hardcoded bear-spoils-food rule to a registration surface so a mod can declare raid rules in JSON.
**Why**: Removes the last hardcoded content rule from the engine. Currently a Considered row, not open.
**Requires**: A second raid rule to exist (Sirus23 or a future HostileEncounters mod). Do not build on spec.
**Complexity**: Medium

### Re-enable `QuestInjector` (after root-cause diagnosis)
**What**: Diagnose why `QuestInjector` resets blueprint research on save load, then re-enable behind a flag.
**Why**: Unblocks any quest-chain downstream mod (TradersAndNPCs, MagicAndSpirits).
**Requires**: A scripted save-compat harness (add-content -> save -> remove-mod -> load); re-author the deleted `fwHarnessTestCharacter` fixture first.
**Complexity**: Complex (spans sessions; root cause undiagnosed since 2026-06-17)

---

## Phase 3: Integration & Depth

> Dormant surfaces that already ship and only need a consumer to exercise them (no engine work).

### `ConstructionCardGroup` / `CookingRecipeGroup` / `BookmarkGroup` content
**What**: Downstream mods ship `ScriptableObject/<Type>/*.json` for these types.
**Why**: These self-activate from `DataBase.AllData` in `InitializeStatsAndActions` - **do NOT write a thin injector** (memory `reference_dirtotypename_types_self_activate`).
**Requires**: A consumer mod (DecorationAndComfort, CookingExpanded idea mods).
**Complexity**: Medium (in the consumer, not here)

### `DropInjections.json` biome-conditional parity
**What**: H&F still carries ~1,200 lines of hand-rolled biome forage C#; the declarative drop path could replace it.
**Why**: Removes a large hand-rolled surface from a downstream mod onto the shared engine.
**Requires**: Coordination with HerbsAndFungi.
**Complexity**: Complex

---

## Phase 4: Polish

> Tooling niceties only - the framework's own content is minimal and already shipped with art.

| Item | What | Complexity |
|------|------|------------|
| Hoist `GetFields()` out of the world loop (M1) | `Portal/PortalService.cs:525` re-enumerates `daType.GetFields(...)` once per registered world inside `foreach (var world in worlds)`; hoist above the loop (the `lkFieldLS`/`dtFieldLS` lookups at :513-518 already are). Load-time, small N - cleanup, not a perf fix | Quick |
| Delete dead private `FindField` (M2) | `Portal/PortalService.cs:806-814` defines `FindField(Type, string)` but nothing calls it; remove the clutter | Quick |
| D17 out-param breadcrumb shape | Teach the preflight D17 check the `causeKey=`/`cause=` out-param pattern so it stops re-flagging WorldMapInjector.cs:2078 as a false positive | Quick |
| Author-time gate-misconfig detection | Fold a `LockConditions`/`GateConditions` mutual-exclusion check into the F21-F27 WorldMap validators | Medium |
| Foreign-plugin PatchAll isolation seam | Only if a second plugin (beyond WikiMod) exhibits the abort-cascade; general `Api`-level seam | Complex |

---

## Long-term Vision

> At v3.0 the framework is a fully declarative content engine: every subsystem a content mod needs
> (content, drops, blueprints, perks, WorldMap, animals, encounters, quests, modifiers, flavour) is
> authorable from JSON with no mod-side C#, and the remaining hand-rolled C# in downstream mods (H&F's
> biome forage, any quest chains) has migrated onto shared declarative surfaces. The engine grows only
> when a real consumer needs a surface; it never carries a speculative or dead API.

**Potential major additions** (not yet justified - revisit when a consumer appears):
- `Api.Raid` / `Raids.json` generalization - fits once a second raid rule exists.
- `QuestInjector` re-enable - fits once the blueprint-reset root cause is diagnosed.
- A general foreign-plugin PatchAll isolation seam - fits once a second plugin shows the abort-cascade.

These live in the framework's archived as-built docs (`Documentation/Design/CSFFModFramework_*_As_Built.md`)
and the dormant-surface dispositions in `.audit/ideas.md`; none is a `Documentation/Plans/` entry today
(the plan drained). Never create a stub under `Documentation/Ideas/`, which keeps no backlog since 2026-10-01.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any engine-surface change | Run `/audit-mod CSFFModFramework` and update this roadmap |
| Game version update | Run `/update-mod-version`, refresh every content mod's `lib/Assembly-CSharp.dll`, re-run `/diagnose-log` |
| After fixing a framework bug | Run `/critical-analysis CSFFModFramework` to verify the fix; add a watched-red gate |
| Before a release batch | Confirm every dependent mod's build resolves against the framework version Nexus will serve (RefCheck in `nexus_publish.py`) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod CSFFModFramework         - full health check, updates .audit/
/critical-analysis CSFFModFramework - adversarial review
/build-mod CSFFModFramework         - build Release DLL
/deploy-mods CSFFMFW                - build + deploy to game (CSFF_Mod_Framework/)
/nexus-publish upload --behind      - ship the release batch when mods are ahead of Nexus
/package-mod-suite                  - repack the suite into Mod Update Manager (MUM-last rule)
```

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

### 2026-09-26 - Content_Update_Plan (2.27.0, closeout)
- **Scope:** CSFFModFramework only. Consumer JSON (AdvancedCopperTools MetalType rows, HerbsAndFungi and CMC Seasons, Harsh Wilds Spawn.Conditions) ships in each consumer's own release and is not part of this plan.
- **Verdict:** all three features built and committed in `fdb344e96` (framework 2.27.0): SmeltingRecipes.json `MetalType`, DropInjections.json per-drop `Seasons`, Animals/*.json `Spawn.Conditions` + `Spawn.ConditionRespawnTicks`. W0-1 GREEN, so feature 1 stays in 2.27.0.
- **Disposition:** ARCHIVE to `Documentation/Design/CSFFModFramework_Content_Update_As_Built.md` (the plan whole, with its Progress note and open questions). Nothing ruled out; the section 0 deferrals keep their reasons in the archive.
- **Pruned:** nothing from the plan body. The three harness rows were recorded and pruned from `Documentation/Playthrough_Test_Plan.md` and `Playthrough_Test/Playthrough_Checklist.html`.
- **Evidence:** TestHarness run 2026-09-26 on EA 0.68b, harness 1.3.0: T2.360 PASS (2 nuggets, both Metal Type 200), T2.361 PASS (3 suppressors, -1000 in range, 0 at 0), T2.362 PASS on the second run (AgentExists 0 -> 1 -> 0, parked in Env_SpiritWorld; the first run failed only a name lookup). Gates: the three new Pester files and 17 Framework-ReflectedSignatures rows; recorded full gate `csff-mods-test-20260927T011309Z`.
- **Verification debt:** T2.363 (an ACT iron item's nuggets feed an iron-gated ACT recipe; waits on ACT's MetalType rows), T2.364 (a consumer's seasoned forage drop in and out of season; H&F plans T2.296 for it), T2.365 (a Harsh Wilds threat switched off never appears).

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 30 selected feature rows and 28 pack prompts (13 open, 15 awaiting) checked. 1 verified done (feature 4, T2.1200-T2.1202 PASS in `confirmedItems`), 15 built and awaiting a harness or human row (features 1, 2, 3, 5, 6, 7, 8, 9, 10, 12, 13, 14, 21, 22, 27; the pack's 15 Awaiting prompts), 2 built with a remainder (feature 28: fix `38282c0e7`, T2.763 green on a deploy and an arrival row still unfiled; feature 29: code `50d3fdb91`, gate 20 passed 0 failed, CHANGELOG, README and row T2.1221 open), 12 open (11, 15, 16, 17, 18, 19, 20, 23, 24, 25, 26 and 30). No FAILED and no OBSOLETE rows; STALE-CLAIMs corrected: the plan's status block called sections 28 and 29 hand-ins with no work done, and a handoff paragraph described section 29 as uncommitted.
- **Disposition:** KEEP IN PLACE (13 open pack prompts remain: 6, 20, 23 to 33; the pack's first prompt that needs no live-game lane is 23).
- **Pruned:** the plan's 2026-10-03 handoff paragraph (landed work is in the commits and the pack, its in-flight section 29 is committed, its lane and usage-limit times had passed). Nothing else: the 2026-10-04 refresh against `287df0ac1` had already collapsed every built section and pruned every finished prompt, and this pass found no further verified-done text. Added to the plan: a dated audit paragraph in the status block, the Selected-table row for section 30 and built notes on rows 28 and 29, and a status bullet in section 29. The pack was not edited: its counts re-count correctly.
- **Evidence:** `git cat-file -t` on every distinct 9-hex hash in the plan and the pack: all resolve to commits, 0 missing. Every `T<n>.<n>` id in the plan (35) and the pack (25) looked up across `items`, `droppedItems` and `confirmedItems` of `.claude/playthrough-test-status.json` (892 rows parsed): 0 missing; T2.1203-T2.1225 all `pending`, T2.1200-T2.1202 and T2.131 and T2.362 `pass`. 14 source and gate files the built rows name exist (including `Patching/AgentEncounterGuardPatch.cs`, `Loading/AuthoringLint.cs`, `Loading/PhaseTimings.cs`, `TestHarness/Game/ProbeS28.cs`). Config-key greps over `CSFFModFramework/*.cs`: the unbuilt keys `CompressModSprites`, `BearRaidEscalationDays`, `LogFrameTiming`, `LogCatchUpEndState`, `OffEnvNpcAiThrottle`, `OffEnvCardSkip`, `CatchUpTailTicks` and the `Settled` timing each match 0 files, with the built keys `CatchUpEquipmentScanSkip` (2 files), `GuardWikiModChineseInEnglish` (1) and `AuthoringLint` (4) as controls that prove the probe hits; `OffScreenCardThrottleFrames` still defaults to 3 (feature 16 open). `AuthoringLint.Rules` holds four rules and none is foreign-UID (feature 19 open). Section 29: `Framework-TypedCacheModCards.Tests.ps1` run alone, 20 passed 0 failed; `FindCardsWhere` in `CSFFModFramework/CHANGELOG.md` and `README.md` matches 0 (control: `GrantInRunPerk` matches in the CHANGELOG). Section 28: `PortalArrivalNextEnvGuard` is wired in `Plugin.cs` and `Portal/PortalService.cs`, and the CHANGELOG [2.28.0] entry exists. Section 30: the "Contamination check" block in `Injection/WorldMapInjector.cs` still compares the whole saved board's card count. `ModInfo.json` and `Plugin.cs` read 2.28.0 against 2.26.12 in `.claude/nexus-publish-status.json`. Nothing here was observed in a running game.
- **Verification debt:** T2.1203 through T2.1225 (23 rows, all pending: features 5, 27, 6, 7, 8, 10, 2, 9, 12, 14, 22, 1, 3, 13, 21 and section 29's T2.1221, which cannot run until a lane turn), T2.763 (section 28's portal step, pending ACT row), and the cited T2.273, T2.296, T2.453, T2.454, T2.456. Section 28's second row (the Metal Mines trip, reload and trip again) and section 30's harness row have no tracker row yet.

### 2026-10-04 - MASTER_PLAN (refresh after the framework harness batch)
- **Verdict:** 17 pack Awaiting entries and 16 open prompts re-checked against the live `.claude/playthrough-test-status.json` and the 2026-10-04 harness reports. 6 verified done, every row PASS and the source re-read: feature 27 (Prompt 34, `5d32f670b`, T2.1205), 6 (Prompt 9, `409d0a475`, T2.1206), 8 (Prompt 11, `ae47e8057`, T2.1208, the regression leg; the allow-list acceptance is Sirus23 plan feature 8's), 14 (Prompt 16, `33133bf38`, T2.1214), 3 (Prompt 19, `aa803945f`, T2.1222) and 21 (Prompt 22, `8a209b269`, T2.1225). 11 still awaiting (features 1, 2, 5, 7, 9, 10, 12, 13, 22, 28, 29); features 2, 5, 9 and 22 each have one PASS row and an open leg. 16 open prompts unchanged (6, 20, 23 to 33, 35, 36, 37). 0 obsolete, 0 failed.
- **Disposition:** KEEP IN PLACE (16 open prompts; the first that needs no live-game lane is 23).
- **Pruned:** pack: the six Awaiting entries above, and every remaining Awaiting entry rewritten to its current state. Plan: sections 3, 6, 8, 14, 21 and 27 deleted (text at `git show 935f9e51d:Documentation/Plans/CSFFModFramework/MASTER_PLAN.md`), with a DONE note on each one's Selected-table row; sections 1, 2, 7, 9, 10, 12, 13 and 22 collapsed to the awaiting pointer (section 12 keeps one bullet Prompt 23 reads). Citations repointed: `Documentation/Plans/HerbsAndFungi/MASTER_PLAN_Implementation_Prompts.md` (an H&F prompt read section 8) and the `CSFFModFramework/CLAUDE.md` heading that cited section 21.
- **Evidence:** `confirmedItems` holds T2.1203, T2.1205, T2.1206, T2.1208, T2.1210, T2.1212, T2.1214, T2.1215, T2.1222 and T2.1225 as PASS (autoplay, recorded in `b71c48631`, each note naming its report.md); the one framework commit after those runs, `d0877ba83`, touches `Loading/AuthoringLint.cs`, the framework DLL and the T2.1204, T2.1213, T2.1216 and T2.1219 scenarios, no cited source file of a verified feature. Source re-read: `SeasonChance` on 25 lines of `Injection/DropInjector.cs`; `EquippedCards` in `Api/GameQuery.cs`; `AddAlternateIngredientIn` in `Api/BlueprintAlternates.cs`; `slowest:` at `Loading/LoadOrchestrator.cs` line 423; `LoadImage(bytes, true)` at `Loading/SpriteResolver.cs` line 177; README "Unresolved references" and the `CSFFModFramework/CLAUDE.md` contract line; the five gates on disk. Not done, by criterion: T2.1212's PASS ran the load half only (its note says the scenario never runs the Basket-and-Chest leg), and T2.1215's report ran the wiring and fuel legs while the CHANGELOG still says the saving is not measured.
- **Verification debt:** reruns after a redeploy from `d0877ba83`: T2.1204, T2.1213, T2.1216 and T2.1219 (FAIL, scenario or lint fixed), T2.1207, T2.1209, T2.1217 and T2.1218 (PASS, gone STALE); T2.362 re-run on the `StatAccess` build; never run: T2.763, T2.1211, T2.1220, T2.1221, T2.1223, T2.1224, T2.1226. No open row yet for feature 9's Basket-and-Chest leg or feature 22's per-hop timing leg (their rows T2.1212 and T2.1215 are in `confirmedItems`).

### 2026-10-05 - MASTER_PLAN (section 28 verified)
- **Verdict:** section 28 VERIFIED DONE: T2.1226 PASS (harness 2026-10-05 02:46, report `TestHarness/client/reports/t2-1226-...-20261005-024627`, recorded FULL with a review.json) and T2.763's portal step PASS (02:25, report `t2-763-...-20261005-022504`), both on the `dcc250731` deploy.
- **Disposition:** section 28 collapsed to a DONE stub (full text at `git show 6568f9754:Documentation/Plans/CSFFModFramework/MASTER_PLAN.md`), its Selected-table row marked DONE; pack Prompt 37 and its Awaiting entry removed.
- **Evidence:** Player.log for the T2.1226 process: 0 `CalculateEnvironmentWeight`, 0 `NullReferenceException`; a CONTROL step proved the check reads that live file. LogOutput.log: 0 `[S28] W` lines; the guard's 'kept the portal arrival's destination' line on each arrival.
- **Verification debt:** T2.763 stays pending for its person clauses (the look, the refusal text, old saves) and the ACT clauses its scenario does not assert (listed in its note); T2.275 (portal return after a reload) has no scenario.

### 2026-10-05 - MASTER_PLAN (refresh against `05edd8f40`)
- **Verdict:** 10 pack Awaiting entries and 15 open prompts re-checked against the live `.claude/playthrough-test-status.json`, the 2026-10-05 harness reports (`TestHarness/client/reports/`, `Playthrough_Test/results/harness-sweep-2026-10-05/`) and `Playthrough_Test/results/2026-10-05_harness_test_report.md`. 3 verified done: feature 7 (Prompt 10, `ee3240661`, T2.1207), 10 (Prompt 12, `01a531d37`, T2.1209) and 12 (Prompt 15, `10e37d03d` narrowed in `d0877ba83`, T2.1213), each row in `confirmedItems` (recorded in `c46ce710d`, batch walkthrough-20261004-214849 on the `dcc250731` deploy). 2 failed: feature 1 (Prompt 18; T2.1216-T2.1218 PASS, T2.1219 harness FAIL twice, untriaged, its bandit never reached the player's environment) and section 29 (T2.1221 harness FAIL twice: 600-656 objects missing from the typed cache, WDI's runtime mill-race clones added by `allDataList.Add` at `WaterDrivenInfrastructure/Patcher/GameLoadPatch.cs` lines 648 and 717). 5 still awaiting (features 5, 9, 2, 22, 13). 15 open prompts unchanged (6, 20, 23 to 33, 35, 36). 0 obsolete.
- **Disposition:** KEEP IN PLACE (15 open prompts; the first that needs no live-game lane is 23).
- **Pruned:** pack: Prompts 10, 12 and 15 removed from Awaiting; Prompt 18 and feature 29 moved to a new Failed list; Prompt 36 lost its scenario step (the scenario exists) and its plan-collapse step; Prompt 35's line citations corrected (block now near 1448-1470, `GetSealableGateSlack` at 2076). Plan: sections 7, 10 and 12 deleted (text at `git show 05edd8f40:Documentation/Plans/CSFFModFramework/MASTER_PLAN.md`), DONE notes on their Selected-table rows; section 12's rule-shape bullet (the `Rule` delegate and the `ScriptableObject/CardData/Lint/` fixture location) moved into section 19 and Prompt 23; section 1's pointer now names the Failed list; section 29 gained its FAIL bullet.
- **Evidence:** `confirmedItems` T2.1204, T2.1207, T2.1209, T2.1213, T2.1216, T2.1217, T2.1218 with `confirmedOn` 2026-10-05T04:14:16Z (commit `c46ce710d`); the only framework commits since that deploy (`e68b6c4e6`, `694c468fd`, the UniqueIDIndex fix) touch none of those features' source files. T2.1221 reports `t2-1221-...-20261005-035753` (3974 vs 4574, 600 missing) and `Playthrough_Test/results/harness-sweep-2026-10-05/reports/t2-1221-report.md` (4029 vs 4666, 656 missing, first `water_sawmill_mill_race_north_124e6a3d`; steps 7-9 PASS). T2.1219 reports `...-20261005-000203` and `...-20261005-030605` (step 15: no bandit `withPlayer`; `DutySuppressed` 0). T2.1223 reports `...-20261004-205847` and `...-20261005-015312` failed their WikiMod-loaded control, so they are not evidence. T2.1227 harness PASS 2026-10-05 10:59 (batch 1b), not recorded. Source: `FindCardsWhere` matches 0 in `CSFFModFramework/CHANGELOG.md` and `README.md` (control `GrantInRunPerk` 1); the "Contamination check" in `Injection/WorldMapInjector.cs` still compares `cardCount > prep.Def.ExtraDropUIDs.Count + 1 + sealableSlack` (line 1469); no file under `CSFFModFramework` names `LogFrameTiming`, `CompressModSprites`, `BearRaidEscalationDays`, `LogCatchUpEndState`, `OffEnvNpcAiThrottle`, `OffEnvCardSkip` or `CatchUpTailTicks`.
- **Verification debt:** T2.1229 (filed this pass: the T2.362 re-run on the `StatAccess` build, which the pack carried with no row), T2.1227 (PASS awaiting record), T2.1219 (rerun on the fixed scenario, then triage), T2.1221 (blocked on WDI's two clone adds), T2.1223 (needs WikiMod loaded), T2.1228, T2.1211, T2.1220, T2.1224.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** 30 features: 13 verified done and pruned (3, 4, 6, 7, 8, 9, 10, 12, 14, 21, 27, 28, 29; of these, sections 9, 28 and 29 and every Selected-table row left this pass, the rest had lost their sections earlier), 0 obsolete, 6 awaiting verification (2, 5, 13, 19, 22, 30), 1 failed (1, T2.1219), 5 stale claims corrected (section 29 "the fix is WDI's", T2.1223 "no run with WikiMod loaded", Considered `Api.Raid` "T2.273 pending", Considered ApplyPerk "feature 4 remains", sections 15 and 23 "nothing exists"), 10 open (11, 15, 16, 17, 18, 20, 23, 24, 25, 26). Pack: 12 open prompts before and after (6, 20, 24-33), 6 awaiting, failed 2 -> 1.
- **Disposition:** KEEP IN PLACE - 10 open features of code or measurement work remain (12 open prompts), led by Prompt 6's live-game probes.
- **Pruned:** plan sections 9, 28 and 29; the 13 done Selected rows (one summary line replaces them); Wave 0 answers W0-3, W0-8 and W0-11 and the feature 21 dispatcher note; Open questions 4, 5, 7 and 14 (resolved); risk rows of features 3, 4, 8, 12 and 21; done release-tail bullets (CLAUDE.md, Animals_Schema and localization edits, the new gates, the repointed `CatchUpTickBatching.cs` and `LoadOrchestrator.cs` citations); the status block's 2026-10-02 to 2026-10-06 history paragraphs (this log holds them). Sections 19 and 30 collapsed to pointers. Pack: the feature 29 Failed bullet; Prompt 27's done repoint sentence. Texts: `git show 189f8b99e:Documentation/Plans/CSFFModFramework/MASTER_PLAN.md` and the pack at the same commit. Plan 846 -> 662 lines, pack 194 -> 193.
- **Evidence:** tracker `confirmedItems` holds T2.1221 (`confirmedOn` 2026-10-05T23:11Z, autoplay PASS from a 16:37 report), T2.1223 (2026-10-05T22:29Z, PASS with WikiMod installed), T2.1227 (2026-10-07), T2.1212, T2.1226, T2.273; section 29's closing fix is `0ee583f13` (`Loading/TypedCacheAllDataSync.cs`, `Database.RegisterAllDataStragglers`, gate `Framework-TypedCacheModCards` 22/22), an ancestor of the previous pin `72f04b7eb` that the 2026-10-06 refresh missed. HEAD carries every built file the awaiting items name (10 checked with `git cat-file -e`, none missing). Since the pin, no commit touches framework source (only `9f68b74dc` deleting `worldmap.txt`); the working tree's uncommitted framework edits (`LifecycleTemplateBuilder.cs`, `TrapIntegrator.cs`, `WarpResolver.cs`, `SmeltingRecipeInjector.cs`, `Plugin.cs`, CHANGELOG, new `Patching/BugFixes/PassiveMultiplierCancelFix.cs`) are another session's in-flight playthrough fixes, none a plan feature, not counted. Harness reports read: T2.1219 `...-20261007-074615` (steps 13, 15-19 FAIL, `DutySuppressed` 0), T2.1231 `...-20261006-195053` (no `mtlintforeignuid` loaded; the fixture is absent from the deployed `Master_Testing`, present at HEAD), T2.1232 `...-20261006-195113` (step 5, unanswered `endgame` popup). Citation sweep (Documentation/, tracker, `Playthrough_Test/`, `TestHarness/client/scenarios/`, `CSFFModFramework/`, memory) found one live read of a pruned section, HarshWilds pack Prompt 23 -> section 9, repointed by that pack's own audit this sweep; the rest are confirmed rows' scenario sources and two code comments.
- **Verification debt:** T2.1229, T2.1219 (triage), T2.1220, T2.1211, T2.1228, T2.1223's person twin T2.1224, T2.1231, T2.1232.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** 17 open features re-checked (pack: 12 open prompts, 7 awaiting entries, 1 failed entry, pinned at `189f8b99e`). 0 verified done, 0 obsolete. 7 built and awaiting verification (features 1, 2, 5, 13, 19, 22, 30): T2.1219 and T2.1220 harness PASS 2026-10-09, T2.1229 PASS x4, T2.1231 PASS x2, T2.1232 PASS 2026-10-07 and 2026-10-09, none recorded (the six-rig audit held them PARTIAL or a skeptic rejected a clause); T2.1211 harness FAIL 2026-10-09 on step 21 alone (untriaged), T2.1228 FAIL on a scenario defect (the timed "East" hidden by stats), T2.1224 run without WikiMod installed. 0 failed (feature 1 left the Failed list: T2.1219's 2026-10-07 FAIL was its scenario's bandit move). 10 open (features 11, 15, 16, 17, 18, 20, 23, 24, 25, 26) plus the release tail. 1 stale claim corrected (section 1's "harness leg failed" line).
- **Disposition:** KEEP IN PLACE (12 open prompts; the first, Prompt 6, has no deps and needs the live-game lane; without it, the scenario fixes for T2.1229, T2.1231 and T2.1228 and the T2.1211 triage are the no-game work).
- **Pruned:** nothing deleted from either file. Pack: the Failed list removed (Prompt 18 moved to Awaiting verification) and every Awaiting entry rewritten to its 2026-10-09 state; header refreshed against `51641ef64`. Plan: section 1's status line corrected, the Selected rows of features 1, 2 and 13 and the Considered row "Bear raid that could not fire" updated, the 2026-10-07 next-work list replaced by a 2026-10-09 one, and an Audit 2026-10-09 paragraph added.
- **Evidence:** `git log 189f8b99e..HEAD -- CSFFModFramework/` returns 3 commits: `56ef680ed` (the last sweep), `e57d44cfc` (five fixes outside this plan's features: WarpResolver enum fields, smelting order, PassiveMultiplierCancelFix, animal food menu, carcass once) and `aa60ef1b0` (a `bin/Release` DLL); control: the same command over `TestHarness/ MasterTesting/` returns 12 commits, so the range is live. Every built claim's file re-read present (`Api/StatAccess.cs` `ResolveNpc` at line 259, `WikiModEnglishGuard.cs`, `AuthoringLint.cs` `ForeignUidRule` at 364, `PluginDependencyReader.cs`, `StripAllContaminationCheck.cs`, `WorldMapInjector.ReadSavedBoardUids` at 2131, `AgentEncounterGuardPatch.cs`, `FrameTimingDiagnostics.cs`, `CatchUpEndStateDiagnostics.cs`); `ActionContext.Produced`, `CompressModSprites`, `BearRaidEscalationDays`, `OffEnvNpcAiThrottle`, `OffEnvCardSkip` and `CatchUpTailTicks` grep to 0 in `CSFFModFramework/**/*.cs` (control: `BatchSizeOverride` and `HasCardEquipped` hit in the same grep). No MasterTesting JSON names the Waterskin `d9c9a691...`, so Prompt 6's fixture is unwritten. Tracker: all 9 of this plan's open rows read `pending` in `items`; `confirmedItems` gained none of them in `aa60ef1b0` or `51641ef64` (both commit messages list their ids). Harness reports read under `TestHarness/client/reports/` (t2-1219, -1220, -1211, -1224, -1228, -1229, -1231, -1232, -456, latest runs 2026-10-09) and the audit `autoplay-audit-20261009-083746.json`; skeptic reasons from `.claude/handoffs/playthrough-six-rig-run-2026-10-09.md`. Deployed framework DLL, `bin/Release` and rig 2 share md5 `88d31771a048c29fbb582522bd138cd2` (built 2026-10-08 17:09, after the last source commit). `git worktree list`: the main checkout only.
- **Verification debt:** T2.1219, T2.1220 (feature 1); T2.1211, T2.1316 (feature 2); T2.1229 (feature 5); T2.1224 (feature 13); T2.1231 (feature 19); T2.1228 (feature 22); T2.1232 (section 30); T2.456 (the shipped bear raid fix feature 20 builds on, harness FAIL 2026-10-09, untriaged).

### 2026-10-09 (later) - MASTER_PLAN, second /cleanup-plans pass
- **Verdict:** NOT drained. 12 open prompts (6, 20, 24-33), 9 awaiting verification, 0 failed. Sections 31 (shared metal parts) and 32 (cross-mod quiet misses and lint precision, all six items) are built and collapsed to awaiting-verification pointers. Still open: features 11, 15, 23 (measurement runs), 16, 17, 18, 20, 24, 25, 26 and Wave 0 W0-4, W0-9, W0-10.
- **Disposition:** KEEP IN PLACE - code work remains.
- **Pruned:** W0-15 row (answered, Prompt 34 done); sections 31 and 32 prose to pointers; open question 14 reduced to a Resolved line (cited by Documentation/Research/CSFFModFramework/shared_metal_parts.md); T2.1211 "untriaged" corrected to a scenario timing fault. Pack prompt ids 34-36 were reused on 2026-10-09; the header now states the next free id is 38.
- **Evidence:** audited against 1d9cbf121. Framework commits since 51641ef64: 5adabf1ab, 49f5736e0, 81c4f1b0a, 78aa8028d. CardData/MetalParts/ holds 3 cards and 7 blueprints; JsonDataLoader.FindHostedUidConflicts and Mods-UniqueIdOnePerMod.Tests.ps1 exist; section 32 pins in Framework-InjectorSkipUnresolved.Tests.ps1 and Framework-AuthoringLint.Tests.ps1. Grep finds no Produced/Settled in ActionRouter and no CompressModSprites, BearRaidEscalationDays, CatchUpTailTicks, OffEnvNpcAiThrottle or OffEnvCardSkip (open features unbuilt), while FrameTimingDiagnostics.cs and CatchUpEndStateDiagnostics.cs are found (control). Gates 30/30.
- **Verification debt:** T2.1232, T2.1233-T2.1235 (harness PASS, unrecorded); T2.1220 (harness PASS, FIX-SCENARIO at the 2026-10-09 second review: escaped village-night Duty start encounters, possibly a real FAIL; T2.1219 recorded CONFIRMED the same day); T2.1211, T2.1228, T2.1229, T2.1231 (scenario fixes); T2.1224 (needs a WikiMod rig); T2.1316 (person); T2.1236-T2.1242 (Fleet plan).

### 2026-10-11 - Fleet MASTER_PLAN (cross-mod decoupling), /cleanup-plans Fleet apply
- Scope: Fleet (cross-mod plan; the same entry is recorded in the ROADMAP of every mod it names: Community_Mod_Chest, AdvancedCopperTools, WaterDrivenInfrastructure, HomesteadPerks, HerbsAndFungi, Sirus23_Mod_Collection, CSFFModFramework).
- **Verdict:** DRAINED. Features 1-13 all built (1-3, 6, 7, 9, 12 in the commits `33ab18fe9`, `a3f965d43`, `76aac3d8a`, `cc0115b9e`, `fed7f7d0a`, `c6b2bad35`, `31034e212`, `e7b558ead`, `7219cd11d`), 0 open pack prompts, 0 unbuilt rows; every acceptance row recorded CONFIRMED (T2.789, T2.632, T2.997, T2.999, T2.998, T2.549, T2.557, T2.560, T2.1236 to T2.1242). Open question 4 settled by measurement, not by an owner choice.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Fleet_Cross_Mod_Decoupling_As_Built.md` with its Considered table and its drained prompt pack appended; the pack was deleted.
- **Pruned:** plan sections 1, 2, 3, 6, 7, 9 and 12 (each BUILT and confirmed), the stale "awaiting verification" pointers, open question 4's default text (replaced by its measured outcome); pack Prompts 9 and 10 (Prompt 10 had already been built in `e7b558ead`, the pack was stale on that point).
- **Evidence:** on dev lane rig 6 at HEAD (H&F, Harsh Wilds and Sirus23 deployed from the working tree, the rest synced from the real install), harness PASS for T2.557, T2.560, T2.632, T2.997 (both variants), T2.999 and T2.1236 to T2.1242; two independent skeptic reviews of all twelve rows (`.claude/handoffs/skeptic-review-2026-10-11-fleet/r-a.md`, `r-b.md`): 11 RECORD-PASS, and T2.560 FIX-SCENARIO (its brine leg dragged the non-draggable Liquid sub-card, so it passed on any build; fixed in the scenario and rerun PASS with a real refusal, "Dropping 'Pickle Brine' onto 'Wolf Companion' does nothing", beside the same container type with water running Give Water once). Three of the seven absent-partner pins had drifted since 2026-10-09 and were re-measured from real alone-boots (CMC 48 entries, five sprites now resolve; H&F plus twelve of its own map-node forward references; Harsh Wilds plus the Forest Lore `fl_mud_volcano` weight row; WDI now 0 lines), each entry checked against `Documentation/Interop_Registry.md`. End state: `grep BepInDependency(` over every `Plugin.cs` finds 13 files and none names anything but CSFFModFramework (the 13 matches are the control that the scan can hit). Open question 4 probe: with Sirus23 set aside the Wool-Quilted Cap and Vest each keep four requirement slots and the Wool one is null (measured), and a researched blueprint is left alone by `AcademyCourseService.HideWoolQuiltedIfNoWool` (read from code).
- **Verification debt:** none open. One unplayed fact, deliberately without a row: what a craft of an already-researched Wool-Quilted recipe asks for after Sirus23 is removed (the game reads a null slot as no requirement, the fleet audit's reading of the code); CMC's README states it as unplayed. Releases still owed per mod under its own version rules (`.claude/nexus-publish-status.json` against `ModInfo.json`, read 2026-10-11): framework 2.28.0 (Nexus holds 2.26.12), CMC 1.69.6 (1.68.45), ACT 1.18.1 (1.16.10), WDI 1.12.2 (1.11.3), HSP 1.3.1 (1.2.4), H&F 1.14.1 (1.13.7) and Sirus23 1.22.0 (1.21.6); MUM not repacked for this change.

### 2026-10-12 - MASTER_PLAN (round 2), closed by pack Prompt 33 (/cleanup-plans CSFFModFramework apply)
- **Verdict:** DRAINED. Every feature in sections 1-32 is built or in "Considered, not selected" (16, 24, 25 and 26 measured out); 0 open code prompts, 0 unbuilt rows. Closed this session: T2.1211's owner question answered from the data (the fight popup's title strip is `StartingOptions.EventTitle`, which generated encounters left empty) and fixed in `7e291aa57`; feature 17's owner question answered by measurement in `7fd06e847` and the owner kept the option as shipped (default off); T2.1224 run with the owner's WikiMod 3.5.4.
- **Disposition:** ARCHIVE - moved to `Documentation/Design/CSFFModFramework_Content_Update_Round2_As_Built.md` with the drained pack appended; the pack was deleted. The plan's Scope note (move the mod to `Stable/`) is superseded: the owner chose on 2026-10-12 to keep CSFFModFramework at the repo root.
- **Pruned:** nothing further from the plan body (earlier passes had already collapsed every built section); added a CLOSED paragraph at its top, a DONE line on section 17, a "Feature 17 follow-up" block in section 15 Measurements, and a final-state list at the top of the appended pack.
- **Evidence:** harness on dev rig 8 (framework at HEAD, owner's third-party mods installed): T2.1211 PASS (batch `walkthrough-20261012-025356`, AI judge PASS 0.85; step 21 reads `StartEventPopup.TitleText` 你看到了猫头鹰！ and step 22 finds it on screen, screenshot read), T2.1224 PASS 50/50 with no allowed failures (WikiMod control and guard line both held), T2.1249 the 2026-10-11 rerun; `autoplay.py audit` classes all three FULL. Feature 17 split from five measurement runs (reports `research-feature-17-tick-part-split-key-off-w0-7-save-20261012-*`): about 310 of 385 ms per off-location agent tick is vanilla agent actions, 25 ms AI selection. Release tail: 11 of 11 framework-linking projects rebuild with 0 errors and 0 warnings against the new DLL, each resolving a refreshed `lib/` copy (`dotnet build -v:d` "Resolved file path"). Gate: 184 of 185 test files, 6,183 passed, 56 failed plus one errored file, none in a framework or plan file (Skill Speed Boost 40, Mod Update Manager docs and the stale WDI embed 6, H&F renamed CT2 pins 2, a missing .NET 9 SDK 2, a new CMC zero-slot mini-inventory, WDI gear piling, two peer scenarios, an unmapped new skill, and `Harness-Feed` broken by `walkthrough.py` now calling `/popups`); `CMC-Round2.Tests.ps1` (872 tests) not run, its author ran it green at `0639bc894`. New gate `Framework-AnimalEncounterEventTitle.Tests.ps1` 4/4, watched red on the pre-fix shape.
- **Verification debt:** T2.1211, T2.1224, T2.1249 (FULL, recorded by `autoplay.py record` once peers' uncommitted tracker and checklist edits land); T2.1316, T2.1245, T2.1247 (a person's look); T2.456 (the bear raid fix in the Considered table); the cited cross-plan rows T2.467, T2.482 (Harsh Wilds) and T2.543 (Sirus23). Release work owed: framework deploy to the real install, `/package-mod-suite`, Mod_Update_Manager deploy (`.claude/handoffs/csff-framework-plan-closeout-2026-10-12.md`).

### 2026-10-28 - Fleet MASTER_PLAN (shared resources and cross-mod tooling, Chiwei / APE), /audit-plan
- Scope: Fleet; recorded here because the framework hosts every surface the plan builds.
- **Verdict:** NOT drained. Section 1 (the 10-21 thread) is built and re-verified; Wave 0 (W0.1 to W0.10), sections 4.1 to 4.3, 5.1 to 5.7, 6.1 to 6.7 and the section 8 reply draft are all OPEN. Nothing FAILED or OBSOLETE.
- **Disposition:** KEEP IN PLACE - code work remains. Next: `/plan-to-prompts Documentation/Plans/Fleet/MASTER_PLAN.md`, then Wave 0 in parallel. Mission `csff-plans-prune-2026-10-28`.
- **Pruned:** section 1's six-row table collapsed to one as-built paragraph (full table at `147c11532`). A dated status paragraph appended to the plan.
- **Evidence:** at `efe084587`: `git grep -l -F` over CSFFModFramework, AdvancedCopperTools, HerbsAndFungi and Community_Mod_Chest (bin and obj excluded) returned 0 files for each new UID, file name and tag the plan introduces (`APE_LQ_LampOil`, `APE_StearinCake`, `APE_LQ_SaltWater`, `APE_LQ_SaltSyrup`, `APE_MetalSheet`, `LiquidConversions`, `MatchAcross`, `tag_x_MetalSheet`, `tag_x_Brine`, `GpTag_FW_Brine`, `GpTag_FW_MetalSheet`) and a word grep for `CardState` in CSFFModFramework returned nothing, while the controls returned 24 files (`GpTag_FW_Oil`) and 2 (`LoadFrameFolders`). Section 1 read from code: `TagGroups.json` tag_Oil groups, ACT `SimpEn.csv` Pour Oil from Container rows, `Interop_Registry.md` R16-R18 and C7, `Loading/UidAliasService.cs`, the armor-merge As_Built. Tracker: T2.1990, T2.1994 and T2.2007 pending; T2.1991 to T2.1993 and T2.2000 to T2.2006 and T2.2008 in `confirmedItems`.
- **Verification debt:** T2.1990 (PNG frames), T2.1994 (any-oil recipes and liquid refuel), T2.2007 (armor merge look and old save), all person rows.
