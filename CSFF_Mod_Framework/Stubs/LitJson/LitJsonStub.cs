// =============================================================
// LitJSON Stub — CSFFModFramework assimilation
//
// This is a MOCK of the LitJSON public API. It exists only so
// third-party CSFF mods that reference LitJSON v0.18.0.0 as an
// assembly dependency can load without FileNotFoundException.
//
// The implementation is intentionally minimal — just enough of
// the real LitJSON surface to cover the common usage patterns
// (JsonMapper.ToObject / ToJson, JsonData indexer access,
// JsonReader/JsonWriter token streaming).
//
// AssemblyVersion is pinned to 0.18.0.0 for CLR binding
// compatibility with mods built against the original DLL.
// =============================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

[assembly: System.Reflection.AssemblyTitle("LitJson")]
[assembly: System.Reflection.AssemblyProduct("LitJSON")]
[assembly: System.Reflection.AssemblyVersion("0.18.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.18.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.18.0-CSFFModFramework-stub")]

public delegate bool StateHandler();

namespace LitJson
{
    // ---------- Exceptions ----------
    public class JsonException : ApplicationException
    {
        public JsonException() { }
        public JsonException(string message) : base(message) { }
        public JsonException(string message, Exception inner) : base(message, inner) { }
        protected internal JsonException(ParserToken token) : base($"Invalid token '{token}' in input string") { }
        protected internal JsonException(ParserToken token, Exception inner)
            : base($"Invalid token '{token}' in input string", inner) { }
        protected internal JsonException(int c) : base($"Invalid character '{(char)c}' in input string") { }
        protected internal JsonException(int c, Exception inner)
            : base($"Invalid character '{(char)c}' in input string", inner) { }
    }

    // ---------- Enums ----------
    public enum JsonType { None, Object, Array, String, Int, Long, Double, Boolean }

    public enum JsonToken
    {
        None, ObjectStart, PropertyName, ObjectEnd, ArrayStart, ArrayEnd,
        Int, Long, Double, String, Boolean, Null
    }

    public enum ParserToken
    {
        None = 0, Number = 257, True = 258, False = 259, Null = 260,
        CharSeq = 261, Char = 262, Text = 263, Object = 264, ObjectPrime = 265,
        Pair = 266, PairRest = 267, Array = 268, ArrayPrime = 269, Value = 270,
        ValueRest = 271, String = 272, End = 273, Epsilon = 274
    }

    // ---------- Wrapper interface ----------
    public interface IJsonWrapper : IList, IDictionary
    {
        bool IsArray { get; }
        bool IsBoolean { get; }
        bool IsDouble { get; }
        bool IsInt { get; }
        bool IsLong { get; }
        bool IsObject { get; }
        bool IsString { get; }

        bool GetBoolean();
        double GetDouble();
        int GetInt();
        JsonType GetJsonType();
        long GetLong();
        string GetString();

        void SetBoolean(bool val);
        void SetDouble(double val);
        void SetInt(int val);
        void SetJsonType(JsonType type);
        void SetLong(long val);
        void SetString(string val);

        string ToJson();
        void ToJson(JsonWriter writer);
    }

    public class JsonMockWrapper : IJsonWrapper, IOrderedDictionary
    {
        public bool IsArray => false;
        public bool IsBoolean => false;
        public bool IsDouble => false;
        public bool IsInt => false;
        public bool IsLong => false;
        public bool IsObject => false;
        public bool IsString => false;

        public bool GetBoolean() => false;
        public double GetDouble() => 0d;
        public int GetInt() => 0;
        public JsonType GetJsonType() => JsonType.None;
        public long GetLong() => 0L;
        public string GetString() => string.Empty;

        public void SetBoolean(bool val) { }
        public void SetDouble(double val) { }
        public void SetInt(int val) { }
        public void SetJsonType(JsonType type) { }
        public void SetLong(long val) { }
        public void SetString(string val) { }

        public string ToJson() => string.Empty;
        public void ToJson(JsonWriter writer) { }

        // IList / IDictionary stub surface
        bool IList.IsFixedSize => true;
        bool IList.IsReadOnly => true;
        object IList.this[int index] { get => null; set { } }
        int IList.Add(object value) => 0;
        void IList.Clear() { }
        bool IList.Contains(object value) => false;
        int IList.IndexOf(object value) => -1;
        void IList.Insert(int index, object value) { }
        void IList.Remove(object value) { }
        void IList.RemoveAt(int index) { }

