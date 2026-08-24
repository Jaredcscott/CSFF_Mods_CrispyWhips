using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Injection;

/// <summary>
/// Activation surface for <see cref="GameModifierPackage"/> in the STANDALONE (character-independent)
/// case: appends mod-declared packages to <c>GameManager.CurrentModifierPackages</c> on every NEW
/// game, so a challenge/total-conversion mod can apply <c>StartingStatModifiers</c> +
/// <c>AddedCards</c> without also shipping a whole <see cref="PlayerCharacter"/> for the player to
/// pick. Declarative: <c>&lt;ModFolder&gt;/Modifiers.json</c>, key <c>AutoApplyPackages</c>.
///
/// <para><b>The character-linked path needs none of this.</b> A package referenced from a character's
/// <c>EasyPackageWarpData</c> already works end-to-end with zero framework code —
/// <see cref="GameModifierPackage"/> IS a <c>UniqueIDScriptable</c>, so it reaches
/// <c>DataBase.AllData</c> through the standard JsonDataLoader path and resolves as WarpData like any
/// other reference (see <c>CharacterRosterInjector</c>'s note, and the cookbook section
/// "GameModifierPackage — character-linked" in <c>Documentation/CSFF_Patterns.md</c>). The only gap
/// was activation WITHOUT a character selection.</para>
///
/// <para><b>Why a MainMenu postfix, not a load-time AllData injector.</b> Unlike
/// <c>PerkInjector</c>/<c>BlueprintInjector</c>, there is no collection in <c>AllData</c> to append
/// to at load time. The consuming list — the static <c>GameManager.CurrentModifierPackages</c>
/// (<c>.decomp/GameManager.cs:90</c>) — does not exist yet when <c>LoadMainGameData</c> (and the
/// framework's <c>LoadOrchestrator</c>) runs at launch; it is created only in
/// <c>MainMenu.StartGame</c> (<c>.decomp/MainMenu.cs:1362-1363</c>:
/// <c>CurrentModifierPackages = new List&lt;GameModifierPackage&gt;(); ...AddRange(SelectedPackages);</c>)
/// immediately before <c>SaveData.StartNewGame</c> loads the game scene. Its consumption is a
/// one-shot, new-game-only application reached from <c>GameManager.Awake</c>:
/// <c>StartingStatModifiers</c> applies inside <c>InitializeStatsAndActions()</c> (called from
/// <c>Awake</c> at <c>GameManager.cs:2385</c>; the modifiers themselves at
/// <c>GameManager.cs:2984-3004</c>, gated <c>!CurrentGameData.HasStatsData &amp;&amp;
/// (CurrentGamemode || CurrentPlayerCharacter)</c>, <c>GameManager.cs:2944-2954</c>) — NOT directly
/// in <c>Awake</c> itself. <c>AddedCards</c> applies via a separate call, <c>InitializeModifierPackages</c>
/// (<c>GameManager.cs:3466-3475</c>, reached only from the <c>!CurrentGameData.HasCardsData</c>
/// branch at <c>GameManager.cs:2427-2430</c>, which IS directly in <c>Awake</c>). Appending in a <c>MainMenu.StartGame</c> POSTFIX
/// therefore lands in the exact window between "list finalized" and "list applied", and rides the
/// same vanilla code that applies character-selected packages — no reimplementation of stat/card
/// application, and the save round-trip (<c>.decomp/GameLoad.cs:644-648</c> writes the UIDs into
/// <c>CurrentGameData.ModifierPackages</c>; <c>GameManager.cs:2203-2221</c> restores them by
/// <c>GetFromID</c> on every later boot) works unchanged.</para>
///
/// <para><b>Scope / boundaries (deliberate).</b> (1) NEW GAMES ONLY — installing an auto-apply mod
/// into an existing save applies nothing, because vanilla only ever consumes this list on a fresh
/// game; a game STARTED with the mod keeps the package in its save. (2) NO PLAYER OPT-IN/OPT-OUT —
/// every listed package applies to every new game regardless of character. A player-facing
/// "pick any package" character-creation UI was considered and explicitly deferred as out of
/// framework scope (new MainMenu UI, not engine support). (3) ADDITIVE with the character-linked
/// path — a character's own <c>EasyPackage</c> still applies; a package that is BOTH selected and
/// auto-applied is added once (dedup by reference below), never twice.</para>
///
/// <para><b>Two-phase design</b> (mirrors <c>FlavourMatrixInjector</c>): <see cref="LoadAll"/> reads
/// every <c>Modifiers.json</c> ONCE at load time and resolves each UID against the already-registered
/// <see cref="GameRegistry"/> entries; <see cref="ApplyPatch"/> then installs the Harmony patch pair.
/// The postfix never touches disk or the registry.</para>
/// </summary>
internal static class ModifierPackageInjector
{
    // Resolved once at load time (LoadAll); read by the MainMenu.StartGame postfix on every
    // new-game start. Never re-read from disk after LoadAll.
    private static readonly List<GameModifierPackage> _autoApply = new();
    private static bool _patchApplied;

