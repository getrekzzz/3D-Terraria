using System.Collections.Generic;
using Godot;
using Strata.Core.Gameplay.Town;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>
/// Town NPC voice blips (X-05): when a conversation starts, the resident babbles a few short syllables
/// (sfx voice_* variants, tools/audio/sfx_recipes_town.py) at their own pitch (data/config/town.json "emotes.voices"),
/// one per group of dialogue characters; a chat bubble between neighbours gets a quieter pair. One positional player,
/// so blips never pile up.
/// </summary>
public sealed partial class NpcVoice : Node3D
{
    private const float Gap = 0.085f;
    private readonly Dictionary<string, AudioStream[]> _streams = new();
    private readonly RandomNumberGenerator _rng = new();
    private AudioStreamPlayer3D _player = null!;
    private GameWorld _game = null!;
    private TownNpc? _speaker;
    private AudioStream[]? _voice;
    private float _pitch, _timer, _volume;
    private int _left;

    public void Initialize(GameWorld game, Player.InteractionController interaction)
    {
        _game = game;
        Name = "NpcVoice";
        _player = new AudioStreamPlayer3D { Bus = AudioBuses.Sound, UnitSize = 6f, MaxDistance = 32f };
        AddChild(_player);
        interaction.TalkRequested += npc => Speak(npc, 4 + _rng.RandiRange(0, 4), -6f);
        game.Town.Emoted += (npc, emote) =>
        {
            if (emote == "chat" && _left == 0) Speak(npc, 2, -14f);
        };
    }

    /// <summary>Starts a babble of <paramref name="syllables"/> blips from the resident.</summary>
    public void Speak(TownNpc npc, int syllables, float volumeDb)
    {
        var voice = _game.Town.Config.Emotes.VoiceOf(npc.Def.Id);
        _voice = Streams(voice.Sound);
        if (_voice.Length == 0) return;
        _speaker = npc;
        _pitch = voice.Pitch;
        _volume = volumeDb;
        _left = syllables;
        _timer = 0f;
    }

    public override void _Process(double delta)
    {
        if (_left <= 0 || _speaker == null || _voice == null) return;
        _timer -= (float)delta;
        if (_timer > 0f) return;
        var at = _speaker.Position;
        _player.GlobalPosition = new Vector3(at.X, at.Y + 1.5f, at.Z);
        _player.Stream = _voice[_rng.RandiRange(0, _voice.Length - 1)];
        _player.PitchScale = _pitch * _rng.RandfRange(0.92f, 1.1f);
        _player.VolumeDb = _volume;
        _player.Play();
        _left--;
        _timer = Gap + _rng.RandfRange(0f, 0.05f);
    }

    private AudioStream[] Streams(string id)
    {
        if (_streams.TryGetValue(id, out var s)) return s;
        var list = new List<AudioStream>();
        for (int i = 1; i <= 8; i++)
        {
            string path = $"{AudioDiagnostics.SfxDirectory}/{id}_{i}.wav";
            if (!ResourceLoader.Exists(path)) break;
            if (GD.Load<AudioStream>(path) is { } stream) list.Add(stream);
        }
        return _streams[id] = list.ToArray();
    }
}
