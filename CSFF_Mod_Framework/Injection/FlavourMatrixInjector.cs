using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Activates <see cref="FlavourSystemRules.FlavourMatrix"/> — the vanilla flavour-synergy pairwise
/// table (<see cref="FlavourCombination"/>: <c>A</c> + <c>B</c> + <see cref="FlavourSynergies"/>)
/// that <c>FlavourSystemRules.GetScoreForCombination</c> consults when the game scores a meal's
/// flavour profile (feeds <c>FlavourBonusReport.SynergyScore</c>/<c>.Feedback</c>, surfaced in the
/// flavour feedback UI). Individual <see cref="FlavourTag"/> definitions need none of this — they
/// ARE <c>UniqueIDScriptable</c> and self-activate through the standard JsonDataLoader → AllData
/// path; only the pairwise synergy table is a dormant activation gap.
///
/// <para><b>Why a boot postfix, not GameSourceModify or a load-time AllData injector.</b>
/// <see cref="FlavourSystemRules"/> is a plain <c>ScriptableObject</c> — NOT <c>UniqueIDScriptable</c>
/// — reached only via the single Inspector-wired <c>GameManager.FlavourRules</c> field; nothing in
/// the decompiled game assigns it by code, and it is not resident in <c>DataBase.AllData</c> at
/// <c>LoadMainGameData</c> time (verified against <c>.decomp/GameManager.cs</c> — no assignment site
/// exists for the field). A <c>GameSourceModify/</c> patch targeting it would silently no-op every
/// boot (<c>"GameSourceModify: no object found for 'FlavourSystemRules'"</c>) — the same scene-scoped
/// -SO limitation documented for <c>StatListTab</c> in root CLAUDE.md §GameSourceModify. The fix
/// mirrors <c>Community_Mod_Chest/Patcher/StatTabInjectionPatch.cs</c>: a Harmony postfix on
/// <c>GameManager.InitializeStatsAndActions</c>, re-resolved every boot, idempotent.
///
/// <para><b>Two-phase design.</b> <see cref="LoadAll"/> runs once during the framework's normal
/// declarative-content pass (mirrors <c>EncounterGuardLoader</c>): scans every mod's
/// <c>FlavourMatrix/*.json</c>, resolves each <c>TagA</c>/<c>TagB</c> UID against the already-UID
/// -registered <see cref="FlavourTag"/> instances (safe here — <c>JsonDataLoader</c> registers every
/// UID object into <see cref="GameRegistry"/> during its own materialize pass, well before this phase
/// runs), and caches the resolved triples — this is the ONE JSON read per boot. <see cref="ApplyPatch"/>
/// then installs the Harmony postfix. Unlike <c>StatListTab</c> (9 loaded assets CMC had to
/// disambiguate by name via a <c>Resources.FindObjectsOfTypeAll</c> scan), <c>GameManager.FlavourRules</c>
/// is a single public field naming the EXACT instance the running game uses — the postfix reads
/// <c>__instance.FlavourRules</c> directly every call (no scan needed) and appends each cached pair
/// only when <c>HasPair</c> is still false, so re-boots and any asset reload between runs are safe.</para>
/// </summary>
internal static class FlavourMatrixInjector
{
    private readonly struct SynergyPair
    {
        public readonly FlavourTag A;
        public readonly FlavourTag B;
        public readonly FlavourSynergies Synergy;

        public SynergyPair(FlavourTag a, FlavourTag b, FlavourSynergies synergy)
        {
            A = a;
            B = b;
            Synergy = synergy;
        }
    }

    // Resolved once at load time (LoadAll); re-applied idempotently on every
    // InitializeStatsAndActions call (ApplyPatch's postfix). Never re-read from disk after LoadAll.
    private static readonly List<SynergyPair> _pairs = new();
    private static bool _patchApplied;
    private static bool _warnedRulesMissing;

