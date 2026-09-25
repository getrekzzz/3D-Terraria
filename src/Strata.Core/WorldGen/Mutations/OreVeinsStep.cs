using Strata.Core.Common;
using Strata.Core.Content;
using Strata.Core.Content.Specs;
using Strata.Core.Determinism;
using Strata.Core.WorldGen.Chunk;
using Strata.Core.WorldGen.Passes;

namespace Strata.Core.WorldGen.Mutations;

/// <summary>New ore veins seeded through the world (hardmode ores), one of each alternate group per world.</summary>
public sealed class OreVeinsStep : IMutationStep
{
    private readonly WorldGenerator _gen;
    private readonly VeinRule[] _rules;
    private readonly HashSet<ushort> _replaced;

    public OreVeinsStep(WorldGenerator gen, ContentDatabase content, OreVeinsMutationParams p, ulong seed)
    {
        _gen = gen;
        var defs = p.Veins.Select(content.OreVeins.Get).ToList();
        var rng = new Pcg32(Hash.Combine(seed, Hash.String("alternates")));
        var chosen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in defs.Where(d => d.Group != null).GroupBy(d => d.Group!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var options = group.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            chosen[group.Key] = options[rng.NextInt(options.Count)].Id;
        }
        _rules = defs.Where(d => d.Group == null || chosen[d.Group] == d.Id)
            .Select(d => OreDistributionPass.Resolve(content, d, Hash.Combine(seed, Hash.String(d.Id))))
            .ToArray();
        _replaced = _rules.SelectMany(r => r.Replace).ToHashSet();
    }

    /// <summary>Ore vein rules this world got (one per alternate group).</summary>
    public IReadOnlyList<VeinRule> Rules => _rules;

    public bool Affects(ushort block) => _replaced.Contains(block);

    public void Apply(ref ChunkBuffer buf, Int3 chunk)
    {
        foreach (var rule in _rules) VeinStamper.Stamp(ref buf, _gen, rule, chunk);
    }
}
