namespace CSFFModFramework.Gif;

/// <summary>
/// Runtime representation of a decoded GIF: frame sprites + per-frame delays.
/// Built by GifLoader and looked up by name from GifAnimationService.
/// </summary>
public class GifFrameSet
{
    public string Name;
    public Sprite[] Frames;
    public float[] Delays;    // per-frame delay in seconds (Safe variant from UnityGifDecoder)
    public bool Loop;
}
