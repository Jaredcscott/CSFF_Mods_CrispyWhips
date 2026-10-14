using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace Quick_Transfer;

/// <summary>
/// Lets Quick Transfer step aside from Repeat Action's count keys (Documentation/Design/QuickTransfer_Round2_As_Built.md
/// Features 1 and 19). Repeat Action changes its repeat count with its count keys (default Plus/Minus and the
/// keypad pair) while one of its modifier keys (default Left/Right Shift) is held, and Quick Transfer nudged its
/// Shift preset with the same keypress, so one press changed two numbers. <see cref="OwnsKey"/> answers "would
/// Repeat Action act on this key on this frame?", and Plugin.Update drops its own nudge for that key when it does.
/// </summary>
/// <remarks>
/// The names below are Repeat Action's published contract (its Plugin.cs comment and plan Features 1 and 21).
/// Development_Tools/Tests/QuickTransfer-RepeatActionYield.Tests.ps1 compares them with Repeat Action's
/// source text and goes red on a rename or a changed default. Resolution is lazy, from the first Update, never Awake: a
/// plugin that loads after this one is absent from Chainloader.PluginInfos during this one's Awake
/// (CSFFModFramework/Patching/BugFixes/CardScaleCompat.cs records that trap), while every plugin is
/// loaded by the first frame.
/// </remarks>
internal static class RepeatActionBridge
{
    internal const string RepeatActionGuid = "crispywhips.repeat_action";
    internal const string KeybindingsSection = "Keybindings";
    internal const string ModifierKeyName = "Repeat Modifier Key";
    internal const string ModifierKeyAltName = "Repeat Modifier Key (Alt)";

    /// <summary>Repeat Action's authored defaults for the two modifier entries, used when an entry cannot be read.</summary>
    internal const KeyCode FallbackModifier = KeyCode.LeftShift;
    internal const KeyCode FallbackModifierAlt = KeyCode.RightShift;

    /// <summary>
    /// Repeat Action's four count-key entries (Keybindings section, type KeyCode), in the order Count Up, Count Up
    /// (Alt), Count Down, Count Down (Alt). Repeat Action 2.2.1 and later binds them; an older one hard-codes the
    /// defaults, which <see cref="CountKeys"/> keeps as the fallback.
    /// </summary>
    internal static readonly string[] CountKeyNames = { "Count Up Key", "Count Up Key (Alt)", "Count Down Key", "Count Down Key (Alt)" };

    /// <summary>
    /// The authored defaults of the four entries above, position for position: Equals and KeypadPlus count up,
    /// Minus and KeypadMinus count down. Used for an entry that cannot be read.
    /// </summary>
    internal static readonly KeyCode[] CountKeys = { KeyCode.Equals, KeyCode.KeypadPlus, KeyCode.Minus, KeyCode.KeypadMinus };

    private static bool _resolved;
    private static bool _present;
    private static ConfigEntry<KeyCode> _modifier;
    private static ConfigEntry<KeyCode> _modifierAlt;
    private static readonly ConfigEntry<KeyCode>[] _countEntries = new ConfigEntry<KeyCode>[4];
    private static readonly HashSet<string> _warned = new HashSet<string>();

