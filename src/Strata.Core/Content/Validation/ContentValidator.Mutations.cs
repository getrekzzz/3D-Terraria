using Strata.Core.Content.Specs;
using Strata.Core.World;

namespace Strata.Core.Content.Validation;

public static partial class ContentValidator
{
    /// <summary>World mutations, the conversion tables they use and spreading biomes.</summary>
    private static void ValidateMutations(Ctx ctx)
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in ctx.Db.Mutations.All)
        {
            if (string.IsNullOrEmpty(m.Trigger)) ctx.Error(m, "trigger (a progression flag) is required.");
            ctx.Ref(m, ctx.Db.Flags, m.Trigger, "trigger");
            ctx.Spec(m, SpecKind.Mutation, m.Step, "step");
            if (m.Step.Type == MutationSpecs.OreVeins && ctx.TryParse<OreVeinsMutationParams>(SpecKind.Mutation, m.Step) is { } ores)
                foreach (var id in ores.Veins)
                {
                    ctx.Ref(m, ctx.Db.OreVeins, id, "step.veins");
                    listed.Add(id);
                    if (ctx.Db.OreVeins.TryGet(id, out var vein) && vein.Generate)
                        ctx.Error(m, $"step.veins '{id}' is also placed by world generation (set generate: false).");
                }
            if (m.Step.Type == MutationSpecs.Wedge && ctx.TryParse<WedgeMutationParams>(SpecKind.Mutation, m.Step) is { } wedge)
            {
                ctx.Ref(m, ctx.Db.Conversions, wedge.Conversion, "step.conversion");
                foreach (var (evil, conversion) in wedge.ByEvil)
                {
                    if (!Enum.TryParse<EvilKind>(evil, ignoreCase: true, out _)) ctx.Error(m, $"step.byEvil key '{evil}' is not an evil kind.");
                    ctx.Ref(m, ctx.Db.Conversions, conversion, $"step.byEvil.{evil}");
                }
                if (wedge.ByEvil.Count > 0)
                    foreach (var kind in Enum.GetNames<EvilKind>())
                        if (!wedge.ByEvil.Keys.Any(k => k.Equals(kind, StringComparison.OrdinalIgnoreCase)))
                            ctx.Error(m, $"step.byEvil has no conversion for {kind} worlds.");
            }
        }
        foreach (var v in ctx.Db.OreVeins.All.Where(v => !v.Generate && !listed.Contains(v.Id)))
            ctx.Warn(v, "generate is false but no world mutation places this vein.");

        foreach (var c in ctx.Db.Conversions.All)
        {
            ctx.Ref(c, ctx.Db.Biomes, c.Zone, "zone");
            if (c.Map.Count == 0) ctx.Error(c, "map must convert at least one block.");
            foreach (var (from, to) in c.Map)
            {
                ctx.Ref(c, ctx.Db.Blocks, from, "map key");
                ctx.Ref(c, ctx.Db.Blocks, to, $"map[{from}]");
                if (from == to) ctx.Error(c, $"map converts '{from}' to itself.");
            }
        }

        foreach (var s in ctx.Db.SpreadRules.All)
        {
            ctx.Ref(s, ctx.Db.Conversions, s.Conversion, "conversion");
            foreach (var src in s.Sources) ctx.Ref(s, ctx.Db.Blocks, src, "sources");
            ctx.Ref(s, ctx.Db.Flags, s.RequiresFlag, "requiresFlag");
            if (s.Chance is <= 0 or > 1) ctx.Error(s, "chance must be in (0, 1].");
            if (s.Radius is < 1 or > 4) ctx.Error(s, "radius must be 1–4.");
        }
    }
}
