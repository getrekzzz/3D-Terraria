# 3D Terraria-Style World — Master Plan (persistent project memory)

> Update this file at the end of every development cycle. It is the single source of truth for what
> exists, what is partial, what is missing, and what comes next. Last update: 2026-09-25 (cycle M1).

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
| 2026-09-25 | `Strata.Core` has no Godot dependency; `GameWorld` runs the whole simulation headless | Unit/integration tests, deterministic, tools reuse it. |
| 2026-09-25 | Lighting is a pure function of a 3×3×3 chunk neighbourhood + sky map, computed in mesh jobs | No global incremental light state; 4-bit light reaches ≤15 cells so the neighbourhood is exact. |
| 2026-09-25 | Save only modified chunks, regenerate the rest; lazy world mutations with per-chunk level | Small saves; hardmode reaches unexplored chunks for free. |
| 2026-09-25 | Player body = Godot CharacterBody3D on the smooth collision mesh; creatures, drops and projectiles = Core AABB voxel physics | Visually exact player movement; everything else simulates headless and at any distance. |
| 2026-09-25 | Props/items/creatures generated in Blender from `tools/blender/assets.json`; materials are texture-layer names, the game supplies the shader | One consistent PBR look with terrain; assets regenerate deterministically. |
| 2026-09-25 | Engine behaviour is validated with command scripts (`game/Testing/Scripts/*.cmds`) run headless in CI | The same command registry powers the debug console, tests and screenshot automation. |
| 2026-09-25 | Liquids simulate only *active* cells; generated liquid is static until disturbed (edits wake it), saved chunks are rescanned on load | Lazy per-chunk generation cannot settle liquid globally like the reference game; this keeps loading cheap and flow local to play. Pass 20 removes liquid hanging over carved space (generator v2). |
| 2026-09-25 | Town residents are a separate Core entity (not enemy NPCs) with A* paths, homes and trade | Friendly, persistent, schedule-driven; never hit by player attacks. |
| 2026-09-25 | Zones (crypt, spider nests, marble/granite halls, evil hearts) are planned features reported as extra biomes in every condition context; a structure zone is its blueprint's volumes, not its bounding box | Spawn rules, locks, music and loot all read one context; a sprawling crypt must not claim the open surface above it. |
| 2026-09-25 | Progression locks are data: a biome's `lockedUntil` flag protects its generated structure cells; `exclusiveSpawns` zones only spawn rules naming them, at closer range | The crypt behaves like the reference dungeon without special-case code. |
| 2026-09-25 | Progression flags are raised only through `ProgressionSystem`; `autoSet` flags follow from their requirements (defeated_mawwall → hardmode) and may announce themselves | One place for consequences (hardmode mutations hook on `Raised`), no special cases in boss code. |
| 2026-09-25 | The Mawwall is an enemy whose brain sets a `Barrier` volume (drawn as tiled flesh, hurts and ejects players behind it) and tethers minions (`Leader`); summoned by burning an effigy (`incinerationSummon`) in underworld lava | A 3D wall of flesh that cannot be walked around (it tracks the target's Z) built from generic parts. |
| 2026-09-25 | World mutations are an ordered log of (id, seed) in world.json; each step is a pure function of (world, seed, chunk, blocks). Produce applies the missing levels on workers (freshly generated chunks start at level 0), loaded chunks catch up a few per tick as ordinary edits | Hardmode reaches every chunk, visited or not, with identical results whether applied at load or live; saves only store chunks players touched. |
| 2026-09-25 | Open air under a floating roof (island, arch, high canopy) is lit as outdoor shade (sky 12) via a per-column `ShadeTop` in the sky map | The land under sky islands was pitch black at noon (sky light is column-based and spreads only 15 cells); caves and rooms are unaffected. |
| 2026-09-25 | Ore veins are placed by one shared `VeinStamper` for generation and mutations (verified bit-identical to the old pass); hardmode ores are `generate: false` vein rules | No second ore algorithm; alternates are chosen per world from the mutation seed without disturbing generation's random stream. |
| 2026-09-25 | Landmark residents (the crypt keeper) live at a planned landmark instead of a house and leave for good when `leavesWhen` holds; boss summoning can be a resident service | Old-man/clothier arc expressed with the town system and data. |

## 3. System status
Legend: ✅ complete for its milestone · 🟡 partial · ⬜ missing

| Area | System | Status | Notes |
|---|---|---|---|
| Foundation | Solution, projects, CI | ✅ | Core tests (Linux; +Windows on PR/main), content/asset/reference checks, Godot headless smoke job |
| Foundation | Determinism | ✅ | SplitMix/PCG32, name-derived seeds, no trig/pow in worldgen, golden hashes |
| Content | Loader, registries, validator, manifest | ✅ | Strict JSON, typed specs, DAG checks, obtainability, conventional asset paths |
| Content | Reference DBs + coverage | ✅ | 819 concepts / 12 DBs (incl. liquids), `Strata.Tools coverage`; overall 49.8 % (items 51 %, enemies 24 %, bosses 17 %, NPCs 21 %) |
| Voxels | Chunks, edits, raycast, tile objects | ✅ | COW chunks, EditBatch, multi-cell objects with containers |
| Persistence | Regions, codec, id tables, migrations, players | ✅ | Atomic writes + .bak, v0→v1 migration, player saves, autosave |
| WorldGen | 20 passes, plan + chunk tiers | ✅ | Structures: cabins, mineshafts, living trees, pyramid, crypt (zone + locked walls + gate landmark) |
| WorldGen | Report tool | ✅ | Maps, sections, ore/depth charts |
| Lighting | Sky + RGB block light | ✅ | Stateless per chunk; outdoor shade under floating roofs; liquid light (lava) from data; sampled by props/creatures via LightProbe; biome fog/sky/ambient looks from data |
| Meshing | Surface nets + crisp shapes + liquids + collision | ✅ | LOD rings ⬜ |
| Rendering | Terrain, props, creatures, drops, sky/fog | 🟡 | Horizon mesh, LOD, cave culling, particles polish ⬜ |
| Streaming | Jobs, chunk streamer, colliders | ✅ | Adaptive completion budget; diagnostics (`stream`) |
| Player | Controller, camera, avatar, carried light | ✅ | Step-up, variable jump, per-liquid swimming, breath/drowning, fall damage, knockback, blink |
| Interaction | Targeting, mining, placing, objects | ✅ | Brushes, falling blocks, support loss, doors/chests/beds |
| Items | Use table, tooltips, dropped items | ✅ | Inferred actions + explicit behaviours; prefixes/reforging ⬜ |
| Crafting | Recipes, stations, environment | ✅ | Liquids/biome/time conditions |
| UI | HUD, inventory, equipment, chests, crafting, console, perf overlay | ✅ | Settings/menus/world select ⬜ |
| Combat | Damage model, melee arcs, projectiles (friendly + hostile), buffs, contact/death effects | ✅ | Whips/yoyos/summons ⬜ |
| AI | Slime, walker, flyer, swimmer, lunger, caster (blink), boss brains, worm, crypt warden (free hands); ranged attacks; voxel A* | ✅ | A* used by residents; enemy path following, utility AI ⬜ |
| Spawning | Data-driven spawn rules (ground or liquid placement), biome + zone rosters, exclusive zones, per-rule caps, player-position rules, despawn conditions, town suppression | ✅ | Closed doors stop creatures |
| Assets | Blender pipeline, icons, props textures | ✅ | 66 object models, 139 item models, 34 creatures (6 residents), 211 icons |
| NPCs | Town residents, 3D housing, shops, services, happiness, landmark residents | ✅ | 6 residents (guide/merchant/nurse/dryad/old man/clothier roles); more residents, enemies attacking town ⬜ |
| Liquids | Flow, mixing, contact effects, buckets | ✅ | Active-cell automaton per liquid; pressure/U-tubes, sponges, shimmer ⬜ |
| Bosses | Framework + bosses | ✅ | Gel Sovereign, The Watcher, Bore Tyrant (tunnels), Crypt Warden (hands, spin, day rage), The Mawwall (sweeping barrier, eye lasers, tethered hungry maws); arenas, boss bar; music state ⬜ |
| Progression | Flags, auto-set cascade, announcements, incineration summons | ✅ | `ProgressionSystem.Raised` is the hook for hardmode mutations |
| Events | Event director | ✅ | Blood moon, slime rain; invasions ⬜ |
| Dungeon | Crypt zone, locked walls, guardian, roster, keeper + curse | ✅ | Crypt keys/locked chests, water bolt loot, spike traps ⬜ |
| Hardmode | Trigger, lazy world mutations, spread, ore tiers | ✅ | Mawwall → `hardmode` → 3 ore tiers (2 alternates each), radiant + blighted wedges (zones), spreading biomes; evil/cinder/hardmode metal families (pick chain 65 → 100 → 110 → 150 → 190); hardmode creatures and mechanical bosses ⬜ |
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
* Crypt Warden hands share the skull's life (the reference boss has separately killable hands).
* The Mawwall only sweeps along X and only its mouth and eyes take hits; its flesh tiles are separate pooled visuals
  (a MultiMesh would be cheaper for very tall cavities).
* Zone membership scans a structure's blueprint ops per query (hundreds for the crypt); fine at spawn/lock rates,
  would want a coarse occupancy grid if queried per frame.
* Outdoor shade considers only the first roof in a column (stacked islands shade as one).
* Spreading only visits loaded chunks, and spread land outside a wedge does not count as that zone (zones are the
  wedges); trees, grass tufts and flowers on converted land keep their forest look.
* Spread converts a few cells every half second, so converted borders keep remeshing (intended in play; tours and
  screenshots pause it with `mutations spread off`).
* All bars smelt at the furnace and all metal gear uses the anvil (no hellforge / hardmode anvil and forge stations yet).
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
| Core unit/integration tests (190) | `tests/Strata.Core.Tests` | ✅ CI |
| Content validation (refs, specs, assets) | `Strata.Tools validate` | ✅ CI |
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
| Engine smoke: keeper at the crypt gate, guardian leaves outside, night summon, warden defeat frees the keeper | `dungeon.cmds` | ✅ CI |
| Engine smoke: effigy burned in underworld lava calls the Mawwall (wayfinder sacrificed), the fight runs, defeat raises hardmode | `mawwall.cmds` | ✅ CI |
| Engine smoke: hardmode appends mutations, loaded chunks catch up, the radiant wedge streams in converted and is a zone, spread runs | `hardmode.cmds` | ✅ CI |
| Screenshots | `--screenshot` + `--exec` (single shot); `screenshot <name>` in `game/Testing/Tours/tour.cmds` (biomes, caves, underworld, town, night, crypt gate, Crypt Warden, Mawwall, radiant wedge) | ✅ CI tour job on main/dispatch (Xvfb + lavapipe, artifacts) |

## 7. Performance
* Targets: 60 fps @1080p on GTX-1060-class; ~250 LOD0 chunks; mesh ≤2 ms/chunk; gen ≤10 ms/chunk.
* Measured (container CPU, Release): light+mesh ~6.5 ms median/chunk, worldgen ~17 ms/chunk single thread.
* Props: trees ~1000 tris, MultiMesh per chunk+model, small decor has 48 m visibility range.
* Concerns: dense forests (hundreds of trees in view), shadow cost of props, LOD not yet implemented.

## 8. Next development targets (M4)
M2 delivered events, three bosses, liquids, voxel A*, 3D housing, town residents and biome creature rosters.
M3 delivered the crypt (zone, locked walls, guardian, roster, keeper, Crypt Warden, tailor), the Mawwall (effigy
incineration summon, sweeping barrier, eye lasers, tethered minions) and the hardmode transformation (lazy world
mutations: new ore tiers, radiant and blighted wedges, spreading biomes; evil, cinder and hardmode metal families).
1. Hardmode life: radiant and blighted creature rosters, hardmode variants of cavern/underworld creatures, a
   hardmode anvil and forge, wings/flight accessories.
2. LOD rings + horizon heightfield; cave culling from navigation summaries.
3. Mechanical bosses (Twin Lenses, Iron Serpent, Warden Prime) on the progression flags that already exist.
4. More residents; residents that fight back; crypt keys and locked chests.
5. Audio director (data-driven cues, music states for bosses/events).

## 9. Directive summary (condensed)
Phases: 1 core loop → 2 world → 3 progression → 4 systemic depth → 5 hardmode → 6 endgame → 7 polish.
Absolute rules: no fake systems, no monoliths, no content tables in code, validate everything, keep
saves compatible, use Blender where custom 3D assets materially help.
