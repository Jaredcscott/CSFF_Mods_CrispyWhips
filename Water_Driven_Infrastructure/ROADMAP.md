# Roadmap: Water Driven Infrastructure
Version at time of writing: 1.11.0
Date: 2026-09-07
Audit score: 9/10 - PASS, 0 CRITICAL (consolidated 2026-09-05; see `.audit/summary.md`)

## Current State

**Theme**: Late-game, water-powered manufacturing and automation. Build large-scale infrastructure
(sawmill, forge, workshop, grinding mill, ore sluice, fishpond, mill race outlets) near rivers,
powered by water wheels and mill races and fed by WDI's own copper/iron metalworking and fastener
pipeline. For players past the early survival tier who want bulk processing. **Fully standalone since
1.8.0** - AdvancedCopperTools is a soft, optional enhancement and never a gate.

**Content**: 24 items / 25 blueprints / 11 CT2 structures + 4 CT10 improvements / 7 perks /
5 NPCDuty files / 30 custom images (all references resolve). 0 SelfTriggeredActions, 0 spawn Triggers,
0 Liquid cards.

**Stability**: **9/10 - release ready.** 0 CRITICAL, 0 DESIGN GAP, 6 WARNING, 7 MINOR. Critical
Analysis verdict: **SOLID** (0 critical / 0 mechanical / 0 design / 0 broken promises). Acquisition
coverage fully closed (112 produced / 55 consumed, 0 unreachable, 0 dead-end). Build clean (0 errors /
0 warnings), versions synced at 1.10.19 across ModInfo.json / Plugin.cs / README.md, English
localization complete (521 CSV / 405 JSON keys, 0 missing), Chinese parity CLEAN at 417/417.

**Resolved since the last roadmap** (re-verified against source 2026-09-05, do not re-open):
- The framework-level `CompatibleNPCDuties[].TargetWarpData` GUID-resolution gap flagged as CRITICAL
  in `structures-report.md` (2026-08-14) - fixed in `CSFFModFramework/Data/WarpResolver.cs:792-806`
  (base-type `GameRegistry.GetByUid` fallback naming `NPCDutyOrDutyTagRef.Target`), shipped framework
  2.22.5, framework now at 2.25.24.
- The `perks-report.md` DESIGN GAP (`Features.json` listed 3 of 7 perk UIDs) - all 7 now listed.
- The "Cast Iron Sheet has no WDI-solo consumer" warning - closed by `Bp_IronRivetsFromSheet.json`
  (v1.10.14), which shears one sheet into 8 Iron Rivets with no forge and no other mod.

**Open work**: no 🔴 Open or 🟡 Pending retrospective references this mod (all 4 WDI retros are
archived as graduated). The real open work is **verification, not repair**: 4 of the 5 Partner station
"operate" duties have never been observed in-game, and 5 further playthrough items are pending
(T2.74, T2.113, T2.115, T2.116, T2.131, T2.132). One external risk is tracked but not WDI-owned: the
open framework `ActionRouter` CardOnCardAction index defect (T1.56) rides WDI's card-unbounded
`MillRaceGate` handler.

**Framework compliance**: **Tier 2, fully adopted.** `Api.ActionRouter` (19 references - all action
interception; WDI no longer patches `ActionRoutine`/`PerformStackActionRoutine` directly),
`Api.SpawnService` (10), `Api.TickEvents` (4), `Api.WorldMap` (4, dynamic mill-race node append),
`Api.BlueprintAlternates` (8 pairs, the ACT decoupling). Zero deprecated patterns: no
`DropCollectionGuardPatch`, no `InitializeNullFields`, no `ModLoaderVerison`/`ModEditorVersion`, no
manual perk or blueprint injection. Plugin.cs uses per-class `ApplyPatch(harmony)` (not `PatchAll`)
and emits exactly one Info line at startup.

---

## Phase 0: Stabilize

