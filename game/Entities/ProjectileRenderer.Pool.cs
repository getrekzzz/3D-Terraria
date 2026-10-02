using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Entities;
using Strata.Game.Player;

namespace Strata.Game.Entities;

/// <summary>
/// Shared projectile resources and the node pool (P1b-15): glow materials keyed by colour and strength, spheres keyed
/// by radius (mm), one bullet streak; a finished view goes back to its type's pool (32 kept per type) when its shot
/// ends, and the next shot of that type reuses it. Creature-model minions, pets and bobbers are never pooled. Also the
/// muzzle offset (P3-10): the local player's new shots are drawn from the held item's muzzle, easing onto the simulated
/// path over a few frames while the simulation stays authoritative.
/// </summary>
public sealed partial class ProjectileRenderer
{
    private const int PoolCap = 32;

    /// <summary>Seconds over which a shot's view eases from the muzzle onto its simulated position.</summary>
    private const float MuzzleEase = 0.07f;

    private static readonly Dictionary<(Color, float), StandardMaterial3D> Glows = new();
    private static readonly Dictionary<int, SphereMesh> Spheres = new();
    private static CapsuleMesh? _bullet;

    private readonly Dictionary<string, Stack<Node3D>> _pool = new();
    private readonly Dictionary<Node3D, string> _pooledType = new();
    private readonly Dictionary<long, (Vector3 Offset, float Left)> _offsets = new();

    /// <summary>Projectile views built from scratch since start (probe <c>projectiles.nodes_created</c>).</summary>
    public int NodesCreated { get; private set; }

    /// <summary>Views waiting in the pools now (probe <c>projectiles.pooled</c>).</summary>
    public int Pooled { get; private set; }

    /// <summary>Shots drawn leaving the muzzle since start (probe <c>projectiles.from_muzzle</c>).</summary>
    public int FromMuzzle { get; private set; }

    private static bool Poolable(Projectile p) => p.Def.Visual != "creature" && p.PetSlot == null && !p.Def.Bobber && p.Def.Minion == null;

    private static StandardMaterial3D SharedGlow(Color color, float energy)
    {
        if (!Glows.TryGetValue((color, energy), out var m)) Glows[(color, energy)] = m = Glow(color, energy);
        return m;
    }

    private static SphereMesh Sphere(float radius)
    {
        int mm = Mathf.Max(1, Mathf.RoundToInt(radius * 1000f));
        if (!Spheres.TryGetValue(mm, out var mesh)) Spheres[mm] = mesh = new SphereMesh { Radius = mm / 1000f, Height = mm / 500f, RadialSegments = 10, Rings = 6 };
        return mesh;
    }

    private static CapsuleMesh BulletMesh => _bullet ??= new CapsuleMesh { Radius = 0.025f, Height = 0.45f, RadialSegments = 6, Rings = 1 };

    /// <summary>A shot ended: its view waits hidden in its type's pool, or is freed when the pool is full or it cannot be reused.</summary>
    private void Release(Projectile p, Node3D node)
    {
        if (_pooledType.TryGetValue(node, out var type) && type == p.Def.Id)
        {
            if (!_pool.TryGetValue(type, out var stack)) _pool[type] = stack = new Stack<Node3D>();
            if (stack.Count < PoolCap)
            {
                node.Visible = false;
                stack.Push(node);
                Pooled++;
                return;
            }
        }
        _pooledType.Remove(node);
        node.QueueFree();
    }

    /// <summary>The local player's new shot starts at the held item's muzzle (or tip) when one is near where it spawned.</summary>
    private void StartAtMuzzle(Projectile p)
    {
        if (p.Owner == null || p.Owner != _game.LocalPlayer || !Poolable(p) || p.FromMinion) return;
        if (!HeldSockets.TryBusinessEnd(out var muzzle)) return;
        var spawn = new Vector3(p.SpawnOrigin.X, p.SpawnOrigin.Y, p.SpawnOrigin.Z);
        var offset = muzzle - spawn;
        if (offset.LengthSquared() > 1.5f * 1.5f || offset.LengthSquared() < 1e-4f) return; // a sky or cursor shot, or already there
        _offsets[p.Id] = (offset, MuzzleEase);
        FromMuzzle++;
    }

    /// <summary>The view's offset from the simulated position this frame: the muzzle offset, shrinking to nothing.</summary>
    private Vector3 MuzzleOffset(long id, float dt)
    {
        if (!_offsets.TryGetValue(id, out var o)) return Vector3.Zero;
        float left = o.Left - dt;
        if (left <= 0f)
        {
            _offsets.Remove(id);
            return Vector3.Zero;
        }
        _offsets[id] = (o.Offset, left);
        return o.Offset * (left / MuzzleEase);
    }
}
