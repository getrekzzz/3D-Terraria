using System.IO.Compression;
using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Gameplay.Items;
using Strata.Core.Voxels;
using Strata.Core.Voxels.TileObjects;

namespace Strata.Core.Persistence;

/// <summary>A chunk read back from a save.</summary>
public sealed record DecodedChunk(ChunkData Data, int MutationLevel, IReadOnlyList<TileObjectInstance> TileObjects);

/// <summary>
/// Versioned binary chunk format: header + tagged, length-prefixed sections (unknown sections are
/// skipped, so newer layers never break older readers), zlib-compressed. Blocks use a per-chunk palette of
/// save ids with run-length encoding.
/// </summary>
public static class ChunkCodec
{
    public const uint Magic = 0x314B4353; // "SCK1"

    /// <summary>
    /// v2: object records may carry a meta list (saved chest names, locked dungeon chests), flagged by a state bit. v1 chunks (no meta) still
    /// read; a v1 reader refuses v2 chunks instead of misreading their object records.
    /// </summary>
    public const byte FormatVersion = 2;

    private const byte TagEnd = 0, TagBlocks = 1, TagShapes = 2, TagLiquids = 3, TagFlags = 4, TagObjects = 5;

    // Object state bits; HasMeta means a tagged meta list follows PendingLoot (v1 chunks never set it).
    private const byte StateActive = 1, StateLootRolled = 2, StateHasMeta = 4;

    // Object meta entries: tag, length, payload, ended by MetaEnd; readers skip tags they do not know.
    private const byte MetaEnd = 0, MetaName = 1, MetaLocked = 2;

    public static byte[] Encode(ChunkData data, int mutationLevel, IReadOnlyList<TileObjectInstance> objects, ContentDatabase db, WorldIdTables ids) =>
        Encode(data, mutationLevel, EncodeObjects(objects, ids), db, ids);

    /// <summary>
    /// Encodes a chunk whose object section was already captured with <see cref="EncodeObjects"/> (objects are live and
    /// mutable, so they are captured on the main thread; the voxel data is an immutable snapshot that any thread may encode).
    /// </summary>
    public static byte[] Encode(ChunkData data, int mutationLevel, byte[] objectSection, ContentDatabase db, WorldIdTables ids)
    {
        using var raw = new MemoryStream();
        using (var w = new BinaryWriter(raw, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.WriteVar(mutationLevel);
            w.WriteSection(TagBlocks, s => WriteBlocks(s, data, db, ids));
            if (data.Shapes != null) w.WriteSection(TagShapes, s => s.WriteByteRuns(data.Shapes));
            if (data.Liquids != null) w.WriteSection(TagLiquids, s => s.WriteByteRuns(data.Liquids));
            if (data.Flags != null) w.WriteSection(TagFlags, s => s.WriteByteRuns(data.Flags));
            if (objectSection.Length > 0)
            {
                w.Write(TagObjects);
                w.WriteVar(objectSection.Length);
                w.Write(objectSection);
            }
            w.Write(TagEnd);
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw.GetBuffer(), 0, (int)raw.Length);
        return compressed.ToArray();
    }

    /// <summary>The body of a chunk's object section (empty when there are no objects).</summary>
    public static byte[] EncodeObjects(IReadOnlyList<TileObjectInstance> objects, WorldIdTables ids)
    {
        if (objects.Count == 0) return Array.Empty<byte>();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) WriteObjects(w, objects, ids);
        return ms.ToArray();
    }

    /// <summary>Reads an object section body written by <see cref="EncodeObjects"/>.</summary>
    public static List<TileObjectInstance> DecodeObjects(byte[] section, ContentDatabase db, WorldIdTables ids)
    {
        var list = new List<TileObjectInstance>();
        if (section.Length == 0) return list;
        using var r = new BinaryReader(new MemoryStream(section));
        ReadObjects(r, db, ids, list);
        return list;
    }

