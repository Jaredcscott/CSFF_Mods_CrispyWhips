# CSFFModFramework — Evaluation Report

**Date:** 2026-05-11  
**Reviewer:** Claude (automated deep-read of full source)  
**Version evaluated:** 2.0.5  

Findings are grouped by severity. Code references use `File.cs:line` format.

---

## Critical Bugs

### C1 — LogTiming dead branch (LoadOrchestrator.cs:223–230)

Both if/else branches log the **identical** string. The condition `ms >= 50` does nothing.

```csharp
if (ms >= 50)
    Log.Debug($"[Timing] {phase}: {ms}ms");
else
    Log.Debug($"[Timing] {phase}: {ms}ms");
```

The likely intent was to log slow phases at `Warn` (or only log phases above the threshold at all). As written, the `if` branch is dead code and every phase always logs at `Debug`. No regression in behavior, but performance regressions in slow phases will only surface when VerboseLogging is on — defeating the purpose of the 15 s warning threshold that follows.

**Fix:** Choose the intended behavior and remove the dead branch. Two options:
```csharp
// Option A: only log phases that took ≥ 50ms
if (ms >= 50) Log.Debug($"[Timing] {phase}: {ms}ms");

// Option B: promote slow phases to Warn so they appear in default logs
if (ms >= 200) Log.Warn($"[Timing SLOW] {phase}: {ms}ms");
else Log.Debug($"[Timing] {phase}: {ms}ms");
```

---

### C2 — `FrameworkEvents.GameDataReady` fires even when `LoadOrchestrator.Execute()` fails (GameLoadPatch.cs:19–23)

```csharp
static void Postfix()
{
    LoadOrchestrator.Execute();            // may throw or return early
    Api.FrameworkEvents.RaiseGameDataReady(); // always fires
}
```

`LoadOrchestrator.Execute()` catches all exceptions internally and returns normally regardless of success. If loading fails midway, mod content is partially registered. `RaiseGameDataReady` then fires, telling subscribers the data is ready when it isn't. Subscribers that call `GameContent.Find<T>()` will get partial or null results with no diagnostic signal.

Meanwhile, `FrameworkEvents.RaiseLoaded()` is only called at the end of a successful `Execute()` path, so that event correctly does NOT fire on failure. The inconsistency means subscribers to `Loaded` know about failures but subscribers to `GameDataReady` don't.

**Fix:** Either guard `RaiseGameDataReady` behind a success flag, or add a `FrameworkEvents.LoadFailed` event:
```csharp
static void Postfix()
{
    LoadOrchestrator.Execute();
    if (LoadOrchestrator.LoadSucceeded)
        Api.FrameworkEvents.RaiseGameDataReady();
}
```

---

## Moderate Issues

### M1 — PerkInjector re-reads JSON files from disk (PerkInjector.cs:25)

```csharp
var json = File.ReadAllText(file);
```

`JsonDataLoader` already loaded, parsed, and cached every perk JSON in `JsonDataLoader.JsonByUniqueId`. PerkInjector ignores that cache and reads every file again. This violates the documented performance rule ("NEVER re-read mod JSON from disk in downstream services") and adds unnecessary I/O in the path where perk injection happens.

**Fix:**
```csharp
// Instead of scanning the filesystem, iterate the already-loaded cache:
foreach (var kvp in JsonDataLoader.JsonByUniqueId)
{
    var uid = kvp.Key;
    var json = kvp.Value;
    var groupId = PathUtil.QuickExtractString(json, "CharacterPerkPerkGroup");
    // ... only add if this is a CharacterPerk (check ParsedJsonByUniqueId for type)
}
```

---

### M2 — BlueprintInjector Priority 2 re-reads JSON files from disk (BlueprintInjector.cs:38–55)

Same problem as M1. When `BlueprintTabs.json` is absent, the injector scans `CardData/` directories and reads every JSON file with `File.ReadAllText`. The content is already in `JsonDataLoader.JsonByUniqueId`.

