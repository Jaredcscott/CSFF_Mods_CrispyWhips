# Roadmap: Harsh Wilds
Version at time of writing: 0.11.10
Date: 2026-11-07
Audit score: 5/10 - FAIL (1 CRITICAL at the shipping layer; source tree clean). That CRITICAL was cleared 2026-11-10; the score waits for the next consolidation.

## Current State

**Theme**: Optional threats for a harder forest (creatures, raids, hazards, world events, afflictions, fair folk) plus the forest's old powers - spirits, rites, brews, ritual objects and three timed realms - each danger behind its own config switch. For players who want a rougher, more mythic survival run.

**Content**: 46 items / 36 blueprints / 3 placed Locations (CT2) + 11 Realm env/location/exit cards (CT4/CT8) / 0 perks / 62 custom images. Plus 57 SelfTriggeredActions, 13 Animal manifests, 8 Encounters.

**Stability**: the 2026-11-07 audit scored 5/10 - FAIL on one CRITICAL (`.audit/summary.md` C1: stale build output kept shipping the golden-hind content cut in 0.11.5). C1 was cleared on 2026-11-10 (Plan Reconciliation Log, 2026-11-11); the score is not re-run yet. The tracked **source tree is clean** (build 0/0, localization + Chinese parity + acquisition all clean). Also 2 warnings (README species-agent contract omits the shaman; owner confirmation pending on the HandAction-only ritual blueprint convention) and 7 minor polish items.

**Open work**: No open or pending retrospective in `Documentation/Retrospectives/INDEX.md` names HarshWilds, its abbreviation, or its plugin GUID.

**Framework compliance**: Tier 2 - the mod routes everything through framework services (ActionRouter via `ActionUser`/`SmithingBurnHook`/`ForageBiteHook`, EncounterGuards for the heron/realm/owl guards, declarative Animals/Encounter/SelfTriggeredAction loading, `ContentModPlugin` base). No deprecated patterns: no `DropCollectionGuardPatch`, no unfiltered hot-path prefixes, no `ModLoaderVerison`/`ModEditorVersion`, no manual perk/blueprint injection. Requires CSFFModFramework 2.28.0+.

---

## Phase 0: Stabilize

> Fix before any new content lands or any upload. The source is clean; this phase is build-output hygiene and two docs fixes.

| Item | Type | Priority | Complexity |
|------|------|----------|------------|
| ~~Clear the golden-hind build-output orphans (C1), verify the live Nexus 0.11.10 file, extend the preflight orphan scan~~ **Done 2026-11-10.** The installed copy and its release zip were rebuilt at 0.11.11 with none of the five files; the live Nexus 0.11.10 file never carried them; the preflight now scans every build-output folder (`dec663077`). See the Plan Reconciliation Log, 2026-11-11. | CRITICAL fix (build/deploy) | Done | - |
| ~~README species-agent contract (W1)~~ **Done** in `699e6be8a`: the README lists the shaman's agent UID. | Docs accuracy | Done | - |
| **Two stale rarebeasts comments (m1, m2).** `hw_lindwurm_fang.json` `//LindwurmFang` still claims the art is the `Hook_Bone` stand-in; `Tgr_HW_Lindwurm.json` `//Roll` names "LakeBeast" instead of "Lindwurm". | Comment fix | P1 | Quick |

---

## Phase 1: Foundation

> Mostly already in place - recorded here as the maintained baseline, not new work.

| Item | Type | State |
|------|------|-------|
| Versions synchronized across ModInfo.json / Plugin.cs / README.md at 0.11.10 | Version hygiene | DONE - keep synced via `/update-mod-version` |
| Chinese localization parity (`SimpCn.csv`, 1162/1162, no stale/no-CJK/extra) | Localization | DONE - keep rows paired per commit |
| Framework Tier 2 adoption (ActionRouter / EncounterGuards / declarative loaders) | Framework | DONE |
| Close pending in-game verification rows (`.claude/playthrough-test-status.json`): the heron-feel row T2.1327 and T2.1811-T2.1814, then drop the matching "not yet confirmed in a running game" caveats from the README in the same commit | Verification | P1, ongoing |

---

## Phase 2: Core Expansion

