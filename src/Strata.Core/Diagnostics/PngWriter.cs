using System.Buffers.Binary;
using System.IO.Compression;

namespace Strata.Core.Diagnostics;

/// <summary>Dependency-free RGBA8 PNG encoder for reports and debug images.</summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, int width, int height, byte[] rgba)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = File.Create(path);
        Write(fs, width, height, rgba);
    }

    public static void Write(Stream output, int width, int height, byte[] rgba)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("rgba length mismatch", nameof(rgba));
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        WriteChunk(output, "IHDR", ihdr);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            int stride = width * 4;
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0); // filter: none
                z.Write(rgba, y * stride, stride);
            }
        }
        WriteChunk(output, "IDAT", raw.ToArray());
        WriteChunk(output, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = new byte[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        s.Write(typeBytes);
        s.Write(data);
        uint crc = 0xFFFFFFFFu;
        crc = Update(crc, typeBytes);
        crc = Update(crc, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFFu);
        s.Write(crcBytes);
    }

    private static uint Update(uint crc, byte[] data)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
