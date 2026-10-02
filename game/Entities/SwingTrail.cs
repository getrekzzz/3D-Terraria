using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Game.Entities;

/// <summary>
/// Makes melee attacks readable: a fading ribbon behind the blade that follows the sweep the melee system tests hits
/// against (the same <see cref="SwingCurve.Blade"/> angle in the same <see cref="SwingPlane"/>, out to the
/// prefix-sized reach, so it widens with size prefixes), drawn from the end of the wind-up through the follow-through,
/// so the ribbon's head passes a creature as it is hit; and for thrusts a 0.2 m streak along the aim that extends with
/// <see cref="SwingCurve.Extension"/>, a sparkle at its tip. Coloured by the swing's trail colour when the item sets
/// one, else white tinted by rarity.
/// </summary>
public sealed partial class SwingTrail : Node3D
{
    private sealed class Trail
    {
        public PlayerEntity Player = null!;
        public bool Thrust;
        public float Yaw, Pitch, Arc, Reach, Duration, StrikeStart, StrikeEnd;
        public SwingCurve Curve = null!;
        public Color Color;
        public float Age;
    }

    private readonly List<Trail> _trails = new();
    private readonly Stack<Trail> _pool = new();
    private readonly ImmediateMesh _mesh = new();
    private GameWorld _game = null!;
    private PlayerEntity? _local;
    private Node3D? _localBody;