> The mod's two archived plans are drained; its remaining substance is tuning, not new systems.

### Threat-balance tuning pass
**What**: tune the threats' chances, numbers and lengths (raids, world events, afflictions, hazards, rare-beast arrival rolls) from recorded harness and person runs. ModInfo and README both disclose these as "first guesses not yet balanced against play".
**Why**: every threat is wired and honest, but un-tuned; this is the single largest lever on how the mod actually feels.
**Requires**: Phase 0 (ship a clean build first) + the pending tracker rows reporting.
**Complexity**: Complex (spans sessions; feed from the Benches/measurement discipline, not ad-hoc probes).

### Owner decision - HandAction-only ritual/shrine/calling blueprints (W2)
**What**: a one-time yes/no on whether 13 ritual/shrine/calling blueprints shipping `BlueprintActionTagsWarpData: ["HandAction"]` with no `CraftAction` is deliberate ("a rite, not a craft").
**Why**: if confirmed, note it so audits stop re-flagging it; if not, add the appropriate `CraftAction` tags.
**Requires**: none.
**Complexity**: Quick (decision) / Medium (if tags must be added).

---

## Phase 3: Integration & Depth

> Cross-mod ties are already shipped and guarded. No open plan proposes new ones; revisit only if a sibling mod opens a natural hook.

Shipped integrations (keep working on game/framework updates): H&F/CMC lit incense for athame rites (IncenseRiteGuard), WDI heron raids on fishponds (HeronRaidGuard), ForestLore mud volcano + Ancient Cairn Underworld door, FarTerritory selkie on the Driftwood Shore, ACT salt-cured meat as black-bear bait, Sirus23 owl/wolf/fox interplay. All optional and no-op when the partner mod is absent.

---

## Phase 4: Polish

| Item | What | Complexity |
|------|------|------------|
| Own art for stand-in ritual/realm cards (m5) | Replace base-game stand-ins: Cleansing Smudge (Dry Bog Myrtle picture), witchfire/brews/ritual-object group, Ancestor Enchantment (cairn picture), the three realm cards. Use `/create-image-refs` for cards that must match a shipped sibling. | Medium |
| Completion sounds on crafted items/structures (m3) | 24 blueprints ship no `WhenCreatedSoundsWarpData`. Add a real vanilla AudioClip (check the clip fixture; a material word is not a clip) or `[]` where nothing fits. | Quick |
| Description / caveat pass | Once Phase 1 verification rows close, drop the "not yet confirmed" caveats from the README in the same commit as each confirmation. | Quick |

---

## Long-term Vision

Harsh Wilds is feature-complete against both of its archived plans: the threat/powers content update and the vanilla-balance/niche pass are built and shipped. Its natural endpoint at v1.0 is a **balanced, fully-illustrated** version of the current scope - every threat's numbers tuned against real play, every card on its own art, and every "not yet confirmed" caveat retired as its tracker row passes. The role it fills is the studio's "harder, more mythic forest" layer, composed of optional switches so a player tunes their own difficulty.

**Potential major additions** (not yet justified - revisit after the tuning pass):
- A difficulty "preset" meta-switch that sets several threat switches at once - fits the config-switch architecture if players find per-threat tuning fiddly.
- Deeper realm progression (a reason to return to a realm beyond the one-bargain-a-run limit) - fits the realms theme but needs a designed loop before it earns a slot.

Anything worth building belongs in a fresh `Documentation/Plans/HarshWilds/MASTER_PLAN.md` (or its Considered table, with the reason, if ruled out). The C1 remediation plan is done and archived at `Documentation/Design/Harsh_Wilds_Audit_Remediation_2026-11_As_Built.md`. Never create a stub under `Documentation/Ideas/`.

---

## Maintenance Calendar

| Trigger | Action |
|---------|--------|
| After any new content phase | Run `/audit-mod HarshWilds` and update this roadmap |
| Before any Nexus upload | Confirm the release zip carries no removed-content orphans (the C1 class); `dotnet clean -c Release` if in doubt |
| Game version update | Run `/update-mod-version`, check CLAUDE.md for EA version notes, re-run `/diagnose-log`; refresh `lib/Assembly-CSharp.dll` |
| After fixing a critical issue | Run `/critical-analysis HarshWilds` to verify the fix |
| After Phase 2 tuning | Run `/export-to-repo HarshWilds` and bump version |

