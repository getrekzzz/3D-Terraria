using Strata.Core.Common;
using Strata.Core.Determinism;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Features;

namespace Strata.Core.WorldGen.Chunk;

/// <summary>Shared routines that apply plan features to the padded chunk volume.</summary>
public static class FeatureCarver
{
    /// <summary>Iterates padded cells inside a feature's bounds.</summary>
    public static void ForEachCell(ChunkGenContext ctx, Aabb3i bounds, Action<int, int, int, int> visit)
    {
        var box = new Aabb3i(ctx.Origin, ctx.Origin + Int3.One * ChunkGenContext.Size).Intersection(bounds);
        if (box.IsEmpty) return;
        for (int y = box.Min.Y; y < box.Max.Y; y++)
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                    visit(x, y, z, ctx.WorldIndex(x, y, z));
    }

    /// <summary>Carves a capsule tunnel (with optional liquid fill and wall lining).</summary>
    public static void Capsule(ChunkGenContext ctx, CapsuleFeature f, ushort lining = 0)
    {
        var box = new Aabb3i(ctx.Origin, ctx.Origin + Int3.One * ChunkGenContext.Size).Intersection(f.Bounds);
        if (box.IsEmpty) return;
        var a = f.A;
        var b = f.B;
        float inv = 1f / f.VerticalScale;
        float by = (b.Y - a.Y) * inv + a.Y;
        float maxR = MathF.Max(f.RadiusA, f.RadiusB) + (lining != 0 ? 1.3f : 0f);
        var p = ctx.Palette;
        for (int y = box.Min.Y; y < box.Max.Y; y++)
        {
            float cy = (y + 0.5f - a.Y) * inv + a.Y;
            for (int z = box.Min.Z; z < box.Max.Z; z++)
                for (int x = box.Min.X; x < box.Max.X; x++)
                {
                    float dist = DetMath.DistanceToSegment(x + 0.5f, cy, z + 0.5f, a.X, a.Y, a.Z, b.X, by, b.Z, out float t);
                    if (dist >= maxR) continue;
                    int i = ctx.WorldIndex(x, y, z);
                    if (ctx.IsStructure(i)) continue;
                    float r = DetMath.Lerp(f.RadiusA, f.RadiusB, t);
                    if (dist < r)
                    {
                        if (ctx.Blocks[i] == p.Bedrock) continue;
                        ctx.Blocks[i] = p.Air;
                        if (f.Fill != LiquidType.None)
                        {
                            float centreY = DetMath.Lerp(a.Y, b.Y, t);
                            float surface = centreY - r * f.VerticalScale + 2f * r * f.VerticalScale * f.FillFraction;
                            if (y + 0.5f < surface) ctx.EnsureLiquids()[i] = LiquidCode.Full(f.Fill);
                        }
                    }
                    else if (lining != 0 && p.IsNatural(ctx.Blocks[i]))
                    {
                        ctx.Blocks[i] = lining;
                    }
                }
        }
    }

