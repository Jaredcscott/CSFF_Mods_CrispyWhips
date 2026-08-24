# Roadmap: RepeatAction
Version at time of writing: 2.0.0
Date: 2026-07-27
Audit score: 10/10 — release-ready (consolidated 2026-07-27)

## Current State

**Theme**: A standalone quality-of-life utility that repeats your last player-initiated action (Forage, Chop, Travel, Eat, drag-drop crafting, stack actions, …) N times with a single keypress, stopping cleanly the moment the game's own requirement checks stop passing.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images — pure C# utility (2 source files: `Plugin.cs` + `Patcher/ActionPatch.cs`, 743 lines total).

**Stability**: 10/10 — 0 CRITICAL, 0 DESIGN GAP, 0 open WARNING. The 2.0.0 native-dispatch rewrite is soundly engineered: capture via non-mutating, try/catch-wrapped prefixes on the game's four public dispatch funnels; timeScale-safe coroutine (`yield return null` + `unscaledDeltaTime`, no `WaitForSeconds`); re-entrancy-guarded with `isRepeating` reset in `finally`; reflection-free (compile-time `typeof(GameManager)`). Build passes 0/0. Critical-analysis (2026-07-23) verdict: SOLID.

**Open work**: None. No 🔴 Open or 🟡 Pending retrospectives for RepeatAction (only 🔵 Graduated `foraging-RA`, 2026-05-17).

**Framework compliance**: N/A by design — RepeatAction 2.0.0 is intentionally **standalone** (no `[BepInDependency]`, no CSFFModFramework reference). It drives the game's own public `GameManager` funnels directly rather than through framework Tier 2 services. This is correct for its architecture, not a compliance gap. (Reusing the framework's `Api.ActionRouter` is a *future* option — see Phase 3 — not a current requirement.)

---

## Phase 0: Stabilize  *(skipped — audit score 10/10, no open retrospectives)*

Nothing to stabilize. Two maintenance-only cleanups are captured in Phase 1 below.

---

## Phase 1: Foundation

> Housekeeping only — the mod is already healthy. All Quick.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Retire/regenerate obsolete `.audit/fix-plan.md` (2026-06-01; targets `IsPermittedAction`/`ActionRoutine_Prefix` line 2908 that no longer exist post-rewrite) | Audit hygiene | P1 | Quick |
| README "Not Supported" table completeness — add the structural non-`DismantleCardAction` one-off exclusion (dialog answers, discard, finish-game) alongside "Continue" | Docs honesty | P2 | Quick |
| Keep versions synced on next bump (currently 2.0.0 across ModInfo/Plugin.cs/README ✓) via `Update-ModVersion.ps1` | Version hygiene | P1 | Quick |

*Localization note:* the mod is card-less, so there is no `SimpEn.csv`/`SimpCn.csv` — the only player-facing strings are the OnGUI run notifications (English, hardcoded). Externalizing those is optional and low-value for a QoL utility; not scheduled.

---

## Phase 2: Core Expansion

> The highest-value QoL additions from `Documentation/Ideas/RepeatAction/IDEAS.md`, all buildable on the shipped 2.0.0 base.

### Stop-reason taxonomy from `ActionBlockedMessage`
**What**: Thread the game's own `ActionBlockedMessage` string verbatim into the stop notification ("stopped: *Not enough Stamina*", "stopped: *Container is full*").
**Why**: The single biggest honesty win the v2 rewrite enables — the availability check already computes this string; surfacing it directly answers the historical "many actions do not actually repeat" complaint by making every stop legible.
**Requires**: none (the funnel dispatch already calls the check).
**Complexity**: Quick.

### "Repeat until cancel" ∞ sentinel count
**What**: `Shift+Minus` below 1 wraps to an unbounded "∞ / until-stop" mode, guarded by a `MaxUnboundedIterations` cap (default 500).
**Why**: The loop already exits on blockers, thresholds, exhaustion, and cancel — unbounded just means "go until one fires." Removes the need to pre-guess a count for open-ended grinds.
**Requires**: none — pure `Plugin.cs` count-handling + one clamp in `RepeatLastAction`.
**Complexity**: Quick.

### `StopOnInventoryFull` config + check
**What**: New config + `IsInventoryFull()` helper; halt when the player's carry capacity (or active receiving container) has no free slot for the next output.
**Why**: Closes the most common "produced items piling on the ground" complaint; more universal than an arbitrary target-container fill stop because it targets the player.
**Requires**: none — hooks the existing per-iteration validation.
**Complexity**: Medium.

### User-editable blocklist config
**What**: Expose the hardcoded `BlockedActionKeywords` ("continue") as a comma-separated BepInEx string, merged with the built-in default.
**Why**: Lets a player suppress a misbehaving mod action (or any action they never want to batch) without a code change or rebuild.
**Requires**: none — follows the existing 14-entry config-binding pattern.
**Complexity**: Quick.