---

## Skill Cheatsheet for This Mod

```
/audit-mod HarshWilds           - full health check, updates .audit/
/critical-analysis HarshWilds   - adversarial review
/repair-items HarshWilds        - auto-fix item JSON issues
/repair-blueprints HarshWilds   - auto-fix blueprint JSON issues
/build-mod HarshWilds           - build Release DLL
/deploy-mods HarshWilds         - build + deploy to game
/package-mod-suite              - rebuild MUM suite (HW is a suite mod)
/update-mod-version HarshWilds <ver> - bump version in all 3 files
/export-to-repo HarshWilds      - push to public repo
```

## Plan Reconciliation Log

### 2026-10-04 - MASTER_PLAN
- **Verdict:** counts: 51 pack prompts and 33 plan features checked. Pack: 5 VERIFIED-DONE (research Prompts 1 to 4, whose four `Documentation/Research/HarshWilds/wave0-*.md` static files exist, and Prompt 13, verified by its own gate), 10 built and awaiting verification (Prompts 8 to 12 and 14 to 18), 36 open (5, 6, the second run of 7, and 19 to 51). Plan: 1 feature VERIFIED-DONE (6), 10 built and awaiting verification (1 to 5 and 7 to 11), 22 open (12 to 33). One STALE-CLAIM pair corrected (the Forest Lore and Far Territory folders now exist).
- **Disposition:** KEEP IN PLACE (36 open prompts remain).
- **Pruned:** pack Prompts 1 to 4, 8 to 18 deleted (their criteria moved to a new Awaiting verification list at the top of the pack); Prompt 7 narrowed to its second run. Plan sections 1 to 5 and 7 to 11 collapsed to one-line BUILT pointers; section 6 collapsed to a pointer and not deleted, because `Development_Tools/Tests/HarshWilds-PublicUids.Tests.ps1` cites it by number. Nothing deleted from the Considered table or the Wave 0 findings.
- **Evidence:** `git log -- HarshWilds` named the build commits (`1d901063a`, `4b7677636`, `d93d3bb37`, `2b17872fd`, `7d65acc0f`, `57f4299c8`, `28ff3ffd4`, `56eb10605`, `30a4ed362`, `a7e7f26f0`, `f9f16ac29`). A probe script read the live files: `Lynx.json` and `Tgr_HW_BloodMoon.json` carry the `Counter` GUID; `hw_tgr_blackbear.json` holds 16 `ExcludeInventory` true rows with `MaxWeightPerCard` -5 against 20 false rows (16 bait plus 4 counterplay); all five larder UIDs sit in both the bait rows and `hw_event_blackbear.json`; the eagle names `DogFriend`, `fc_wolf_companion`, `fc_fox_companion` and the `BloodSpilled` and `RainValue` GUIDs; `MuskOx2.json` and `MuskOx3.json` carry `hw_muskox2` and `hw_muskox3`; the heron, adder and cold snap files, their gates and `Plugin.cs` guard registration exist; `Tgr_HW_Adder.json` holds 24 `RequiredCardsOnBoard` actions; `hw_stat_eclipse.json` carries -110. Zeros for the unbuilt features (no `hw_tgr_wolverine.json`, `Tgr_HW_AlgaeBloom.json`, `SmithingBurnHook.cs`, `hw_lynx_trapped`, `hw_tune_raids`, no `Traps` in `Lynx.json`, no `wave0-live-a.md` or `wave0-live-b.md`) ran beside controls that hit (`hw_switch_heron` found by `git grep`, `Heron` found in `Switchboard.cs`). `ForestLore/` and `FarTerritory/` exist at 0.1.0 (a control that the folder probe could hit; the first run of that probe wrongly asserted both absent and failed, which is how the stale claim was caught). Deployed `Harsh_Wilds.dll` MD5 `014eb29f...` differs from `bin/Release` `9f11029d...`, both ModInfo at 0.7.0. Every `T\d+\.\d+` id left in the plan and the pack resolved in `.claude/playthrough-test-status.json` (22 and 29 ids, none missing).
- **Verification debt:** T2.1100 to T2.1117 (all 18 pending), and T2.482 and T2.484 (0.7.0 content, pending). No built feature lacks a tracker row except feature 6, which its gate verifies.

