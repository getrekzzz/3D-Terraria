using Strata.Core.Content.Defs;
using Strata.Core.Determinism;
using Strata.Core.Diagnostics;
using Strata.Core.WorldGen.Plan;

namespace Strata.Core.WorldGen.Passes;

/// <summary>
/// Per-biome surface relief from <see cref="BiomeDef.Relief"/> (rolling hills, soft snow ridges, jungle mounds, dunes,
/// mountain ranges in the open forest) blended across wide borders and blurred, so landforms read as rounded hills
/// instead of knife-edge ridges and biome borders ramp instead of stepping.
/// </summary>
internal sealed class BiomeRelief
{
    /// <summary>The biome of the open land outside every region (its relief also carries the mountain ranges).</summary>
    private const string OpenLand = "forest";

    private readonly WorldPlan _plan;
    private readonly ReliefSpec _open;
    private readonly ReliefSpec[] _specs;
    private readonly NoiseField _hills, _ridges, _bumps, _dunes, _mountainMask, _mountains;
    private readonly bool _useRolling, _useHills, _useRidges, _useBumps, _useDunes;

    public BiomeRelief(SeedTree seeds, PlanContext ctx)
    {
        _plan = ctx.Plan;
        _hills = NoiseField.Create(seeds, "hills", 190f, NoiseFractal.Fbm, 3);
        _ridges = NoiseField.Create(seeds, "ridges", 240f, NoiseFractal.Fbm, 3);
        _bumps = NoiseField.Create(seeds, "bumps", 90f, NoiseFractal.Fbm, 2);
        _dunes = NoiseField.Create(seeds, "dunes", 70f, NoiseFractal.Fbm, 2);
        _mountainMask = NoiseField.Create(seeds, "mountain_mask", 700f, NoiseFractal.Fbm, 2);
        _mountains = NoiseField.Create(seeds, "mountains", 260f, NoiseFractal.Fbm, 3);

        var biomes = ctx.Content.Biomes;
        _open = biomes.TryGet(OpenLand, out var forest) && forest.Relief != null ? forest.Relief : Missing(ctx, OpenLand, new ReliefSpec());
        var regions = _plan.Regions;
        _specs = new ReliefSpec[regions.Count];
        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            _specs[i] = r.Profile == RegionProfile.Buried ? _open
                : biomes.TryGet(r.Biome, out var def) && def.Relief != null ? def.Relief
                : Missing(ctx, r.Biome, _open);
        }
        foreach (var s in _specs.Append(_open))
        {
            _useRolling |= s.Rolling != 0f;
            _useHills |= s.Hills != 0f;
            _useRidges |= s.Ridges != 0f;
            _useBumps |= s.Bumps != 0f;
            _useDunes |= s.Dunes != 0f;
        }
    }

    private static ReliefSpec Missing(PlanContext ctx, string biome, ReliefSpec fallback)
    {
        ctx.Log.Warn("worldgen", $"Biome '{biome}' is placed on the surface but has no relief; using the {OpenLand} relief.");
        return fallback;
    }

    /// <summary>Adds the relief to the plan heights (scaled down towards the coast) and sets the ruggedness grid.</summary>
    public void Apply()
    {
        var plan = _plan;
        var regions = plan.Regions;
        var weights = new float[regions.Count];
        var relief = new Grid2D(plan.Size.X, plan.Size.Z, WorldPlan.HeightStep);
        var rugged = new Grid2D(plan.Size.X, plan.Size.Z, WorldPlan.HeightStep);
        var mountains = _open.Mountains;

        for (int gz = 0; gz < relief.Depth; gz++)
            for (int gx = 0; gx < relief.Width; gx++)
            {
                float x = gx * relief.Step, z = gz * relief.Step;
                float total = 0f;
                for (int i = 0; i < regions.Count; i++)
                {
                    weights[i] = Weight(regions[i], x, z);
                    total += weights[i];
                }
                var terms = Sample(x, z);
                float forest = MathF.Max(0f, 1f - total);
                float norm = forest + total;
                float h = forest * terms.Relief(_open), rug = forest * _open.Ruggedness;
                for (int i = 0; i < regions.Count; i++)
                {
                    if (weights[i] <= 0f) continue;
                    h += weights[i] * terms.Relief(_specs[i]);
                    rug += weights[i] * _specs[i].Ruggedness;
                }
                h /= norm;
                rug /= norm;

                float spawnCalm = SpawnCalm(x, z);
                float mask = MountainMask(x, z, spawnCalm, forest);
                if (mask > 0f)
                {
                    h += mask * (mountains!.Height + mountains.Peak * DetMath.SoftRidge(_mountains.Sample(x, z), 0.3f));
                    rug = DetMath.Lerp(rug, 0.5f, mask);
                }

                relief[gx, gz] = h * DetMath.Lerp(0.35f, 1f, spawnCalm);
                rugged[gx, gz] = rug * spawnCalm;
            }

        // Two radius-2 box passes approximate a Gaussian about 40 cells wide; ruggedness ramps too, so overhang
        // amplitude never jumps at a biome border.
        relief.Blur(2);
        relief.Blur(2);
        rugged.Blur(3);
        for (int i = 0; i < plan.Height.Values.Length; i++)
        {
            float land = plan.Landness.Values[i];
            float beach = DetMath.SmoothStep(0.45f, 0.85f, land);
            plan.Height.Values[i] += relief.Values[i] * land * beach;
            plan.Ruggedness.Values[i] = rugged.Values[i] * land;
        }
    }

    /// <summary>
    /// 0–1: how strongly a mountain range rises at a point (before the blur): the large-scale mountain mask over open
    /// land, faded out near spawn and inside biome regions.
    /// </summary>
    internal float MountainMask(float x, float z)
    {
        float total = 0f;
        foreach (var r in _plan.Regions) total += Weight(r, x, z);
        return MountainMask(x, z, SpawnCalm(x, z), MathF.Max(0f, 1f - total));
    }

    private float MountainMask(float x, float z, float spawnCalm, float openLand) =>
        _open.Mountains == null || spawnCalm * openLand <= 0f ? 0f
            : DetMath.SmoothStep(0.25f, 0.55f, _mountainMask.Sample(x, z)) * spawnCalm * openLand;

    /// <summary>A region's share of the relief at a point: ramps in over a wide border (buried regions have none).</summary>
    private static float Weight(BiomeRegion r, float x, float z) =>
        r.Profile == RegionProfile.Buried ? 0f : DetMath.SmoothStep(0f, 0.55f, r.Membership(x, z, 0f));

    /// <summary>0 at spawn rising to 1 by 140 cells out: the spawn area stays calm (lower relief, no mountains or overhangs).</summary>
    private float SpawnCalm(float x, float z)
    {
        float dx = x - _plan.Size.X * 0.5f, dz = z - _plan.Size.Z * 0.5f;
        return DetMath.SmoothStep(30f, 140f, MathF.Sqrt(dx * dx + dz * dz));
    }

    /// <summary>The relief noise terms at a point (only those some placed biome uses), each 0–1.</summary>
    private Terms Sample(float x, float z)
    {
        float hills = _useHills || _useRolling ? _hills.Sample01(x, z) : 0f;
        return new Terms
        {
            Hills = hills,
            Rolling = _useRolling ? hills * _hills.Sample01(z * 0.7f, x * 0.7f) : 0f,
            Ridges = _useRidges ? DetMath.SoftRidge(_ridges.Sample(x, z), 0.25f) : 0f,
            Bumps = _useBumps ? _bumps.Sample01(x, z) : 0f,
            Dunes = _useDunes ? DetMath.SoftRidge(_dunes.Sample(x, z), 0.12f) : 0f,
        };
    }

    private struct Terms
    {
        public float Hills, Rolling, Ridges, Bumps, Dunes;

        public readonly float Relief(ReliefSpec s) =>
            s.Base + s.Rolling * Rolling + s.Hills * Hills + s.Ridges * Ridges + s.Bumps * Bumps + s.Dunes * Dunes;
    }
}
