# Roadmap: Mod Update Manager
Version at time of writing: 2.1.13
Date: 2026-07-27
Audit score: 10/10 (release-ready)

## Current State

**Theme**: A standalone BepInEx utility that scans installed CSFF mods, checks them against Nexus Mods for updates, and bundles a one-click "Install & Update" installer for the crispywhips 8-mod suite (framework + ACT + H&F + WDI + CMC + Repeat Action + Quick Transfer + Skill Speed Boost). For players who run modpacks and want update visibility without leaving the game.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom card images. UTILITY mod — 21 source `.cs` files, 8 embedded suite ZIPs, 1 IMGUI dashboard (F3), 1 Harmony postfix.

**Stability**: 10/10 — 0 CRITICAL, 0 DESIGN GAP, 0 WARNING. Two 2026-07-23 warnings (F8-vs-F3 keybind hint; breadcrumb-less catch on the suite-wipe path) were fixed same-day and re-confirmed against current source. The 2.1.11-era embedded-ZIP staleness CRITICAL is resolved (all 8 ZIPs match live source).

**Open work**: None. No entry in `Documentation/Retrospectives/INDEX.md` names Mod_Update_Manager, its GUID (`crispywhips.mod_update_manager`), or the MUM shorthand.

**Framework compliance**: N/A by design. MUM is intentionally standalone — no `[BepInDependency]` on CSFFModFramework/ModCore/ModLoader, and its mod-local `SimpleJson`/`MiniZip`/`ModScanner` are deliberate (zero-dependency goal), NOT framework-service duplication. Tier 2 services (ActionRouter/SpawnService/TickEvents) are not applicable to a network+IMGUI utility that touches the game via a single once-per-load postfix.

---

## Phase 0: Stabilize  *(skipped — audit score 10/10, no open retrospectives)*

Nothing to stabilize. Recommend a preflight guardrail so the one class of bug that has recurred (embedded-ZIP version drift) is caught automatically instead of by manual re-audit:

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Preflight/pre-publish rule: FAIL when any embedded suite ZIP's `ModInfo.json` version < the live sibling mod folder version | Regression guard | P1 | Medium |

---

## Phase 1: Foundation

> Table-stakes hygiene. Most already satisfied — listed for completeness.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Versions synced across ModInfo/Plugin.cs/README (all 2.1.13) | Version hygiene | done | — |
| bin/Release sync clean, build 0-warn | Build hygiene | done | — |
| Name the "Install & Update" suite installer in `ModInfo.json` Description (m1) | Docs honesty | P1 | Quick |
| Drop the `//`-comment header from the mappings file, or move to a sidecar `.txt` (m4) | Standards | P2 | Quick |
| SimpCn.csv Chinese localization | Localization | P3 (low) | Quick |

*Note: SimpEn.csv holds 2 keys; MUM is an IMGUI utility whose UI is not driven by game localization, so Chinese-CSV parity is optional, not table-stakes.*

---

## Phase 2: Core Expansion

> The most impactful near-term additions that extend MUM's core update-tracking loop. All are pre-scoped in `Documentation/Ideas/Mod_Update_Manager/IDEAS.md`.

### Suite-install verification pass
**What**: After `ModSuiteExtractor.Extract`, walk the ZIP entry list and `File.Exists` each destination; mark a row `[Install Incomplete]` when any file is missing instead of reporting success on "no exception thrown."
**Why**: closes the orphan/partial-write class of bug the CLAUDE.md deploy rules warn about; turns silent partial extracts into a visible status.
**Requires**: none.
**Complexity**: Medium.

### Quality-of-life IMGUI additions
**What**: "Copy to clipboard" button for the mod-list export (`GUIUtility.systemCopyBuffer = ExportText(...)`); persist the search filter across F3 close; a manual "re-check rate-limited mods" button that re-runs only HTTP-429'd rows.
**Why**: the export text and search box already exist but require hand-selection / re-typing; the 429 path currently leaves mods unchecked with no cheap recovery.
**Requires**: none (all data already on `ModPreferences` / `InstalledModInfo`).
**Complexity**: Quick each.

### Rate-limit robustness
**What**: single exponential-backoff retry on HTTP 429 inside `NexusApiClient`, distinct from the manual re-check button.
**Why**: a burst of checks can 429 mods that then stay unchecked for the whole session.
**Requires**: none.
**Complexity**: Medium.

---

## Phase 3: Integration & Depth

> Cross-mod hooks and heavier tracking features.