> Score is 9/10 with 0 CRITICAL and no open retrospectives, so a full Phase 0 is not warranted. Two
> items nonetheless gate a clean public release.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **Regenerate `lib/Assembly-CSharp-nstrip.dll`** against EA 0.67h. Current copy is dated 2026-04-30 - months stale. A stale publicized DLL compiles clean and throws `MissingMethodException` at runtime on any direct typed call into a changed game signature. **Cannot be fixed by copy** - requires the operator's external NStrip tool, then a full rebuild. Fleet-wide gap (7 of 9 content mods), tracked as playthrough T1.71. | Runtime-break risk | P0 | Operator task (blocked on external tool) |
| **Close the honesty caveat on v1.10.16-v1.10.18** - those CHANGELOG/README entries read as done, without the "not yet confirmed in-game" caveat their unverified siblings carry. `wdi_selfsmelt_quality_chain` (T2.131) is still `pending`. Either add the caveat or run the verification pass (melt a copper gear or iron part in the Forge/Workshop, confirm output nugget quality >= source quality, floored at 50%). | Docs honesty | P0 | Quick |

---

## Phase 1: Foundation

> Table stakes. Version, localization and build hygiene are already clean, so this phase is short.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| **Verify the 4 pending Partner station duties in-game** (Ore Sluice, Sawmill, Forge, Workshop). 1 of 5 is confirmed (T2.80, PASS 2026-08-15, covering M0 firetending + the M2 Grinding Mill). This is the mod's single largest confidence hole and it blocks any further duty work. | Verification | P1 | Medium (needs a play session) |
| **Re-check the unresolved "no reachable path" observation** from the 1.10.13 diagnostic session - four of five stations reported no reachable path for the Partner, never re-tested. It may simply reflect which structures existed in that save. If it reproduces, open `/failure-digest wdi-duty-selection-weight`. | Verification | P1 | Medium |
| **Validate `StationDutyBaseWeight = 850`** (`Patcher/MillDutyPatch.cs:77`). It is an unvalidated tuning constant introduced to fix duty-selection starvation; nothing has confirmed it does not now starve vanilla survival duties. | Tuning | P1 | Quick (rides the same play session) |
| **[F1] Cache the per-click reflection lookups** in `ResolveGrindResult` (`ActionInterceptPatch.cs:746`) and `GetHammerHitInfo` (`:1923`) behind a `Dictionary<Type, FieldInfo>`, mirroring `FishpondPopulationPatch._cardModelCache`. Perf hygiene, carried since 2026-08-16. | Framework hygiene | P2 | Quick |
| **Replace the 2 em-dash characters in `ModInfo.json` Description** with hyphens or colons (fleet style rule; player-facing field). | Style | P2 | Quick |
| **Re-run `/audit-blueprints WaterDrivenInfrastructure`** - the existing report is dated 2026-05-20 and scanned only 16 of the now-25 blueprints. | Audit coverage | P2 | Quick |
| **Comment the `tag_SmeltsAt1100` naming discrepancy** (copper gears self-smelt at >=900C; the tag name is accurate only for the Forge "Smelt High-Heat Ore" gate). **Do NOT rename the tag** - it is save-visible data. | Clarity | P3 | Quick |

---

## Phase 2: Core Expansion

> Two of the three shipped in 1.11.0 (Fish Funnel, Ore Sluice tailings) and were removed from
> this phase on 2026-09-07; see the Plan Reconciliation Log at the end of this file.
> One remains, re-verified as NOT shipped.

### Fish Drying Rack
**What**: A river-side drying rack turning raw fish into dried fish, using the `FuelCapacity`/
`Wetness` drying mechanism (memory `reference_tendon_drying_fuelcapacity`).
**Why**: Completes the fishing subsystem - pond and funnel produce raw fish with no preservation
path, the classic food-without-preservation gap. With ice fishing already shipped on the winter pond,
WDI now produces fish year-round and has nowhere to put them.
**Requires**: confirm no sibling mod already claims fish preservation.
**Complexity**: Medium

