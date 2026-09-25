using System.Runtime.CompilerServices;

namespace Strata.Core.Common;

/// <summary>Integer 3D vector used for cell and chunk coordinates.</summary>
public readonly struct Int3 : IEquatable<Int3>
{
    public readonly int X;
    public readonly int Y;
    public readonly int Z;

    public Int3(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static readonly Int3 Zero = new(0, 0, 0);
    public static readonly Int3 One = new(1, 1, 1);
    public static readonly Int3 Up = new(0, 1, 0);
    public static readonly Int3 Down = new(0, -1, 0);

    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator -(Int3 a) => new(-a.X, -a.Y, -a.Z);
    public static Int3 operator *(Int3 a, int s) => new(a.X * s, a.Y * s, a.Z * s);
    public static bool operator ==(Int3 a, Int3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
    public static bool operator !=(Int3 a, Int3 b) => !(a == b);

    public int ManhattanLength => Math.Abs(X) + Math.Abs(Y) + Math.Abs(Z);
    public int ChebyshevLength => Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
    public long LengthSquared => (long)X * X + (long)Y * Y + (long)Z * Z;

    public static Int3 Min(Int3 a, Int3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Int3 Max(Int3 a, Int3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    public bool Equals(Int3 other) => this == other;
    public override bool Equals(object? obj) => obj is Int3 o && this == o;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        unchecked
        {
            int h = X * 73856093;
            h ^= Y * 19349663;
            h ^= Z * 83492791;
            return h;
        }
    }

    public override string ToString() => $"({X}, {Y}, {Z})";

    public void Deconstruct(out int x, out int y, out int z)
    {
        x = X;
        y = Y;
        z = Z;
    }
}
