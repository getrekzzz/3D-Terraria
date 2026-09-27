# 3D Terraria-Style World — Master Plan (persistent project memory)

> Update this file at the end of every development cycle. It is the single source of truth for what
> exists, what is partial, what is missing, and what comes next. Last update: 2026-09-27 (cycle M4, player feedback: nights, light, lava, caves, performance).

## 1. Vision
A native-3D evolution of Terraria's design philosophy — depth, interconnectivity, freedom, discovery,
progression structure and systemic interaction — not "voxels with Terraria items". One 0.5 m voxel grid,
smooth natural terrain and crisp player building, a Terraria-shaped world (spawn centre, snow/jungle on
opposite sides, ocean ring, evil patches, sky islands, caverns, underworld), data-driven content and a
world that is played in real 3D (vertical caves, overhangs, tunnelling bosses).

## 2. Decision log
| Date | Decision | Rationale |
|---|---|---|
| 2026-09-25 | Godot 4.7.2 .NET (C#, net8.0) + Blender 5.2 (`bpy`) | User choice. Compiled C# for voxel work; testable engine-agnostic core. |
| 2026-09-25 | Hybrid terrain: one 0.5 m grid; natural materials smooth (constrained surface nets), built materials crisp | Organic caves + precise building from one data model. |
| 2026-09-25 | Original names; `data/reference/` maps Terraria concepts; `covers` links measure coverage | Distributable; progress is measurable. |
| 2026-09-26 | Terraria's own names for everything (ids and display names); `data/config/aliases.json` maps old ids for old saves | The player's choice: a personal build played for fun. |
| 2026-09-25 | `Strata.Core` has no Godot dependency; `GameWorld` runs the whole simulation headless | Unit/integration tests, deterministic, tools reuse it. |
| 2026-09-25 | Lighting is a pure function of a 3×3×3 chunk neighbourhood + sky map, computed in mesh jobs | No global incremental light state; 4-bit light reaches ≤15 cells so the neighbourhood is exact. |
| 2026-09-25 | Save only modified chunks, regenerate the rest; lazy world mutations with per-chunk level | Small saves; hardmode reaches unexplored chunks for free. |
| 2026-09-25 | Player body = Godot CharacterBody3D on the smooth collision mesh; creatures, drops and projectiles = Core AABB voxel physics | Visually exact player movement; everything else simulates headless and at any distance. |
| 2026-09-25 | Props/items/creatures generated in Blender from `tools/blender/assets.json`; materials are texture-layer names, the game supplies the shader | One consistent PBR look with terrain; assets regenerate deterministically. |
| 2026-09-25 | Engine behaviour is validated with command scripts (`game/Testing/Scripts/*.cmds`) run headless in CI | The same command registry powers the debug console, tests and screenshot automation. |
| 2026-09-25 | Liquids simulate only *active* cells; generated liquid is static until disturbed (edits wake it), saved chunks are rescanned on load | Lazy per-chunk generation cannot settle liquid globally like the reference game; this keeps loading cheap and flow local to play. Pass 20 removes liquid hanging over carved space (generator v2). |
| 2026-09-25 | Town residents are a separate Core entity (not enemy NPCs) with A* paths, homes and trade | Friendly, persistent, schedule-driven; never hit by player attacks. |
| 2026-09-25 | Zones (crypt, spider nests, marble/granite halls, evil hearts) are planned features reported as extra biomes in every condition context; a structure zone is its blueprint's volumes, not its bounding box | Spawn rules, locks, music and loot all read one context; a sprawling crypt must not claim the open surface above it. |
| 2026-09-25 | Progression locks are data: a biome's `lockedUntil` flag protects its generated structure cells; `exclusiveSpawns` zones only spawn rules naming them, at closer range | The Dungeon behaves like the reference dungeon without special-case code. |
| 2026-09-25 | Progression flags are raised only through `ProgressionSystem`; `autoSet` flags follow from their requirements (defeated_mawwall → hardmode) and may announce themselves | One place for consequences (hardmode mutations hook on `Raised`), no special cases in boss code. |
| 2026-09-25 | The Wall of Flesh is an enemy whose brain sets a `Barrier` volume (drawn as tiled flesh, hurts and ejects players behind it) and tethers minions (`Leader`); summoned by burning an effigy (`incinerationSummon`) in underworld lava | A 3D wall of flesh that cannot be walked around (it tracks the target's Z) built from generic parts. |
| 2026-09-25 | World mutations are an ordered log of (id, seed) in world.json; each step is a pure function of (world, seed, chunk, blocks). Produce applies the missing levels on workers (freshly generated chunks start at level 0), loaded chunks catch up a few per tick as ordinary edits | Hardmode reaches every chunk, visited or not, with identical results whether applied at load or live; saves only store chunks players touched. |
| 2026-09-25 | Open air under a floating roof (island, arch, high canopy) is lit as outdoor shade (sky 12) via a per-column `ShadeTop` in the sky map | The land under sky islands was pitch black at noon (sky light is column-based and spreads only 15 cells); caves and rooms are unaffected. |
| 2026-09-25 | Ore veins are placed by one shared `VeinStamper` for generation and mutations (verified bit-identical to the old pass); hardmode ores are `generate: false` vein rules | No second ore algorithm; alternates are chosen per world from the mutation seed without disturbing generation's random stream. |
| 2026-09-25 | Landmark residents (the Dungeon keeper) live at a planned landmark instead of a house and leave for good when `leavesWhen` holds; boss summoning can be a resident service | Old-man/clothier arc expressed with the town system and data. |
| 2026-09-26 | Physically based lighting: single-scattering sky (Rayleigh/Mie/ozone) drives sun colour, sky ambient and reflections; the baked voxel sky exposure becomes per-surface AO (caves get no sky light); block light stays baked emission and yields to daylight; GGX with cloud shadows | Realistic outdoor light without losing the voxel game's dark caves; no global illumination bake to maintain. |
| 2026-09-26 | Graphics presets are data (`data/config/graphics.json`, validated in Core) applied by `GraphicsSettings`; `ultra` is the default | Every knob tunable without code; CI and software rendering pick lighter presets. |
| 2026-09-26 | Textures are synthesised in numpy (height in albedo alpha, detail array); terrain blends its three surfaces by height | Deterministic, license-free, regenerable materials with realistic transitions. |
| 2026-09-26 | People are generated procedurally in Blender (metaball anatomy, sculpted head, jointed hands, clothing shells, 52-bone rig) with a dedicated character shader | Detailed humans from the same reproducible pipeline. Reading the CC0 MakeHuman/MPFB2 assets was not permitted in this environment; that route (a MakeHuman base mesh + our clothing and rig) remains an option if approved. |
| 2026-09-26 | Grass is scattered in the Core mesh jobs (deterministic, per grass block kind from `data/config/grass.json`) and drawn as per-chunk MultiMeshes whose shuffled prefix is the distance LOD | Grass follows edits and remeshes for free, costs nothing when switched off, and thins evenly with distance. |
| 2026-09-26 | Tree crowns and bushes are alpha-tested leaf cards (`card:<layer>`, sprig textures) with crown-shaped custom normals | Leafy, see-through silhouettes at ~2k triangles per tree instead of solid blobs. |
| 2026-09-26 | Vertex colours are written as sRGB (bmesh bytes / `color_srgb`) | glTF exports linear colour; writing linear floats made painted skin and clothes pale. |
| 2026-09-26 | Hardmode stations are tier-table data (`barStation`/`craftStation` per metal): cinder forge → hardmode anvil → hardmode forge | The station chain stays one table edit; better stations list the stations they replace. |
| 2026-09-26 | Far terrain is a generator-sampled polar heightfield (horizon) rather than coarse voxel chunks | Distance reads right at ~22k vertices with no extra chunk generation; voxel detail (caves, builds) only matters close by, where real chunks are streamed. |
| 2026-09-26 | Boss pairs are `companions` sharing one defeat flag (spawned together, one announcement and bar, flag on the last death); second phases are `Npc.Enraged` + `ranged.enraged` | The Twins need no special-case code, and any brain can switch attacks by phase. |
| 2026-09-27 | Natural spawning follows the reference rates (1 attempt/s: 10% day, 17% night), counts hostiles and critters separately and never spawns hostiles on safe ground (30 m around the world spawn and beds; torch-lit surface outside events); every resident nearby slows it | A fresh character's first nights were far harder than the reference game's; safe ground is the 3D stand-in for the reference game's off-screen spawning and walled houses. |
| 2026-09-27 | Knockback replaces a creature's motion and makes it reel (no steering) for 6 + 2·kb ticks; bosses only take the push | Knockback was erased within a few ticks by the AI's steering, so crowds pinned the player. |
| 2026-09-27 | Expert/Master multiply creature life and damage ×2/×3 (`DamageModel.EnemyScale`) | The menu promised tougher enemies; only player defense scaled before. |
| 2026-09-27 | Dynamic lights are a preset-sized pool (`DynamicLights`) shared by placed lights, lava hotspots and glowing creatures; flames are a data spec (`flame` on tile objects) drawn by `flame.gdshader` | Baked 4-bit light stops at 7.5 m and every glowing creature carried its own light (fireflies alone could add dozens). |
| 2026-09-27 | Lava is its own opaque mesh surface and shader; the mesher lists sampled lava surface cells for embers and heat lights | Realistic lava needs its own look and needs no refraction; the transparent pass and screen copy were wasted on it. |
| 2026-09-27 | Sealed chunks sit on a visual layer the sun and moon shadow maps skip (`Light3D.ShadowCasterMask`) | Torch shadows had forced every cave chunk back into the 4-cascade sun shadow passes on high/ultra. |
| 2026-09-27 | Cabins stand in the cave tunnels with doorways along the tunnel; one or two rooms; Gold Chests in the cavern layer (also loose, with pots) | Buried cabins were only found by digging; the reference game's caves are dotted with cabins and gold chests. Generator version 9. |

## 3. System status
Legend: ✅ complete for its milestone · 🟡 partial · ⬜ missing

| Area | System | Status | Notes |
|---|---|---|---|
| Foundation | Solution, projects, CI | ✅ | Core tests (Linux; +Windows on PR/main), content/asset/reference checks, Godot headless smoke job |
| Foundation | Determinism | ✅ | SplitMix/PCG32, name-derived seeds, no trig/pow in worldgen, golden hashes |
| Content | Loader, registries, validator, manifest | ✅ | Strict JSON, typed specs, DAG checks, obtainability, conventional asset paths |
| Content | Reference DBs + coverage | ✅ | 834 concepts / 12 DBs (incl. liquids), `Strata.Tools coverage`; overall 54.3 % (items 56 %, enemies 32 %, bosses 28 %, NPCs 21 %) |
| Voxels | Chunks, edits, raycast, tile objects | ✅ | COW chunks, EditBatch, multi-cell objects with containers |
| Persistence | Regions, codec, id tables, migrations, players | ✅ | Atomic writes + .bak, v0→v1 migration, player saves, autosave |
| WorldGen | 20 passes, plan + chunk tiers | ✅ | Structures: cabins, mineshafts, living trees, pyramid, crypt (zone + locked walls + gate landmark) |
| WorldGen | Report tool | ✅ | Maps, sections, ore/depth charts |
| Lighting | Sky + RGB block light | ✅ | Stateless per chunk; outdoor shade under floating roofs; liquid light (lava) from data; sampled by props/creatures via LightProbe; biome fog/sky/ambient looks from data |
| Meshing | Surface nets + crisp shapes + liquids + collision + grass scatter | ✅ | Far terrain is the horizon heightfield (no coarse voxel chunks) |
| Rendering | Physical sky + clouds, sky-lit PBR, graphics presets, terrain materials, water/lava, grass, leaf-card trees, character shader, horizon | ✅ | See `docs/architecture/rendering.md`. Cave culling, particles polish ⬜ |
| Streaming | Jobs, chunk streamer, colliders | ✅ | Adaptive completion budget; diagnostics (`stream`) |
| Player | Controller, camera, avatar, carried light | ✅ | Step-up, variable jump, per-liquid swimming, breath/drowning, fall damage, knockback, blink |
| Interaction | Targeting, mining, placing, objects | ✅ | Brushes, falling blocks, support loss, doors/chests/beds |
| Items | Use table, tooltips, dropped items | ✅ | Inferred actions + explicit behaviours; prefixes/reforging ⬜ |
| Crafting | Recipes, stations, environment | ✅ | Liquids/biome/time conditions |
| UI | HUD, inventory, equipment, chests, crafting, console, perf overlay | ✅ | Settings/menus/world select ⬜ |
| Combat | Damage model, melee arcs, projectiles (friendly + hostile), buffs, contact/death effects | ✅ | Whips/yoyos/summons ⬜ |
| AI | Slime, walker, flyer, swimmer, lunger, caster (blink), boss brains, worm, crypt warden (free hands); ranged attacks; voxel A* | ✅ | A* used by residents; enemy path following, utility AI ⬜ |
| Spawning | Data-driven spawn rules (ground or liquid placement), biome + zone rosters, exclusive zones, per-rule caps, player-position rules, despawn conditions, town suppression | ✅ | Closed doors stop creatures |
| Assets | Blender pipeline, icons, synthesised textures | ✅ | 35 object kinds, 245 item models/icons, 34 creatures; 11 procedural humans (player, 6 residents, shambler, skeleton, imp, hexcaster) with faces, hands, hair, clothing, 52-bone rig |
| NPCs | Town residents, 3D housing, shops, services, happiness, landmark residents | ✅ | 6 residents (guide/merchant/nurse/dryad/old man/clothier roles); more residents, enemies attacking town ⬜ |
| Liquids | Flow, mixing, contact effects, buckets | ✅ | Active-cell automaton per liquid; pressure/U-tubes, sponges, shimmer ⬜ |
| Bosses | Framework + bosses | ✅ | King Slime, The Eye of Cthulhu, Eater of Worlds (tunnels), Skeletron (hands, spin, day rage), The Wall of Flesh (sweeping barrier, eye lasers, tethered hungry maws), Twins (paired eyes, phase-2 lasers and flamethrower), Destroyer (50-segment flying worm, part lasers, probes), Skeletron Prime (four tools, gun arms fire); arenas, boss bar; music state ⬜ |
| Progression | Flags, auto-set cascade, announcements, incineration summons | ✅ | `ProgressionSystem.Raised` is the hook for hardmode mutations |
| Events | Event director | ✅ | Blood moon, slime rain; invasions ⬜ |
| Dungeon | Crypt zone, locked walls, guardian, roster, keeper + curse | ✅ | Crypt keys/locked chests, water bolt loot, spike traps ⬜ |
| Hardmode | Trigger, lazy world mutations, spread, ore tiers, rosters, stations, mechanical bosses | ✅ | Wall of Flesh → `hardmode` → 3 ore tiers (2 alternates each), radiant + blighted wedges (zones), spreading biomes; evil/cinder/hardmode metal families (pick chain 65 → 100 → 110 → 150 → 190 → 200); 12 hardmode creatures; cinder forge, hardmode anvils and forges; Twins, Destroyer, Skeletron Prime → radiant gear; wings, hardmode variants of old creatures ⬜ |
| Audio | Director + sounds | ⬜ | |

## 4. Technical debt
* Creatures use AABB voxel collision while the terrain renders smooth: feet can float up to ~0.25 m on slopes (visual offset fix: ray down to the collider in the renderer).
* `TileObjectRenderer` rebuilds a chunk's MultiMeshes when its light changes; fine now, could update custom data in place.
* Screenshot runs on software Vulkan (llvmpipe) are slow (frame-rate-bound job completion); budget is adaptive now.
* Streaming once failed to reach idle in a rendered run (not reproduced since); `ChunkStreamer.DebugState()` is logged by the screenshot tool to catch it.
* Item held-in-hand orientation is generic (+90° about X); per-item grip data would improve swords/bows.
* Prefix (reforge) data exists but no reforging service/UI yet.
* Generated liquid can still hang across a chunk's bottom face (pass 20 only sees inside the chunk); it stays static
  until disturbed.
* Skeletron hands share the skull's life (the reference boss has separately killable hands).
* The Wall of Flesh only sweeps along X and only its mouth and eyes take hits; its flesh tiles are separate pooled visuals
  (a MultiMesh would be cheaper for very tall cavities).
* Zone membership scans a structure's blueprint ops per query (hundreds for the Dungeon); fine at spawn/lock rates,
  would want a coarse occupancy grid if queried per frame.
* Outdoor shade considers only the first roof in a column (stacked islands shade as one).
* Spreading only visits loaded chunks, and spread land outside a wedge does not count as that zone (zones are the
  wedges); trees, grass tufts and flowers on converted land keep their forest look.
* Spread converts a few cells every half second, so converted borders keep remeshing (intended in play; tours and
  screenshots pause it with `mutations spread off`).
* All bars smelt at the furnace and all metal gear uses the anvil (no hellforge / hardmode anvil and forge stations yet).
* Texture layers are 256² (terrain at 2 m tiles, props at 1 m); 512² would sharpen close-ups at 4× the atlas size.
* Humans are 50–67k triangles each with no authored LODs (Godot's automatic LOD only); crowds will cost. Hair is
  shell-based and reads as a solid mass up close (strand cards would look better).
* Water is drawn in the transparent pass (refraction needs the screen texture), so it gets sky reflections only, no
  screen-space reflections of the shore.
* Grass grows through tile objects standing on grass (chests, stations, tree bases); it only avoids blocks.
* Lavapipe (CI, container QA) cannot run the `ultra` preset at usable speed; real-GPU frame times of `high`/`ultra`
  with grass and leaf cards are not measured yet.
* The horizon samples generator fields: it shows neither caves nor structures, edits or hardmode wedges, and its
  seam with the streamed chunks can show a small step on steep ground.
* Bosses share one life pool per creature: the Destroyer's segments and Skeletron Prime's tools take hits for the
  body (the reference game lets you break Prime's arms and destroys the Destroyer segment by segment).
* Liquids have no pressure: water never rises in a U-tube (as in the reference game), and a flow step is one edit
  batch per liquid, so a large flood remeshes its chunks every few steps (rate-limited by the mesh queue).

## 5. Dependency map
```
Content ─┬─> Voxels ─> WorldGen ─> Lighting ─> Meshing ─> Godot streaming/render
         ├─> Items/ItemUse ─> Crafting ─> UI
         ├─> Combat ─> Creatures/Brains ─> Spawning ─> Events ─> Bosses
         └─> Persistence (world, regions, players, migrations)
GameWorld (Core) = streaming + clock + editor + drops + players + liquids + NPCs + town + melee + projectiles + spawner + events
Commands (Core) ─> console (Godot), smoke scripts (CI), screenshots (--exec)
Progression flags ─> Mutations (hardmode) ─> WorldGen passes (lazy)
```

## 6. Validation status
| Check | Where | Status |
|---|---|---|
| Core unit/integration tests (313, incl. graphics presets, grass scatter, horizon, hardmode life, mechanical bosses, nights, cabins, legacy saves) | `tests/Strata.Core.Tests` | ✅ CI |
| Content validation (refs, specs, assets, graphics presets, grass kinds) | `Strata.Tools validate` | ✅ CI |
| Reference DB freshness + coverage report | `gen_reference.py --check`, `coverage` | ✅ CI summary |
| Asset coverage (every object/item/enemy) | `build_all.py --check`, `AssetCoverageTests` | ✅ CI |
| Module size guard (≤400 lines) | `tools/ci/check_file_length.py` | ✅ CI |
| Engine smoke: boot, stream, land, walk, mine, collect, build, objects, time, save | `basic.cmds` | ✅ CI |
| Engine smoke: contact damage, melee, arrows, death/respawn | `combat.cmds` | ✅ CI |
| Engine smoke: event start/stop, sky tint, kill goals | `events.cmds` | ✅ CI |
| Engine smoke: boss summons, arenas, segments, defeat flags | `bosses.cmds` | ✅ CI |
| Engine smoke: liquid flow, lava + water → obsidian, buckets, burning/dousing, submersion | `liquids.cmds` | ✅ CI |
| Engine smoke: starter guide, built home, move-in, walking home through a door at night, talk/shop UI, buying | `town.cmds` | ✅ CI |
| Engine smoke: every biome creature spawns, moves and renders; ranged attackers | `creatures.cmds` | ✅ CI |
| Engine smoke: keeper at the Dungeon gate, guardian leaves outside, night summon, warden defeat frees the keeper | `dungeon.cmds` | ✅ CI |
| Engine smoke: effigy burned in underworld lava calls the Wall of Flesh (guide sacrificed), the fight runs, defeat raises hardmode | `mawwall.cmds` | ✅ CI |
| Engine smoke: hardmode appends mutations, loaded chunks catch up, the Hallow wedge streams in converted and is a zone, spread runs; hardmode stations place and every hardmode creature spawns | `hardmode.cmds` | ✅ CI |
| Engine smoke: clockwork eye brings both Twins (flag on the second death), Destroyer releases probes, Skeletron Prime, all-mechs flag | `mechs.cmds` | ✅ CI |
| Screenshots | `--screenshot` + `--exec` (single shot); `screenshot <name>` in `game/Testing/Tours/tour.cmds` (biomes, caves, underworld, town, night, crypt gate, Skeletron, Wall of Flesh, Hallow wedge) | ✅ CI tour job on main/dispatch (Xvfb + lavapipe, artifacts) |

## 7. Performance
* Targets: 60 fps @1080p on GTX-1060-class; ~250 LOD0 chunks; mesh ≤2 ms/chunk; gen ≤10 ms/chunk.
* Measured (container CPU, Release): light+mesh ~6.5 ms median/chunk, worldgen ~17 ms/chunk single thread.
* Props: trees ~1–3k tris of bark + leaf cards, MultiMesh per chunk+model, small decor has 48 m visibility range.
* Grass: up to 14 clumps/m² scattered per chunk (≤16k), drawn within 22–50 m by preset; near clumps 63 triangles,
  far 15, far chunks draw a thinned prefix; no shadows.
* Concerns: dense forests (hundreds of trees in view, alpha-tested overdraw), shadow cost of props, human triangle
  counts, LOD rings not yet implemented.
* 2026-09-27 (player report: 20–30 fps on ultra, RX 6600): sealed chunks out of the sun/moon shadow maps, one
  shadow-casting sky light at a time, sky radiance re-rendered 4×/s instead of every frame (and 128² on high/ultra),
  half-resolution SSAO/SSIL, lighter ultra shadows (4096 atlas, filter 4, 150 m) and fog grid, dynamic resolution driven
  by measured GPU time, GPU/CPU ms on F3. Not yet measured on the player's GPU.

## 8. Next development targets (rest of M4)
M4 graphics delivered the physical sky and clouds, sky-lit PBR shading with graphics presets, synthesised
materials with height blending, refractive water and crusted lava, instanced grass and flowers, leaf-card trees, and
detailed procedural people with a character shader.
M2 delivered events, three bosses, liquids, voxel A*, 3D housing, town residents and biome creature rosters.
M3 delivered the Dungeon (zone, locked walls, guardian, roster, keeper, Skeletron, tailor), the Wall of Flesh (effigy
incineration summon, sweeping barrier, eye lasers, tethered minions) and the hardmode transformation (lazy world
mutations: new ore tiers, radiant and blighted wedges, spreading biomes; evil, cinder and hardmode metal families).
M4 content delivered hardmode life (12 creatures, new debuffs and drops, the forge/anvil chain) and the three
mechanical bosses with radiant gear; the horizon heightfield carries the view to 0.7–2 km by preset.
1. Cave culling from navigation summaries; horizon showing hardmode wedges.
2. Wings/flight accessories; hardmode variants of cavern/underworld creatures.
3. Post-mechanical: Plantera (jungle), chlorophyte, the temple.
4. More residents; residents that fight back; crypt keys and locked chests.
5. Audio director (data-driven cues, music states for bosses/events).

## 9. Directive summary (condensed)
Phases: 1 core loop → 2 world → 3 progression → 4 systemic depth → 5 hardmode → 6 endgame → 7 polish.
Absolute rules: no fake systems, no monoliths, no content tables in code, validate everything, keep
saves compatible, use Blender where custom 3D assets materially help.
