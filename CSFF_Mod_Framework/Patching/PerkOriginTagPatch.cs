using CSFFModFramework.Discovery;
using CSFFModFramework.Loading;
using CSFFModFramework.Util;
using TMPro;

namespace CSFFModFramework.Patching;

/// <summary>
/// Perk origin marker: appends a short mod tag (" [CMC]") to the DISPLAYED name of every
/// framework-loaded character perk, so a player with several perk mods can tell them apart.
/// Config <c>[Perks] ShowModOriginTag</c>, default on (owner decision 2026-09-14, plan
/// Trait_Effect_Repair_Plan 3.4). Vanilla perks, and perks another loader owns, have no entry
/// in <see cref="JsonDataLoader.UniqueIdToModName"/> and are never touched.
///
/// <para><b>Display only.</b> This file writes three rendered text members and one tooltip title.
/// It NEVER writes <c>CharacterPerk.PerkName</c> or any <c>LocalizedString</c>: the CSV is
/// authoritative for that string and it also feeds <c>StatModifierReport</c> sources,
/// <c>StatInfluenceInfo</c> and <c>PerkUnlockPopup</c>, none of which should carry a tag.</para>
///
/// <para>Rendered members, verified in the decompile (EA 0.67i):</para>
/// <list type="bullet">
/// <item><c>MenuPerkButton.Setup(int, CharacterPerk, bool)</c> ends in
/// <c>IndexButton.Setup(int, string, string, bool)</c> then <c>TooltipButton.Setup</c>, which
/// assigns <c>TooltipButton.Text</c>, a public property over the protected <c>ButtonText</c>
/// TextMeshProUGUI. <c>MainMenu.RefreshPerkLists</c> re-parents the SAME pooled buttons between
/// the Available and Equipped panels, so this one hook covers both lists.</item>
/// <item><c>MenuPerkPreview.Setup(CharacterPerk)</c> assigns the private
/// <c>new TextMeshProUGUI Title</c> (it hides <c>TooltipProvider.Title</c>, a string) and sets the
/// tooltip title through the public <c>TooltipProvider.SetTooltip</c>. The prefab serves the
/// character select card AND the in-game character sheet (<c>CharacterScreen</c>, including
/// in-run perks), so both show the tag.</item>
/// <item><c>MainMenu.SelectPerk(int)</c> assigns the public <c>MainMenu.PerkName</c>
/// TextMeshProUGUI, the header of the selected-perk detail panel.</item>
/// </list>
///
/// <para><b>Idempotency.</b> Every one of those vanilla methods REWRITES its text from
/// <c>PerkName</c> on each call, before the postfix runs. So a pooled button re-used for a vanilla
/// perk comes back clean with no work here, and a re-setup of a mod perk starts from the bare
/// name. The <c>EndsWith</c> test in <see cref="Append"/> is the second line of defence, against a
/// postfix that somehow runs twice on one assignment.</para>
///
/// <para>NPC creation (<c>MenuNPCPerkButton</c> / <c>MenuNPCPerkPreview</c>) renders
/// <c>NPCCharacterPerk</c>, a different type, and is out of scope.</para>
/// </summary>
internal static class PerkOriginTagPatch
{
    /// <summary>Bound from <c>[Perks] ShowModOriginTag</c> in Plugin.Awake. BepInEx reads the
    /// .cfg once at Awake, so a change needs a full quit and relaunch.</summary>
    internal static bool Enabled = true;

    // Mod Name -> " [TAG]". Keyed on ModManifest.Name because that is exactly what
    // JsonDataLoader.LoadAll stores as the value of UniqueIdToModName (item.Mod.Name).
    private static readonly Dictionary<string, string> _suffixByModName = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _breadcrumbs = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _tracedPerks = new(StringComparer.OrdinalIgnoreCase);

    // Resolved once and cached. The bool is separate from the FieldInfo so a miss is not retried
    // (and not re-warned by AccessTools) on every button.
    private static bool _previewTitleResolved;
    private static FieldInfo _previewTitleField;
    private static bool _allPerksResolved;
    private static FieldInfo _allPerksField;