    /// <summary>
    /// <paramref name="local"/>'s trails follow <paramref name="localBody"/>'s interpolated position (the body on
    /// screen moves between ticks), others' their simulated centre.
    /// </summary>
    public void Initialize(GameWorld game, PlayerEntity? local = null, Node3D? localBody = null)
    {
        Name = "SwingTrail";
        TopLevel = true;
        _game = game;
        _local = local;
        _localBody = localBody;
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
        // As the melee system: the arc is centred on where the player looks (the body faces it) and pitched by the aim.
        var aim = player.Aim;
        var facing = player.Facing;
        bool thrust = info.Has(ItemAction.Thrust); // as the melee system
        var held = player.State.Inventory.Held;
        float size = held.Item == info.Item ? Prefixes.Size(held) : 1f;
        string clipName = thrust ? _game.ItemUse.Timing.ThrustClip(info) : UseTiming.Swing;
        var clip = _game.ItemUse.Timing.Clip(clipName);
        for (int i = _trails.Count - 1; i >= 0; i--)
            if (_trails[i].Player == player) Recycle(i);
        var t = _pool.Count > 0 ? _pool.Pop() : new Trail();
        t.Player = player;
        t.Thrust = thrust;
        t.Yaw = Mathf.Atan2(facing.X, facing.Z);
        t.Pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.Y, -1f, 1f)), -1.1f, 1.1f);
        t.Arc = info.Swing?.Arc ?? 150f;
        t.Reach = thrust ? SwingPlane.ThrustReach(info.ThrustParams!.Length, size) : SwingPlane.SwingReach(info.Swing?.Length ?? 3f, size);
        t.Curve = thrust ? SwingCurve.Extension(clip) : SwingCurve.Blade(clip);
        t.Duration = Mathf.Max(2, player.UseAnimationMax) / (float)Units.TicksPerSecond; // the animation after speed bonuses
        t.StrikeStart = clip.StrikeStart ?? 0f;
        t.StrikeEnd = clip.StrikeEnd ?? 1f;
        t.Color = TrailColor(info);
        t.Age = 0f;
        _trails.Add(t);
    }

    /// <summary>The swing's own trail colour (MeleeSwingParams.TrailColor, hex), else white tinted by the item's rarity.</summary>
    private static Color TrailColor(ItemUseInfo info)
    {
        if (info.Swing?.TrailColor is { } hex && Color.HtmlIsValid(hex)) return Color.FromHtml(hex);
        return new Color(1f, 1f, 1f).Lerp(UI.UiTheme.Rarity(info.Item.Rarity), 0.35f);
    }

    private void Recycle(int index)
    {
        var t = _trails[index];
        _trails.RemoveAt(index);
        t.Player = null!;
        _pool.Push(t);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.swing_trail");
        _mesh.ClearSurfaces();
        for (int i = _trails.Count - 1; i >= 0; i--)
        {
            var t = _trails[i];
            t.Age += (float)delta;
            if (t.Age > End(t) || !t.Player.Alive) Recycle(i);
        }
        if (_trails.Count == 0) return;
        bool begun = false; // (a surface with no vertices is an error: begun only once something is drawn)
        foreach (var t in _trails)
        {
            float phase = t.Age / t.Duration;
            if (phase < t.StrikeStart) continue; // nothing to show through the wind-up
            if (t.Thrust && t.Curve.Evaluate(phase) <= 0.01f) continue;
            if (!begun) _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            begun = true;
            var c = t.Player == _local && _localBody != null
                ? _localBody.GetGlobalTransformInterpolated().Origin + new Vector3(0, PlayerEntity.HalfExtents.Y, 0)
                : new Vector3(t.Player.Center.X, t.Player.Center.Y, t.Player.Center.Z);
            float fade = Fade(t);
            if (t.Thrust) DrawThrust(t, c + new Vector3(0, 0.3f, 0), t.Curve.Evaluate(phase), fade);
            else DrawArc(t, new SwingPlane(new System.Numerics.Vector3(c.X, c.Y, c.Z), t.Yaw, t.Reach, t.Arc), phase, fade);
        }
        if (begun) _mesh.SurfaceEnd();
    }

    /// <summary>When the trail starts to fade (s): once the strike and its follow-through are over, as gameplay stops hitting.</summary>
    private static float FadeFrom(Trail t) => Mathf.Min(1f, t.StrikeEnd + UseTiming.FollowThrough) * t.Duration;

    /// <summary>When the trail is gone (s): faded over the rest of the use, at least a few frames.</summary>
    private static float End(Trail t) => FadeFrom(t) + Mathf.Max(0.08f, t.Duration - FadeFrom(t));

    private static float Fade(Trail t) => 1f - Mathf.SmoothStep(FadeFrom(t), End(t), t.Age);

    /// <summary>How far back (use phase) the ribbon's tail trails the blade.</summary>
    private const float TrailLag = 0.14f;

    /// <summary>The ribbon: its head at the blade (where the melee system tests hits), a tail fading behind it.</summary>
    private void DrawArc(Trail t, SwingPlane plane, float phase, float fade)
    {
        float end = Mathf.Min(1f, t.StrikeEnd + UseTiming.FollowThrough);
        float head = t.Curve.Evaluate(Mathf.Min(phase, end));
        float tail = t.Curve.Evaluate(Mathf.Clamp(phase - TrailLag, t.StrikeStart, end));
        const int steps = 14;
        float inner = t.Reach * 0.35f;
        for (int k = 0; k < steps; k++)
        {
            float a0 = Mathf.Lerp(head, tail, k / (float)steps), a1 = Mathf.Lerp(head, tail, (k + 1) / (float)steps);
            float alpha0 = (1f - k / (float)steps) * 0.55f * fade, alpha1 = (1f - (k + 1) / (float)steps) * 0.55f * fade;
            Quad(Gd(plane.Point(a0, inner)), Gd(plane.Point(a0, t.Reach)), Gd(plane.Point(a1, t.Reach)), Gd(plane.Point(a1, inner)),
                new Color(t.Color, alpha0 * 0.2f), new Color(t.Color, alpha0), new Color(t.Color, alpha1), new Color(t.Color, alpha1 * 0.2f));
        }
    }

    private static Vector3 Gd(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>The thrust: a 0.2 m streak along the aim reaching out as far as the stab has extended, a sparkle at its tip.</summary>
    private void DrawThrust(Trail t, Vector3 origin, float extend, float fade)
    {
        float alpha = 0.6f * fade * Mathf.Min(1f, extend * 3f);
        var dir = Direction(t.Yaw, t.Pitch);
        var side = dir.Cross(Vector3.Up).Normalized() * 0.1f;
        var a = origin + dir * SwingPlane.ThrustArm;
        var b = origin + dir * (SwingPlane.ThrustArm + (t.Reach - SwingPlane.ThrustArm) * extend);
        Quad(a - side, a + side, b + side * 0.3f, b - side * 0.3f,
            new Color(t.Color, 0), new Color(t.Color, 0), new Color(t.Color, alpha), new Color(t.Color, alpha));
        // The tip sparkle: two crossed diamonds, brightest at full extension.
        float s = 0.14f * extend;
        var up = side.Cross(dir).Normalized() * s;
        var across = side.Normalized() * s;
        var bright = new Color(t.Color.Lerp(Colors.White, 0.6f), alpha * extend);
        var clear = new Color(t.Color, 0f);
        Quad(b - across, b + up, b + across, b - up, clear, bright, clear, bright);
        Quad(b - dir * s, b + up, b + dir * s, b - up, clear, bright, clear, bright);
    }

    private static Vector3 Direction(float yaw, float pitch) =>
        new(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
    {
        Vertex(a, ca);
        Vertex(b, cb);
        Vertex(c, cc);
        Vertex(a, ca);
        Vertex(c, cc);
        Vertex(d, cd);
    }

    private void Vertex(Vector3 p, Color color)
    {
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(p);
    }
}
