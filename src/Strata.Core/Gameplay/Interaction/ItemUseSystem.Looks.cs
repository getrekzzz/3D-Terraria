using Strata.Core.Content.Specs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>Hair dyes: the character's hair takes the dye (the remover washes it out); the bottle is used up.</summary>
    private UseOutcome DyeHair(PlayerEntity p, HairDyeParams dye)
    {
        var look = p.State.Look;
        string? id = dye.Mode == HairDyeMode.Remove ? null : p.State.Inventory.Held.Item!.Id;
        if (look.HairDye == id) return UseOutcome.None;
        look.HairDye = id;
        if (p.State.Inventory.Held.Item!.Consumable) ConsumeHeld(p);
        p.State.RaiseLookChanged();
        return UseOutcome.Consumed;
    }
}
