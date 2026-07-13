using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace mod_update_manager
{
    /// <summary>
    /// Minimal reader for ZIP archives containing only Stored (uncompressed) entries.
    /// Exists because System.IO.Compression cannot be resolved in this game's Mono runtime
    /// (confirmed: FileNotFoundException loading System.IO.Compression at Plugin.Awake).
    /// Suite ZIPs are packed with CompressionLevel.NoCompression by Pack-Suite.ps1 specifically
    /// so this reader never has to inflate anything — it only parses ZIP structure and copies bytes.
    /// </summary>
    internal static class MiniZip
    {
        private const uint EocdSignature = 0x06054b50;
        private const uint CentralDirectorySignature = 0x02014b50;
        private const uint LocalFileHeaderSignature = 0x04034b50;
        private const ushort StoredMethod = 0;

        public class Entry
        {
            public string Name;
            public long LocalHeaderOffset;
            public long CompressedSize;
            public long UncompressedSize;
            public ushort Method;
        }

        public static List<Entry> ReadEntries(Stream zip)
        {
            if (!zip.CanSeek) throw new NotSupportedException("MiniZip requires a seekable stream.");

            // EOCD is the fixed-size trailing 22 bytes for a comment-less archive.
            // Pack-Suite.ps1 never sets a ZIP comment, so this holds for every Suite ZIP.
            const int eocdSize = 22;
            if (zip.Length < eocdSize) throw new InvalidDataException("MiniZip: stream too small to be a ZIP archive.");

            zip.Seek(-eocdSize, SeekOrigin.End);
            var reader = new BinaryReader(zip, Encoding.UTF8, leaveOpen: true);

            if (reader.ReadUInt32() != EocdSignature)
                throw new InvalidDataException("MiniZip: end-of-central-directory record not found (unsupported ZIP comment or format).");

            reader.ReadUInt16(); // disk number
            reader.ReadUInt16(); // disk where central directory starts
            reader.ReadUInt16(); // central directory records on this disk
            ushort totalEntries = reader.ReadUInt16();
            reader.ReadUInt32(); // size of central directory
            uint cdOffset = reader.ReadUInt32();

            zip.Seek(cdOffset, SeekOrigin.Begin);
            var entries = new List<Entry>(totalEntries);

            for (int i = 0; i < totalEntries; i++)
            {
                if (reader.ReadUInt32() != CentralDirectorySignature)
                    throw new InvalidDataException($"MiniZip: bad central directory entry signature at index {i}.");

                reader.ReadUInt16(); // version made by
                reader.ReadUInt16(); // version needed to extract
                reader.ReadUInt16(); // general purpose bit flag
                ushort method = reader.ReadUInt16();
                reader.ReadUInt16(); // last mod time
                reader.ReadUInt16(); // last mod date
                reader.ReadUInt32(); // crc-32
                uint compressedSize = reader.ReadUInt32();
                uint uncompressedSize = reader.ReadUInt32();
                ushort nameLen = reader.ReadUInt16();
                ushort extraLen = reader.ReadUInt16();
                ushort commentLen = reader.ReadUInt16();
                reader.ReadUInt16(); // disk number start
                reader.ReadUInt16(); // internal file attributes
                reader.ReadUInt32(); // external file attributes
                uint localHeaderOffset = reader.ReadUInt32();

                byte[] nameBytes = reader.ReadBytes(nameLen);
                string name = Encoding.UTF8.GetString(nameBytes, 0, nameBytes.Length);

                if (extraLen > 0) reader.ReadBytes(extraLen);
                if (commentLen > 0) reader.ReadBytes(commentLen);

                entries.Add(new Entry
                {
                    Name = name,
                    LocalHeaderOffset = localHeaderOffset,
                    CompressedSize = compressedSize,
                    UncompressedSize = uncompressedSize,
                    Method = method
                });
            }

            return entries;
        }

        public static byte[] ExtractBytes(Stream zip, Entry entry)
        {
            long dataOffset = LocateDataOffset(zip, entry);
            zip.Seek(dataOffset, SeekOrigin.Begin);

            var data = new byte[entry.UncompressedSize];
            int read = 0;
            while (read < data.Length)
            {
                int n = zip.Read(data, read, data.Length - read);
                if (n <= 0) throw new EndOfStreamException($"MiniZip: unexpected end of stream reading '{entry.Name}'.");
                read += n;
            }
            return data;
        }

        public static void ExtractToFile(Stream zip, Entry entry, string destPath)
        {
            long dataOffset = LocateDataOffset(zip, entry);
            zip.Seek(dataOffset, SeekOrigin.Begin);

            const int bufSize = 81920;
            var buffer = new byte[bufSize];
            long remaining = entry.UncompressedSize;

            using (var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
            {
                while (remaining > 0)
                {
                    int toRead = (int)Math.Min(bufSize, remaining);
                    int n = zip.Read(buffer, 0, toRead);
                    if (n <= 0) throw new EndOfStreamException($"MiniZip: unexpected end of stream extracting '{entry.Name}'.");
                    outStream.Write(buffer, 0, n);
                    remaining -= n;
                }
            }
        }

        private static long LocateDataOffset(Stream zip, Entry entry)
        {
            if (entry.Method != StoredMethod)
                throw new NotSupportedException($"MiniZip: entry '{entry.Name}' uses compression method {entry.Method}; only Stored (0) is supported. Re-pack Suite ZIPs with CompressionLevel.NoCompression.");

            zip.Seek(entry.LocalHeaderOffset, SeekOrigin.Begin);
            var reader = new BinaryReader(zip, Encoding.UTF8, leaveOpen: true);

            if (reader.ReadUInt32() != LocalFileHeaderSignature)
                throw new InvalidDataException($"MiniZip: bad local file header signature for '{entry.Name}'.");

            reader.ReadUInt16(); // version needed to extract
            reader.ReadUInt16(); // general purpose bit flag
            reader.ReadUInt16(); // compression method
            reader.ReadUInt16(); // last mod time
            reader.ReadUInt16(); // last mod date
            reader.ReadUInt32(); // crc-32
            reader.ReadUInt32(); // compressed size
            reader.ReadUInt32(); // uncompressed size
            ushort nameLen = reader.ReadUInt16();
            ushort extraLen = reader.ReadUInt16();

            return entry.LocalHeaderOffset + 30 + nameLen + extraLen;
        }
    }
}