---

## Phase 3: Integration & Depth

### Water Wheel power model (unblocks three downstream features)
**What**: Decide whether to introduce an explicit load model (one wheel powers N stations, the N+1th
needs a second wheel) or keep the current any-adjacent-race-works model. Needs a per-tick C#
dispatcher either way.
**Why**: This one decision is the bottleneck for the **Control Valve** (on/off toggle without
dismantling), the **Overshot Wheel** (higher-throughput tier: shorter Cut/Hammer/Smelt timers or
higher Blast gain), and the ACT bronze gear/bearing tier. The README's chain already implies one
wheel feeds many machines, while every station in fact gates on mill-race adjacency alone - so the
mod's own documentation is ahead of its mechanics here.
**Requires**: a design decision from the user before any code.
**Complexity**: Complex

### AdvancedCopperTools - bronze gear and bearing tier
**What**: A bronze tier gated on ACT billets (SD4 metal types Tin=120, TinBronze=130, WhiteBronze=140),
feeding a WDI Overshot Wheel upgrade.
**Why**: The fastener/sheet interop is already live and proven in both directions
(`Api.BlueprintAlternates`, 8 pairs in `GameLoadPatch.cs:117-136`). A materials tier is the natural
next rung, and WDI already hard-references ACT's SD4 scheme.
**Requires**: the power-model decision above.
**Complexity**: Medium

### Community_Mod_Chest - iron fishing-rod fittings
**What**: A WDI Workshop "Forge Iron Fittings" recipe producing the fittings CMC's fishing rod
consumes.
**Why**: WDI is the repo's only iron-forging station, and CMC's rod currently has no in-repo source
for its metal parts. Tightest current cross-mod pairing.
**Requires**: decide the UID owner first (memory `feedback_cross_mod_output_dependency`).
**Complexity**: Medium

### Powered Pond Aeration
**What**: A wheel-driven aerator upgrade (or a "Water Mill nearby" gate) raising the pond's stocking
cap in `FishpondPopulationPatch`.
**Why**: The Fishpond gates on mill-race adjacency only - it never actually consumes the Water Wheel
or Water Mill, so the mod's flagship power source has no fisheries payoff. Distinct from
feeding-for-growth (that speeds growth; this raises the ceiling).
**Requires**: new cap values; decide gate-on-placed-Water-Mill vs. a consumable.
**Complexity**: Medium

### RepeatAction compatibility confirmation
**What**: Confirm WDI's IEnumerator-wrapped Cut / Hammer All / Smelt are RepeatAction-queueable.
**Why**: Verification task, not new content - but WDI is the repo's bulk-processing mod and RA is the
repo's repetition mod, so a silent incompatibility would be felt by exactly the overlapping audience.
Document the result either way.
**Complexity**: Quick

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Structure maintenance / wear loop | Water wheels and mill races never degrade - permanent once built. Add a `UsageDurability` drain plus a "Repair" DismantleAction consuming a few Planks. **Decision needed**: decay rate, and whether a fully-worn wheel stops powering downstream stations or only warns. Note this is structure upkeep, distinct from the ruled-out tool-repair mechanic below. | Complex |
| Fishpond feeding for accelerated growth | A "Feed" DismantleAction consuming bugs or food scraps, boosting the growth rate already driven by `FishpondPopulationPatch`. Decision needed on feed types and acceleration amount. | Medium |
| Sized storage station variants (QoL) | Sawmill (6 slots) and Workshop (14 slots) have fixed inventories. Decision needed: a separate log-yard / ingot-rack adjacent structure, vs. an in-place "Expand" upgrade swapping the station's CardModel for a higher-slot variant (in-place swap only - never `Destroy`). | Medium |
| Clean the T2.80/T2.81 playtest note bodies | Both carry `"status": "pass"` while the note text still reads "Confirm both..." / "never verified". Status is authoritative; the prose is just stale. | Quick |
| GIF animation candidates | The Water Wheel and Mill Race are the mod's identity cards and are currently static. See `Documentation/CSFF_GIF_Authoring.md`; framework ships native GIF support. | Medium |

