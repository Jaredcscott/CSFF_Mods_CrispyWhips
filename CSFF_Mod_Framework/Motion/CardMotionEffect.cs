using UnityEngine.UI;

namespace CSFFModFramework.Motion;

/// <summary>
/// Moves a card's art by bending the UI mesh the Image draws, never by swapping sprites, so a
/// moving card costs no texture memory and no image file. Added to <c>CardGraphics.CardImage</c>
/// and <c>AboveTitleCardImage</c> by <see cref="CardMotionService"/>; does nothing while the sprite
/// it shows has no entry in <see cref="CardMotionLoader.BySprite"/>.
///
/// Two ways to bend, chosen per sprite:
/// <list type="bullet">
/// <item><b>Cut-out art</b> (trimmed to its subject, transparent round it): the mesh itself moves, so
/// the animal grows and leans within its own transparent margin.</item>
/// <item><b>Full painting</b> (the 512x768 card canvas, painted to its edges): the mesh stays on the card
/// and only the texture coordinates move, weighted to the middle columns where the figure stands, so
/// the picture never spills over the card frame and its side walls stay put.</item>
/// </list>
///
/// It re-reads the sprite on every tick, so it follows a pooled card visual onto its next card, an
/// NPC portrait written after Setup (<c>InGameNPC.RefreshPortrait</c>, or a mod's own portrait swap),
/// and a GIF taking the image over (GIF frames are not in the motion table, so the GIF plays unbent).
/// </summary>
public class CardMotionEffect : BaseMeshEffect
{
    private const float TwoPi = Mathf.PI * 2f;

    private Image _image;
    private Sprite _sprite;
    private CardMotionProfile _profile;
    private bool _inFrame;
    private float _phase;
    private readonly List<UIVertex> _verts = new List<UIVertex>(96);

    internal bool IsMoving => _profile != null && !_profile.IsStill;

    protected override void Awake()
    {
        base.Awake();
        _image = GetComponent<Image>();
        // A fixed per-object offset so a row of the same animal does not breathe in step.
        _phase = (GetInstanceID() & 0xFFFF) * 0.0137f;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Refresh();
    }

    protected override void OnDisable()
    {
        CardMotionDriver.Unregister(this);
        base.OnDisable();
    }

    /// <summary>Re-read the sprite on the image and pick its profile. Cheap: one dictionary lookup on change.</summary>
    internal void Refresh()
    {
        if (_image == null) _image = GetComponent<Image>();
        var s = _image != null ? _image.overrideSprite : null;   // overrideSprite reads the active sprite
        if (!ReferenceEquals(s, _sprite))
        {
            _sprite = s;
            var had = IsMoving;
            _profile = s != null && CardMotionLoader.BySprite.TryGetValue(s.name, out var p) ? p : null;
            _inFrame = s != null && IsFullCanvas(s.rect);
            if (had && !IsMoving && graphic != null) graphic.SetVerticesDirty(); // put the plain mesh back once
        }
        if (IsMoving && isActiveAndEnabled) CardMotionDriver.Register(this);
        else CardMotionDriver.Unregister(this);
    }

    // The card canvas every full painting keeps (CLAUDE.md, Sprites/Images): 512x768, or a 2:3 image at
    // least that big. Trimmed art is any other shape.
    private static bool IsFullCanvas(Rect r) =>
        r.height >= 760f && Mathf.Abs(r.width / r.height - 2f / 3f) < 0.02f;

    internal void Tick()
    {
        Refresh();
        if (IsMoving && graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || !IsMoving || vh.currentVertCount == 0) return;
        var p = _profile;
        float t = Time.unscaledTime + _phase;

        _verts.Clear();
        var v = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++) { vh.PopulateUIVertex(ref v, i); _verts.Add(v); }

        Vector2 pMin = new Vector2(float.MaxValue, float.MaxValue), pMax = new Vector2(float.MinValue, float.MinValue);
        Vector2 uMin = pMin, uMax = pMax;
        foreach (var x in _verts)
        {
            pMin = Vector2.Min(pMin, x.position); pMax = Vector2.Max(pMax, x.position);
            Vector2 uv = x.uv0;
            uMin = Vector2.Min(uMin, uv); uMax = Vector2.Max(uMax, uv);
        }
        var b = new Bounds2(pMin, pMax - pMin, uMin, uMax);
        if (b.Size.x <= 0f || b.Size.y <= 0f) return;

