using System.Numerics;
using Strata.Core.AI;
using Strata.Core.Common;
using Strata.Core.Physics;
using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Town;

public sealed partial class TownSystem
{
    private const float Gravity = 28f;

    private void TickResident(TownNpc r)
    {
        const float dt = 1f / Units.TicksPerSecond;
        if (!r.Alive)
        {
            if (r.RespawnTicks > 0 && --r.RespawnTicks == 0) Revive(r);
            return;
        }
        if (World.GetBlock(VoxelMotion.CellOf(r.Center)) == VoxelWorld.Unknown) return; // frozen while unloaded
        TakeContactHits(r);
        if (!r.Alive) return;
        Decide(r);
        var wish = Steer(r);
        if (r.ImmuneTicks < HitImmunity - 12)
        {
            r.Velocity.X = wish.X;
            r.Velocity.Z = wish.Z;
        }
        r.Velocity.Y = MathF.Max(r.Velocity.Y - Gravity * dt, -30f);
        var center = r.Center;
        r.OnGround = VoxelMotion.Move(World, ref center, ref r.Velocity, TownNpc.HalfExtents, dt);
        r.Position = center - new Vector3(0, TownNpc.HalfExtents.Y, 0);
        if (new Vector2(wish.X, wish.Z).LengthSquared() > 0.01f) r.Yaw = BrainFactory.YawOf(new Vector3(wish.X, 0, wish.Z));
        CloseDoorsBehind(r);
    }

    /// <summary>Chooses the errand: talk, go home at night, or stroll near home by day.</summary>
    private void Decide(TownNpc r)
    {
        if (r.TalkingTo is { } p)
        {
            bool near = p.Alive && Vector3.Distance(p.Center, r.Center) <= Config.TalkRange + 1f;
            if (near && --r.TalkTicks > 0)
            {
                r.Activity = ResidentActivity.Talking;
                r.Path = null;
                r.Yaw = BrainFactory.YawOf(p.Position - r.Position);
                return;
            }
            EndTalk(r);
        }
        bool night = !_world.Session.Clock.IsDay;
        if (night && r.Home is { } home)
        {
            if (r.Activity != ResidentActivity.GoingHome)
            {
                r.Activity = ResidentActivity.GoingHome;
                GoTo(r, home);
                r.IdleTicks = 240;
            }
            else if (r.Path == null && Vector3.Distance(r.Position, NodeFeet(home)) > 1f && --r.IdleTicks <= 0)
            {
                GoTo(r, home); // the way was blocked: try again now and then
                r.IdleTicks = 240;
            }
            return;
        }
        if (r.Path != null) return;
        r.Activity = ResidentActivity.Idle;
        if (--r.IdleTicks > 0) return;
        r.IdleTicks = _rng.Range(Config.IdleTicksMin, Config.IdleTicksMax);
        var landmark = r.Home == null ? LandmarkNode(r.Def) : null;
        var anchor = r.Home ?? landmark ?? CurrentNode(r);
        int reach = (int)((landmark != null ? Config.LandmarkRadius : Config.WanderRadius) / Units.CellSize);
        var target = anchor + new Int3(_rng.Range(-reach, reach), 0, _rng.Range(-reach, reach));
        if (_world.Navigator.NearestStandable(target, Agent, 3) is { } node)
        {
            r.Activity = ResidentActivity.Walking;
            GoTo(r, node);
        }
    }

    private void GoTo(TownNpc r, Int3 goal)
    {
        var start = CurrentNode(r);
        r.Path = _world.Navigator.FindPath(start, goal, Agent, maxNodes: 2500, allowPartial: true);
        r.PathIndex = r.Path is { Count: > 1 } ? 1 : 0;
        r.StuckTicks = 0;
        r.LastDistance = float.MaxValue;
        if (r.Path is not { Count: > 1 }) r.Path = null;
    }

