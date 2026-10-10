using Godot;
using Strata.Core.Gameplay.Interaction;
using Strata.Core.Gameplay.Items;

namespace Strata.Game.Player;

public sealed partial class InteractionController
{
    /// <summary>
    /// Smart digging: looking steeply down with a pickaxe targets the ground under the middle of the feet (instead of
    /// wherever the over-the-shoulder ray lands), so holding the button digs a shaft straight down that you drop into.
    /// </summary>
    private Target? DigDownTarget(Vector3 dir)
    {
        if (!Settings.UserSettings.Current.SmartDig || dir.Y > -0.8f) return null;
        var held = Entity.State.Inventory.Held;
        if (held.IsEmpty || !Game.Uses[held.Item!].Has(ItemAction.Mine)) return null;
        if (Target != null)
        {
            // Only when the ray lands next to the feet anyway (not at a ledge far below).
            var hit = Target.HitCells * Strata.Core.Common.Units.CellSize;
            var feet = Entity.Position;
            if (new Vector2(hit.X - feet.X, hit.Z - feet.Z).Length() > 1.6f || Target.Object != null) return null;
        }
        return Targeting.UnderFeet(Game.Session.World, Entity.Position);
    }

    /// <summary>Tries to open (or greet) a disguised creature within reach; it wakes. True when it did.</summary>
    private bool OpenDisguised(Strata.Core.Gameplay.Entities.Npc npc)
    {
        float reach = ItemUseSystem.ReachMeters(Entity, null);
        if (Game.Interaction.Interact(Entity, npc, reach) != InteractOutcome.Unmasked) return false;
        Interacted?.Invoke(InteractOutcome.Unmasked);
        return true;
    }
}
