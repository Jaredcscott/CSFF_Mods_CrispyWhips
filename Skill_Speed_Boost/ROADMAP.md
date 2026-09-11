# Roadmap: Skill Speed Boost
Version at time of writing: 1.10.1
Date: 2026-09-06
Audit score: 10/10 - PASS (0 CRITICAL, 0 DESIGN GAP, 2 open WARNING, 0 MINOR) after the 2026-09-06
v1.10.1 remediation, which closed ten of the twelve items the 2026-09-05 re-consolidation opened.
That 8/10 (0/0/7/5) was correct for v1.10.0 and is the record of what was fixed; the two survivors
are Phase 0 item 5 below (blocked on an external tool) and a v1.9.7-scoped `feature-map.md`.

## Current State

**Theme**: A pure-C# utility mod that gives players fine-grained control over skill progression in
CSFF - per-skill XP multipliers (0-10x), natural staleness decay (global or per-skill), and a stack
of optional situational bonuses (morning window, area familiarity, synergy combos, level scaling,
first-use-of-the-day, well-rested) with one-key difficulty presets over the whole thing. For players
who want to tune the grind up or down without touching game content.

**Content**: 0 items / 0 blueprints / 0 structures / 0 perks / 0 custom images / 0 localization rows.
Ships 10 C# source files, ~1,989 lines (`Plugin`, `DifficultyProfiles`, `SkillSynergies`,
`SkillConfigManager`, `GlobalUsing`, and `Patcher/{GameLoadPatch, MorningBonusPatch,
AreaFamiliarityPatch, AreaFamiliarityService, PlayerConditionService}`). Every advertised feature is
wired to live code. Player-facing text is entirely BepInEx `Config.Bind` descriptions, which is why
the preflight "missing SimpEn.csv" (E16) is a standing, documented false positive - do not create a
dummy CSV to silence it.

**Stability**: 10/10 - 0 CRITICAL, 0 DESIGN GAP, 2 open WARNING, 0 MINOR after the 2026-09-06
v1.10.1 remediation. The v1.10.0 adversarial read (`.audit/critical-analysis.md`, 2026-09-05, verdict
SHIPS WITH CAVEATS) **cleared the central risk** - all seven bonus flags present in BOTH the
composition body and the early-out guard of the single `ChangeStatValue` composition point, a correct
`IEnumerator` postfix, every advertised config key read by live code, the three new GameStat GUIDs
real - and everything it found around that code has now been fixed: the build is deployed (v1.10.1),
a preset is applied once per profile change rather than re-asserted every launch, all six previously
silent well-rested read paths carry a per-cause breadcrumb, and the Pester gate now rejects the two
bonus-authoring shapes it used to pass blind. `code-quality.md` was re-run against v1.10.0 by a peer
session on 2026-09-06 (9/10, its three findings fixed in `7b67816e2`). Two items remain open: the
stale `Assembly-CSharp-nstrip.dll` (blocked on the operator's external NStrip tool) and a
v1.9.7-scoped `feature-map.md`.

**Open work**: **Zero open retrospectives** - `Documentation/Retrospectives/INDEX.md` has no entry for
SkillSpeedBoost, SSB, or `crispywhips.skill_speed_boost`. Two warnings remain, neither blocking:
(1) `lib/Assembly-CSharp-nstrip.dll` is dated 2026-04-26 against an EA 0.67h game - it compiles clean
while any direct typed call into a game type would throw `MissingMethodException` at runtime, and it
can only be regenerated with the operator's external NStrip tool (fleet-tracked as T1.71; v1.10.1
added no new direct typed call, so no new exposure); (2) `.audit/feature-map.md` is still scoped to
v1.9.7 and describes a smaller mod than the one that ships - re-run `/feature-map SkillSpeedBoost`.
**Runtime verification is now unblocked, not done**: T1.74 and T1.75 can finally be run against a
deployed build, and v1.10.1 adds two checks of its own - that an individual toggle edited after
choosing a preset survives a restart, and that `LogOutput.log` carries no `[WellRested]` warning when
the bonus is enabled.

