# Community Mod Chest — Module Notes

These notes cover CMC-specific subsystems. Also read the root `CLAUDE.md` for all general rules.

## WorldMap System
Full architecture: `Documentation/CSFF_Map_Travel_System.md`.

### WorldMap Travel DA Injection (CRITICAL)
- **Strip `RequiredStatValues` from cloned travel DAs** — inherited Light/Stamina gate hides the button until dawn (red X all night). Set to empty array/list after cloning. Red X = gated DA; absent slot = `HiddenOnInGameMap` issue — don't confuse them.
- **To suppress a direction button: remove the DA entirely** — `DontShowOnCompass=true` does NOT hide it (only reparents the widget).
- `"Bidirectional": true` on a `VanillaExits` entry requires `"TargetVanillaLocUID"` for the framework to inject the reverse DA.
- Full detail: `Documentation/CSFF_Map_Travel_System.md` § Travel DA Injection. See memory: `reference_worldmap_travel_da_injection`.

### WorldMap Clone Env Nodes (CRITICAL)
- **NEVER `AlwaysUpdate=true` on CT4/CT8 clone** — card follows player onto destination board → self-loop travel softlock. `CardCloneService.CloneCard` resets to `false`; fixed CSFFMFW v2.7.4.
- **NEVER clone an instanced env** (`InstancedEnvironment==true`, e.g. `Env_Cabin`) — silent stack overflow crash on every env transition. Always clone outdoor envs.
- Full detail: `Documentation/CSFF_Map_Travel_System.md` § Clone Env Nodes. See memory: `reference_alwaysupdate_env_node_follow`, `reference_instanced_env_not_map_node`.

### WorldMap Clone Env Board Seeding (CRITICAL)
- **Env watch must spawn CT8 ONLY** — `ExtraDropUIDs` accumulate +N per entry (guard is always false). Failed and reverted twice — do NOT re-add.
- **Env UIDs MUST NOT contain underscores** — `LoadCard` splits UID on `_` and silently fails. Use camelCase: `"actTinCaveEnv"` not `"act_env_tin_cave"`. Sweep ALL mods when found.
- **Symptom fingerprint:** CT8 takes ~1s to appear + capacity bars start full then drain to 0 → check UID for `_` first.
- Full detail: `Documentation/CSFF_Map_Travel_System.md` § Board Seeding. See memory: `reference_clone_env_board_seeding`.

### WorldMap Node Coordinates & Naming (CRITICAL)
- **Check `DefaultWorldMap.json` for a free adjacent cell before authoring `Coords`** (10-unit grid) — collisions corrupt map rendering. Confirm via `python3`. `TravelDirection` must match geometric relationship.
- **Clone node DisplayName must match the cloned template's actual trees/terrain** — feature-honesty violation otherwise.
- Full detail: `Documentation/CSFF_Map_Travel_System.md` § Node Coordinates.

### WorldMap Clone Node Env Capacity Stats
Env capacity bars (Trees/Overgrowth/Foraging/Fertility) are `SpecialDurability1–4` on the CT8 card — **NOT `GameStat`s**. `StatModifier` / `PassiveStatEffects` have zero effect. Patch via C# reflection at `LoadMainGameData` postfix time. Full field list: `Documentation/CSFF_Map_Travel_System.md` § Clone Env Capacity Stats. See memory: `reference_worldmap_clone_env_stats`.

## Encounter Guards
Prefer `Api.EncounterGuards` or `EncounterGuards/*.json` over direct patching.
When a C# patch is needed: target `EncounterPopup.StartEncounter(Encounter, InGameNPC, bool)` (3-param overload). `_WithNPC == null` → wildlife; `!= null` → NPC (**never suppress NPC encounters**). Return `false` from prefix to cancel. `CardEnvironment.MatchesPlayerEnv` for env check.
