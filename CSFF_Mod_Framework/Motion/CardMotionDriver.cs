namespace CSFFModFramework.Motion;

/// <summary>
/// One hidden object that re-bends every moving card a few times a second. A rebuild per frame
/// would be smoother but costs a canvas rebatch per frame for every moving card; at the default
/// 20 per second a breath that moves 3 pixels still reads as smooth. Runs on unscaled time
/// because CSFF sets <c>Time.timeScale = 0</c> while it waits for the player.
/// </summary>
internal sealed class CardMotionDriver : MonoBehaviour
{
    public static float TicksPerSecond = 20f;

    private static CardMotionDriver _instance;
    private static readonly HashSet<CardMotionEffect> Active = new HashSet<CardMotionEffect>();
    private static readonly List<CardMotionEffect> Scratch = new List<CardMotionEffect>(64);
    private float _next;

    internal static int ActiveCount => Active.Count;

    internal static void Register(CardMotionEffect e)
    {
        if (e == null || !Active.Add(e)) return;
        EnsureInstance();
    }

    internal static void Unregister(CardMotionEffect e)
    {
        if (e != null) Active.Remove(e);
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        var go = new GameObject("CSFFModFramework.CardMotionDriver") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CardMotionDriver>();
    }

    private void Update()
    {
        if (Active.Count == 0) return;
        float now = Time.unscaledTime;
        if (now < _next) return;
        _next = now + 1f / Mathf.Clamp(TicksPerSecond, 5f, 60f);

        Scratch.Clear();
        Scratch.AddRange(Active);
        foreach (var e in Scratch)
        {
            // A destroyed effect (its card visual went back to the pool and was destroyed) drops out.
            if (e == null) { Active.Remove(e); continue; }
            e.Tick();
        }
    }
}
