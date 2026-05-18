using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CSFFModFramework.Util;
namespace CSFFModFramework.Loading;

/// <summary>
/// Caches decoded sprite textures so PNG decode is skipped on subsequent loads.
/// Cache lives under <c>BepInEx/plugins/CSFF_Mod_Framework/SpriteCache/</c>.
///
/// <para>Two layers, both keyed on PNG mtime+length (fast) or content MD5 (slow rescue):</para>
/// <list type="number">
///   <item><b>Bundle</b> (<c>bundle.bin</c>) — one file containing every entry. Read once
///   per load with a single <c>File.ReadAllBytes</c>; subsequent <see cref="TryLoad"/>
///   calls slice from the in-memory buffer. This is the hot path on a normal launch.</item>
///   <item><b>Per-file</b> (<c>&lt;md5&gt;.sc</c>) — one file per sprite. Used only as a
///   fallback when the bundle is missing/corrupt or a specific entry isn't in it.
///   Also kept up-to-date so a corrupt bundle can be rebuilt from per-file caches.</item>
/// </list>
///
/// <para>On every load, every successfully-loaded entry is queued to be written back
/// into a fresh bundle (<see cref="QueueBundleEntry"/>). The bundle is rebuilt at end
/// of load via <see cref="AwaitPendingWrites"/>. Stale bundle entries (PNGs that no
/// longer exist) are dropped from the next bundle.</para>
/// </summary>
internal static class SpriteTextureCache
{
    private static string _cacheDir;
    private static string _bundlePath;
    private static readonly byte[] Magic = { 0x43, 0x53, 0x46, 0x46 }; // "CSFF" — per-file
    private static readonly byte[] BundleMagic = { 0x43, 0x53, 0x46, 0x42 }; // "CSFB" — bundle
    // FormatVersion 2 = adds 16-byte content-hash to the per-file header.
    private const int FormatVersion = 2;
    private const int BundleVersion = 1;

    private static readonly List<Task> _pendingWrites = new();
    private static readonly object _pendingLock = new();

    private static int _cacheHits;
    private static int _cacheMisses;
    private static int _hashRescues;
    private static int _bundleHits;
    internal static int CacheHits => _cacheHits;
    internal static int CacheMisses => _cacheMisses;
    internal static int HashRescues => _hashRescues;
    internal static int BundleHits => _bundleHits;

    // Cumulative timing buckets (ticks) for the warm cache hit path.
    internal static long PngStatTicks;
    internal static long CacheReadTicks;
    internal static long TextureCreateTicks;
    internal static long GpuApplyTicks;
    internal static long BundleLoadTicks;

    // Bundle: kept alive for the duration of a load so sliced rawData stays valid.
    private static byte[] _bundleBuffer;
    private static Dictionary<string, BundleEntry> _bundleIndex;

    // Entries successfully resolved this load — written back as the next bundle.
    // Held on the main thread; snapshotted in WriteBundleAsync.
    private static readonly List<BundleEntry> _bundleWriteQueue = new();
    private static readonly HashSet<string> _bundleWriteSeen = new(StringComparer.OrdinalIgnoreCase);
    private static bool _anyEntryChanged;
    private static Task _bundleWriteTask;

    /// <summary>
    /// One sprite's worth of cache data. <see cref="DataArray"/> is preferred when set;
    /// otherwise the writer slices <see cref="_bundleBuffer"/> at <see cref="DataOffset"/>.
    /// This dual mode keeps memory low: bundle-hit entries don't need a copy.
    /// </summary>
    private struct BundleEntry
    {
        public string PngPath;
        public long PngMtime;
        public long PngLength;
        public byte[] Hash;          // 16 bytes
        public int Width;
        public int Height;
        public TextureFormat Format;
        public int DataLength;

        // Either DataArray is non-null (fresh data) or DataOffset references _bundleBuffer.
        public byte[] DataArray;
        public int DataOffset;
    }

