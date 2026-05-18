using CSFFModFramework.Loading;

namespace CSFFModFramework.Api;

/// <summary>
/// Read-only descriptor for a parsed JSON map cache supplied by a mod.
/// Cache schemas are intentionally free-form; use the helper methods for
/// common top-level metadata and cast nested objects according to your schema.
/// </summary>
public sealed class MapCacheInfo
{
    private readonly MapCacheRecord _inner;

    internal MapCacheInfo(MapCacheRecord inner) { _inner = inner; }

    public string ModName => _inner.ModName;
    public string RelativePath => _inner.RelativePath;
    public string AbsolutePath => _inner.AbsolutePath;
    public string Version => _inner.Version;
    public string GeneratedFrom => _inner.GeneratedFrom;
    public DateTime SourceLastWriteUtc => _inner.SourceLastWriteUtc;
    public long SourceLength => _inner.SourceLength;
    public IReadOnlyDictionary<string, object> Root => _inner.Root;

    public string GetString(string key, string fallback = null)
    {
        if (!TryGetValue(key, out var value) || value == null)
            return fallback;
        return value as string ?? value.ToString();
    }

    public int GetInt(string key, int fallback = 0)
    {
        if (!TryGetValue(key, out var value) || value == null)
            return fallback;
        if (value is int intValue) return intValue;
        if (value is long longValue) return (int)longValue;
        if (value is double doubleValue) return (int)doubleValue;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public float GetFloat(string key, float fallback = 0f)
    {
        if (!TryGetValue(key, out var value) || value == null)
            return fallback;
        if (value is float floatValue) return floatValue;
        if (value is double doubleValue) return (float)doubleValue;
        if (value is long longValue) return longValue;
        return float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    public bool GetBool(string key, bool fallback = false)
    {
        if (!TryGetValue(key, out var value) || value == null)
            return fallback;
        if (value is bool boolValue) return boolValue;
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : fallback;
    }

    public bool TryGetArray(string key, out IReadOnlyList<object> items)
    {
        if (TryGetValue(key, out var value) && value is List<object> list)
        {
            items = list;
            return true;
        }
        items = Array.Empty<object>();
        return false;
    }

    public bool TryGetObject(string key, out IReadOnlyDictionary<string, object> value)
    {
        if (TryGetValue(key, out var raw) && raw is Dictionary<string, object> dict)
        {
            value = dict;
            return true;
        }
        value = null;
        return false;
    }

    public bool TryGetValue(string key, out object value)
    {
        value = null;
        return !string.IsNullOrEmpty(key) && _inner.Root != null && _inner.Root.TryGetValue(key, out value);
    }
}