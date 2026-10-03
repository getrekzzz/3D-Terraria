using System.Collections.Generic;
using Godot;
using Strata.Game.Settings;

namespace Strata.Game.Audio;

/// <summary>
/// Sound effects by id: <c>res://assets/audio/sfx/&lt;id&gt;.wav</c> (several variants <c>&lt;id&gt;_1.wav</c>… are picked at
/// random). Non-positional sounds (the player's own actions) use a small player pool; world sounds are positional. Both
/// pause with the scene tree (the pause menu); menu sounds have their own players that always run.
/// Volumes follow the master and sound settings through the audio buses (see <see cref="AudioBuses"/>).
/// </summary>
public static class Sfx
{
    private const int PoolSize = 12;
    private static readonly Dictionary<string, AudioStream[]> Cache = new();
    private static readonly List<AudioStreamPlayer> Pool = new();
    private static readonly List<AudioStreamPlayer3D> Pool3D = new();
    private static readonly List<AudioStreamPlayer> UiPool = new();
    private static Node? _host;
    private static readonly RandomNumberGenerator Rng = new();

    /// <summary>Attaches the players to a node that lives for the whole session (the game root).</summary>
    public static void Attach(Node host)
    {
        _host = host;
        for (int i = 0; i < PoolSize; i++)
        {
            var p = new AudioStreamPlayer { Bus = AudioBuses.Sound };
            host.AddChild(p);
            Pool.Add(p);
            var p3 = new AudioStreamPlayer3D { Bus = AudioBuses.Sound, UnitSize = 6f, MaxDistance = 48f, AttenuationFilterCutoffHz = 6000f };
            host.AddChild(p3);
            Pool3D.Add(p3);
        }
        for (int i = 0; i < 4; i++)
        {
            var ui = new AudioStreamPlayer { Bus = AudioBuses.Sound, ProcessMode = Node.ProcessModeEnum.Always };
            host.AddChild(ui);
            UiPool.Add(ui);
        }
    }

    /// <summary>Plays a sound without position (UI, the local player's own actions).</summary>
    public static void Play(string id, float volumeDb = 0f, float pitchJitter = 0.06f) => Play(Pool, id, volumeDb, pitchJitter);

    /// <summary>Plays a menu sound (clicks, hovers); these still sound while the game is paused.</summary>
    public static void PlayUi(string id, float volumeDb = 0f) => Play(UiPool, id, volumeDb, 0.03f);

    private static void Play(List<AudioStreamPlayer> pool, string id, float volumeDb, float pitchJitter)
    {
        if (_host == null || Streams(id) is not { Length: > 0 } streams) return;
        var p = Free(pool);
        if (p == null) return;
        p.Stream = streams[Rng.RandiRange(0, streams.Length - 1)];
        p.VolumeDb = volumeDb;
        p.PitchScale = 1f + Rng.RandfRange(-pitchJitter, pitchJitter);
        p.Play();
    }

    /// <summary>Plays a sound at a world position (heard louder close by).</summary>
    public static void PlayAt(string id, Vector3 position, float volumeDb = 0f, float pitchJitter = 0.08f)
    {
        if (_host == null || Streams(id) is not { Length: > 0 } streams) return;
        var p = Free(Pool3D);
        if (p == null) return;
        p.Stream = streams[Rng.RandiRange(0, streams.Length - 1)];
        p.GlobalPosition = position;
        p.VolumeDb = volumeDb;
        p.PitchScale = 1f + Rng.RandfRange(-pitchJitter, pitchJitter);
        p.Play();
    }

    public static bool Has(string id) => Streams(id) is { Length: > 0 };

    private static T? Free<T>(List<T> pool) where T : Node
    {
        foreach (var p in pool)
            if (p is AudioStreamPlayer a && !a.Playing || p is AudioStreamPlayer3D b && !b.Playing) return p;
        return null;
    }

    /// <summary>Sound files that exist but could not be loaded (not imported, or damaged).</summary>
    public static int FailedLoads { get; private set; }

    private static AudioStream[]? Streams(string id)
    {
        if (Cache.TryGetValue(id, out var cached)) return cached;
        var list = new List<AudioStream>();
        Add(list, $"{AudioDiagnostics.SfxDirectory}/{id}.wav");
        for (int i = 1; i <= 8; i++)
        {
            string variant = $"{AudioDiagnostics.SfxDirectory}/{id}_{i}.wav";
            if (!ResourceLoader.Exists(variant)) break;
            Add(list, variant);
        }
        return Cache[id] = list.ToArray();
    }

    private static void Add(List<AudioStream> list, string path)
    {
        if (!ResourceLoader.Exists(path)) return;
        if (ResourceLoader.Load<AudioStream>(path) is { } stream)
        {
            list.Add(stream);
            return;
        }
        FailedLoads++;
        GD.PushError($"audio: could not load {path}. Open the project in the Godot editor once (or run `godot --path . --import`) so the sounds are imported.");
    }
}

/// <summary>The audio buses (Master → Music, Sound, Ambient) and their volumes from the user settings.</summary>
public static class AudioBuses
{
    public const string Music = "Music";
    public const string Sound = "Sound";
    public const string Ambient = "Ambient";

    public static void Setup()
    {
        foreach (var name in new[] { Music, Sound, Ambient })
        {
            if (AudioServer.GetBusIndex(name) >= 0) continue;
            AudioServer.AddBus();
            int index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
        Apply(UserSettings.Current);
        UserSettings.Current.Changed += Apply;
    }

    public static void Apply(UserSettings s)
    {
        AudioDiagnostics.ApplyDevice(s.AudioDevice);
        Set("Master", s.MasterVolume);
        Set(Music, s.MusicVolume);
        Set(Sound, s.SoundVolume);
        Set(Ambient, s.AmbientVolume);
    }

    private static void Set(string bus, float volume)
    {
        int index = AudioServer.GetBusIndex(bus);
        if (index < 0) return;
        AudioServer.SetBusMute(index, volume <= 0.001f);
        AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(Mathf.Max(volume, 0.001f)));
    }
}