        bool IDictionary.IsFixedSize => true;
        bool IDictionary.IsReadOnly => true;
        ICollection IDictionary.Keys => Array.Empty<object>();
        ICollection IDictionary.Values => Array.Empty<object>();
        object IDictionary.this[object key] { get => null; set { } }
        object IOrderedDictionary.this[int index] { get => null; set { } }
        void IDictionary.Add(object key, object value) { }
        void IDictionary.Clear() { }
        bool IDictionary.Contains(object key) => false;
        IDictionaryEnumerator IDictionary.GetEnumerator() => ((IDictionary)new Hashtable()).GetEnumerator();
        IDictionaryEnumerator IOrderedDictionary.GetEnumerator() => ((IDictionary)new Hashtable()).GetEnumerator();
        void IDictionary.Remove(object key) { }
        void IOrderedDictionary.Insert(int index, object key, object value) { }
        void IOrderedDictionary.RemoveAt(int index) { }

        int ICollection.Count => 0;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        void ICollection.CopyTo(Array array, int index) { }
        IEnumerator IEnumerable.GetEnumerator() { yield break; }
    }

    // ---------- Delegates ----------
    internal delegate void ExporterFunc(object obj, JsonWriter writer);
    public delegate void ExporterFunc<T>(T obj, JsonWriter writer);
    internal delegate object ImporterFunc(object input);
    public delegate TValue ImporterFunc<TJson, TValue>(TJson input);
    public delegate IJsonWrapper WrapperFactory();

    // ---------- JsonData ----------
    public class JsonData : IJsonWrapper, IEquatable<JsonData>, IOrderedDictionary
    {
        private IList<JsonData> _array;
        private IDictionary<string, JsonData> _obj;
        private IList<KeyValuePair<string, JsonData>> _objList;
        private bool _bool;
        private double _double;
        private int _int;
        private long _long;
        private string _string;
        private JsonType _type = JsonType.None;

        public JsonData() { }
        public JsonData(bool v) { _type = JsonType.Boolean; _bool = v; }
        public JsonData(double v) { _type = JsonType.Double; _double = v; }
        public JsonData(int v) { _type = JsonType.Int; _int = v; }
        public JsonData(long v) { _type = JsonType.Long; _long = v; }
        public JsonData(string v) { _type = JsonType.String; _string = v; }
        public JsonData(object v)
        {
            switch (v)
            {
                case null: _type = JsonType.None; break;
                case bool b: _type = JsonType.Boolean; _bool = b; break;
                case double d: _type = JsonType.Double; _double = d; break;
                case float f: _type = JsonType.Double; _double = f; break;
                case int i: _type = JsonType.Int; _int = i; break;
                case long l: _type = JsonType.Long; _long = l; break;
                case string s: _type = JsonType.String; _string = s; break;
                default: throw new ArgumentException("Unsupported type for JsonData", nameof(v));
            }
        }

        public int Count => EnsureCollection().Count;
        public bool IsArray => _type == JsonType.Array;
        public bool IsBoolean => _type == JsonType.Boolean;
        public bool IsDouble => _type == JsonType.Double;
        public bool IsInt => _type == JsonType.Int;
        public bool IsLong => _type == JsonType.Long;
        public bool IsObject => _type == JsonType.Object;
        public bool IsString => _type == JsonType.String;

        bool IJsonWrapper.IsArray => IsArray;
        bool IJsonWrapper.IsBoolean => IsBoolean;
        bool IJsonWrapper.IsDouble => IsDouble;
        bool IJsonWrapper.IsInt => IsInt;
        bool IJsonWrapper.IsLong => IsLong;
        bool IJsonWrapper.IsObject => IsObject;
        bool IJsonWrapper.IsString => IsString;

        public ICollection<string> Keys { get { EnsureDictionary(); return _obj.Keys; } }

        public JsonData this[string prop]
        {
            get { EnsureDictionary(); return _obj.TryGetValue(prop, out var v) ? v : null; }
            set
            {
                EnsureDictionary();
                var kvp = new KeyValuePair<string, JsonData>(prop, value);
                if (_obj.ContainsKey(prop))
                {
                    for (int i = 0; i < _objList.Count; i++)
                        if (_objList[i].Key == prop) { _objList[i] = kvp; break; }
                }
                else _objList.Add(kvp);
                _obj[prop] = value;
            }
        }

