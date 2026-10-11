using Godot;

namespace Strata.Game.Audio;

/// <summary>
/// Seamless ambience loops (<c>res://assets/audio/ambience/&lt;id&gt;.ogg</c>, synthesised by tools/audio/ambience.py):
/// rain, storm, wind, blizzard, sandstorm, and the biome and cave beds. Each loop plays on a player that fades its
/// volume rather than starting and stopping, and stops mixing once silent.
/// </summary>
public static class AmbienceLoops
{
    public const string Directory = AudioDiagnostics.AmbienceDirectory;

    /// <summary>The loop (the engine's resource cache shares it between players), set to loop seamlessly.</summary>
    public static AudioStream? Load(string id)
    {
        var s = AudioDiagnostics.Load($"{Directory}/{id}.ogg");
        if (s is AudioStreamOggVorbis ogg) ogg.Loop = true;
        return s;
    }

    /// <summary>Creates a looping player for <paramref name="id"/> on <paramref name="bus"/>, silent until faded in.</summary>
    public static Fader Player(Node parent, string id, string bus)
    {
        var player = new AudioStreamPlayer { Name = id, Bus = bus, Stream = Load(id), VolumeDb = -80f };
        parent.AddChild(player);
        return new Fader(player);
    }

    /// <summary>A looping player whose loudness eases toward a target (linear 0–1); it plays only while audible.</summary>
    public sealed class Fader
    {
        private readonly AudioStreamPlayer _player;
        private float _level;

        public Fader(AudioStreamPlayer player) => _player = player;

        public float Level => _level;

        public void Drive(float target, float dt, float rate = 0.6f)
        {
            _level = Mathf.MoveToward(_level, Mathf.Clamp(target, 0f, 1.5f), dt * rate);
            bool audible = _level > 0.002f && _player.Stream != null;
            if (audible != _player.Playing)
            {
                if (audible) _player.Play();
                else _player.Stop();
            }
            if (audible) _player.VolumeDb = Mathf.LinearToDb(_level);
        }
    }
}