**Fix:** Same approach — iterate `JsonDataLoader.ParsedJsonByUniqueId` and filter by `CardType == 7`.

---

### M3 — BlueprintInjector.LoadFromTabsConfig uses a hand-rolled JSON parser instead of MiniJson (BlueprintInjector.cs:76–119)

The parser does positional `IndexOf` string scanning. It cannot handle:
- Escaped quotes (`\"`) inside key or value strings
- Object values before the array (parser looks for the next `[` after the key, which could be inside a different field)
- Keys that appear out of order in the JSON
- Any whitespace or comment variations

`MiniJson` is already available in the project and used everywhere else. The only reason to hand-roll is to avoid a dependency — but MiniJson is an internal utility, not external.

**Fix:**
```csharp
if (MiniJson.Parse(json) is not Dictionary<string, object> root) return;
foreach (var kvp in root)
{
    if (kvp.Value is not List<object> ids) continue;
    foreach (var id in ids)
        if (id is string bpId) _queued.Add((bpId, kvp.Key, null));
}
```

---

### M4 — GameRegistry.AllData caches null permanently after a transient early call (GameRegistry.cs:143–181)

```csharp
if (_allDataCached != null) return _allDataCached;
if (_allDataInitAttempted) return null;  // permanent null after any failure
_allDataInitAttempted = true;
```

If any service calls `AllData` before `GameLoad.Instance` is ready (e.g., very early in `Awake`), resolution fails and `_allDataInitAttempted` is permanently set. Every subsequent call returns `null` even after `GameLoad.Instance` is populated.

This is a silent total failure: mods produce no content, no error is logged at call sites, and the only clue is the single Warn in `LoadOrchestrator` that `GetAllData()` returned nothing.

**Fix:** Drop the `_allDataInitAttempted` guard or add a retry:
```csharp
// Allow re-attempt if cached result is null
if (_allDataCached != null) return _allDataCached;
```

---

### M5 — LoadOrchestrator single try-catch silently skips all remaining phases on failure (LoadOrchestrator.cs:33–221)

The entire 27-phase sequence is wrapped in one try-catch at the outer level. Each service has internal error handling, but if a service throws *outside* its own catch (e.g., from an unexpected null in service initialization before its try-catch), the outer catch logs one error and exits. All phases after the failing one are skipped.

The framework then appears to be "running" (Plugin.Update fires, BepInEx shows no crash), but `FrameworkEvents.Loaded` never fires and all mod content is absent. There is no way for downstream mods to detect this state.

**Fix:** Give each major phase its own try-catch that logs which phase failed and continues:
```csharp
RunPhase("WarpResolver", () => WarpResolver.ResolveAll(allData, mods));
RunPhase("NullReferenceCompactor", () => NullReferenceCompactor.CompactAll(allData));
// ...

static void RunPhase(string name, Action phase)
{
    try { phase(); }
    catch (Exception ex) { Log.Error($"Phase '{name}' failed: {ex}"); }
}
```

---

### M6 — Bare `catch { }` in WarpResolver hides Activator.CreateInstance errors (WarpResolver.cs:283)

```csharp
try
{
    var newElem = Activator.CreateInstance(elemType);
    Walk(newElem, elemDict);
    newElems.Add(newElem);
}
catch { }
```

If `Activator.CreateInstance` fails (abstract type, no parameterless constructor, etc.), the error is silently eaten. Authors get no feedback that their WarpType 4/6 dict-element injection is failing. The outer "unresolved" counters don't capture this because the failure happens before `Lookup()`.

**Fix:**
```csharp
catch (Exception ex)
{
    Log.Debug($"WarpResolver: failed to create {elemType.Name} for {_currentUid}: {Log.ExceptionText(ex)}");
}
```

---

### M7 — `GameLoadPatch` silently does nothing if both GameLoad method targets fail (GameLoadPatch.cs:11–13)

```csharp
if (!SafePatcher.TryPatch(harmony, "GameLoad", "LoadMainGameData", postfix: postfix))
    SafePatcher.TryPatch(harmony, "GameLoad", "LoadGameFilesData", postfix: postfix);
```

