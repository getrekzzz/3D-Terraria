using Strata.Core.ThirdParty;

namespace Strata.Core.Determinism;

/// <summary>Kind of fractal used by a <see cref="NoiseField"/>.</summary>
public enum NoiseFractal
{
    None,
    Fbm,
    Ridged,
    PingPong,
}

/// <summary>
/// Immutable, thread-safe wrapper over a configured FastNoiseLite instance.
/// Frequencies are expressed as "features per cell" (1 / wavelength in cells).
/// Output is roughly in [-1, 1].
/// </summary>
public sealed class NoiseField
{
    private readonly FastNoiseLite _noise;

    public NoiseField(
        int seed,
        float wavelengthCells,
        NoiseFractal fractal = NoiseFractal.Fbm,
        int octaves = 4,
        float lacunarity = 2f,
        float gain = 0.5f,
        bool cellular = false)
    {
        if (wavelengthCells <= 0) throw new ArgumentOutOfRangeException(nameof(wavelengthCells));
        _noise = new FastNoiseLite(seed);
        _noise.SetNoiseType(cellular ? FastNoiseLite.NoiseType.Cellular : FastNoiseLite.NoiseType.OpenSimplex2);
        _noise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXZPlanes);
        _noise.SetFrequency(1f / wavelengthCells);
        _noise.SetFractalType(fractal switch
        {
            NoiseFractal.Fbm => FastNoiseLite.FractalType.FBm,
            NoiseFractal.Ridged => FastNoiseLite.FractalType.Ridged,
            NoiseFractal.PingPong => FastNoiseLite.FractalType.PingPong,
            _ => FastNoiseLite.FractalType.None,
        });
        _noise.SetFractalOctaves(octaves);
        _noise.SetFractalLacunarity(lacunarity);
        _noise.SetFractalGain(gain);
        if (cellular)
        {
            _noise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
            _noise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance2Sub);
        }
    }

    public static NoiseField Create(SeedTree seeds, string name, float wavelengthCells,
        NoiseFractal fractal = NoiseFractal.Fbm, int octaves = 4, float gain = 0.5f) =>
        new(seeds.IntSeed(name), wavelengthCells, fractal, octaves, 2f, gain);

    public float Sample(float x, float z) => _noise.GetNoise(x, z);

    public float Sample(float x, float y, float z) => _noise.GetNoise(x, y, z);

    /// <summary>Sample remapped to [0, 1].</summary>
    public float Sample01(float x, float z) => _noise.GetNoise(x, z) * 0.5f + 0.5f;

    public float Sample01(float x, float y, float z) => _noise.GetNoise(x, y, z) * 0.5f + 0.5f;
}
