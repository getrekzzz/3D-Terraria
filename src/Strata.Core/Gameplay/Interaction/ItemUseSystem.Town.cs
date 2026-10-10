using Strata.Core.Content.Specs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Interaction;

public sealed partial class ItemUseSystem
{
    /// <summary>
    /// A town pet license (Terraria's Cat, Dog and Bunny Licenses): sets its world flag, which the pet's arrival waits
    /// for, and is used up. A license whose pet is already licensed is refused and kept.
    /// </summary>
    private UseOutcome UseLicense(PlayerEntity p, LicenseParams cfg)
    {
        if (_world.Progression.IsSet(cfg.Flag)) return Refuse("This license has already been used.");
        _world.Progression.Set(cfg.Flag);
        ConsumeHeld(p, 1);
        return UseOutcome.Consumed;
    }
}
