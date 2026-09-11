# Roadmap: RepeatAction
Version at time of writing: 2.1.5
Date: 2026-09-08 (plan-reconciliation update at 2.1.5; supersedes the 2026-09-05 consolidated refresh @ 2.1.0)
Audit score: 10/10 - release-ready (consolidated 2026-09-05; all open warnings closed in v2.1.0; no audit re-run since 2.1.5)

> **2026-09-08 - the second promoted Near-Term tier shipped too, v2.1.1 through v2.1.5, one patch
> release per feature:** `Per-Card Group Repeat` (2.1.1), `Verbose Run Diagnostics` (2.1.2), `Show
> Count Indicator` (2.1.3), `Show Progress Bar` (2.1.4) and `Extra Stat Thresholds` (2.1.5). That
> closes BOTH code items in Phase 3 and three of the four Phase 4 rows; all five are **awaiting
> in-game verification** (T2.216-T2.220). Record: `Documentation/Design/RepeatAction_v2.1.5_As_Built.md`
> and the § Plan Reconciliation Log entry at the end of this file. The v2.1.0 tier's own checks
> T2.158-T2.163 PASSED in playthrough r33; T2.164 and T2.165 were skipped and remain open.

> **2026-09-05 - Phases 1 and 2 are complete.** v2.1.0 shipped every Phase 1 item (the four
> 2026-08-24 audit warnings W1-W4 plus both hygiene items m1/m2) and all four promoted Phase 2
> features. Phase 3 (cross-mod verification sweep + optional framework promotion) is now the head of
> the roadmap, and the whole of v2.1.0 is **awaiting in-game verification** (playthrough items
> T2.158-T2.165) - nothing below has been played yet.

## Current State

**Theme**: A standalone quality-of-life utility that repeats your last player-initiated action (Forage, Chop, Travel, Eat, drag-drop crafting, stack actions, group harvests, ...) N times (or Unlimited) with a single keypress, stopping cleanly the moment the game's own requirement checks stop passing and reporting the game's own reason.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images - a pure C# utility (2 source files: `Plugin.cs` + `Patcher/ActionPatch.cs`).

**Stability**: 10/10 - 0 CRITICAL, 0 DESIGN GAP, **0 open warnings**. The four 2026-08-24 messaging/robustness warnings and both Minor items were all closed in v2.1.0 (see `.audit/summary.md` Section Resolution). The 2.0.0 native-dispatch rewrite is soundly engineered: capture via non-mutating, try/catch-wrapped prefixes on the game's four public dispatch funnels; timeScale-safe coroutine (`yield return null` + `unscaledDeltaTime`, no `WaitForSeconds`); re-entrancy-guarded with `isRepeating` reset in `finally`; all Harmony patch targets resolved at compile time via `typeof(GameManager)`. Build passes 0/0. `lib/Assembly-CSharp.dll` byte-matches the framework canonical, so all direct typed calls bind to the live game.

*One deliberate exception to "reflection-free" as of 2.1.0:* W1 reads `GameManager.CurrentActionBlockers`, which is private, through a single cached `AccessTools.Field` lookup with a `LogDebug` breadcrumb if the field ever disappears. Every other game member the mod touches is compile-time typed.

**Open work**: None release-blocking, and no open audit findings. No open or pending retrospectives for RepeatAction (only a graduated `foraging-RA`, 2026-05-17). The one genuinely open item is **in-game verification of everything v2.1.0 added** - it is all statically verified only. Next feature work is Phase 3.

**Framework compliance**: N/A by design - RepeatAction 2.0.0+ is intentionally **standalone** (no `[BepInDependency]`, no CSFFModFramework reference). It drives the game's own public `GameManager` funnels directly rather than through framework Tier 2 services. This is correct for its architecture, not a compliance gap. Reusing the framework's `Api.ActionRouter` is a *future* option (Phase 3), not a current requirement.

---

## Phase 0: Stabilize  *(skipped - audit score 10/10, no open retrospectives)*

