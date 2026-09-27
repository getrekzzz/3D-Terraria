using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Progression;

/// <summary>
/// Bosses called up by a gathering (see <see cref="BossRitual"/>): once the ritual's flag is raised, worshippers and
/// their guards gather around an altar at a landmark whenever a player comes near. Striking down a worshipper ends
/// the ritual: the rest of the circle and the altar vanish and the boss rises in their place.
/// </summary>
public sealed class Rituals
{
    /// <summary>Ticks between checks, and after a boss leaves before the circle gathers again.</summary>
    public const int CheckInterval = 60;
    public const int Regather = 600;

    private readonly GameWorld _world;
    private readonly List<(EnemyDef Boss, BossRitual Ritual)> _rituals = new();
    private int _tick, _cooldown;

    public Rituals(GameWorld world)
    {
        _world = world;
        foreach (var e in world.Session.Content.Enemies.All)
            if (e.Boss?.Ritual is { } r) _rituals.Add((e, r));
        world.Npcs.Killed += Disturbed;
        world.Npcs.BossLeft += npc =>
        {
            if (_rituals.Any(r => r.Boss == npc.Def)) _cooldown = Regather;
        };
    }

    public void Tick()
    {
        if (_cooldown > 0) _cooldown--;
        if (++_tick % CheckInterval != 0 || _cooldown > 0) return;
        foreach (var (boss, ritual) in _rituals)
            if (Ready(boss, ritual) is { } site) Gather(ritual, site);
    }

    /// <summary>The landmark (feet position) when this ritual should gather now: flag raised, boss unbeaten and absent, no circle yet, a player near.</summary>
    public Vector3? Ready(EnemyDef boss, BossRitual ritual)
    {
        var progression = _world.Progression;
        if (!progression.IsSet(ritual.Requires) || (boss.Boss?.Flag is { } done && progression.IsSet(done))) return null;
        if (!_world.Session.Generator.Plan.Landmarks.TryGetValue(ritual.Landmark, out var cell)) return null;
        if (_world.Npcs.All.Any(n => n.Alive && (n.Def == boss || n.Def.Id == ritual.Worshipper))) return null;
        var site = new Vector3(cell.X + 0.5f, cell.Y, cell.Z + 0.5f) * Units.CellSize;
        return _world.Players.Any(p => p.Alive && Vector3.Distance(p.Position, site) < ritual.Range) ? site : null;
    }

    /// <summary>Gathers the circle at a site now (also used by commands and tests).</summary>
    public void Gather(BossRitual ritual, Vector3 site)
    {
        var content = _world.Session.Content;
        var npcs = _world.Npcs;
        if (ritual.Altar != null && content.Enemies.TryGet(ritual.Altar, out var altar)) npcs.Spawn(altar, site);
        if (content.Enemies.TryGet(ritual.Worshipper, out var worshipper))
            for (int i = 0; i < ritual.Count; i++)
            {
                float a = i * MathF.Tau / ritual.Count;
                var at = site + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * ritual.Radius;
                if (npcs.Spawn(worshipper, at) is { } w) w.Yaw = MathF.Atan2(site.X - at.X, site.Z - at.Z); // facing the altar
            }
        if (ritual.Guard != null && content.Enemies.TryGet(ritual.Guard, out var guard))
            for (int i = 0; i < ritual.Guards; i++)
            {
                float a = (i + 0.5f) * MathF.Tau / Math.Max(1, ritual.Guards);
                npcs.Spawn(guard, site + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * (ritual.Radius + 3f));
            }
    }

    private void Disturbed(Npc npc)
    {
        foreach (var (boss, ritual) in _rituals)
        {
            if (npc.Def.Id != ritual.Worshipper || _world.Npcs.All.Any(n => n.Alive && n.Def == boss)) continue;
            var circle = _world.Npcs.All.Where(n => n.Alive && n != npc && (n.Def.Id == ritual.Worshipper || n.Def.Id == ritual.Altar)).ToList();
            var at = circle.FirstOrDefault(n => n.Def.Id == ritual.Altar)?.Position ?? npc.Position;
            _world.Npcs.Spawn(boss, at + new Vector3(0, 1.5f, 0));
            foreach (var n in circle) _world.Npcs.Kill(n);
        }
    }
}
