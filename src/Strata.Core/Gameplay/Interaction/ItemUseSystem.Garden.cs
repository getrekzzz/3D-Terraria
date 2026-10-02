using Strata.Core.Common;
using Strata.Core.Content.Specs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>
    /// Grass seeds (Terraria's Grass, Jungle, Mushroom, Corrupt, Crimson and Hallowed Seeds): the aimed block, when it
    /// is one the seeds take on and nothing solid covers it, becomes the grass block; one seed is used up.
    /// </summary>
    private UseOutcome PlantGrass(PlayerEntity p, PlantGrassParams cfg, Target target)
    {
        var blocks = _world.Session.Content.Blocks;
        var world = _world.Session.World;
        var cell = target.Cell;
        ushort id = world.GetBlock(cell);
        if (id == 0 || !blocks.TryGet(cfg.Grass, out var grass) || Array.IndexOf(cfg.On, blocks[id].Id) < 0) return UseOutcome.Blocked;
        ushort above = world.GetBlock(cell + Int3.Up);
        if (above != 0 && above != Voxels.VoxelWorld.Unknown && blocks[above].IsSolid) return UseOutcome.Blocked;
        _world.Editor.SetBlock(cell, (ushort)grass.RuntimeId, world.GetShape(cell.X, cell.Y, cell.Z), world.GetFlags(cell.X, cell.Y, cell.Z));
        ConsumeHeld(p, 1);
        return UseOutcome.PlacedBlock;
    }
}
