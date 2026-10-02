using Godot;
using Strata.Core.Presentation;

namespace Strata.Game.Audio;

/// <summary>
/// Where the listener is shapes what they hear: the Outdoors bus (weather and surface ambience, feeding Ambient) is
/// low-passed as the open sky is lost, so rain and birdsong sound dull and distant from inside a house or a cave
/// mouth; and world sounds on the Sound bus get a reverb that grows with enclosure and depth (caves ring, the deep
/// caverns boom). Driven each frame by the ambience director from the camera's sky exposure and depth.
/// </summary>
public static class AudioEnvironment
{
    public const string Outdoors = "Outdoors";

    private static AudioEffectLowPassFilter? _muffle;
    private static AudioEffectReverb? _reverb;
    private static float _open = 1f, _wet, _room = 0.5f;

    /// <summary>Current reverb wet level on world sounds (probe <c>audio.reverb</c>).</summary>
    public static float ReverbWet => _wet;

    /// <summary>Current low-pass cutoff of the outdoors bus in Hz (probe <c>audio.muffle</c>).</summary>
    public static float OutdoorsCutoff => _muffle?.CutoffHz ?? 20000f;

    /// <summary>Creates the Outdoors bus and the Sound bus reverb once (idempotent).</summary>
    public static void Ensure()
    {
        int index = AudioServer.GetBusIndex(Outdoors);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, Outdoors);
            AudioServer.SetBusSend(index, AudioBuses.Ambient);
            AudioServer.AddBusEffect(index, new AudioEffectLowPassFilter { CutoffHz = 20000f, Resonance = 0.5f });
        }
        _muffle = (AudioEffectLowPassFilter)AudioServer.GetBusEffect(index, 0);
        int sound = AudioServer.GetBusIndex(AudioBuses.Sound);
        if (sound < 0) return;
        for (int i = 0; i < AudioServer.GetBusEffectCount(sound); i++)
            if (AudioServer.GetBusEffect(sound, i) is AudioEffectReverb found)
            {
                _reverb = found;
                return;
            }
        _reverb = new AudioEffectReverb { Wet = 0f, Dry = 1f, RoomSize = 0.5f, Damping = 0.55f, Spread = 0.8f, PredelayMsec = 30f, Hipass = 0.15f };
        AudioServer.AddBusEffect(sound, _reverb);
    }

    /// <param name="outdoor">Smoothed open-sky exposure at the listener (0 enclosed … 1 open).</param>
    /// <param name="depth">Metres below the local surface (negative above it).</param>
    public static void Update(float outdoor, float depth, ReverbSettings settings, float dt)
    {
        _open = Mathf.MoveToward(_open, Mathf.Clamp(outdoor, 0f, 1f), dt * 1.5f);
        if (_muffle != null) _muffle.CutoffHz = Mathf.Lerp(600f, 20000f, _open * _open);
        if (_reverb == null) return;
        float deep = Mathf.Clamp(depth / Mathf.Max(settings.DeepMeters, 1f), 0f, 1f);
        float enclosed = 1f - _open;
        float wet = enclosed * Mathf.Lerp(settings.EnclosedWet, settings.DeepWet, deep);
        float room = Mathf.Lerp(settings.RoomSize, settings.DeepRoomSize, deep);
        _wet = Mathf.MoveToward(_wet, wet, dt * 0.5f);
        _room = Mathf.MoveToward(_room, room, dt * 0.2f);
        _reverb.Wet = _wet;
        _reverb.RoomSize = _room;
    }
}