Nothing to stabilize. The four 2026-08-24 warnings were messaging/robustness polish and are all closed in v2.1.0.

---

## Phase 1: Foundation  *(COMPLETE - shipped v2.1.0)*

> Table-stakes health items. All done.

| Item | Type | Priority | Status |
|------|------|----------|--------|
| W1 - surface the game's real `BlockedMessage` on stat/status stops | Honesty fix | P1 | DONE v2.1.0 |
| W2 - drop the misattributed "event popup" clause from the `StopOnLowStats` tooltip + README mirror | Docs honesty | P1 | DONE v2.1.0 |
| W3 - stop pinning a stale game-version label in README (record per-release build in CHANGELOG) | Docs hygiene | P1 | DONE v2.1.0 |
| W4 - explicit `CannotBeTransferred` re-check on liquid-transfer replay | Robustness | P1 | DONE v2.1.0 (defence-in-depth) |
| m1 - retire the obsolete `.audit/fix-plan.md` | Audit hygiene | P2 | DONE v2.1.0 |
| m2 - `LogDebug` breadcrumb on `CheckThreshold` null-stat early-return | Silent-failure breadcrumb | P2 | DONE v2.1.0 |

Version hygiene: ModInfo.json / Plugin.cs / README.md all at 2.1.0. Localization: N/A (0 CSV content by design). Framework Tier 2: N/A by design (standalone).

---

## Phase 2: Core Expansion  *(COMPLETE - shipped v2.1.0)*

> The promoted Near-Term feature tier. All four (plus three supporting items) shipped.

- **`StopOnInventoryFull`** - halts once every carried container is full; ships **default-off** (many actions drop output on the ground, not into inventory) and is a no-op when carrying no container.
- **Unbounded "Unlimited" count** - lowering the count below 1 wraps to unbounded mode, guarded by a `Maximum Unlimited Iterations` backstop (default 500).
- **User-editable blocklist** - the hardcoded single-entry blocklist is now a comma-separated `Extra Blocked Actions` config merged with the built-in default.
- **Mouse-wheel count adjustment** - hold Shift + scroll to raise/lower the repeat count.
- **Per-funnel label in the count toast** - the captured `FunnelKind` is read into the toast text (action / stack / drag-drop / group).
- **Boot-time dispatch-funnel signature log** - Debug on the healthy path, **Error** on an unresolvable funnel, respecting the one-Info-line startup budget; grounded in the EA 0.66bb `CollectActionModifiers` incident.
- **Explicit `CannotBeTransferred` liquid guard** (= W4 above).

**All statically verified only - not yet exercised in-game** (T2.158-T2.165).

---

## Phase 3: Integration & Depth  *(head of the roadmap)*

> Cross-mod verification is mostly a play-test pass, not code. Framework promotion is optional and weighed against the standalone stance.

### Cross-mod repeat-verification sweep
**What**: play-test that RA captures and cleanly stops each repetitive action across the sibling mods - WDI (sawmill "Cut" CI + workshop DAs), ACT (Grind-All + forge recipes), HerbsAndFungi (drying-rack + oil-press cycle), Sirus23 SheepHusbandry (shear/feed/dairy DAs on companion cards), SkillSpeedBoost (sped-up action completion timing must not misfire a timeout stop).
**Why**: RA exists to automate exactly these loops; each needs a confirmed clean stop on its own gate. Structural (non-keyword) capture should already work - this is verification of the v2 UID re-find + native-funnel path, not new code.
**Requires**: v2.1.0 in-game verification (T2.158-T2.165) first.
**Complexity**: Medium (play-test time, minimal code).

### Repeat-whole-group vs. per-card toggle  *(DONE v2.1.1 - awaiting T2.216)*
**What**: a config so a player who wants a hard per-card cap can opt into per-card mode for `PerformGroupInventoryAction` (one RA iteration currently always means one full group sweep).
**Why**: gives fine-grained control over harvest-style sweeps; reads the already-captured `GroupCards` length, no new dispatch.
**Shipped as**: `Per-Card Group Repeat` (default off = unchanged whole-group sweep); one card per iteration through the same funnel, "no more targets" on exhaustion.

