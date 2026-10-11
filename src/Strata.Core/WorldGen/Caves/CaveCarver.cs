using Strata.Core.Common;
using Strata.Core.Voxels;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Fields;

namespace Strata.Core.WorldGen.Caves;

/// <summary>
/// Builds the cave network in one chunk, inside the caves pass. <see cref="Prepare"/> evaluates every room and passage
/// touching the chunk: which cells they open (and fill with a pool's water or lava) and which rock noise caves must leave
/// alone (the shell round each room, so its floor stays whole and it keeps its shape, and the rock under passages). The
/// pass carves its noise caves around the protected rock, then <see cref="Apply"/> opens the network, dresses the room
/// walls by kind and grows the formations. Pure: every decision comes from plan features, cell positions and the warp noise, never from another
/// chunk, so padding matches the neighbours' cores.
/// </summary>
internal sealed partial class CaveCarver
{
    /// <summary><see cref="State"/> flags: noise caves keep out; the network opens the cell; and fills it with water or lava.</summary>
    public const byte Protect = 1, Open = 2, Water = 4, Lava = 8;

    /// <summary>Edge of the owner box: the padded box plus one cell all round, so wall dressing can see past the padding.</summary>
    private const int Wide = ChunkGenContext.Size + 2;

    /// <summary><see cref="Owner"/> bits marking a pool cell: water (for pool beds) and lava (for obsidian rims); the low
    /// bits hold the room.</summary>
    private const byte WaterOwner = 0x40, LavaOwner = 0x80, RoomBits = 0x3F;

    [ThreadStatic] private static byte[]? _state, _owner;
    [ThreadStatic] private static float[]? _warp;

    private readonly ChunkGenContext _ctx;
    private readonly List<CaveRoomFeature> _rooms;
    private readonly List<CavePassageFeature> _passages;

    private CaveCarver(ChunkGenContext ctx, List<CaveRoomFeature> rooms, List<CavePassageFeature> passages)
    {
        _ctx = ctx;
        _rooms = rooms;
        _passages = passages;
        State = _state ??= new byte[ChunkGenContext.Volume];
        Owner = _owner ??= new byte[Wide * Wide * Wide];
        Array.Clear(State);
        Array.Clear(Owner);
        var warp = _warp ??= new float[Wide * Wide * Wide];
        new LatticeSampler(ctx.Origin - Int3.One, Int3.One * Wide, ctx.Gen.Caves.Warp).FillDense(warp, Wide);
    }

    /// <summary>Per padded cell (<see cref="ChunkGenContext.Index"/>): <see cref="Protect"/>, <see cref="Open"/>, <see cref="Water"/>, <see cref="Lava"/>.</summary>
    public byte[] State { get; }

    /// <summary>Per cell of the wide box: the index + 1 of the room whose interior holds it (0 for none), with its pool bits.</summary>
    private byte[] Owner { get; }

    /// <summary>Evaluates the network features touching a chunk; null when there are none.</summary>
    public static CaveCarver? Prepare(ChunkGenContext ctx)
    {
        List<CaveRoomFeature>? rooms = null;
        List<CavePassageFeature>? passages = null;
        foreach (var f in ctx.Plan.Features.InChunk(ctx.Coord))
        {
            if (f is CaveRoomFeature r) (rooms ??= new()).Add(r);
            else if (f is CavePassageFeature p) (passages ??= new()).Add(p);
        }
        if (rooms == null && passages == null) return null;
        var carver = new CaveCarver(ctx, rooms ?? new(), passages ?? new());
        for (int k = 0; k < carver._rooms.Count && k < RoomBits; k++) carver.MarkRoom(carver._rooms[k], (byte)(k + 1));
        foreach (var p in carver._passages) carver.MarkPassage(p);
        return carver;
    }

    private float Warp(int wx, int wy, int wz) =>
        _warp![((wy - _ctx.Origin.Y + 1) * Wide + (wz - _ctx.Origin.Z + 1)) * Wide + (wx - _ctx.Origin.X + 1)];

    private int WideIndex(int wx, int wy, int wz) => ((wy - _ctx.Origin.Y + 1) * Wide + (wz - _ctx.Origin.Z + 1)) * Wide + (wx - _ctx.Origin.X + 1);

