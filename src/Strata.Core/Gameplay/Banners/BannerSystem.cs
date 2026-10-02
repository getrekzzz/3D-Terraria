using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Entities;
using Strata.Core.Gameplay.Items;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Banners;

/// <summary>
/// Enemy banners (X-03): every <see cref="BannerConfig.KillsPerBanner"/> kills of a banner's creatures (counted per world
/// in <c>Session.Counters["banner:&lt;id&gt;"]</c>; statue creatures, parts and loot-less boss phases do not count) drop
/// that banner. A placed banner empowers players near it against its creatures: they deal
/// <see cref="BannerConfig.DamageDealt"/>× damage and take <see cref="BannerConfig.DamageTaken"/>× contact damage, and
/// they show the Monster Banner buff. Banners are tile objects with <see cref="TileObjectDef.Banner"/>.
/// </summary>
public sealed class BannerSystem
{
    public const string CounterPrefix = "banner:";

    private readonly GameWorld _world;
    private Dictionary<string, TileObjectDef>? _byEnemy;
    private readonly Dictionary<PlayerEntity, HashSet<string>> _near = new();
    private static readonly HashSet<string> None = new();

    public BannerSystem(GameWorld world)
    {
        _world = world;
        world.Npcs.Killed += OnKilled;
    }

    public BannerConfig Config { get; set; } = new();

    /// <summary>Banners dropped so far this session (probe <c>banners.dropped</c>).</summary>
    public int Dropped { get; private set; }

    /// <summary>The banner that counts and empowers against <paramref name="enemy"/>, if any.</summary>
    public TileObjectDef? BannerFor(EnemyDef enemy)
    {
        if (_byEnemy == null)
        {
            _byEnemy = new Dictionary<string, TileObjectDef>(StringComparer.Ordinal);
            foreach (var def in _world.Session.Content.TileObjects.All)
                if (def.Banner is { } spec)
                    foreach (var e in spec.Enemies) _byEnemy.TryAdd(e, def);
        }
        return _byEnemy.GetValueOrDefault(enemy.Id);
    }

    /// <summary>World-wide kills counted towards a banner.</summary>
    public int Kills(TileObjectDef banner) => _world.Session.Counters.GetValueOrDefault(CounterPrefix + banner.Id);

    /// <summary>Creatures (enemy ids) the banners near a player work against.</summary>
    public IReadOnlyCollection<string> Near(PlayerEntity p) => _near.TryGetValue(p, out var set) ? set : None;

    /// <summary>Damage a player deals to a creature, raised when one of its banners is near.</summary>
    public int Dealt(PlayerEntity? attacker, Npc npc, int damage) =>
        attacker != null && _near.TryGetValue(attacker, out var set) && set.Contains(npc.Def.Id) ? (int)MathF.Round(damage * Config.DamageDealt) : damage;

    /// <summary>Contact damage a creature deals to a player, lowered when one of its banners is near.</summary>
    public int Taken(PlayerEntity player, Npc npc, int damage) =>
        _near.TryGetValue(player, out var set) && set.Contains(npc.Def.Id) ? Math.Max(1, (int)MathF.Round(damage * Config.DamageTaken)) : damage;

    private void OnKilled(Npc npc)
    {
        if (npc.FromStatue || npc.IsPart || npc.Def.Boss != null || BannerFor(npc.Def) is not { } banner || Config.KillsPerBanner <= 0) return;
        string key = CounterPrefix + banner.Id;
        int kills = _world.Session.Counters.GetValueOrDefault(key) + 1;
        _world.Session.Counters[key] = kills;
        if (kills % Config.KillsPerBanner != 0 || !_world.Session.Content.Items.TryGet(banner.Id, out var item)) return;
        _world.Drops.Spawn(new ItemStack(item, 1), npc.Center, new Vector3(0, 3f, 0));
        Dropped++;
    }

    /// <summary>Rescans the banners near each player every <see cref="BannerConfig.RefreshTicks"/> ticks.</summary>
    public void Tick()
    {
        if (Config.RefreshTicks <= 0 || _world.Session.Clock.Tick % Config.RefreshTicks != 0) return;
        foreach (var p in _world.Players)
        {
            if (!_near.TryGetValue(p, out var set)) _near[p] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Clear();
            if (!p.Alive) continue;
            var c = p.FeetCell;
            var r = new Int3(Config.Radius, Config.Radius, Config.Radius);
            foreach (var obj in _world.Session.World.TileObjects.InBox(new Aabb3i(c - r, c + r)))
                if (obj.Def.Banner is { } spec)
                    foreach (var e in spec.Enemies) set.Add(e);
            if (set.Count > 0 && _world.Session.Content.Buffs.TryGet(Config.Buff, out var buff))
            {
                p.State.Buffs.Add(buff, Config.RefreshTicks + 15, p.State.Effects.Immunities);
                p.State.Recompute();
            }
        }
    }
}