### Generic stat-threshold config  *(DONE v2.1.5 - awaiting T2.220)*
**What**: extend the fixed 3-stat thresholds (Stamina/Satiation/Hydration) to an arbitrary GameStat GUID list via comma-separated config.
**Why**: lets a player running a mod that adds a custom vital (temperature, sanity-style) gate a run on it without a code change.
**Shipped as**: `Extra Stat Thresholds`, comma-separated `guid:percent` pairs checked after the fixed three; the stop reason uses the stat's own GameName; malformed entries are skipped with a breadcrumb.

### Optional: promote native-dispatch replay into `Api.ActionRouter`
**What**: lift RA's capture+dispatch+await core into a reusable CSFFModFramework Tier-2 service.
**Why**: RA v2 is the first mod driving the 4 public `GameManager` funnels for replay; other mods could reuse it. **Trade-off**: re-introduces a framework dependency, directly against the deliberate 2.0.0 standalone stance - do NOT pursue unless the standalone constraint is intentionally relaxed.
**Requires**: v2.1.0 stability confirmed in-game.
**Complexity**: Complex.

---

## Phase 4: Polish

> UI/visibility refinements. Small, opt-in, none release-blocking. Three of four shipped 2026-09-08.

| Item | What | Complexity | Status |
|------|------|------------|--------|
| Progress bar | Visual fill bar alongside the existing `{completed}/{count}` text notification | Medium | DONE v2.1.4 (`Show Progress Bar`, default on; sweep in Unlimited mode) - awaiting T2.219 |
| Persistent repeat-count HUD toggle | `Show Count Indicator` config drawing an always-on `OnGUI` count label while a card popup is open, reusing the existing renderer | Quick | DONE v2.1.3 (default off; game's `CurrentInspectionPopup` as the open signal) - awaiting T2.218 |
| Verbose Run Diagnostics | Promote the loop's `LogDebug` abort/stop paths to `LogInfo` only while `isRepeating`, so a player sees why a run stopped without permanent spam | Quick | DONE v2.1.2 (default off) - awaiting T2.217 |
| "Run finished" cue toggle | Optional `PlayCompletionSound` playing a short vanilla UI sound when a long run ends | Quick | Open - the only unbuilt Phase 4 row |

---

## Long-term Vision

> Where RepeatAction should be at v3.0.

The natural endpoint is a small, reliable **action-automation layer** that never surprises the player: it repeats any single action honestly (with the game's own stop reason), scales from a fixed count to Unlimited-with-a-backstop, and is proven to stop cleanly against every repetitive loop the sibling mods add. The biggest scope jump - not yet justified at current scale - is a **sequence recorder / macro system** (record several actions, replay in order, bind to a hotkey, save/load a named library), with a single "pinned action" slot as its stepping-stone. That only becomes worth building once the pinned-action prototype is proven and v2.1.0's native-dispatch engine is confirmed stable in-game.

**Potential major additions** (not yet justified - revisit after Phase 3):
- Sequence recording / macros - the flagship future feature; folds in "plant -> water -> harvest chain" and heterogeneous-action queuing.
- Timed repetition (repeat for N in-game minutes) - needs a new elapsed-time exit condition; design decision on real-time vs. in-game clock.
- Rest-and-retry-once on a recoverable stat-gated block - uses the typed `SimpleConditionsCheck` reason to retry only on a Stamina block, never a hard requirement.

These live in `Documentation/Ideas/RepeatAction/IDEAS.md` (engine/QoL only - RA ships zero content by design).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| v2.1.0 in-game verification done | T2.158-T2.163 recorded PASS (r33); still open: T2.164, T2.165. Then re-run `/critical-analysis RepeatAction` to re-baseline against current code |
| v2.1.1-v2.1.5 in-game verification | Record T2.216-T2.220 via `/playthrough-test-plan record`; if T2.218 reports HUD overlap, move the two constants in `Plugin.DrawCountIndicator`; if T2.219 judges the bar intrusive, flip `Show Progress Bar`'s default |
| Game version update | Refresh `lib/Assembly-CSharp.dll` (byte-copy from framework canonical - non-nstrip, fixable by plain copy), rebuild, watch the boot-time funnel signature log for drift, run `/diagnose-log` |
| After any Phase 3/4 item | Run `/audit-mod RepeatAction` and update this roadmap |
| After a public release | Run `/export-to-repo RepeatAction`; record the build it compiled against in `CHANGELOG.md` |

---

## Skill Cheatsheet for This Mod

```
/audit-mod RepeatAction            - full health check, updates .audit/
/critical-analysis RepeatAction    - adversarial review (re-run after in-game verification)
/code-quality RepeatAction         - C# reliability/maintainability scan
/build-mod RepeatAction            - build Release DLL
/deploy-mods RepeatAction          - build + deploy to game
/playthrough-test-plan record      - advance the v2.1.x in-game verification items
/update-mod-version RepeatAction <ver> - bump version in all 3 files
/export-to-repo RepeatAction       - push to public repo
```

---

## Plan Reconciliation Log

> Written per `/cleanup-plans` Step 4b (this first entry by the session that built the plan, same
> shape). Append-only: never reword, reorder or delete an entry. `/roadmap` Step 7 preserves this
> section verbatim when it overwrites the rest of this file.

### 2026-09-08 - Audit_Remediation_Plan (second Near-Term tier, promoted 2026-09-05)
- **Verdict:** 5 of 5 feature rows built (N1-N5), one patch release each, v2.1.1 through v2.1.5;
  0 rows ruled out. The Fixes section was empty by design (0 promoted; 0 open CRITICAL and 0 open
  Design Gap at promotion). No code work remains.
- **Disposition:** ARCHIVE - moved to `Documentation/Design/RepeatAction_v2.1.5_As_Built.md`,
  together with the acceptance criteria and flagged design decisions of its Implementation Prompts
  pack (never committed; refreshed 2026-09-07 against `f06153209`, 5 open / 0 awaiting verification),
  which was removed with it. `Documentation/Plans/RepeatAction/` is now empty.
- **Pruned:** the whole plan doc (Features table N1-N5 and its Promotion Log, carried into the
  as-built) and the whole 5-prompt pack; nothing was left behind as a stub.
- **Evidence:** commits `c863847c0` (2.1.1, N1 `Per-Card Group Repeat`), `3eb2d9b4b` (2.1.2, N2
  `Verbose Run Diagnostics`), `8dc02553d` (2.1.3, N3 `Show Count Indicator`), `3dbab945d` (2.1.4,
  N4 `Show Progress Bar`), `294a343f6` (2.1.5, N5 `Extra Stat Thresholds`); each step
  `dotnet build -c Release` 0 warnings / 0 errors against the EA 0.67i lib, each bumped via
  `Update-ModVersion.ps1` with the 3 version strings re-validated in sync. `Plugin.cs` now binds 5
  config entries that the 2026-09-05 promotion's Step 3a grep gate had found absent. Inbound
  reference sweep for `Plans/RepeatAction` over the repo (gitignore-aware Grep) and the memory
  store returned only the plan and pack's own self-references, so nothing needed repointing.
  `git status --porcelain -- RepeatAction/` was clean before the first edit and every commit staged
  exactly the seven RepeatAction paths it named.
- **Verification debt:** T2.216, T2.217, T2.218, T2.219, T2.220 present in
  `.claude/playthrough-test-status.json` `items`, all `pending` (five new `ra_2_1_*` slugs, checked
  against `items`, `confirmedItems` and `droppedItems` before insertion; ids were allocated from the
  live ceiling after a reserved range T2.213-T2.217 went stale within minutes). Plan-lifecycle gate 2
  is therefore satisfied. Still open from the previous tier: T2.164 and T2.165 (`skip` in r33).