### Auto-seed the known-mod registry from sibling mods
**What**: build-time PowerShell generator (sibling to `Update-ModVersion.ps1`) that reads every crispywhips mod's `ModInfo.json` + `NexusModId` and regenerates `KnownModRegistry`, so a renamed folder or newly published mod never silently drops out of the map. Pair with honoring `"NexusModId"` in each first-party `ModInfo.json`.
**Why**: the registry is hand-maintained today; drift is invisible until a mod stops being tracked.
**Requires**: coordination with `/export-to-repo` so the field ships in published builds.
**Complexity**: Medium.

### Suite health + compatibility surfacing
**What**: (a) bake each first-party mod's `.audit/summary.md` `Overall Score` into a package-time JSON MUM reads, adding a "Health: N/10" chip per first-party row; (b) a tested-version-combo compatibility matrix (`Documentation/compatibility-matrix.json`) flagged when installed combos are untested; (c) stale-framework banner on the All Mods tab when installed framework < newest available (framework must load first, per CLAUDE.md).
**Why**: turns MUM from a pure Nexus tracker into the suite's health dashboard.
**Requires**: Phase 3 registry auto-seed for reliable first-party identification.
**Complexity**: Complex.

### Suite install-order enforcement
**What**: guarantee `CSFF_Mod_Framework` extracts first in the "Apply Updates" batch regardless of selection order.
**Why**: matches the CLAUDE.md `-All`/`-Published` invariant that content mods must never load against stale framework code.
**Requires**: none (`SuiteModRegistry` already knows the framework entry).
**Complexity**: Quick.

---

## Phase 4: Polish

> UX and precision.

| Item | What | Complexity |
|------|------|------------|
| Conflict-detector precision (m3) | Re-key `_knownConflicts` to `BepInPlugin` GUID pairs; demote name-substring guesses to a labeled "heuristic" tier | Medium |
| Sortable mod list | Status / alphabetical / endorsement-desc ordering with persisted choice | Medium |
| Non-intrusive update indicator | Small "N updates" nudge gated to a safe game state (not FadeToBlack) | Medium |
| GameLoad reflection watch (m2) | Revisit compile-time `typeof(GameLoad)` if a game update renames the class; current loud-fail form is acceptable | Quick (watch) |

---

## Long-term Vision

> Where this mod should be at v3.0.

MUM's natural endpoint is the crispywhips suite's control panel: not just "is there a newer version on Nexus" but "is my install internally consistent, correctly ordered, and version-compatible across the whole family." The biggest additions that become natural at that scale are the read-only **mod-profile diff** (snapshot an install as a named set, later diff current-vs-saved) and the **per-mod audit-health + tested-combo dashboard** — both leverage data MUM already sees (installed versions) plus package-time artifacts the repo already produces (`.audit/summary.md` scores). Any path that *drives* the installer to reach a target set (profile apply, self-update staging) is deliberately deferred: it touches file writes / MUM's own locked DLL and needs the same opt-in/confirmation scrutiny CLAUDE.md applies to destructive actions.

**Potential major additions** (not yet justified — revisit after Phase 3):
- Read-only mod profiles (save/diff an enabled-set) — pure comparison, 80% value at 20% risk; state-changing "apply" deferred.
- Self-update staging via `.pending` extract — MUM cannot overwrite its own running DLL; needs a proof-of-concept on a throwaway install first (see `Documentation/Ideas/Mod_Update_Manager/Embedded_Mod_Suite_Integration.md`).
- Nexus "recently updated CSFF mods you don't have" discovery digest — behind the existing `EnableNexusDiscovery` opt-in.

These live in `Documentation/Ideas/Mod_Update_Manager/` (IDEAS.md + Embedded_Mod_Suite_Integration.md).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any suite mod is re-packed | Re-run the embedded-ZIP currency check; confirm all 8 ZIPs match live source before publishing MUM |
| After any new feature phase | Run `/audit-mod Mod_Update_Manager` and refresh this roadmap |
| Game version update | Run `/update-mod-version`, watch `GameLoad`/`LoadMainGameData` for renames (m2), re-run `/diagnose-log` |
| After fixing an issue | Run `/critical-analysis Mod_Update_Manager` to verify |
| Before publish | Run `/export-to-repo Mod_Update_Manager` (bump patch version) |

---

## Skill Cheatsheet for This Mod

```
/audit-mod Mod_Update_Manager           — full health check, updates .audit/
/critical-analysis Mod_Update_Manager   — adversarial review
/build-mod Mod_Update_Manager           — build Release DLL
/deploy-mods Mod_Update_Manager         — build + deploy to game
/package-mod-suite                      — re-pack the 8 embedded suite ZIPs into MUM
/update-mod-version Mod_Update_Manager <ver> — bump version in ModInfo/Plugin.cs/README
/export-to-repo Mod_Update_Manager      — push to public repo
```
