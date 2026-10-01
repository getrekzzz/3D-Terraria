using System.Collections.Generic;
using Godot;
using Strata.Core.Common;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Game.Entities;

/// <summary>
/// Makes melee attacks readable: a fading blade arc that follows the sweep the melee system tests hits against
/// (from +arc/2 to −arc/2 around the facing, in the aim's plane, as far as the prefix-sized blade reaches), drawn only
/// once the wind-up is over and swept by the same eased strike window (<see cref="UseTiming.Sweep"/>), so the arc
/// passes a creature as it is hit; and a quick streak along the aim for thrusts in their strike window. Coloured by
/// the swing's trail colour when the item sets one, else white tinted by rarity.
/// </summary>
public sealed partial class SwingTrail : Node3D
{
    private sealed class Trail
    {
        public PlayerEntity Player = null!;
        public bool Thrust;
        public float Yaw, Pitch, HalfArc, Reach, Duration, StrikeStart, StrikeEnd;
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
        bool thrust = info.Has(ItemAction.Thrust) && !info.Has(ItemAction.Melee);
        var held = player.State.Inventory.Held;
        float size = held.Item == info.Item ? Prefixes.Size(held) : 1f;
        var clip = _game.ItemUse.Timing.Clip(thrust ? UseTiming.Thrust : UseTiming.Swing);
        for (int i = _trails.Count - 1; i >= 0; i--)
            if (_trails[i].Player == player) Recycle(i);
        var t = _pool.Count > 0 ? _pool.Pop() : new Trail();
        t.Player = player;
        t.Thrust = thrust;
        t.Yaw = Mathf.Atan2(facing.X, facing.Z);
        t.Pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.Y, -1f, 1f)), -1.1f, 1.1f);
        t.HalfArc = thrust ? 0f : (info.Swing?.Arc ?? 150f) * Mathf.Pi / 360f;
        t.Reach = (thrust ? info.ThrustParams!.Length : info.Swing?.Length ?? 3f) * size * Units.CellSize + (thrust ? 0.4f : 0.6f);
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
        var timing = _game.ItemUse.Timing;
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
            float sweep = timing.Sweep(t.Thrust ? UseTiming.Thrust : UseTiming.Swing, phase);
            if (phase < t.StrikeStart || sweep <= 0f) continue; // nothing to show through the wind-up
            if (!begun) _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
            begun = true;
            var c = t.Player == _local && _localBody != null
                ? _localBody.GetGlobalTransformInterpolated().Origin + new Vector3(0, PlayerEntity.HalfExtents.Y, 0)
                : new Vector3(t.Player.Center.X, t.Player.Center.Y, t.Player.Center.Z);
            var origin = c + new Vector3(0, 0.3f, 0);
            float fade = Fade(t);
            if (t.Thrust) DrawThrust(t, origin, sweep, fade);
            else DrawArc(t, origin, sweep, fade);
        }
        if (begun) _mesh.SurfaceEnd();
    }

    /// <summary>When the trail starts to fade (s): once the strike and its follow-through are over, as gameplay stops hitting.</summary>
    private static float FadeFrom(Trail t) => Mathf.Min(1f, t.StrikeEnd + UseTiming.FollowThrough) * t.Duration;

    /// <summary>When the trail is gone (s): faded over the rest of the use, at least a few frames.</summary>
    private static float End(Trail t) => FadeFrom(t) + Mathf.Max(0.08f, t.Duration - FadeFrom(t));

    private static float Fade(Trail t) => 1f - Mathf.SmoothStep(FadeFrom(t), End(t), t.Age);

    /// <summary>The arc: its head at the blade (where the melee system tests hits), a tail fading behind it.</summary>
    private void DrawArc(Trail t, Vector3 origin, float sweep, float fade)
    {
        float head = t.HalfArc - sweep * 2f * t.HalfArc;
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

    /// <summary>The thrust: a streak along the aim reaching out as the stab extends through its strike window.</summary>
    private void DrawThrust(Trail t, Vector3 origin, float extend, float fade)
    {
        float alpha = 0.6f * fade * Mathf.Min(1f, extend * 3f);
        var dir = Direction(t.Yaw, t.Pitch);
        var side = dir.Cross(Vector3.Up).Normalized() * 0.07f;
        var a = origin + dir * 0.4f;
        var b = origin + dir * (0.4f + t.Reach * extend);
        Quad(a - side, a + side, b + side * 0.2f, b - side * 0.2f,
            new Color(t.Color, 0), new Color(t.Color, 0), new Color(t.Color, alpha), new Color(t.Color, alpha));
    }

    /// <summary>A point on the sweep: flat distance <paramref name="r"/> at <paramref name="angle"/> from the facing yaw, on the aim plane.</summary>
    private static Vector3 Point(Trail t, Vector3 origin, float angle, float r)
    {
        float yaw = t.Yaw + angle;
        return origin + new Vector3(Mathf.Sin(yaw) * r, Mathf.Tan(t.Pitch) * r, Mathf.Cos(yaw) * r);
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
