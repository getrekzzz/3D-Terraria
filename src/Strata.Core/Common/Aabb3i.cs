namespace Strata.Core.Common;

/// <summary>Integer axis-aligned box with inclusive <see cref="Min"/> and exclusive <see cref="Max"/>.</summary>
public readonly struct Aabb3i : IEquatable<Aabb3i>
{
    public readonly Int3 Min;
    public readonly Int3 Max;

    public Aabb3i(Int3 min, Int3 max)
    {
        Min = min;
        Max = max;
    }

    public static Aabb3i FromCenter(Int3 center, Int3 halfExtent) =>
        new(center - halfExtent, center + halfExtent + Int3.One);

    public static readonly Aabb3i Empty = new(Int3.Zero, Int3.Zero);

    public Int3 Size => Max - Min;
    public bool IsEmpty => Max.X <= Min.X || Max.Y <= Min.Y || Max.Z <= Min.Z;
    public long Volume => IsEmpty ? 0 : (long)Size.X * Size.Y * Size.Z;

    public bool Contains(Int3 p) =>
        p.X >= Min.X && p.X < Max.X && p.Y >= Min.Y && p.Y < Max.Y && p.Z >= Min.Z && p.Z < Max.Z;

    public bool Contains(int x, int y, int z) =>
        x >= Min.X && x < Max.X && y >= Min.Y && y < Max.Y && z >= Min.Z && z < Max.Z;

    public bool Intersects(Aabb3i o) =>
        Min.X < o.Max.X && o.Min.X < Max.X &&
        Min.Y < o.Max.Y && o.Min.Y < Max.Y &&
        Min.Z < o.Max.Z && o.Min.Z < Max.Z;

    public Aabb3i Intersection(Aabb3i o)
    {
        var r = new Aabb3i(Int3.Max(Min, o.Min), Int3.Min(Max, o.Max));
        return r.IsEmpty ? Empty : r;
    }

    public Aabb3i Union(Aabb3i o)
    {
        if (IsEmpty) return o;
        if (o.IsEmpty) return this;
        return new Aabb3i(Int3.Min(Min, o.Min), Int3.Max(Max, o.Max));
    }

    public Aabb3i Expand(int amount) => new(Min - Int3.One * amount, Max + Int3.One * amount);

    public bool Equals(Aabb3i other) => Min == other.Min && Max == other.Max;
    public override bool Equals(object? obj) => obj is Aabb3i o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Min, Max);
    public override string ToString() => $"[{Min} .. {Max})";
}
