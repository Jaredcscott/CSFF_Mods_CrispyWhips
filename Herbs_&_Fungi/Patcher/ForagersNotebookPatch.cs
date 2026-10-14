using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CSFFModFramework.Api;
using CSFFModFramework.Util;
using HarmonyLib;

namespace Herbs_And_Fungi.Patcher;

/// <summary>
/// Forager's Notebook (feature 43, part c). When a wild plant or mushroom of this mod is created, the
/// carried notebook(s) rewrite their own saved custom description (<c>InGameCardBase.SetCustomDescription</c>,
/// which the game saves in <c>CardSaveData.CustomDesc</c> and restores on load) so the card text lists each
/// species once, under the place it was first found. The text IS the saved state: no stat, no second store.
/// Subscribed once per process (the event is static and never re-fired per run), so reloads do not stack handlers.
/// </summary>
internal static class ForagersNotebookPatch
{
    internal const string NotebookUid = "herbs_fungi_foragers_notebook";

    // Raw wild finds the notebook records. Cooked, dried, ground and unidentified variants are different cards
    // and are deliberately not listed.
    internal static readonly string[] SpeciesUids =
    {
        "herbs_fungi_morel_mushroom", "herbs_fungi_lions_mane", "herbs_fungi_king_oyster", "herbs_fungi_golden_oyster",
        "herbs_fungi_chanterelle", "herbs_fungi_reishi", "herbs_fungi_puffball", "herbs_fungi_chicken_of_woods",
        "herbs_fungi_black_trumpet", "herbs_fungi_shiitake", "herbs_fungi_chaga", "herbs_fungi_death_cap",
        "herbs_fungi_velvet_shank", "herbs_fungi_truffle", "herbs_fungi_ginseng", "herbs_fungi_ginger",
        "herbs_fungi_yarrow", "herbs_fungi_wild_flowers", "herbs_fungi_dandelion", "herbs_fungi_common_plantain",
        "herbs_fungi_chamomile", "herbs_fungi_blackcurrant", "herbs_fungi_redcurrant", "herbs_fungi_lingonberry",
        "herbs_fungi_cloudberry", "herbs_fungi_watercress",
        "herbs_fungi_camas_bulb", "herbs_fungi_fiddleheads", "herbs_fungi_thistle",
        "herbs_fungi_mandrake_root", "herbs_fungi_rhubarb", "herbs_fungi_mustard_seeds",
        "herbs_fungi_horseradish_root", "herbs_fungi_chicory_root", "herbs_fungi_goosefoot",
        "herbs_fungi_water_lily_rhizome", "herbs_fungi_crabapple", "herbs_fungi_pear", "herbs_fungi_plum",
        "herbs_fungi_chestnut", "herbs_fungi_hazelnut", "herbs_fungi_sloe", "herbs_fungi_elderflower", "herbs_fungi_elderberry",
    };

    // Tunable placeholder: the inspection pane cuts its tail at about 15 lines, so after this many place lines
    // a find at a new place is filed under the last place line instead.
    internal const int MaxPlaceLines = 12;

    private static readonly HashSet<string> Species = new HashSet<string>(SpeciesUids);
    private static bool _subscribed;
    private static bool _warnedNoAllCards;
    private static bool _warnedNoSetter;
    private static bool _warnedNoDesc;
    private static bool _warnedDescType;

    public static void ApplyPatch(Harmony harmony)
    {
        try
        {
            if (_subscribed) return;
            _subscribed = true;
            SpawnService.CardSpawned += OnCardSpawned;
            Plugin.Logger.LogDebug("[ForagersNotebook] Subscribed to CardSpawned.");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[ForagersNotebook] Failed to apply: {ex}");
        }
    }