        public JsonData this[int index]
        {
            get
            {
                EnsureCollection();
                if (_type == JsonType.Array) return _array[index];
                return _objList[index].Value;
            }
            set
            {
                EnsureCollection();
                if (_type == JsonType.Array) _array[index] = value;
                else
                {
                    var prev = _objList[index];
                    var kvp = new KeyValuePair<string, JsonData>(prev.Key, value);
                    _objList[index] = kvp;
                    _obj[prev.Key] = value;
                }
            }
        }

        public JsonType GetJsonType() => _type;
        public bool GetBoolean() { return _type == JsonType.Boolean ? _bool : throw new InvalidOperationException("Not a boolean"); }
        public double GetDouble() { return _type == JsonType.Double ? _double : throw new InvalidOperationException("Not a double"); }
        public int GetInt() { return _type == JsonType.Int ? _int : throw new InvalidOperationException("Not an int"); }
        public long GetLong() { return _type == JsonType.Long ? _long : throw new InvalidOperationException("Not a long"); }
        public string GetString() { return _type == JsonType.String ? _string : throw new InvalidOperationException("Not a string"); }

        public void SetBoolean(bool val) { _type = JsonType.Boolean; _bool = val; }
        public void SetDouble(double val) { _type = JsonType.Double; _double = val; }
        public void SetInt(int val) { _type = JsonType.Int; _int = val; }
        public void SetLong(long val) { _type = JsonType.Long; _long = val; }
        public void SetString(string val) { _type = JsonType.String; _string = val; }
        public void SetJsonType(JsonType type)
        {
            if (_type == type) return;
            _type = type;
            switch (type)
            {
                case JsonType.Array: _array = new List<JsonData>(); break;
                case JsonType.Object:
                    _obj = new Dictionary<string, JsonData>();
                    _objList = new List<KeyValuePair<string, JsonData>>();
                    break;
            }
        }

        public int Add(object value)
        {
            var jd = value as JsonData ?? new JsonData(value);
            EnsureArray();
            _array.Add(jd);
            return _array.Count - 1;
        }

        public bool Remove(object value)
        {
            EnsureArray();
            return _array.Remove(ToJsonData(value));
        }

        public void Clear()
        {
            if (_type == JsonType.Array) _array.Clear();
            else if (_type == JsonType.Object) { _obj.Clear(); _objList.Clear(); }
        }

        public bool ContainsKey(string key) { EnsureDictionary(); return _obj.ContainsKey(key); }
        public bool Has(string key) => ContainsKey(key);

        public string ToJson()
        {
            var sb = new StringBuilder();
            var w = new JsonWriter(sb);
            WriteJson(this, w);
            return sb.ToString();
        }

        public void ToJson(JsonWriter writer) => WriteJson(this, writer);

        public override string ToString()
        {
            switch (_type)
            {
                case JsonType.Array: return "JsonData array";
                case JsonType.Boolean: return _bool.ToString();
                case JsonType.Double: return _double.ToString(CultureInfo.InvariantCulture);
                case JsonType.Int: return _int.ToString(CultureInfo.InvariantCulture);
                case JsonType.Long: return _long.ToString(CultureInfo.InvariantCulture);
                case JsonType.Object: return "JsonData object";
                case JsonType.String: return _string;
                default: return string.Empty;
            }
        }

        public bool Equals(JsonData other)
        {
            if (other == null || _type != other._type) return false;
            switch (_type)
            {
                case JsonType.None: return true;
                case JsonType.Boolean: return _bool == other._bool;
                case JsonType.Double: return _double == other._double;
                case JsonType.Int: return _int == other._int;
                case JsonType.Long: return _long == other._long;
                case JsonType.String: return _string == other._string;
                default: return ReferenceEquals(this, other);
            }
        }

        // ----- Implicit conversions -----
        public static implicit operator JsonData(bool v) => new JsonData(v);
        public static implicit operator JsonData(double v) => new JsonData(v);
        public static implicit operator JsonData(int v) => new JsonData(v);
        public static implicit operator JsonData(long v) => new JsonData(v);
        public static implicit operator JsonData(string v) => new JsonData(v);
        public static explicit operator bool(JsonData d) => d.GetBoolean();
        public static explicit operator double(JsonData d) => d.GetDouble();
        public static explicit operator int(JsonData d) => d.GetInt();
        public static explicit operator long(JsonData d) => d.GetLong();
        public static explicit operator string(JsonData d) => d._type == JsonType.String ? d._string : null;

