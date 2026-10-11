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
5 caves (the cave network, cheese/spaghetti lattices + plan worms) · 6 large caverns · 7 underground rivers · 8 ores ·
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

## Cave network (GenVersion 15)

The caves pass plans a network of rooms and passages (`WorldGen/Caves`, `CaveNetworkPlanner`, tuning in `CaveTuning`)
on four levels, each a band of normalized depth: the underground (0.10–0.28; rooms also keep 16 cells of rock under
the ground), the upper caverns (0.36–0.52), the deep caverns (0.57–0.75) and the abyss over the underworld (0.80–0.90).
Rooms sit one per square of a jittered grid (spacing 84/92/100/116 cells by level) on land, away from spawn on the
first level, dropping any that would crowd a room already placed. Each level has its own kinds (`CaveRoomKind`):
halls, long galleries and mossy grottos round a pool in the underground; tall domes over a great stalagmite, lakes with
a dry shore, terraced bowls and grottos below; vast pillared halls, crystal caves, domes and lakes deeper; magma
chambers with lava pools and pillared halls in the abyss. A room (`CaveRoomFeature`) is a rotated elliptical footprint
with a flat floor row (`FloorY`, open; the row under it rock), a vaulted roof (`Roof` 2–4 from round to nearly flat),
a fillet where floor meets wall, and floor sinks for pools and terraces (`SinkAt`).

Passages (`CavePassageFeature`, straight stretches between floor points) link each level's rooms to their three nearest
neighbours: a spanning tree plus 30% of the remaining links as loops (skipping links that would crowd a door). They
meander (a bend every 22 cells) and arch over a flat floor no steeper than `MaxSlope` (0.56, one-cell steps), so the
whole network can be walked. Descents join each level to the one above (22% of the lower rooms, at least one per
connected piece): where the drop is too steep they switch back in legs of 26–36 cells at slope 0.5 with a small
landing room at each turn. A share of the underground rooms gets a walk-in entrance from the surface (not in Don't Dig
Up worlds); 35% of rooms get a dead-end spur to a treasure niche, and some get a side alcove. Doors sit at 0.72 of the
footprint on the floor row, so passages meet rooms level.