Both `TryPatch` calls log their own warn when they fail, but the `Postfix()` (which calls `Execute()`) is never registered. The framework silently never loads. The Warn messages are at Debug level in SafePatcher, which means they're hidden unless VerboseLogging is on.

Actually, `SafePatcher.TryPatch` logs at `Log.Warn` on failure — but the user won't see a Warn-level message saying "framework never started." All they see is that mods produced no content.

**Fix:** Add a final fallback log at `Error` level if both fail:
```csharp
var patched = SafePatcher.TryPatch(harmony, "GameLoad", "LoadMainGameData", postfix: postfix)
           || SafePatcher.TryPatch(harmony, "GameLoad", "LoadGameFilesData", postfix: postfix);
if (!patched)
    Log.Error("CSFFModFramework: failed to hook GameLoad — mod content will NOT load. Check for game version mismatch.");
```

---

### M8 — `_loaded` flag is never reset; new-game reloads skip framework loading (LoadOrchestrator.cs:30–31)

```csharp
private static bool _loaded = false;

internal static void Execute()
{
    if (_loaded) return;
    _loaded = true;
```

If the game triggers `LoadMainGameData` again (returning to main menu and starting a new game), the framework skips all loading on the second invocation. New mod content added to AllData in the first session persists, but `WarpResolver`, `PerkInjector`, and others don't re-run against any freshly created ScriptableObjects. Whether this matters depends on whether the game recreates its ScriptableObject graph between plays.

If this is a known non-issue (the graph persists across new games in this Unity build), the guard comment should say so explicitly. If it IS an issue, the flag needs to be reset — but that requires all static caches in Database, WarpResolver, GameRegistry, etc. to also be cleared.

**Recommendation:** Add a code comment clarifying the intent. If multi-load ever becomes needed, the reset path is complex and would need a full audit of all static state.

---

## Minor Issues

### m1 — `HasAudio` probes with `"*.*"` pattern matching any file extension (ModDiscovery.cs:115)

```csharp
mod.HasAudio =
    HasDeclaredAssets(mod.Assets?.Audio) ||
    HasAnyFile(Path.Combine(dir, "Resource", "Audio"), new[] { "*.*" }, SearchOption.AllDirectories);
```

Any file in `Resource/Audio/` — including `README.txt`, `.gitkeep`, or stray build artifacts — sets `HasAudio = true`, causing `AudioLoader.LoadAll` to run and scan the directory. This is harmless (AudioLoader silently skips non-audio files) but wastes time.

**Fix:** Use `new[] { "*.wav", "*.mp3", "*.ogg" }` (or whatever Unity supports).

---

### m2 — `GifLoader.LoadAll` always runs; no `HasGif` short-circuit (LoadOrchestrator.cs:57–59)

Every other optional phase uses a `HasXxx` guard. GifLoader just runs unconditionally, adding overhead for mods without GIFs. The comment says "No-op when no mod ships GIF content" but the no-op check is inside GifLoader, not before calling it.

**Fix:** Either add `mod.HasGifContent` to `ModManifest` and `ProbeFeatures`, or document the unconditional call as intentional.

---

### m3 — `SpriteTextureCache.GetCachePath` creates a new MD5 instance on every call (SpriteTextureCache.cs:605–612)

```csharp
private static string GetCachePath(string pngPath)
{
    using var md5 = MD5.Create();
    ...
}
```

Called once per sprite lookup that misses the bundle. For a cold first load with 200 sprites, that's 200 MD5 instance creations. `MD5.Create()` allocates a managed crypto object. Could use a thread-static or pooled instance.

**Fix:** `[ThreadStatic] private static MD5 _md5;` then `(_md5 ??= MD5.Create()).ComputeHash(...)`.

---

### m4 — `ReflectionCache.GetField` does not cache null (ReflectionCache.cs:133) while `WarpResolver.CachedField` does (WarpResolver.cs:25–29)

