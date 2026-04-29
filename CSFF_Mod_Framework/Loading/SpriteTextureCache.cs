using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CSFFModFramework.Util;
namespace CSFFModFramework.Loading;

/// <summary>
/// Caches decoded sprite textures as raw bytes so PNG decode is skipped on subsequent loads.
/// Cache lives under <c>BepInEx/plugins/CSFF_Mod_Framework/SpriteCache/</c>.
///
/// <para>Hit path is two-tier:</para>
/// <list type="number">
///   <item>Fast mtime+size match — no PNG read, no hashing. Used on the typical
///   "didn't redeploy since last launch" launch.</item>
///   <item>Slow content-hash match — reads PNG bytes and MD5s them. Survives a
///   redeploy that only changed mtimes (e.g. <c>cp</c> from <c>/deploy-mods</c>).
///   On a slow-path hit we rewrite the cache header with the new mtime so the
///   next launch goes back to the fast path.</item>
/// </list>
///
/// First load is unchanged; every load after is dramatically faster.
/// </summary>
internal static class SpriteTextureCache
{
    private static string _cacheDir;
    private static readonly byte[] Magic = { 0x43, 0x53, 0x46, 0x46 }; // "CSFF"
    // FormatVersion 2 = adds 16-byte content-hash to the header so a redeploy that
    // only changes mtimes (cp) can still hit the cache via the hash path.
    private const int FormatVersion = 2;

    // Pending Save tasks queued during this load. AwaitPendingWrites() is called by
    // LoadOrchestrator before logging "Loading Complete" so writes always finish
    // within the load pass (avoids cross-load races on the same cache path).
    private static readonly List<Task> _pendingWrites = new();
    private static readonly object _pendingLock = new();

    private static int _cacheHits;
    private static int _cacheMisses;
    private static int _hashRescues;
    internal static int CacheHits => _cacheHits;
    internal static int CacheMisses => _cacheMisses;
    internal static int HashRescues => _hashRescues;

