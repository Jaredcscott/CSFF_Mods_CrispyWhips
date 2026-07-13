# Mod_Update_Manager — Next Steps
Last updated: 2026-06-17
Current version: 2.1.1 | Score: 9/10 (audit 2026-06-17)

---

## What was done this session

All audit warnings that were safe to fix are resolved:

| Fix | File |
|-----|------|
| Deleted no-op `Start()` debug log line | `Plugin.cs` |
| Widened disk cache body regex to handle `{}` in summaries | `NexusApiClient.cs` |
| Removed `//`-comment header from auto-generated mappings JSON | `ModMappingManager.cs` |
| Removed `"herbs"`/`"fungi"` false-positive conflict patterns | `ConflictDetector.cs` |
| GameLoad patch: assembly-scan by name instead of `AccessTools.TypeByName` | `Patcher/GameLoadPatch.cs` |
| Scheduler: replaced `Time.deltaTime` accumulator with `DateTime.UtcNow` delta | `UpdateScheduler.cs` |

Build is clean (0 errors, 0 warnings, Release config).

---

## Remaining open items

### 1. No Release `<OutputPath>` in csproj (low priority / optional)
**File:** `Mod_Update_Manager.csproj`

Release builds land in `bin/Release/` and must be deployed via the deploy script. This is intentional — it prevents `dotnet build -c Release` from accidentally overwriting a live game install. No action needed unless the deploy script is being retired.

If you want parity with Debug (auto-deploy on build), add to the Release `<PropertyGroup>`:
```xml
<OutputPath>C:\Program Files (x86)\Steam\steamapps\common\Card Survival Fantasy Forest\BepInEx\plugins\Mod_Update_Manager\</OutputPath>
```

---

### 2. `isNetworkError`/`isHttpError` deprecated (deferred — Unity version gate)
**File:** `NexusApiClient.cs`

Both properties are deprecated in Unity 2020.2+. CSFF runs Unity 2019.4.41, so they are correct for the current build. When the game upgrades Unity, replace in `GetModInfoCoroutine()`, `ValidateApiKeyCoroutine()`, and `GetChangelogsCoroutine()`:
```csharp
// BEFORE (Unity 2019)
if (!request.isNetworkError && !request.isHttpError)

// AFTER (Unity 2020.2+)
if (request.result == UnityWebRequest.Result.Success)
```
Do not apply until the game's Unity version is confirmed ≥ 2020.2.

---

### 3. Publish to Nexus (when ready)
When the mod is published on Nexus:
1. Get the assigned Nexus mod ID.
2. In `KnownModRegistry.cs`, replace the `"self"` sentinel entries with the real ID:
   ```csharp
   // BEFORE
   { "Mod_Update_Manager", ("self", "Mod Update Manager") },
   { "ModUpdateManager",   ("self", "Mod Update Manager") },
   { "Mod Update Manager", ("self", "Mod Update Manager") },
   { "crispywhips.mod_update_manager", ("self", "Mod Update Manager") },

   // AFTER (example — use real ID)
   { "Mod_Update_Manager", ("38", "Mod Update Manager") },
   // etc.
   ```
3. Bump the version (use `/update-mod-version MUM <new_version>`).
4. Run `/export-to-repo MUM` to push to the public repo.

---

## Verify in-game before next release

The scheduler wall-clock change (`DateTime.UtcNow`) is a behavioral change — background check intervals now run on real time rather than in-game time. Verify:
- Background checking starts correctly after `OnGameDataLoaded()`.
- The "time until next check" display in the UI counts down at wall-clock pace (not affected by game pause).
- `ResetTimer()` (called after a manual check) correctly postpones the next scheduled check.

No Harmony patch was changed, so no BepInEx log verification is needed beyond the existing `Mod_Update_Manager v2.1.1 loaded.` startup line.