Inconsistent null-caching policy between two similar field caches in the same project. `ReflectionCache.GetField` retries missing fields on every call (no null caching). `WarpResolver.CachedField` caches nulls and never retries.

Neither is wrong given their contexts, but the inconsistency is confusing. The code comments claim "null stays uncached so retries can succeed" in ReflectionCache and the WarpResolver comment says "stores nulls so absent-field lookups don't keep paying either" — opposite philosophies with no explanation of why one context warrants retry and the other doesn't.

**Recommendation:** Add a comment to each explaining the rationale.

---

### m5 — `FrameworkDirtyTracker` is not thread-safe (FrameworkDirtyTracker.cs:20)

`_dirty` is a `HashSet<UniqueIDScriptable>` with no locking. Currently all framework services run on Unity's main thread, so this is safe in practice. If any future service uses `Task.Run` and calls `MarkDirty`, it will corrupt the set.

**Recommendation:** Use `ConcurrentDictionary` (as a set via `TryAdd`) or lock around mutations.

---

### m6 — `SpriteTextureCache` bundle write is not atomic on Windows (SpriteTextureCache.cs:589–591)

```csharp
if (File.Exists(bundlePath))
    File.Delete(bundlePath);
File.Move(tmp, bundlePath);
```

There is a brief window between `File.Delete` and `File.Move` where `bundle.bin` does not exist. A crash in that window leaves the next launch with no bundle (falls back to per-file caches, which is recoverable, but the first load after crash will be slow). On Windows, `File.Replace` can do this atomically:

```csharp
if (File.Exists(bundlePath))
    File.Replace(tmp, bundlePath, null);  // atomic on NTFS
else
    File.Move(tmp, bundlePath);
```

---

### m7 — `GameSourceModifier.ApplyPatch` calls `JsonUtility.FromJsonOverwrite` on vanilla objects without protection against field zeroing

`FromJsonOverwrite` will zero any array or collection field that is present in the JSON with an empty value, and it will NOT zero fields absent from the JSON. This is the correct and expected behavior. However, a malformed `GameSourceModify` patch that accidentally includes an empty `"CardInteractions": []` will silently erase all vanilla interactions on the target card.

There is no validation or safeguard in the framework. Authors get no warning.

**Recommendation:** After `FromJsonOverwrite`, log a debug summary of which fields changed (compare field values before and after). This would help mod authors detect accidental zeroing.

---

## Design / Architecture Observations

### D1 — Public API has no versioning strategy

`Api/` classes are `public static` with no version guard. If a downstream mod references `Api.GameContent.Find<T>()` and the method signature changes, the downstream mod breaks at runtime with `MissingMethodException`. There is no semantic versioning check or compatibility shim.

**Recommendation:** Add an `Api.FrameworkVersion` constant and document which API surfaces are stable vs. experimental.

---

### D2 — `ContentRegistry.Register(UniqueIDScriptable)` is silent on failure

```csharp
public static bool Register(UniqueIDScriptable so)
{
    if (so == null) return false;
    var newlyRegistered = GameRegistry.TryRegister(so);
    GameRegistry.TryAddToAllData(so);
    return newlyRegistered;
}
```

Returns `false` on both "already registered" (normal dedup) and "registration failed" (error). Callers can't distinguish silent dedup from an error. CLAUDE.md recommends "log at every null check in spawn chains" — the same principle should apply here.

---

### D3 — `LoadOrchestrator._loaded` is `private static`, not `internal`, blocking testability

The `_loaded` guard means re-runs are impossible without reflection hacks. For unit-testing individual phases in isolation, this static global state is a barrier. Consider extracting phases into an instance class or exposing a test-only reset path.

---

### D4 — No mechanism to query which mods are loaded before `FrameworkEvents.Loaded` fires

Mods subscribing to `FrameworkEvents.Loaded` get the event but can't call `Api.ModRegistry.All` during their `Awake` (it's populated during `LoadOrchestrator.Execute`, which happens later). The API is correctly documented, but there's no defensive check that logs a clear error if `ModRegistry.All` is accessed too early.

