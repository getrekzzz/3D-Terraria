using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Game.Entities;

/// <summary>
/// Makes melee attacks readable: a fading blade arc that follows the sweep the melee system tests hits against
/// (from +arc/2 to −arc/2 around the aim, in the aim's plane) and a quick streak along the aim for thrusts.
/// </summary>
public sealed partial class SwingTrail : Node3D
{
    private sealed class Trail
    {
        public required PlayerEntity Player;
        public required bool Thrust;
        public required float Yaw, Pitch, HalfArc, Reach, Duration;
        public required Color Color;
        public float Age;
    }

    private readonly List<Trail> _trails = new();
    private readonly ImmediateMesh _mesh = new();

    public void Initialize(GameWorld game)
    {
        Name = "SwingTrail";
        TopLevel = true;
        AddChild(new MeshInstance3D
        {
            Mesh = _mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                NoDepthTest = false,
            },
        });
        game.ItemUse.Swung += Start;
    }

    private void Start(PlayerEntity player, ItemUseInfo info)
    {
        if (info.IsTool) return; // pickaxes and axes show the block cursor instead
        var aim = player.Aim;
        bool thrust = info.Has(ItemAction.Thrust) && !info.Has(ItemAction.Melee);
        var rarity = UI.UiTheme.Rarity(info.Item.Rarity);
        _trails.RemoveAll(t => t.Player == player);
        _trails.Add(new Trail
        {
            Player = player,
            Thrust = thrust,
            Yaw = Mathf.Atan2(aim.X, aim.Z),
            Pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.Y, -1f, 1f)), -1.1f, 1.1f),
            HalfArc = thrust ? 0f : (info.Swing?.Arc ?? 150f) * Mathf.Pi / 360f,
            Reach = (thrust ? info.ThrustParams!.Length : info.Swing?.Length ?? 3f) * Units.CellSize + (thrust ? 0.4f : 0.6f),
            Duration = Mathf.Max(0.12f, info.Item.UseAnimation / 60f),
            Color = new Color(1f, 1f, 1f).Lerp(rarity, 0.35f),
        });
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.swing_trail");
        _mesh.ClearSurfaces();
        if (_trails.Count == 0) return;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        for (int i = _trails.Count - 1; i >= 0; i--)
        {
            var t = _trails[i];
            t.Age += (float)delta;
            if (t.Age > t.Duration * 1.3f || !t.Player.Alive)
            {
                _trails.RemoveAt(i);
                continue;
            }
            var c = t.Player.Center;
            var origin = new Vector3(c.X, c.Y + 0.3f, c.Z);
            if (t.Thrust) DrawThrust(t, origin);
            else DrawArc(t, origin);
        }
        _mesh.SurfaceEnd();
    }

    private void DrawArc(Trail t, Vector3 origin)
    {
        float progress = Mathf.Clamp(t.Age / t.Duration, 0f, 1f);
        float fade = t.Age > t.Duration ? 1f - (t.Age - t.Duration) / (t.Duration * 0.3f) : 1f;
        float head = t.HalfArc - progress * 2f * t.HalfArc;
        float tail = Mathf.Min(t.HalfArc, head + 1.4f);
        const int steps = 14;
        float inner = t.Reach * 0.35f;
        for (int k = 0; k < steps; k++)
        {
            float a0 = Mathf.Lerp(head, tail, k / (float)steps), a1 = Mathf.Lerp(head, tail, (k + 1) / (float)steps);
            float alpha0 = (1f - k / (float)steps) * 0.55f * fade, alpha1 = (1f - (k + 1) / (float)steps) * 0.55f * fade;
            Quad(Point(t, origin, a0, inner), Point(t, origin, a0, t.Reach), Point(t, origin, a1, t.Reach), Point(t, origin, a1, inner),
                new Color(t.Color, alpha0 * 0.2f), new Color(t.Color, alpha0), new Color(t.Color, alpha1), new Color(t.Color, alpha1 * 0.2f));
        }
    }

    private void DrawThrust(Trail t, Vector3 origin)
    {
        float progress = Mathf.Clamp(t.Age / t.Duration, 0f, 1f);
        float alpha = Mathf.Sin(progress * Mathf.Pi) * 0.6f;
        var dir = Direction(t.Yaw, t.Pitch);
        var side = dir.Cross(Vector3.Up).Normalized() * 0.07f;
        var a = origin + dir * 0.4f;
        var b = origin + dir * (0.4f + t.Reach * Mathf.Min(1f, progress * 2f));
        Quad(a - side, a + side, b + side * 0.2f, b - side * 0.2f,
            new Color(t.Color, 0), new Color(t.Color, 0), new Color(t.Color, alpha), new Color(t.Color, alpha));
    }

    /// <summary>A point on the sweep: flat distance <paramref name="r"/> at <paramref name="angle"/> from the aim yaw, on the aim plane.</summary>
    private static Vector3 Point(Trail t, Vector3 origin, float angle, float r)
    {
        float yaw = t.Yaw + angle;
        return origin + new Vector3(Mathf.Sin(yaw) * r, Mathf.Tan(t.Pitch) * r, Mathf.Cos(yaw) * r);
    }

    private static Vector3 Direction(float yaw, float pitch) =>
        new(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
    {
        foreach (var (p, col) in new[] { (a, ca), (b, cb), (c, cc), (a, ca), (c, cc), (d, cd) })
        {
            _mesh.SurfaceSetColor(col);
            _mesh.SurfaceAddVertex(p);
        }
    }
}
