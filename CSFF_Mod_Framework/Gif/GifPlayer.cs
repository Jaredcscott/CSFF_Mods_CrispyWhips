using CSFFModFramework.Util;
using UnityEngine.UI;

namespace CSFFModFramework.Gif;

/// <summary>
/// MonoBehaviour that cycles through a GifFrameSet on a Unity UI Image.
/// Attach to the same GameObject as the Image component you want to animate.
/// Use Play()/Stop()/SetFrameSet() to drive it from GifAnimationPatch.
/// Default state after construction: stopped (no frames playing).
/// </summary>
public class GifPlayer : MonoBehaviour
{
    // How often a card whose definition has ConditionSets re-selects its GIF. Vanilla refreshes
    // the art only at Setup and on a cooking change, so without this a condition on Fuel or
    // Liquid (a kettle that steams only when hot) never switched while the card sat on screen.
    private const float ConditionRecheckSeconds = 1f;

    private Image _image;
    private GifFrameSet _current;
    private Coroutine _coroutine;
    private Coroutine _watch;
    private int _index;

    // The art vanilla put on the image before a GIF took it over, restored when the GIF stops,
    // so a card whose conditions stop matching shows its still art again rather than freezing
    // on whichever GIF frame was showing.
    private Sprite _staticSprite;
    private System.Func<GifFrameSet> _resolver;

    public bool IsPlaying => _coroutine != null;
    public GifFrameSet Current => _current;

    private void Awake()
    {
        _image = GetComponent<Image>();
    }

    private void OnDisable()
    {
        // Pause: coroutines are automatically stopped when the GO is disabled; clear references.
        _coroutine = null;
        _watch = null;
    }

    private void OnEnable()
    {
        // Resume if we were playing before the GO was disabled.
        if (_current != null && _coroutine == null)
            _coroutine = StartCoroutine(PlayLoop());
        if (_resolver != null && _watch == null)
            _watch = StartCoroutine(WatchLoop());
    }

    /// <summary>
    /// Records the art vanilla just wrote. Called on every Setup/refresh before the GIF is chosen:
    /// a sprite that is one of the playing GIF's own frames is never taken as the still art.
    /// </summary>
    public void CaptureStatic()
    {
        if (_image == null) _image = GetComponent<Image>();
        if (_image == null) return;
        var s = _image.overrideSprite;
        if (s != null && !IsOwnFrame(s)) _staticSprite = s;
    }

    /// <summary>
    /// Re-select the GIF every ConditionRecheckSeconds through <paramref name="resolver"/>
    /// (null stops watching). Used only for definitions with ConditionSets.
    /// </summary>
    public void SetResolver(System.Func<GifFrameSet> resolver)
    {
        _resolver = resolver;
        if (_watch != null) { StopCoroutine(_watch); _watch = null; }
        if (_resolver != null && isActiveAndEnabled) _watch = StartCoroutine(WatchLoop());
    }

    /// <summary>
    /// Assign a new frame set and start playing it.
    /// Pass null to stop animation and put the still art back.
    /// </summary>
    public void SetFrameSet(GifFrameSet frameSet)
    {
        if (_current == frameSet) return;
        if (frameSet == null) { Stop(); return; }

        StopAnimation();
        _current = frameSet;

        if (isActiveAndEnabled)
            _coroutine = StartCoroutine(PlayLoop());
    }

    /// <summary>Stops the GIF and restores the still art if a GIF frame is still showing.</summary>
    public void Stop()
    {
        StopAnimation();
        if (_image != null && _current != null && IsOwnFrame(_image.overrideSprite) && _staticSprite != null)
            _image.overrideSprite = _staticSprite;
        _current = null;
    }

    /// <summary>
    /// Re-applies the frame currently showing. Vanilla CardGraphics.Setup and RefreshCookingStatus
    /// write the static art into overrideSprite; calling this right after them avoids a frame of
    /// static art before the loop's next tick.
    /// </summary>
    public void ReapplyCurrentFrame()
    {
        if (_image == null || _current == null || _current.Frames.Length == 0) return;
        _image.overrideSprite = _current.Frames[Mathf.Clamp(_index, 0, _current.Frames.Length - 1)];
    }

    private bool IsOwnFrame(Sprite s)
    {
        if (s == null || _current?.Frames == null) return false;
        foreach (var f in _current.Frames)
            if (f == s) return true;
        return false;
    }

    private void StopAnimation()
    {
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = null;
        }
    }

    private System.Collections.IEnumerator WatchLoop()
    {
        while (_resolver != null)
        {
            yield return new WaitForSecondsRealtime(ConditionRecheckSeconds);
            GifFrameSet next;
            try { next = _resolver(); }
            catch (System.Exception ex)
            {
                Log.Debug($"GifPlayer: condition recheck failed, watching stopped: {Log.ExceptionText(ex)}");
                _resolver = null;
                yield break;
            }
            if (next != _current) SetFrameSet(next);
        }
        _watch = null;
    }

    private System.Collections.IEnumerator PlayLoop()
    {
        if (_image == null || _current == null || _current.Frames.Length == 0)
            yield break;

        // overrideSprite, not sprite: the card Image renders overrideSprite whenever it is set, and
        // vanilla always sets it (CardGraphics.Setup, RefreshCookingStatus), so frames written to
        // .sprite were never visible (fixed 2.26.7).
        _index = 0;
        while (true)
        {
            _image.overrideSprite = _current.Frames[_index];

            float delay = _current.Delays.Length > _index ? _current.Delays[_index] : 0.1f;
            yield return new WaitForSecondsRealtime(delay);

            _index++;
            if (_index >= _current.Frames.Length)
            {
                if (!_current.Loop) { _index = _current.Frames.Length - 1; yield break; }
                _index = 0;
            }
        }
    }
}
