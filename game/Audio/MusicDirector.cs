using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Session;
using Strata.Core.World;

namespace Strata.Game.Audio;

/// <summary>
/// Chooses and cross-fades the soundtrack (<c>res://assets/audio/music/&lt;track&gt;.ogg</c>): a boss's theme while one
/// lives, then an active event's, then the biome around the player (with night, rain, town and underground variants,
/// see MusicDirector.Variants.cs), then the depth layer. Every major boss has its own theme (its music state names the
/// track); a state without a track falls back along <see cref="Aliases"/> to a family track ("boss_1", "boss_2"...).
/// <see cref="Sting"/> plays a short non-looping cue over the soundtrack (the victory sting as a boss falls).
/// </summary>
public sealed partial class MusicDirector : Node
{
    public const float FadeSeconds = 2.0f;

    /// <summary>
    /// Where a music state goes when it has no track of its own, followed until a track exists: the evil bosses share one
    /// theme, the per-boss themes fall back to their family track, events to the invasion or lunar themes.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["boss_worm"] = "boss_evil", ["boss_brain"] = "boss_evil", ["boss_evil"] = "boss_1", ["boss_eye"] = "boss_1", ["boss_crypt"] = "boss_1",
        ["boss_gel"] = "boss_1", ["boss_deer"] = "boss_1", ["boss_maw"] = "boss_1", ["boss_bee"] = "boss_queen", ["boss_queen_slime"] = "boss_queen",
        ["boss_empress"] = "boss_queen", ["boss_mech"] = "boss_2", ["boss_fishron"] = "boss_2", ["boss_golem"] = "boss_2", ["boss_pirates"] = "boss_2",
        ["boss_martians"] = "boss_2", ["boss_ooa"] = "boss_2", ["boss_pumpkin"] = "boss_3", ["boss_frost"] = "boss_3", ["boss_cultist"] = "boss_lunar",
        ["solar_eclipse"] = "eclipse", ["goblin_army"] = "invasion", ["pirate_invasion"] = "invasion", ["martian_madness"] = "invasion",
        ["old_ones_army"] = "invasion", ["frost_legion"] = "invasion", ["lunar_events"] = "boss_lunar",
    };

    /// <summary>Short non-looping cues: <c>res://assets/audio/music/stings/&lt;id&gt;.ogg</c>.</summary>
    public const string StingDirectory = AudioDiagnostics.MusicDirectory + "/stings";

    private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[2];
    private AudioStreamPlayer _sting = null!;
    private readonly Dictionary<string, AudioStream?> _streams = new();
    private readonly Dictionary<string, AudioStream?> _stings = new();
    private int _active;
    private string? _current;
    private double _check;
    private GameWorld? _game;
    private PlayerEntity? _player;
    private Tween? _fade;
    private double _silence;

    /// <summary>True while <see cref="Silence"/> holds the soundtrack back.</summary>
    public bool Silent => _silence > 0;

    /// <summary>The track playing (or fading in), or null while silent (the <c>music.track.&lt;id&gt;</c> probe).</summary>
    public string? Current => _current;

    /// <summary>Stings played so far (the <c>music.stings</c> probe).</summary>
    public int Stings { get; private set; }

    /// <summary>A sting started (its id): for captions.</summary>
    public event System.Action<string>? Stung;

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
        _sting = new AudioStreamPlayer { Name = "Sting", Bus = AudioBuses.Music };
        AddChild(_sting);
    }

    /// <summary>
    /// Plays the short cue <paramref name="id"/> once over whatever the soundtrack does (a boss's fall: over the hush of
    /// <see cref="Silence"/>); a newer sting cuts the last one off.
    /// </summary>
    public void Sting(string id)
    {
        if (!_stings.TryGetValue(id, out var stream))
        {
            stream = AudioDiagnostics.Load($"{StingDirectory}/{id}.ogg");
            if (stream is AudioStreamOggVorbis ogg) ogg.Loop = false;
            _stings[id] = stream;
        }
        if (stream == null) return;
        _sting.Stream = stream;
        _sting.Play();
        Stings++;
        Stung?.Invoke(id);
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
        if (_silence > 0)
        {
            _silence -= delta;
            if (_silence > 0) return;
            _check = 0;
        }
        if (_game == null || _player == null) return;
        _check -= delta;
        if (_check > 0) return;
        _check = 0.75;
        Play(Choose(_game, _player));
    }

    /// <summary>
    /// Fades the soundtrack out over a second and holds it silent for <paramref name="seconds"/> (the hush after a boss
    /// falls, as in Terraria); then the track that fits is chosen again and fades in as usual.
    /// </summary>
    public void Silence(double seconds)
    {
        _silence = System.Math.Max(_silence, seconds);
        _current = null; // whatever fits afterwards fades back in, even the track that was playing
        _fade?.Kill();
        var tween = _fade = CreateTween().SetParallel();
        foreach (var p in _players)
            tween.TweenProperty(p, "volume_db", -60f, 1.0);
        tween.Chain().TweenCallback(Callable.From(() =>
        {
            foreach (var p in _players) p.Stop();
        }));
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
        for (int hop = 0; hop < 4; hop++) // a short chain: boss_worm → boss_evil → boss_1
        {
            if (Stream(state) != null) return state;
            if (!Aliases.TryGetValue(state, out var alias)) return null;
            state = alias;
        }
        return null;
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
