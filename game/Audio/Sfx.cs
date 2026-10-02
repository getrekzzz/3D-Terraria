using System.Collections.Generic;
using Godot;
using Strata.Core.Presentation;
using Strata.Game.Settings;

namespace Strata.Game.Audio;

/// <summary>
/// Sound effects by id: <c>res://assets/audio/sfx/&lt;id&gt;.wav</c> (several variants <c>&lt;id&gt;_1.wav</c>… are picked at
/// random). Non-positional sounds (UI, the player's own actions) and world sounds each have a fixed pool of
/// <see cref="Voices"/> players; <see cref="SfxVoicePool"/> picks the player, stealing the oldest voice when the pool is
/// full or an id already plays <see cref="MaxPerId"/> times, so a burst never drops the newest sound. World sounds
/// beyond hearing distance are skipped. Volumes follow the master and sound settings through the audio buses
/// (see <see cref="AudioBuses"/>).
/// </summary>
public static class Sfx
{
    public const int Voices = 24;
    public const int MaxPerId = 3;
    private const float MaxDistance = 48f;
    private static readonly Dictionary<string, Entry?> Cache = new();
    private static readonly AudioStreamPlayer[] Pool = new AudioStreamPlayer[Voices];
    private static readonly AudioStreamPlayer3D[] Pool3D = new AudioStreamPlayer3D[Voices];
    private static readonly SfxVoicePool Voices2D = new(Voices, MaxPerId);
    private static readonly SfxVoicePool Voices3D = new(Voices, MaxPerId);
    private static Node? _host;
    private static readonly RandomNumberGenerator Rng = new();

    /// <summary><see cref="Has"/> as one delegate, for <see cref="ItemSounds.Resolve"/> without a per-call allocation.</summary>
    public static readonly System.Func<string, bool> Exists = Has;

    /// <summary>Attaches the players to a node that lives for the whole session (the game root).</summary>
    public static void Attach(Node host)
    {
        _host = host;
        for (int i = 0; i < Voices; i++)
        {
            var p = new AudioStreamPlayer { Bus = AudioBuses.Sound };
            host.AddChild(p);
            Pool[i] = p;
            var p3 = new AudioStreamPlayer3D { Bus = AudioBuses.Sound, UnitSize = 6f, MaxDistance = MaxDistance, AttenuationFilterCutoffHz = 6000f };
            host.AddChild(p3);
            Pool3D[i] = p3;
        }
        Voices2D.Clear();
        Voices3D.Clear();
    }

    /// <summary>Voices sounding now, across both pools.</summary>
    public static int Busy => Voices2D.Busy(Now) + Voices3D.Busy(Now);

    /// <summary>The most voices one pool had sounding at once (never more than <see cref="Voices"/>).</summary>
    public static int PeakBusy => System.Math.Max(Voices2D.PeakBusy, Voices3D.PeakBusy);

    /// <summary>Voices cut short for a newer sound so far.</summary>
    public static int Stolen => Voices2D.Stolen + Voices3D.Stolen;

    /// <summary>World sounds skipped as too far away to hear.</summary>
    public static int SkippedFar { get; private set; }

    /// <summary>Zeroes the stolen, peak and far counters (the <c>audio reset</c> command; smoke tests).</summary>
    public static void ResetCounters()
    {
        Voices2D.ResetCounters();
        Voices3D.ResetCounters();
        SkippedFar = 0;
    }

    private static double Now => Time.GetTicksUsec() / 1_000_000.0;

    /// <summary>Plays a sound without position (UI, the local player's own actions).</summary>
    public static void Play(string id, float volumeDb = 0f, float pitchJitter = 0.06f)
    {
        if (_host == null || Streams(id) is not { } entry) return;
        int v = Rng.RandiRange(0, entry.Streams.Length - 1);
        float pitch = 1f + Rng.RandfRange(-pitchJitter, pitchJitter);
        var p = Pool[Voices2D.Acquire(entry.Key, Now, entry.Lengths[v] / pitch, Mathf.DbToLinear(volumeDb), out _)];
        p.Stream = entry.Streams[v];
        p.VolumeDb = volumeDb;
        p.PitchScale = pitch;
        p.Play();
    }

    /// <summary>Plays a sound at a world position (heard louder close by); skipped beyond hearing distance.</summary>
    public static void PlayAt(string id, Vector3 position, float volumeDb = 0f, float pitchJitter = 0.08f)
    {
        if (_host == null || Streams(id) is not { } entry) return;
        if (_host.GetViewport()?.GetCamera3D() is { } camera && camera.GlobalPosition.DistanceSquaredTo(position) > MaxDistance * MaxDistance)
        {
            SkippedFar++;
            return;
        }
        int v = Rng.RandiRange(0, entry.Streams.Length - 1);
        float pitch = 1f + Rng.RandfRange(-pitchJitter, pitchJitter);
        var p = Pool3D[Voices3D.Acquire(entry.Key, Now, entry.Lengths[v] / pitch, Mathf.DbToLinear(volumeDb), out _)];
        p.Stream = entry.Streams[v];
        p.GlobalPosition = position;
        p.VolumeDb = volumeDb;
        p.PitchScale = pitch;
        p.Play();
    }

    public static bool Has(string id) => Streams(id) != null;

    /// <summary>Sound files that exist but could not be loaded (not imported, or damaged).</summary>
    public static int FailedLoads { get; private set; }

    /// <summary>One sound id: its pool key, its variants and their lengths in seconds.</summary>
    private sealed record Entry(int Key, AudioStream[] Streams, double[] Lengths);

    private static Entry? Streams(string id)
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
        var lengths = list.ConvertAll(s => System.Math.Max(s.GetLength(), 0.05)).ToArray();
        return Cache[id] = list.Count == 0 ? null : new Entry(Cache.Count + 1, list.ToArray(), lengths);
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