    /// <summary>True once Repeat Action was found loaded (valid after the first Update).</summary>
    internal static bool Present => _resolved && _present;

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
            Plugin.Logger.LogWarning("Repeat Action count-key yield: " + message);
    }

    /// <summary>
    /// Look Repeat Action up once. Called from Plugin.Update; a no-op after the first call.
    /// Every outcome leaves one log line, so a lookup that silently found nothing cannot pass for
    /// "Repeat Action is not installed".
    /// </summary>
    internal static void EnsureResolved()
    {
        if (_resolved) return;
        _resolved = true;
        _present = false;

        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(RepeatActionGuid, out var info) || info == null)
            {
                // The normal case for a Quick Transfer-only install: nothing to yield to.
                Plugin.Logger.LogDebug($"Repeat Action ({RepeatActionGuid}) is not loaded; Plus/Minus are never yielded.");
                return;
            }
            if (info.Instance == null)
            {
                WarnOnce("instance", $"{RepeatActionGuid} is listed but has no plugin instance (it failed to load?); Plus/Minus are never yielded.");
                return;
            }

            _present = true;
            ConfigFile config = info.Instance.Config;
            _modifier = ReadEntry(config, ModifierKeyName, FallbackModifier);
            _modifierAlt = ReadEntry(config, ModifierKeyAltName, FallbackModifierAlt);
            for (int i = 0; i < _countEntries.Length; i++)
                _countEntries[i] = ReadEntry(config, CountKeyNames[i], CountKeys[i]);
            Plugin.Logger.LogDebug($"Repeat Action found; yielding {DescribeCountKeys()} while {DescribeModifier(_modifier, FallbackModifier)} or {DescribeModifier(_modifierAlt, FallbackModifierAlt)} is held.");
        }
        catch (Exception ex)
        {
            _present = false;
            WarnOnce("lookup", $"the plugin lookup threw ({ex.GetType().Name}: {ex.Message}); Plus/Minus are never yielded.");
        }
    }

    private static ConfigEntry<KeyCode> ReadEntry(ConfigFile config, string key, KeyCode fallback)
    {
        if (config == null)
        {
            WarnOnce("config", $"Repeat Action has no config file; using its default modifiers ({FallbackModifier} / {FallbackModifierAlt}) and count keys.");
            return null;
        }
        try
        {
            if (config.TryGetEntry<KeyCode>(KeybindingsSection, key, out var entry) && entry != null)
                return entry;
            WarnOnce("entry:" + key, $"Repeat Action has no [{KeybindingsSection}] \"{key}\" entry (renamed?); assuming {fallback}.");
        }
        catch (Exception ex)
        {
            // TryGetEntry casts the stored entry, so an entry of another type throws here.
            WarnOnce("entry-type:" + key, $"[{KeybindingsSection}] \"{key}\" could not be read as a KeyCode ({ex.GetType().Name}); assuming {fallback}.");
        }
        return null;
    }

    private static KeyCode Current(ConfigEntry<KeyCode> entry, KeyCode fallback) => entry != null ? entry.Value : fallback;

    private static string DescribeModifier(ConfigEntry<KeyCode> entry, KeyCode fallback) => Current(entry, fallback).ToString();

    private static string DescribeCountKeys()
    {
        var names = new List<string>(CountKeys.Length);
        for (int i = 0; i < CountKeys.Length; i++)
        {
            var key = Current(_countEntries[i], CountKeys[i]);
            if (key != KeyCode.None) names.Add(key.ToString());
        }
        return names.Count == 0 ? "no count keys" : string.Join("/", names);
    }

    /// <summary>
    /// True when <paramref name="key"/> is one of the four count keys Repeat Action reads right now. Read live, so a
    /// rebind in F1 counts at once; an entry set to None owns nothing, and an entry that could not be read counts
    /// as its default.
    /// </summary>
    private static bool IsCountKey(KeyCode key)
    {
        if (key == KeyCode.None) return false;
        for (int i = 0; i < CountKeys.Length; i++)
            if (Current(_countEntries[i], CountKeys[i]) == key) return true;
        return false;
    }

    /// <summary>True while either of Repeat Action's modifier keys (read live, so a rebind in F1 counts at once) is held.</summary>
    private static bool ModifierHeld()
    {
        var first = Current(_modifier, FallbackModifier);
        var second = Current(_modifierAlt, FallbackModifierAlt);
        return (first != KeyCode.None && Input.GetKey(first)) || (second != KeyCode.None && Input.GetKey(second));
    }

    /// <summary>
    /// True when Repeat Action is loaded, the Yield Count Keys To Repeat Action switch is on,
    /// <paramref name="key"/> is one of Repeat Action's count keys (as bound now), one of its modifiers is held right
    /// now, and the held chord is not Ctrl+Shift together. That last term keeps Quick Transfer's
    /// Ctrl+Shift restate alive with Repeat Action loaded: that branch writes no number, so it collides
    /// on nothing, and T2.213_qt_ctrlshift_half_preset.json step (e) relies on it.
    /// </summary>
    internal static bool OwnsKey(KeyCode key)
    {
        if (Plugin.YieldCountKeysToRepeatAction == null || !Plugin.YieldCountKeysToRepeatAction.Value) return false;
        EnsureResolved();
        if (!_present) return false;
        if (!IsCountKey(key)) return false;
        if (!ModifierHeld()) return false;
        if (Plugin.CtrlHeld() && Plugin.ShiftHeld()) return false;
        return true;
    }
}
