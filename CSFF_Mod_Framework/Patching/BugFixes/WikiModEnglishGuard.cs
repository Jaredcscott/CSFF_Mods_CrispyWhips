using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace CSFFModFramework.Patching.BugFixes;

/// <summary>
/// Stops WikiMod writing Chinese durability-stat labels (Quality, Freshness, Temperature, Metal Type
/// and 56 more) onto every card when the game runs in English.
///
/// The mechanism (plan W0-5, Mono.Cecil read of WikiMod 3.5.1.0, 2026-10-02):
///   WikiMod.DataStore.InitByGameData runs eight appliers on WikiMod.CustomTranslation. Seven of them
///   take their text from WikiMod's I18n.t(zh, en), which reads LocalizationManager.CurrentLanguage,
///   so in English they write WikiMod's ENGLISH relabelling. The eighth,
///   ApplyDurabilityTranslations, reads a literal English-to-Chinese table (DurabilityNameTranslations)
///   and writes the Chinese into DurabilityStat.CardStatName.LocalizedText whenever the key has no
///   official translation, which in English mode is every key, because the game ships no En.csv.
///   LocalizedString.ToString() returns LocalizedText first, so the Chinese wins over DefaultText.
///
/// What this guard does: one prefix on ApplyDurabilityTranslations ONLY, which skips it unless
/// LocalizationManager.CurrentLanguage == 1 (Chinese; WikiMod's own LanguageCode.zh and the
/// framework's LocalizationLoader use the same index). The other seven appliers are left alone:
/// patching them would strip WikiMod's English relabelling from English players. InitByGameData is
/// never patched, because it does other work. The language is already set when InitByGameData runs
/// (GameLoad.AwakeWithLoadingScreen calls LoadOptions first), and a runtime language switch never
/// re-runs it, so the boot-time decision matches WikiMod's own.
///
/// Timing: InitByGameData runs from a postfix on GameLoad.LoadMainGameData (or ModCore's
/// LoadCompleteEvent), which can be the first frame after the plugins load, so a deferred per-frame
/// retry could arrive late. The prefix is installed the moment the WikiMod assembly loads
/// (AppDomain.AssemblyLoad), or at once if it is already loaded, which is always before WikiMod's
/// own Awake and therefore before any of its data work.
///
/// Risk: a future WikiMod renames the applier or its type. SafePatcher then fails to bind and this
/// guard logs one Warn naming the consequence, as WikiModPatchAllRescue does; nothing else breaks.
/// </summary>
internal static class WikiModEnglishGuard
{
    private const string WikiModAssemblyName = "WikiMod";
    private const string TranslationTypeName = "WikiMod.CustomTranslation";
    private const string GuardedApplier = "ApplyDurabilityTranslations";
    private const int ChineseLanguageIndex = 1;

    private static Harmony _harmony;
    private static bool _attempted;
    private static bool _loggedEngaged;
    private static bool _subscribed;

    public static void Configure(ConfigFile config, Harmony harmony)
    {
        var guardCfg = config.Bind(
            "Compatibility", "GuardWikiModChineseInEnglish", true,
            "With WikiMod installed and the game in English, stop WikiMod's durability-stat translation "
            + "table writing Chinese labels (Quality, Freshness, Temperature, Metal Type and more) onto "
            + "every card. Only that one WikiMod translation step is skipped, and only outside Chinese "
            + "mode; WikiMod's other English relabelling is untouched. Set false to let WikiMod run it.");
        if (!guardCfg.Value)
        {
            Util.Log.Debug("WikiModEnglishGuard: disabled by config.");
            return;
        }

        _harmony = harmony;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (IsWikiMod(asm)) { TryInstall(asm); return; }
        }

        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        _subscribed = true;
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        // An exception thrown out of an AssemblyLoad handler would surface inside whoever loaded the
        // assembly (BepInEx's chainloader), so nothing may escape this method.
        try
        {
            var asm = args?.LoadedAssembly;
            if (asm == null || !IsWikiMod(asm)) return;
            Unsubscribe();
            TryInstall(asm);
        }
        catch (Exception ex)
        {
            Util.Log.Warn($"WikiModEnglishGuard: install on WikiMod load failed: {Util.Log.ExceptionText(ex)}");
        }
    }

    private static void Unsubscribe()
    {
        if (!_subscribed) return;
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        _subscribed = false;
    }

    private static bool IsWikiMod(Assembly asm)
    {
        try
        {
            return string.Equals(asm.GetName().Name, WikiModAssemblyName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Util.Log.Debug($"WikiModEnglishGuard: assembly-name read failed: {ex.GetType().Name} {ex.Message}");
            return false;
        }
    }

    private static void TryInstall(Assembly wikiMod)
    {
        if (_attempted) return;
        _attempted = true;

        Type translationType = null;
        try { translationType = wikiMod.GetType(TranslationTypeName, false); }
        catch (Exception ex)
        {
            Util.Log.Debug($"WikiModEnglishGuard: GetType({TranslationTypeName}) threw: {ex.GetType().Name} {ex.Message}");
        }

        var bound = translationType != null && SafePatcher.TryPatch(
            _harmony, translationType, GuardedApplier,
            prefix: new HarmonyMethod(typeof(WikiModEnglishGuard), nameof(SkipUnlessChinese)));

        if (!bound)
        {
            string version;
            try { version = wikiMod.GetName().Version?.ToString() ?? "unknown"; }
            catch (Exception ex)
            {
                version = "unknown";
                Util.Log.Debug($"WikiModEnglishGuard: version read failed: {ex.GetType().Name} {ex.Message}");
            }
            Util.Log.Warn(
                $"WikiModFix: could not bind {TranslationTypeName}.{GuardedApplier} on WikiMod v{version} "
                + "(renamed in this WikiMod build?). GuardWikiModChineseInEnglish is not active, so if this "
                + "WikiMod still translates durability-stat labels unconditionally, English players may see "
                + "Chinese stat names (Quality, Freshness, Temperature).");
        }
    }

    /// <summary>
    /// Prefix on WikiMod.CustomTranslation.ApplyDurabilityTranslations: run the original only in
    /// Chinese mode.
    /// </summary>
    static bool SkipUnlessChinese()
    {
        int language;
        try { language = LocalizationManager.CurrentLanguage; }
        catch (Exception ex)
        {
            // Cannot tell the language: let WikiMod behave as it would without the framework.
            Util.Log.Warn($"WikiModEnglishGuard: LocalizationManager.CurrentLanguage read failed, WikiMod's durability translation left to run: {ex.GetType().Name} {ex.Message}");
            return true;
        }

        if (language == ChineseLanguageIndex) return true;

        if (!_loggedEngaged)
        {
            _loggedEngaged = true;
            Util.Log.Info($"WikiModFix: game language index {language} is not Chinese, so WikiMod's "
                          + $"{GuardedApplier} was skipped (durability-stat labels stay in English). "
                          + "Config: [Compatibility] GuardWikiModChineseInEnglish.");
        }
        return false;
    }
}
