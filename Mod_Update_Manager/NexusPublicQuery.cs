using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace mod_update_manager
{
    /// <summary>One mod as Nexus's public query lists it.</summary>
    public sealed class NexusPublicMod
    {
        public int ModId;
        public string Name;
        public string Version;
        public string Status;
        public string UpdatedAt;
        public string Summary;
        public int Endorsements;
        /// <summary>Whether Nexus marks the mod adult content: true or false as the answer says, and null when
        /// the answer did not carry the field or carried null. Only the New and updated list asks for it.</summary>
        public bool? AdultContent;
    }

    /// <summary>
    /// What one public query came back with: the mods it listed (Ok), or why it cannot be used
    /// (Failure). A failed answer settles no mod; PublicCheckPlan.Decide sends every mod it was asked
    /// about to the per-mod request when a key is set.
    /// </summary>
    public sealed class NexusPublicAnswer
    {
        public bool Ok { get; private set; }
        public string Failure { get; private set; }
        public List<NexusPublicMod> Mods { get; private set; }

        public static NexusPublicAnswer Listed(List<NexusPublicMod> mods)
        {
            return new NexusPublicAnswer { Ok = true, Mods = mods ?? new List<NexusPublicMod>() };
        }

        public static NexusPublicAnswer Failed(string why)
        {
            return new NexusPublicAnswer { Ok = false, Failure = why ?? "", Mods = new List<NexusPublicMod>() };
        }

        /// <summary>The first listed mod with this id, or null when the answer does not list it.</summary>
        public NexusPublicMod Find(int modId)
        {
            foreach (var mod in Mods)
                if (mod.ModId == modId) return mod;
            return null;
        }
    }

    /// <summary>
    /// Builds the one request an update check sends to Nexus's public read endpoint, and reads its
    /// answer. The query is the one nexus_publish.py sends (W0-1 request 6), less the two fields W0-1
    /// section 7 says a per-check request must not select: description (10,034 characters for Mod
    /// Update Manager's page alone) and uid. A mod the answer does not list is left out silently by
    /// Nexus (HTTP 200, no errors; W0-1 request 5), so the listed mods are counted from nodes itself,
    /// never from nodesCount or totalCount. Any GraphQL errors entry, any HTTP or network failure and
    /// any body that is not the expected shape make the whole answer a failure.
    ///
    /// SimpleJson reads flat objects only, so the answer, an array of objects, goes through a small
    /// reader of its own below; strings are decoded by the shared JsonText.Unescape.
    ///
    /// The New and updated list on My Mods (plan feature 16) is the same endpoint's second request: the
    /// root mods query, newest update first, for the game's domain, asking for as many mods as
    /// DiscoveryFeedPolicy.ClampSize allows and for the seven fields W0-1 section 7 names. It is read by
    /// the same reader and the same failure rules below (ParseFeed, ReadFeedResponse); only the list the
    /// answer is read from differs.
    ///
    /// Host-free: no BepInEx or UnityEngine reference. Development_Tools/Tests/MUM-CompiledLogic.Tests.ps1
    /// compiles and drives it over the recorded answers in Development_Tools/Tests/Fixtures/MUM_NexusPublic/.
    /// </summary>
    public static class NexusPublicQuery
    {
        /// <summary>Nexus's public GraphQL endpoint, the default graphql_url in nexus_publish.py.</summary>
        public const string Endpoint = "https://api.nexusmods.com/v2/graphql";

        /// <summary>The game's Nexus domain, game_domain in Development_Tools/Nexus/nexus-mods.json.</summary>
        public const string GameDomain = "cardsurvivalfantasyforest";

        /// <summary>The most ids one query names, and the page size it asks for (W0-1: accepted with 11 ids).</summary>
        public const int MaxIdsPerQuery = 100;

        /// <summary>The fields asked for each mod.</summary>
        public const string Fields = "modId name version status updatedAt endorsements summary";

        public static readonly string Query =
            "query($ids:[CompositeDomainWithIdInput!]!){ legacyModsByDomain(ids:$ids, count: "
            + MaxIdsPerQuery.ToString(CultureInfo.InvariantCulture) + "){ nodes { " + Fields + " } } }";

        /// <summary>The fields the New and updated list asks for each mod, and nothing else: no description
        /// (10,034 characters for Mod Update Manager's page alone), no uid, no status.</summary>
        public const string FeedFields = "modId name version summary endorsements updatedAt adultContent";

        /// <summary>
        /// The New and updated query for the most recently updated mods of the game, the shape W0-1
        /// request 4 ran live (its recent list, there with count 5). The domain is GameDomain and the
        /// count is DiscoveryFeedPolicy.ClampSize of what was asked, a whole number written by this
        /// method, so nothing the player types reaches the query text.
        /// </summary>
        public static string FeedQuery(int size)
        {
            return "query{ mods(filter:{gameDomainName:[{value:\"" + GameDomain + "\"}]}, sort:[{updatedAt:{direction:DESC}}], count:"
                + DiscoveryFeedPolicy.ClampSize(size).ToString(CultureInfo.InvariantCulture) + "){ nodes{ " + FeedFields + " } } }";
        }

        /// <summary>The New and updated POST body: the query and nothing else.</summary>
        public static string BuildFeedBody(int size)
        {
            return "{\"query\":\"" + EscapeJson(FeedQuery(size)) + "\"}";
        }

        /// <summary>A Nexus id as the query can carry it: a positive whole number.</summary>
        public static bool TryReadId(string raw, out int id)
        {
            return int.TryParse((raw ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        /// <summary>The ids one query names: each readable id once, in the order given, at most
        /// MaxIdsPerQuery. An id left out here is simply not in the answer.</summary>
        public static List<int> SelectIds(IEnumerable<string> nexusIds)
        {
            var ids = new List<int>();
            if (nexusIds == null) return ids;
            foreach (var raw in nexusIds)
            {
                if (ids.Count >= MaxIdsPerQuery) break;
                if (!TryReadId(raw, out int id)) continue;
                if (!ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>The POST body: the query and one {gameDomain, modId} pair per id. Nothing else.</summary>
        public static string BuildBody(IList<int> ids)
        {
            var sb = new StringBuilder();
            sb.Append("{\"query\":\"").Append(EscapeJson(Query)).Append("\",\"variables\":{\"ids\":[");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"gameDomain\":\"").Append(GameDomain).Append("\",\"modId\":")
                  .Append(ids[i].ToString(CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append("]}}");
            return sb.ToString();
        }

        /// <summary>
        /// Reads what the request came back with. A network error, an HTTP error or a status outside
        /// 2xx is a failure whatever the body holds; otherwise the body is parsed.
        /// </summary>
        public static NexusPublicAnswer ReadResponse(bool networkError, bool httpError, long responseCode, string body)
        {
            return ReadResponse(networkError, httpError, responseCode, body, Parse);
        }

        /// <summary>The New and updated list's answer, read as ReadResponse reads a check's: the same
        /// network, HTTP and body rules, with the mods taken from data.mods.nodes.</summary>
        public static NexusPublicAnswer ReadFeedResponse(bool networkError, bool httpError, long responseCode, string body)
        {
            return ReadResponse(networkError, httpError, responseCode, body, ParseFeed);
        }

        private static NexusPublicAnswer ReadResponse(bool networkError, bool httpError, long responseCode, string body, Func<string, NexusPublicAnswer> parse)
        {
            if (networkError) return NexusPublicAnswer.Failed("a network error");
            if (httpError || responseCode < 200 || responseCode >= 300)
                return NexusPublicAnswer.Failed("HTTP " + responseCode.ToString(CultureInfo.InvariantCulture));
            return parse(body);
        }

        /// <summary>
        /// Parses a check's answer body (the mods under data.legacyModsByDomain). Never throws: anything it
        /// cannot read is a failure with the reason.
        /// </summary>
        public static NexusPublicAnswer Parse(string body)
        {
            return ParseList(body, "legacyModsByDomain");
        }

        /// <summary>Parses a New and updated answer body (the mods under data.mods). Never throws.</summary>
        public static NexusPublicAnswer ParseFeed(string body)
        {
            return ParseList(body, "mods");
        }

        private static NexusPublicAnswer ParseList(string body, string listName)
        {
            if (string.IsNullOrWhiteSpace(body)) return NexusPublicAnswer.Failed("an empty answer");

            object root;
            try
            {
                root = new Reader(body).ReadDocument();
            }
            catch (Exception ex)
            {
                MumLog.Debug($"NexusPublicQuery: a {body.Length}-character answer could not be read ({ex.GetType().Name}: {ex.Message}).");
                return NexusPublicAnswer.Failed("an answer that is not JSON");
            }

            var top = root as Dictionary<string, object>;
            if (top == null) return NexusPublicAnswer.Failed("an answer that is not a JSON object");

            if (top.TryGetValue("errors", out object errors) && HasEntries(errors))
                return NexusPublicAnswer.Failed("an answer carrying GraphQL errors");

            var nodes = Member(Member(Member(top, "data"), listName), "nodes") as List<object>;
            if (nodes == null) return NexusPublicAnswer.Failed("an answer with no " + listName + " nodes list");

            var mods = new List<NexusPublicMod>();
            foreach (var item in nodes)
            {
                var node = item as Dictionary<string, object>;
                if (node == null) continue;
                if (!TryInt(Member(node, "modId"), out int modId)) continue;
                mods.Add(new NexusPublicMod
                {
                    ModId = modId,
                    Name = Member(node, "name") as string,
                    Version = Member(node, "version") as string,
                    Status = Member(node, "status") as string,
                    UpdatedAt = Member(node, "updatedAt") as string,
                    Summary = Member(node, "summary") as string,
                    Endorsements = TryInt(Member(node, "endorsements"), out int endorsements) ? endorsements : 0,
                    AdultContent = Member(node, "adultContent") as bool?,
                });
            }
            return NexusPublicAnswer.Listed(mods);
        }

        private static bool HasEntries(object value)
        {
            if (value == null) return false;
            if (value is List<object> list) return list.Count > 0;
            if (value is Dictionary<string, object> map) return map.Count > 0;
            return true;
        }

        private static object Member(object value, string name)
        {
            return value is Dictionary<string, object> map && map.TryGetValue(name, out object member) ? member : null;
        }

        private static bool TryInt(object value, out int result)
        {
            result = 0;
            string text = value is Number n ? n.Text : value as string;
            return text != null && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>A JSON number kept as written; only whole numbers are ever read from one.</summary>
        private sealed class Number
        {
            public string Text;
        }

        /// <summary>
        /// A JSON reader for one document: objects become Dictionary&lt;string, object&gt;, arrays
        /// List&lt;object&gt;, strings string, numbers Number, true and false bool, null null. Anything
        /// else, trailing text, or nesting deeper than MaxDepth throws FormatException.
        /// </summary>
        private sealed class Reader
        {
            private const int MaxDepth = 64;
            private readonly string _s;
            private int _i;

            public Reader(string s) { _s = s; }

            public object ReadDocument()
            {
                var value = ReadValue(0);
                SkipSpace();
                if (_i != _s.Length) throw new FormatException("text after the JSON value at " + _i);
                return value;
            }

            private void SkipSpace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            private char Peek()
            {
                if (_i >= _s.Length) throw new FormatException("the answer ends early");
                return _s[_i];
            }

            private object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw new FormatException("nested deeper than " + MaxDepth);
                SkipSpace();
                char c = Peek();
                if (c == '{') return ReadObject(depth);
                if (c == '[') return ReadArray(depth);
                if (c == '"') return ReadString();
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                if (Match("true")) return true;
                if (Match("false")) return false;
                if (Match("null")) return null;
                throw new FormatException("unexpected '" + c + "' at " + _i);
            }

            private bool Match(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
                _i += word.Length;
                return true;
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++;
                SkipSpace();
                if (Peek() == '}') { _i++; return map; }
                while (true)
                {
                    SkipSpace();
                    if (Peek() != '"') throw new FormatException("expected a member name at " + _i);
                    var name = ReadString();
                    SkipSpace();
                    if (Peek() != ':') throw new FormatException("expected ':' at " + _i);
                    _i++;
                    var value = ReadValue(depth + 1);
                    if (!map.ContainsKey(name)) map[name] = value;
                    SkipSpace();
                    char c = Peek();
                    _i++;
                    if (c == '}') return map;
                    if (c != ',') throw new FormatException("expected ',' or '}' at " + (_i - 1));
                }
            }

            private List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                _i++;
                SkipSpace();
                if (Peek() == ']') { _i++; return list; }
                while (true)
                {
                    list.Add(ReadValue(depth + 1));
                    SkipSpace();
                    char c = Peek();
                    _i++;
                    if (c == ']') return list;
                    if (c != ',') throw new FormatException("expected ',' or ']' at " + (_i - 1));
                }
            }

            private string ReadString()
            {
                int start = ++_i;
                while (true)
                {
                    char c = Peek();
                    if (c == '"') break;
                    if (c == '\\') _i++;
                    _i++;
                }
                var raw = _s.Substring(start, _i - start);
                _i++;
                return JsonText.Unescape(raw);
            }

            private Number ReadNumber()
            {
                int start = _i;
                while (_i < _s.Length && "+-0123456789.eE".IndexOf(_s[_i]) >= 0) _i++;
                return new Number { Text = _s.Substring(start, _i - start) };
            }
        }
    }
}
