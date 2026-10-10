using Strata.Core.Determinism;

namespace Strata.Core.WorldGen.Plan;

/// <summary>Fingerprint of a world plan; saves store it to detect generator drift between builds.</summary>
public static class PlanHasher
{
    public static string Compute(WorldPlan plan)
    {
        ulong h = Hash.Combine(Hash.String("plan"), (long)GenVersion.Current);
        h = Hash.Combine(h, plan.Settings.Seed);
        // Secret seeds that change generation (ordinary worlds hash as before).
        var shaping = plan.Settings.Secret & (World.SecretSeed.Drunk | World.SecretSeed.NotTheBees | World.SecretSeed.NoTraps | World.SecretSeed.DontDigUp);
        if (shaping != World.SecretSeed.None) h = Hash.Combine(h, (long)shaping);
        foreach (var v in plan.Height.Values) h = Hash.Combine(h, (long)MathF.Round(v * 16f));
        foreach (var v in plan.Cliffs.Values) h = Hash.Combine(h, (long)MathF.Round(v * 64f));
        foreach (var r in plan.Regions)
        {
            h = Hash.Combine(h, Hash.String(r.Biome));
            h = Hash.Combine(h, (long)(r.CenterX * 16) ^ ((long)(r.CenterZ * 16) << 24));
        }
        foreach (var f in plan.Features.All)
        {
            h = Hash.Combine(h, Hash.String(f.Kind));
            var b = f.Bounds;
            h = Hash.Combine(Hash.Combine(Hash.Combine(h, b.Min.X), b.Min.Y), b.Min.Z);
            h = Hash.Combine(Hash.Combine(Hash.Combine(h, b.Max.X), b.Max.Y), b.Max.Z);
        }
        foreach (var kv in plan.OreChoices.OrderBy(k => k.Key, StringComparer.Ordinal))
            h = Hash.Combine(h, Hash.String(kv.Key + "=" + kv.Value));
        return h.ToString("x16");
    }
}