    public static DecodedChunk Decode(byte[] blob, ContentDatabase db, WorldIdTables ids)
    {
        using var input = new ZLibStream(new MemoryStream(blob), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        input.CopyTo(raw);
        raw.Position = 0;
        using var r = new BinaryReader(raw);

        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a Strata chunk blob.");
        byte version = r.ReadByte();
        if (version > FormatVersion) throw new InvalidDataException($"Chunk format v{version} is newer than supported v{FormatVersion}.");
        int mutationLevel = r.ReadVar();

        ushort[]? blocks = null;
        byte[]? shapes = null, liquids = null, flags = null;
        var objects = new List<TileObjectInstance>();
        while (true)
        {
            byte tag = r.ReadByte();
            if (tag == TagEnd) break;
            int length = r.ReadVar();
            long end = raw.Position + length;
            switch (tag)
            {
                case TagBlocks: blocks = ReadBlocks(r, db, ids); break;
                case TagShapes: shapes = r.ReadByteRuns(ChunkMath.Volume); break;
                case TagLiquids: liquids = r.ReadByteRuns(ChunkMath.Volume); break;
                case TagFlags: flags = r.ReadByteRuns(ChunkMath.Volume); break;
                case TagObjects: ReadObjects(r, db, ids, objects); break;
                default: break; // unknown section from a newer version: skip
            }
            raw.Position = end;
        }
        if (blocks == null) throw new InvalidDataException("Chunk blob has no block section.");
        return new DecodedChunk(ChunkData.Compact(blocks, shapes, liquids, flags), mutationLevel, objects);
    }

    private static void WriteBlocks(BinaryWriter w, ChunkData data, ContentDatabase db, WorldIdTables ids)
    {
        var palette = new List<ushort>();
        var paletteIndex = new Dictionary<ushort, int>();
        var indices = new int[ChunkMath.Volume];
        for (int i = 0; i < ChunkMath.Volume; i++)
        {
            ushort b = data.GetBlock(i);
            if (!paletteIndex.TryGetValue(b, out int p))
            {
                p = palette.Count;
                palette.Add(b);
                paletteIndex[b] = p;
            }
            indices[i] = p;
        }

        w.WriteVar(palette.Count);
        foreach (var b in palette) w.WriteVar(ids.Blocks.Intern(db.Blocks[b].Id));

        var runs = new List<(int, int)>();
        int start = 0;
        for (int i = 1; i <= indices.Length; i++)
        {
            if (i == indices.Length || indices[i] != indices[start])
            {
                runs.Add((i - start, indices[start]));
                start = i;
            }
        }
        w.WriteVar(runs.Count);
        foreach (var (len, p) in runs)
        {
            w.WriteVar(len);
            w.WriteVar(p);
        }
    }

    private static ushort[] ReadBlocks(BinaryReader r, ContentDatabase db, WorldIdTables ids)
    {
        int paletteCount = r.ReadVar();
        var palette = new ushort[paletteCount];
        for (int i = 0; i < paletteCount; i++) palette[i] = ids.ResolveBlock(db, r.ReadVar());

        var blocks = new ushort[ChunkMath.Volume];
        int runs = r.ReadVar(), pos = 0;
        for (int i = 0; i < runs; i++)
        {
            int len = r.ReadVar(), p = r.ReadVar();
            if (p >= paletteCount || pos + len > blocks.Length) throw new InvalidDataException("Corrupt block runs.");
            blocks.AsSpan(pos, len).Fill(palette[p]);
            pos += len;
        }
        if (pos != blocks.Length) throw new InvalidDataException("Block runs do not cover the chunk.");
        return blocks;
    }

    private static void WriteObjects(BinaryWriter w, IReadOnlyList<TileObjectInstance> objects, WorldIdTables ids)
    {
        w.WriteVar(objects.Count);
        foreach (var o in objects)
        {
            w.WriteVar(ids.TileObjects.Intern(o.Def.Id));
            w.WriteZig(o.Anchor.X);
            w.WriteZig(o.Anchor.Y);
            w.WriteZig(o.Anchor.Z);
            w.Write((byte)o.Rotation);
            w.Write((byte)o.Variant);
            w.WriteVar(Math.Max(0, o.HitPoints));
            bool meta = o.CustomName != null || o.Locked;
            w.Write((byte)((o.Active ? StateActive : 0) | (o.LootRolled ? StateLootRolled : 0) | (meta ? StateHasMeta : 0)));
            w.Write(o.PendingLoot ?? "");
            if (meta) WriteObjectMeta(w, o);
            int slots = o.Container?.Length ?? 0;
            w.WriteVar(slots);
            for (int i = 0; i < slots; i++)
            {
                var s = o.Container![i];
                if (s.IsEmpty)
                {
                    w.WriteVar(0);
                    continue;
                }
                w.WriteVar(ids.Items.Intern(s.Item!.Id) + 1);
                w.WriteVar(s.Count);
                w.WriteVar(s.Prefix == null ? 0 : ids.Prefixes.Intern(s.Prefix.Id) + 1);
            }
        }
    }

    private static void ReadObjects(BinaryReader r, ContentDatabase db, WorldIdTables ids, List<TileObjectInstance> into)
    {
        int count = r.ReadVar();
        for (int n = 0; n < count; n++)
        {
            var def = db.GetOrAddPlaceholderTileObject(ids.TileObjects.NameOf(r.ReadVar()));
            var anchor = new Int3(r.ReadZig(), r.ReadZig(), r.ReadZig());
            int rotation = r.ReadByte(), variant = r.ReadByte();
            var obj = new TileObjectInstance(def, anchor, rotation, variant) { HitPoints = r.ReadVar() };
            byte state = r.ReadByte();
            obj.Active = (state & StateActive) != 0;
            obj.LootRolled = (state & StateLootRolled) != 0;
            var loot = r.ReadString();
            obj.PendingLoot = loot.Length == 0 ? null : loot;
            if ((state & StateHasMeta) != 0) ReadObjectMeta(r, obj);
            int slots = r.ReadVar();
            for (int i = 0; i < slots; i++)
            {
                int itemId = r.ReadVar();
                if (itemId == 0) continue;
                var item = db.GetOrAddPlaceholderItem(ids.Items.NameOf(itemId - 1));
                int stackCount = r.ReadVar();
                int prefixId = r.ReadVar();
                var prefix = prefixId == 0 ? null : db.GetOrAddPlaceholderPrefix(ids.Prefixes.NameOf(prefixId - 1));
                if (obj.Container != null && i < obj.Container.Length) obj.Container[i] = new ItemStack(item, stackCount, prefix);
            }
            into.Add(obj);
        }
    }

    private static void WriteObjectMeta(BinaryWriter w, TileObjectInstance o)
    {
        if (o.CustomName != null) w.WriteSection(MetaName, s => s.Write(o.CustomName));
        if (o.Locked)
        {
            w.Write(MetaLocked); // a flag entry: no payload
            w.WriteVar(0);
        }
        w.Write(MetaEnd);
    }

    private static void ReadObjectMeta(BinaryReader r, TileObjectInstance obj)
    {
        while (true)
        {
            byte tag = r.ReadByte();
            if (tag == MetaEnd) return;
            int length = r.ReadVar();
            long end = r.BaseStream.Position + length;
            if (tag == MetaName) obj.CustomName = r.ReadString();
            else if (tag == MetaLocked) obj.Locked = true;
            r.BaseStream.Position = end;
        }
    }
}