    /// <summary>
    /// Scans <c>&lt;ModFolder&gt;/Modifiers.json</c> across all mods and resolves the listed
    /// <see cref="GameModifierPackage"/> UIDs. Call once, after JsonDataLoader has registered every
    /// mod's (and vanilla's) GameModifierPackage instances into <see cref="GameRegistry"/>.
    ///
    /// Schema (parsed with MiniJson — never JsonUtility, which nulls object arrays):
    /// <code>
    /// {
    ///   "_comment": "keys starting with _ are ignored",
    ///   "AutoApplyPackages": [ "&lt;GameModifierPackage UID&gt;", "&lt;another UID&gt;" ]
    /// }
    /// </code>
    /// </summary>
    public static void LoadAll(List<ModManifest> mods)
    {
        _autoApply.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int resolved = 0, skipped = 0;

        foreach (var mod in mods)
        {
            var path = Path.Combine(mod.DirectoryPath, "Modifiers.json");
            if (!File.Exists(path)) continue;

            try
            {
                if (MiniJson.Parse(File.ReadAllText(path)) is not Dictionary<string, object> root)
                {
                    Log.Warn($"[ModifierPackageInjector] {mod.Name}/Modifiers.json: root must be a JSON object "
                        + "with an \"AutoApplyPackages\" array — skipped.");
                    skipped++;
                    continue;
                }

                if (!root.TryGetValue("AutoApplyPackages", out var listVal) || listVal is not List<object> uidList)
                {
                    Log.Warn($"[ModifierPackageInjector] {mod.Name}/Modifiers.json: missing or non-array "
                        + "\"AutoApplyPackages\" — skipped.");
                    skipped++;
                    continue;
                }

                foreach (var entry in uidList)
                {
                    if (entry is not string uid || string.IsNullOrWhiteSpace(uid))
                    {
                        Log.Warn($"[ModifierPackageInjector] {mod.Name}/Modifiers.json: AutoApplyPackages entry "
                            + $"'{entry}' is not a UID string — skipped.");
                        skipped++;
                        continue;
                    }

                    if (!seen.Add(uid))
                    {
                        Log.Debug($"[ModifierPackageInjector] {mod.Name}: '{uid}' already queued by an earlier mod — ignored.");
                        continue;
                    }

                    var package = GameRegistry.GetByUid<GameModifierPackage>(uid);
                    if (package == null)
                    {
                        Log.Warn($"[ModifierPackageInjector] {mod.Name}/Modifiers.json: no GameModifierPackage with "
                            + $"UniqueID '{uid}' is registered — skipped (ship it under GameModifierPackage/*.json).");
                        skipped++;
                        continue;
                    }

                    _autoApply.Add(package);
                    resolved++;
                    Log.Debug($"[ModifierPackageInjector] {mod.Name}: queued auto-apply package '{uid}'.");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[ModifierPackageInjector] failed to load {path}: {Log.ExceptionText(ex)}");
                skipped++;
            }
        }

        if (resolved > 0 || skipped > 0)
            Log.Debug($"[ModifierPackageInjector] resolved {resolved} auto-apply package(s) ({skipped} skipped).");
    }

    /// <summary>
    /// Installs the <c>MainMenu.StartGame</c> patch pair. Call AFTER <see cref="LoadAll"/>
    /// (LoadOrchestrator calls both back-to-back) — self-no-ops (zero patch, zero cost) when no mod
    /// shipped a resolvable auto-apply package.
    /// </summary>
    public static void ApplyPatch(Harmony harmony)
    {
        if (_patchApplied) return;
        if (_autoApply.Count == 0) return; // nothing to inject — skip patch registration entirely
        _patchApplied = true;

        try
        {
            var target = AccessTools.Method(typeof(MainMenu), nameof(MainMenu.StartGame));
            if (target == null)
            {
                Log.Warn("[ModifierPackageInjector] MainMenu.StartGame not found — auto-apply packages will not be injected.");
                return;
            }
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(ModifierPackageInjector), nameof(StartGame_Prefix)),
                postfix: new HarmonyMethod(typeof(ModifierPackageInjector), nameof(StartGame_Postfix)));
            Log.Debug($"[ModifierPackageInjector] patched MainMenu.StartGame ({_autoApply.Count} package(s) queued).");
        }
        catch (Exception ex)
        {
            Log.Warn($"[ModifierPackageInjector] failed to patch MainMenu.StartGame: {Log.ExceptionText(ex)}");
        }
    }

    // StartGame is a multi-step state machine: the player's click re-enters it once per settings
    // popup (checkpoint / quest mode / skip-tutorial) and it RETURNS EARLY on every step but the
    // last (.decomp/MainMenu.cs:1241, 1295, 1318). Only the terminal pass reaches the
    // `CurrentModifierPackages = new List<...>()` assignment, so capture the list reference before
    // the call and act only when it was replaced during it — exactly-once, no step-number guessing.
    private static void StartGame_Prefix(out List<GameModifierPackage> __state)
    {
        __state = GameManager.CurrentModifierPackages;
    }

    private static void StartGame_Postfix(List<GameModifierPackage> __state)
    {
        try
        {
            var current = GameManager.CurrentModifierPackages;
            if (current == null) return;                        // vanilla never got to the assignment
            if (ReferenceEquals(current, __state)) return;      // early-return step — not a real start

            int appended = 0;
            foreach (var package in _autoApply)
            {
                if (package == null) continue;
                if (current.Contains(package)) continue;        // character's own EasyPackage already added it
                current.Add(package);
                appended++;
                Log.Debug($"[ModifierPackageInjector] appended auto-apply package '{package.UniqueID}'.");
            }

            if (appended > 0)
                Log.Info($"[ModifierPackageInjector] appended {appended} auto-apply package(s) to new game.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[ModifierPackageInjector] StartGame_Postfix failed: {Log.ExceptionText(ex)}");
        }
    }
}
