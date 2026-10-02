namespace Strata.Core.Gameplay.Town;

public sealed partial class TownSystem
{
    private PylonNetwork? _pylons;

    /// <summary>Biome pylons: who sells them, which work, and teleporting between them (P5-34).</summary>
    public PylonNetwork Pylons => _pylons ??= new PylonNetwork(_world, this);
}
