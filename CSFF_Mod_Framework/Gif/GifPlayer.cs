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
    private Image _image;
    private GifFrameSet _current;
    private Coroutine _coroutine;
    private int _index;

    public bool IsPlaying => _coroutine != null;
    public GifFrameSet Current => _current;

    private void Awake()
    {
        _image = GetComponent<Image>();
    }

    private void OnDisable()
    {
        // Pause: coroutine is automatically stopped when the GO is disabled; clear reference.
        _coroutine = null;
    }

    private void OnEnable()
    {
        // Resume if we were playing before the GO was disabled.
        if (_current != null && _coroutine == null)
            _coroutine = StartCoroutine(PlayLoop());
    }

    /// <summary>
    /// Assign a new frame set and start playing it.
    /// Pass null to stop animation and clear the image override.
    /// </summary>
    public void SetFrameSet(GifFrameSet frameSet)
    {
        if (_current == frameSet) return;

        StopAnimation();
        _current = frameSet;

        if (_current != null && isActiveAndEnabled)
            _coroutine = StartCoroutine(PlayLoop());
    }

    public void Stop()
    {
        StopAnimation();
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

    private void StopAnimation()
    {
        if (_coroutine != null)
        {
            StopCoroutine(_coroutine);
            _coroutine = null;
        }
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