---

## Long-term Vision

At v2.0, WDI should be the repo's answer to "I have survived; now I want to industrialize." The
infrastructure chain and the metalworking pipeline are both complete and standalone today, so the
remaining growth is in two directions: **power that actually means something** (a load model, wheel
tiers, and stations that consume the wheel rather than merely standing near it), and **automation the
player can trust** (five Partner duties that are confirmed to fire, then extended). The Irrigation
Mill Race Chain is the natural centerpiece: multi-environment plumbing where directional mill-race
improvements carry flow across locations and a terminal race auto-tops `TilledField`/`GardenPlot`
Hydration, turning WDI from a set of stations into a network the player routes.

**Potential major additions** (not yet justified - revisit after Phase 3):
- **Irrigation Mill Race Chain** - fully specced (6 JSON + 16 CSV + a multi-source BFS in
  `IrrigationChainPatch.cs`); blocked on HerbsAndFungi crops for a meaningful flagship input.
- **Water Reservoir** - large-scale CT2 water storage fillable in rainy seasons, an offline buffer for
  outlets when river access is seasonal. Player-requested (Sirus23, Discord, 2026-08-08).
- **Stamp Mill / Ore Crusher** - a crushing step ahead of the Sluice, making ore processing a genuine
  two-stage pipeline rather than one wide-input station.
- **Aquatic plant cultivation** (watercress, reeds) - WDI owns every water structure in the repo and
  no mod claims aquatic flora. Confirm vanilla ships no watercress/reed/cattail item first.

**Explicitly ruled out - do not re-promote without an owner decision:**
- **Water-powered Sharpening Wheel** (restoring `UsageDurability` on blades). Repo convention forbids
  repair mechanics for crafted metal items (memory `feedback_no_repair_mechanics`); the sanctioned
  loop is smelt-and-recraft. Requires an explicit carve-out from the user BEFORE any build.
- **A bare index swap on `Api.ActionRouter`** to fix the T1.56 CardOnCardAction defect. Already
  attempted and REVERTED - it double-fires every receiver-keyed drag handler across 6 mods. The fix is
  framework-owned and needs its own plan; do not attempt a WDI-local workaround.

These live in `Documentation/Ideas/WaterDrivenInfrastructure/IDEAS.md`. The promoted subset used to
live in `Documentation/Plans/WaterDrivenInfrastructure/Audit_Remediation_Plan.md`; that plan drained on
2026-09-07 and is archived at `Documentation/Design/WDI_Audit_Remediation_As_Built.md`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod WaterDrivenInfrastructure` and update this roadmap |
| **Game version update** | Refresh `lib/Assembly-CSharp.dll` AND regenerate `lib/Assembly-CSharp-nstrip.dll` with NStrip, then rebuild. WDI carries its own nstrip copy and makes direct typed calls - a stale one is a silent `MissingMethodException`. Then `/update-mod-version`, `/diagnose-log`. |
| After fixing a critical issue | Run `/critical-analysis WaterDrivenInfrastructure` to verify the fix |
| After any duty or `ActionRouter` change | Re-run the Partner duty playthrough items - this is the mod's least-verified subsystem |
| After Phase 2 complete | Run `/export-to-repo WaterDrivenInfrastructure` and bump the minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod WaterDrivenInfrastructure         - full health check, updates .audit/
/critical-analysis WaterDrivenInfrastructure - adversarial review
/audit-blueprints WaterDrivenInfrastructure  - close the 16-of-25 coverage gap (Phase 1)
/repair-items WaterDrivenInfrastructure      - auto-fix item JSON issues
/build-mod WaterDrivenInfrastructure         - build Release DLL
/deploy-mods WaterDrivenInfrastructure       - build + deploy to game
/update-mod-version WaterDrivenInfrastructure <ver> - bump version in all 3 files
/export-to-repo WaterDrivenInfrastructure    - push to public repo
/playthrough-test-plan record                - record the pending duty/quality verifications
```