---

## Phase 3: Integration & Depth

> Cross-mod verification passes and a framework promotion — RA consumes other mods' repetitive station/companion actions.

### Cross-mod repeat verification sweep
**What**: Confirm RA cleanly repeats WDI sawmill "Cut" + water-driven workshop DAs, ACT Grind-All + forge recipes, H&F drying-rack + oil-press cycles, and Sirus23 shear/feed/dairy DAs (v2 UID re-find is the robust path the old button-index replay couldn't handle on companion cards). Also verify SkillSpeedBoost's shortened durations don't misfire RA's completion timeout.
**Why**: These are exactly the repetitive operations RA exists to automate; a blocklist-based mod should already capture them, but each needs a confirmed clean stop (regrow gates, one-shot kit-places, sped-up ticks).
**Requires**: coordination with WDI / ACT / H&F / Sirus23 / SSB current versions.
**Complexity**: Medium (mostly verification, minimal code).

### Promote native-dispatch replay into `Api.ActionRouter`
**What**: Lift RA's capture-record + funnel-dispatch + completion-await core into a reusable framework Tier 2 "replay last action" service; RA becomes a thin consumer.
**Why**: RA v2 is the first mod to drive the four public `GameManager` funnels for *replay* (memory `reference_vanilla_action_dispatch_funnel`). Other QoL/automation mods could reuse it instead of re-deriving the funnel signatures.
**Requires**: v2 stability confirmed in-game; coordination with `CSFFModFramework/CLAUDE.md` scope rules. Note this would re-introduce a framework dependency — weigh against the deliberate 2.0.0 standalone stance.
**Complexity**: Complex.

---

## Phase 4: Polish

> No card art applies (0 cards). Polish here is display/UX and input surface.

| Item | What | Complexity |
|------|------|------------|
| Completion summary notification | On natural completion, tally what stopped the run ("Chop ×12 done — stopped: tree depleted") using the existing stop-reason strings | Quick |
| Per-funnel capability display in the count toast | Name the captured funnel (Action / Stack / CardOnCard / GroupInventory), e.g. "Repeat drag-drop ×5" | Quick |
| Mouse-wheel count adjustment | Hold modifier + scroll to raise/lower `CurrentRepeatCount` (clamped) — one `Input.mouseScrollDelta` check in `Update()` | Quick |
| Persistent repeat-count across sessions | Write last-used count back to BepInEx config so the preferred grind count survives restarts | Medium |
| Investigate gamepad / Steam Deck bindings | Probe CSFF's own input path before adding legacy-input gamepad bindings | Complex |

---

## Long-term Vision

At v3.0 RepeatAction should be the definitive "do this again" primitive for CSFF: an unbounded-or-counted repeater that reports the game's own reason for every stop, adapts to depleting/migrating targets, and optionally recovers-and-resumes (rest between iterations) rather than only hard-stopping. Its clean, serializable capture record is the natural seed for a small "pinned action" (re-fire a remembered action later) that bridges toward — without fully committing to — the deferred macro/sequence-recording family.

**Potential major additions** (not yet justified — revisit after Phase 3):
- **Sequence recording / macros** — record a set of actions and replay in order, with a saveable named library. The biggest scope jump; only worth it once single-action repeat is fully mature.
- **Rest-and-retry-once on recoverable stat failures (v2-native)** — retry only when `SimpleConditionsCheck` says the block is a recoverable stat (Stamina), not a hard requirement. A safer, game-truth-driven form of auto-recovery.
- **Sweep matching cards** — when the original target is exhausted but adjacent same-UID cards remain (a row of ready crops / drying racks), optionally roll onto the next and continue the count.

These live in `Documentation/Ideas/RepeatAction/IDEAS.md` (already spec'd across the 2026-06-14 / 07-06 / 07-20 generations).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new Phase 2 feature | Run `/audit-mod RepeatAction` and refresh this roadmap |
| Game version update | Run `/update-mod-version`, re-verify the four `GameManager` dispatch-funnel signatures against the new `Assembly-CSharp`, re-run `/diagnose-log` |
| After any code change | Run `/critical-analysis RepeatAction` to confirm still SOLID |
| After Phase 2 complete | Run `/export-to-repo RepeatAction` and bump version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod RepeatAction         — full health check, updates .audit/
/critical-analysis RepeatAction — adversarial review
/build-mod RepeatAction         — build Release DLL
/deploy-mods RepeatAction       — build + deploy to game
/update-mod-version RepeatAction <ver> — bump version in ModInfo/Plugin.cs/README
/export-to-repo RepeatAction    — push to public repo
```

*(`/repair-items` and `/repair-blueprints` are N/A — this mod ships no card JSON.)*