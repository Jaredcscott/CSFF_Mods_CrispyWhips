using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using WaterDrivenInfrastructure.Util;

namespace WaterDrivenInfrastructure.Patcher
{
    [Serializable]
    public sealed class MillRaceMapEdge
    {
        public string SourceEnvUid;
        public string SourceLocationUid;
        public int Direction;
        public string DirectionName;
        public string DestinationEnvUid;
        public string DestinationLocationUid;
        public float PathCost;
        public bool HiddenOnInGameMap;
    }

    [Serializable]
    public sealed class MillRaceMapEdgeFile
    {
        public string Version;
        public string GeneratedFrom;
        public MillRaceMapEdge[] Edges;
    }

    internal static class MillRaceMapEdgeProvider
    {
        public static List<MillRaceMapEdge> Load(ManualLogSource logger, IDictionary<string, CardData> explorableLocations)
        {
            var result = new List<MillRaceMapEdge>();
            var filePath = GetMapPath();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                logger?.LogError($"[MillRaceMap] Static map file not found: {filePath ?? "<unknown>"}");
                return result;
            }

            MillRaceMapEdgeFile mapFile;
            try
            {
                if (TryLoadFrameworkMapCache(logger, out mapFile))
                {
                    int cachedEdgesLen = mapFile?.Edges?.Length ?? -1;
                    logger?.LogDebug($"[MillRaceMap] Parse diagnostic: parser=CSFFModFramework.MapCacheRegistry, mapFile={(mapFile == null ? "null" : "ok")}, edgesArrayLen={cachedEdgesLen}");
                }
                else
                {
                    var jsonText = ReadAllTextDetectEncoding(filePath);
                    mapFile = ParseMapFile(jsonText);
                    int rawEdgesLen = mapFile?.Edges?.Length ?? -1;
                    logger?.LogDebug($"[MillRaceMap] Parse diagnostic: parser=MiniJson, encoding={DescribeEncoding(filePath)}, mapFile={(mapFile == null ? "null" : "ok")}, edgesArrayLen={rawEdgesLen}");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError($"[MillRaceMap] Failed to parse static map: {ex.InnerException?.ToString() ?? ex.ToString()}");
                return result;
            }

            int total = mapFile?.Edges?.Length ?? 0;
            int skippedMissing = 0;
            int duplicate = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (mapFile?.Edges != null)
            {
                foreach (var edge in mapFile.Edges)
                {
                    if (edge == null)
                        continue;

                    if (edge.Direction < 0 || edge.Direction > 3)
                    {
                        skippedMissing++;
                        continue;
                    }

                    if (explorableLocations == null ||
                        !explorableLocations.ContainsKey(edge.SourceLocationUid) ||
                        !explorableLocations.ContainsKey(edge.DestinationLocationUid))
                    {
                        skippedMissing++;
                        continue;
                    }

                    var key = RouteKey(edge.SourceLocationUid, edge.Direction, edge.DestinationLocationUid);
                    if (!seen.Add(key))
                    {
                        duplicate++;
                        continue;
                    }

                    result.Add(edge);
                }
            }

            var summary = $"[MillRaceMap] Loaded static map: edges={total}, valid={result.Count}, skippedMissing={skippedMissing}, duplicate={duplicate}, version={mapFile?.Version ?? "unknown"}";
            if (result.Count == 0 || skippedMissing > 0)
                logger?.LogWarning(summary);
            else
                logger?.LogDebug(summary);
            return result;
        }

        private static string GetMapPath()
        {
            var pluginPath = Plugin.Instance?.Info?.Location;
            if (string.IsNullOrEmpty(pluginPath))
                return null;

            var pluginDir = Path.GetDirectoryName(pluginPath);
            return string.IsNullOrEmpty(pluginDir) ? null : Path.Combine(pluginDir, "Data", "MillRaceMapEdges.json");
        }