`CaveFurnisher` dresses the plan: pools (a lake's water, a grotto's pond, a magma chamber's lava), formations
(stalagmites, stalactites and floor-to-roof columns; a dome's great stalagmite; a pillared hall's colonnade; crystal
clusters in crystal caves) kept clear of doors, and treasure: a chest by level and biome (`CaveChests`: the biome's
wooden-tier chest in the underground, Gold Chests below and in every niche, Obsidian Chests in the abyss) and pot
clusters. Everything is plan data; `CaveCarver` builds it per chunk inside pass 5. It marks the cells each room and
passage opens (warped by a λ18 noise so walls are not perfect ellipses), fills pools, and protects a rock shell round
every room (4 cells thick, from under the floor to a share of the roof by kind, whole for domes, grottos, crystal caves
and niches) and the rock under passages, which the older noise caves then carve around, so rooms keep their floors and
shapes while the noise caves still break in high on the walls of the great halls. Then it opens the network, dresses
the walls by room kind (moss and glowing argon moss, crystal and gems, obsidian, ash and hellstone, silt under water)
and grows the formations. The treasure pass stands each planned chest and pot on the floor (searching a few rows if a
later pass changed it) before scattering the random ones, which are now about a third as many chests and half as many
pots, the wooden-tier ones themed by biome.

Chest loot (`data/loot/containers.json`) ranks by chest type and grows with depth: `cave_chest` (wooden-tier) below
`gold_chest` below `obsidian_chest` (always one of `deep_chest_primary`'s rarer treasures), and every underground
table's coins and stacks grow with world depth (`depthScale`), while entries with a `depthBonus` (deep Gold Chest
treasure, healing potions, gems) grow likelier deeper. `ConditionContext.WorldDepth` (0 at sea level, 1 at the
underworld's top) carries the depth, set by `WorldSession.ContextAt`; the `worldDepth` condition reads it.

**Cave census** (`CaveProfile`): worldgen-report `--cave-window=N --cave-offset=M` measures an N-cell square M cells
east of spawn from the underworld's top up to the surface: open share per layer, how the open space hangs together
(pieces, the share the sky reaches, pockets under 512 cells), floor and hall-floor shares (a player's headroom of 4 and
16 cells), cramped spots, walk-in and round-trip shares, and the chests and pots by layer. The report also paints
`cave_network.png` (rooms by level, passages, entrances white, chests yellow) and `rooms/room_L*_*.png` (for the room
of each kind nearest spawn, a section along its long axis over a plan slice two rows above its floor), and lists those
rooms and the entrances nearest spawn in stats.json (handy for aiming screenshots). `CaveNetworkTests` pin the levels,
walkable slopes, connectivity and the planned chests.

## Blueprint op index and zone fast path

`Blueprint.Freeze()` (called by the `StructureFeature` constructor, idempotent and thread-safe) builds an immutable
per-chunk index: every op is listed under each chunk whose padded generation volume (core plus `ChunkGenContext.Pad`)
its bounds touch, in original op order. `FeatureCarver.Stamp` and `StructureFeature.InZone` only visit
`OpIndicesFor(chunk)`, so a dungeon of thousands of ops costs a dictionary lookup per chunk or zone query. A frozen
blueprint rejects new ops. `CarveOp`/`FillOp` carry `NoZone`: approach cuts and steps outside a building are stamped
but are not "in the structure" for spawns, music or mining locks. The index changes no output (golden hashes
unchanged; `BlueprintIndexTests` compares it with brute force).

## The Dungeon

The Terraria 1:1 Dungeon (GenVersion 11, on the Phase 1 relief of version 10) is one `DungeonPass`: `Plan` picks the
site, plans the layout (pure data), builds the blueprint in phases, decorates it, plans its wires and landmarks, and
adds a `DungeonFeature`; `Generate` stamps the blueprint chunk by chunk. Each step below is deterministic from the seed
tree and integer-only. Tests: `DungeonLayoutTests` (synthetic sites), `DungeonGenerationTests` and
`DungeonDecorTests` (seed 1234 Tiny), `DungeonSiteTests` (seeds 1-12 Tiny on the real relief), `DungeonTests`
(zone, locks, guardian, keeper), and the dungeon chunks among the golden hashes. The smoke `dungeon.cmds` walks the
arc by landmarks.

### Layout planner

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

### Site, carving and the keep

`DungeonPass.Plan` picks the site (the snow-side angle ±0.07 at 0.86 of the land radius, stepping inward while landness
< 0.97; inward = the dominant axis toward the centre; it only samples `SurfaceHeight` and `Landness`). The Phase 1
relief rolls, so `DungeonPass.FlattestSite` then tries the spots 0/12/24/36 cells further inland and 12/24 to either
side (land only) and keeps the one whose keep, apron and approach (20 back, 34 in front, 20 to each side, sampled every
3 cells) span the least relief, the planned spot winning ties. It plans the layout,
picks a `DungeonStyle` (blue, green or pink brick with its brick / slab / tiled wall panels) and adds a `DungeonFeature`
(a `StructureFeature` carrying `Layout` and `Style`). Landmarks: `dungeon_gate` (ground 3 cells out of the keep door,
outside the zone), `dungeon_bottom` (the vault floor), `dungeon_chest` (in front of the first locked chest), and for
the smoke route and the tour `dungeon_foyer` (inside the door), `dungeon_shaft` (the shaft's foot), `dungeon_library`,
`dungeon_treasure` (room centres), `dungeon_banner` / `dungeon_hall` (the first hall banner and its hall's floor) and
`dungeon_spikes` (the first spike, when there is one). `aim landmark <id> [dy]` faces one.

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
ground, so the keep never floats. Before the shell, `DungeonBuilder.Clearing` cuts any ground that still rises over the
floor round the keep (a hillside the flattest site touches): NoZone carves one layer per cell over the floor, each
reaching 3 + its height beyond the footprint (a 1:1 bank, at most 24 layers, none on level ground), so the walls stand
free. The approach is NoZone: a level landing past the apron, then rows at most one cell
lower each until they meet the ground (brick steps where the ground is lower), the first 12 rows cut clear 10 wide
and 12 tall; `DungeonBuilder.GateCell` (the landing just past the apron) is `dungeon_gate`, outside the zone.

Every cell is flagged `CellFlags.Structure` (locked until Skeletron falls). Later passes leave structure cells alone
(`ChunkGenContext.IsStructure`): cave/nest/chasm carvers skip them, treasure and decoration do not place on them, and
the dungeon stamp drops objects earlier passes left inside its zone. A Large dungeon is 7000-8000 ops: planned in ~3 ms, built in ~3 ms and
decorated in 25-70 ms. `DungeonGenerationTests` check shells and flags, platforms, stair shapes and walking, reachability on foot from
the entrance junction and open crossings on the real seed-1234 Tiny world.

### Decorator

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

## Don't Dig Up (X-07 secret seed, GenVersion 14)

The `dont_dig_up` secret seed generates Terraria's upside-down world by reordering the plan's layer bands, not by
flipping finished chunks: every pass still builds in real orientation (+Y up, normal gravity), so floors are floors,
liquids pool at the bottom of their basins, grass grows on top of ground and trees stand upright.

**Layout** (`LayerBands(height, inverted: true)`, fractions of the world height): bedrock at y 0–2; the ground under
the bottom surface; the surface around `SeaLevel` (0.20) with open air above it (`SkyStart` 0.29 starts the Sky layer)
up to the rock roof at `UndergroundTop` (0.37); the underground and cavern layers (`CavernTop` 0.49); the Underworld
above `UnderworldTop` (0.82): an ash floor, the lava sea at `LavaLevel` (0.86), the cavity up to about
`UnderworldCeiling` (0.955) + 10, a thin ash crust and bedrock in the top three rows. There is no Space layer
(`SpaceStart` = the height): the open air is under rock. `LayerAt` returns Underground for the ground below
`SurfaceFloor` under the bottom surface.

**Depth.** `NormalizedDepth` keeps the ordinary values at each layer boundary (0 at sea level, the ordinary
underground/cavern values at their tops, 1 at `UnderworldTop`) but grows upwards above the roof; under the bottom
surface it grows downwards as usual, reaching about the cavern's depth at the bedrock. So every depth-driven placement
(`YForNormalizedDepth`: caverns, lakes, rivers and lava tubes, ores, hives, the temple, spider nests, the Aether,
mini-biomes, cabins, mineshafts) lands in the matching layer unchanged. Features dug down *from the surface* (worm
tunnels, evil chasms, hardmode wedges) use `DescentY`, which in an inverted world bottoms out in the ground under the
bottom surface; shafts and sinkholes are clamped above the bedrock. Ore veins test each vein's depth (depth is not
monotonic in y there). `IsDeeper`/`Deeper` express "deeper than" comparisons (strata lines).

**Per pass.** Density adds the roof: `TerrainField.CeilingHeight` (the underground's base plus up to 16 cells of noise,
at least `RoofClearance` = 48 cells above the ground; +infinity in ordinary worlds). "Cells below the surface" becomes
`SurfaceDepth` — below the bottom surface as usual, and above the open air the height into the roof's rock — so soil,
cave roofs (caves stay 5+ cells off the roof's underside), biome materials and grass/moss read the nearest exposed
surface. The roof never holds loose sand or slush (sandstone/ice instead). Biomes: columns read their depth below the
bottom surface or into the roof's rock, so the underground jungle and ice lie above the jungle and the tundra; the Sky
biome is the upper open air; the ocean's column runs up to the cavern layer. The Underworld pass builds the same
floor, lava sea and ruined towers under the world's top, all ash around the cavity. Floating islands float in the
open air between `SkyStart` and the roof (skipped where they would not fit). The Dungeon stands on the bottom surface
and descends into the ground under it, its deepest floor at y 14 (`DungeonBudget.InvertedFloor`) — reachable on foot
from the surface like an ordinary one. Ocean, structures on the surface (living trees, pyramids, cabins), decoration
and liquids need no changes.

**Spawn.** The player starts in the Underworld near the top (as in Terraria): `UnderworldPass` spirals out from the
centre for a column of dry ash floor with no lava sea within 12 cells, level ground, 14+ cells of headroom and no
ruined tower nearby. The Wall of Flesh is fought in that top cavity (its band comes from `LavaLevel` and
`UnderworldCeiling`).

**Gameplay probes.** `LayerAt` (spawns, music, conditions, the Guide), `ConditionContext.Depth` (`SurfaceDepth`), cave
finders (`CaveRange`) and the camera's environment depth all read the inverted layout. Light: there is no open sky;
`SkyMap` gets the roof and lights the open air between the ground and the roof as outdoor shade (`ShadeTop` up to
`ShadeCap`; the caves above stay dark); the map treats that air as outdoors. Gravity is unchanged.

**Compatibility and gaps.** Ordinary seeds generate identically (golden hashes unchanged); the plan hash includes the
flag. A Don't Dig Up world saved before GenVersion 14 keeps the ordinary layout (`WorldMeta.ToSettings`). Not
inverted: weather (no rain or snow under the roof — the sky top is the world's top), invasions and meteors arrive at
the bottom surface, hardmode wedges reach down from the bottom surface only, and nothing renders a sky above the roof.