        // ----- ICollection ----- (IList/IDictionary inherit)
        int ICollection.Count => Count;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;
        void ICollection.CopyTo(Array array, int index) { foreach (var v in (IEnumerable)this) array.SetValue(v, index++); }
        IEnumerator IEnumerable.GetEnumerator()
        {
            if (_type == JsonType.Array) return _array.GetEnumerator();
            if (_type == JsonType.Object) return _objList.GetEnumerator();
            return Array.Empty<object>().GetEnumerator();
        }

        // ----- IList -----
        bool IList.IsFixedSize => false;
        bool IList.IsReadOnly => false;
        object IList.this[int index] { get => this[index]; set => this[index] = ToJsonData(value); }
        int IList.Add(object value) { return Add(value); }
        void IList.Clear() => Clear();
        bool IList.Contains(object value) { EnsureArray(); return _array.Contains(ToJsonData(value)); }
        int IList.IndexOf(object value) { EnsureArray(); return _array.IndexOf(ToJsonData(value)); }
        void IList.Insert(int index, object value) { EnsureArray(); _array.Insert(index, ToJsonData(value)); }
        void IList.Remove(object value) { Remove(value); }
        void IList.RemoveAt(int index) { EnsureArray(); _array.RemoveAt(index); }

        // ----- IDictionary -----
        bool IDictionary.IsFixedSize => false;
        bool IDictionary.IsReadOnly => false;
        ICollection IDictionary.Keys { get { EnsureDictionary(); return (ICollection)_obj.Keys; } }
        ICollection IDictionary.Values { get { EnsureDictionary(); return (ICollection)_obj.Values; } }
        object IDictionary.this[object key] { get => this[(string)key]; set => this[(string)key] = ToJsonData(value); }
        object IOrderedDictionary.this[int index] { get => this[index]; set => this[index] = ToJsonData(value); }
        void IDictionary.Add(object key, object value) => this[(string)key] = ToJsonData(value);
        void IDictionary.Clear() => Clear();
        bool IDictionary.Contains(object key) { EnsureDictionary(); return _obj.ContainsKey((string)key); }
        void IDictionary.Remove(object key)
        {
            EnsureDictionary();
            _obj.Remove((string)key);
            for (int i = 0; i < _objList.Count; i++)
                if (_objList[i].Key.Equals(key)) { _objList.RemoveAt(i); break; }
        }
        void IOrderedDictionary.Insert(int index, object key, object value)
        {
            EnsureDictionary();
            var stringKey = (string)key;
            var jsonValue = ToJsonData(value);
            _obj[stringKey] = jsonValue;
            _objList.Insert(index, new KeyValuePair<string, JsonData>(stringKey, jsonValue));
        }
        void IOrderedDictionary.RemoveAt(int index)
        {
            EnsureDictionary();
            var key = _objList[index].Key;
            _objList.RemoveAt(index);
            _obj.Remove(key);
        }
        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            EnsureDictionary();
            var ht = new OrderedDictionary();
            foreach (var kvp in _objList) ht.Add(kvp.Key, kvp.Value);
            return ht.GetEnumerator();
        }
        IDictionaryEnumerator IOrderedDictionary.GetEnumerator() => ((IDictionary)this).GetEnumerator();

        // ----- Internal helpers -----
        private ICollection EnsureCollection()
        {
            if (_type == JsonType.Array) return (ICollection)_array;
            if (_type == JsonType.Object) return (ICollection)_objList;
            throw new InvalidOperationException("JsonData is not a collection");
        }
        private void EnsureArray()
        {
            if (_type == JsonType.None) { _type = JsonType.Array; _array = new List<JsonData>(); }
            else if (_type != JsonType.Array) throw new InvalidOperationException("JsonData is not an array");
        }
        private void EnsureDictionary()
        {
            if (_type == JsonType.None)
            {
                _type = JsonType.Object;
                _obj = new Dictionary<string, JsonData>();
                _objList = new List<KeyValuePair<string, JsonData>>();
            }
            else if (_type != JsonType.Object) throw new InvalidOperationException("JsonData is not an object");
        }
        private static JsonData ToJsonData(object v) => v as JsonData ?? new JsonData(v);