    // Latched when a tagger throws, so one broken game member costs one warning, not one
    // exception per perk button on every list refresh.
    private static bool _buttonBroken, _previewBroken, _detailBroken;

    // ── Tag table ───────────────────────────────────────────────────────────────

    /// <summary>Builds the mod-name-to-tag table from the manifests ModDiscovery returned.
    /// Called by LoadOrchestrator next to JsonDataLoader.LoadAll, which fills the UID-to-mod map
    /// from the same manifest objects. Never throws.</summary>
    internal static void SetModTags(IReadOnlyList<ModManifest> mods)
    {
        _suffixByModName.Clear();
        if (!Enabled || mods == null) return;
        try
        {
            foreach (var mod in mods)
            {
                if (mod == null || string.IsNullOrEmpty(mod.Name)) continue;
                var tag = mod.DisplayTag;
                if (string.IsNullOrEmpty(tag))
                {
                    Log.Debug($"PerkOriginTag: no tag could be derived for mod '{mod.Name}' - its perks stay untagged");
                    continue;
                }
                _suffixByModName[mod.Name] = " [" + tag + "]";
            }
            if (Log.Verbose)
                Log.Debug("PerkOriginTag: " + _suffixByModName.Count + " mod tag(s): "
                          + string.Join(", ", _suffixByModName.Select(kv => kv.Key + " =" + kv.Value)));
        }
        catch (Exception ex)
        {
            Log.Warn($"PerkOriginTag: building the mod tag table failed - perks stay untagged: {Log.ExceptionText(ex)}");
        }
    }

    // ── Patch registration ──────────────────────────────────────────────────────

