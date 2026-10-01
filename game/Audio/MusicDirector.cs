using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Session;
using Strata.Core.World;

namespace Strata.Game.Audio;

/// <summary>
/// Chooses and cross-fades the soundtrack (<c>res://assets/audio/music/&lt;track&gt;.ogg</c>): a boss's theme while one
/// lives, then an active event's, then the biome around the player (with night, rain, town and underground variants,
/// see MusicDirector.Variants.cs), then the depth layer. Content music states that have no track of their own fall back
/// to a family track (every early boss shares "boss_1").
/// </summary>
public sealed partial class MusicDirector : Node
{
    public const float FadeSeconds = 2.0f;

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["boss_eye"] = "boss_1", ["boss_worm"] = "boss_1", ["boss_brain"] = "boss_1", ["boss_crypt"] = "boss_1", ["boss_gel"] = "boss_1",
        ["boss_deer"] = "boss_1", ["boss_maw"] = "boss_1", ["boss_bee"] = "boss_queen", ["boss_queen_slime"] = "boss_queen", ["boss_empress"] = "boss_queen",
        ["boss_mech"] = "boss_2", ["boss_fishron"] = "boss_2", ["boss_golem"] = "boss_2", ["boss_pirates"] = "boss_2", ["boss_martians"] = "boss_2",
        ["boss_ooa"] = "boss_2", ["boss_pumpkin"] = "boss_3", ["boss_frost"] = "boss_3", ["boss_cultist"] = "boss_lunar",
        ["solar_eclipse"] = "eclipse", ["goblin_army"] = "invasion", ["pirate_invasion"] = "invasion", ["martian_madness"] = "invasion",
        ["old_ones_army"] = "invasion", ["frost_legion"] = "invasion", ["lunar_events"] = "boss_lunar",
    };

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[2];
    private readonly Dictionary<string, AudioStream?> _streams = new();
    private int _active;
    private string? _current;
    private double _check;
    private GameWorld? _game;
    private PlayerEntity? _player;
    private Tween? _fade;

    /// <summary>The one director (created by the game root at boot).</summary>
    public static MusicDirector? Instance { get; private set; }

    public override void _Ready()
    {
        Name = "MusicDirector";
        Instance = this;
        for (int i = 0; i < 2; i++)
        {
            _players[i] = new AudioStreamPlayer { Bus = AudioBuses.Music, VolumeDb = -80f };
            AddChild(_players[i]);
        }
    }

    /// <summary>Follows a world from now on (the in-game soundtrack).</summary>
    public void Follow(GameWorld game, PlayerEntity player)
    {
        _game = game;
        _player = player;
        _check = 0;
    }

    /// <summary>Plays a fixed track (the title screen) and stops following any world.</summary>
    public void PlayFixed(string track)
    {
        _game = null;
        _player = null;
        Play(track);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.music");
        if (_game == null || _player == null) return;
        _check -= delta;
        if (_check > 0) return;
        _check = 0.75;
        Play(Choose(_game, _player));
    }

    /// <summary>The track that fits the player's situation now.</summary>
    public string Choose(GameWorld game, PlayerEntity player)
    {
        var boss = game.Npcs.All.Where(n => n.Alive && n.IsBoss && n.Def.Boss!.Music != null)
            .OrderBy(n => System.Numerics.Vector3.DistanceSquared(n.Center, player.Center)).FirstOrDefault();
        if (boss != null && Resolve(boss.Def.Boss!.Music!) is { } bossTrack) return bossTrack;
        foreach (var e in game.Events.Active.Reverse())
            if (e.Def.Phase != Strata.Core.Content.Defs.EventPhase.Weather && Resolve(e.Def.Id) is { } eventTrack) return eventTrack; // weather picks variants below

        var session = game.Session;
        bool night = !session.Clock.IsDay;
        if (night && session.Events.Contains("blood_moon")) return "blood_moon";
        var ctx = session.ContextAt(player.FeetCell);
        foreach (var biome in ctx.Biomes.Select(id => session.Content.Biomes.TryGet(id, out var b) ? b : null).Where(b => b?.Music != null).OrderByDescending(b => b!.Priority))
        {
            var music = biome!.Music!;
            if (Variant(game, player, biome, music, ctx.Layer, ctx.Weather, night) is { } variant) return variant;
            if (ctx.Layer is WorldLayer.Underground or WorldLayer.Cavern && biome.Kind == Strata.Core.Content.Defs.BiomeKind.Surface) continue; // the surface theme stays up top
            if (night && Resolve(music.Replace("_day", "") + "_night") is { } nightTrack) return nightTrack;
            if (Resolve(music) is { } track) return track;
        }
        if (LayerVariant(ctx.Layer) is { } layerTrack) return layerTrack;
        return ctx.Layer switch
        {
            WorldLayer.Underground => "underground",
            WorldLayer.Cavern => "cavern",
            WorldLayer.Underworld => "cinderdeep",
            WorldLayer.Space or WorldLayer.Sky => "sky",
            _ => night ? "forest_night" : "forest_day",
        };
    }

    private string? Resolve(string state)
    {
        if (Stream(state) != null) return state;
        return Aliases.TryGetValue(state, out var alias) && Stream(alias) != null ? alias : null;
    }

    private AudioStream? Stream(string track)
    {
        if (_streams.TryGetValue(track, out var s)) return s;
        string path = $"{AudioDiagnostics.MusicDirectory}/{track}.ogg";
        s = AudioDiagnostics.Load(path);
        if (s == null && ResourceLoader.Exists(path))
            GD.PushError($"audio: could not load {path}. Open the project in the Godot editor once (or run `godot --path . --import`) so the music is imported.");
        if (s is AudioStreamOggVorbis ogg) ogg.Loop = true;
        return _streams[track] = s;
    }

    private void Play(string track)
    {
        if (track == _current || Stream(track) is not { } stream) return;
        _current = track;
        var from = _players[_active];
        _active = 1 - _active;
        var to = _players[_active];
        to.Stream = stream;
        to.VolumeDb = -40f;
        to.Play();
        _fade?.Kill(); // a quick second change must not leave two fades fighting over the same player
        var tween = _fade = CreateTween().SetParallel();
        tween.TweenProperty(to, "volume_db", 0f, FadeSeconds);
        tween.TweenProperty(from, "volume_db", -60f, FadeSeconds);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            if (from != _players[_active]) from.Stop();
            to.VolumeDb = 0f;
        }));
    }
}
