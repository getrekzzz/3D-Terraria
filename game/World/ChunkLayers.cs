namespace Strata.Game.World;

/// <summary>Visual layers (bit masks) that sort terrain for lights: cameras see every layer.</summary>
public static class ChunkLayers
{
    /// <summary>Everything by default (Godot's layer 1).</summary>
    public const uint Default = 1u;

    /// <summary>Chunks sealed away from the sky: lit and shadowed by torches, skipped by the sun and moon shadow maps.</summary>
    public const uint Sealed = 1u << 1;
}