    /// <summary>Stamps a structure blueprint (ops in order) into the padded volume.</summary>
    public static void Stamp(ChunkGenContext ctx, Structures.Blueprint blueprint)
    {
        var padded = new Aabb3i(ctx.Origin, ctx.Origin + Int3.One * ChunkGenContext.Size);
        if (!padded.Intersects(blueprint.Bounds)) return;
        byte flag = (byte)blueprint.Flags;
        var ops = blueprint.Ops;
        foreach (int opIndex in blueprint.OpIndicesFor(ctx.Coord))
        {
            var op = ops[opIndex];
            if (!padded.Intersects(op.Bounds)) continue;
            switch (op)
            {
                case Structures.FillOp fill:
                {
                    ushort block = ctx.Palette.Id(fill.Block);
                    var ib = fill.Bounds.Expand(-1);
                    ForEachCell(ctx, fill.Bounds, (x, y, z, i) =>
                    {
                        if (fill.ShellOnly && ib.Contains(x, y, z)) return;
                        if (fill.OnlyAir && ctx.Blocks[i] != ctx.Palette.Air) return;
                        ctx.Blocks[i] = block;
                        if (fill.Shape != 0) ctx.EnsureShapes()[i] = fill.Shape;
                        else if (ctx.Shapes != null) ctx.Shapes[i] = 0;
                        if (ctx.Liquids != null) ctx.Liquids[i] = 0;
                        if (flag != 0) ctx.EnsureFlags()[i] |= flag;
                    });
                    break;
                }
                case Structures.CarveOp carve:
                    ForEachCell(ctx, carve.Bounds, (x, y, z, i) =>
                    {
                        ctx.Blocks[i] = ctx.Palette.Air;
                        if (ctx.Shapes != null) ctx.Shapes[i] = 0;
                        if (carve.Liquid != 0 || ctx.Liquids != null) ctx.EnsureLiquids()[i] = carve.Liquid;
                    });
                    break;
                case Structures.ReplaceOp rep:
                {
                    ushort block = ctx.Palette.Id(rep.Block);
                    var only = rep.OnlyReplace?.Select(ctx.Palette.Id).ToHashSet();
                    ForEachCell(ctx, rep.Bounds, (x, y, z, i) =>
                    {
                        ushort cur = ctx.Blocks[i];
                        if (cur == ctx.Palette.Air) return;
                        if (only != null ? only.Contains(cur) : ctx.Palette.IsNatural(cur)) ctx.Blocks[i] = block;
                    });
                    break;
                }
                case Structures.EllipsoidOp el:
                {
                    ushort block = el.Block != null ? ctx.Palette.Id(el.Block) : ctx.Palette.Air;
                    ForEachCell(ctx, el.Bounds, (x, y, z, i) =>
                    {
                        float dx = (x + 0.5f - el.Center.X) / el.Radii.X, dy = (y + 0.5f - el.Center.Y) / el.Radii.Y, dz = (z + 0.5f - el.Center.Z) / el.Radii.Z;
                        if (dx * dx + dy * dy + dz * dz >= 1f) return;
                        if (el.OnlyAir && ctx.Blocks[i] != ctx.Palette.Air) return;
                        if (ctx.Blocks[i] == ctx.Palette.Bedrock) return;
                        ctx.Blocks[i] = block;
                        if (ctx.Shapes != null) ctx.Shapes[i] = 0;
                        if (el.Liquid != 0 || ctx.Liquids != null) ctx.EnsureLiquids()[i] = block == ctx.Palette.Air ? el.Liquid : (byte)0;
                        if (flag != 0 && block != ctx.Palette.Air) ctx.EnsureFlags()[i] |= flag;
                    });
                    break;
                }
                case Structures.CylinderOp cyl:
                {
                    ushort block = cyl.Block != null ? ctx.Palette.Id(cyl.Block) : ctx.Palette.Air;
                    ForEachCell(ctx, cyl.Bounds, (x, y, z, i) =>
                    {
                        float dx = x + 0.5f - cyl.CenterX, dz = z + 0.5f - cyl.CenterZ;
                        if (dx * dx + dz * dz >= cyl.Radius * cyl.Radius) return;
                        if (cyl.OnlyAir && ctx.Blocks[i] != ctx.Palette.Air) return;
                        if (ctx.Blocks[i] == ctx.Palette.Bedrock) return;
                        ctx.Blocks[i] = block;
                        if (ctx.Liquids != null) ctx.Liquids[i] = 0;
                        if (flag != 0 && block != ctx.Palette.Air) ctx.EnsureFlags()[i] |= flag;
                    });
                    break;
                }
                case Structures.ObjectOp obj:
                {
                    if (!ctx.InCore(obj.Anchor.X, obj.Anchor.Y, obj.Anchor.Z)) break;
                    var def = ctx.Gen.Content.TileObjects.Get(obj.ObjectId);
                    var inst = new Voxels.TileObjects.TileObjectInstance(def, obj.Anchor, obj.Rotation, obj.Variant) { PendingLoot = obj.Loot, Locked = obj.Locked };
                    ctx.AddObject(inst);
                    break;
                }
            }
        }
    }
}
