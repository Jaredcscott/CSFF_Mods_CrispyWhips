using CSFFModFramework.Data;
using CSFFModFramework.Discovery;
using CSFFModFramework.Util;

namespace CSFFModFramework.Loading;

/// <summary>
/// Loads audio clips from mod directories. Supports WAV files natively via Unity's
/// AudioClip API. OGG and MP3 are not supported (would require bundling NAudio/NVorbis).
/// </summary>
internal static class AudioLoader
{
    public static void LoadAll(List<ModManifest> mods)
    {
        int totalLoaded = 0;

        foreach (var mod in mods)
        {
            int modCount = 0;

            foreach (var file in ModAssets.ResolveAudio(mod))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                var clipName = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrEmpty(clipName)) continue;
                if (Database.AudioClipDict.ContainsKey(clipName)) continue;

                try
                {
                    AudioClip clip = null;

                    if (ext == ".wav")
                    {
                        clip = LoadWav(file, clipName);
                    }
                    else
                    {
                        Log.Warn($"AudioLoader: unsupported format '{ext}' for {clipName} — only WAV is natively supported");
                        continue;
                    }

                    if (clip != null)
                    {
                        Database.AudioClipDict[clipName] = clip;
                        modCount++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"AudioLoader: failed to load {file}: {ex.Message}");
                }
            }

            if (modCount > 0)
                Log.Info($"AudioLoader: loaded {modCount} audio clips from {mod.Name}");
            totalLoaded += modCount;
        }

        if (totalLoaded > 0)
            Log.Info($"AudioLoader: {totalLoaded} total audio clips loaded");
    }

    private static AudioClip LoadWav(string filePath, string clipName)
    {
        var bytes = File.ReadAllBytes(filePath);

        // Parse WAV header to get format info
        if (bytes.Length < 44) return null;

        // RIFF header check
        if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F') return null;
        if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E') return null;

        int channels = BitConverter.ToInt16(bytes, 22);
        int sampleRate = BitConverter.ToInt32(bytes, 24);
        int bitsPerSample = BitConverter.ToInt16(bytes, 34);

        // Find data chunk
        int dataOffset = 12;
        int dataSize = 0;
        while (dataOffset < bytes.Length - 8)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(bytes, dataOffset, 4);
            var chunkSize = BitConverter.ToInt32(bytes, dataOffset + 4);
            if (chunkId == "data")
            {
                dataOffset += 8;
                dataSize = chunkSize;
                break;
            }
            dataOffset += 8 + chunkSize;
        }

        if (dataSize == 0 || dataOffset + dataSize > bytes.Length) return null;

        int bytesPerSample = bitsPerSample / 8;
        int sampleCount = dataSize / (bytesPerSample * channels);

        var clip = AudioClip.Create(clipName, sampleCount, channels, sampleRate, false);
        clip.name = clipName;

        // Convert to float samples
        var samples = new float[sampleCount * channels];
        int byteIndex = dataOffset;
        for (int i = 0; i < samples.Length && byteIndex < bytes.Length; i++)
        {
            if (bitsPerSample == 16)
            {
                if (byteIndex + 1 < bytes.Length)
                {
                    short sample = BitConverter.ToInt16(bytes, byteIndex);
                    samples[i] = sample / 32768f;
                }
                byteIndex += 2;
            }
            else if (bitsPerSample == 8)
            {
                samples[i] = (bytes[byteIndex] - 128) / 128f;
                byteIndex += 1;
            }
            else if (bitsPerSample == 24)
            {
                if (byteIndex + 2 < bytes.Length)
                {
                    int sample = bytes[byteIndex] | (bytes[byteIndex + 1] << 8) | (bytes[byteIndex + 2] << 16);
                    if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000);
                    samples[i] = sample / 8388608f;
                }
                byteIndex += 3;
            }
            else if (bitsPerSample == 32)
            {
                if (byteIndex + 3 < bytes.Length)
                {
                    samples[i] = BitConverter.ToSingle(bytes, byteIndex);
                }
                byteIndex += 4;
            }
        }

        clip.SetData(samples, 0);
        return clip;
    }
}
