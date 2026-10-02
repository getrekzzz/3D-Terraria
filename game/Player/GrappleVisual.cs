using Godot;
using Strata.Core.Gameplay.Player;
using Strata.Game.World;

namespace Strata.Game.Player;

/// <summary>
/// Draws the grappling hook: a chain of links from the hand to the hook head, and the hook's claw. The Core hook
/// latches to a voxel cell, but natural terrain is drawn smoothed up to a quarter metre off the cell grid, so the claw
/// is moved onto the rendered surface: one physics ray across the struck face when a throw starts and again when it
/// latches finds the collider (built from the same mesh), and the head eases onto it over its last metre of flight.
/// </summary>
public sealed partial class GrappleVisual : Node3D
{
    /// <summary>How far (m) out of and into the struck face the snap ray reaches.</summary>
    private const float SnapReach = 0.6f;

    /// <summary>A snap point further than this (m) from the voxel hit is another surface; the voxel point is kept.</summary>
    private const float MaxSnap = 0.5f;

    /// <summary>The head moves onto the snapped surface over this last stretch (m) of its flight to the anchor.</summary>
    private const float SnapBlend = 1f;

    private const uint SnapMask = ColliderManager.TerrainLayer | ColliderManager.PlatformLayer | ColliderManager.ObjectLayer;

    private PlayerController _player = null!;
    private readonly ImmediateMesh _mesh = new();
    private StandardMaterial3D _material = null!;
    private PhysicsRayQueryParameters3D? _query;
    private HookState _lastState;
    private Vector3 _snap; // rendered surface minus the voxel hit point, for the current throw

    /// <summary>How far (m) the claw was moved onto the rendered surface, or -1 when no surface was found (probe).</summary>
    public float SnapDistance { get; private set; } = -1f;

    public void Initialize(PlayerController player)
    {
        _player = player;
        Name = "GrappleVisual";
        TopLevel = true;
        _material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Metallic = 0.8f, Roughness = 0.35f };
        AddChild(new MeshInstance3D { Mesh = _mesh, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.On });
    }

    /// <summary>Snaps each new throw at a cell, and each new latch, onto the rendered surface (physics queries run here).</summary>
    public override void _PhysicsProcess(double delta)
    {
        var hook = _player.Entity.Hook;
        var state = hook.State;
        if (state != _lastState && state is HookState.None or HookState.Flying or HookState.Attached)
        {
            bool found = state != HookState.None && hook.Hit && Snap(hook, out _snap);
            if (!found) _snap = Vector3.Zero;
            SnapDistance = found ? _snap.Length() : -1f;
        }
        _lastState = state;
    }

    /// <summary>
    /// Finds the offset from the voxel hit point to the rendered surface: one ray from <see cref="SnapReach"/> outside
    /// the struck face to as far inside, against terrain, platform and object colliders. False when it misses (thin
    /// geometry, an object without a collider such as a canopy, a chunk without a body yet), when the throw started
    /// inside its cell, or when what it found is too far off to be the struck surface.
    /// </summary>
    private bool Snap(GrappleHook hook, out Vector3 offset)
    {
        offset = Vector3.Zero;
        var n = new Vector3(hook.AnchorNormal.X, hook.AnchorNormal.Y, hook.AnchorNormal.Z);
        var space = GetWorld3D()?.DirectSpaceState;
        if (n == Vector3.Zero || space == null) return false;
        var target = PlayerController.ToGodot(hook.Target);
        // Front faces only: in a gap narrower than the reach the ray starts inside the opposite wall, whose inner side
        // (a back face, the terrain collides both ways) must not catch the claw.
        _query ??= new PhysicsRayQueryParameters3D { CollisionMask = SnapMask, HitBackFaces = false };
        _query.From = target + n * SnapReach;
        _query.To = target - n * SnapReach;
        var hit = space.IntersectRay(_query);
        if (hit.Count == 0) return false;
        offset = (Vector3)hit["position"] - target;
        return offset.LengthSquared() <= MaxSnap * MaxSnap;
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.grapple");
        _mesh.ClearSurfaces();
        var hook = _player.Entity.Hook;
        if (hook.State == HookState.None) return;
        var hand = _player.DrawnHandPosition;
        var head = _player.HookHead;
        if (_snap != Vector3.Zero)
        {
            // Fully on the surface while latched; eased on over the end of the flight and off as it comes back.
            float away = head.DistanceTo(PlayerController.ToGodot(hook.Target));
            head += _snap * Mathf.Clamp(1f - away / SnapBlend, 0f, 1f);
        }
        var along = head - hand;
        float length = along.Length();
        if (length < 0.05f) return;
        var dir = along / length;
        var color = _player.HookColor;
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        var (side, up) = Rendering.ChainStrip.Links(_mesh, hand, head, color); // chain links: alternating flat diamonds
        // The claw: three prongs around the head.
        _mesh.SurfaceSetColor(color.Darkened(0.2f));
        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.Tau / 3f;
            var spoke = (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.12f;
            Rendering.ChainStrip.Quad(_mesh, head - dir * 0.05f, head + spoke * 0.3f + dir * 0.05f, head + spoke - dir * 0.06f, head + spoke * 0.3f - dir * 0.02f);
        }
        _mesh.SurfaceEnd();
    }
}
