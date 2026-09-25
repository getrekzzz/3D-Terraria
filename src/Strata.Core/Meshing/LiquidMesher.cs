using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Meshing;

/// <summary>
/// Meshes liquid volumes: a smooth free surface (corner heights averaged across neighbouring columns)
/// plus side and bottom faces where liquid meets open air. Liquid type travels in custom0 for the shader.
/// </summary>
public static class LiquidMesher
{
    public static void Build(MeshVolume vol, MeshSurface surface)
    {
        if (!vol.Nb.HasLiquids) return;
        const int c0 = Neighborhood.Margin, c1 = Neighborhood.Margin + ChunkMath.Size;
        var liquids = vol.Nb.Liquids;
        Span<int> tri = stackalloc int[3];
        Span<float> h = stackalloc float[4];
        for (int y = c0; y < c1; y++)
            for (int z = c0; z < c1; z++)
                for (int x = c0; x < c1; x++)
                {
                    int i = MeshVolume.Index(x, y, z);
                    byte liquid = liquids[i];
                    if (liquid == 0) continue;
                    var type = LiquidCode.Type(liquid);
                    ulong ids = MeshSurface.PackIds((int)type, (int)type, (int)type, MeshSurface.FlagLiquid);
                    ushort l = vol.Light[i];
                    uint light = MeshSurface.PackLight(LightValue.R(l), LightValue.G(l), LightValue.B(l), LightValue.Sky(l));
                    bool coveredAbove = SameLiquid(liquids[MeshVolume.Index(x, y + 1, z)], type);

                    // Corner heights (x0z0, x1z0, x1z1, x0z1).
                    h[0] = Corner(vol, x, y, z, -1, -1, type, coveredAbove);
                    h[1] = Corner(vol, x, y, z, 1, -1, type, coveredAbove);
                    h[2] = Corner(vol, x, y, z, 1, 1, type, coveredAbove);
                    h[3] = Corner(vol, x, y, z, -1, 1, type, coveredAbove);

                    if (!coveredAbove && !vol.Opaque[MeshVolume.Index(x, y + 1, z)])
                    {
                        Quad(surface, tri, ids, light, Vector3.UnitY,
                            new Vector3(x, y + h[0], z), new Vector3(x + 1, y + h[1], z), new Vector3(x + 1, y + h[2], z + 1), new Vector3(x, y + h[3], z + 1));
                    }

                    SideIfOpen(vol, surface, tri, ids, light, x, y, z, 1, 0, new Vector3(x + 1, y, z), new Vector3(x + 1, y, z + 1), h[1], h[2], type);
                    SideIfOpen(vol, surface, tri, ids, light, x, y, z, -1, 0, new Vector3(x, y, z + 1), new Vector3(x, y, z), h[3], h[0], type);
                    SideIfOpen(vol, surface, tri, ids, light, x, y, z, 0, 1, new Vector3(x + 1, y, z + 1), new Vector3(x, y, z + 1), h[2], h[3], type);
                    SideIfOpen(vol, surface, tri, ids, light, x, y, z, 0, -1, new Vector3(x, y, z), new Vector3(x + 1, y, z), h[0], h[1], type);

                    int below = MeshVolume.Index(x, y - 1, z);
                    if (!vol.Opaque[below] && liquids[below] == 0)
                        Quad(surface, tri, ids, light, -Vector3.UnitY, new Vector3(x, y, z), new Vector3(x + 1, y, z), new Vector3(x + 1, y, z + 1), new Vector3(x, y, z + 1));
                }
    }

    private static bool SameLiquid(byte code, LiquidType type) => code != 0 && LiquidCode.Type(code) == type;

    private static float Height(byte code) => LiquidCode.Level(code) / (float)LiquidCode.MaxLevel;

    private static float Corner(MeshVolume vol, int x, int y, int z, int dx, int dz, LiquidType type, bool coveredAbove)
    {
        if (coveredAbove) return 1f;
        float sum = 0;
        int n = 0;
        for (int k = 0; k < 4; k++)
        {
            int cx = x + ((k & 1) == 1 ? dx : 0), cz = z + ((k & 2) == 2 ? dz : 0);
            int i = MeshVolume.Index(cx, y, cz);
            byte code = vol.Nb.Liquids[i];
            if (!SameLiquid(code, type)) continue;
            if (SameLiquid(vol.Nb.Liquids[MeshVolume.Index(cx, y + 1, cz)], type)) return 1f;
            sum += Height(code);
            n++;
        }
        return n == 0 ? 0.9f : Math.Min(0.94f, sum / n);
    }

    private static void SideIfOpen(MeshVolume vol, MeshSurface surface, Span<int> tri, ulong ids, uint light,
        int x, int y, int z, int dx, int dz, Vector3 a, Vector3 b, float ha, float hb, LiquidType type)
    {
        int n = MeshVolume.Index(x + dx, y, z + dz);
        if (vol.Opaque[n] || SameLiquid(vol.Nb.Liquids[n], type)) return;
        Quad(surface, tri, ids, light, new Vector3(dx, 0, dz), a, b, b + new Vector3(0, hb, 0), a + new Vector3(0, ha, 0));
    }

    private static void Quad(MeshSurface s, Span<int> tri, ulong ids, uint light, Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var color = new Vector4(1, 0, 0, 1);
        int v0 = s.AddVertex(MeshVolume.ToLocalMeters(a), normal, color, ids, light);
        int v1 = s.AddVertex(MeshVolume.ToLocalMeters(b), normal, color, ids, light);
        int v2 = s.AddVertex(MeshVolume.ToLocalMeters(c), normal, color, ids, light);
        int v3 = s.AddVertex(MeshVolume.ToLocalMeters(d), normal, color, ids, light);
        tri[0] = v0; tri[1] = v1; tri[2] = v2;
        SurfaceNetsMesher.OrientClockwise(s, tri, normal);
        s.AddTriangle(tri[0], tri[1], tri[2]);
        tri[0] = v0; tri[1] = v2; tri[2] = v3;
        SurfaceNetsMesher.OrientClockwise(s, tri, normal);
        s.AddTriangle(tri[0], tri[1], tri[2]);
    }
}
