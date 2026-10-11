using System.Collections.Generic;
using Godot;
using Strata.Core.Content;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.Voxels;

namespace Strata.Game.Rendering;

/// <summary>
/// Starts threaded loads (<see cref="AssetCache"/>) for what the player is about to meet, so it is ready on first
/// sight: at launch every humans' animation set, the town residents and the items carried (with their projectiles);
/// the tile objects of every chunk as it streams in; and every 2 s the creatures likely to spawn where the player
/// stands and the bosses the carried summoning items call, whose pools are also pre-warmed (1 visual each on the low
/// preset, 2 otherwise). Relevance only: nothing is loaded just because it exists.
/// </summary>
public sealed partial class AssetPreloader : Node
{
    private const double Interval = 2.0;
    private const int WarmKinds = 6;
    private GameWorld _game = null!;
    private Entities.NpcRenderer _creatures = null!;
    private int _warmCount;
    private double _clock = Interval;
    private readonly HashSet<(string Id, int Variant)> _objects = new();

    /// <summary>Model requests made (probe <c>assets.preloaded</c>).</summary>
    public int Requested { get; private set; }

    public void Initialize(GameWorld game, Entities.NpcRenderer creatures, bool lowPreset)
    {
        _game = game;
        _creatures = creatures;
        _warmCount = lowPreset ? 1 : 2;
        Name = "AssetPreloader";
    }

    public override void _Ready()
    {
        foreach (var set in HumanAnimations.AllSetPaths()) Request(set);
        foreach (var resident in _game.Town.Residents) RequestModel(AssetPaths.TownNpcModel(resident.Def));
        _game.Session.ChunkInstalled += OnChunkInstalled;
    }

    public override void _ExitTree() => _game.Session.ChunkInstalled -= OnChunkInstalled;

    public override void _Process(double delta)
    {
        _clock += delta;
        if (_clock < Interval) return;
        _clock = 0;
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.assets.preload");
        foreach (var resident in _game.Town.Residents) RequestModel(AssetPaths.TownNpcModel(resident.Def));
        if (_game.LocalPlayer is not { } player) return;
        var inventory = player.State.Inventory;
        RequestItems(inventory.Backpack);
        RequestItems(inventory.Armor);
        RequestItems(inventory.Accessories);
        int warmed = 0;
        foreach (var enemy in _game.Spawner.LikelyEnemies(player))
        {
            var names = enemy.ModelNames();
            foreach (var name in names) RequestModel(Creature(name));
            if (warmed++ < WarmKinds) _creatures.Prewarm(names[0], _warmCount);
        }
    }

    private void RequestItems(Strata.Core.Gameplay.Items.Inventory slots)
    {
        var content = _game.Session.Content;
        for (int i = 0; i < slots.Size; i++)
        {
            if (slots[i].Item is not { } item) continue;
            if (AssetPaths.ItemModel(item, content) is { } path) RequestModel(path);
            if (item.Projectile != null && content.Projectiles.TryGet(item.Projectile, out var shot))
                RequestModel(shot.Visual == "creature" ? Creature(shot.Model ?? shot.Id) : shot.Model != null ? AssetPaths.ProjectileModel(shot) : null);
            if (_game.Uses[item].Summon is { } summon && content.Enemies.TryGet(summon.Enemy, out var boss))
                foreach (var name in boss.ModelNames()) RequestModel(Creature(name));
        }
    }

    private void OnChunkInstalled(Chunk chunk, bool fromSave)
    {
        foreach (var o in _game.Session.World.TileObjects.InChunk(chunk.Coord))
            if (_objects.Add((o.Def.Id, o.Variant))) RequestModel(AssetPaths.ObjectModel(o.Def, o.Variant));
    }

    private static string Creature(string model) => $"assets/models/creatures/{model}.glb";

    private void RequestModel(string? projectPath)
    {
        if (projectPath != null) Request("res://" + projectPath);
    }

    private void Request(string resPath)
    {
        if (AssetCache.IsPending(resPath)) return;
        AssetCache.Request(resPath);
        if (AssetCache.IsPending(resPath)) Requested++;
    }
}
