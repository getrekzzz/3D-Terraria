using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Session;
using Strata.Core.World;

namespace Strata.Game.Audio;

/// <summary>
/// The soundscape under the music, on the Ambient bus (<c>res://assets/audio/ambient/&lt;id&gt;.ogg</c>): the ambience of
/// the highest-priority biome around the player that names one (its "&lt;id&gt;_night" loop after dark, on the surface),
/// else the depth layer's own (caves underground, lava in the underworld, wind up high, birds on the surface). Changes
/// cross-fade; a living boss pushes the ambience back so the fight reads.
/// </summary>
public sealed partial class AmbienceDirector : Node
{
    public const float FadeSeconds = 3.0f;
    private const float BossDuckDb = -9f;

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[2];
    private readonly float[] _level = new float[2];
    private readonly Dictionary<string, AudioStream?> _streams = new();
    private GameWorld _game = null!;
    private PlayerEntity _player = null!;
    private int _active;
    private string? _current;
    private double _check;
    private bool _boss;
    private float _duckDb;

    public void Initialize(GameWorld game, PlayerEntity player)
    {
        Name = "AmbienceDirector";
        _game = game;
        _player = player;
    }

    public override void _Ready()
    {
        for (int i = 0; i < 2; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = AudioBuses.Ambient, VolumeDb = -80f };
            AddChild(_players[i]);
        }
    }

    /// <summary>Stops the loops on the way out, so no playback is left running when the game quits.</summary>
    public override void _ExitTree()
    {
        foreach (var p in _players) p.Stop();
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.ambience");
        _check -= delta;
        if (_check <= 0)
        {
            _check = 0.75;
            _boss = _game.Npcs.All.Any(n => n.Alive && n.IsBoss);
            Play(Choose(_game, _player));
        }
        float dt = (float)delta;
        _duckDb = Mathf.MoveToward(_duckDb, _boss ? BossDuckDb : 0f, dt * 6f);
        for (int i = 0; i < 2; i++)
        {
            _level[i] = Mathf.MoveToward(_level[i], i == _active && _current != null ? 1f : 0f, dt / FadeSeconds);
            var p = _players[i];
            p.VolumeDb = Mathf.LinearToDb(Mathf.Max(_level[i], 0.0001f)) + _duckDb;
            if (_level[i] <= 0f && p.Playing) p.Stop();
        }
    }

    /// <summary>The ambience loop that fits where the player is now (null when none applies).</summary>
    public string? Choose(GameWorld game, PlayerEntity player)
    {
        var session = game.Session;
        var ctx = session.ContextAt(player.FeetCell);
        bool below = ctx.Layer is WorldLayer.Underground or WorldLayer.Cavern;
        bool night = !below && !session.Clock.IsDay;
        foreach (var biome in ctx.Biomes.Select(id => session.Content.Biomes.TryGet(id, out var b) ? b : null).Where(b => b?.Ambience != null).OrderByDescending(b => b!.Priority))
        {
            if (below && biome!.Kind == BiomeKind.Surface) continue; // the surface's birds stay up top
            if (Resolve(biome!.Ambience!, night) is { } id) return id;
        }
        return Resolve(ctx.Layer switch
        {
            WorldLayer.Underground or WorldLayer.Cavern => "cave",
            WorldLayer.Underworld => "lava",
            WorldLayer.Space or WorldLayer.Sky => "high_wind",
            _ => "birds",
        }, night);
    }

    private string? Resolve(string id, bool night)
    {
        if (night && Stream(id + "_night") != null) return id + "_night";
        return Stream(id) != null ? id : null;
    }

    private AudioStream? Stream(string id)
    {
        if (_streams.TryGetValue(id, out var s)) return s;
        string path = $"{AudioDiagnostics.AmbientDirectory}/{id}.ogg";
        s = AudioDiagnostics.Load(path);
        if (s == null && ResourceLoader.Exists(path))
            GD.PushError($"audio: could not load {path}. Open the project in the Godot editor once (or run `godot --path . --import`) so the ambience is imported.");
        if (s is AudioStreamOggVorbis ogg) ogg.Loop = true;
        return _streams[id] = s;
    }

    private void Play(string? id)
    {
        if (id == _current) return;
        _current = id;
        if (id == null || Stream(id) is not { } stream) return;
        _active = 1 - _active;
        var to = _players[_active];
        if (to.Playing && to.Stream == stream) return; // turning straight back: the fading copy just fades in again
        to.Stream = stream;
        _level[_active] = 0f;
        to.Play();
    }
}
