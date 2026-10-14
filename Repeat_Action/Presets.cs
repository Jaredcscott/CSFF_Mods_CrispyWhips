using BepInEx.Configuration;
using System;
using System.Collections.Generic;

namespace Repeat_Action;

/// <summary>
/// Repeat presets (plan Feature 19): named sets of run settings that <c>Next Preset Key</c> loads one after
/// another. A preset is a saved set of values written over whatever is in the config now, so loading one
/// replaces values the player set by hand.
/// </summary>
/// <remarks>
/// Format of the <c>Repeat Presets</c> setting: presets split on ';', the name before the first ':', pairs split
/// on ',' and each pair on its first '='. The key <c>count</c> sets the repeat count (0 is Unlimited, a real
/// value: "missing" is decided by a failed parse, never by truthiness). Any other key names a bool, int or float
/// entry of this mod's own config outside "Keybindings", written through BepInEx's own string parse so an entry's
/// AcceptableValueRange still applies. String entries are refused because their values hold ':' and ','. A
/// malformed pair is skipped with an Info breadcrumb (this runs outside a run, so RunLog's Debug level would be
/// invisible) and the preset's other pairs still apply.
/// </remarks>
internal static class Presets
{
    private sealed class Preset
    {
        public string Name;
        public readonly List<KeyValuePair<string, string>> Pairs = new List<KeyValuePair<string, string>>();
    }

    private const string CountKey = "count";
    private const string KeybindingsSection = "Keybindings";
    // Managed by the mod, not a setting a preset should write.
    private const string ManagedEntry = "Last Count";

    private static string cachedSource;
    private static List<Preset> cached = new List<Preset>();
    private static int index = -1;
    private static string loadedName;

    /// <summary>The name of the preset loaded last, or null when none was loaded since the source last changed.</summary>
    internal static string LoadedName => loadedName;

    private static List<Preset> Parsed()
    {
        string raw = Plugin.RepeatPresets != null ? Plugin.RepeatPresets.Value : null;
        raw = raw ?? "";
        if (raw == cachedSource) return cached;
        cachedSource = raw;
        cached = Parse(raw);
        index = -1;
        loadedName = null;
        return cached;
    }

    private static List<Preset> Parse(string raw)
    {
        var presets = new List<Preset>();
        foreach (string chunk in raw.Split(';'))
        {
            string text = chunk.Trim();
            if (text.Length == 0) continue;
            int colon = text.IndexOf(':');
            string name = colon > 0 ? text.Substring(0, colon).Trim() : "";
            if (name.Length == 0)
            {
                Plugin.Logger?.LogInfo($"[Presets] skipping '{text}': expected name: key=value, key=value");
                continue;
            }
            var preset = new Preset { Name = name };
            foreach (string pairText in text.Substring(colon + 1).Split(','))
            {
                string pair = pairText.Trim();
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                if (eq <= 0 || eq == pair.Length - 1)
                {
                    Plugin.Logger?.LogInfo($"[Presets] preset '{name}': skipping malformed pair '{pair}' (expected key=value)");
                    continue;
                }
                preset.Pairs.Add(new KeyValuePair<string, string>(pair.Substring(0, eq).Trim(), pair.Substring(eq + 1).Trim()));
            }
            presets.Add(preset);
        }
        return presets;
    }

    /// <summary>
    /// The Next Preset Key: load preset i+1, wrapping. While a run is active it changes nothing and says so, so a
    /// running batch never changes under the player.
    /// </summary>
    internal static void LoadNext()
    {
        if (Patcher.ActionPatch.IsRepeating)
        {
            Plugin.ShowNotification(Messages.Text("A repeat is running; presets cannot change"));
            return;
        }
        var presets = Parsed();
        if (presets.Count == 0)
        {
            Plugin.ShowNotification(Messages.Text("No presets set"));
            return;
        }
        index = (index + 1) % presets.Count;
        Preset preset = presets[index];
        loadedName = preset.Name;
        string summary = Apply(preset);
        Plugin.Logger?.LogInfo($"[Presets] loaded '{preset.Name}' ({summary})");
        if (summary.Length == 0)
            Plugin.ShowNotification(Messages.Text("Preset: {0}", preset.Name));
        else
            Plugin.ShowNotification(Messages.Text("Preset: {0} ({1})", preset.Name, summary));
    }

    private static string Apply(Preset preset)
    {
        var applied = new List<string>();
        foreach (var pair in preset.Pairs)
        {
            string label = TryApply(preset.Name, pair.Key, pair.Value);
            if (label != null) applied.Add(label);
        }
        return string.Join(", ", applied);
    }

    /// <summary>Writes one pair; returns its summary text, or null when it was skipped (already logged).</summary>
    private static string TryApply(string presetName, string key, string value)
    {
        if (string.Equals(key, CountKey, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int count))
            {
                Plugin.Logger?.LogInfo($"[Presets] preset '{presetName}': count='{value}' is not a whole number, skipped");
                return null;
            }
            int clamped = Plugin.SetRepeatCount(count);
            return clamped == Plugin.UnlimitedCount ? "count Unlimited" : "count " + clamped;
        }

        ConfigEntryBase entry = FindEntry(key);
        if (entry == null)
        {
            Plugin.Logger?.LogInfo($"[Presets] preset '{presetName}': '{key}' is not a setting a preset can write (only count and bool, whole-number or decimal settings outside Keybindings), skipped");
            return null;
        }
        try
        {
            entry.SetSerializedValue(value);
        }
        catch (Exception ex)
        {
            Plugin.Logger?.LogInfo($"[Presets] preset '{presetName}': '{key}={value}' was not accepted ({ex.GetType().Name}), skipped");
            return null;
        }

        string now = entry.GetSerializedValue();
        if (entry.SettingType == typeof(bool))
            return string.Equals(now, "true", StringComparison.OrdinalIgnoreCase) ? entry.Definition.Key + " on" : entry.Definition.Key + " off";
        return entry.Definition.Key + " " + now;
    }

    private static ConfigEntryBase FindEntry(string key)
    {
        var config = Plugin.Instance != null ? Plugin.Instance.Config : null;
        if (config == null) return null;
        foreach (ConfigDefinition def in config.Keys)
        {
            if (!string.Equals(def.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(def.Section, KeybindingsSection, StringComparison.Ordinal)) return null;
            if (string.Equals(def.Key, ManagedEntry, StringComparison.Ordinal)) return null;
            ConfigEntryBase entry = config[def];
            Type t = entry.SettingType;
            return t == typeof(bool) || t == typeof(int) || t == typeof(float) ? entry : null;
        }
        return null;
    }
}
