using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Repeat_Action;

/// <summary>
/// The player-visible toast texts (plan Feature 16, half 2). Each entry is an English template, which is also its
/// key, and a Simplified Chinese template. The Chinese is session-written and has not been reviewed by a native
/// speaker. The toast label draws Chinese through the operating system's fallback font behind Unity's built-in
/// Arial (plan Wave 0 finding W0.8, measured on Windows only). No CSV: RepeatAction has no Localization folder,
/// and this table is the only source.
/// </summary>
/// <remarks>
/// Only toasts translate. Every log line, and every string code compares (CancelledReason, GameEndedReason,
/// LockedTargetGone), stays English: a stop reason is built in English with <see cref="En"/>, logged as it is, and
/// translated only where it is shown, with <see cref="Shown"/>. The language is
/// <c>LocalizationManager.CurrentLanguage</c> (1 is Simplified Chinese), never <c>SelectedLanguageName</c>, whose
/// Korean spelling ends in a space and whose index 4 reads English (W0.8). The table is resolved once per language
/// change, and a Chinese template whose placeholders differ from its English one falls back to English, with one
/// warning per entry. Development_Tools/Tests/RepeatAction-MessageTable.Tests.ps1 holds every key used in code to
/// this table and every pair to the same placeholders, so keep one entry per line in the shape below.
/// </remarks>
internal static class Messages
{
    private const int ChineseLanguage = 1;

    // { English template (the key), Simplified Chinese template }
    private static readonly string[,] Table =
    {
        // Plugin.cs: the count keys and the Repeat / Fire Pinned Action keys
        { "Repeat Count: {0}", "重复次数：{0}" },
        { "Unlimited", "无限" },
        { "Repeat Cancelled", "已取消重复" },
        { "'{0}' is not supported", "不支持“{0}”" },
        { "No action to repeat", "没有可重复的动作" },
        { "A repeat is already running", "已有重复正在进行" },
        { "No pinned action", "没有固定的动作" },
        // ActionPatch.cs: pin, pause and the run itself
        { "No action to pin", "没有可固定的动作" },
        { "Pinned: {0}", "已固定：{0}" },
        { "No repeat is running", "没有正在进行的重复" },
        { "Resuming", "继续" },
        { "Pause cancelled", "已取消暂停" },
        { "Pausing after this iteration", "本次完成后暂停" },
        { "Game not ready", "游戏尚未就绪" },
        { "Repeating {0}: {1} {2}", "正在重复{0}：{1} {2}" },
        { "Paused at {0} - {1} to resume", "已暂停于 {0} - 按 {1} 继续" },
        { "Complete: {0}", "完成：{0}" },
        { "Stopped - {0} ({1})", "已停止 - {0}（{1}）" },
        { "Stopped - reached the {0}-iteration limit ({1})", "已停止 - 达到 {0} 次上限（{1}）" },
        // The run's kind and length, shown in "Repeating {0}: {1} {2}"
        { "action", "动作" },
        { "stack", "堆叠" },
        { "drag-drop", "拖放" },
        { "group", "成组" },
        { "group, per card", "成组，逐张" },
        { "pinned {0}", "固定的{0}" },
        { "unlimited", "无限" },
        // Stop reasons, shown in "Stopped - {0} ({1})"
        { "cancelled", "已取消" },
        { "game ended", "游戏已结束" },
        { "locked target is gone", "锁定的目标已不在" },
        { "an encounter interrupted the run", "遭遇打断了重复" },
        { "timed out waiting for the game to settle", "等待游戏空闲超时" },
        { "blocked by your condition", "受你的状态所限" },
        { "inventory full", "背包已满" },
        { "night is falling", "夜幕降临" },
        { "the season changed", "季节已变" },
        { "reached the in-game time limit", "已达游戏内时间上限" },
        { "tool changed", "工具已变" },
        { "{0} - pin cleared", "{0} - 已取消固定" },
        { "unknown action kind", "未知的动作类型" },
        { "internal error (see log)", "内部错误（见日志）" },
        { "no more '{0}' targets", "没有更多“{0}”的目标" },
        { "no more targets", "没有更多目标" },
        { "target card not found", "找不到目标卡牌" },
        { "target cards not found", "找不到目标卡牌" },
        { "the game rejected the action", "游戏拒绝了该动作" },
        { "stack used up", "堆叠已用完" },
        { "action no longer available", "动作已不可用" },
        { "source used up", "来源已用完" },
        { "source card not found", "找不到来源卡牌" },
        { "tool durability below {0}%", "工具耐久低于 {0}%" },
        { "liquid can no longer be transferred", "液体无法再转移" },
        { "requirements no longer met", "条件已不满足" },
        { "availability check failed (see log)", "可用性检查失败（见日志）" },
        { "{0} above {1}%", "{0}高于 {1}%" },
        { "{0} below {1}%", "{0}低于 {1}%" },
        { "{0} reached {1}", "{0}已达到 {1}" },
        // Refuel From Pinned Action (Feature 17): the toast when it fires, and the two stops around a stat floor
        { "Refuelling: {0}", "补给：{0}" },
        { "{0} (refuel did not help)", "{0}（补给无效）" },
        { "{0} (pinned action unavailable)", "{0}（固定的动作不可用）" },
        { "{0} (refuel limit reached)", "{0}（已达补给上限）" },
        // Repeat presets (Feature 19): the Next Preset Key's toasts
        { "Preset: {0}", "预设：{0}" },
        { "Preset: {0} ({1})", "预设：{0}（{1}）" },
        { "No presets set", "未设置预设" },
        { "A repeat is running; presets cannot change", "重复进行中，无法切换预设" },
        // Recorded sequences (Feature 20): the Record, Play and Next Sequence keys
        { "Recording (step {0})", "录制中（第 {0} 步）" },
        { "Recording cancelled (no steps)", "已取消录制（没有步骤）" },
        { "Recording: stop it with {0} first", "正在录制：请先按 {0} 停止" },
        { "Sequence saved: {0} ({1} steps)", "序列已保存：{0}（{1} 步）" },
        { "Sequence kept for this session: {0} ({1} steps)", "序列仅保留到本次退出：{0}（{1} 步）" },
        { "Sequence: {0} ({1} steps)", "序列：{0}（{1} 步）" },
        { "No sequences recorded", "没有录制的序列" },
        { "Playing {0}: {1}", "正在播放 {0}：{1}" },
        { "step {0} of {1} ({2}): {3}", "第 {0}/{1} 步（{2}）：{3}" },
        { "cannot be rebuilt", "无法重建" },
        // The three fixed stat floors' names: the game's own Chinese for each stat (its Cn.csv GameName rows)
        { "Satiation", "饱食度" },
        { "Hydration", "水分" },
        { "Stamina", "耐力" },
    };

