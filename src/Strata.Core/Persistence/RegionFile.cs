using System.Buffers.Binary;
using Strata.Core.Common;

namespace Strata.Core.Persistence;

/// <summary>
/// Stores up to 8³ chunk blobs in one file: header, offset/length table, then blob data.
/// Files are rewritten atomically on save (regions are small: only modified chunks are stored).
/// </summary>
public static class RegionFile
{
    public const int Shift = 3;
    public const int Size = 1 << Shift;
    public const int Entries = Size * Size * Size;
    private const uint Magic = 0x31475253; // "SRG1"
    private const int HeaderBytes = 8 + Entries * 8;

    public static Int3 RegionOf(Int3 chunk) => new(chunk.X >> Shift, chunk.Y >> Shift, chunk.Z >> Shift);

    public static int LocalIndex(Int3 chunk) =>
        ((chunk.Y & (Size - 1)) << (Shift * 2)) | ((chunk.Z & (Size - 1)) << Shift) | (chunk.X & (Size - 1));

    public static string FileName(Int3 region) => $"r.{region.X}.{region.Y}.{region.Z}.srg";

    /// <summary>Reads every blob in a region file (index → blob).</summary>
    public static Dictionary<int, byte[]> ReadAll(string path)
    {
        var result = new Dictionary<int, byte[]>();
        var bytes = AtomicFile.ReadWithFallback(path, IsValid);
        if (bytes == null) return result;
        var span = bytes.AsSpan();
        for (int i = 0; i < Entries; i++)
        {
            int offset = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8 + i * 8));
            int length = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(12 + i * 8));
            if (length > 0) result[i] = span.Slice(offset, length).ToArray();
        }
        return result;
    }

    /// <summary>Merges updates (null value = delete entry) into the region file and rewrites it atomically.</summary>
    public static void Update(string path, IReadOnlyDictionary<int, byte[]?> updates)
    {
        var blobs = ReadAll(path);
        foreach (var (index, blob) in updates)
        {
            if (blob == null) blobs.Remove(index);
            else blobs[index] = blob;
        }
        Write(path, blobs);
    }

    /// <summary>Rewrites a region file atomically with exactly these blobs (an empty set deletes the file).</summary>
    public static void Write(string path, IReadOnlyDictionary<int, byte[]> blobs)
    {
        if (blobs.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        AtomicFile.WriteAllBytes(path, Serialize(blobs));
    }

    /// <summary>The file image of a region: header, offset/length table, then the blobs in index order.</summary>
    public static byte[] Serialize(IReadOnlyDictionary<int, byte[]> blobs)
    {
        int total = HeaderBytes;
        foreach (var blob in blobs.Values) total += blob.Length;
        var output = new byte[total];
        var span = output.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), 1);
        int cursor = HeaderBytes;
        foreach (var (index, blob) in blobs.OrderBy(kv => kv.Key))
        {
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8 + index * 8), cursor);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12 + index * 8), blob.Length);
            blob.CopyTo(span.Slice(cursor));
            cursor += blob.Length;
        }
        return output;
    }

    private static bool IsValid(byte[] bytes) =>
        bytes.Length >= HeaderBytes && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == Magic;
}