**Framework compliance**: Tier 2, heavy and correct - 44 `CSFFModFramework.Api` call sites across
`GameLoadPatch` (14), `MorningBonusPatch` (21), `PlayerConditionService` (5) and
`AreaFamiliarityPatch` (4), using `Reflect` / `StatAccess` / `CardUtil`. The card-facing Tier 2
services (`ActionRouter`, `SpawnService`, `TickEvents`, `EncounterGuards`, `ContentModPlugin`) are
correctly absent - a 0-CardData mod that hooks the leaf `ChangeStatValue` coroutine has nothing to
route or spawn. No deprecated patterns: no `DropCollectionGuardPatch`, no `PatchAll()`, no unfiltered
hot-path prefix, no `ModLoaderVerison`/`ModEditorVersion`. **No further Tier-2 migration is owed.**

---

## Phase 0: Stabilize  *(items 1-4 DONE in v1.10.1, 2026-09-06; item 5 remains)*

> **Items 1-4 are closed.** v1.10.1 deployed the build, made preset application one-shot behind an
> `AppliedProfile` latch, added per-cause breadcrumbs to all six previously silent well-rested read
> paths, and hardened the composition gate against `var`-hoisted and inline bonus reads (each of the
> three fixture breaks watched RED before the gate went green at 26/26). Durable record: the 1.10.1
> commit and `CHANGELOG.md [1.10.1]`; item-by-item evidence in `.audit/summary.md` § Remediation pass.
>
> **Item 5 is the only one left**, and it is the one that can break the mod silently for every player
> tomorrow. It needs the operator's external NStrip tool - nothing in this repo can regenerate that
> DLL.

