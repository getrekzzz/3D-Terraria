using System.Numerics;
using Strata.Core.Common;
using Strata.Core.Voxels;

namespace Strata.Core.Meshing;

/// <summary>One polygon of a shape template in unit-cell space, with the cell face it lies on (-1 = interior).</summary>
public readonly record struct ShapePolygon(Vector3[] Points, Vector3 Normal, int BoundaryFace);

/// <summary>Geometry templates for crisp cell shapes (slopes, halves, stairs, panels, platforms, posts), per rotation.</summary>
public static class ShapeGeometry
{
    private static readonly ShapePolygon[][] Cache = Build();

    public static ShapePolygon[] Get(byte shapeCode)
    {
        int shape = (int)ShapeCode.Shape(shapeCode), rot = ShapeCode.Rotation(shapeCode);
        return Cache[Math.Min(shape, ShapeCode.ShapeCount - 1) * 4 + rot];
    }

    /// <summary>The full unit cube (for transparent cells).</summary>
    public static ShapePolygon[] Cube { get; } = Box(Vector3.Zero, Vector3.One).ToArray();

    private static ShapePolygon[][] Build()
    {
        var result = new ShapePolygon[ShapeCode.ShapeCount * 4][];
        for (int shape = 0; shape < ShapeCode.ShapeCount; shape++)
        {
            var baseGeometry = Template((CellShape)shape);
            for (int rot = 0; rot < 4; rot++)
                result[shape * 4 + rot] = baseGeometry.Select(p => Rotate(p, rot)).ToArray();
        }
        return result;
    }

    private static List<ShapePolygon> Template(CellShape shape) => shape switch
    {
        CellShape.HalfBottom => Box(new(0, 0, 0), new(1, 0.5f, 1)),
        CellShape.HalfTop => Box(new(0, 0.5f, 0), new(1, 1, 1)),
        CellShape.Stair => Box(new(0, 0, 0), new(1, 0.5f, 1)).Concat(Box(new(0, 0.5f, 0.5f), new(1, 1, 1))).ToList(),
        CellShape.StairCeiling => Box(new(0, 0.5f, 0), new(1, 1, 1)).Concat(Box(new(0, 0, 0.5f), new(1, 0.5f, 1))).ToList(),
        CellShape.Panel => Box(new(0, 0, 0.75f), new(1, 1, 1)),
        CellShape.Platform => Box(new(0, 0.8f, 0), new(1, 1, 1)),
        CellShape.Post => Box(new(0.375f, 0, 0.375f), new(0.625f, 1, 0.625f)),
        CellShape.Slope => Slope(ceiling: false),
        CellShape.SlopeCeiling => Slope(ceiling: true),
        _ => Box(Vector3.Zero, Vector3.One),
    };

    private static List<ShapePolygon> Slope(bool ceiling)
    {
        // Ramp rising towards +Z: floor, back wall, sloped top, two triangular sides.
        Vector3 F(float x, float y, float z) => ceiling ? new Vector3(x, 1 - y, z) : new Vector3(x, y, z);
        float sy = ceiling ? -1 : 1;
        var list = new List<ShapePolygon>
        {
            new(new[] { F(0, 0, 0), F(1, 0, 0), F(1, 0, 1), F(0, 0, 1) }, new Vector3(0, -sy, 0), ceiling ? Faces.PosY : Faces.NegY),
            new(new[] { F(0, 0, 1), F(1, 0, 1), F(1, 1, 1), F(0, 1, 1) }, Vector3.UnitZ, Faces.PosZ),
            new(new[] { F(0, 0, 0), F(0, 1, 1), F(1, 1, 1), F(1, 0, 0) }, Vector3.Normalize(new Vector3(0, sy, -1)), -1),
            new(new[] { F(0, 0, 0), F(0, 0, 1), F(0, 1, 1) }, -Vector3.UnitX, Faces.NegX),
            new(new[] { F(1, 0, 0), F(1, 1, 1), F(1, 0, 1) }, Vector3.UnitX, Faces.PosX),
        };
        return list;
    }

    private static List<ShapePolygon> Box(Vector3 min, Vector3 max)
    {
        var l = new List<ShapePolygon>(6);
        float x0 = min.X, y0 = min.Y, z0 = min.Z, x1 = max.X, y1 = max.Y, z1 = max.Z;
        l.Add(new(new[] { new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1) }, Vector3.UnitX, x1 >= 1 ? Faces.PosX : -1));
        l.Add(new(new[] { new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0) }, -Vector3.UnitX, x0 <= 0 ? Faces.NegX : -1));
        l.Add(new(new[] { new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0) }, Vector3.UnitY, y1 >= 1 ? Faces.PosY : -1));
        l.Add(new(new[] { new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1) }, -Vector3.UnitY, y0 <= 0 ? Faces.NegY : -1));
        l.Add(new(new[] { new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1) }, Vector3.UnitZ, z1 >= 1 ? Faces.PosZ : -1));
        l.Add(new(new[] { new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0) }, -Vector3.UnitZ, z0 <= 0 ? Faces.NegZ : -1));
        return l;
    }

    /// <summary>Rotates a polygon by quarter turns about the vertical axis through the cell centre.</summary>
    private static ShapePolygon Rotate(ShapePolygon p, int quarterTurns)
    {
        if (quarterTurns == 0) return p;
        Vector3 R(Vector3 v)
        {
            for (int i = 0; i < quarterTurns; i++) v = new Vector3(1 - v.Z, v.Y, v.X);
            return v;
        }
        Vector3 RN(Vector3 n)
        {
            for (int i = 0; i < quarterTurns; i++) n = new Vector3(-n.Z, n.Y, n.X);
            return n;
        }
        int face = p.BoundaryFace;
        if (face >= 0 && Faces.Axis(face) != 1)
        {
            var off = Faces.Offsets[face];
            var rotated = RN(new Vector3(off.X, off.Y, off.Z));
            face = Array.FindIndex(Faces.Offsets, o => o.X == (int)MathF.Round(rotated.X) && o.Z == (int)MathF.Round(rotated.Z) && o.Y == 0);
        }
        return new ShapePolygon(p.Points.Select(R).ToArray(), RN(p.Normal), face);
    }
}