    private void MarkRoom(CaveRoomFeature room, byte id)
    {
        var o = _ctx.Origin - Int3.One;
        var box = new Aabb3i(o, o + Int3.One * Wide).Intersection(room.Bounds);
        if (box.IsEmpty) return;
        // The shell noise caves keep out of: from under the floor to a share of the roof's height (past the roof when whole).
        float enclosure = CaveTuning.Enclosure(room.Room);
        float shellTop = enclosure >= 1f ? room.Height * (1f + CaveTuning.CeilingWarp) + CaveTuning.ShellCells : room.Height * enclosure;
        float shellReach = 1f + CaveTuning.RoomWarp;
        int deepest = room.Sink + CaveTuning.RoomFloorRock;
        for (int z = box.Min.Z; z < box.Max.Z; z++)
            for (int x = box.Min.X; x < box.Max.X; x++)
            {
                float cx = x + 0.5f, cz = z + 0.5f;
                float u0 = room.Footprint(cx, cz);
                // Cells past the warped wall, measured outward along the column's direction from the centre.
                float dx = cx - room.X, dz = cz - room.Z, beyond = u0 > 1e-4f ? MathF.Sqrt(dx * dx + dz * dz) * (1f - shellReach / u0) : -1f;
                bool shell = beyond < CaveTuning.ShellCells;
                if (!shell && u0 * (1f - CaveTuning.RoomWarp) >= 1f) continue;
                int sink = u0 < 1f ? room.SinkAt(cx, cz, u0) : 0;
                var liquid = LiquidType.None;
                bool pooled = sink > 0 && room.InPool(cx, cz, out liquid);
                for (int y = box.Min.Y; y < box.Max.Y; y++)
                {
                    float rel = y + 0.5f - room.FloorY;
                    bool core = _ctx.Contains(x, y, z);
                    int i = core ? _ctx.WorldIndex(x, y, z) : -1;
                    if (core && shell && rel >= -deepest && rel < shellTop) State[i] |= Protect;
                    if (rel < -sink) continue;
                    float n = Warp(x, y, z);
                    float u = u0 * (1f + CaveTuning.RoomWarp * n);
                    if (u >= 1f || (sink == 0 && rel < room.RiseAt(u))) continue;
                    if (rel >= room.CeilingAt(u) * (1f + CaveTuning.CeilingWarp * n)) continue;
                    byte pool = !pooled || rel >= 0f ? (byte)0 : liquid == LiquidType.Lava ? LavaOwner : WaterOwner;
                    Owner[WideIndex(x, y, z)] = (byte)(id | pool);
                    if (!core) continue;
                    State[i] |= Open;
                    if (pooled && rel < 0f) State[i] |= liquid == LiquidType.Lava ? Lava : Water;
                }
            }
    }

    private void MarkPassage(CavePassageFeature p)
    {
        var box = new Aabb3i(_ctx.Origin, _ctx.Origin + Int3.One * ChunkGenContext.Size).Intersection(p.Bounds);
        if (box.IsEmpty) return;
        float reach = p.HalfWidth / (1f - CaveTuning.PassageWarp), guard = p.HalfWidth + 2f;
        float wallBand = p.Height * CaveTuning.PassageWallBand;
        for (int z = box.Min.Z; z < box.Max.Z; z++)
            for (int x = box.Min.X; x < box.Max.X; x++)
            {
                float w = p.Axis(x + 0.5f, z + 0.5f, out float t);
                if (w >= reach && w >= guard) continue;
                float floor = p.FloorAt(t), wn = w / p.HalfWidth;
                int lo = Math.Max(box.Min.Y, (int)MathF.Floor(floor) - CaveTuning.PassageFloorRock - 1);
                int hi = Math.Min(box.Max.Y, (int)MathF.Ceiling(floor + p.Height * (1f + CaveTuning.CeilingWarp)) + 1);
                for (int y = lo; y < hi; y++)
                {
                    float rel = y + 0.5f - floor;
                    int i = _ctx.WorldIndex(x, y, z);
                    if (w < guard && rel >= -CaveTuning.PassageFloorRock && rel < wallBand) State[i] |= Protect;
                    if (w >= reach) continue;
                    float n = Warp(x, y, z);
                    float u = wn * (1f + CaveTuning.PassageWarp * n);
                    if (u >= 1f || rel < p.RiseAt(u) || rel >= p.CeilingAt(u) * (1f + CaveTuning.CeilingWarp * n)) continue;
                    State[i] |= Open;
                }
            }
    }

    /// <summary>Opens the network (after the noise caves), then dresses the room walls and grows the formations.</summary>
    public void Apply()
    {
        var p = _ctx.Palette;
        var blocks = _ctx.Blocks;
        for (int i = 0; i < ChunkGenContext.Volume; i++)
        {
            byte s = State[i];
            if ((s & Open) == 0 || blocks[i] == p.Bedrock) continue;
            blocks[i] = p.Air;
            if ((s & (Water | Lava)) != 0) _ctx.EnsureLiquids()[i] = LiquidCode.Full((s & Lava) != 0 ? LiquidType.Lava : LiquidType.Water);
            else if (_ctx.Liquids != null) _ctx.Liquids[i] = 0;
        }
        DressWalls();
        foreach (var room in _rooms)
            foreach (var f in room.Formations) Grow(room, f);
    }
}
