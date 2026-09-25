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

## 3. System status
Legend: ✅ complete for its milestone · 🟡 partial · ⬜ missing

| Area | System | Status | Notes |
|---|---|---|---|
| Foundation | Solution, projects, CI | ✅ | Core tests (Linux; +Windows on PR/main), content/asset/reference checks, Godot headless smoke job |
| Foundation | Determinism | ✅ | SplitMix/PCG32, name-derived seeds, no trig/pow in worldgen, golden hashes |
| Content | Loader, registries, validator, manifest | ✅ | Strict JSON, typed specs, DAG checks, obtainability, conventional asset paths |
| Content | Reference DBs + coverage | ✅ | 775 concepts / 11 DBs, `Strata.Tools coverage`; overall 34.7 % |
| Voxels | Chunks, edits, raycast, tile objects | ✅ | COW chunks, EditBatch, multi-cell objects with containers |
| Persistence | Regions, codec, id tables, migrations, players | ✅ | Atomic writes + .bak, v0→v1 migration, player saves, autosave |
| WorldGen | 20 passes, plan + chunk tiers | ✅ | Structures: cabins, mineshafts, living trees, pyramid, dungeon v1 |
| WorldGen | Report tool | ✅ | Maps, sections, ore/depth charts |
| Lighting | Sky + RGB block light | ✅ | Stateless per chunk; sampled by props/creatures via LightProbe |
| Meshing | Surface nets + crisp shapes + liquids + collision | ✅ | LOD rings ⬜ |
| Rendering | Terrain, props, creatures, drops, sky/fog | 🟡 | Horizon mesh, LOD, cave culling, particles polish ⬜ |
| Streaming | Jobs, chunk streamer, colliders | ✅ | Adaptive completion budget; diagnostics (`stream`) |
| Player | Controller, camera, avatar | ✅ | Step-up, variable jump, swim, fall damage, knockback, blink |
| Interaction | Targeting, mining, placing, objects | ✅ | Brushes, falling blocks, support loss, doors/chests/beds |
| Items | Use table, tooltips, dropped items | ✅ | Inferred actions + explicit behaviours; prefixes/reforging ⬜ |
| Crafting | Recipes, stations, environment | ✅ | Liquids/biome/time conditions |
| UI | HUD, inventory, equipment, chests, crafting, console, perf overlay | ✅ | Settings/menus/world select ⬜ |
| Combat | Damage model, melee arcs, projectiles, buffs | ✅ | Whips/yoyos/summons ⬜ |
| AI | Slime, walker, flyer brains | ✅ | Voxel A*, utility AI, behaviour trees ⬜ (M2) |
| Spawning | Data-driven spawn rules | ✅ | Housing suppression ⬜ |
| Assets | Blender pipeline, icons, props textures | ✅ | 66 object models, 136 item models, 5 creatures, 190 icons |
| NPCs | Town NPCs, housing | ⬜ | M2 |
| Liquids | Flow simulation | ⬜ | Static liquids only (M2) |
| Bosses | Framework + bosses | ⬜ | M2 |
| Events | Blood moon, invasions | ⬜ | M2 |
| Hardmode | Mutations, spread | ⬜ | M3 |
| Audio | Director + sounds | ⬜ | |

## 4. Technical debt
* Creatures use AABB voxel collision while the terrain renders smooth: feet can float up to ~0.25 m on slopes (visual offset fix: ray down to the collider in the renderer).
* `TileObjectRenderer` rebuilds a chunk's MultiMeshes when its light changes; fine now, could update custom data in place.
* Screenshot runs on software Vulkan (llvmpipe) are slow (frame-rate-bound job completion); budget is adaptive now.
* Streaming once failed to reach idle in a rendered run (not reproduced since); `ChunkStreamer.DebugState()` is logged by the screenshot tool to catch it.
* Item held-in-hand orientation is generic (+90° about X); per-item grip data would improve swords/bows.
* Prefix (reforge) data exists but no reforging service/UI yet.

## 5. Dependency map
```
Content ─┬─> Voxels ─> WorldGen ─> Lighting ─> Meshing ─> Godot streaming/render
         ├─> Items/ItemUse ─> Crafting ─> UI
         ├─> Combat ─> Creatures/Brains ─> Spawning ─> Events ─> Bosses
         └─> Persistence (world, regions, players, migrations)
GameWorld (Core) = streaming + clock + editor + drops + players + NPCs + melee + projectiles + spawner
Commands (Core) ─> console (Godot), smoke scripts (CI), screenshots (--exec)
Progression flags ─> Mutations (hardmode) ─> WorldGen passes (lazy)
```

## 6. Validation status
| Check | Where | Status |
|---|---|---|
| Core unit/integration tests (115) | `tests/Strata.Core.Tests` | ✅ CI |
| Content validation (refs, specs, assets) | `Strata.Tools validate` | ✅ CI |
| Reference DB freshness + coverage report | `gen_reference.py --check`, `coverage` | ✅ CI summary |
| Asset coverage (every object/item/enemy) | `build_all.py --check`, `AssetCoverageTests` | ✅ CI |
| Module size guard (≤400 lines) | `tools/ci/check_file_length.py` | ✅ CI |
| Engine smoke: boot, stream, land, walk, mine, collect, build, objects, time, save | `basic.cmds` | ✅ CI |
| Engine smoke: contact damage, melee, arrows, death/respawn | `combat.cmds` | ✅ CI |
| Screenshots | local (Xvfb + lavapipe) via `--screenshot` + `--exec` | 🟡 CI screenshot tour ⬜ |

## 7. Performance
* Targets: 60 fps @1080p on GTX-1060-class; ~250 LOD0 chunks; mesh ≤2 ms/chunk; gen ≤10 ms/chunk.
* Measured (container CPU, Release): light+mesh ~6.5 ms median/chunk, worldgen ~17 ms/chunk single thread.
* Props: trees ~1000 tris, MultiMesh per chunk+model, small decor has 48 m visibility range.
* Concerns: dense forests (hundreds of trees in view), shadow cost of props, LOD not yet implemented.

## 8. Next development targets (M2)
1. Liquids: cellular automaton (water/lava/honey), buoyancy, swimming polish, liquid meshing updates.
2. Boss framework (phases, telegraphs, arenas, music state) + Gel Sovereign, The Watcher, Bore Tyrant (real tunnelling worm).
3. Events: blood moon, slime rain; `event` and `boss` commands; event conditions in spawn rules.
4. Town NPCs: 3D housing validator (enclosed room, light, table, chair, door), voxel A*, first NPCs with shops.
5. LOD rings + horizon heightfield; cave culling from navigation summaries.
6. Screenshot tour in CI (surface, cave, cavern, biomes, underworld, ocean).

## 9. Directive summary (condensed)
Phases: 1 core loop → 2 world → 3 progression → 4 systemic depth → 5 hardmode → 6 endgame → 7 polish.
Absolute rules: no fake systems, no monoliths, no content tables in code, validate everything, keep
saves compatible, use Blender where custom 3D assets materially help.
