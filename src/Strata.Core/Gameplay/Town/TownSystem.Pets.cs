using System.Numerics;
using Strata.Core.Content.Defs;
using Strata.Core.Session;

namespace Strata.Core.Gameplay.Town;

/// <summary>
/// Town pets (P5-20, Terraria's licensed Cat, Dog and Bunny): they move into a free house when their license flag is
/// set, wander like residents, cannot be hurt or targeted, never count toward the population and sell nothing.
/// Interacting pets them: they stop for a moment and the game shows a burst of hearts.
/// </summary>
public sealed partial class TownSystem
{
    private const int PetTicks = 90;

    /// <summary>A pet was petted (hearts, sound).</summary>
    public event Action<TownNpc, PlayerEntity>? Petted;

    /// <summary>How many times pets were petted (probe town.petted).</summary>
    public int PetCount { get; private set; }

    /// <summary>Pets a town pet within talking range; false for anyone else or out of range.</summary>
    public bool PetAnimal(TownNpc pet, PlayerEntity p)
    {
        if (pet.Def.Kind != TownNpcKind.Pet || !pet.Alive || Vector3.Distance(p.Center, pet.Center) > Config.TalkRange) return false;
        pet.TalkingTo = p;
        pet.TalkTicks = PetTicks;
        pet.Path = null;
        PetCount++;
        Petted?.Invoke(pet, p);
        return true;
    }
}