    public static void Initialize()
    {
        _cacheDir = Path.Combine(PathUtil.FrameworkDir, "SpriteCache");
        _bundlePath = _cacheDir != null ? Path.Combine(_cacheDir, "bundle.bin") : null;

        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _hashRescues, 0);
        Interlocked.Exchange(ref _bundleHits, 0);
        Interlocked.Exchange(ref PngStatTicks, 0);
        Interlocked.Exchange(ref CacheReadTicks, 0);
        Interlocked.Exchange(ref TextureCreateTicks, 0);
        Interlocked.Exchange(ref GpuApplyTicks, 0);
        Interlocked.Exchange(ref BundleLoadTicks, 0);

        lock (_pendingLock) _pendingWrites.Clear();
        _bundleWriteQueue.Clear();
        _bundleWriteSeen.Clear();
        _anyEntryChanged = false;
        _bundleBuffer = null;
        _bundleIndex = null;

        try
        {
            Directory.CreateDirectory(_cacheDir);
        }
        catch (Exception ex)
        {
            Log.Warn($"SpriteTextureCache: failed to create cache dir: {Log.ExceptionText(ex)}");
            _cacheDir = null;
            _bundlePath = null;
            return;
        }

        LoadBundleIfExists();
    }

    /// <summary>
    /// Reads <c>bundle.bin</c> into memory and builds a path → entry index. Sets
    /// <see cref="_bundleBuffer"/> + <see cref="_bundleIndex"/> on success; leaves
    /// them null on miss/corrupt so the per-file fallback handles everything.
    /// </summary>
    private static void LoadBundleIfExists()
    {
        if (_bundlePath == null) return;

        var t0 = Stopwatch.GetTimestamp();
        byte[] buf;
        try { buf = File.ReadAllBytes(_bundlePath); }
        catch { return; } // missing — that's fine, bundle is optional
        Interlocked.Add(ref BundleLoadTicks, Stopwatch.GetTimestamp() - t0);

        try
        {
            // Header: magic(4) version(4) entryCount(4) = 12 bytes
            if (buf.Length < 12) { TryDelete(_bundlePath); return; }
            for (int i = 0; i < 4; i++)
                if (buf[i] != BundleMagic[i]) { TryDelete(_bundlePath); return; }
            if (BitConverter.ToInt32(buf, 4) != BundleVersion) { TryDelete(_bundlePath); return; }
            int entryCount = BitConverter.ToInt32(buf, 8);

            var index = new Dictionary<string, BundleEntry>(entryCount, StringComparer.OrdinalIgnoreCase);
            int pos = 12;
            for (int i = 0; i < entryCount; i++)
            {
                if (pos + 4 > buf.Length) return;
                int pathLen = BitConverter.ToInt32(buf, pos); pos += 4;
                if (pathLen <= 0 || pathLen > 4096 || pos + pathLen > buf.Length) return;
                string path = Encoding.UTF8.GetString(buf, pos, pathLen); pos += pathLen;

                if (pos + 8 + 8 + 16 + 4 + 4 + 4 + 4 > buf.Length) return;
                long mtime = BitConverter.ToInt64(buf, pos); pos += 8;
                long length = BitConverter.ToInt64(buf, pos); pos += 8;

                var hash = new byte[16];
                Array.Copy(buf, pos, hash, 0, 16); pos += 16;

                int width = BitConverter.ToInt32(buf, pos); pos += 4;
                int height = BitConverter.ToInt32(buf, pos); pos += 4;
                var format = (TextureFormat)BitConverter.ToInt32(buf, pos); pos += 4;
                int dataLen = BitConverter.ToInt32(buf, pos); pos += 4;

                if (dataLen < 0 || pos + dataLen > buf.Length) return;
                int dataOffset = pos;
                pos += dataLen;

                index[path] = new BundleEntry
                {
                    PngPath = path,
                    PngMtime = mtime,
                    PngLength = length,
                    Hash = hash,
                    Width = width,
                    Height = height,
                    Format = format,
                    DataLength = dataLen,
                    DataArray = null,
                    DataOffset = dataOffset,
                };
            }

            _bundleBuffer = buf;
            _bundleIndex = index;
        }
        catch (Exception ex)
        {
            Log.Warn($"SpriteTextureCache: bundle parse failed ({Log.ExceptionText(ex)}); falling back to per-file cache");
            _bundleBuffer = null;
            _bundleIndex = null;
            TryDelete(_bundlePath);
        }
    }

    /// <summary>
    /// Attempts to load a texture from cache. Tries the bundle first (one in-memory
    /// dictionary lookup + slice), then the per-file <c>.sc</c> fallback.
    /// </summary>
    public static bool TryLoad(string pngPath, out Texture2D texture)
    {
        texture = null;
        if (_cacheDir == null) return false;

        // Bundle fast path
        if (_bundleIndex != null && TryLoadFromBundle(pngPath, out texture))
            return true;

        // Per-file fallback
        return TryLoadFromPerFile(pngPath, out texture);
    }

    private static bool TryLoadFromBundle(string pngPath, out Texture2D texture)
    {
        texture = null;
        if (!_bundleIndex.TryGetValue(pngPath, out var entry))
            return false;

        var t1 = Stopwatch.GetTimestamp();
        long pngMtime;
        long pngLength;
        try
        {
            var info = new FileInfo(pngPath);
            pngMtime = info.LastWriteTimeUtc.Ticks;
            pngLength = info.Length;
        }
        catch { return false; }
        Interlocked.Add(ref PngStatTicks, Stopwatch.GetTimestamp() - t1);

        if (entry.PngMtime != pngMtime || entry.PngLength != pngLength)
        {
            // Stale entry — try content-hash rescue before giving up. The PNG bytes
            // and the entry's hash are cheaper to compare than re-decoding.
            byte[] pngBytes;
            try { pngBytes = File.ReadAllBytes(pngPath); }
            catch { return false; }
            if (pngBytes.LongLength != entry.PngLength) return false;
            using var md5 = MD5.Create();
            var actualHash = md5.ComputeHash(pngBytes);
            for (int i = 0; i < 16; i++)
                if (actualHash[i] != entry.Hash[i]) return false;

            // Hash matched → refresh metadata so next bundle write has correct mtime+length.
            entry.PngMtime = pngMtime;
            entry.PngLength = pngLength;
            _anyEntryChanged = true; // bundle needs rewrite
            Interlocked.Increment(ref _hashRescues);
        }

        try
        {
            var t2 = Stopwatch.GetTimestamp();
            texture = new Texture2D(entry.Width, entry.Height, entry.Format, false);
            unsafe
            {
                fixed (byte* p = &_bundleBuffer[entry.DataOffset])
                    texture.LoadRawTextureData((IntPtr)p, entry.DataLength);
            }
            Interlocked.Add(ref TextureCreateTicks, Stopwatch.GetTimestamp() - t2);

            var t3 = Stopwatch.GetTimestamp();
            texture.Apply(false, true);
            Interlocked.Add(ref GpuApplyTicks, Stopwatch.GetTimestamp() - t3);

            Interlocked.Increment(ref _cacheHits);
            Interlocked.Increment(ref _bundleHits);
            QueueBundleEntry(entry); // re-record so it stays in next bundle
            return true;
        }
        catch
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
            return false;
        }
    }

    private static bool TryLoadFromPerFile(string pngPath, out Texture2D texture)
    {
        texture = null;
        var cachePath = GetCachePath(pngPath);

        var t0 = Stopwatch.GetTimestamp();
        byte[] buf;
        try { buf = File.ReadAllBytes(cachePath); }
        catch { return false; }
        Interlocked.Add(ref CacheReadTicks, Stopwatch.GetTimestamp() - t0);

        // Per-file header: magic(4) version(4) mtime(8) length(8) hash(16) w(4) h(4) format(4) byteCount(4) = 56
        const int HeaderSize = 56;
        if (buf.Length < HeaderSize) { TryDelete(cachePath); return false; }
        for (int i = 0; i < 4; i++)
            if (buf[i] != Magic[i]) { TryDelete(cachePath); return false; }
        if (BitConverter.ToInt32(buf, 4) != FormatVersion) { TryDelete(cachePath); return false; }

        long storedMtime = BitConverter.ToInt64(buf, 8);
        long storedLength = BitConverter.ToInt64(buf, 16);
        int width = BitConverter.ToInt32(buf, 40);
        int height = BitConverter.ToInt32(buf, 44);
        var format = (TextureFormat)BitConverter.ToInt32(buf, 48);
        int byteCount = BitConverter.ToInt32(buf, 52);
        if (buf.Length < HeaderSize + byteCount) { TryDelete(cachePath); return false; }

        var t1 = Stopwatch.GetTimestamp();
        long pngMtime;
        long pngLength;
        try
        {
            var pngInfo = new FileInfo(pngPath);
            pngMtime = pngInfo.LastWriteTimeUtc.Ticks;
            pngLength = pngInfo.Length;
        }
        catch { return false; }
        Interlocked.Add(ref PngStatTicks, Stopwatch.GetTimestamp() - t1);

        bool fastMatch = storedMtime == pngMtime && storedLength == pngLength;
        if (!fastMatch)
        {
            byte[] pngBytes;
            try { pngBytes = File.ReadAllBytes(pngPath); }
            catch { return false; }
            if (pngBytes.LongLength != storedLength) return false;
            using var md5 = MD5.Create();
            var actualHash = md5.ComputeHash(pngBytes);
            for (int i = 0; i < 16; i++)
                if (actualHash[i] != buf[24 + i]) return false;

            ScheduleHeaderRefresh(cachePath, pngMtime, pngLength);
            Interlocked.Increment(ref _hashRescues);
            storedMtime = pngMtime;
            storedLength = pngLength;
        }

        try
        {
            var t2 = Stopwatch.GetTimestamp();
            texture = new Texture2D(width, height, format, false);
            unsafe
            {
                fixed (byte* p = &buf[HeaderSize])
                    texture.LoadRawTextureData((IntPtr)p, byteCount);
            }
            Interlocked.Add(ref TextureCreateTicks, Stopwatch.GetTimestamp() - t2);

            var t3 = Stopwatch.GetTimestamp();
            texture.Apply(false, true);
            Interlocked.Add(ref GpuApplyTicks, Stopwatch.GetTimestamp() - t3);

            Interlocked.Increment(ref _cacheHits);

            // This entry came from a per-file cache rather than the bundle, so the
            // bundle is missing it — flag a rewrite. Copy raw data into a fresh
            // array since `buf` is local to this method.
            var hash = new byte[16];
            Array.Copy(buf, 24, hash, 0, 16);
            var rawCopy = new byte[byteCount];
            Array.Copy(buf, HeaderSize, rawCopy, 0, byteCount);
            QueueBundleEntry(new BundleEntry
            {
                PngPath = pngPath,
                PngMtime = storedMtime,
                PngLength = storedLength,
                Hash = hash,
                Width = width,
                Height = height,
                Format = format,
                DataLength = byteCount,
                DataArray = rawCopy,
                DataOffset = 0,
            });
            _anyEntryChanged = true;
            return true;
        }
        catch
        {
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
            return false;
        }
    }

    /// <summary>
    /// Schedules a per-file cache entry write on a background thread (PNG decode path).
    /// Also records the entry for the bundle write at end of load.
    /// </summary>
    public static void SaveAsync(string pngPath, byte[] pngBytes, Texture2D texture)
    {
        if (_cacheDir == null) return;
        try
        {
            var rawData = texture.GetRawTextureData();
            var width = texture.width;
            var height = texture.height;
            var format = texture.format;
            var name = Path.GetFileName(pngPath);
            long mtime;
            long length;
            try
            {
                var info = new FileInfo(pngPath);
                mtime = info.LastWriteTimeUtc.Ticks;
                length = info.Length;
            }
            catch { mtime = 0; length = pngBytes.LongLength; }

            // Compute hash on main thread so we can record the bundle entry immediately.
            // (MD5 of the PNG bytes is fast — same cost as the per-file writer doing it.)
            byte[] hash;
            using (var md5 = MD5.Create()) hash = md5.ComputeHash(pngBytes);

            QueueBundleEntry(new BundleEntry
            {
                PngPath = pngPath,
                PngMtime = mtime,
                PngLength = length,
                Hash = hash,
                Width = width,
                Height = height,
                Format = format,
                DataLength = rawData.Length,
                DataArray = rawData,
                DataOffset = 0,
            });
            _anyEntryChanged = true;

            var task = Task.Run(() =>
                WriteCacheFile(pngPath, mtime, length, hash, rawData, width, height, (int)format, name));
            lock (_pendingLock) _pendingWrites.Add(task);
            Interlocked.Increment(ref _cacheMisses);
        }
        catch (Exception ex)
        {
            Log.Warn($"SpriteTextureCache: failed to schedule save for {Path.GetFileName(pngPath)}: {Log.ExceptionText(ex)}");
            Interlocked.Increment(ref _cacheMisses);
        }
    }

    private static void QueueBundleEntry(BundleEntry entry)
    {
        if (_bundleWriteSeen.Add(entry.PngPath))
            _bundleWriteQueue.Add(entry);
    }

    private static void WriteCacheFile(string pngPath, long mtime, long length, byte[] hash,
        byte[] rawData, int width, int height, int format, string nameForLog)
    {
        try
        {
            using var bw = new BinaryWriter(File.Create(GetCachePath(pngPath)));
            bw.Write(Magic);
            bw.Write(FormatVersion);
            bw.Write(mtime);
            bw.Write(length);
            bw.Write(hash); // 16 bytes
            bw.Write(width);
            bw.Write(height);
            bw.Write(format);
            bw.Write(rawData.Length);
            bw.Write(rawData);
        }
        catch (Exception ex)
        {
            Log.Warn($"SpriteTextureCache: background save failed for {nameForLog}: {Log.ExceptionText(ex)}");
        }
    }

    private static void ScheduleHeaderRefresh(string cachePath, long newMtime, long newLength)
    {
        var task = Task.Run(() =>
        {
            try
            {
                using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Write, FileShare.Read);
                fs.Seek(8, SeekOrigin.Begin); // mtime is at byte 8
                using var bw = new BinaryWriter(fs);
                bw.Write(newMtime);
                bw.Write(newLength);
            }
            catch (Exception ex)
            {
                Log.Debug($"SpriteTextureCache: header refresh failed for {Path.GetFileName(cachePath)}: {Log.ExceptionText(ex)}");
            }
        });
        lock (_pendingLock) _pendingWrites.Add(task);
    }

    /// <summary>
    /// Block until all background per-file writes finish, then write the bundle if any
    /// entries changed (or if the bundle was missing). Called by <c>LoadOrchestrator</c>
    /// right before reporting "Loading Complete".
    /// </summary>
    /// <summary>
    /// Waits up to 3 s for the bundle write task to finish. Called from Plugin.OnDestroy
    /// so a clean game exit doesn't abandon a half-written bundle.bin.
    /// </summary>
    public static void FlushBundleWrite()
    {
        var t = _bundleWriteTask;
        if (t == null || t.IsCompleted) return;
        try { t.Wait(3000); }
        catch (Exception ex) { Log.Warn($"SpriteTextureCache: FlushBundleWrite: {Log.ExceptionText(ex)}"); }
    }

    public static void AwaitPendingWrites()
    {
        Task[] tasks;
        lock (_pendingLock)
        {
            if (_pendingWrites.Count > 0)
            {
                tasks = _pendingWrites.ToArray();
                _pendingWrites.Clear();
            }
            else
            {
                tasks = Array.Empty<Task>();
            }
        }

        if (tasks.Length > 0)
        {
            try { Task.WaitAll(tasks, 5000); }
            catch (Exception ex)
            {
                Log.Warn($"SpriteTextureCache: AwaitPendingWrites encountered an error: {Log.ExceptionText(ex)}");
            }
        }

        TryWriteBundle();
    }

    /// <summary>
    /// Writes <c>bundle.bin</c> from the entries queued during this load. Skipped when:
    ///   - the bundle is fresh and contained exactly the same entries we re-queued
    ///   - no entries were queued (no sprites loaded — nothing to bundle)
    /// </summary>
    private static void TryWriteBundle()
    {
        if (_bundlePath == null) return;
        if (_bundleWriteQueue.Count == 0) return;

        // If bundle on disk already matches what we'd write, skip the rewrite.
        bool bundleUpToDate = !_anyEntryChanged
            && _bundleIndex != null
            && _bundleIndex.Count == _bundleWriteQueue.Count;
        if (bundleUpToDate) return;

        // Snapshot under lock — entries reference _bundleBuffer for bundle-hit slices,
        // which stays alive for the lifetime of the load.
        var entries = _bundleWriteQueue.ToArray();
        var bundleBuffer = _bundleBuffer; // captured for the closure

        // Per-file .sc caches are on disk as fallback; track the task so Plugin.OnDestroy
        // can wait for it before the process exits and the write is abandoned.
        _bundleWriteTask = Task.Run(() => WriteBundleFile(_bundlePath, entries, bundleBuffer));
    }

    private static void WriteBundleFile(string bundlePath, BundleEntry[] entries, byte[] bundleBuffer)
    {
        var tmp = bundlePath + ".tmp";
        try
        {
            using (var fs = File.Create(tmp))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(BundleMagic);
                bw.Write(BundleVersion);
                bw.Write(entries.Length);

                foreach (var e in entries)
                {
                    var pathBytes = Encoding.UTF8.GetBytes(e.PngPath);
                    bw.Write(pathBytes.Length);
                    bw.Write(pathBytes);
                    bw.Write(e.PngMtime);
                    bw.Write(e.PngLength);
                    bw.Write(e.Hash); // 16 bytes
                    bw.Write(e.Width);
                    bw.Write(e.Height);
                    bw.Write((int)e.Format);
                    bw.Write(e.DataLength);
                    if (e.DataArray != null)
                        bw.Write(e.DataArray, 0, e.DataLength);
                    else
                        bw.Write(bundleBuffer, e.DataOffset, e.DataLength);
                }
            }

            // Atomic replace so a crash mid-write can't leave a corrupt bundle.
            // File.Replace is atomic on NTFS; fall back to Move when target doesn't exist yet.
            if (File.Exists(bundlePath))
                File.Replace(tmp, bundlePath, null);
            else
                File.Move(tmp, bundlePath);
        }
        catch (Exception ex)
        {
            Log.Warn($"SpriteTextureCache: failed to write bundle: {Log.ExceptionText(ex)}");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    [ThreadStatic] private static MD5 _md5;

    private static string GetCachePath(string pngPath)
    {
        _md5 ??= MD5.Create();
        var hash = _md5.ComputeHash(Encoding.UTF8.GetBytes(pngPath.ToLowerInvariant()));
        var sb = new StringBuilder(32);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return Path.Combine(_cacheDir, sb.ToString() + ".sc");
    }
}