        private static string ReadAllTextDetectEncoding(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static string DescribeEncoding(string filePath)
        {
            try
            {
                var bytes = File.ReadAllBytes(filePath);
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    return "utf8-bom";
                if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                    return "utf16-le";
                if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                    return "utf16-be";
            }
            catch { }
            return "utf8";
        }

        private static MillRaceMapEdgeFile ParseMapFile(string jsonText)
        {
            if (!(MiniJson.Parse(jsonText) is Dictionary<string, object> root))
                return null;

            return ParseMapRoot(root);
        }

        private static MillRaceMapEdgeFile ParseMapRoot(Dictionary<string, object> root)
        {
            var mapFile = new MillRaceMapEdgeFile
            {
                Version = GetString(root, "Version"),
                GeneratedFrom = GetString(root, "GeneratedFrom")
            };

            if (!root.TryGetValue("Edges", out var edgesObj) || !(edgesObj is List<object> edgeItems))
            {
                mapFile.Edges = Array.Empty<MillRaceMapEdge>();
                return mapFile;
            }

            var edges = new List<MillRaceMapEdge>(edgeItems.Count);
            foreach (var item in edgeItems)
            {
                if (!(item is Dictionary<string, object> edgeRoot))
                    continue;

                edges.Add(new MillRaceMapEdge
                {
                    SourceEnvUid = GetString(edgeRoot, "SourceEnvUid"),
                    SourceLocationUid = GetString(edgeRoot, "SourceLocationUid"),
                    Direction = GetInt(edgeRoot, "Direction"),
                    DirectionName = GetString(edgeRoot, "DirectionName"),
                    DestinationEnvUid = GetString(edgeRoot, "DestinationEnvUid"),
                    DestinationLocationUid = GetString(edgeRoot, "DestinationLocationUid"),
                    PathCost = GetFloat(edgeRoot, "PathCost"),
                    HiddenOnInGameMap = GetBool(edgeRoot, "HiddenOnInGameMap")
                });
            }

            mapFile.Edges = edges.ToArray();
            return mapFile;
        }

        private static bool TryLoadFrameworkMapCache(ManualLogSource logger, out MillRaceMapEdgeFile mapFile)
        {
            mapFile = null;
            try
            {
                var registryType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType("CSFFModFramework.Api.MapCacheRegistry", throwOnError: false))
                    .FirstOrDefault(type => type != null);
                if (registryType == null)
                    return false;

                var tryGet = registryType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "TryGet" && method.GetParameters().Length == 3);
                if (tryGet == null)
                    return false;

                var args = new object[] { Plugin.PluginName, "Data/MillRaceMapEdges.json", null };
                if (!(tryGet.Invoke(null, args) is bool found) || !found || args[2] == null)
                    return false;

                mapFile = ParseFrameworkCache(args[2]);
                return mapFile != null;
            }
            catch (Exception ex)
            {
                logger?.LogDebug($"[MillRaceMap] Framework map cache unavailable, falling back to local parse: {ex.InnerException?.Message ?? ex.Message}");
                mapFile = null;
                return false;
            }
        }

        private static MillRaceMapEdgeFile ParseFrameworkCache(object cacheInfo)
        {
            if (cacheInfo == null)
                return null;

            var cacheType = cacheInfo.GetType();
            var tryGetArray = cacheType.GetMethod("TryGetArray", BindingFlags.Public | BindingFlags.Instance);
            if (tryGetArray == null)
                return null;

            var args = new object[] { "Edges", null };
            if (!(tryGetArray.Invoke(cacheInfo, args) is bool hasEdges) || !hasEdges || !(args[1] is IEnumerable edgeItems))
                return new MillRaceMapEdgeFile
                {
                    Version = GetCacheString(cacheInfo, "Version"),
                    GeneratedFrom = GetCacheString(cacheInfo, "GeneratedFrom"),
                    Edges = Array.Empty<MillRaceMapEdge>()
                };

            var edges = new List<MillRaceMapEdge>();
            foreach (var item in edgeItems)
            {
                if (!(item is Dictionary<string, object> edgeRoot))
                    continue;

                edges.Add(new MillRaceMapEdge
                {
                    SourceEnvUid = GetString(edgeRoot, "SourceEnvUid"),
                    SourceLocationUid = GetString(edgeRoot, "SourceLocationUid"),
                    Direction = GetInt(edgeRoot, "Direction"),
                    DirectionName = GetString(edgeRoot, "DirectionName"),
                    DestinationEnvUid = GetString(edgeRoot, "DestinationEnvUid"),
                    DestinationLocationUid = GetString(edgeRoot, "DestinationLocationUid"),
                    PathCost = GetFloat(edgeRoot, "PathCost"),
                    HiddenOnInGameMap = GetBool(edgeRoot, "HiddenOnInGameMap")
                });
            }

            return new MillRaceMapEdgeFile
            {
                Version = GetCacheString(cacheInfo, "Version"),
                GeneratedFrom = GetCacheString(cacheInfo, "GeneratedFrom"),
                Edges = edges.ToArray()
            };
        }

        private static string GetCacheString(object cacheInfo, string key)
        {
            var prop = cacheInfo.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance);
            var propValue = prop?.GetValue(cacheInfo, null) as string;
            if (!string.IsNullOrEmpty(propValue))
                return propValue;

            var getString = cacheInfo.GetType().GetMethod("GetString", BindingFlags.Public | BindingFlags.Instance);
            return getString?.Invoke(cacheInfo, new object[] { key, null }) as string;
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            return data.TryGetValue(key, out var value) ? value as string : null;
        }

        private static int GetInt(Dictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return 0;
            if (value is int intValue) return intValue;
            if (value is long longValue) return (int)longValue;
            if (value is double doubleValue) return (int)doubleValue;
            return int.TryParse(value.ToString(), out var parsed) ? parsed : 0;
        }

        private static float GetFloat(Dictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return 0f;
            if (value is float floatValue) return floatValue;
            if (value is double doubleValue) return (float)doubleValue;
            if (value is long longValue) return longValue;
            return float.TryParse(value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0f;
        }

        private static bool GetBool(Dictionary<string, object> data, string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return false;
            if (value is bool boolValue) return boolValue;
            return bool.TryParse(value.ToString(), out var parsed) && parsed;
        }

        private static string RouteKey(string sourceLocationUid, int direction, string destinationLocationUid)
        {
            return $"{sourceLocationUid}:{direction}:{destinationLocationUid}";
        }
    }
}