using Strata.Core.Common;

namespace Strata.Core.WorldGen.Fields;

/// <summary>
/// Evaluates an expensive 3D field on a coarse lattice (every <see cref="Step"/> cells) over a box and
/// trilinearly interpolates per cell — ~64× fewer noise evaluations for smooth fields.
/// </summary>
public sealed class LatticeSampler
{
    public const int DefaultStep = 4;

    private readonly float[] _values;
    private readonly int _nx, _ny, _nz;
    private readonly Int3 _origin;

    public int Step { get; }

    public LatticeSampler(Int3 origin, Int3 size, Func<int, int, int, float> field, int step = DefaultStep)
    {
        Step = step;
        _origin = origin;
        _nx = size.X / Step + 2;
        _ny = size.Y / Step + 2;
        _nz = size.Z / Step + 2;
        _values = new float[_nx * _ny * _nz];
        for (int j = 0; j < _ny; j++)
            for (int k = 0; k < _nz; k++)
                for (int i = 0; i < _nx; i++)
                    _values[(j * _nz + k) * _nx + i] = field(origin.X + i * Step, origin.Y + j * Step, origin.Z + k * Step);
    }

    /// <summary>Interpolated value at a world cell inside the sampled box.</summary>
    public float Sample(int x, int y, int z)
    {
        int lx = x - _origin.X, ly = y - _origin.Y, lz = z - _origin.Z;
        int i = lx / Step, j = ly / Step, k = lz / Step;
        float invStep = 1f / Step;
        float tx = (lx - i * Step) * invStep, ty = (ly - j * Step) * invStep, tz = (lz - k * Step) * invStep;
        int i1 = Math.Min(i + 1, _nx - 1), j1 = Math.Min(j + 1, _ny - 1), k1 = Math.Min(k + 1, _nz - 1);
        float c000 = V(i, j, k), c100 = V(i1, j, k), c010 = V(i, j1, k), c110 = V(i1, j1, k);
        float c001 = V(i, j, k1), c101 = V(i1, j, k1), c011 = V(i, j1, k1), c111 = V(i1, j1, k1);
        float c00 = c000 + (c100 - c000) * tx, c10 = c010 + (c110 - c010) * tx;
        float c01 = c001 + (c101 - c001) * tx, c11 = c011 + (c111 - c011) * tx;
        float c0 = c00 + (c10 - c00) * ty, c1 = c01 + (c11 - c01) * ty;
        return c0 + (c1 - c0) * tz;
    }

    private float V(int i, int j, int k) => _values[(j * _nz + k) * _nx + i];

    /// <summary>
    /// Writes interpolated values for every cell of a cubic box of edge <paramref name="size"/> starting at the
    /// sampler origin into <paramref name="dest"/> (layout: (y * size + z) * size + x). Much faster than
    /// per-cell <see cref="Sample"/> calls: interpolation is incremental within each lattice block.
    /// </summary>
    public void FillDense(float[] dest, int size)
    {
        float inv = 1f / Step;
        for (int j = 0; j * Step < size; j++)
            for (int k = 0; k * Step < size; k++)
                for (int i = 0; i * Step < size; i++)
                {
                    int i1 = Math.Min(i + 1, _nx - 1), j1 = Math.Min(j + 1, _ny - 1), k1 = Math.Min(k + 1, _nz - 1);
                    float c000 = V(i, j, k), c100 = V(i1, j, k), c010 = V(i, j1, k), c110 = V(i1, j1, k);
                    float c001 = V(i, j, k1), c101 = V(i1, j, k1), c011 = V(i, j1, k1), c111 = V(i1, j1, k1);
                    int yEnd = Math.Min(Step, size - j * Step), zEnd = Math.Min(Step, size - k * Step), xEnd = Math.Min(Step, size - i * Step);
                    for (int dy = 0; dy < yEnd; dy++)
                    {
                        float ty = dy * inv;
                        float a00 = c000 + (c010 - c000) * ty, a10 = c100 + (c110 - c100) * ty;
                        float a01 = c001 + (c011 - c001) * ty, a11 = c101 + (c111 - c101) * ty;
                        for (int dz = 0; dz < zEnd; dz++)
                        {
                            float tz = dz * inv;
                            float left = a00 + (a01 - a00) * tz, right = a10 + (a11 - a10) * tz;
                            float step = (right - left) * inv;
                            int row = ((j * Step + dy) * size + (k * Step + dz)) * size + i * Step;
                            float v = left;
                            for (int dx = 0; dx < xEnd; dx++, v += step) dest[row + dx] = v;
                        }
                    }
                }
    }
}