---

## Plan Reconciliation Log

> Append-only. `/roadmap` overwrites this file wholesale but re-appends this section verbatim
> as the last H2. Never reword an entry, never reorder them.

### 2026-09-07 - Audit_Remediation_Plan drained and archived (shipped as WDI 1.11.0)

**Verdict:** 2 of 2 remaining build rows shipped; 0 rows left open, so the plan is Done. Over its
life the plan carried 4 feature rows and 0 Fix rows (0 CRITICAL / 0 Design Gap throughout, at
9/10). Final state of each: **N1 Fish Funnel** built and shipped in 1.11.0; **N4 Ore Sluice
tailings** built and shipped in 1.11.0; **N3 Cast Iron Sheet sink** already shipped in 1.10.14
and now carrying a recorded in-game FAIL (T2.113) that static re-verification could not
reproduce; **N2 Fish Drying Rack** dropped 2026-08-16 by user decision (vanilla `DryingRack` +
`Smokehouse` already cover the chain). The two open Warnings (W1 em-dashes in the `ModInfo.json`
Description, W2 missing in-game caveats on the README changelog) had been fixed in an earlier
2026-09-07 pass and left uncommitted; the 1.11.0 commit carries them.

**Disposition:** ARCHIVE. Plan and pack both moved verbatim into
`Documentation/Design/WDI_Audit_Remediation_As_Built.md` (the pack as an appendix, because its
Prompt 1 CONFIRMED BUILD DIRECTIVE is the only record of the decompile research behind the
shipped cards, and its Wave-0 block holds the vanilla `DryingRack`/`Smokehouse` GUIDs the N2
research produced). `Documentation/Plans/WaterDrivenInfrastructure/` is now empty and removed.
3 acceptance rows filed before anything was deleted: **T2.198** (funnel builds and places),
**T2.199** (funnel actually doubles a Funnel Trap's fill rate), **T2.200** (sluice tailings).
Nothing was pruned on a classification alone - every row below was re-read from source.

**Evidence:** `dotnet build -c Release` 0 warnings / 0 errors;
`Development_Tools/Deploy-Mods.ps1 -WaterDrivenInfrastructure` reports CardData files 61 -> 64.
Card census over `CardData/**/*.json` after the change: CT0 24, CT2 11, CT7 25, CT10 4.
`BlueprintTabs.json` Advanced Tools 5 -> 6 entries, 25 total across all tabs, no duplicate UID.
`Localization/SimpEn.csv` and `SimpCn.csv` both 611 -> 626 rows (15 keys, appended in binary
mode with a byte-prefix assertion, so no existing row was rewritten). A purpose-built checker
verified 32 `*WarpData`/`*WarpType` pairs across the 3 new cards and was watched going RED on 8
separately planted defects (missing WarpType, CT10 instead of CT2, inactive Special1 modifier,
(0,0) drop quantity, `BuildingDaytimeCost` 16, an obfuscated vanilla asset name, an empty
`EffectName`, and a DA with no effect) and GREEN again after each was reverted.
N4's honesty gate could NOT be met as literally written and was not faked: a sweep of all 2841
entries in `UniqueIDScriptableGUID/CardData.json` found no Gravel, Sand, Pebble, Rubble or
aggregate card in EA 0.67i, so the tailings route to real vanilla Stone, and the row's premise
was narrowed from "discards its waste silently" to "about 29% of mud piles and 34% of fine dirt
yielded nothing at all" (computed from the per-soil drop chances in `RollSluiceDrops`).
`.claude/playthrough-test-status.json` items 110 -> 113, `confirmedItems` untouched at 229,
conservation-checked line-by-line with a multiset compare against the HEAD blob.

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