    public static void ApplyPatch(Harmony harmony)
    {
        if (!Enabled)
        {
            Log.Debug("PerkOriginTagPatch: disabled by [Perks] ShowModOriginTag - no perk UI method patched");
            return;
        }
        // The typeof() tokens live in ApplyPatchCore: if a game update renames one of these UI
        // types, the TypeLoadException surfaces at that call, inside this try, and costs the tag
        // feature only. Thrown from here it would abort the rest of Plugin.Awake.
        try { ApplyPatchCore(harmony); }
        catch (Exception ex)
        {
            Log.Warn($"PerkOriginTagPatch: not applied, perk names show without a mod tag: {Log.ExceptionText(ex)}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ApplyPatchCore(Harmony harmony)
    {
        // Parameter types are spelled out because "Setup" is overloaded up the hierarchy
        // (MenuPerkButton, IndexButton and TooltipButton each declare one): a name-only
        // AccessTools.Method lookup throws AmbiguousMatchException.
        PatchOne(harmony, typeof(MenuPerkButton), "Setup",
            new[] { typeof(int), typeof(CharacterPerk), typeof(bool) }, nameof(MenuPerkButtonSetup_Postfix));
        PatchOne(harmony, typeof(MenuPerkPreview), "Setup",
            new[] { typeof(CharacterPerk) }, nameof(MenuPerkPreviewSetup_Postfix));
        PatchOne(harmony, typeof(MainMenu), "SelectPerk",
            new[] { typeof(int) }, nameof(MainMenuSelectPerk_Postfix));
    }

    private static void PatchOne(Harmony harmony, Type type, string methodName, Type[] args, string postfixName)
    {
        try
        {
            var method = AccessTools.Method(type, methodName, args);
            if (method == null)
            {
                Log.Warn($"PerkOriginTagPatch: {type.Name}.{methodName} not found - that perk label shows without a mod tag");
                return;
            }
            harmony.Patch(method, postfix: new HarmonyMethod(typeof(PerkOriginTagPatch), postfixName));
            Log.Debug($"PerkOriginTagPatch: patched {type.Name}.{methodName}");
        }
        catch (Exception ex)
        {
            Log.Warn($"PerkOriginTagPatch: patching {type.Name}.{methodName} failed - that perk label shows without a mod tag: {Log.ExceptionText(ex)}");
        }
    }

    // ── Postfixes ───────────────────────────────────────────────────────────────
    // Each postfix is a thin shell around a NoInlining tagger. A member that a game update
    // removed fails when the TAGGER is JIT-compiled, which happens at the call inside the try,
    // so it is caught here. An exception escaping a postfix would propagate into the game's own
    // Setup call and abort MainMenu.RefreshPerkLists halfway through the perk list.

    private static void MenuPerkButtonSetup_Postfix(MenuPerkButton __instance)
    {
        if (!Enabled || _buttonBroken) return;
        try { TagButton(__instance); }
        catch (Exception ex)
        {
            _buttonBroken = true;
            WarnOnce("button-threw", $"tagging MenuPerkButton failed, perk list buttons stay untagged: {Log.ExceptionText(ex)}");
        }
    }

    // __0 = the CharacterPerk argument, bound by position so a renamed parameter cannot unbind it.
    private static void MenuPerkPreviewSetup_Postfix(MenuPerkPreview __instance, CharacterPerk __0)
    {
        if (!Enabled || _previewBroken) return;
        try { TagPreview(__instance, __0); }
        catch (Exception ex)
        {
            _previewBroken = true;
            WarnOnce("preview-threw", $"tagging MenuPerkPreview failed, perk previews stay untagged: {Log.ExceptionText(ex)}");
        }
    }

    // __0 = the index into MainMenu.AllCharacterPerks that SelectPerk just rendered.
    private static void MainMenuSelectPerk_Postfix(MainMenu __instance, int __0)
    {
        if (!Enabled || _detailBroken) return;
        try { TagDetailPanel(__instance, __0); }
        catch (Exception ex)
        {
            _detailBroken = true;
            WarnOnce("detail-threw", $"tagging the selected-perk panel failed, its header stays untagged: {Log.ExceptionText(ex)}");
        }
    }

    // ── Taggers ─────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TagButton(MenuPerkButton button)
    {
        if (!button) return;
        var perk = button.AssociatedPerk;
        if (!TryGetSuffix(perk, out var suffix)) return;

        // TooltipButton.Text is a null-safe property: "" / no-op when ButtonText is unassigned.
        var current = button.Text;
        var tagged = Append(current, suffix);
        if (!ReferenceEquals(tagged, current)) button.Text = tagged;
        Trace(perk, suffix, "MenuPerkButton");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TagPreview(MenuPerkPreview preview, CharacterPerk perk)
    {
        if (!preview) return;
        if (!TryGetSuffix(perk, out var suffix)) return;

        var label = ResolvePreviewTitle(preview);
        if (label)
        {
            var current = label.text;
            var tagged = Append(current, suffix);
            if (!ReferenceEquals(tagged, current)) label.text = tagged;
        }

        // The tooltip title is the same perk name. Title/Content are read through the base type on
        // purpose: MenuPerkPreview declares its own private "Title" (the TMP label above) that
        // hides this string property. An empty title means NoTooltip cancelled it: leave it so.
        TooltipProvider tip = preview;
        var tipTitle = tip.Title;
        if (!string.IsNullOrEmpty(tipTitle))
        {
            var taggedTitle = Append(tipTitle, suffix);
            // "" is the hold text vanilla's own Setup passes for a perk preview.
            if (!ReferenceEquals(taggedTitle, tipTitle)) tip.SetTooltip(taggedTitle, tip.Content, "");
        }
        Trace(perk, suffix, "MenuPerkPreview");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TagDetailPanel(MainMenu menu, int index)
    {
        if (!menu) return;
        var perks = ResolveAllPerks(menu);
        if (perks == null || index < 0 || index >= perks.Count) return;
        var perk = perks[index];
        if (!TryGetSuffix(perk, out var suffix)) return;

        // Public serialized field; vanilla dereferenced it unguarded one statement earlier, so a
        // null here cannot be reached through SelectPerk.
        var label = menu.PerkName;
        if (!label) return;
        var current = label.text;
        var tagged = Append(current, suffix);
        if (!ReferenceEquals(tagged, current)) label.text = tagged;
        Trace(perk, suffix, "MainMenu.PerkName");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>False for a null perk, a vanilla perk (no map entry) and a mod with no tag.</summary>
    private static bool TryGetSuffix(CharacterPerk perk, out string suffix)
    {
        suffix = null;
        if (!perk) return false;
        var uid = perk.UniqueID;
        if (string.IsNullOrEmpty(uid)) return false;
        // No entry = vanilla, or a perk some other loader created: not ours to mark.
        if (!JsonDataLoader.UniqueIdToModName.TryGetValue(uid, out var modName)) return false;
        if (_suffixByModName.TryGetValue(modName ?? "", out suffix)) return true;

        DebugOnce("no-tag:" + modName, $"perk '{uid}' maps to mod '{modName}', which has no tag in the table - left untagged");
        return false;
    }

    /// <summary>Returns <paramref name="text"/> itself (same reference) when it already carries
    /// the suffix, so callers can skip the UI write.</summary>
    private static string Append(string text, string suffix)
    {
        text ??= "";
        return text.EndsWith(suffix, StringComparison.Ordinal) ? text : text + suffix;
    }

    private static TextMeshProUGUI ResolvePreviewTitle(MenuPerkPreview preview)
    {
        if (!_previewTitleResolved)
        {
            _previewTitleResolved = true;
            var field = AccessTools.Field(typeof(MenuPerkPreview), "Title");
            if (field == null)
                WarnOnce("preview-title-field", "MenuPerkPreview has no 'Title' field (renamed by a game update?) - perk preview labels stay untagged; the tooltip title is still tagged");
            else if (!typeof(TextMeshProUGUI).IsAssignableFrom(field.FieldType))
                WarnOnce("preview-title-type", $"MenuPerkPreview.Title is a {field.FieldType.Name}, not a TextMeshProUGUI - perk preview labels stay untagged");
            else
                _previewTitleField = field;
        }
        if (_previewTitleField == null) return null;

        var label = _previewTitleField.GetValue(preview) as TextMeshProUGUI;
        // Not a failure: vanilla guards Title with "if ((bool)Title)", so an icon-only variant of
        // the prefab is a supported shape. A Debug breadcrumb, not a warning on a clean install.
        if (!label) DebugOnce("preview-title-unassigned", "a MenuPerkPreview instance has no Title label assigned (icon-only prefab variant) - only its tooltip title is tagged");
        return label;
    }

    private static List<CharacterPerk> ResolveAllPerks(MainMenu menu)
    {
        if (!_allPerksResolved)
        {
            _allPerksResolved = true;
            var field = AccessTools.Field(typeof(MainMenu), "AllCharacterPerks");
            if (field == null)
                WarnOnce("menu-allperks-field", "MainMenu has no 'AllCharacterPerks' field (renamed by a game update?) - the selected-perk panel header stays untagged");
            else if (!typeof(List<CharacterPerk>).IsAssignableFrom(field.FieldType))
                WarnOnce("menu-allperks-type", $"MainMenu.AllCharacterPerks is a {field.FieldType.Name}, not a List<CharacterPerk> - the selected-perk panel header stays untagged");
            else
                _allPerksField = field;
        }
        if (_allPerksField == null) return null;

        var perks = _allPerksField.GetValue(menu) as List<CharacterPerk>;
        if (perks == null) WarnOnce("menu-allperks-null", "MainMenu.AllCharacterPerks read as null while a perk was being selected - the selected-perk panel header stays untagged");
        return perks;
    }

    // Once per perk, and only under VerboseLogging: RefreshPerkLists re-runs Setup for every perk
    // on each equip, unequip and tab click, and the interpolation alone is not free.
    private static void Trace(CharacterPerk perk, string suffix, string where)
    {
        if (!Log.Verbose) return;
        var uid = perk.UniqueID;
        if (_tracedPerks.Add(where + ":" + uid))
            Log.Debug($"PerkOriginTag: '{uid}' tagged{suffix} on {where}");
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_breadcrumbs.Add(cause)) Log.Warn("PerkOriginTag: " + message);
    }

    private static void DebugOnce(string cause, string message)
    {
        if (_breadcrumbs.Add(cause)) Log.Debug("PerkOriginTag: " + message);
    }
}