---

## Summary Table

| ID | Severity | Location | Description |
|----|----------|----------|-------------|
| C1 | Critical | `LoadOrchestrator.cs:223` | LogTiming if/else both log identical string — dead branch |
| C2 | Critical | `GameLoadPatch.cs:22` | `GameDataReady` fires even when framework loading fails |
| M1 | Moderate | `PerkInjector.cs:25` | Re-reads JSON from disk; violates cache-reuse rule |
| M2 | Moderate | `BlueprintInjector.cs:38` | Re-reads JSON from disk (Priority 2 fallback); same violation |
| M3 | Moderate | `BlueprintInjector.cs:72` | Hand-rolled JSON parser instead of existing MiniJson |
| M4 | Moderate | `GameRegistry.cs:147` | AllData caches null permanently after any early transient failure |
| M5 | Moderate | `LoadOrchestrator.cs:33` | Single outer catch — one phase failure skips all subsequent phases |
| M6 | Moderate | `WarpResolver.cs:283` | Bare `catch { }` swallows Activator.CreateInstance errors silently |
| M7 | Moderate | `GameLoadPatch.cs:11` | No Error-level log when both GameLoad hook targets fail |
| M8 | Moderate | `LoadOrchestrator.cs:30` | `_loaded` flag never resets; second GameLoad call skips everything |
| m1 | Minor | `ModDiscovery.cs:115` | `HasAudio` uses `"*.*"` — matches non-audio files |
| m2 | Minor | `LoadOrchestrator.cs:57` | GifLoader always runs; no HasGif short-circuit like other phases |
| m3 | Minor | `SpriteTextureCache.cs:605` | MD5 instance created per call; could be pooled |
| m4 | Minor | `ReflectionCache.cs:133` / `WarpResolver.cs:25` | Inconsistent null-caching policy with no explanation |
| m5 | Minor | `FrameworkDirtyTracker.cs:20` | HashSet not thread-safe; latent risk if Tasks are added |
| m6 | Minor | `SpriteTextureCache.cs:589` | Non-atomic bundle replace on Windows; use `File.Replace` |
| m7 | Minor | `GameSourceModifier.cs:99` | No guard against accidental field zeroing via `FromJsonOverwrite` |
| D1 | Design | `Api/` | No API versioning strategy |
| D2 | Design | `ContentRegistry.cs:33` | Register() returns false for both dedup and error — indistinguishable |
| D3 | Design | `LoadOrchestrator.cs:12` | `_loaded` static global blocks testability |
| D4 | Design | `Api/ModRegistry.cs` | No guard/log when ModRegistry is queried before it's populated |

---

## What Is Working Well

- **WarpResolver** is thorough and handles Array, List\<T\>, value types, struct write-back, WarpType 3/4/5/6, nested walks, and runtime tag creation. The field cache is well-designed.
- **SpriteTextureCache** two-layer strategy (bundle + per-file) with MD5 rescue on mtime mismatch is robust and performant.
- **NullReferenceCompactor** type-graph analysis (`TypeHasUnityCollection`) prunes the walk efficiently and the dirty-set mechanism avoids the full sweep after injection passes.
- **FrameworkDirtyTracker** drain-and-clear pattern correctly separates first-pass and second-pass compaction.
- **GameLoadPatch** two-target fallback (`LoadMainGameData` → `LoadGameFilesData`) shows good defensive thinking.
- **PerkInjector** per-type `FieldInfo` cache correctly handles `PerkTabGroup` vs `PerkGroup` type difference (documented pitfall from CLAUDE.md).
- **ContentRegistry** additive first-wins coexistence model is correct and the comment explains why.
- **ModDiscovery** content-count dedup (not mtime dedup) is the right heuristic for the stale-folder problem.
- **FrameworkEvents** per-subscriber try-catch prevents one bad subscriber from blocking others.
- **SafePatcher** wraps Harmony calls with logging and graceful failure — no hard crash on a missing method.
