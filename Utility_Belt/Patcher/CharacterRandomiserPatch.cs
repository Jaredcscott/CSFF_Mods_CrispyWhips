using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Utility_Belt.Patcher;

/// <summary>
/// Character randomiser (plan U4). A postfix on MainMenu.SetupCharacterCreationScreen, the method that builds
/// the character-creation perk screen (it ends by calling RefreshPerkLists), adds a "Randomise" button under
/// the Submit button. Pressing it re-rolls the character's perks through the screen's OWN private
/// EquipPerk(int, bool), UnequipPerk(int, bool), CheckThatMandatoryPerksAreValid and RefreshPerkLists, never by
/// writing CharacterPerks strings (the substring match in CLAUDE.md "Perks"): the rating band the character
/// had before the roll is kept, at most one perk per PerkGroup, only perks the player has unlocked.
/// </summary>
/// <remarks>
/// What is rolled: every perk outside the groups below. What is not: perks of a mandatory group (skin, hair, eyes,
/// the start location and so on, which the game fills itself), perks of a start place or start season group, and any
/// perk that overrides the start environment or the weather, so the start place and season stay as the player set
/// them (plan Q10). Perks from a mod whose
/// ModInfo.json Description says "not for distribution" are left out; with no framework dependency those perk ids
/// are read straight from that mod's CharacterPerk/*.json under BepInEx/plugins. The button is a clone of the
/// Submit button with its click wiring replaced; it does nothing until pressed.
/// </remarks>
internal static class CharacterRandomiserPatch
{
    internal const string ButtonName = "UB_RandomiseButton";
    private const int MaxAttempts = 400;

    // The vanilla start groups are Pkg_StartingLocation, Pkg_StartingSeason and Pkg_SeasonStart; a mod's start perks live in them too.
    private static readonly Regex StartGroup = new("Start|Season|Location", RegexOptions.IgnoreCase);

    private static readonly HashSet<string> _warned = new();
    private static bool _resolved;
    private static bool _resolveOk;
    private static FieldInfo _fAllPerks, _fUnlocked, _fEquipped, _fGroups, _fSelected;
    private static MethodInfo _mEquip, _mUnequip, _mCheckMandatory, _mRefresh, _mSelect, _mIsMandatory;
    private static HashSet<string> _excludedPerkIds;

    internal static void ApplyPatch(Harmony harmony)
    {
        var setup = AccessTools.Method(typeof(MainMenu), "SetupCharacterCreationScreen");
        if (setup == null)
        {
            Plugin.Logger.LogWarning("[UB] MainMenu.SetupCharacterCreationScreen not found; the Randomise button is not added.");
            return;
        }

        harmony.Patch(setup, postfix: new HarmonyMethod(typeof(CharacterRandomiserPatch), nameof(SetupPostfix)));
    }