    /// <summary>Horizontal velocity toward the next path node; jumps ledges, opens doors, recovers when stuck.</summary>
    private Vector3 Steer(TownNpc r)
    {
        if (r.Path == null) return Vector3.Zero;
        if (r.PathIndex >= r.Path.Count)
        {
            r.Path = null;
            return Vector3.Zero;
        }
        var node = r.Path[r.PathIndex];
        var target = NodeFeet(node);
        var flat = new Vector3(target.X - r.Position.X, 0, target.Z - r.Position.Z);
        float dist = flat.Length();
        if (dist < 0.12f && MathF.Abs(target.Y - r.Position.Y) < 0.45f)
        {
            r.PathIndex++;
            r.StuckTicks = 0;
            r.LastDistance = float.MaxValue;
            return Vector3.Zero;
        }
        OpenDoorsAt(r, node);
        if (node.Y * Units.CellSize > r.Position.Y + 0.2f && r.OnGround)
            r.Velocity.Y = MathF.Sqrt(2f * Gravity * (target.Y - r.Position.Y + 0.35f));

        // No progress for a while: hop, then give up on this node.
        if (dist < r.LastDistance - 0.01f)
        {
            r.LastDistance = dist;
            r.StuckTicks = 0;
        }
        else if (++r.StuckTicks == 60 && r.OnGround)
        {
            r.Velocity.Y = 6f;
        }
        else if (r.StuckTicks > 150)
        {
            r.Position = target;
            r.Velocity = Vector3.Zero;
            r.PathIndex++;
            r.StuckTicks = 0;
            r.LastDistance = float.MaxValue;
            return Vector3.Zero;
        }
        float speed = MathF.Min(r.Def.Speed, dist * 6f);
        return dist > 1e-4f ? flat / dist * speed : Vector3.Zero;
    }

    private void OpenDoorsAt(TownNpc r, Int3 node)
    {
        foreach (var door in _world.Navigator.DoorsAt(node, Agent))
        {
            if (door.Active) continue;
            if (_world.Interaction.SetDoor(door, true, Array.Empty<(Vector3, Vector3)>())) r.OpenedDoors.Add(door);
        }
    }

    /// <summary>Doors a resident opened are shut again once nobody stands in them.</summary>
    private void CloseDoorsBehind(TownNpc r)
    {
        for (int i = r.OpenedDoors.Count - 1; i >= 0; i--)
        {
            var door = r.OpenedDoors[i];
            if (World.TileObjects.Get(door.Id) != door)
            {
                r.OpenedDoors.RemoveAt(i);
                continue;
            }
            var bodies = _residents.Select(o => o.CellBox).Concat(_world.Players.Where(p => p.Alive).Select(p => p.CellBox)).ToList();
            var b = door.Bounds;
            var near = new Vector3((b.Min.X + b.Max.X) * 0.5f, (b.Min.Y + b.Max.Y) * 0.5f, (b.Min.Z + b.Max.Z) * 0.5f) * Units.CellSize;
            if (Vector3.Distance(near, r.Center) < 1.4f) continue;
            if (_world.Interaction.SetDoor(door, false, bodies) || !door.Active) r.OpenedDoors.RemoveAt(i);
        }
    }

    /// <summary>The navigation node the resident is standing on (nearest standable vertex).</summary>
    public Int3 CurrentNode(TownNpc r)
    {
        var guess = new Int3((int)MathF.Round(r.Position.X / Units.CellSize), (int)MathF.Floor((r.Position.Y + 0.05f) / Units.CellSize), (int)MathF.Round(r.Position.Z / Units.CellSize));
        return _world.Navigator.IsStandable(guess, Agent) ? guess : _world.Navigator.NearestStandable(guess, Agent, 2) ?? guess;
    }

    private void Revive(TownNpc r)
    {
        var at = r.Home ?? LandmarkNode(r.Def);
        if (at == null && r.Def.Landmark != null)
        {
            r.RespawnTicks = LandmarkInterval; // their place is not loaded: come back once it is
            return;
        }
        at ??= _world.Navigator.NearestStandable(_world.Session.SpawnCell, Agent, 6) ?? _world.Session.SpawnCell;
        r.Life = r.Def.Life;
        r.Position = NodeFeet(at.Value);
        r.Velocity = Vector3.Zero;
        r.Path = null;
        Spawned?.Invoke(r);
    }

    /// <summary>Starts a conversation: the resident stops and faces the player for a while.</summary>
    public bool BeginTalk(TownNpc r, Session.PlayerEntity p)
    {
        if (!r.Alive || Vector3.Distance(p.Center, r.Center) > Config.TalkRange) return false;
        r.TalkingTo = p;
        r.TalkTicks = Config.TalkTicks;
        r.Path = null;
        return true;
    }

    public void EndTalk(TownNpc r)
    {
        r.TalkingTo = null;
        r.TalkTicks = 0;
        r.Activity = ResidentActivity.Idle;
    }
}
