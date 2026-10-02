using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;

namespace Strata.Game.Entities;

/// <summary>
/// Ribbon trails (P3-11): a projectile with a <c>trail</c> colour keeps its last 12 positions (sampled at 60 Hz), drawn
/// as a camera-facing strip that tapers from <c>trailWidth</c> at the head to nothing and fades over <c>trailTime</c>.
/// All trails share one additive ImmediateMesh; a trail outlives its shot by <c>trailTime</c>; point buffers are
/// pooled. Very long segments (a bullet in one tick) are capped so a ribbon never stretches across the sky.
/// </summary>
public sealed partial class ProjectileRenderer
{
    private const int TrailPoints = 12;
    private const float TrailStep = 1f / 60f;
    private const float MaxSegment = 1.5f;

    private sealed class Trail
    {
        public readonly Vector3[] Points = new Vector3[TrailPoints];
        public readonly float[] Times = new float[TrailPoints];
        public int Count, Head;
        public Color Color;
        public float Width, Life;

        public void Reset(Color color, float width, float life)
        {
            Count = Head = 0;
            Color = color;
            Width = width;
            Life = life;
        }

        public void Add(Vector3 at, float time)
        {
            Head = (Head + 1) % TrailPoints;
            Points[Head] = at;
            Times[Head] = time;
            if (Count < TrailPoints) Count++;
        }

        /// <summary>The i-th newest point (0 = the head).</summary>
        public int Index(int i) => (Head - i + TrailPoints) % TrailPoints;
    }

    private readonly Dictionary<long, Trail> _trails = new();
    private readonly List<Trail> _dying = new();
    private readonly Stack<Trail> _spareTrails = new();
    private ImmediateMesh? _ribbons;

    /// <summary>Trails drawn this frame (probe <c>projectiles.trails</c>).</summary>
    public int TrailsDrawn { get; private set; }

    private void Sample(Projectile p, Vector3 at)
    {
        if (p.Def.Trail == null) return;
        if (!_trails.TryGetValue(p.Id, out var trail))
        {
            trail = _spareTrails.Count > 0 ? _spareTrails.Pop() : new Trail();
            trail.Reset(Color.FromHtml(p.Def.Trail), p.Def.TrailWidth, p.Def.TrailTime);
            _trails[p.Id] = trail;
        }
        if (trail.Count == 0 || _clock - trail.Times[trail.Head] >= TrailStep) trail.Add(at, _clock);
        else trail.Points[trail.Head] = at; // the head follows the shot between samples
    }

    /// <summary>The shot ended: its trail fades out where it is.</summary>
    private void EndTrail(long id)
    {
        if (_trails.Remove(id, out var trail)) _dying.Add(trail);
    }

    private void DrawTrails(float dt)
    {
        TrailsDrawn = 0;
        if (_ribbons == null)
        {
            _ribbons = new ImmediateMesh();
            AddChild(new MeshInstance3D
            {
                Mesh = _ribbons, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                },
            });
        }
        _ribbons.ClearSurfaces();
        for (int i = _dying.Count - 1; i >= 0; i--)
        {
            var t = _dying[i];
            if (t.Count == 0 || _clock - t.Times[t.Head] > t.Life)
            {
                _dying.RemoveAt(i);
                _spareTrails.Push(t);
            }
        }
        if (_trails.Count == 0 && _dying.Count == 0) return;
        var eye = GetViewport()?.GetCamera3D()?.GlobalPosition ?? Vector3.Zero;
        bool open = false;
        foreach (var t in _trails.Values) Ribbon(t, eye, ref open);
        foreach (var t in _dying) Ribbon(t, eye, ref open);
        if (open) _ribbons.SurfaceEnd();
    }

    /// <summary>Adds one trail's strip: a quad per segment, wide and bright at the head, thin and clear at the tail.</summary>
    private void Ribbon(Trail t, Vector3 eye, ref bool open)
    {
        if (t.Count < 2) return;
        bool drawn = false;
        for (int i = 0; i < t.Count - 1; i++)
        {
            int a = t.Index(i), b = t.Index(i + 1);
            float ageA = (_clock - t.Times[a]) / t.Life, ageB = (_clock - t.Times[b]) / t.Life;
            if (ageA >= 1f) break;
            var pa = t.Points[a];
            var pb = t.Points[b];
            var seg = pb - pa;
            if (seg.LengthSquared() < 1e-6f) continue;
            if (seg.Length() > MaxSegment) pb = pa + seg.Normalized() * MaxSegment;
            float ka = 1f - (float)i / (t.Count - 1), kb = 1f - (float)(i + 1) / (t.Count - 1);
            ka *= 1f - ageA;
            kb *= 1f - Mathf.Min(ageB, 1f);
            var side = seg.Cross(eye - pa).Normalized();
            var wa = side * t.Width * 0.5f * ka;
            var wb = side * t.Width * 0.5f * kb;
            var ca = new Color(t.Color, ka);
            var cb = new Color(t.Color, kb);
            if (!open)
            {
                _ribbons!.SurfaceBegin(Mesh.PrimitiveType.Triangles);
                open = true;
            }
            Vertex(pa + wa, ca);
            Vertex(pa - wa, ca);
            Vertex(pb + wb, cb);
            Vertex(pb + wb, cb);
            Vertex(pa - wa, ca);
            Vertex(pb - wb, cb);
            drawn = true;
        }
        if (drawn) TrailsDrawn++;
    }

    private void Vertex(Vector3 at, Color color)
    {
        _ribbons!.SurfaceSetColor(color);
        _ribbons.SurfaceAddVertex(at);
    }
}
