using Godot;

namespace Strata.Game.Ambience;

/// <summary>
/// The tileable noise behind the cloud layer, the moon's maria and the galactic band: three seamless noise fields
/// packed into one RGBA texture (r broad billows, g lumpy cells, b fine wisps), published as the global
/// <c>strata_cloud_noise</c> shared by the sky and every lit surface (cloud shadows).
/// </summary>
public static class SkyNoise
{
    private const int Size = 512;

    public static ImageTexture Build(int seed)
    {
        var broad = Field(new FastNoiseLite
        {
            Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 5f / Size,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 5, FractalGain = 0.5f,
        });
        var cells = Field(new FastNoiseLite
        {
            Seed = seed + 1, NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 14f / Size,
            CellularReturnType = FastNoiseLite.CellularReturnTypeEnum.Distance, FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
            FractalOctaves = 3, FractalGain = 0.45f,
        });
        var wisps = Field(new FastNoiseLite
        {
            Seed = seed + 2, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 24f / Size,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 4, FractalGain = 0.55f,
        });
        var image = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Cellular distance is small at cell centres: invert it into puffy lumps.
                image.SetPixel(x, y, new Color(broad.GetPixel(x, y).R, 1f - cells.GetPixel(x, y).R, wisps.GetPixel(x, y).R, 1f));
            }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static Image Field(FastNoiseLite noise) => noise.GetSeamlessImage(Size, Size, false, false, 0.1f, true);
}
