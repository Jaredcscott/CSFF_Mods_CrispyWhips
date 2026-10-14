using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Motion;

/// <summary>
/// Reads every mod's <c>CardData/Motion/*.json</c>: which sprites move, and how. No image files:
/// the motion is a mesh effect on the card art (<see cref="CardMotionEffect"/>), so a definition
/// costs a few bytes on disk and nothing in texture memory.
///
/// Schema (any file name in CardData/Motion/):
/// {
///   "Profiles": { "slowbreath": { "Breathe": 0.01, "Sway": 0, "Drift": 0, "Period": 5 } },
///   "Sprites": {
///     "CMC_Miller": "npc",                       // a built-in profile: npc, animal, location, plant, still
///     "FT_SeaShore": "location",
///     "Wolf": "slowbreath",                       // a profile this mod declared above
///     "Fox": { "Breathe": 0.02, "Period": 2.6 }   // inline numbers
///   }
/// }
///
/// Keys are SPRITE names (the PNG file name without extension, or a vanilla sprite name), never
/// card UIDs, because an NPC's card is built at runtime and only its portrait sprite is stable.
/// A later mod's entry for the same sprite replaces an earlier one, with one Debug line.
/// </summary>
internal static class CardMotionLoader
{
    public static Dictionary<string, CardMotionProfile> BySprite { get; } =
        new Dictionary<string, CardMotionProfile>(StringComparer.Ordinal);

    public static void LoadAll(List<ModManifest> mods)
    {
        BySprite.Clear();
        int files = 0, bad = 0;
        foreach (var mod in mods)
        {
            if (!mod.HasMotionContent) continue;
            var dir = Path.Combine(mod.DirectoryPath, "CardData", "Motion");
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                files++;
                try
                {
                    if (!LoadFile(mod, file)) bad++;
                }
                catch (Exception ex)
                {
                    bad++;
                    Log.Warn($"CardMotionLoader: {mod.Name}/{Path.GetFileName(file)} not read: {ex.Message}");
                }
            }
        }
        Log.Info($"CardMotionLoader: {BySprite.Count} moving sprite(s) from {files} file(s)"
                 + (bad > 0 ? $", {bad} file(s) with errors (see warnings)" : ""));
    }

    private static bool LoadFile(ModManifest mod, string file)
    {
        var text = File.ReadAllText(file);
        if (MiniJson.Parse(text) is not Dictionary<string, object> root)
        {
            Log.Warn($"CardMotionLoader: {mod.Name}/{Path.GetFileName(file)} is not a JSON object; skipped.");
            return false;
        }

        var local = new Dictionary<string, CardMotionProfile>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetValue("Profiles", out var pr) && pr is Dictionary<string, object> profiles)
        {
            foreach (var kv in profiles)
            {
                if (kv.Value is Dictionary<string, object> body) local[kv.Key] = Parse(kv.Key, body, null);
            }
        }

        bool ok = true;
        if (!root.TryGetValue("Sprites", out var sr) || sr is not Dictionary<string, object> sprites)
        {
            Log.Warn($"CardMotionLoader: {mod.Name}/{Path.GetFileName(file)} has no \"Sprites\" object; nothing moves.");
            return false;
        }
        foreach (var kv in sprites)
        {
            CardMotionProfile p = null;
            if (kv.Value is string name)
            {
                p = local.TryGetValue(name, out var lp) ? lp : CardMotionProfile.BuiltIn(name);
                if (p == null)
                {
                    Log.Warn($"CardMotionLoader: {mod.Name}: sprite '{kv.Key}' names unknown profile '{name}'; skipped.");
                    ok = false;
                    continue;
                }
            }
            else if (kv.Value is Dictionary<string, object> body)
            {
                p = Parse(kv.Key, body, null);
            }
            else
            {
                Log.Warn($"CardMotionLoader: {mod.Name}: sprite '{kv.Key}' has no profile; skipped.");
                ok = false;
                continue;
            }
            if (BySprite.ContainsKey(kv.Key))
                Log.Debug($"CardMotionLoader: {mod.Name} replaces the motion for sprite '{kv.Key}'.");
            BySprite[kv.Key] = p;
        }
        return ok;
    }

    private static CardMotionProfile Parse(string name, Dictionary<string, object> d, CardMotionProfile basis)
    {
        var p = basis?.Copy(name) ?? new CardMotionProfile { Name = name };
        if (d.TryGetValue("Profile", out var b) && b is string bn && CardMotionProfile.BuiltIn(bn) is { } bp)
            p = bp.Copy(name);
        p.Breathe = Num(d, "Breathe", p.Breathe, 0f, 0.05f);
        p.Sway = Num(d, "Sway", p.Sway, 0f, 0.05f);
        p.Drift = Num(d, "Drift", p.Drift, 0f, 0.15f);
        p.Period = Num(d, "Period", p.Period, 0.5f, 120f);
        return p;
    }

    // Clamped so a typo cannot fling the art across the board.
    private static float Num(Dictionary<string, object> d, string key, float fallback, float min, float max)
    {
        if (!d.TryGetValue(key, out var raw) || raw is not double v) return fallback;
        return Mathf.Clamp((float)v, min, max);
    }
}
