using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>
/// Turns what happens in the world into sound: picks biting blocks by material (or tinking off one too hard), placing,
/// swings and shots by weapon kind, a dry click or a mana fizzle for a press that could not be used, hits and deaths,
/// explosions, bosses rising and falling, events beginning, residents arriving, pick-ups, drinks, the Magic Mirror and the
/// local player's death.
/// </summary>
public sealed partial class SoundEvents : Node
{
    private GameWorld _game = null!;
    private PlayerEntity _local = null!;
    private ulong _lastPlaceFrame;
    private ulong _tinkFrame;
    private ulong _blastFrame;
    private bool _wasAlive = true;
    private int _lastSerial;

    public void Initialize(GameWorld game, PlayerEntity local)
    {
        Name = "SoundEvents";
        _game = game;
        _local = local;
        WireWorld(game);
        WireImpacts(game);
        WireWeapons(game);
        WireCreatures(game);
        WirePlayer(game);
        InitializeLocks();
        WireTown(game);
    }

    public override void _Process(double delta)
    {
        using var profile = Strata.Core.Diagnostics.Metrics.Frame.Time("gd.sounds");
        if (_wasAlive && !_local.Alive) Sfx.Play("player_death");
        _wasAlive = _local.Alive;
    }

    private static Vector3 Cell(Strata.Core.Common.Int3 c) => (new Vector3(c.X, c.Y, c.Z) + new Vector3(0.5f, 0.5f, 0.5f)) * Strata.Core.Common.Units.CellSize;

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
