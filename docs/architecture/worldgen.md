# World generation

Deterministic from `(seed, size, GenVersion)`. Two tiers:

1. **Plan** (`WorldPlan`, built once, cheap): 1/8-resolution landness/height/ruggedness grids, biome regions
   (Terraria layout: spawn at the centre, snow and jungle on opposite sides, dungeon on the snow side, desert
   with an underground sandstone body, evil patches, an ocean ring), structure placements and spline features
   (worm tunnels, shafts, rivers, lava tubes) indexed per chunk in a `FeatureIndex`, ore choices, spawn.
   Only the plan hash is saved; the plan is rebuilt from the seed on load.
2. **Chunk passes** (pure functions of plan + chunk coordinate) fill a padded 34³ context. A chunk generated
   alone equals the same chunk generated inside a region (purity test), so chunks generate in any order on
   any thread.

Passes (`PassRegistry`, in order): 1 global density · 2 continents · 3 geological layers · 4 biome masks ·
5 caves (cheese/spaghetti lattices + plan worms) · 6 large caverns · 7 underground rivers · 8 ores ·
9 structures (cabins, mineshafts, living trees, pyramid, mini-biomes) · 10 dungeon · 11 jungle · 12 hostile
biome · 13 underworld · 14 floating islands · 15 ocean · 16 treasure · 17 ecology · 18 decoration ·
19 navigation (face connectivity, walkable cells) · 20 simulation init.

Layer bands (fractions of height): underworld < 0.14 · cavern < 0.49 · underground < 0.625 · sea 0.66 ·
sky ≥ 0.80 · space ≥ 0.93. Sizes (cells, 0.5 m): Tiny 512³, Small 2048×768×2048, Medium 4096×1024×4096,
Large 6144×1024×6144.

Determinism rules: SplitMix/PCG32 streams derived from names (`SeedTree`), never call order; no
`Math.Sin/Pow/Exp` in generation (polynomial `Trig`); vendored FastNoiseLite. Golden chunk hashes guard
unintended changes; a deliberate change bumps `GenVersion` (existing saves keep their edited chunks and
regenerate the rest with the new generator, with a logged warning).

Inspect with `dotnet run --project tools/Strata.Tools -- worldgen-report --seed=N --size=tiny`
(top-down biome map, cross-sections, ore-by-depth charts).
