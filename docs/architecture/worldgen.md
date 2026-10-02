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

**Surface shape.** The ground is a heightfield plus a small 3D jitter: `TerrainField.Density` adds a two-octave
overhang noise (λ48, y stretched by 0.8) of amplitude `0.6 + 1.4 × ruggedness + 11 × cliff` cells within amplitude + 1
of the surface (`TerrainTuning` holds these generator constants). Outside cliff zones the amplitude stays at most about
2 cells, so the density falls monotonically with height: one surface per column, no fins, floating blobs or pits.
Cliffs and undercuts appear only in rare deliberate cliff zones (`WorldPlan.Cliffs`, planned by `BiomeRelief`): where a
λ520 mask peaks, times the biome's `cliffs` affinity (blended towards 0.8 in mountain ranges), kept 180+ cells from
spawn and off the beaches, about 5% of the land on Small. There the noise is also terraced (steepened 2.5× and
clipped), so the ground forms shelves between steep walls, with an undercut in about 1% of the columns. `ChunkGenContext` caches each column's cliff strength and
overhang amplitude; below surface − amplitude every cell is solid, so the noise caves measure their roof from there
(at least 5 cells under the lowest the surface can dip). Worm tunnels: about 30% are cave mouths that start one cell
under the ground, head into rising ground and dive 25–36° for three segments, a hole into the hillside rather than a
trench; the rest start 14–40 cells down. Chasms, shafts and sinkholes stay deliberate surface openings.
`TerrainShapeTests` pin these properties.

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

The keep (`DungeonBuilder.Keep`, emitted in the same phases): the 20 x 16 x 20 hall behind walls 3 thick (2 brick +
an inner panel), the inland-facing front 2 cells thicker, a 2-cell roof with merlons, four corner turrets (cylinders
r 3, 24 tall, stepped caps), an arched door 6 x 9 (+ 4 and 2 wide at +9 / +10), two slit windows and a foyer of four
pillars round the entrance shaft's 10 x 10 opening. The foundation fills brick under the walls, turrets and a 3-cell
apron from 6 under the lowest ground (at most 24 under the floor) and, in 4 x 4 blocks, 2 cells into each block's own
ground, so the keep never floats. The approach is NoZone: a level landing past the apron, then rows at most one cell
lower each until they meet the ground (brick steps where the ground is lower), the first 12 rows cut clear 10 wide
and 12 tall; `DungeonBuilder.GateCell` (the landing just past the apron) is `dungeon_gate`, outside the zone.

Every cell is flagged `CellFlags.Structure` (locked until Skeletron falls). Later passes leave structure cells alone
(`ChunkGenContext.IsStructure`): cave/nest/chasm carvers skip them, treasure and decoration do not place on them, and
the dungeon stamp drops objects earlier passes left inside its zone. A Large dungeon is ~6000 ops, planned and built in
~15 ms. `DungeonGenerationTests` check shells and flags, platforms, stair shapes and walking, reachability on foot from
the entrance junction and open crossings on the real seed-1234 Tiny world.

## Dungeon decorator

`DungeonDecorator.Decorate` (run by `DungeonPass.Plan` between `DungeonBuilder.Build` and the feature, rng stream
`dungeon.decor`) appends object ops and small fills to the open blueprint. `BlueprintProbe` answers what a cell will
be once stamped (ops scanned last to first over a 16-cell index; the decorator's own fills and carves are added as it
goes), so every placement is checked: footprint in carved air, floor objects on full brick, wall objects against solid
wall, ceiling objects under solid ceiling, books on platform shelves. A global occupied set keeps objects from sharing
a cell across chunk borders; reserved cells keep doorways (3 cells round), hall mouths and shafts through rooms, the
bound Mechanic's spot and the vault's six chest spots clear. Budgets per anchor chunk: 40 objects, 6 lights, 64 spikes
(hazards are counted apart: one MultiMesh per chunk).

* Rooms by role, in the dungeon colour's set (`<colour>_dungeon_<piece>`): libraries (3-6 bookcases, platform shelves
  6 long at +3/+6/+9 with books on 60% of their cells, a table with a chair and a candle; about 1 book in 40, and at
  least one per dungeon, carries `PendingLoot` `water_bolt_book`), treasure rooms (the centre chest stands on a 1-cell
  brick dais, `DungeonBuilder.OnDais`; statues and candelabras either side, a banner behind), barracks (2 tables with
  chairs, vases, pots, a chandelier), spike pits (two rows of spikes across the middle under a 3-wide platform bridge
  at +3), plain halls, alcoves, the vault (light and banners; chests are P4-34's) and the keep foyer (2 chandeliers,
  4 banners, 2 statues inside the door).
* Mechanic room (`RoomKind.Mechanic`, the `bound_npc` site for P5-06b): a 5 x 5 x 5 box round `MechanicCell` is kept
  empty of objects and fixtures (pillars already avoid it); the room gets a chandelier, a table with a chair and a
  candle off to one side, a bookcase and a banner, so the bound Mechanic stands in a lived-in room. SiteFinder should
  use `DungeonLayout.MechanicCell`.
* Flat halls: banners (both walls), lamps and candelabras in turn every 12-16 cells, 1-3 pot clusters, cobwebs (2x2x2,
  only-air fills) in upper corners at 40%; 15% of halls of 20+ get a 3-long spike strip across the hall, else 10% of
  halls of 16+ get a pressure plate on the centre line and a dart trap set into the side wall at +2 (its panel cell
  carved, brick left behind) aimed across. The wire runs under the floor to the wall and up to the trap; the cells go
  to `WorldPlan.PlannedWires`, which `WorldSession.Create` copies into `Session.Wires` (new worlds only).
* `DungeonFeature.ObjectIds` lists the object ids placed; `TileObjectRenderer` queues their models
  (`PropLibrary.Preload`, one GLB per frame) once the player is within 64 m of `dungeon_gate` or inside the zone.
* `DungeonDecorTests`: no overlaps and every op installed, support (wall/ceiling/floor) on stamped chunks, libraries and
  the Water Bolt, treasure chests, the Mechanic's spot, budgets, plate-to-trap wire BFS, and a plate firing its dart.
* Vault biome chests (P4-34): `DungeonDecorator.VaultChests` puts the jungle, corruption, crimson, hallowed, frozen and
  desert chests (`data/tileobjects/biome_chests.json`) Locked on the six `VaultChestSpots` (footprint clamped as
  `DungeonBuilder.ChestAnchor`), each rolling its `<biome>_chest` table (the P4-33 weapon). Their locks need the
  matching biome key (`data/items/biome_keys.json`; `global_kill` drops each at 1 in 2500 in hardmode in its biome)
  and refuse until `defeated_plantera`.
