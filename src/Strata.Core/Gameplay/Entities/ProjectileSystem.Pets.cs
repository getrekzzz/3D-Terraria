using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Items;
using Strata.Core.Physics;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Entities;

/// <summary>
/// Pets and light pets: exactly one companion per filled misc pet / light-pet slot (the item's projectile, which has
/// <see cref="ProjectileDef.Pet"/>). They are harmless, never expire and follow their owner; a light pet's glow comes
/// from its projectile light (the renderer offers it to the shared dynamic light pool).
/// </summary>
public sealed partial class ProjectileSystem
{
    /// <summary>A pet left further behind than this (m) appears beside its owner again.</summary>
    public const float PetTeleport = 30f;

    private static readonly MiscSlot[] PetSlots = { MiscSlot.Pet, MiscSlot.LightPet };

    /// <summary>The pet projectile an item summons, or null when the item is not a pet.</summary>
    public ProjectileDef? PetOf(ItemDef? item) =>
        item?.Projectile is { } id && _world.Session.Content.Projectiles.TryGet(id, out var def) && def.Pet != null ? def : null;

    /// <summary>The owner's pet in a misc slot (Pet or LightPet), or null.</summary>
    public Projectile? PetIn(PlayerEntity owner, MiscSlot slot)
    {
        foreach (var p in _items)
            if (p.Owner == owner && p.PetSlot == slot) return p;
        return null;
    }

    /// <summary>Pets and light pets out in the world (all owners).</summary>
    public int PetCount(bool lights)
    {
        int n = 0;
        foreach (var p in _items)
            if (p.PetSlot is { } slot && (slot == MiscSlot.LightPet) == lights) n++;
        return n;
    }

    /// <summary>Spawns, swaps and dismisses pets to match each living player's misc pet slots.</summary>
    private void SyncPets()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var p = _items[i];
            if (p.PetSlot is not { } slot) continue;
            var owner = p.Owner;
            if (owner == null || !owner.Alive || !_world.Players.Contains(owner)
                || owner.State.Inventory.Misc[(int)slot].Item != p.Source || PetOf(p.Source) != p.Def)
                End(i);
        }
        foreach (var owner in _world.Players)
        {
            if (!owner.Alive) continue;
            foreach (var slot in PetSlots)
            {
                var item = owner.State.Inventory.Misc[(int)slot].Item;
                if (PetOf(item) is not { } def || PetIn(owner, slot) != null) continue;
                var pet = Spawn(def, PetHome(owner, def, 0), Vector3.Zero, owner, 0, 0, DamageClass.None, 0, item, slot);
                pet.Position = PetHome(owner, def, pet.Id);
            }
        }
    }

    /// <summary>Where a pet wants to be: behind the owner (fliers above the shoulder, walkers at their feet).</summary>
    private static Vector3 PetHome(PlayerEntity owner, ProjectileDef def, long seed)
    {
        var pet = def.Pet!;
        var facing = new Vector3(owner.Facing.X, 0, owner.Facing.Z);
        facing = facing.LengthSquared() > 1e-4f ? Vector3.Normalize(facing) : -Vector3.UnitZ;
        var side = new Vector3(-facing.Z, 0, facing.X) * (seed % 2 == 0 ? 0.55f : -0.55f);
        var behind = -facing * pet.Follow + side;
        return pet.Flies ? owner.Center + behind + new Vector3(0, pet.Hover, 0) : owner.Position + behind + new Vector3(0, PetHalf(def).Y, 0);
    }

    private static Vector3 PetHalf(ProjectileDef def) => new(MathF.Min(def.Radius * Units.CellSize, 0.3f));

    /// <summary>Advances a pet; false when it should be removed (its owner died).</summary>
    private bool TickPet(Projectile p, float dt)
    {
        p.Age++;
        if (p.Owner is not { Alive: true } owner) return false;
        var pet = p.Def.Pet!;
        var home = PetHome(owner, p.Def, p.Id);
        if (Vector3.DistanceSquared(p.Position, home) > PetTeleport * PetTeleport)
        {
            p.Position = home;
            p.Velocity = Vector3.Zero;
            return true;
        }
        if (pet.Flies)
        {
            // Hover behind the shoulder with a gentle bob, hurrying more the further off it is (through terrain).
            var goal = home + new Vector3(0, 0.15f * MathF.Sin(p.Age * 0.07f + p.Id), 0);
            var to = goal - p.Position;
            float dist = to.Length();
            float speed = MathF.Min(dist * 3f, pet.Speed * (1f + dist / 6f));
            var want = dist > 1e-4f ? to / dist * speed : Vector3.Zero;
            p.Velocity = Vector3.Lerp(p.Velocity, want, MathF.Min(1f, 8f * dt));
            p.Position += p.Velocity * dt;
            return true;
        }
        // Walkers run after their owner on the ground and hop up one-cell steps (and after an owner who climbed).
        var flat = new Vector3(home.X - p.Position.X, 0, home.Z - p.Position.Z);
        float far = flat.Length();
        float run = far < 0.35f ? 0f : MathF.Min(far * 4f, pet.Speed * (1f + far / 5f));
        var dir = far > 1e-4f ? flat / far : Vector3.Zero;
        float k = MathF.Min(1f, 10f * dt);
        p.Velocity.X += (dir.X * run - p.Velocity.X) * k;
        p.Velocity.Z += (dir.Z * run - p.Velocity.Z) * k;
        p.Velocity.Y -= 30f * dt;
        var before = p.Velocity;
        p.Grounded = VoxelMotion.Move(_world.Session.World, ref p.Position, ref p.Velocity, PetHalf(p.Def), dt);
        bool blocked = (before.X != 0 && p.Velocity.X == 0) || (before.Z != 0 && p.Velocity.Z == 0);
        if (p.Grounded && (blocked || home.Y - p.Position.Y > 1.2f))
            p.Velocity = new Vector3(before.X, 7.5f, before.Z); // about 1.5 cells: clears one step
        return true;
    }
}