    private static void OnCardSpawned(object card, string uid)
    {
        try
        {
            if (uid == null || !Species.Contains(uid)) return;

            var gm = CardUtil.GetGameManagerInstance();
            if (gm == null) return;
            var allCardsField = CardUtil.GetCachedField(gm.GetType(), "AllCards");
            if (!(allCardsField?.GetValue(gm) is IEnumerable all))
            {
                if (!_warnedNoAllCards)
                {
                    _warnedNoAllCards = true;
                    Plugin.Logger.LogWarning("[ForagersNotebook] GameManager.AllCards not readable; notebook will not update.");
                }
                return;
            }

            var notebooks = new List<object>();
            foreach (var c in all)
            {
                if (c == null) continue;
                if (CardUtil.GetCardUniqueId(c) == NotebookUid) notebooks.Add(c);
            }
            if (notebooks.Count == 0) return;

            string speciesName = DisplayName(CardUtil.GetCardData(card)) ?? uid;
            string placeName = DisplayName(CardUtil.GetCardDataById(GameQuery.CurrentEnvironmentUniqueId ?? ""))
                               ?? "Somewhere";

            foreach (var nb in notebooks)
                Record(nb, speciesName, placeName);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[ForagersNotebook] OnCardSpawned failed: {ex.InnerException?.ToString() ?? ex.ToString()}");
        }
    }

    private static string DisplayName(object cardData)
    {
        if (cardData == null) return null;
        var name = CardUtil.GetMemberValue(cardData, "CardName");
        var s = name?.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static void Record(object notebook, string speciesName, string placeName)
    {
        // Resolve CustomDesc explicitly: a null VALUE is a fresh notebook (no entries yet), but a missing MEMBER
        // (a game update renamed it) must not read as empty, or the write below would replace every earlier
        // entry with only this find. Skip the write instead, and say so once.
        var type = notebook.GetType();
        var prop = CardUtil.GetCachedProperty(type, "CustomDesc");
        var field = prop == null ? CardUtil.GetCachedField(type, "CustomDesc") : null;
        if (prop == null && field == null)
        {
            if (!_warnedNoDesc)
            {
                _warnedNoDesc = true;
                Plugin.Logger.LogWarning($"[ForagersNotebook] {type.Name}.CustomDesc not found (a game update may have renamed it); notebook will not update, so its saved entries are kept.");
            }
            return;
        }
        var raw = prop != null ? prop.GetValue(notebook, null) : field.GetValue(notebook);
        if (raw != null && !(raw is string))
        {
            if (!_warnedDescType)
            {
                _warnedDescType = true;
                Plugin.Logger.LogWarning($"[ForagersNotebook] {type.Name}.CustomDesc is a {raw.GetType().Name}, not text; notebook will not update, so its saved entries are kept.");
            }
            return;
        }
        var current = raw as string ?? "";
        var updated = Merge(current, DisplayName(CardUtil.GetCardData(notebook)) ?? "Forager's Notebook", speciesName, placeName);
        if (updated == null || updated == current) return;

        var setter = CardUtil.FindMethodBySignature(notebook.GetType(), "SetCustomDescription", "String");
        if (setter == null)
        {
            if (!_warnedNoSetter)
            {
                _warnedNoSetter = true;
                Plugin.Logger.LogWarning("[ForagersNotebook] InGameCardBase.SetCustomDescription(string) not found; notebook will not update.");
            }
            return;
        }
        setter.Invoke(notebook, new object[] { updated });
        Plugin.Logger.LogDebug($"[ForagersNotebook] Recorded {speciesName} at {placeName}.");
    }

    /// <summary>
    /// Pure text step, kept free of game types so it can be reasoned about on its own. The first line is the card
    /// title; every later line is "Place: Species, Species". Returns the new text, or null when the species is
    /// already listed anywhere (first find wins).
    /// </summary>
    internal static string Merge(string current, string title, string species, string place)
    {
        var places = new List<string>();
        var found = new Dictionary<string, List<string>>();
        if (!string.IsNullOrEmpty(current))
        {
            var lines = current.Replace("\r\n", "\n").Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                int cut = line.IndexOf(": ", StringComparison.Ordinal);
                if (cut <= 0) continue;
                var p = line.Substring(0, cut);
                if (!found.TryGetValue(p, out var list)) { list = new List<string>(); found[p] = list; places.Add(p); }
                foreach (var sp in line.Substring(cut + 2).Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
                    list.Add(sp);
            }
        }
        if (found.Values.Any(l => l.Contains(species))) return null;

        string target = place;
        if (!found.ContainsKey(target) && places.Count >= MaxPlaceLines)
            target = places[places.Count - 1];
        if (!found.TryGetValue(target, out var dest)) { dest = new List<string>(); found[target] = dest; places.Add(target); }
        dest.Add(species);

        var sb = new StringBuilder(title);
        foreach (var p in places)
            sb.Append('\n').Append(p).Append(": ").Append(string.Join(", ", found[p]));
        return sb.ToString();
    }
}
