using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Meshing;

/// <summary>
/// Meshes cells that are not part of the surface-nets boundary: shaped crisp cells (slopes, halves,
/// stairs, panels, platforms, posts) into the opaque surface, and see-through cubes (glass, leaves,
/// crystal, webs) into the transparent surface. Faces against full opaque neighbours are culled.
/// </summary>
public static class CellMesher
{
    public static void Build(MeshVolume vol, MeshSurface opaque, MeshSurface transparent, CollisionBuilder collision)
    {
        const int c0 = Neighborhood.Margin, c1 = Neighborhood.Margin + ChunkMath.Size;
        var tables = vol.Tables;
        var blocks = vol.Nb.Blocks;
        var shapes = vol.Nb.HasShapes ? vol.Nb.Shapes : null;
        for (int y = c0; y < c1; y++)
            for (int z = c0; z < c1; z++)
                for (int x = c0; x < c1; x++)
                {
                    int i = MeshVolume.Index(x, y, z);
                    ushort block = blocks[i];
                    var cls = tables.Class[block];
                    if (cls == CellClass.Empty) continue;
                    byte shape = shapes?[i] ?? 0;
                    if (shape != 0)
                    {
                        bool platform = ShapeCode.Shape(shape) == CellShape.Platform;
                        EmitPolygons(vol, x, y, z, ShapeGeometry.Get(shape), block, opaque, collision, tables.Solid[block], platform, sameBlockCull: false);
                    }
                    else if (cls == CellClass.Transparent)
                    {
                        EmitPolygons(vol, x, y, z, ShapeGeometry.Cube, block, transparent, collision, tables.Solid[block], platform: false, sameBlockCull: true);
                    }
                }
    }

    private static void EmitPolygons(MeshVolume vol, int x, int y, int z, ShapePolygon[] polygons, ushort block,
        MeshSurface surface, CollisionBuilder collision, bool solid, bool platform, bool sameBlockCull)
    {
        int self = MeshVolume.Index(x, y, z);
        ushort material = vol.Tables.Surface[block];
        ulong ids = MeshSurface.PackIds(material, material, material, MeshSurface.FlagCrisp);
        var origin = new Vector3(x, y, z);
        Span<int> tri = stackalloc int[3];
        foreach (var poly in polygons)
        {
            int lightIndex = self;
            if (poly.BoundaryFace >= 0)
            {
                var o = Faces.Offsets[poly.BoundaryFace];
                int n = MeshVolume.Index(x + o.X, y + o.Y, z + o.Z);
                if (vol.Opaque[n]) continue;
                if (sameBlockCull && vol.Nb.Blocks[n] == block && (!vol.Nb.HasShapes || vol.Nb.Shapes[n] == 0)) continue;
                lightIndex = n;
            }
            ushort l = vol.Light[lightIndex];
            uint light = MeshSurface.PackLight(LightValue.R(l), LightValue.G(l), LightValue.B(l), LightValue.Sky(l));
            int first = -1;
            for (int k = 0; k < poly.Points.Length; k++)
            {
                int v = surface.AddVertex(MeshVolume.ToLocalMeters(origin + poly.Points[k]), poly.Normal, new Vector4(1, 0, 0, 1), ids, light);
                if (k == 0) first = v;
            }
            for (int k = 1; k + 1 < poly.Points.Length; k++)
            {
                tri[0] = first;
                tri[1] = first + k;
                tri[2] = first + k + 1;
                SurfaceNetsMesher.OrientClockwise(surface, tri, poly.Normal);
                surface.AddTriangle(tri[0], tri[1], tri[2]);
                if (solid) collision.AddTriangle(surface.Positions[tri[0]], surface.Positions[tri[1]], surface.Positions[tri[2]], platform);
            }
        }
    }
}