    /// <summary>
    /// Scans <c>&lt;ModFolder&gt;/FlavourMatrix/*.json</c> across all mods and resolves each pair's
    /// FlavourTag UIDs. Call once, after JsonDataLoader has registered every mod's (and vanilla's)
    /// FlavourTag instances into <see cref="GameRegistry"/> — resolving by UID here is safe and
    /// avoids re-parsing JSON later inside the per-boot postfix.
    ///
    /// Schema (one pair per file, parsed with MiniJson — never JsonUtility):
    /// <code>
    /// {
    ///   "TagA": "&lt;FlavourTag UID&gt;",
    ///   "TagB": "&lt;FlavourTag UID&gt;",
    ///   "Synergy": "Good"   // FlavourSynergies: "Good" | "Neutral" | "Bad" (default "Neutral")
    /// }
    /// </code>
    /// </summary>
    public static void LoadAll(List<ModManifest> mods)
    {
        _pairs.Clear();
        int filesLoaded = 0, filesSkipped = 0;

        foreach (var mod in mods)
        {
            var dir = Path.Combine(mod.DirectoryPath, "FlavourMatrix");
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if (MiniJson.Parse(File.ReadAllText(file)) is not Dictionary<string, object> root)
                    {
                        Log.Warn($"[FlavourMatrixInjector] {mod.Name}/{Path.GetFileName(file)}: not a JSON object — skipped.");
                        filesSkipped++;
                        continue;
                    }

                    var tagAUid = root.TryGetValue("TagA", out var a) ? a as string : null;
                    var tagBUid = root.TryGetValue("TagB", out var b) ? b as string : null;
                    if (string.IsNullOrEmpty(tagAUid) || string.IsNullOrEmpty(tagBUid))
                    {
                        Log.Warn($"[FlavourMatrixInjector] {mod.Name}/{Path.GetFileName(file)}: missing TagA/TagB — skipped.");
                        filesSkipped++;
                        continue;
                    }

                    var tagA = GameRegistry.GetByUid<FlavourTag>(tagAUid);
                    var tagB = GameRegistry.GetByUid<FlavourTag>(tagBUid);
                    if (tagA == null || tagB == null)
                    {
                        Log.Warn($"[FlavourMatrixInjector] {mod.Name}/{Path.GetFileName(file)}: unresolved FlavourTag "
                            + $"('{tagAUid}'={(tagA == null ? "MISSING" : "ok")}, '{tagBUid}'={(tagB == null ? "MISSING" : "ok")}) — skipped.");
                        filesSkipped++;
                        continue;
                    }
                    if (tagA == tagB)
                    {
                        Log.Warn($"[FlavourMatrixInjector] {mod.Name}/{Path.GetFileName(file)}: TagA == TagB ('{tagAUid}') "
                            + "— FlavourCombination.IsValid() requires distinct tags, skipped.");
                        filesSkipped++;
                        continue;
                    }

                    var synergy = FlavourSynergies.Neutral;
                    if (root.TryGetValue("Synergy", out var sVal))
                    {
                        if (sVal is string sStr && Enum.TryParse(sStr, ignoreCase: true, out FlavourSynergies parsed))
                            synergy = parsed;
                        else if (sVal is double sNum)
                            synergy = (FlavourSynergies)(int)sNum;
                        else
                            Log.Warn($"[FlavourMatrixInjector] {mod.Name}/{Path.GetFileName(file)}: unrecognized Synergy value "
                                + $"'{sVal}' — defaulting to Neutral (valid: Good/Neutral/Bad).");
                    }

                    _pairs.Add(new SynergyPair(tagA, tagB, synergy));
                    filesLoaded++;
                    Log.Debug($"[FlavourMatrixInjector] {mod.Name}: queued {tagAUid} + {tagBUid} = {synergy}");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[FlavourMatrixInjector] failed to load {file}: {Log.ExceptionText(ex)}");
                    filesSkipped++;
                }
            }
        }

        if (filesLoaded > 0 || filesSkipped > 0)
            Log.Debug($"[FlavourMatrixInjector] resolved {filesLoaded} synergy pair(s) from JSON ({filesSkipped} skipped).");
    }

    /// <summary>
    /// Installs the boot postfix on <c>GameManager.InitializeStatsAndActions</c>. Call AFTER
    /// <see cref="LoadAll"/> (LoadOrchestrator calls both back-to-back) — self-no-ops (zero patch,
    /// zero per-boot cost) when no mod shipped any FlavourMatrix/*.json.
    /// </summary>
    public static void ApplyPatch(Harmony harmony)
    {
        if (_patchApplied) return;
        if (_pairs.Count == 0) return; // nothing to inject — skip patch registration entirely
        _patchApplied = true;

        try
        {
            var target = AccessTools.Method(typeof(GameManager), "InitializeStatsAndActions");
            if (target == null)
            {
                Log.Warn("[FlavourMatrixInjector] GameManager.InitializeStatsAndActions not found — synergy pairs will not be injected.");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(FlavourMatrixInjector), nameof(InitStats_Postfix)));
            Log.Debug($"[FlavourMatrixInjector] patched GameManager.InitializeStatsAndActions ({_pairs.Count} pair(s) queued).");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FlavourMatrixInjector] failed to patch GameManager.InitializeStatsAndActions: {Log.ExceptionText(ex)}");
        }
    }

    // Fires every GameManager.Awake (new game AND continue) — InitializeStatsAndActions is called
    // unconditionally before the branch-specific save/new-game logic, so this runs every boot.
    private static void InitStats_Postfix(GameManager __instance)
    {
        if (__instance == null) return;
        try
        {
            var rules = __instance.FlavourRules;
            if (rules == null)
            {
                if (!_warnedRulesMissing)
                {
                    _warnedRulesMissing = true;
                    Log.Warn("[FlavourMatrixInjector] GameManager.FlavourRules is null — synergy pairs cannot be injected this boot.");
                }
                return;
            }

            rules.FlavourMatrix ??= new List<FlavourCombination>();

            int appended = 0;
            foreach (var pair in _pairs)
            {
                if (rules.HasPair(pair.A, pair.B)) continue; // already present — idempotent across re-boots
                rules.FlavourMatrix.Add(new FlavourCombination(pair.A, pair.B) { Synergy = pair.Synergy });
                appended++;
                Log.Debug($"[FlavourMatrixInjector] appended {pair.A.UniqueID} + {pair.B.UniqueID} = {pair.Synergy}");
            }

            if (appended > 0)
                Log.Info($"[FlavourMatrixInjector] appended {appended} synergy pair(s) to FlavourMatrix.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FlavourMatrixInjector] InitStats_Postfix failed: {Log.ExceptionText(ex)}");
        }
    }
}
