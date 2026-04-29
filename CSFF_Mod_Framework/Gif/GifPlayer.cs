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

        int index = 0;
        while (true)
        {
            _image.sprite = _current.Frames[index];

            float delay = _current.Delays.Length > index ? _current.Delays[index] : 0.1f;
            yield return new WaitForSeconds(delay);

            index++;
            if (index >= _current.Frames.Length)
            {
                if (!_current.Loop) yield break;
                index = 0;
            }
        }
    }
}
