namespace Strata.Core.Persistence;

/// <summary>Varint/zigzag helpers for compact binary save formats.</summary>
internal static class BinaryExtensions
{
    public static void WriteVar(this BinaryWriter w, int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Use WriteZig for signed values.");
        w.Write7BitEncodedInt(value);
    }

    public static int ReadVar(this BinaryReader r) => r.Read7BitEncodedInt();

    public static void WriteZig(this BinaryWriter w, int value) => w.Write7BitEncodedInt((value << 1) ^ (value >> 31));

    public static int ReadZig(this BinaryReader r)
    {
        int v = r.Read7BitEncodedInt();
        return (int)((uint)v >> 1) ^ -(v & 1);
    }

    /// <summary>Writes a length-prefixed section so readers can skip unknown tags.</summary>
    public static void WriteSection(this BinaryWriter w, byte tag, Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        using (var inner = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) body(inner);
        w.Write(tag);
        w.WriteVar((int)ms.Length);
        w.Write(ms.GetBuffer(), 0, (int)ms.Length);
    }

    public static void WriteByteRuns(this BinaryWriter w, byte[] data)
    {
        var runs = new List<(int Length, byte Value)>();
        int i = 0;
        while (i < data.Length)
        {
            int j = i + 1;
            while (j < data.Length && data[j] == data[i]) j++;
            runs.Add((j - i, data[i]));
            i = j;
        }
        w.WriteVar(runs.Count);
        foreach (var (len, value) in runs)
        {
            w.WriteVar(len);
            w.Write(value);
        }
    }

    public static byte[] ReadByteRuns(this BinaryReader r, int expectedLength)
    {
        var data = new byte[expectedLength];
        int runs = r.ReadVar(), pos = 0;
        for (int i = 0; i < runs; i++)
        {
            int len = r.ReadVar();
            byte value = r.ReadByte();
            if (pos + len > expectedLength) throw new InvalidDataException("Run-length data overflows the chunk.");
            data.AsSpan(pos, len).Fill(value);
            pos += len;
        }
        if (pos != expectedLength) throw new InvalidDataException("Run-length data does not cover the chunk.");
        return data;
    }
}