    private static void SetupPostfix(MainMenu __instance)
    {
        try
        {
            EnsureButton(__instance);
        }
        catch (Exception ex)
        {
            WarnOnce("setup:" + ex.GetType().Name, $"[UB] Could not add the Randomise button ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void EnsureButton(MainMenu menu)
    {
        if (menu == null || menu.CharacterBioField == null)
        {
            WarnOnce("no-bio", "[UB] The character-creation screen has no bio field to find the Submit button from; no Randomise button.");
            return;
        }

        var info = menu.CharacterBioField.transform.parent;
        if (info == null)
        {
            WarnOnce("no-info", "[UB] The character-creation screen's bio field has no parent; no Randomise button.");
            return;
        }

        var existing = info.Find(ButtonName);
        if (!Plugin.ShowRandomiseButton.Value)
        {
            if (existing != null && existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(false);
            }

            return;
        }

        if (existing != null)
        {
            if (!existing.gameObject.activeSelf)
            {
                existing.gameObject.SetActive(true);
            }

            SetLabel(existing);
            return;
        }

        var save = info.Find("SaveButton") as RectTransform;
        if (save == null)
        {
            WarnOnce("no-save", "[UB] The Submit button (CharacterInfo/SaveButton) was not found; no Randomise button.");
            return;
        }

        var clone = UnityEngine.Object.Instantiate(save.gameObject, info);
        clone.name = ButtonName;

        // The clone's text would be rewritten by the game's localiser on Start; remove the localiser.
        foreach (var loc in clone.GetComponentsInChildren<LocalizedStaticText>(true))
        {
            UnityEngine.Object.DestroyImmediate(loc);
        }

        // Replace every click handler (the inspector-wired SaveCustomCharacter is persistent) with ours.
        var pressed = false;
        foreach (var b in clone.GetComponentsInChildren<Button>(true))
        {
            b.onClick = new Button.ButtonClickedEvent();
            if (!pressed)
            {
                b.onClick.AddListener(() => OnPress(menu));
                pressed = true;
            }
        }

        if (!pressed)
        {
            WarnOnce("no-button", "[UB] The Submit button has no Button component to clone; no Randomise button.");
            UnityEngine.Object.Destroy(clone);
            return;
        }

        // Sit under Submit, the same width and under half its height, in the gap above the perk panel.
        var rt = (RectTransform)clone.transform;
        var h = save.sizeDelta.y * 0.45f;
        rt.anchorMin = save.anchorMin;
        rt.anchorMax = save.anchorMax;
        rt.pivot = save.pivot;
        rt.sizeDelta = new Vector2(save.sizeDelta.x, h);
        var drop = save.sizeDelta.y * save.pivot.y + 3f + h * save.pivot.y;
        rt.anchoredPosition = save.anchoredPosition + new Vector2(0f, -drop);
        rt.localScale = save.localScale;

        SetLabel(clone.transform);
        clone.SetActive(true);
        Plugin.Logger.LogDebug("[UB] Randomise button added to the character-creation screen.");
    }

    private static void SetLabel(Transform root)
    {
        var text = OverlayText.Get(OverlayText.RandomiseKey, OverlayText.RandomiseDefault);
        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            t.text = text;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The roll
    // ---------------------------------------------------------------------------------------------

    private static void OnPress(MainMenu menu)
    {
        try
        {
            if (!Resolve())
            {
                return;
            }

            var all = (List<CharacterPerk>)_fAllPerks.GetValue(menu);
            var unlocked = (List<CharacterPerk>)_fUnlocked.GetValue(menu);
            var equipped = (List<CharacterPerk>)_fEquipped.GetValue(menu);
            var groups = (List<PerkGroup>)_fGroups.GetValue(menu);
            var rater = menu.CharacterInfos;
            if (all == null || unlocked == null || equipped == null || groups == null || rater == null)
            {
                WarnOnce("null-state", "[UB] Randomise: the perk screen's state is not ready (a perk list or the rating table is null); nothing was changed.");
                return;
            }

            var excluded = ExcludedPerkIds();

            // Perks the roll never touches: those of a mandatory group the game fills itself (skin, hair, eyes, start
            // location) and those of a start place or start season group, so the start place and season stay as set.
            bool Keep(CharacterPerk p) => IsMandatory(menu, p) || groups.Any(g => g != null && StartGroup.IsMatch(g.name ?? string.Empty) && g.HasPerk(p));

            // The band the character is in now, and the perks that stay (mandatory groups).
            var before = equipped.Sum(p => p != null ? p.DifficultyRating : 0);
            var band = rater.GetRating(before);
            var retained = equipped.Where(p => p != null && Keep(p)).ToList();
            var retainedSum = retained.Sum(p => p.DifficultyRating);
            var used = new HashSet<PerkGroup>();
            foreach (var p in retained)
            {
                foreach (var g in groups)
                {
                    if (g != null && g.HasPerk(p))
                    {
                        used.Add(g);
                    }
                }
            }

            // The pool: unlocked, alive, not mandatory, not a start-place or weather perk, not from a dev-only mod.
            var pool = new List<(CharacterPerk perk, PerkGroup[] groups)>();
            foreach (var p in all)
            {
                if (p == null || !p.IsAlive() || !unlocked.Contains(p) || Keep(p)
                    || p.OverrideEnvironment != null || p.OverrideWeather != null
                    || (!string.IsNullOrEmpty(p.UniqueID) && excluded.Contains(p.UniqueID)))
                {
                    continue;
                }

                pool.Add((p, groups.Where(g => g != null && g.HasPerk(p)).ToArray()));
            }

            // How many perks to roll: as many non-mandatory perks as the character has now (so Randomise swaps what
            // you have for something else), or 4 to 8 when it has none. The rating bands are very wide (the middle
            // one runs from -2000 to 2000), so the count, not the band, is what keeps a roll a believable character.
            var wanted = equipped.Count(p => p != null && !Keep(p));
            if (wanted == 0)
            {
                wanted = UnityEngine.Random.Range(4, 9);
            }

            // Draw: shuffle, take a perk when none of its groups is used yet (at most one per group) until the count
            // is reached, and accept the first roll whose summed rating lands in the recorded band. Keep the closest
            // if none does.
            List<CharacterPerk> best = null;
            var bestDistance = int.MaxValue;
            var landed = false;
            var attempts = 0;
            for (; attempts < MaxAttempts; attempts++)
            {
                var order = pool.OrderBy(_ => UnityEngine.Random.value).ToList();
                var taken = new List<CharacterPerk>();
                var usedNow = new HashSet<PerkGroup>(used);
                var sum = retainedSum;
                foreach (var (perk, pg) in order)
                {
                    if (taken.Count >= wanted)
                    {
                        break;
                    }

                    if (pg.Any(g => usedNow.Contains(g)))
                    {
                        continue;
                    }

                    taken.Add(perk);
                    sum += perk.DifficultyRating;
                    foreach (var g in pg)
                    {
                        usedNow.Add(g);
                    }
                }

                var d = Distance(rater, band, sum);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = taken;
                }

                if (d == 0)
                {
                    landed = true;
                    attempts++;
                    break;
                }
            }

            if (best == null)
            {
                WarnOnce("no-roll", "[UB] Randomise: no roll was produced; nothing was changed.");
                return;
            }

            if (!landed)
            {
                WarnOnce("off-band", $"[UB] Randomise: no roll landed in the '{band}' band after {MaxAttempts} tries; the closest one (distance {bestDistance}) was kept. Said once.");
            }

            // Apply through the screen's own methods: unequip every non-mandatory perk, equip the draw, refill the
            // mandatory groups, redraw. Indexes are looked up fresh each call (RefreshPerkLists re-sorts the list).
            foreach (var p in equipped.Where(p => p != null && !Keep(p)).ToList())
            {
                var i = all.IndexOf(p);
                if (i >= 0)
                {
                    _mUnequip.Invoke(menu, new object[] { i, false });
                }
            }

            foreach (var p in best)
            {
                var i = all.IndexOf(p);
                if (i >= 0)
                {
                    _mEquip.Invoke(menu, new object[] { i, false });
                }
            }

            _mCheckMandatory.Invoke(menu, null);
            _mRefresh.Invoke(menu, new object[] { false });

            // Keep an open perk panel's Equip and Unequip buttons in step (only when a perk was already selected, so
            // this never opens an empty panel).
            var selected = (int)_fSelected.GetValue(menu);
            if (selected >= 0 && selected < all.Count)
            {
                _mSelect.Invoke(menu, new object[] { selected });
            }

            var after = equipped.Sum(p => p != null ? p.DifficultyRating : 0);
            Plugin.Logger.LogDebug($"[UB] Randomise: band '{band}' ({before}), rolled {best.Count} of {wanted} perk(s), rating now {after} ('{rater.GetRating(after)}'), {attempts} attempt(s){(landed ? "" : ", NOT in band")}.");
        }
        catch (Exception ex)
        {
            WarnOnce("press:" + ex.GetType().Name, $"[UB] Randomise failed ({(ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex)}); the perks may be half changed, press it again or edit by hand.");
        }
    }

    /// <summary>0 when the summed rating is in the target band, else how far it is from the band's range.</summary>
    private static int Distance(GlobalCharacterInfo rater, string band, int sum)
    {
        if (rater.GetRating(sum) == band)
        {
            return 0;
        }

        var best = int.MaxValue;
        var table = rater.DifficultyRatings;
        if (table == null)
        {
            return 1;
        }

        foreach (var entry in table)
        {
            if ((string)entry.Label != band)
            {
                continue;
            }

            var lo = Math.Min(entry.Range.x, entry.Range.y);
            var hi = Math.Max(entry.Range.x, entry.Range.y);
            var d = sum < lo ? lo - sum : sum >= hi ? sum - (hi - 1) : 0;
            best = Math.Min(best, d);
        }

        return best == int.MaxValue ? 1 : Math.Max(1, best);
    }

    private static bool IsMandatory(MainMenu menu, CharacterPerk perk)
    {
        return (bool)_mIsMandatory.Invoke(menu, new object[] { perk });
    }

    private static bool Resolve()
    {
        if (_resolved)
        {
            return _resolveOk;
        }

        _resolved = true;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = typeof(MainMenu);
        _fAllPerks = t.GetField("AllCharacterPerks", F);
        _fUnlocked = t.GetField("UnlockedPerks", F);
        _fEquipped = t.GetField("CurrentlyEquippedPerks", F);
        _fGroups = t.GetField("AllPerkGroups", F);
        _fSelected = t.GetField("SelectedPerk", F);
        _mEquip = t.GetMethod("EquipPerk", F, null, new[] { typeof(int), typeof(bool) }, null);
        _mUnequip = t.GetMethod("UnequipPerk", F, null, new[] { typeof(int), typeof(bool) }, null);
        _mCheckMandatory = t.GetMethod("CheckThatMandatoryPerksAreValid", F, null, Type.EmptyTypes, null);
        _mRefresh = t.GetMethod("RefreshPerkLists", F, null, new[] { typeof(bool) }, null);
        _mSelect = t.GetMethod("SelectPerk", F, null, new[] { typeof(int) }, null);
        _mIsMandatory = t.GetMethod("IsMandatoryPerk", F, null, new[] { typeof(CharacterPerk) }, null);

        string missing = null;
        void Need(object member, string name)
        {
            if (member == null)
            {
                missing = missing == null ? name : missing + ", " + name;
            }
        }

        Need(_fAllPerks, "AllCharacterPerks");
        Need(_fUnlocked, "UnlockedPerks");
        Need(_fEquipped, "CurrentlyEquippedPerks");
        Need(_fGroups, "AllPerkGroups");
        Need(_fSelected, "SelectedPerk");
        Need(_mEquip, "EquipPerk(int,bool)");
        Need(_mUnequip, "UnequipPerk(int,bool)");
        Need(_mCheckMandatory, "CheckThatMandatoryPerksAreValid()");
        Need(_mRefresh, "RefreshPerkLists(bool)");
        Need(_mSelect, "SelectPerk(int)");
        Need(_mIsMandatory, "IsMandatoryPerk(CharacterPerk)");
        if (missing != null)
        {
            WarnOnce("resolve", $"[UB] Randomise: the game's perk screen no longer has: {missing}. The button does nothing.");
            return _resolveOk = false;
        }

        return _resolveOk = true;
    }

    // ---------------------------------------------------------------------------------------------
    // Dev-only mods' perks
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The CharacterPerk UniqueIDs of every installed mod whose ModInfo.json Description says "not for distribution"
    /// (the repo's marker for a dev-only mod, such as MasterTesting), read from that mod's own CharacterPerk JSON
    /// under BepInEx/plugins (the folder and one level below). Computed once per process.
    /// </summary>
    private static HashSet<string> ExcludedPerkIds()
    {
        if (_excludedPerkIds != null)
        {
            return _excludedPerkIds;
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var plugins = BepInEx.Paths.PluginPath;
            var mods = 0;
            foreach (var dir in ModFolders(plugins))
            {
                string desc;
                try
                {
                    var text = File.ReadAllText(Path.Combine(dir, "ModInfo.json"));
                    var m = Regex.Match(text, "\"Description\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    desc = m.Success ? m.Groups[1].Value : string.Empty;
                }
                catch (Exception ex)
                {
                    Plugin.Logger.LogDebug($"[UB] Randomise: could not read {dir}/ModInfo.json ({ex.GetType().Name}).");
                    continue;
                }

                if (desc.IndexOf("not for distribution", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                mods++;
                var perks = Path.Combine(dir, "CharacterPerk");
                if (!Directory.Exists(perks))
                {
                    continue;
                }

                foreach (var file in Directory.GetFiles(perks, "*.json", SearchOption.AllDirectories))
                {
                    var m = Regex.Match(File.ReadAllText(file), "\"UniqueID\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success)
                    {
                        ids.Add(m.Groups[1].Value);
                    }
                }
            }

            Plugin.Logger.LogDebug($"[UB] Randomise: {mods} dev-only mod(s) found, {ids.Count} perk id(s) left out of the roll.");
        }
        catch (Exception ex)
        {
            WarnOnce("exclude:" + ex.GetType().Name, $"[UB] Randomise: could not scan for dev-only mods' perks ({ex.GetType().Name}: {ex.Message}); none are left out of the roll.");
        }

        return _excludedPerkIds = ids;
    }

    private static IEnumerable<string> ModFolders(string plugins)
    {
        if (!Directory.Exists(plugins))
        {
            yield break;
        }

        foreach (var d1 in Directory.GetDirectories(plugins))
        {
            if (File.Exists(Path.Combine(d1, "ModInfo.json")))
            {
                yield return d1;
            }

            foreach (var d2 in Directory.GetDirectories(d1))
            {
                if (File.Exists(Path.Combine(d2, "ModInfo.json")))
                {
                    yield return d2;
                }
            }
        }
    }

    private static void WarnOnce(string cause, string message)
    {
        if (_warned.Add(cause))
        {
            Plugin.Logger.LogWarning(message);
        }
    }
}
