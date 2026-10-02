using System.Linq;
using Godot;
using Strata.Core.Content.Defs;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Session;

namespace Strata.Game.Audio;

/// <summary>The local player's own moments: pick-ups, catches, the Magic Mirror (death is watched in _Process).</summary>
public sealed partial class SoundEvents
{
    private void WirePlayer(GameWorld game)
    {
        game.ItemPickedUp += (p, stack) =>
        {
            if (p == _local) Sfx.Play(stack.Item!.HasTag("coin") ? "coins" : "pickup");
        };
        game.ItemUse.Caught += (p, _) =>
        {
            if (p == _local) Sfx.Play("splash");
        };
        game.ItemUse.Recalled += (p, _) =>
        {
            if (p == _local) Sfx.Play("teleport");
        };
    }
}
