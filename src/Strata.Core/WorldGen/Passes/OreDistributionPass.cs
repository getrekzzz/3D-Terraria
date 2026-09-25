using Strata.Core.Common;
using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Pass 8 — ore, gem and material-pocket veins driven entirely by <c>oreVeins</c> content data (placed by
/// <see cref="VeinStamper"/>). Veins marked <c>generate: false</c> are left to world mutations (hardmode ores).
/// </summary>
public sealed class OreDistributionPass : GenPass
{
    private readonly List<VeinRule> _veins = new();

    public override string Name => "ores";
    public override int Number => 8;

    public override void Plan(PlanContext ctx)
    {
        var rng = ctx.SeedsFor(this).Rng("alternates");
        var generated = ctx.Content.OreVeins.All.Where(v => v.Generate).ToList();
        foreach (var group in generated.Where(v => v.Group != null).GroupBy(v => v.Group!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var options = group.OrderBy(v => v.Id, StringComparer.Ordinal).ToList();
            ctx.Plan.OreChoices[group.Key] = options[rng.NextInt(options.Count)].Id;
        }

        foreach (var def in generated)
        {
            if (def.Group != null && ctx.Plan.OreChoices[def.Group] != def.Id) continue;
            _veins.Add(Resolve(ctx.Content, def, ctx.SeedsFor(this).Child(def.Id).Value));
        }
    }

    /// <summary>Resolves a vein rule's block ids against content (also used by ore mutations).</summary>
    public static VeinRule Resolve(Content.ContentDatabase content, OreVeinDef def, ulong seed) => new(
        def,
        (ushort)content.Blocks.Get(def.Block).RuntimeId,
        def.Replace.Select(r => (ushort)content.Blocks.Get(r).RuntimeId).ToHashSet(),
        def.Biomes.Select(b => (byte)content.Biomes.Get(b).RuntimeId).ToHashSet(),
        seed);

    public override void Generate(ChunkGenContext ctx)
    {
        if (ctx.AllAir) return;
        var buf = new GenBuffer(ctx);
        foreach (var vein in _veins) VeinStamper.Stamp(ref buf, ctx.Gen, vein, ctx.Coord);
    }

    /// <summary>The generating chunk's padded block array as a vein buffer.</summary>
    private readonly struct GenBuffer : IBlockBuffer
    {
        private readonly ChunkGenContext _ctx;

        public GenBuffer(ChunkGenContext ctx) => _ctx = ctx;

        public Int3 Origin => _ctx.Origin;
        public int Size => ChunkGenContext.Size;
        public ushort Get(int wx, int wy, int wz) => _ctx.Blocks[_ctx.WorldIndex(wx, wy, wz)];
        public void Set(int wx, int wy, int wz, ushort block) => _ctx.Blocks[_ctx.WorldIndex(wx, wy, wz)] = block;
    }
}
