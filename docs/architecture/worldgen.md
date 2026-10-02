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
regenerate the rest with the new generator, with a logged warning).

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

## Blueprint op index and zone fast path

`Blueprint.Freeze()` (called by the `StructureFeature` constructor, idempotent and thread-safe) builds an immutable
per-chunk index: every op is listed under each chunk whose padded generation volume (core plus `ChunkGenContext.Pad`)
its bounds touch, in original op order. `FeatureCarver.Stamp` and `StructureFeature.InZone` only visit
`OpIndicesFor(chunk)`, so a dungeon of thousands of ops costs a dictionary lookup per chunk or zone query. A frozen
blueprint rejects new ops. `CarveOp`/`FillOp` carry `NoZone`: approach cuts and steps outside a building are stamped
but are not "in the structure" for spawns, music or mining locks. The index changes no output (golden hashes
unchanged; `BlueprintIndexTests` compares it with brute force).

## Dungeon layout planner

`WorldGen/Structures/Dungeon/` plans the Terraria-style dungeon as pure data (`DungeonPlanner.Plan(site, budget,
seeds)` returns a `DungeonLayout`), which `DungeonPass` carves (see Dungeon carving below). `DungeonSite` gives the gate (ground cell at the keep centre), the inward axis, world size, layer
bands and surface/landness samplers; `DungeonBudget.For(size)` sets trunk length, room/chest/branch quotas and the
floor limit (`CavernTop - n`, never below `UnderworldTop + 40`).

* Keep (room 0, 20 x 16 x 20) and an entrance shaft (hall 0, 10 wide) to a 16-wide junction (node 1) under the lowest
  ground near the keep. `Door` is the floor cell in the keep wall facing `Inward`.
* Trunk walk: flat 16-40 / stairs 12-28 (one cell down per step, one interior box per step) / shafts 16-32 (8 x 8,
  dug just outside a node box's wall), weights 0.5 / 0.35 / 0.15. It keeps its heading (0.5) or turns 90 degrees,
  never against `Inward`, never twice through the same wall, and paces its descent over its length. Halls are 7 wide
  with 9 of headroom. A node gets a room (14-26 x 10-16 x 14-26, more often while behind the room quota) or a
  10 x 12 x 10 junction; a horizontal arrival enters through the box's near wall, a shaft through its ceiling. When
  stuck short of its length or room quota, the trunk resumes from an earlier node (a fork).
* Every interior must stay 12 cells inside the world, above the floor limit and, outside the keep footprint, 12 cells
  under the ground (sampled every 6 cells) and under land. Overlaps: `OccupancyGrid` (8-cell broad phase, exact boxes
  with a 2-cell wall gap) except with the pieces at the node a hall leaves from.
* Branches (p 0.3 per trunk node, 80-160 cells) end in a room; dead-end junctions grow into rooms until the room quota
  is met; 35% of flat halls of 24+ get a side room behind a 2-cell wall with a 3 x 5 doorway.
* Roles (`dungeon.roles` stream, geometry never depends on it): the deepest room is the Vault (6 `VaultChestSpots`
  against doorless walls), the room nearest the median depth is the Mechanic room, the `bound_npc` site for the bound
  Mechanic (`MechanicCell`), branch ends are Treasure or Library, then libraries (1 per 4), spike pits (1 per 5),
  treasure until the locked-chest quota, barracks. `ChestSpots` (one per treasure room, chest alcoves, extra spots
  until the quota) carry exactly one `ShadowKey`. Rooms list their `Doorways` (cuts through the 2-cell shell).
* Seed streams `dungeon.trunk`, `dungeon.branches`, `dungeon.side`, `dungeon.roles`; integer geometry only. A Large
  plan takes a few milliseconds. `DungeonLayoutTests` check determinism, reachability, cover, floor, quotas, stairs,
  headroom and that interiors only meet where joined.

## Dungeon carving

`DungeonPass.Plan` picks the site (the snow-side angle ±0.07 at 0.86 of the land radius, stepping inward while landness
< 0.97; inward = the dominant axis toward the centre; it only samples `SurfaceHeight` and `Landness`), plans the layout,
picks a `DungeonStyle` (blue, green or pink brick with its brick / slab / tiled wall panels) and adds a `DungeonFeature`
(a `StructureFeature` carrying `Layout` and `Style`). Landmarks: `dungeon_gate` (ground 3 cells out of the keep door,
outside the zone), `dungeon_bottom` (the vault floor), `dungeon_chest` (in front of the first locked chest).

`DungeonBuilder.Build(content, layout, style)` emits ops in phases so crossings merge: A shells (`interior.Expand(2)`
brick for every hall step, shaft, room and the keep), B panels (the side faces of `interior.Expand(1)` over the
interior's height; floors and ceilings stay brick), C carves (every interior, every doorway), D fixtures:

* Stairs: the ground row under every step but the last is a brick `CellShape.Stair` rotated uphill
  (`DungeonBuilder.RotationToward`: rotation 0 faces +Z, 1 -X, 2 -Z, 3 +X, as ShapeGeometry), so each one-cell drop is
  two half steps; brick is built up under a step that passes over a node box's air.
* Shafts: one-way brick platforms every 4 cells (the first 3 over the bottom floor, the top one at most 4 under the
  floor the shaft leaves, on that node's side), one quadrant each, winding round the shaft like a spiral stair: the
  next platform up is beside the one you stand on (a 4-cell jump), never above the next one down, so walkers (and the
  navigator) step down with full headroom.
* Rooms wider than 18 get four 2 x 2 pillars at their quarter points (left out where they would touch a hall,
  doorway, chest or the Mechanic spot); rooms taller than 12 get 2-deep platform galleries at +6 along the long walls,
  broken at doorways, each with a step platform at +2.
* Gold chests on the planned `ChestSpots` (the Shadow Key spot rolls `dungeon_chest_shadow_key`).

Every cell is flagged `CellFlags.Structure` (locked until Skeletron falls). Later passes leave structure cells alone
(`ChunkGenContext.IsStructure`): cave/nest/chasm carvers skip them, treasure and decoration do not place on them, and
the dungeon stamp drops objects earlier passes left inside its zone. A Large dungeon is ~6000 ops, planned and built in
~15 ms. `DungeonGenerationTests` check shells and flags, platforms, stair shapes and walking, reachability on foot from
the entrance junction and open crossings on the real seed-1234 Tiny world.
