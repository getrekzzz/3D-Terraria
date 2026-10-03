using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;
using Strata.Game.Audio;
using Strata.Game.Player;
using Strata.Game.Rendering;

namespace Strata.Game.Entities;

/// <summary>
/// Whip lashes (P3-15): each lash in the air is drawn as a six-sided tube tapering from 3 cm at the hand to 8 mm at the
/// tip along WhipGeometry, sampled at the render frame's sub-tick time so it moves smoothly, in the whip's colour.
/// The local player's lash starts at the held whip's grip in the active view. A swish plays as it winds up; at the
/// crack the tip throws sparks and snaps.
/// </summary>
public sealed partial class WhipRenderer : Node3D
{
    private const int Sides = 6;
    private const float RootWidth = 0.03f, TipWidth = 0.008f;
    private const float CrackT = 0.57f;

    private GameWorld _game = null!;
    private EffectKit _kit = null!;
    private readonly ImmediateMesh _mesh = new();
    private readonly System.Numerics.Vector3[] _points = new System.Numerics.Vector3[64];
    private readonly System.Collections.Generic.Dictionary<WhipSystem.Lash, bool> _cracked = new();
    private readonly Vector3[] _ringA = new Vector3[Sides], _ringB = new Vector3[Sides];

    /// <summary>Lashes drawn this frame and cracks shown since start (probes <c>whips.drawn</c>, <c>whips.cracks</c>).</summary>
    public int Drawn { get; private set; }

    public int Cracks { get; private set; }

    public void Initialize(GameWorld game, EffectKit kit)
    {
        _game = game;
        _kit = kit;
        Name = "Whips";
        AddChild(new MeshInstance3D
        {
            Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.7f },
        });
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.whips");
        _mesh.ClearSurfaces();
        Drawn = 0;
        var lashes = _game.Whips.Active;
        if (lashes.Count == 0)
        {
            _cracked.Clear();
            return;
        }
        float frac = (float)Engine.GetPhysicsInterpolationFraction();
        bool open = false;
        foreach (var lash in lashes)
        {
            var w = lash.Weapon.Whip!;
            float t = Mathf.Clamp((lash.Age + frac) / lash.Duration, 0f, 1f);
            var p = lash.Owner;
            var forward = p.Aim.LengthSquared() > 1e-6f ? System.Numerics.Vector3.Normalize(p.Aim) : System.Numerics.Vector3.UnitZ;
            var side = System.Numerics.Vector3.Cross(forward, System.Numerics.Vector3.UnitY);
            var up = side.LengthSquared() > 1e-6f ? System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(System.Numerics.Vector3.Normalize(side), forward)) : System.Numerics.Vector3.UnitY;
            var hand = ShotGeometry.Origin(_game.Session.World, p);
            int n = Mathf.Clamp(w.Segments, 4, _points.Length);
            var span = new System.Span<System.Numerics.Vector3>(_points, 0, n);
            WhipGeometry.Points(t, hand, forward, up, w.Length * Strata.Core.Common.Units.CellSize, span);
            // The local player's lash leaves the drawn grip.
            var shift = Vector3.Zero;
            if (p == _game.LocalPlayer && HeldSockets.TryNamed("grip", out var grip)) shift = grip - ToGodot(span[0]);
            if (!open)
            {
                _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
                open = true;
            }
            Tube(span, shift, Color.FromHtml(w.Color));
            Drawn++;
            if (!_cracked.ContainsKey(lash))
            {
                // First seen (at a low frame rate maybe a tick or two in): swish unless the crack is already past. The
                // local player's swing already sounded through its use style (P3-27 whip_swing).
                _cracked[lash] = false;
                if (t < CrackT && p != _game.LocalPlayer) Sfx.PlayAt("whip_swing", ToGodot(hand) + shift, -4f);
            }
            if (t >= CrackT && _cracked.TryGetValue(lash, out bool done) && !done)
            {
                _cracked[lash] = true;
                var tip = ToGodot(span[n - 1]) + shift;
                _kit.Burst(EffectRecipe.Sparks, tip, new Color(1f, 0.92f, 0.7f), 8, 3f);
                Sfx.PlayAt("whip_crack", tip, 0f, 0.08f);
                Cracks++;
            }
        }
        if (open) _mesh.SurfaceEnd();
        if (_cracked.Count > lashes.Count)
            foreach (var gone in System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(_cracked.Keys, l => !System.Linq.Enumerable.Contains(lashes, l)))) _cracked.Remove(gone);
    }

    /// <summary>Adds a tapered six-sided tube through the points.</summary>
    private void Tube(System.ReadOnlySpan<System.Numerics.Vector3> points, Vector3 shift, Color color)
    {
        _mesh.SurfaceSetColor(color);
        int n = points.Length;
        var prevRing = _ringA;
        var ring = _ringB;
        for (int i = 0; i < n; i++)
        {
            var c = ToGodot(points[i]) + shift;
            var dir = i < n - 1 ? ToGodot(points[i + 1]) - ToGodot(points[i]) : ToGodot(points[i]) - ToGodot(points[i - 1]);
            if (dir.LengthSquared() < 1e-8f) dir = Vector3.Forward;
            dir = dir.Normalized();
            var a = dir.Cross(Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
            var b = dir.Cross(a).Normalized();
            float r = Mathf.Lerp(RootWidth, TipWidth, (float)i / (n - 1)) * 0.5f;
            for (int k = 0; k < Sides; k++)
            {
                float ang = k * Mathf.Tau / Sides;
                ring[k] = c + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * r;
            }
            if (i > 0)
                for (int k = 0; k < Sides; k++)
                {
                    int k2 = (k + 1) % Sides;
                    ChainStrip.Quad(_mesh, prevRing[k], prevRing[k2], ring[k2], ring[k]);
                }
            (prevRing, ring) = (ring, prevRing);
        }
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
