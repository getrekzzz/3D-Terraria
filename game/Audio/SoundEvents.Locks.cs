namespace Strata.Game.Audio;

public sealed partial class SoundEvents
{
    /// <summary>A key turning in a chest's lock, or the dull clack of a lock (or sealed door, or keyless altar) that
    /// holds, for the local player.</summary>
    private void InitializeLocks()
    {
        _game.Interaction.Unlocked += (p, obj) =>
        {
            if (p == _local) Sfx.PlayAt("unlock", Cell(obj.Anchor), -2f);
        };
        _game.Interaction.Refused += (p, _) =>
        {
            if (p == _local) Sfx.Play("locked", -3f);
        };
    }
}
