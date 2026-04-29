using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace mod_update_manager
{
    /// <summary>
    /// Simple JSON serialization/deserialization without external dependencies
    /// </summary>
    public static class SimpleJson
    {
        /// <summary>
        /// Serializes a list of ModMapping to JSON
        /// </summary>
        public static string SerializeMappings(List<ModMapping> mappings)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            
            for (int i = 0; i < mappings.Count; i++)
            {
                var m = mappings[i];
                sb.AppendLine("  {");
                sb.AppendLine($"    \"LocalModName\": \"{EscapeString(m.LocalModName ?? "")}\",");
                sb.AppendLine($"    \"NexusModId\": \"{EscapeString(m.NexusModId ?? "")}\",");
                sb.AppendLine($"    \"NexusModName\": \"{EscapeString(m.NexusModName ?? "")}\"");
                sb.Append("  }");
                if (i < mappings.Count - 1) sb.Append(",");
                sb.AppendLine();
            }
            
            sb.AppendLine("]");
            return sb.ToString();
        }

        /// <summary>
        /// Deserializes JSON to a list of ModMapping
        /// </summary>
        public static List<ModMapping> DeserializeMappings(string json)
        {
            var result = new List<ModMapping>();
            
            if (string.IsNullOrEmpty(json)) return result;

            // Remove comments (lines starting with //)
            var lines = json.Split('\n');
            var cleanLines = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("//"))
                {
                    cleanLines.Add(line);
                }
            }
            json = string.Join("\n", cleanLines);

            // Simple regex-based parsing for our specific format
            var objectPattern = new Regex(@"\{[^{}]*\}", RegexOptions.Singleline);
            var matches = objectPattern.Matches(json);

            foreach (Match match in matches)
            {
                var obj = match.Value;
                var mapping = new ModMapping
                {
                    LocalModName = ExtractValue(obj, "LocalModName"),
                    NexusModId = ExtractValue(obj, "NexusModId"),
                    NexusModName = ExtractValue(obj, "NexusModName")
                };
                
                if (!string.IsNullOrEmpty(mapping.LocalModName))
                {
                    result.Add(mapping);
                }
            }

            return result;
        }

        /// <summary>
        /// Parses a simple JSON object for Nexus API responses
        /// </summary>
        public static Dictionary<string, string> ParseJsonObject(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            if (string.IsNullOrEmpty(json)) return result;

            // Match "key": "value" or "key": number patterns
            var stringPattern = new Regex("\"([^\"]+)\"\\s*:\\s*\"([^\"]*)\"");
            var numberPattern = new Regex("\"([^\"]+)\"\\s*:\\s*([0-9.]+)");

            foreach (Match match in stringPattern.Matches(json))
            {
                // Don't overwrite — first occurrence wins (root-level "name" beats nested "user.name")
                if (!result.ContainsKey(match.Groups[1].Value))
                    result[match.Groups[1].Value] = UnescapeString(match.Groups[2].Value);
            }

            foreach (Match match in numberPattern.Matches(json))
            {
                if (!result.ContainsKey(match.Groups[1].Value))
                {
                    result[match.Groups[1].Value] = match.Groups[2].Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Serializes ModInfoJson to JSON string
        /// </summary>
        public static string SerializeModInfo(ModInfoJson modInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"Name\": \"{EscapeString(modInfo.Name ?? "")}\",");
            sb.AppendLine($"  \"Author\": \"{EscapeString(modInfo.Author ?? "")}\",");
            sb.AppendLine($"  \"Version\": \"{EscapeString(modInfo.Version ?? "")}\",");
            sb.AppendLine($"  \"NexusModId\": \"{EscapeString(modInfo.NexusModId ?? "")}\"");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// Deserializes JSON to ModInfoJson
        /// </summary>
        public static ModInfoJson DeserializeModInfo(string json)
        {
            var dict = ParseJsonObject(json);
            return new ModInfoJson
            {
                Name = dict.ContainsKey("Name") ? dict["Name"] : null,
                Author = dict.ContainsKey("Author") ? dict["Author"] : null,
                Version = dict.ContainsKey("Version") ? dict["Version"] : null,
                NexusModId = dict.ContainsKey("NexusModId") ? dict["NexusModId"] : null
            };
        }

        private static string ExtractValue(string json, string key)
        {
            var pattern = new Regex($"\"{key}\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            var match = pattern.Match(json);
            return match.Success ? UnescapeString(match.Groups[1].Value) : null;
        }

        private static string EscapeString(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private static string UnescapeString(string s)
        {
            if (s == null) return "";
            return s.Replace("\\\\", "\\")
                    .Replace("\\\"", "\"")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t");
        }
    }
}
