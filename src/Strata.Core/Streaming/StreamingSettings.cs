namespace Strata.Core.Streaming;

/// <summary>How much of the world is kept loaded and meshed around the focus (in chunks).</summary>
public sealed class StreamingSettings
{
    /// <summary>Horizontal radius of meshed chunks.</summary>
    public int MeshRadius { get; init; } = 6;

    /// <summary>Meshed chunks below / above the focus chunk.</summary>
    public int MeshDown { get; init; } = 3;
    public int MeshUp { get; init; } = 3;

    /// <summary>Extra rings kept loaded beyond the unload threshold (hysteresis against thrashing).</summary>
    public int Hysteresis { get; init; } = 2;

    /// <summary>Horizontal radius of columns loaded down from the sky (exact sunlight into shafts near the focus).</summary>
    public int SkyColumnRadius { get; init; } = 2;

    public int MaxLoadsInFlight { get; init; } = 48;
    public int MaxMeshesInFlight { get; init; } = 32;
}