    public static void Initialize()
    {
        _cacheDir = Path.Combine(PathUtil.FrameworkDir, "SpriteCache");
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _hashRescues, 0);
        lock (_pendingLock) _pendingWrites.Clear();
        try
        {
            Directory.CreateDirectory(_cacheDir);
        }
        catch (Exception ex)
        {
            Log.Warn($"SpriteTextureCache: failed to create cache dir: {ex.Message}");
            _cacheDir = null;
        }
    }

    /// <summary>
    /// Attempts to load a texture from the cache. Tries the fast (mtime+size) match
    /// first; falls back to a content-hash compare so a fresh redeploy still hits.
    /// The returned texture has already been uploaded to the GPU (CPU copy freed).
    /// </summary>
    public static bool TryLoad(string pngPath, out Texture2D texture)
    {
        texture = null;
        if (_cacheDir == null) return false;
        var cachePath = GetCachePath(pngPath);
        if (!File.Exists(cachePath)) return false;

        FileInfo pngInfo;
        try { pngInfo = new FileInfo(pngPath); }
        catch { return false; }

        // Open once, peek the header, then decide which path to take.
        try
        {
            byte[] storedHash = new byte[16];
            long storedMtime;
            long storedLength;
            int width, height, byteCount;
            TextureFormat format;
            byte[] rawData;
            bool fastMatch;

            using (var br = new BinaryReader(File.OpenRead(cachePath)))
            {
                var magic = br.ReadBytes(4);
                for (int i = 0; i < 4; i++)
                    if (magic[i] != Magic[i]) { TryDelete(cachePath); return false; }
                if (br.ReadInt32() != FormatVersion) { TryDelete(cachePath); return false; }

                storedMtime = br.ReadInt64();
                storedLength = br.ReadInt64();
                if (br.Read(storedHash, 0, 16) != 16) { TryDelete(cachePath); return false; }

                width = br.ReadInt32();
                height = br.ReadInt32();
                format = (TextureFormat)br.ReadInt32();
                byteCount = br.ReadInt32();
                rawData = br.ReadBytes(byteCount);

                fastMatch = storedMtime == pngInfo.LastWriteTimeUtc.Ticks
                         && storedLength == pngInfo.Length;
            }

            if (!fastMatch)
            {
                // mtime/size changed — try the content hash before giving up. Reading
                // + hashing the PNG is much cheaper than re-decoding it (LoadImage).
                byte[] pngBytes;
                try { pngBytes = File.ReadAllBytes(pngPath); }
                catch { return false; }

                if (pngBytes.LongLength != storedLength) return false;

                using var md5 = MD5.Create();
                var actualHash = md5.ComputeHash(pngBytes);
                for (int i = 0; i < 16; i++)
                    if (actualHash[i] != storedHash[i]) return false;

                // Hash matched — refresh the mtime+size header so next launch goes
                // back to the fast path. Fire-and-forget on a background thread.
                ScheduleHeaderRefresh(cachePath, pngInfo.LastWriteTimeUtc.Ticks, pngInfo.Length);
                Interlocked.Increment(ref _hashRescues);
            }

            texture = new Texture2D(width, height, format, false);
            texture.LoadRawTextureData(rawData);
            texture.Apply(false, true); // upload to GPU + free CPU memory
            Interlocked.Increment(ref _cacheHits);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Schedules a cache entry write on a background thread. Must be called while the
    /// texture is still CPU-readable; <paramref name="rawData"/> is captured up front
    /// so the texture can be safely freed on the main thread immediately after.
    ///
    /// <paramref name="pngBytes"/> is the raw PNG content the caller already read
    /// for <see cref="Texture2D.LoadImage"/>; reusing it here avoids a redundant
    /// re-read from disk on every cache miss.
    ///
    /// Call <see cref="AwaitPendingWrites"/> at the end of the load pass to flush
    /// any in-flight writes before declaring the load complete.
    /// </summary>
    public static void SaveAsync(string pngPath, byte[] pngBytes, Texture2D texture)
    {
        if (_cacheDir == null) return;
        try
        {
            // Capture every Unity-side input on the main thread. The background
            // task only does file IO + MD5 — never touches Unity APIs.
            var rawData = texture.GetRawTextureData();
            var width = texture.width;
            var height = texture.height;
            var format = (int)texture.format;
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

            var task = Task.Run(() =>
                WriteCacheFile(pngPath, pngBytes, mtime, length, rawData, width, height, format, name));
            lock (_pendingLock) _pendingWrites.Add(task);
            Interlocked.Increment(ref _cacheMisses);
        }
        catch (Exception ex)
        {
            Log.Warn($"SpriteTextureCache: failed to schedule save for {Path.GetFileName(pngPath)}: {ex.Message}");
            Interlocked.Increment(ref _cacheMisses);
        }
    }

    private static void WriteCacheFile(string pngPath, byte[] pngBytes,
        long mtime, long length, byte[] rawData,
        int width, int height, int format, string nameForLog)
    {
        try
        {
            byte[] hash;
            using (var md5 = MD5.Create()) hash = md5.ComputeHash(pngBytes);

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
            Log.Warn($"SpriteTextureCache: background save failed for {nameForLog}: {ex.Message}");
        }
    }

    /// <summary>
    /// Rewrite just the mtime+size fields in an existing cache header. Used after
    /// a content-hash rescue so the next launch hits the fast (mtime) path.
    /// </summary>
    private static void ScheduleHeaderRefresh(string cachePath, long newMtime, long newLength)
    {
        var task = Task.Run(() =>
        {
            try
            {
                using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Write, FileShare.Read);
                // Skip magic (4) + version (4) → mtime starts at byte 8.
                fs.Seek(8, SeekOrigin.Begin);
                using var bw = new BinaryWriter(fs);
                bw.Write(newMtime);
                bw.Write(newLength);
            }
            catch (Exception ex)
            {
                Log.Debug($"SpriteTextureCache: header refresh failed for {Path.GetFileName(cachePath)}: {ex.Message}");
            }
        });
        lock (_pendingLock) _pendingWrites.Add(task);
    }

    /// <summary>
    /// Block until all background SaveAsync writes scheduled this load finish.
    /// Called by <c>LoadOrchestrator</c> right before reporting "Loading Complete"
    /// so cache files are durable before the next launch reads them.
    /// </summary>
    public static void AwaitPendingWrites()
    {
        Task[] tasks;
        lock (_pendingLock)
        {
            if (_pendingWrites.Count == 0) return;
            tasks = _pendingWrites.ToArray();
            _pendingWrites.Clear();
        }
        try
        {
            // Generous ceiling — 141 small file writes finish in ~50ms in practice.
            Task.WaitAll(tasks, 5000);
        }
        catch (Exception ex)
        {
            Log.Warn($"SpriteTextureCache: AwaitPendingWrites encountered an error: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    // Cache filename = MD5 of the PNG path (stable across launches and redeploys).
    // Content versioning is handled by the header, not the filename.
    private static string GetCachePath(string pngPath)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pngPath.ToLowerInvariant()));
        var sb = new System.Text.StringBuilder(32);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return Path.Combine(_cacheDir, sb.ToString() + ".sc");
    }
}
