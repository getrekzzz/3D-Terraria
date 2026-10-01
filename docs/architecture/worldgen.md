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
`Math.Sin/Pow/Exp` in generation (polynomial `Trig`); vendored FastNoiseLite. Golden chunk hashes
(`WorldGenTests.GeneratedChunksMatchTheirGoldenHashes`) guard unintended changes; a deliberate change bumps `GenVersion` (existing saves keep their edited chunks and
regenerate the rest with the new generator, with a logged warning). The golden chunks include at least three surface
chunks (spawn forest, tundra, jungle, desert) and hash blocks by string id, so adding block content that shifts runtime
ids does not break them.

**Outdated worlds.** A world whose `WorldMeta.GeneratorVersion` is older than `GenVersion.Current` is flagged by
`SaveCatalog` (`WorldEntry.OldGenerator`): the world list shows "old terrain" and asks before playing ("Made with an older
world generator - new terrain will not match"), and `WorldSession` logs a warning on load. Old worlds are disposable;
nothing converts them.

**Surface relief** (`BiomeRelief`, planned in pass 4) comes from data: each surface biome's `relief` in
`data/biomes.json` (`ReliefSpec`: base rise plus rolling, hills, ridges, bumps and dunes amplitudes, ruggedness, cliff
affinity; the forest's also carries `mountains`). The forest relief covers the open land outside every region. Ridges,
dunes and mountains run fBm through `DetMath.SoftRidge`, which gives rounded crests where ridged fractals made knife
edges. Regions ramp in with SmoothStep(0, 0.55, membership), so borders blend over a wide band, and mountain ranges rise
under a λ700 mask away from spawn. The relief grid is box-blurred twice (radius 2, about a 40-cell Gaussian) and the
ruggedness grid once (radius 3). `TerrainField` samples plan height and ruggedness with a cubic B-spline
(`Grid2D.SampleSmooth`, continuous in value and slope), so slopes show no creases along the 8-cell plan grid. On top
sits a gentle two-octave detail of at most 2 cells. Editing a relief value changes world output, so it needs a
`GenVersion` bump and re-pinned golden hashes. `ReliefTests` keep inland slopes outside mountains below 0.9 and the
hills around spawn from flattening.

**World mutations** (hardmode) are a third, lazy tier: an ordered log of steps saved with the world, each a pure
function of (world, mutation seed, chunk, blocks). `WorldSession.Produce` applies whatever a chunk is missing (freshly
generated chunks start at level 0; saved chunks store their level), and loaded chunks are caught up live, so a region
first visited long after hardmode looks exactly as if it had been loaded at the moment the world changed. Ore veins
for both generation and mutations come from the same `VeinStamper`.

Inspect with `dotnet run --project tools/Strata.Tools -- worldgen-report --seed=N --size=tiny`
(top-down biome map, cross-sections, ore-by-depth charts).

**Terrain metrics** (`WorldGen/Analysis`). `SurfaceProfile.Measure` generates the chunks around the surface of a
window (per chunk column, surface bounds sampled every 4 cells) and records per column the highest solid cell (Top),
scanning from `SurfaceHeight` + 24 down to − 24, plus flags: *lip* (topmost solid run ≤ 6 cells thick over open space:
overhangs, thin roofs), *floating* (solid cells with ≥ 2 open cells under them within 12 cells of Top, counted),
*breach* (Top more than 3 cells below `TerrainField.SurfaceHeight`: trenches, caves breaking through) and *water*
(liquid on Top). `TerrainStats` aggregates a histogram of |ΔTop| between 4-neighbours (0/1/2/3/4–7/8+), p95/p99/max
step, lip %, floating cells per 10k columns, breach %, water %, roughness (mean |second difference| of Top on both
axes), relief (mean standard deviation of Top in 128² blocks, which guards against "smoothing" by flattening) and the
same per surface biome (≥ 256 columns). worldgen-report measures a `--terrain-window=N` square (default 512, centred
on spawn, clamped to the world; 0 skips it), adds a `terrain` object to stats.json and paints `terrain_hillshade.png`
(1 px per column, lit from the north-west, biome map colours) and `terrain_flags.png` (grey hillshade; red lips,
yellow floating, blue breaches, cyan water; white dot = spawn). The metric definitions are frozen so reports stay
comparable: never tune them inside a terrain change.

Evidence for a terrain change: run the report on the parent commit with `--out=build/wg-before`, then on the change
with `--out=build/wg-after --compare=build/wg-before/stats.json`, which prints metric / before / after / delta
(overall, step buckets and per biome) and writes it to `terrain_compare.txt`. `build/` is gitignored, so paste the
table and the two terrain PNGs into the PR or commit message. Options take `--key=value` or `--key value`.
