using System.Collections.Generic;
using System.Linq;
using Godot;
using Strata.Core.Diagnostics;

namespace Strata.Game.Audio;

/// <summary>
/// Makes "no sound" diagnosable: which driver and output device the engine uses, how the buses are set, whether the
/// sound files load, and a test tone synthesised in code (it plays even when every imported file failed).
/// </summary>
public static class AudioDiagnostics
{
    public const string SfxDirectory = "res://assets/audio/sfx";
    public const string MusicDirectory = "res://assets/audio/music";
    public const string AmbientDirectory = "res://assets/audio/ambient";
    private static AudioStreamPlayer? _tester;

    /// <summary>One line: driver, device, mix rate and latency.</summary>
    public static string Device() =>
        $"{AudioServer.GetDriverName()} · {AudioServer.OutputDevice} · {AudioServer.GetMixRate():0} Hz · {AudioServer.GetOutputLatency() * 1000:0} ms";

    /// <summary>Each bus with its volume and mute state.</summary>
    public static string Buses()
    {
        var parts = new List<string>();
        for (int i = 0; i < AudioServer.BusCount; i++)
            parts.Add($"{AudioServer.GetBusName(i)} {AudioServer.GetBusVolumeDb(i):0.#} dB{(AudioServer.IsBusMute(i) ? " (muted)" : "")}");
        return string.Join(", ", parts);
    }

    /// <summary>True when the engine runs without an audio device (nothing can be heard).</summary>
    public static bool Silent => AudioServer.GetDriverName() == "Dummy";

    /// <summary>Logs the audio setup once at startup (the first thing to read when a player hears nothing).</summary>
    /// <param name="automated">Scripted runs (smoke tests, screenshots) are expected to run silent.</param>
    public static void LogStartup(ILog log, bool automated = false)
    {
        log.Info("audio", $"Output {Device()}. Devices: {string.Join(" | ", AudioServer.GetOutputDeviceList())}.");
        log.Info("audio", $"Buses: {Buses()}.");
        if (Silent && automated) log.Info("audio", "Silent audio driver (scripted run).");
        else if (Silent) log.Error("audio", "The audio driver is 'Dummy': no sound will play. Check that an output device is connected and enabled in Windows.");
        foreach (var path in new[] { $"{SfxDirectory}/ui_click.wav", $"{MusicDirectory}/title.ogg" })
            if (Load(path) == null) log.Error("audio", $"Could not load {path}. Open the project in the Godot editor once (or run `godot --path . --import`) so the audio files are imported.");
    }

    /// <summary>Tries every sound, music and ambience file; returns how many loaded and the names of those that did not.</summary>
    public static (int Loaded, int Total, List<string> Failed) ProbeFiles()
    {
        var failed = new List<string>();
        int loaded = 0, total = 0;
        foreach (var (dir, ext) in new[] { (SfxDirectory, ".wav"), (MusicDirectory, ".ogg"), (AmbientDirectory, ".ogg") })
        {
            foreach (var name in Files(dir, ext))
            {
                total++;
                if (Load($"{dir}/{name}") != null) loaded++;
                else failed.Add(name);
            }
        }
        return (loaded, total, failed);
    }

    /// <summary>Source files in a directory, also in exported builds where only their .import/.remap stubs are listed.</summary>
    public static IEnumerable<string> Files(string dir, string ext) =>
        DirAccess.GetFilesAt(dir)
            .Select(f => f.EndsWith(".import") ? f[..^7] : f.EndsWith(".remap") ? f[..^6] : f)
            .Where(f => f.EndsWith(ext))
            .Distinct()
            .OrderBy(f => f);

    public static AudioStream? Load(string path) => ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;

    /// <summary>Selects an output device by name ("Default" follows the system's default device).</summary>
    public static void ApplyDevice(string device)
    {
        if (string.IsNullOrEmpty(device)) device = "Default";
        if (AudioServer.OutputDevice == device) return;
        if (!AudioServer.GetOutputDeviceList().Contains(device))
        {
            GD.PushWarning($"audio: output device '{device}' is not available; using the default device.");
            device = "Default";
        }
        AudioServer.OutputDevice = device;
    }

    /// <summary>Plays a two-note chime generated in code straight to the master bus, then a game sound through the sound bus.</summary>
    public static void PlayTest(Node host)
    {
        if (_tester == null || !GodotObject.IsInstanceValid(_tester))
        {
            _tester = new AudioStreamPlayer { Name = "AudioTest", Bus = "Master", Stream = Chime() };
            host.AddChild(_tester);
        }
        _tester.Play();
        host.GetTree().CreateTimer(0.9).Timeout += () => Sfx.Play("ui_click");
    }

    /// <summary>C5 then G5 with a soft attack and decay, 16-bit mono PCM at 44.1 kHz.</summary>
    public static AudioStreamWav Chime()
    {
        const int rate = 44100;
        var notes = new[] { (523.25f, 0.28f), (783.99f, 0.42f) };
        int count = notes.Sum(n => (int)(n.Item2 * rate));
        var data = new byte[count * 2];
        int i = 0;
        foreach (var (freq, seconds) in notes)
        {
            int n = (int)(seconds * rate);
            for (int s = 0; s < n; s++, i++)
            {
                float t = s / (float)rate;
                float env = Mathf.Min(1f, t / 0.01f) * Mathf.Exp(-t * 5f);
                float v = (Mathf.Sin(Mathf.Tau * freq * t) + 0.25f * Mathf.Sin(Mathf.Tau * freq * 2f * t)) * 0.3f * env;
                short sample = (short)(Mathf.Clamp(v, -1f, 1f) * short.MaxValue);
                data[i * 2] = (byte)(sample & 0xff);
                data[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }
}