### 2026-10-07 - MASTER_PLAN (cleanup-plans fleet sweep)
- **Verdict:** 33 plan features and 51 pack prompt numbers checked against `HarshWilds/` at `189f8b99e`. 1 feature VERIFIED-DONE and pruned (7); 0 OBSOLETE; 17 built features awaiting verification (1 to 5, 8 to 11 and 13 were already pointers; 15, 17 to 21 and 23 collapsed in this pass); feature 6 stays a pointer (gate-verified; its README contract waits on W0-11's live probe); 0 FAILED (eight harness FAILs were CONTROL or fixture faults, none a mod defect); 4 STALE-CLAIMs corrected (section 0's version bullets, the Release tail's "not MUM-bundled" and "cites no tracker ids", the "uncommitted / not deployed" notes on 13 to 23); 16 features OPEN (12, 14, 16, 22, 24 to 33) plus the live Wave 0 probes. Pack: 27 open prompts before and after (5, 6, 7, 19, 21, 23, 29, 31 to 39, 41 to 51), 17 awaiting before and after, none fully verified yet.
- **Disposition:** KEEP IN PLACE - 27 open prompts of code and research work remain.
- **Pruned:** plan section 7 (three musk oxen, T2.1104 PASS) deleted; sections 13, 15, 17, 18, 19, 20, 21 and 23 collapsed to BUILT lines (with the facts open prompts read kept as one bullet each, and the bear rows of 17 and 18 folded into section 16); the plan's dated status history of 2026-10-01 to 2026-10-06 condensed to one paragraph (owner quotes kept). Pack: the Awaiting verification list rewritten as one compact bullet per prompt under a gated `## Awaiting verification` heading; Prompts 6, 7, 23, 29, 47, 48 and 51 corrected (built features are live checks, not closes; the bear rows Prompts 24 and 25 shipped; sibling deps met; section 7 citation; 0.8.0 and the pre-release suite membership).
- **Evidence:** `git log -- HarshWilds/` (`67a9ed09f` committed Prompts 22 and 24 to 30, which the pack header still called uncommitted; `git status -- HarshWilds/` clean outside `bin/`); MD5 of the game's `Harsh_Wilds.dll` equals `bin/Release` (`ea8bc11c`), both `ModInfo.json` 0.8.0; `CHANGELOG.md` 0.8.0 (2026-10-05) carries features 1 to 11 and 13. Spot reads: `Bandit.json` `ApproachLabelLocalizationKey` and its CSV rows; `hw_tune_raids` 2 hits in each of the eagle, bear and heron STAs; the `SleepInterrupt` GUID 1 hit in the bear and the eagle STA and 0 in the heron's (as built: the heron wakes nobody); `hw_bp_bugtrap_resin` and `_tar` in `BlueprintTabs.json`; `SmithingBurnHook.RegisterOnce` and `ForageBiteHook` registering through `ActionRouter`; `Switchboard.cs` calling `StatAccess.Set` (line 220; `CSFFModFramework/Api/StatAccess.cs` declares `Set(string, float)`). Open zeros (no `Traps` in `Lynx.json`, no wolverine, mud volcano, trapped lynx, spirit, fairy, realm, shaman or lindwurm file, no `BlackBear` animal, no `wave0-live-*.md`) ran beside a control that hit (`heron` found in three HarshWilds JSON files). Tracker: 32 HarshWilds rows T2.1100 to T2.1131, T2.1100, 1104, 1106, 1110, 1111, 1114, 1116 and 1125 in `confirmedItems`. Harness reports under `TestHarness/client/reports/`: PASS for T2.1101, 1102, 1105, 1109 and 1121 (unrecorded); FAIL for T2.1103, 1107, 1108, 1113, 1118, 1123, 1127 and 1129, each on a CONTROL or fixture step (T2.1127's burn and skilled-smith legs passed). No tracker row filed: feature 23's check is the open T2.477.
- **Verification debt:** T2.1101 to T2.1103, T2.1105, T2.1107 to T2.1109, T2.1112, T2.1113, T2.1115, T2.1117 to T2.1124, T2.1126 to T2.1131, and T2.477.

### 2026-10-09 - MASTER_PLAN
- **Verdict:** 33 plan features and the pack's 51 numbered prompts checked against `HarshWilds/` and the tracker at `51641ef64`. 0 VERIFIED-DONE (every built feature still has at least one pending row), 0 OBSOLETE, 17 built features awaiting verification (1 to 5, 8 to 11, 13, 15, 17 to 21, 23; feature 6 stays a gate-verified pointer), 0 FAILED in the tracker but 12 rows with an untriaged harness FAIL in the 2026-10-09 six-rig batch, 4 STALE-CLAIM corrections (section 13's INFERRED fishing read, section 22's unchecked deploy, the W0-5 and W0-8 findings lines), 15 features OPEN (12, 14, 16, 22, 24 to 33). Pack: 28 open (27 carried plus new Prompt 52), 17 awaiting verification.
- **Disposition:** KEEP IN PLACE - 28 open prompts of code and research work remain.
- **Pruned:** nothing deleted from the plan or the pack. Pack Prompt 6 item W0-8 removed (answered by T2.1118, CONFIRMED 2026-10-09 in `51641ef64`); Prompt 6's W0-12, W0-13 and W0-14 items made skippable once T2.1123, T2.1127 and T2.1129 are recorded. Added Prompt 52 (triage of the 12 six-rig FAILs) and a "Failed - needs a fix pass" list. Every BUILT pointer in plan sections 4, 8 to 11, 13, 15 and 17 to 21 now names the r46 person rows (`9191af9f9`) and the 2026-10-09 confirmations (T2.1107, T2.1115, T2.1118).
- **Evidence:** `git log 189f8b99e..HEAD -- HarshWilds` (only `a11a1fd6f`, comment-only, and a `bin/Release` CSV refresh in `e57d44cfc`); MD5 of the game's `Harsh_Wilds.dll` equals `bin/Release` (`ea8bc11c`), both `ModInfo.json` 0.8.0; a name search under `HarshWilds/` for each open feature's planned files and UIDs (`hw_lynx_trapped`, wolverine, `Animals/BlackBear`, mudvolcano, `hw_spirit`, athame, mistletoe, banshee, shaman, lindwurm and 15 more) found 0 hits, control `hw_tgr_heron` 4 hits; a tracker script read all 118 Harsh Wilds rows (T2.1107, T2.1115, T2.1118 newly in confirmedItems; T2.1326 to T2.1356 filed as person rows); the newest report per row under `TestHarness/client/reports/` gave six-rig PASS for T2.1102, T2.1105, T2.1109, T2.1113, T2.1121, T2.1123, T2.1124, T2.1126, T2.1127 and T2.1129 (unrecorded, not in the skeptic-rejected list of `.claude/handoffs/playthrough-six-rig-run-2026-10-09.md`) and FAIL for T2.1101, T2.1103, T2.1108, T2.1112, T2.1117, T2.1119, T2.1120, T2.1122, T2.1128, T2.1130, T2.1131 and T2.477; T2.1118's report asserts eight rod casts with no Perch during the bloom. The deployed Forest Lore `MudVolcano.json` and Far Territory `FT_Shore.json` are byte-equal to source (MD5 `d502b8ad`, `c75bbf97`). No `.claude/game-lock.txt` exists, so the lane is free.
- **Verification debt:** T2.1101 to T2.1103, T2.1105, T2.1108, T2.1109, T2.1112, T2.1113, T2.1117, T2.1119 to T2.1124, T2.1126 to T2.1131, T2.477, and the person rows T2.1326 to T2.1339.

### 2026-10-09 (later) - MASTER_PLAN, second /cleanup-plans pass
- **Verdict:** NOT drained. 28 open prompts, 17 awaiting verification. 0 pruned, 0 obsolete, 9 stale claims corrected (version bullets and seven BUILT pointers that said Unreleased but shipped in 0.9.0). Prompt 52 narrowed: 618ef7c78 reworked 11 of its 12 FAIL scenarios; it now triages T2.477, T2.1119 (new MissingStatInfo on every Spearfish cast) and T2.1122 (eight-day eagle count 1 of 8).
- **Disposition:** KEEP IN PLACE - code work remains.
- **Pruned:** nothing; the pack's Failed section rewritten to the post-rework state; header notes that the next build prompt must open a new Unreleased CHANGELOG head and Prompt 51 now closes 0.10.0.
- **Evidence:** audited against 1d9cbf121. 375d80a78 set 0.9.0 for the MUM 2.1.63 pack with no behaviour change; deployed Harsh_Wilds.dll equals bin/Release by MD5; none of the open features' files exist; batch walkthrough-20261009-154702 results read for T2.1108, T2.1119, T2.1122. PlaythroughRow-Citations 13/13.
- **Verification debt:** T2.1102, T2.1105, T2.1109, T2.1113, T2.1121, T2.1123, T2.1124, T2.1126, T2.1127, T2.1129 (PASS, unrecorded); T2.477, T2.1119, T2.1122 (triage); stale tracker notes on T2.1102, T2.1129, T2.342. T2.1108 was recorded CONFIRMED by the 2026-10-09 second review.

### 2026-10-12 - MASTER_PLAN, drained and archived
- **Verdict:** DRAINED. The pack (header "0 open") holds no open prompt: Prompts 43 (Moon Realm and Underworld, `d90f8502d`..`9e3ee9f30`), 46 (fairy king and queen, `6bd980acb`) and 51 (0.11.0 closeout) landed this day. Plan features 1 to 33: every one is built except feature 16 (the manifest black bear) and the animal beacon row of 26, both parked by the owner 2026-10-12 and recorded in the Considered table; each built feature still has pending person rows. 0 OPEN rows, 0 FAILED, 0 STALE after this pass (the README intro and Status line and the 0.11.0 version were the last stale claims, fixed in the closeout).
- **Disposition:** ARCHIVE - the whole plan moved to `Documentation/Design/Harsh_Wilds_Content_Update_As_Built.md` (section numbers unchanged, so every "plan section N" citation still resolves); the pack was deleted and its Awaiting-verification list was carried into the archive as an appendix, since that list is the only place the per-feature acceptance wording lives.
- **Pruned:** nothing from the plan body (it was already collapsed to BUILT pointers and as-built notes on earlier passes); the pack, 0 open prompts, deleted whole. 188 inbound `Plans/HarshWilds/MASTER_PLAN.md` citations in gates, scenarios, shipped-JSON comments, research and design docs and the tracker were repointed; dated records (CHANGELOG, this log, the 2026-09 plan-review report) were left as written.
- **Evidence:** this session ran the full Harsh Wilds gate list at HEAD (HarshWilds-* 23 gates, Animals-ManifestHygiene, Framework-AnimalSpawnConditions, CardData-Integrity, Csproj-ContentIncludes, MapNodes-Schema, Deploy-Mods, Sprite-NameResolves, CardArt-ShippedForm, Harness-ScenarioDryRun, Version-Synchronization, Encounter-ResultReachability, CardArt-PickRecord, the localization gates; Realms 101, FairyFolk 39, PublicUids 13, RitualObjects 29, Spirits 57 all green when run alone), Release build 0 warnings, `derive_content.py --check` exit 0; scenarios T2.1545, T2.1193 and T2.1194 PASS on dev lane HW-P46 (rig 2) and T2.1196 to T2.1199 PASS on lane HW-P43 (rig 3). Tracker probe: all 111 ids the pack's Awaiting list cites resolve (87 are `pending` rows in `items`, the other 24 are in `confirmedItems`); `Plans-AwaitingRowIds` green. Control: the first run of this probe read `items` as a list and reported all 111 ids missing, so the probe can return a miss; after reading `items` as the dict it is, 0 are missing.
- **Verification debt:** 87 `pending` rows (T2.477, T2.1101 to T2.1102, T2.1105, T2.1109, T2.1113, T2.1117, T2.1121 to T2.1122, T2.1126, T2.1129, T2.1131 to T2.1133, T2.1136 to T2.1141, T2.1143, T2.1146 to T2.1153, T2.1155 to T2.1163, T2.1166 to T2.1167, T2.1170 to T2.1186, T2.1189 to T2.1196, T2.1199, T2.1326 to T2.1339, T2.1540 to T2.1546); the other 24 ids the appendix cites are in `confirmedItems`. The court is T2.1545 (harness PASS) and T2.1546 (person). The founding-plan blight defect (T2.473, T2.477) is unrouted to `/failure-digest` (`Documentation/Research/HarshWilds/harness-triage-2026-10-10.md`).

### 2026-10-16 - MASTER_PLAN (vanilla balance and niche content), drained and archived
- **Verdict:** DRAINED. Section 1 (vanilla balance, HW-B1 to HW-B6 and the coat armour fix) shipped in 0.11.6; section 2 niche rows N1, N2, N4 and N5 were built in 0.11.8 by pack Prompts 4, 1, 3 and 2 (`ea7050bc8`, `7d192a92f`, `476710e87`, `141c651fd`, integrated on branch `hw-pack-merge-2026-10-16` and landed on master 2026-10-17 as 0.11.8, since 0.11.7 had already gone to Nexus without them); N3 (smoke-cure for fleas) was already built before the pass (`Tgr_HW_FleasBogMyrtle`, `Tgr_HW_FleasJuniper`, T2.488), so the pack never carried it. Every new number is a tunable placeholder.
- **Disposition:** ARCHIVE - the plan moved to `Documentation/Design/Harsh_Wilds_Vanilla_Balance_As_Built.md` (section numbers unchanged); the pack, 0 open prompts, was deleted and its Awaiting-verification list carried into the archive as Appendix A.
- **Pruned:** the section 2 idea table became as-built lines. Four live citations repointed (the N1 trigger's `//Decision`, the Coats and BalanceGates test headers, a `Deploy-Mods.ps1` comment); dated records left as written.
- **Evidence:** on the integrated tree, `dotnet build -c Release` 0 warnings 0 errors and `Development_Tools/Run-Gates.py` over every `HarshWilds-*` gate plus the localization, card-art, sprite, drop, blueprint, version, plans, encounter-guard and UID gates: 40 gates, 0 red. Two mutation cases that threw under `Set-StrictMode -Version 3.0` were fixed first (`5add5f379`; BalanceGates B4 was red on master under Run-Gates since 0.11.6).
- **Verification debt:** T2.1749 to T2.1754 (0.11.6) and T2.1800, T2.1802, T2.1804 to T2.1810 (0.11.8) pending. Card art for the Lynx Cloak, Lindwurm Fang, Dragon Charm and Wolverine at the Kill is a stand-in (no OpenArt access in the build session).


### 2026-11-06 - Fleet_APE_Port
Scope: Fleet
- **Verdict:** Fully landed. Section 10 (W0.10) rows 11 and 14 - the plan's last two open code
  rows - built, build-verified across every affected mod, and gate-verified in Prompts 45 and
  46 (`Api.Npc.RegisterMoveGuard`/`MoveGuardPatch` for row 11; `GameQuery.IsInPlayerEnv` made
  public plus 3 call-site migrations for row 14). Every other section (1, 2, 3, 4.1, 5.1, 5.7,
  6.4, 6.5, 8, 9) was already reduced to as-built lines in earlier prompts. 0 open code rows
  remain in the plan or its pack.
- **Disposition:** ARCHIVE - plan doc moved to `Documentation/Design/Fleet_APE_Port_As_Built.md`
  (full history preserved, nothing summarized away). Pack
  (`MASTER_PLAN_Implementation_Prompts.md`) DELETED per policy (packs are always deleted, never
  archived); its citations elsewhere were repointed to the landing commit of the row each one
  names, since no archive exists for it.
- **Pruned:** `Documentation/Plans/Fleet/MASTER_PLAN.md` and
  `Documentation/Plans/Fleet/MASTER_PLAN_Implementation_Prompts.md`, both now removed from
  `Documentation/Plans/Fleet/`.
- **Evidence:** Row 11 commit `9a5305424` (Prompt 45, `Api.Npc.RegisterMoveGuard` +
  `MoveGuardPatch`, collapsed into the pack by `a3c7ab227`); row 14 commit `5c28c03e4` (Prompt
  46, `GameQuery.IsInPlayerEnv` public plus the 3 call-site migrations in one commit, collapsed
  into the pack by `e01e911ef`); unrelated pre-existing gate fix found during verification,
  commit `6f0a0bc91` (`CMC-SilentExits.Tests.ps1`, 3 stale rows from the earlier StatAccess
  migration); citation sweep commits `6567bc510` (`.claude/playthrough-test-status.json`
  private-index fix) and `e01776032` (gate comment + `AUTHORING.md`). `python3 -c` count against
  `.claude/playthrough-test-status.json`'s `items` bucket for rows citing
  `Fleet_APE_Port_As_Built.md`: 4 pending, 0 dropped; `confirmedItems` bucket: 25 already
  confirmed. `git status --porcelain -- Documentation/Plans/Fleet/` empty after the move.
- **Verification debt:** 4 rows still `pending` in `.claude/playthrough-test-status.json`
  `items` as of this sweep: `cmc_homestead_kit_metal_nuggets`,
  `fw_mod_action_sounds_resolve_smithing_heard`, `fw_heated_metal_art_look_person`,
  `act_metal_gloves_art_look_person`. Per CLAUDE.md "code work never waits on a human
  playthrough," these do not block this archival; they remain open tracker rows for a future
  human playthrough pass to confirm.

### 2026-11-11 - MASTER_PLAN (audit remediation AF1), drained and archived
- **Verdict:** DRAINED. The plan held one row (AF1, the golden-hind build-output orphans), 0 feature rows and no pack. Checked part by part against disk on 2026-11-10 and 2026-11-11: build output clean, installed copy and release zip rebuilt clean at 0.11.11, both embedded suite zips and both Mod Update Manager DLLs clean, the live Nexus 0.11.10 file clean, the preflight orphan scan built (`dec663077`). 1 STALE-CLAIM corrected: the row's "unresolved `hw_encounter_goldenhind` reference" (no such card ever existed; the hind had no encounter by design).
- **Disposition:** ARCHIVE to `Documentation/Design/Harsh_Wilds_Audit_Remediation_2026-11_As_Built.md`. `Documentation/Plans/HarshWilds/` no longer exists.
- **Pruned:** this roadmap's three Phase 0 rows for C1 collapsed to one done line. `Documentation/Ideas/IDEAS_OVERVIEW.md` repointed; `.audit/summary.md`, `.audit/ideas.md` and the modwide critical-analysis given dated resolution lines; four `Documentation/Fleet_Review_Queue.md` checkboxes ticked. Dated records left as written.
- **Evidence:** `Documentation/Benches/HW_GoldenHind_Shipping_2026-11-10/` (two read-only scan scripts, raw results, a generated `SUMMARY.md`): installed folder 415 files, 0 golden-hind files; `Harsh_Wilds_0-11-11.zip` 416 entries, 0; the Nexus MAIN 0.11.10 manifest 416 paths, 0, its archive md5 equal to the recorded upload, with the control that the archived 0.11.2 to 0.11.4 manifests each list all five; HarshWilds `bin/Release` 409 files under subfolders, 0 with no source. Deploy: `Deploy-Mods.ps1 -CSFFMFW -HarshWilds`, 2026-11-10 10:48, the framework with it because 0.11.11 calls `Api.Localized` and the installed framework build lacked it. A four-agent read-only sweep on 2026-11-11 (local artifacts and rigs, git and the public repo, the Mothership and Harman over ssh, runtime residue) found nothing that ships still carrying the files; what it found and left is in the bench README. Rig 2's game log of 2026-11-11 01:52 loads 0.11.11 with no hind line. Preflight on HarshWilds with G20d: 0 CRITICAL, 0 WARNING.
- **Verification debt:** none added. The row named no in-game acceptance check; its runtime check was the Nexus file read. The `.audit/items-report.md` and `.audit/images-report.md` bodies still describe the removed files; re-running them is on the next `/fleet-review` pass item in `Documentation/Fleet_Review_Queue.md`.