| # | Item | Type | Priority | Complexity |
|---|------|------|----------|------------|
| 1 | ~~**Deploy v1.10.0**, then re-check `.claude/mod-deploy-status.json`~~ **DONE 2026-09-06** - deployed v1.10.1; installed DLL 59,904 B byte-matches `bin/Release`, stale 1.9.7 `ModInfo.json` removed, zip refreshed to `_1-10-1`. T1.74/T1.75 unblocked. | Release hygiene | ~~P0~~ | Done |
| 2 | ~~**Fix the A11 silent-null path**~~ **DONE 2026-09-06** - `_warnedCauses` per-cause latch + `WarnOnce` at all five null returns and the NaN branch; below-threshold stays silent. | Diagnostics | ~~P0~~ | Done |
| 3 | ~~**Fix the M12 profile clobber**~~ **DONE 2026-09-06** - `Plugin.ApplyProfileOnce(force)` + `AppliedProfile` latch; `ApplyProfileSettings` returns `bool` so an unknown name cannot latch; both doc lines corrected in the same commit. | Correctness + Docs-Honesty | ~~P0~~ | Done |
| 4 | ~~**Harden the A12 gate regex**~~ **DONE 2026-09-06** - brace-matched method body, guard split, `var`-hoist rejection, and an unguarded-`Plugin.X`-read check with an explicit non-flag allowlist. Breaks (a)/(b)/(c) each went RED on the correct named test; baseline 26/26 green. | Gate hardening | ~~P0~~ | Done |
| 5 | Regenerate `lib/Assembly-CSharp-nstrip.dll` against the EA 0.67h game binary, then rebuild SSB | Game-update reference refresh | P0 | Medium (needs the user's external NStrip tool - not doable from this repo) |

**Why 1 is first**: `BepInEx/plugins/Skill_Speed_Boost/ModInfo.json` reads `"Version": "1.9.7"` and
its DLL is **38,912 B @ 2026-08-29 14:39** against `bin/Release/Skill_Speed_Boost.dll` at
**54,784 B @ 2026-09-05 15:20**; `.claude/mod-deploy-status.json` still records
`lastDeployedCommit: 787c3b250`, two commits before `e37f7ed03`. **Playthrough item T1.74 cannot be
executed as written** until this is done: under 1.9.7 the six new config keys are not bound at all, so
a tester edits keys BepInEx never created and correctly concludes "these settings do nothing",
attributing a deploy gap to the code. T1.75 is blocked the same way. No player is affected -
`Mod_Update_Manager/Resources/EmbeddedMods/Skill_Speed_Boost.zip` was opened and verified to carry
`ModInfo.json` 1.10.0 with the matching 54,784-byte DLL - so this is a local-install gap, not a
release defect. It costs one command and it is the precondition for every runtime item on this
roadmap.

**Why 2 is P0**: `PlayerConditionService.cs:50-53` promises "A read failure warns once per session
(fleet silent-catch rule) so a future field rename is visible rather than looking like the player
simply never qualifies." The latched `LogWarning` exists only in the `catch` at `:108-118`, and all
five realistic drift paths return null **without throwing** (`CardUtil.GetCachedField` returns null
and never throws - `CSFFModFramework/Util/CardUtil.cs:254-260`), so the catch never runs and
well-rested silently never applies with zero log output. Same defect class `fd99f7f40` claimed to
close as "the last silent early-return on the XP write path" - true for the write path, but the read
path added by the immediately preceding commit has five more.

**Why 3 is P0**: `Plugin.cs:362-363` re-applies the profile unconditionally at the end of `Awake()`,
after every `Config.Bind` has read the player's file, and `DifficultyProfiles.cs:83-90` writes eight
`ConfigEntry.Value` setters that BepInEx persists back to the `.cfg`. An individual toggle the player
edits survives until the next launch and no further - and `README.md:267` and `FEATURES.md:107` both
tell the player to make exactly that edit. Pre-1.10.0 the apply wrote only two keys; 1.10.0 raised it
to eight in the same release that added the false advice. Code and docs ship together (Docs-Honesty).

**Why 4 is P0 despite the gate being green**: the gate passes 23/23 and **does** catch its own
regression - deleting `&& !wellRestedOn` from the guard drove it red, 22/1, with the correct message.
But a fixture-copy break-and-watch-red (scratchpad only, no tracked file mutated) showed two blind
spots that stay **GREEN 23/23**: an eighth bonus hoisted as `var newBonusOn = Plugin.X;`, and the same
bonus read inline with no hoist. `:41` harvests only `bool\s+(\w+)\s*=\s*Plugin\.`, and the `:87`
sanity floor (`>= 5`) still sees the seven existing `bool` flags. This matters more than its severity
suggests because the guard is **unreachable in the default config** (`expMultOn` is always true while
`EnablePerSkillMultipliers` defaults to `true`), so a guard-omission regression would never surface in
ordinary play-testing - this gate is the only thing that would catch it. The open plan row N1 leans
directly on it.

**Why 5 stays P0**: SSB references the publicized nstrip DLL (`SkillSpeedBoost.csproj:38`), and its
copy is dated **2026-04-26** while the plain `lib/Assembly-CSharp.dll` is 2026-09-03. Per CLAUDE.md a
stale nstrip reference compiles clean and then throws `MissingMethodException` on the first direct
typed call into a game type - invisible at build time, invisible in the log until a player hits it.
Narrowing from the v1.10.0 read: **no new direct typed call into a game type was introduced in
1.10.0** (`PlayerConditionService` reaches the game entirely through reflection and framework
helpers), so no release-specific `MissingMethodException` surface was identified. This is a fleet-wide
gap (tracker T1.71 covers eight mods), so batch it with the other nstrip regenerations rather than
doing it alone.

---

## Phase 1: Foundation

> Table stakes. All cheap, all mechanical, none of them add a feature.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| Re-run `/critical-analysis SkillSpeedBoost` against 1.10.0 - **DONE 2026-09-05**, verdict SHIPS WITH CAVEATS; its findings are Phase 0 items 1-4 above | Audit refresh | - | Done |
| Re-run `/code-quality SkillSpeedBoost` - still 2026-08-24 / v1.9.7, never run against the ~600 lines 1.10.0 added including all of `PlayerConditionService.cs` | Audit refresh | P1 | Medium |
| Re-run `/feature-map SkillSpeedBoost` (its own header warns it must be regenerated after any version bump; it still says v1.9.7 / 7 features, the mod now advertises 13) | Audit refresh | P1 | Quick |
| Docs sweep: `Blade_Multiplier` -> `Knife Fighting_Multiplier` (`FEATURES.md:57`, `:235`), "Foraging -> Herbal" -> a real chain (`:142`), "Club Fighting" -> `ClubFighting` (`README.md:143`), `FEATURES.md:5` to thirteen features, config example regenerated from `Plugin.cs:121-345`, EA 0.65 -> EA 0.67h (`README.md:5`, `:48`), `CSFF-JsonData_EA_0-65h` -> `_Current` (`SkillSynergies.cs:25`), `ActiveProfile` description regenerated from the `Profile` struct (`Plugin.cs:225-228`) | Docs honesty | P1 | Quick |
| Run `/consolidate-ideas SkillSpeedBoost` - `Documentation/Ideas/SkillSpeedBoost/IDEAS.md` still lists all seven Near-Term rows as open when six shipped in 1.10.0 | Backlog hygiene | P1 | Quick |
| Play-verify T1.74 (the 1.10.0 bonuses) and T1.75 (morning window) | Runtime verification | P1 | Medium (human, in-game) |
| Version hygiene: **already clean** - `ModInfo.json` / `Plugin.cs` `PluginVersion` / `README.md` header all read 1.10.0; `CHANGELOG.md` and README Version History both carry a 1.10.0 entry | Version hygiene | - | Done |
| Localization: **N/A by design** - no CardData, no CSV. E16 is an adjudicated false positive | Localization | - | Not applicable |

**Why the remaining refreshes are still P1**: the adversarial read is done, but `code-quality.md`
and `feature-map.md` still describe a seven-feature v1.9.7 mod. Seven new config-driven bonuses now
multiply into the same composed total, and the interaction surface (`MaxComposedMultiplier` vs. the
presets vs. a per-skill 0x vs. the daily-first-use claim) is genuinely combinatorial for the first
time. Bonus number eight (the Phase 2 low-condition penalty) has since shipped as v1.10.2 without that
refresh, against a gate that is no longer blind to its shape: Phase 0 item 4 closed the
authoring-shape blindness in v1.10.1, and invariant 4 now covers the sub-1.0 write path the
penalty needed. The two refreshes stay P1 on their own merits - both artifacts still describe a
seven-feature v1.9.7 mod, and the mod now advertises fourteen features.

---

## Phase 2: Core Expansion

> One clear next feature, and one decision that is worth more than any feature.

### ~~Low-condition XP penalty (the "stick" knob)~~ SHIPPED v1.10.2, 2026-09-07
**What shipped**: `LowConditionPenaltyEnabled` (default false) with `LowConditionThreshold` (60)
and `LowConditionMultiplier` (0.5) multiply skill XP by a sub-1.0 factor while ANY of Energy,
Satiation or Hydration is below the threshold percent of its live maximum - the exact inverse of
the well-rested bonus at the same threshold. `PlayerConditionService.IsAnyConditionBelow` sits
beside `IsWellRested` on the same cached reads, with its own `[LowCondition]` per-cause
breadcrumbs so the two consumers cannot silence each other; the composition line AND the
early-out guard entry are both in `MorningBonusPatch.ChangeStat_Post`; `Hardcore`, `Legacy` and
`Immersive` now switch it on, which is the stick those three presets never had.
**What the plan did not anticipate**: the write path discarded every sub-1.0 composed multiplier.
`if (multiplier <= 1f) yield break;` and a write gated on `capped > after` were both correct while
every term could only raise the total, and both would have thrown this penalty away in full, with
no log and no exception - the composition line and the guard entry alone would have shipped a dead
feature behind a green gate. Both are now a tolerance comparison, and the gate carries invariant 4
to keep them from coming back (32/32; three planted breaks watched RED on a fixture copy).
**Owed**: nothing in code. In-game acceptance is tracker **T1.78** (the penalty reduces a real
gain) and **T1.79** (default config unchanged against 1.10.1).

### Decision: `SkillXpModifierService` - one public extension point
**What**: A single registration surface, `SkillXpModifierService.Register(Func<string skillName,
float multiplier> provider)`, with every registered provider folded into `ChangeStat_Post`'s composed
multiplier as one more loop.
**Why**: Highest leverage on the whole roadmap. Every cross-mod idea in Phase 3 currently wants its
own bespoke public setter on SSB; this collapses all of them into consumer-side-only changes with
zero further SSB edits. It also supersedes the older informal "promote `SkillSynergies` to public"
idea.
**Requires**: Three decisions from Jared before any code - provider ordering and how providers
interact with `MaxComposedMultiplier` (1.10.0 made this a live question, not a hypothetical);
per-tick delegate cost vs. caching at action start; and how stable the API is promised to be.
**Complexity**: Medium.

### Decision: does SSB get a save hook?
**What**: Not a feature - the single question that is independently blocking three separate ideas
(Consecutive-Day Streak, Rested-XP Bank, and the level-milestone one-shot bonus). Today nothing SSB
accrues survives a reload: synergy combos live in memory, the daily-first-use ledger re-arms on load
(CHANGELOG 1.10.0), and area familiarity persists only because it got its own TSV file.
**Why**: Answer it once and three parked ideas become buildable together; leave it unanswered and
each stays parked forever, re-litigated by every planning pass. A "no" is a perfectly good answer -
it just needs to be written down so those three rows can move to Ideas with a rejection reason.
**Requires**: nothing. **Complexity**: Quick (the decision; the implementation is Complex).

---

## Phase 3: Integration & Depth

> All soft-dependency only. SSB must stay fully functional with none of these installed.

### RepeatAction: confirm multipliers fire on automated repeats
**What**: Verify SSB's bonuses apply on RA-driven repeat actions, then decide on an
`ExcludeAutomatedFromSynergy` toggle so AFK looping cannot farm the +50% synergy combo.
**Why**: RA shipped 2.1.0 on 2026-09-05, so its dispatch path is newly worth re-checking; and the
synergy combo is the one SSB bonus with an obvious automation exploit.
**Requires**: T1.74/T1.75 verification first (know the bonuses work before asking whether they
over-work). **Complexity**: Medium.

### Station-anchored themed XP bonus (ACT / WDI / CMC)
**What**: A `StudyLocationUID` -> multiplier config map, resolved through the
`AreaFamiliarityPatch.CurrentLocationUid` the mod already tracks. Any CT2 station qualifies by config
- CMC's Study Desk, an ACT forge, a WDI workshop - with no partner-mod code change at all.
**Why**: It is pure config on SSB's side and needs nothing from the other mods, which makes it the
lowest-coordination cross-mod win available. **Requires**: none (though it is much cleaner as a
`SkillXpModifierService` consumer). **Complexity**: Medium.

### HerbsAndFungi "Focus Tonic" -> timed global XP flag
**What**: An H&F consumable sets a timed flag; SSB folds it in as one more multiplier for a duration.
**Why**: The first concrete consumer of `SkillXpModifierService`, and the one that proves the API
shape is right before others build on it. **Requires**: the Phase 2 `SkillXpModifierService`
decision. **Complexity**: Medium.

### Area Familiarity decay
**What**: Fade familiarity after N unvisited in-game days, mirroring the mod's own staleness
philosophy. Needs a `lastVisitedDay` column in `AreaFamiliarity.tsv`.
**Why**: Familiarity is the only monotonic thing in a mod whose entire thesis is that unused things
fade. 1.10.0's `AreaFamiliarityMinBonus` set the floor; nothing lowers a learned location.
**Requires**: a decision on decay rate and floor, plus care around the TSV schema change (v1.7.5
already flagged that path as fragile). **Complexity**: Medium.

---

## Phase 4: Polish

> A 0-CardData mod has no art or animation phase. Polish here means text, diagnostics, and docs.

| Item | What | Complexity |
|------|------|------------|
| Config-description pass | 1.10.0 added roughly a dozen config keys at once. Re-read every `Config.Bind` description as a player would: it is the mod's ONLY player-facing text, and there is no CSV or card description to correct it | Quick |
| FEATURES.md preset grid | **Verified present this pass** (FEATURES.md:117 area) - the seven presets each list their full toggle row. Nothing owed; re-check it after adding preset number eight or toggle number seven, since the grid is the only place a player can compare them | Quick |
| Document the config-write side effect | Choosing a profile now writes all six toggles into the player's `.cfg`, so an individual key must be set AFTER picking a profile. CHANGELOG says it; README/FEATURES troubleshooting should too, since "my setting reverted" is the shape of the support question it will generate | Quick |
| Area Familiarity UI feedback | Players cannot see their familiarity level without reading a TSV. A perk-card display via framework perk injection is the likely approach - **needs a decision from Jared** on whether it clutters the perk list | Medium |

---

## Long-term Vision

SSB's natural endpoint is **the fleet's skill-progression substrate, not just a slider pack**. 1.10.0
finished the "every knob a solo player wants" arc - eleven advertised features, all off or no-op by
default, with presets that set the whole stack coherently. There is little left to add for a player
tuning their own game, and adding more risks a config surface nobody can hold in their head. The next
axis is outward: `SkillXpModifierService` turns SSB into the one place any mod in this repo can say
"this thing should teach you faster," and every cross-mod idea already on the backlog is waiting on
exactly that one surface.

**Potential major additions** (not yet justified - revisit after Phase 3):
- **Per-action context capture (tool quality / action difficulty / biome)** - three separate ideas
  that share one prerequisite investigation: does the tool or action captured in
  `ActionRoutine_Post` reliably pair with the following `ChangeStatValue` tick? Resolve that once and
  all three ride the same plumbing; leave it unresolved and none of them are buildable.
- **Live `NoveltyCooldownDuration` re-tune without a reload** - would delete the "takes effect after
  reloading" caveat that every staleness and profile change carries. `Plugin.cs` still carries the
  "hot-reload of deep stat graph proved unstable" comment; the investigation is whether the two
  novelty fields specifically were the unstable part, or the full graph rewrite was.
- **Night-Owl bonus + time-window arbitration** - explicitly **blocked** on the T1.75 morning-window
  confirmation, since both share `IsMorningWindow`'s clock math. One playthrough item unblocks the
  whole time-of-day family, including per-skill windows.

These live in `Documentation/Ideas/SkillSpeedBoost/IDEAS.md` (which needs a `/consolidate-ideas` pass
- see Phase 1) and in `SkillSpeedBoost/.audit/ideas.md` (current as of 2026-09-05).

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod SkillSpeedBoost` and update this roadmap |
| Game version update | **Regenerate `lib/Assembly-CSharp-nstrip.dll` with NStrip** (a plain DLL copy does NOT fix this mod), rebuild, then `/update-mod-version` and `/diagnose-log` |
| After adding any new XP bonus | Confirm it appears in BOTH the composition body and the early-out guard, then run `Development_Tools/Tests/SkillSpeedBoost-BonusComposition.Tests.ps1` |
| After fixing a critical issue | Run `/critical-analysis SkillSpeedBoost` to verify the fix |
| After Phase 2 complete | Run `/export-to-repo SkillSpeedBoost` and bump the minor version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod SkillSpeedBoost         - full health check, updates .audit/
/critical-analysis SkillSpeedBoost - adversarial review (STALE: last run 2026-08-24 vs v1.9.7)
/code-quality SkillSpeedBoost      - C# reliability scan (STALE: last run 2026-08-24 vs v1.9.7)
/feature-map SkillSpeedBoost       - regenerate the feature table (STALE: scoped v1.9.7)
/consolidate-ideas SkillSpeedBoost - reconcile IDEAS.md against what 1.10.0 shipped
/build-mod SkillSpeedBoost         - build Release DLL
/deploy-mods SkillSpeedBoost       - build + deploy to game
/update-mod-version SkillSpeedBoost <ver> - bump version in all 3 files
/export-to-repo SkillSpeedBoost    - push to public repo
```

*(`/repair-items` and `/repair-blueprints` are not applicable - this mod ships no JSON content.)*


## Plan Reconciliation Log

> Written by `/cleanup-plans` Step 4b (here, by the session that drained the plan). Append-only:
> never reword, reorder or delete an entry. `/roadmap` Step 7 preserves this section verbatim when
> it overwrites the rest of this file.

### 2026-09-07 - Audit_Remediation_Plan
- **Verdict:** 0 fix rows (the threshold gate never cleared: 0 open CRITICAL, 0 Design Gap), 1 feature
  row. **N1 low-condition XP suppression BUILT this pass** as v1.10.2. The 1-prompt pack is drained,
  0 open prompts remain, and the plan's own "Not promoted, and why" table held 7 rows that are all
  blocked on a design decision or an investigation, every one of them already sequenced with its
  reason in this ROADMAP's Phase 2 / Phase 3 / Long-term Vision sections - so nothing buildable was
  left behind.
- **Disposition:** DELETE from `Documentation/Plans/SkillSpeedBoost/` and ARCHIVE to
  `Documentation/Design/SkillSpeedBoost_Audit_Remediation_As_Built.md` (plan verbatim plus its prompt
  pack as an appendix), per `Documentation/Plans/README.md` step 4 and matching the ACT and CMC
  archives of the same day. Nothing is erased: the 7 rejection reasons travel with the archived doc
  and are independently live in this file.
- **Pruned:** the whole plan doc (137 lines) and the whole 1-prompt pack, both archived rather than
  dropped. The `Documentation/Plans/SkillSpeedBoost/` scope folder is now gone.
- **Evidence:** N1 as built, re-derived on disk rather than taken from the plan. Pre-build shipped
  state re-confirmed: `LowCondition` matched 0 times across `SkillSpeedBoost/**/*.cs` (unbuilt, not
  merely unlisted). Post-build: 3 `Config.Bind` entries under a new `[LowCondition]` section with
  defaults `false` / `60f` / `0f-1f` clamped `0.5f`; `SetLowConditionPenaltyEnabled` called by
  `ApplyProfileSettings` (9 setters now, one per preset key); `IsAnyConditionBelow` sharing
  `ResolveInstance`/`Evaluate` with `IsWellRested` behind a `Mode` discriminator and a per-consumer
  `WarnOnce` key prefix, so all 7 failure paths breadcrumb under `[LowCondition]` as well;
  `lowConditionOn` present in BOTH the hoist block and the early-out guard. Build: `dotnet build -c
  Release` clean, 0 warnings / 0 errors. Gate: `SkillSpeedBoost-BonusComposition.Tests.ps1` 32/32
  green (was 26/26), the count rising by the 3 new invariant-4 tests and 1 new no-op-default case.
  The gate is not merely green - it was watched failing first: it went RED 27/1 on the two new
  magnitudes until they were classified in `knownNonFlagReads` (invariant 1c doing its job), and then
  on a scratchpad fixture copy of the tree, break A (restore `multiplier <= 1f` early-out) drove 2
  named tests red, break B (restore `if (target > after)`) drove 1 red, and break C (drop
  `&& !lowConditionOn` from the guard) drove the pre-existing flag check red, with 32/32 on the
  unbroken control. **What no check here can see:** whether the penalty actually changes a number in
  a running game. That is the whole content of T1.78/T1.79, filed as `pending` in
  `.claude/playthrough-test-status.json` BEFORE this record was written, and it is why the CHANGELOG
  says the 60/0.5 defaults are tunable starting points rather than balanced values.
