using Strata.Core.Gameplay.Items;
using Strata.Core.Gameplay.Mounts;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>Using a mount item summons the mount (or dismounts from it); see <see cref="MountRules"/>.</summary>
    private UseOutcome ToggleMount(PlayerEntity p, ItemUseInfo info)
    {
        MountRules.Toggle(_world, p, info.Item, info.Ride!);
        return UseOutcome.Mounted;
    }
}