        private static void WriteJson(JsonData d, JsonWriter w)
        {
            if (d == null) { w.Write(null); return; }
            switch (d._type)
            {
                case JsonType.None: w.Write(null); break;
                case JsonType.Boolean: w.Write(d._bool); break;
                case JsonType.Double: w.Write(d._double); break;
                case JsonType.Int: w.Write(d._int); break;
                case JsonType.Long: w.Write(d._long); break;
                case JsonType.String: w.Write(d._string); break;
                case JsonType.Array:
                    w.WriteArrayStart();
                    foreach (var e in d._array) WriteJson(e, w);
                    w.WriteArrayEnd();
                    break;
                case JsonType.Object:
                    w.WriteObjectStart();
                    foreach (var kvp in d._objList) { w.WritePropertyName(kvp.Key); WriteJson(kvp.Value, w); }
                    w.WriteObjectEnd();
                    break;
            }
        }
    }

    // ---------- JsonReader ----------
    public class JsonReader
    {
        private readonly TextReader _reader;
        public JsonToken Token { get; private set; } = JsonToken.None;
        public object Value { get; private set; }
        public bool AllowComments { get; set; } = true;
        public bool AllowSingleQuotedStrings { get; set; } = true;
        public bool SkipNonMembers { get; set; } = true;
        public bool EndOfInput { get; private set; }
        public bool EndOfJson { get; private set; }

        public JsonReader(string json) : this(new StringReader(json)) { }
        public JsonReader(TextReader reader) { _reader = reader ?? throw new ArgumentNullException(nameof(reader)); }

        public void Close() => _reader.Dispose();

        public bool Read()
        {
            if (EndOfInput) return false;
            JsonData root = JsonMapper.ToObject(_reader); // single-shot parse
            EndOfJson = true; EndOfInput = true;
            // Surface the type of the parsed root as a token hint
            Value = root;
            Token = root == null ? JsonToken.Null :
                    root.IsObject ? JsonToken.ObjectStart :
                    root.IsArray ? JsonToken.ArrayStart :
                    root.IsString ? JsonToken.String :
                    root.IsBoolean ? JsonToken.Boolean :
                    root.IsInt ? JsonToken.Int :
                    root.IsLong ? JsonToken.Long :
                    root.IsDouble ? JsonToken.Double : JsonToken.None;
            return false;
        }
    }

    // ---------- JsonWriter ----------
    public class JsonWriter
    {
        private readonly StringBuilder _sb;
        private readonly TextWriter _writer;
        private readonly Stack<Ctx> _stack = new Stack<Ctx>();
        private bool _needComma;
        private int _indent;

        public bool PrettyPrint { get; set; }
        public int IndentValue { get; set; } = 4;
        public TextWriter TextWriter => _writer;
        public bool Validate { get; set; } = true;
        public bool LowerCaseProperties { get; set; }

        private enum Ctx { Object, Array, Property }

        public JsonWriter() { _sb = new StringBuilder(); _writer = new StringWriter(_sb); }
        public JsonWriter(StringBuilder sb) { _sb = sb; _writer = new StringWriter(sb); }
        public JsonWriter(TextWriter tw) { _writer = tw ?? throw new ArgumentNullException(nameof(tw)); }

        public override string ToString() => _sb != null ? _sb.ToString() : _writer.ToString();

        public void Reset() { _stack.Clear(); _needComma = false; _indent = 0; _sb?.Clear(); }

        private void WriteSep()
        {
            if (_needComma) _writer.Write(',');
            if (PrettyPrint) { _writer.Write('\n'); for (int i = 0; i < _indent; i++) _writer.Write(' '); }
            _needComma = false;
        }

        public void Write(bool v) { WriteSep(); _writer.Write(v ? "true" : "false"); _needComma = true; }
        public void Write(decimal v) { WriteSep(); _writer.Write(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; }
        public void Write(float v) { Write((double)v); }
        public void Write(double v)
        {
            WriteSep();
            string s = v.ToString("R", CultureInfo.InvariantCulture);
            if (!s.Contains(".") && !s.Contains("E") && !s.Contains("e")) s += ".0";
            _writer.Write(s); _needComma = true;
        }
        public void Write(int v) { WriteSep(); _writer.Write(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; }
        public void Write(long v) { WriteSep(); _writer.Write(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; }
        public void Write(ulong v) { WriteSep(); _writer.Write(v.ToString(CultureInfo.InvariantCulture)); _needComma = true; }
        public void Write(string v)
        {
            WriteSep();
            if (v == null) _writer.Write("null");
            else WriteEscaped(v);
            _needComma = true;
        }

        public void WriteArrayEnd() { _indent -= IndentValue; if (PrettyPrint) { _writer.Write('\n'); for (int i = 0; i < _indent; i++) _writer.Write(' '); } _writer.Write(']'); _stack.Pop(); _needComma = true; }
        public void WriteArrayStart() { WriteSep(); _writer.Write('['); _stack.Push(Ctx.Array); _indent += IndentValue; _needComma = false; }
        public void WriteObjectEnd() { _indent -= IndentValue; if (PrettyPrint) { _writer.Write('\n'); for (int i = 0; i < _indent; i++) _writer.Write(' '); } _writer.Write('}'); _stack.Pop(); _needComma = true; }
        public void WriteObjectStart() { WriteSep(); _writer.Write('{'); _stack.Push(Ctx.Object); _indent += IndentValue; _needComma = false; }
        public void WritePropertyName(string name)
        {
            WriteSep();
            WriteEscaped(LowerCaseProperties ? name.ToLowerInvariant() : name);
            _writer.Write(':');
            _needComma = false;
        }

        private void WriteEscaped(string s)
        {
            _writer.Write('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': _writer.Write("\\\""); break;
                    case '\\': _writer.Write("\\\\"); break;
                    case '\b': _writer.Write("\\b"); break;
                    case '\f': _writer.Write("\\f"); break;
                    case '\n': _writer.Write("\\n"); break;
                    case '\r': _writer.Write("\\r"); break;
                    case '\t': _writer.Write("\\t"); break;
                    default:
                        if (c < 0x20) _writer.Write("\\u" + ((int)c).ToString("x4"));
                        else _writer.Write(c);
                        break;
                }
            }
            _writer.Write('"');
        }
    }

    // ---------- JsonMapper ----------
    public class JsonMapper
    {
        public JsonMapper() { }

        public static string ToJson(object obj)
        {
            var sb = new StringBuilder();
            var w = new JsonWriter(sb);
            WriteValue(obj, w);
            return sb.ToString();
        }

        public static void ToJson(object obj, JsonWriter writer) => WriteValue(obj, writer);

        public static JsonData ToObject(string json) => Parse(new StringReader(json ?? string.Empty));
        public static object ToObject(string json, Type type) => FromJsonData(ToObject(json), type);
        public static JsonData ToObject(TextReader reader) => Parse(reader);
        public static JsonData ToObject(JsonReader reader) { reader.Read(); return reader.Value as JsonData; }

        public static T ToObject<T>(string json) => (T)FromJsonData(ToObject(json), typeof(T));
        public static T ToObject<T>(TextReader reader) => (T)FromJsonData(ToObject(reader), typeof(T));
        public static T ToObject<T>(JsonReader reader) => (T)FromJsonData(ToObject(reader), typeof(T));

        public static IJsonWrapper ToWrapper(WrapperFactory factory, string json) => factory?.Invoke();
        public static IJsonWrapper ToWrapper(WrapperFactory factory, JsonReader reader) => factory?.Invoke();

        // ---- Exporter / Importer registrations (no-op; surface compatibility only) ----
        public static void RegisterExporter<T>(ExporterFunc<T> exporter) { }
        public static void RegisterImporter<TJson, TValue>(ImporterFunc<TJson, TValue> importer) { }
        public static void UnregisterExporters() { }
        public static void UnregisterImporters() { }

        // ============ Serialization ============
        private static void WriteValue(object obj, JsonWriter w)
        {
            if (obj == null) { w.Write((string)null); return; }
            switch (obj)
            {
                case JsonData jd: jd.ToJson(w); return;
                case IJsonWrapper iw: iw.ToJson(w); return;
                case string s: w.Write(s); return;
                case bool b: w.Write(b); return;
                case int i: w.Write(i); return;
                case long l: w.Write(l); return;
                case ulong ul: w.Write(ul); return;
                case double d: w.Write(d); return;
                case float f: w.Write((double)f); return;
                case decimal dec: w.Write(dec); return;
                case byte by: w.Write((int)by); return;
                case sbyte sb: w.Write((int)sb); return;
                case short sh: w.Write((int)sh); return;
                case ushort ush: w.Write((int)ush); return;
                case uint ui: w.Write((long)ui); return;
                case char c: w.Write(c.ToString()); return;
                case Enum e: w.Write(Convert.ToInt64(e)); return;
            }
            if (obj is IDictionary dict)
            {
                w.WriteObjectStart();
                foreach (DictionaryEntry de in dict) { w.WritePropertyName(de.Key.ToString()); WriteValue(de.Value, w); }
                w.WriteObjectEnd(); return;
            }
            if (obj is IList list)
            {
                w.WriteArrayStart();
                foreach (var e in list) WriteValue(e, w);
                w.WriteArrayEnd(); return;
            }
            // Plain object -> reflect public props + fields
            var t = obj.GetType();
            w.WriteObjectStart();
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                if (p.CanRead && p.GetIndexParameters().Length == 0)
                    try { w.WritePropertyName(p.Name); WriteValue(p.GetValue(obj, null), w); } catch { }
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
            { w.WritePropertyName(f.Name); WriteValue(f.GetValue(obj), w); }
            w.WriteObjectEnd();
        }

        // ============ Parsing ============
        // Convention: `c` is the current lookahead character (last char read from reader).
        // SkipWs advances past whitespace starting at current c.
        private static JsonData Parse(TextReader r)
        {
            int c = r.Read();
            c = SkipWs(r, c);
            if (c < 0) return null;
            return ParseValue(r, ref c);
        }

        private static JsonData ParseValue(TextReader r, ref int c)
        {
            switch (c)
            {
                case '{': return ParseObject(r, ref c);
                case '[': return ParseArray(r, ref c);
                case '"': case '\'': return new JsonData(ParseString(r, ref c));
                case 't': case 'f': return new JsonData(ParseBool(r, ref c));
                case 'n': ParseNull(r, ref c); return null;
                default: return ParseNumber(r, ref c);
            }
        }

        private static JsonData ParseObject(TextReader r, ref int c)
        {
            var d = new JsonData(); d.SetJsonType(JsonType.Object);
            c = r.Read(); c = SkipWs(r, c); // past '{'
            if (c == '}') { c = r.Read(); return d; }
            while (c >= 0)
            {
                if (c != '"' && c != '\'') throw new JsonException(c);
                string key = ParseString(r, ref c);
                c = SkipWs(r, c);
                if (c != ':') throw new JsonException(c);
                c = r.Read(); c = SkipWs(r, c);
                d[key] = ParseValue(r, ref c);
                c = SkipWs(r, c);
                if (c == ',') { c = r.Read(); c = SkipWs(r, c); continue; }
                if (c == '}') { c = r.Read(); return d; }
                throw new JsonException(c);
            }
            throw new JsonException("Unexpected end of JSON object");
        }

        private static JsonData ParseArray(TextReader r, ref int c)
        {
            var d = new JsonData(); d.SetJsonType(JsonType.Array);
            c = r.Read(); c = SkipWs(r, c); // past '['
            if (c == ']') { c = r.Read(); return d; }
            while (c >= 0)
            {
                d.Add(ParseValue(r, ref c));
                c = SkipWs(r, c);
                if (c == ',') { c = r.Read(); c = SkipWs(r, c); continue; }
                if (c == ']') { c = r.Read(); return d; }
                throw new JsonException(c);
            }
            throw new JsonException("Unexpected end of JSON array");
        }

        private static string ParseString(TextReader r, ref int c)
        {
            // c is the opening quote; consume it
            int quote = c;
            var sb = new StringBuilder();
            while (true)
            {
                int ch = r.Read();
                if (ch < 0) throw new JsonException("Unterminated string");
                if (ch == quote) { c = r.Read(); return sb.ToString(); }
                if (ch == '\\')
                {
                    int esc = r.Read();
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\'': sb.Append('\''); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            var hex = new char[4];
                            for (int i = 0; i < 4; i++) { int h = r.Read(); if (h < 0) throw new JsonException("Bad \\u escape"); hex[i] = (char)h; }
                            sb.Append((char)int.Parse(new string(hex), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            break;
                        default: throw new JsonException(esc);
                    }
                }
                else sb.Append((char)ch);
            }
        }

        private static bool ParseBool(TextReader r, ref int c)
        {
            if (c == 't') { Expect(r, "rue"); c = r.Read(); return true; }
            if (c == 'f') { Expect(r, "alse"); c = r.Read(); return false; }
            throw new JsonException(c);
        }
        private static void ParseNull(TextReader r, ref int c) { Expect(r, "ull"); c = r.Read(); }
        private static void Expect(TextReader r, string tail) { foreach (var ch in tail) if (r.Read() != ch) throw new JsonException("Bad literal"); }

        private static JsonData ParseNumber(TextReader r, ref int c)
        {
            var sb = new StringBuilder();
            bool isFloat = false;
            while (c >= 0 && (c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E' || (c >= '0' && c <= '9')))
            {
                if (c == '.' || c == 'e' || c == 'E') isFloat = true;
                sb.Append((char)c);
                c = r.Read();
            }
            var s = sb.ToString();
            if (s.Length == 0) throw new JsonException(c);
            if (isFloat) return new JsonData(double.Parse(s, CultureInfo.InvariantCulture));
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
            {
                if (l >= int.MinValue && l <= int.MaxValue) return new JsonData((int)l);
                return new JsonData(l);
            }
            return new JsonData(double.Parse(s, CultureInfo.InvariantCulture));
        }

        // Skip whitespace and comments. `c` is the current lookahead; returns the first
        // non-whitespace/non-comment char (or -1 on EOF).
        private static int SkipWs(TextReader r, int c)
        {
            while (c >= 0)
            {
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { c = r.Read(); continue; }
                if (c == '/')
                {
                    int n = r.Peek();
                    if (n == '/') { r.Read(); while ((c = r.Read()) >= 0 && c != '\n') { } continue; }
                    if (n == '*') { r.Read(); int prev = 0; int ch; while ((ch = r.Read()) >= 0) { if (prev == '*' && ch == '/') break; prev = ch; } c = r.Read(); continue; }
                }
                return c;
            }
            return -1;
        }

        // ============ JsonData -> T (reflection mapper) ============
        private static object FromJsonData(JsonData d, Type t)
        {
            if (d == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
            // Primitives / strings / enums
            if (t == typeof(JsonData)) return d;
            if (t == typeof(object)) return Unwrap(d);
            if (t == typeof(string)) return d.IsString ? d.GetString() : d.ToString();
            if (t.IsEnum)
            {
                if (d.IsString) return Enum.Parse(t, d.GetString(), true);
                if (d.IsInt) return Enum.ToObject(t, d.GetInt());
                if (d.IsLong) return Enum.ToObject(t, d.GetLong());
            }
            if (t == typeof(bool)) return d.IsBoolean ? d.GetBoolean() : Convert.ToBoolean(Unwrap(d));
            if (t == typeof(int)) return d.IsInt ? d.GetInt() : Convert.ToInt32(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(long)) return d.IsLong ? d.GetLong() : Convert.ToInt64(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(double)) return d.IsDouble ? d.GetDouble() : Convert.ToDouble(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(float)) return d.IsDouble ? (float)d.GetDouble() : Convert.ToSingle(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(decimal)) return Convert.ToDecimal(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(byte)) return Convert.ToByte(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(sbyte)) return Convert.ToSByte(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(short)) return Convert.ToInt16(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(ushort)) return Convert.ToUInt16(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(uint)) return Convert.ToUInt32(Unwrap(d), CultureInfo.InvariantCulture);
            if (t == typeof(ulong)) return Convert.ToUInt64(Unwrap(d), CultureInfo.InvariantCulture);

            // Arrays
            if (t.IsArray)
            {
                var elem = t.GetElementType();
                var arr = Array.CreateInstance(elem, d.Count);
                for (int i = 0; i < d.Count; i++) arr.SetValue(FromJsonData(d[i], elem), i);
                return arr;
            }
            // Generic List<T>
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elem = t.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(t);
                for (int i = 0; i < d.Count; i++) list.Add(FromJsonData(d[i], elem));
                return list;
            }
            // Generic Dictionary<string,T>
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = t.GetGenericArguments();
                if (args[0] != typeof(string)) throw new JsonException("Only Dictionary<string,T> supported");
                var dict = (IDictionary)Activator.CreateInstance(t);
                foreach (var key in d.Keys) dict.Add(key, FromJsonData(d[key], args[1]));
                return dict;
            }
            // Plain object
            var inst = Activator.CreateInstance(t);
            if (d.IsObject)
            {
                foreach (var key in d.Keys)
                {
                    var prop = t.GetProperty(key, BindingFlags.Instance | BindingFlags.Public);
                    if (prop != null && prop.CanWrite) { try { prop.SetValue(inst, FromJsonData(d[key], prop.PropertyType), null); } catch { } continue; }
                    var fld = t.GetField(key, BindingFlags.Instance | BindingFlags.Public);
                    if (fld != null) { try { fld.SetValue(inst, FromJsonData(d[key], fld.FieldType)); } catch { } }
                }
            }
            return inst;
        }

        private static object Unwrap(JsonData d)
        {
            if (d == null) return null;
            switch (d.GetJsonType())
            {
                case JsonType.Boolean: return d.GetBoolean();
                case JsonType.Double: return d.GetDouble();
                case JsonType.Int: return d.GetInt();
                case JsonType.Long: return d.GetLong();
                case JsonType.String: return d.GetString();
                default: return d;
            }
        }
    }
}