        var w = new Wave(p, t);
        if (p.NeedsGrid && vh.currentVertCount == 4)
        {
            // A plain quad cannot bend: rebuild it as a grid spanning the same rectangle and UVs.
            // A full painting needs more columns, since its bend is weighted across the width.
            int cols = _inFrame ? 8 : 4, rows = _inFrame ? 8 : 6;
            var c = _verts[0].color;
            vh.Clear();
            for (int r = 0; r <= rows; r++)
            {
                float fy = r / (float)rows;
                for (int col = 0; col <= cols; col++)
                {
                    float fx = col / (float)cols;
                    var g = UIVertex.simpleVert;
                    g.color = c;
                    g.position = new Vector3(Mathf.Lerp(pMin.x, pMax.x, fx), Mathf.Lerp(pMin.y, pMax.y, fy), 0f);
                    g.uv0 = new Vector2(Mathf.Lerp(uMin.x, uMax.x, fx), Mathf.Lerp(uMin.y, uMax.y, fy));
                    Deform(ref g, p, w, b, _inFrame);
                    vh.AddVert(g);
                }
            }
            int stride = cols + 1;
            for (int r = 0; r < rows; r++)
            for (int col = 0; col < cols; col++)
            {
                int i0 = r * stride + col;
                vh.AddTriangle(i0, i0 + stride, i0 + stride + 1);
                vh.AddTriangle(i0 + stride + 1, i0 + 1, i0);
            }
            return;
        }

        for (int i = 0; i < _verts.Count; i++)
        {
            var x = _verts[i];
            Deform(ref x, p, w, b, _inFrame);
            vh.SetUIVertex(x, i);
        }
    }

    private readonly struct Bounds2
    {
        public readonly Vector2 Min, Size, UMin, UMax;
        public Bounds2(Vector2 min, Vector2 size, Vector2 uMin, Vector2 uMax) { Min = min; Size = size; UMin = uMin; UMax = uMax; }
    }

    private readonly struct Wave
    {
        public readonly float Breath;   // 0..1, eased in and out
        public readonly float Lean;     // -1..1
        public readonly float Zoom;     // 0..1
        public readonly float PanX, PanY;

        public Wave(CardMotionProfile p, float t)
        {
            float period = Mathf.Max(0.5f, p.Period);
            Breath = 0.5f - 0.5f * Mathf.Cos(TwoPi * t / period);
            Lean = Mathf.Sin(TwoPi * t / (period * 1.37f));
            Zoom = 0.5f - 0.5f * Mathf.Cos(TwoPi * t / period);
            PanX = Mathf.Sin(TwoPi * t / (period * 1.71f));
            PanY = Mathf.Cos(TwoPi * t / (period * 2.23f));
        }
    }

    private static void Deform(ref UIVertex v, CardMotionProfile p, in Wave w, in Bounds2 b, bool inFrame)
    {
        var pos = v.position;
        Vector2 uv = v.uv0;
        var uSize = b.UMax - b.UMin;
        float fx = Mathf.Clamp01((pos.x - b.Min.x) / b.Size.x);
        float h = Mathf.Clamp01((pos.y - b.Min.y) / b.Size.y);   // 0 at the base, 1 at the top
        float chest = 4f * h * (1f - h);                          // widest at mid height, nothing at base and top
        // On a full painting only the middle (where the figure stands) moves; the side walls stay.
        float centre = inFrame ? Mathf.Exp(-Sq((fx - 0.5f) / 0.26f)) : 1f;
        float breath = p.Breathe * w.Breath * centre;
        float lean = p.Sway * w.Lean * h * h * centre;

        if (!inFrame)
        {
            float cx = b.Min.x + b.Size.x * 0.5f;
            if (breath > 0f)
            {
                pos.y = b.Min.y + (pos.y - b.Min.y) * (1f + breath);
                pos.x = cx + (pos.x - cx) * (1f + breath * 0.45f * chest);
            }
            pos.x += b.Size.x * lean;
            v.position = pos;
        }
        else if (breath > 0f || lean != 0f)
        {
            // The same bend, done to the texture instead of the mesh: showing a point of the art higher
            // up means sampling lower in the texture there.
            float ucx = (b.UMin.x + b.UMax.x) * 0.5f;
            uv.y = b.UMin.y + (uv.y - b.UMin.y) / (1f + breath);
            uv.x = ucx + (uv.x - ucx) / (1f + breath * 0.45f * chest);
            uv.x -= uSize.x * lean;
        }

        if (p.Drift > 0f)
        {
            // Zoom in by up to Drift and pan inside the margin the zoom opens, so the edges never show.
            float z = 1f + p.Drift * w.Zoom;
            var uc = (b.UMin + b.UMax) * 0.5f;
            float margin = (1f - 1f / z) * 0.5f * 0.85f;
            uv = uc + (uv - uc) / z + new Vector2(w.PanX * margin * uSize.x, w.PanY * margin * uSize.y);
        }

        uv.x = Mathf.Clamp(uv.x, b.UMin.x, b.UMax.x);
        uv.y = Mathf.Clamp(uv.y, b.UMin.y, b.UMax.y);
        v.uv0 = uv;
    }

    private static float Sq(float x) => x * x;
}
