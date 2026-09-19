using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace mod_update_manager
{
    /// <summary>
    /// One [BepInDependency] declaration read off a plugin assembly.
    /// </summary>
    public class DeclaredDependency
    {
        public string Guid { get; set; }

        /// <summary>
        /// False only when the declaration carries DependencyFlags.SoftDependency. BepInEx refuses
        /// to load a plugin whose HARD dependency is missing; a missing soft one is informational.
        /// </summary>
        public bool IsHard { get; set; }

        /// <summary>
        /// Set only for the BepInDependency(string, string) overload (minimum-version form),
        /// which BepInEx treats as a HARD dependency. Null otherwise.
        /// </summary>
        public string MinimumVersion { get; set; }
    }

    /// <summary>
    /// What a single plugin assembly declares about itself and its dependencies.
    /// </summary>
    public class PluginMetadata
    {
        public string DllPath { get; set; }
        public List<string> PluginGuids { get; } = new List<string>();
        public List<DeclaredDependency> Dependencies { get; } = new List<DeclaredDependency>();
    }

    /// <summary>
    /// Reads [BepInPlugin] and [BepInDependency] declarations out of a .NET assembly's METADATA
    /// TABLES, without loading, executing, or locking the assembly.
    ///
    /// WHY NOT Assembly.LoadFrom: this runs against arbitrary third-party plugin DLLs from a
    /// utility mod's UI. Loading them would execute module initializers, pin the files for the
    /// process lifetime, and can hard-fail under the game's Mono runtime (the same class of
    /// constraint that keeps System.IO.Compression out of this mod - see
    /// Development_Tools/Pack-Suite.ps1). System.Reflection.Metadata is not available on net48
    /// under this runtime either, and shipping it is out of scope, so the tables are walked by
    /// hand here. Confirmed 2026-09-08 against all 25 DLLs in the live BepInEx/plugins tree:
    /// 25/25 parsed, 0 assemblies loaded.
    ///
    /// ENCODING NOTE (this is the part that is easy to get wrong): a [BepInDependency("guid")]
    /// argument is NOT an IL ldstr literal, so it is NOT in the #US (UTF-16-LE) heap. Custom
    /// attribute fixed arguments are serialized into the CustomAttribute table's Value blob in
    /// the #Blob heap as SerStrings - a compressed length prefix followed by UTF-8 bytes.
    /// Verified by byte scan: "crispywhips.CSFFModFramework" occurs exactly once as UTF-8 and
    /// zero times as UTF-16-LE in Herbs_And_Fungi.dll.
    ///
    /// Reads only the CLI metadata region, never the whole file: the plugins tree measured
    /// 55.3 MB of DLL bytes but only 2.4 MB of metadata (this mod's own DLL is 41 MB, almost
    /// all of it embedded suite ZIPs).
    ///
    /// Every failure mode is soft: a malformed, native, or unreadable DLL logs at Debug and is
    /// skipped. This must never throw into the UI.
    /// </summary>
    public static class PluginMetadataReader
    {
        // Metadata table ids used below (ECMA-335 II.22).
        private const int TBL_MODULE = 0x00;
        private const int TBL_TYPEREF = 0x01;
        private const int TBL_TYPEDEF = 0x02;
        private const int TBL_FIELDPTR = 0x03;
        private const int TBL_FIELD = 0x04;
        private const int TBL_METHODPTR = 0x05;
        private const int TBL_METHODDEF = 0x06;
        private const int TBL_PARAMPTR = 0x07;
        private const int TBL_PARAM = 0x08;
        private const int TBL_INTERFACEIMPL = 0x09;
        private const int TBL_MEMBERREF = 0x0A;
        private const int TBL_CONSTANT = 0x0B;
        private const int TBL_CUSTOMATTRIBUTE = 0x0C;

        // Coded-index table lists (ECMA-335 II.24.2.6).
        private static readonly int[] HasCustomAttribute =
        {
            0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x00, 0x0E, 0x17, 0x14,
            0x11, 0x1A, 0x1B, 0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2C, 0x2B
        };
        private static readonly int[] CustomAttributeType = { -1, -1, 0x06, 0x0A, -1 };
        private static readonly int[] MemberRefParent = { 0x02, 0x01, 0x1A, 0x06, 0x1B };
        private static readonly int[] ResolutionScope = { 0x00, 0x1A, 0x23, 0x01 };
        private static readonly int[] TypeDefOrRef = { 0x02, 0x01, 0x1B };
        private static readonly int[] HasConstant = { 0x04, 0x08, 0x17 };

        // Sanity ceiling on the metadata region we are willing to buffer.
        private const int MaxMetadataBytes = 48 * 1024 * 1024;

        private const string AttrPlugin = "BepInPlugin";
        private const string AttrDependency = "BepInDependency";

        /// <summary>
        /// Returns what the assembly at <paramref name="dllPath"/> declares, or null if it is not a
        /// managed assembly or could not be parsed. Never throws.
        /// </summary>
        public static PluginMetadata Read(string dllPath)
        {
            try
            {
                return new Parser(dllPath).Parse();
            }
            catch (Exception ex)
            {
                // Soft-fail per DLL: a native DLL, an obfuscated/packed assembly, or a file another
                // process is rewriting must skip quietly rather than break the Conflicts tab.
                // The logger itself is null-guarded, so the soft-fail path can never become the
                // thing that throws.
                try
                {
                    Plugin.Logger?.LogDebug($"PluginMetadataReader: skipped '{dllPath}' ({ex.GetType().Name}: {ex.Message})");
                }
                catch { }
                return null;
            }
        }

        private sealed class Parser
        {
            private readonly string _path;
            private byte[] _md;                 // the CLI metadata region, offset 0 == metadata root
            private int _stringsBase, _blobBase;
            private int _stringsSize, _blobSize;
            private int _strIdx, _guidIdx, _blobIdx;   // heap index widths
            private readonly Dictionary<int, int> _rows = new Dictionary<int, int>();
            private readonly Dictionary<int, int[]> _layout = new Dictionary<int, int[]>();
            private readonly Dictionary<int, int> _tableOffset = new Dictionary<int, int>();
            private int _caTagBits, _catTagBits, _mrpTagBits;

            public Parser(string path) { _path = path; }

            public PluginMetadata Parse()
            {
                ReadMetadataRegion();
                ReadStreams();
                ReadTableHeader();
                BuildLayout();
                return ScanCustomAttributes();
            }

            // ---------- PE / CLI ----------

            private void ReadMetadataRegion()
            {
                // FileShare.ReadWrite: never block, and never be blocked by, whoever else has the
                // file open. FileAccess.Read: we never write and never take a write lock.
                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var head = ReadAt(fs, 0, (int)Math.Min(fs.Length, 4096));
                    if (head.Length < 0x40 || head[0] != 'M' || head[1] != 'Z')
                        throw new InvalidDataException("not a PE image");

                    int peOff = (int)U32(head, 0x3C);
                    if (peOff <= 0 || peOff + 24 > head.Length)
                        throw new InvalidDataException("bad PE offset");
                    if (head[peOff] != 'P' || head[peOff + 1] != 'E' || head[peOff + 2] != 0 || head[peOff + 3] != 0)
                        throw new InvalidDataException("no PE signature");

                    int coff = peOff + 4;
                    int numSections = U16(head, coff + 2);
                    int optSize = U16(head, coff + 16);
                    int opt = coff + 20;
                    if (numSections <= 0 || numSections > 96)
                        throw new InvalidDataException("implausible section count");

                    int magic = U16(head, opt);
                    int dataDirs;
                    if (magic == 0x10B) dataDirs = opt + 96;
                    else if (magic == 0x20B) dataDirs = opt + 112;
                    else throw new InvalidDataException("unknown optional header magic");

                    int sectionTable = opt + optSize;
                    int need = sectionTable + numSections * 40;
                    if (need > head.Length)
                        head = ReadAt(fs, 0, (int)Math.Min(fs.Length, need));
                    if (dataDirs + 15 * 8 > head.Length || need > head.Length)
                        throw new InvalidDataException("headers truncated");

                    var sections = new int[numSections][];
                    for (int i = 0; i < numSections; i++)
                    {
                        int b = sectionTable + i * 40;
                        sections[i] = new[]
                        {
                            (int)U32(head, b + 12),  // virtual address
                            (int)U32(head, b + 8),   // virtual size
                            (int)U32(head, b + 20),  // raw pointer
                            (int)U32(head, b + 16)   // raw size
                        };
                    }

                    // Data directory 14 == CLI header. Absent on a native DLL.
                    int cliRva = (int)U32(head, dataDirs + 14 * 8);
                    if (cliRva == 0)
                        throw new InvalidDataException("unmanaged assembly (no CLI header)");

                    var cli = ReadAt(fs, RvaToOffset(sections, cliRva), 72);
                    int mdRva = (int)U32(cli, 8);
                    long mdSize = U32(cli, 12);
                    if (mdRva == 0 || mdSize < 24 || mdSize > MaxMetadataBytes)
                        throw new InvalidDataException($"implausible metadata size {mdSize}");

                    _md = ReadAt(fs, RvaToOffset(sections, mdRva), (int)mdSize);
                }
            }

            private static byte[] ReadAt(FileStream fs, long offset, int count)
            {
                if (offset < 0 || count < 0 || offset + count > fs.Length)
                    throw new InvalidDataException("read past end of file");
                fs.Position = offset;
                var buf = new byte[count];
                int got = 0;
                while (got < count)
                {
                    int n = fs.Read(buf, got, count - got);
                    if (n <= 0) throw new EndOfStreamException();
                    got += n;
                }
                return buf;
            }

            private static long RvaToOffset(int[][] sections, int rva)
            {
                foreach (var s in sections)
                {
                    int va = s[0], vsize = s[1], raw = s[2], rsize = s[3];
                    int span = Math.Max(vsize, rsize);
                    if (rva >= va && rva < va + span)
                        return raw + (rva - va);
                }
                throw new InvalidDataException($"RVA 0x{rva:X} is not inside any section");
            }

            // ---------- metadata root / streams ----------

            private void ReadStreams()
            {
                if (_md[0] != 'B' || _md[1] != 'S' || _md[2] != 'J' || _md[3] != 'B')
                    throw new InvalidDataException("no BSJB metadata signature");

                int versionLength = (int)U32(_md, 12);
                int p = 16 + versionLength;
                p += 2;                                  // flags
                int streamCount = U16(_md, p); p += 2;
                if (streamCount <= 0 || streamCount > 16)
                    throw new InvalidDataException("implausible stream count");

                int tablesBase = -1, tablesSize = 0;
                for (int i = 0; i < streamCount; i++)
                {
                    int off = (int)U32(_md, p);
                    int size = (int)U32(_md, p + 4);
                    p += 8;
                    int e = p;
                    while (e < _md.Length && _md[e] != 0) e++;
                    string name = Encoding.ASCII.GetString(_md, p, e - p);
                    p = (e + 1 + 3) & ~3;                // names are padded to a 4-byte boundary

                    if (off < 0 || size < 0 || (long)off + size > _md.Length)
                        throw new InvalidDataException($"stream '{name}' out of range");

                    switch (name)
                    {
                        case "#Strings": _stringsBase = off; _stringsSize = size; break;
                        case "#Blob": _blobBase = off; _blobSize = size; break;
                        case "#~":
                        case "#-": tablesBase = off; tablesSize = size; break;
                    }
                }

                if (tablesBase < 0) throw new InvalidDataException("no table stream");
                if (_stringsSize == 0) throw new InvalidDataException("no #Strings heap");
                if (_blobSize == 0) throw new InvalidDataException("no #Blob heap");
                _tablesBase = tablesBase;
                _tablesSize = tablesSize;
            }

            private int _tablesBase, _tablesSize;

            private void ReadTableHeader()
            {
                int b = _tablesBase;
                byte heapSizes = _md[b + 6];
                _strIdx = (heapSizes & 0x01) != 0 ? 4 : 2;
                _guidIdx = (heapSizes & 0x02) != 0 ? 4 : 2;
                _blobIdx = (heapSizes & 0x04) != 0 ? 4 : 2;

                ulong valid = U64(_md, b + 8);
                int p = b + 24;
                for (int t = 0; t < 64; t++)
                {
                    if (((valid >> t) & 1) == 0) continue;
                    _rows[t] = (int)U32(_md, p);
                    p += 4;
                }
                if ((heapSizes & 0x40) != 0) p += 4;   // extra-data word (ENC streams)
                _firstRowOffset = p;
            }

            private int _firstRowOffset;

            private int RowCount(int table) { int n; return _rows.TryGetValue(table, out n) ? n : 0; }

            private int SimpleIndexSize(int table) { return RowCount(table) >= 0x10000 ? 4 : 2; }

            private int CodedIndexSize(int[] tables, out int tagBits)
            {
                tagBits = 0;
                while ((1 << tagBits) < tables.Length) tagBits++;
                int limit = 1 << (16 - tagBits);
                foreach (var t in tables)
                    if (t >= 0 && RowCount(t) >= limit) { return 4; }
                return 2;
            }

            private void BuildLayout()
            {
                int caSize = CodedIndexSize(HasCustomAttribute, out _caTagBits);
                int catSize = CodedIndexSize(CustomAttributeType, out _catTagBits);
                int mrpSize = CodedIndexSize(MemberRefParent, out _mrpTagBits);
                int dummy;
                int rsSize = CodedIndexSize(ResolutionScope, out dummy);
                int tdrSize = CodedIndexSize(TypeDefOrRef, out dummy);
                int hcSize = CodedIndexSize(HasConstant, out dummy);

                int s = _strIdx, g = _guidIdx, bl = _blobIdx;

                // Only the tables at or before CustomAttribute (0x0C) need widths - we never read
                // past it, so the higher tables' schemas are irrelevant here.
                _layout[TBL_MODULE] = new[] { 2, s, g, g, g };
                _layout[TBL_TYPEREF] = new[] { rsSize, s, s };
                _layout[TBL_TYPEDEF] = new[] { 4, s, s, tdrSize, SimpleIndexSize(TBL_FIELD), SimpleIndexSize(TBL_METHODDEF) };
                _layout[TBL_FIELDPTR] = new[] { SimpleIndexSize(TBL_FIELD) };
                _layout[TBL_FIELD] = new[] { 2, s, bl };
                _layout[TBL_METHODPTR] = new[] { SimpleIndexSize(TBL_METHODDEF) };
                _layout[TBL_METHODDEF] = new[] { 4, 2, 2, s, bl, SimpleIndexSize(TBL_PARAM) };
                _layout[TBL_PARAMPTR] = new[] { SimpleIndexSize(TBL_PARAM) };
                _layout[TBL_PARAM] = new[] { 2, 2, s };
                _layout[TBL_INTERFACEIMPL] = new[] { SimpleIndexSize(TBL_TYPEDEF), tdrSize };
                _layout[TBL_MEMBERREF] = new[] { mrpSize, s, bl };
                _layout[TBL_CONSTANT] = new[] { 2, hcSize, bl };
                _layout[TBL_CUSTOMATTRIBUTE] = new[] { caSize, catSize, bl };

                int p = _firstRowOffset;
                foreach (var t in SortedTableIds())
                {
                    int[] widths;
                    if (!_layout.TryGetValue(t, out widths))
                        break;                              // reached a table past CustomAttribute
                    _tableOffset[t] = p;
                    p += RowCount(t) * RowSize(widths);
                    if (p > _tablesBase + _tablesSize)
                        throw new InvalidDataException("table stream overrun");
                }

                if (!_tableOffset.ContainsKey(TBL_CUSTOMATTRIBUTE) && RowCount(TBL_CUSTOMATTRIBUTE) > 0)
                    throw new InvalidDataException("could not locate CustomAttribute table");
            }

            private List<int> SortedTableIds()
            {
                var ids = new List<int>(_rows.Keys);
                ids.Sort();
                return ids;
            }

            private static int RowSize(int[] widths)
            {
                int n = 0;
                foreach (var w in widths) n += w;
                return n;
            }

            private uint[] ReadRow(int table, int rowIndex)
            {
                var widths = _layout[table];
                int o = _tableOffset[table] + rowIndex * RowSize(widths);
                var vals = new uint[widths.Length];
                for (int i = 0; i < widths.Length; i++)
                {
                    vals[i] = widths[i] == 2 ? U16(_md, o) : U32(_md, o);
                    o += widths[i];
                }
                return vals;
            }

            // ---------- heaps ----------

            private string ReadString(uint index)
            {
                int o = _stringsBase + (int)index;
                if (index >= (uint)_stringsSize) return null;
                int e = o;
                int end = _stringsBase + _stringsSize;
                while (e < end && _md[e] != 0) e++;
                return Encoding.UTF8.GetString(_md, o, e - o);
            }

            private byte[] ReadBlob(uint index)
            {
                if (index >= (uint)_blobSize) return null;
                int o = _blobBase + (int)index;
                int consumed;
                int len = (int)UncompressUnsigned(_md, o, out consumed);
                o += consumed;
                if (len < 0 || o + len > _blobBase + _blobSize) return null;
                var b = new byte[len];
                Buffer.BlockCopy(_md, o, b, 0, len);
                return b;
            }

            private static uint UncompressUnsigned(byte[] b, int o, out int consumed)
            {
                byte x = b[o];
                if ((x & 0x80) == 0) { consumed = 1; return x; }
                if ((x & 0x40) == 0) { consumed = 2; return (uint)(((x & 0x3F) << 8) | b[o + 1]); }
                consumed = 4;
                return (uint)(((x & 0x1F) << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
            }

            // ---------- attribute walk ----------

            private PluginMetadata ScanCustomAttributes()
            {
                var result = new PluginMetadata { DllPath = _path };
                int caRows = RowCount(TBL_CUSTOMATTRIBUTE);
                if (caRows == 0) return result;

                int skippedRows = 0;
                for (int i = 0; i < caRows; i++)
                {
                    string attrName;
                    uint ctorSigBlob;
                    uint valueBlob;
                    try
                    {
                        var row = ReadRow(TBL_CUSTOMATTRIBUTE, i);
                        valueBlob = row[2];
                        if (!TryResolveAttributeType(row[1], out attrName, out ctorSigBlob)) continue;
                    }
                    catch
                    {
                        skippedRows++;
                        continue;   // one bad row must not abort the whole assembly
                    }

                    if (attrName != AttrPlugin && attrName != AttrDependency) continue;

                    List<object> args;
                    try
                    {
                        var kinds = ReadCtorParamKinds(ctorSigBlob);
                        if (kinds == null) continue;
                        args = DecodeFixedArgs(valueBlob, kinds);
                        if (args == null) continue;
                        if (attrName == AttrPlugin) AddPlugin(result, args);
                        else AddDependency(result, kinds, args);
                    }
                    catch
                    {
                        skippedRows++;
                        continue;
                    }
                }

                // Breadcrumb, not per-row noise: a skipped row on a Plugin/Dependency attribute is
                // otherwise indistinguishable from "this DLL declares neither" (D17 - a silent
                // catch on a reflection-adjacent parse path must not read as a clean negative).
                if (skippedRows > 0)
                {
                    try { Plugin.Logger?.LogDebug($"PluginMetadataReader: skipped {skippedRows} malformed CustomAttribute row(s) in '{_path}'"); } catch { }
                }

                return result;
            }

            /// <summary>
            /// Resolves a CustomAttribute row's Type coded index to the attribute's type NAME plus the
            /// ctor's signature blob index. Only MemberRef ctors whose parent is a TypeRef are of
            /// interest: BepInPlugin/BepInDependency always live in the referenced BepInEx assembly.
            /// </summary>
            private bool TryResolveAttributeType(uint coded, out string typeName, out uint ctorSigBlob)
            {
                typeName = null;
                ctorSigBlob = 0;

                int tag = (int)(coded & (uint)((1 << _catTagBits) - 1));
                uint rid = coded >> _catTagBits;
                if (tag < 0 || tag >= CustomAttributeType.Length) return false;
                if (CustomAttributeType[tag] != TBL_MEMBERREF) return false;
                if (rid == 0 || rid > (uint)RowCount(TBL_MEMBERREF)) return false;

                var memberRef = ReadRow(TBL_MEMBERREF, (int)rid - 1);
                ctorSigBlob = memberRef[2];

                int parentTag = (int)(memberRef[0] & (uint)((1 << _mrpTagBits) - 1));
                uint parentRid = memberRef[0] >> _mrpTagBits;
                if (parentTag < 0 || parentTag >= MemberRefParent.Length) return false;
                if (MemberRefParent[parentTag] != TBL_TYPEREF) return false;
                if (parentRid == 0 || parentRid > (uint)RowCount(TBL_TYPEREF)) return false;

                var typeRef = ReadRow(TBL_TYPEREF, (int)parentRid - 1);
                typeName = ReadString(typeRef[1]);
                return typeName != null;
            }

            private enum ArgKind { String, Int32, Unsupported }

            /// <summary>
            /// Parses the ctor's MethodDefSig far enough to know each fixed argument's kind. The kinds
            /// - not argument position - are what distinguish BepInDependency's two overloads:
            /// (string, DependencyFlags) from (string, string).
            /// </summary>
            private List<ArgKind> ReadCtorParamKinds(uint sigBlobIndex)
            {
                var sig = ReadBlob(sigBlobIndex);
                if (sig == null || sig.Length < 3) return null;

                int o = 1;                                   // calling convention
                int consumed;
                int paramCount = (int)UncompressUnsigned(sig, o, out consumed);
                o += consumed;
                if (paramCount < 0 || paramCount > 16) return null;
                o += 1;                                      // return type (void for a ctor)

                var kinds = new List<ArgKind>(paramCount);
                for (int i = 0; i < paramCount; i++)
                {
                    if (o >= sig.Length) return null;
                    byte et = sig[o++];
                    switch (et)
                    {
                        case 0x0E:                            // ELEMENT_TYPE_STRING
                            kinds.Add(ArgKind.String);
                            break;
                        case 0x08:                            // ELEMENT_TYPE_I4
                            kinds.Add(ArgKind.Int32);
                            break;
                        case 0x11:                            // ELEMENT_TYPE_VALUETYPE + TypeDefOrRef
                            UncompressUnsigned(sig, o, out consumed);
                            o += consumed;
                            // DependencyFlags is an int-backed enum; a custom-attribute blob stores an
                            // enum as its underlying type. Any non-int-backed enum decodes wrong, so
                            // this is the one assumption here - and it holds for every BepInEx
                            // attribute we read.
                            kinds.Add(ArgKind.Int32);
                            break;
                        default:
                            kinds.Add(ArgKind.Unsupported);
                            break;
                    }
                }
                return kinds;
            }

            /// <summary>
            /// Decodes the CustomAttribute Value blob's fixed arguments: a 0x0001 prolog, then one
            /// serialized value per ctor parameter. Strings are SerStrings (compressed length + UTF-8,
            /// or a single 0xFF for null).
            /// </summary>
            private List<object> DecodeFixedArgs(uint valueBlobIndex, List<ArgKind> kinds)
            {
                var blob = ReadBlob(valueBlobIndex);
                if (blob == null || blob.Length < 2) return null;
                if (U16(blob, 0) != 1) return null;           // prolog

                int o = 2;
                var vals = new List<object>(kinds.Count);
                foreach (var k in kinds)
                {
                    if (k == ArgKind.String)
                    {
                        if (o >= blob.Length) return null;
                        if (blob[o] == 0xFF) { vals.Add(null); o += 1; continue; }
                        int consumed;
                        int len = (int)UncompressUnsigned(blob, o, out consumed);
                        o += consumed;
                        if (len < 0 || o + len > blob.Length) return null;
                        vals.Add(Encoding.UTF8.GetString(blob, o, len));
                        o += len;
                    }
                    else if (k == ArgKind.Int32)
                    {
                        if (o + 4 > blob.Length) return null;
                        vals.Add(BitConverter.ToInt32(blob, o));
                        o += 4;
                    }
                    else
                    {
                        return null;                          // unknown shape - report nothing rather than a guess
                    }
                }
                return vals;
            }

            private static void AddPlugin(PluginMetadata result, List<object> args)
            {
                // [BepInPlugin(GUID, Name, Version)]
                if (args.Count < 1) return;
                var guid = args[0] as string;
                if (!string.IsNullOrEmpty(guid) && !result.PluginGuids.Contains(guid))
                    result.PluginGuids.Add(guid);
            }

            private static void AddDependency(PluginMetadata result, List<ArgKind> kinds, List<object> args)
            {
                if (args.Count < 1) return;
                var guid = args[0] as string;
                if (string.IsNullOrEmpty(guid)) return;

                var dep = new DeclaredDependency { Guid = guid, IsHard = true, MinimumVersion = null };

                if (args.Count >= 2 && kinds.Count >= 2)
                {
                    if (kinds[1] == ArgKind.Int32 && args[1] is int)
                    {
                        // BepInDependency(string, DependencyFlags). Flags is a [Flags] enum, so test
                        // the bit rather than compare equality, and take the value from BepInEx's own
                        // enum instead of hardcoding it.
                        int flags = (int)args[1];
                        dep.IsHard = (flags & (int)BepInEx.BepInDependency.DependencyFlags.SoftDependency) == 0;
                    }
                    else if (kinds[1] == ArgKind.String)
                    {
                        // BepInDependency(string, string) is the minimum-version overload; BepInEx
                        // gives it Flags = HardDependency (verified against BepInEx.dll 2026-09-08).
                        dep.MinimumVersion = args[1] as string;
                        dep.IsHard = true;
                    }
                }

                result.Dependencies.Add(dep);
            }

            private static ushort U16(byte[] b, int o) { return (ushort)(b[o] | (b[o + 1] << 8)); }

            private static uint U32(byte[] b, int o)
            {
                return (uint)b[o] | ((uint)b[o + 1] << 8) | ((uint)b[o + 2] << 16) | ((uint)b[o + 3] << 24);
            }

            private static ulong U64(byte[] b, int o)
            {
                return U32(b, o) | ((ulong)U32(b, o + 4) << 32);
            }
        }
    }
}
