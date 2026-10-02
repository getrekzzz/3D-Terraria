using Strata.Core.Voxels;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class WorldEditor
{
    /// <summary>
    /// Commits an edit prepared elsewhere (actuators switching blocks off and on) and schedules remeshing, without
    /// drops or support checks: attached objects stay, as with Terraria's inactive blocks.
    /// </summary>
    public void Apply(EditBatch batch) => Commit(batch);
}
