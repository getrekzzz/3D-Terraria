using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Gameplay.Progression;

/// <summary>
/// The world's breakable objectives: smashing Shadow Orbs and Crimson Hearts (every third wakes the world's evil
/// boss, and a meteorite may fall that night) and Demon Altars (each smash seeds a new tier of hardmode ore through
/// the flags it raises). Counts persist in the world's counters.
/// </summary>
public sealed class WorldObjectives
{
    public const string MeteorCounter = "meteors";
    public const string MeteorPending = "meteor_pending";
    public const string MeteorMutation = "meteorite";

    private readonly GameWorld _world;
    private bool _wasDay;
    private Determinism.Pcg32 _rng;

    public WorldObjectives(GameWorld world)
    {
        _world = world;
        _rng = new Determinism.Pcg32(Determinism.Hash.Combine((ulong)world.Session.Settings.Seed, 0x0B1EC7UL));
        _wasDay = world.Session.Clock.IsDay;
        world.Editor.ObjectBroken += Broken;
    }

    /// <summary>A message for every player (colour as hex).</summary>
    public event Action<string, string>? Announced;

    /// <summary>A meteorite struck: crater centre (cells).</summary>
    public event Action<Int3>? MeteorLanded;

    public int Count(string counter) => _world.Session.Counters.GetValueOrDefault(counter);

    private void Broken(TileObjectInstance obj)
    {
        var fx = obj.Def.OnBreak;
        if (fx == null) return;
        var counters = _world.Session.Counters;
        int n = counters.GetValueOrDefault(fx.Counter) + 1;
        counters[fx.Counter] = n;
        if (n - 1 < fx.Flags.Length) _world.Progression.Set(fx.Flags[n - 1]);
        if (fx.Messages.Length > 0) Announced?.Invoke(fx.Messages[Math.Min(n, fx.Messages.Length) - 1], "#b080ff");
        var at = (new Vector3(obj.Anchor.X, obj.Anchor.Y, obj.Anchor.Z) + new Vector3(obj.Footprint.X, 0, obj.Footprint.Z) * 0.5f) * Units.CellSize;
        var content = _world.Session.Content;
        if (fx.Summon != null && fx.Every > 0 && n % fx.Every == 0 && content.Enemies.TryGet(fx.Summon, out var boss)
            && !_world.Npcs.All.Any(x => x.Alive && x.Def == boss))
            _world.Npcs.Spawn(boss, at + new Vector3(0, 2f, 0));
        if (fx.Release != null && _rng.Chance(fx.ReleaseChance) && content.Enemies.TryGet(fx.Release, out var released))
            _world.Npcs.Spawn(released, at + new Vector3(0, 1f, 0));
        if (fx.Meteor > 0 && _rng.Chance(fx.Meteor)) counters[MeteorPending] = 1;
    }

    /// <summary>At nightfall a pending meteorite lands somewhere on the surface.</summary>
    public void Tick()
    {
        bool day = _world.Session.Clock.IsDay;
        if (_wasDay && !day && Count(MeteorPending) > 0) DropMeteor();
        _wasDay = day;
    }

    /// <summary>Drops a meteorite now (commands, tests); returns the crater centre, or null without the content.</summary>
    public Int3? DropMeteor()
    {
        var content = _world.Session.Content;
        if (!content.Mutations.TryGet(MeteorMutation, out var def)) return null;
        var counters = _world.Session.Counters;
        counters.Remove(MeteorPending);
        int n = counters.GetValueOrDefault(MeteorCounter) + 1;
        counters[MeteorCounter] = n;
        var step = (WorldGen.Mutations.MeteorStep)_world.Session.Mutations.AppendOccurrence(def, n);
        Announced?.Invoke("A meteorite has landed!", "#50ff80");
        MeteorLanded?.Invoke(step.Center);
        return step.Center;
    }
}
