using Godot;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>
/// The fishing line's sounds: the bobber yanked under when a fish bites the local player's line (fish_bite at the
/// bobber), so a bite is heard as well as seen. Watches the few live projectiles for a bobber turning to biting.
/// </summary>
public sealed partial class FishingSounds : Node
{
    private GameWorld _game = null!;
    private PlayerEntity _local = null!;
    private long _bitingId = -1;

    public void Initialize(GameWorld game, PlayerEntity local)
    {
        Name = "FishingSounds";
        _game = game;
        _local = local;
    }

    public override void _Process(double delta)
    {
        long biting = -1;
        var all = _game.Projectiles.All;
        for (int i = 0; i < all.Count; i++)
        {
            var p = all[i];
            if (!p.Biting || p.Owner != _local) continue;
            biting = p.Id;
            if (biting != _bitingId)
            {
                var at = p.Position;
                Sfx.PlayAt("fish_bite", new Vector3(at.X, at.Y, at.Z), 2f, 0.1f);
            }
            break;
        }
        _bitingId = biting;
    }
}