    // Keys used in code with no words to translate: they stay English in every language.
    private static readonly string[] EnglishOnly =
    {
        "{0}: {1}",
        "{0}: {1}/{2}",
        "x{0}",
    };

    private static readonly Regex PlaceholderRx = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.Compiled);

    // English key -> the template in the language last resolved (Chinese, or the English itself).
    private static readonly Dictionary<string, string> active = new Dictionary<string, string>(StringComparer.Ordinal);
    private static int resolvedFor = int.MinValue;
    // Shown: English keys with no placeholder, and a pattern for each key with one, longest literal text first so
    // "tool durability below {0}%" is tried before "{0} below {1}%". Built once: the English never changes.
    private static HashSet<string> exactKeys;
    private static List<KeyValuePair<Regex, string>> patterns;
    private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The number of translated entries (the set counted at build time).</summary>
    internal static int Count => Table.GetLength(0);

    /// <summary>A toast: the current language's template for <paramref name="english"/>, formatted.</summary>
    internal static string Text(string english, params object[] args)
    {
        Resolve();
        if (!active.TryGetValue(english, out string template))
        {
            if (Array.IndexOf(EnglishOnly, english) < 0) WarnOnce("unknown:" + english, $"[Messages] '{english}' has no table entry; shown in English.");
            template = english;
        }
        return Format(template, english, args);
    }

    /// <summary>English text for a log line or a stop reason, from a table key; never translated.</summary>
    internal static string En(string english, params object[] args)
        => args == null || args.Length == 0 ? english : Format(english, english, args);

    /// <summary>
    /// An English text built with <see cref="En"/> (a stop reason, a run's kind), translated for a toast. Text that
    /// matches no key passes through unchanged: the game's own blocked-action messages arrive already in the
    /// player's language, and card and action names in a placeholder are already the localized display forms.
    /// A placeholder's value is translated too when it is itself a key ("pinned {0}" around "drag-drop").
    /// </summary>
    internal static string Shown(string english) => Shown(english, 0);

    private static string Shown(string english, int depth)
    {
        if (string.IsNullOrEmpty(english)) return english;
        Resolve();
        if (resolvedFor != ChineseLanguage || depth > 4) return english;
        if (exactKeys.Contains(english)) return active[english];
        for (int i = 0; i < patterns.Count; i++)
        {
            var m = patterns[i].Key.Match(english);
            if (!m.Success) continue;
            string key = patterns[i].Value;
            int n = MaxPlaceholder(key) + 1;
            var args = new object[n];
            for (int p = 0; p < n; p++)
            {
                var g = m.Groups["p" + p];
                args[p] = g.Success ? Shown(g.Value, depth + 1) : "";
            }
            return Format(active[key], key, args);
        }
        return english;
    }

    private static string Format(string template, string english, object[] args)
    {
        if (args == null || args.Length == 0) return template;
        try { return string.Format(template, args); }
        catch (FormatException)
        {
            WarnOnce("format:" + english, $"[Messages] '{template}' did not format; shown in English.");
            try { return string.Format(english, args); }
            catch (FormatException) { return english; }
        }
    }

    /// <summary>Picks each entry's template for the current language, once per language change.</summary>
    private static void Resolve()
    {
        int lang = CurrentLanguage();
        if (lang == resolvedFor) return;
        if (patterns == null) BuildPatterns();
        active.Clear();
        int fallbacks = 0;
        for (int i = 0; i < Table.GetLength(0); i++)
        {
            string en = Table[i, 0], zh = Table[i, 1];
            string use = en;
            if (lang == ChineseLanguage)
            {
                if (!string.IsNullOrEmpty(zh) && SamePlaceholders(en, zh)) use = zh;
                else { fallbacks++; WarnOnce("malformed:" + en, $"[Messages] the Chinese for '{en}' is missing or its placeholders differ; shown in English."); }
            }
            active[en] = use;
        }
        resolvedFor = lang;
        Plugin.Logger?.LogDebug($"[Messages] {Count} toast texts resolved for language {lang} ({fallbacks} in English)");
    }

    private static void BuildPatterns()
    {
        exactKeys = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<KeyValuePair<Regex, string>>();
        for (int i = 0; i < Table.GetLength(0); i++)
        {
            string en = Table[i, 0];
            if (!PlaceholderRx.IsMatch(en)) { exactKeys.Add(en); continue; }
            string rx = Regex.Escape(en);
            rx = Regex.Replace(rx, @"\\\{(\d+)\}", "(?<p$1>.+?)");
            list.Add(new KeyValuePair<Regex, string>(new Regex("^" + rx + "$", RegexOptions.Singleline), en));
        }
        list.Sort((a, b) => LiteralLength(b.Value).CompareTo(LiteralLength(a.Value)));
        patterns = list;
    }

    private static int LiteralLength(string template) => PlaceholderRx.Replace(template, "").Length;

    private static int MaxPlaceholder(string template)
    {
        int max = -1;
        foreach (Match m in PlaceholderRx.Matches(template)) max = Math.Max(max, int.Parse(m.Groups[1].Value));
        return max;
    }

    private static bool SamePlaceholders(string a, string b)
    {
        var sa = new SortedSet<int>();
        var sb = new SortedSet<int>();
        foreach (Match m in PlaceholderRx.Matches(a)) sa.Add(int.Parse(m.Groups[1].Value));
        foreach (Match m in PlaceholderRx.Matches(b)) sb.Add(int.Parse(m.Groups[1].Value));
        return sa.SetEquals(sb);
    }

    private static int CurrentLanguage()
    {
        try { return ReadGameLanguage(); }
        catch (Exception ex)
        {
            // A game update could rename the property; the toasts then stay English, said once.
            WarnOnce("language", $"[Messages] LocalizationManager.CurrentLanguage could not be read ({ex.GetType().Name}: {ex.Message}); toasts stay English.");
            return 0;
        }
    }

    // In its own non-inlined method so a missing member surfaces at this call, inside the caller's try.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadGameLanguage() => LocalizationManager.CurrentLanguage;

    private static void WarnOnce(string cause, string message)
    {
        if (warned.Add(cause)) Plugin.Logger?.LogWarning(message);
    }
}
