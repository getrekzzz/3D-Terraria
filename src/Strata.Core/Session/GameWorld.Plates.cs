namespace Strata.Core.Session;

public sealed partial class GameWorld
{
    /// <summary>Stepping onto a pressure plate sends a signal down its wires (once, until the player steps off).</summary>
    private void StepOnPlates(PlayerEntity p)
    {
        var world = Session.World;
        var plate = world.TileObjects.At(p.FeetCell) is { Def.PressurePlate: true } here ? here
            : world.TileObjects.At(p.FeetCell - Common.Int3.Up) is { Def.PressurePlate: true } below ? below : null;
        long id = plate?.Id ?? -1;
        if (id == p.PressurePlate) return;
        p.PressurePlate = id;
        if (plate != null) Wires.Signal(plate);
    }
}
