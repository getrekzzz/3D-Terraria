# 3D Terraria-Style World — Master Plan (persistent project memory)

> Update this file at the end of every development cycle. It is the single source of truth for what
> exists, what is partial, what is missing, and what comes next. Last update: 2026-10-02 (Phase 1 of the
> player-feedback overhaul, W1a and W1b: the reported bugs are fixed; status rows refreshed from the audit in
> [`AUDIT.md`](AUDIT.md)).

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
| 2026-09-27 | The world map is per player per world (`maps/<player>.map`): 1 m pixels in sparse 64² tiles remembering the surface seen outdoors and the cave floor or rock seen underground at the player's depth; M opens it, a minimap follows the player | A top-down adaptation of the reference game's discoverable map that still shows caves; block ids are saved by name. |
| 2026-09-27 | Cabins stand in the cave tunnels with doorways along the tunnel; one or two rooms; Gold Chests in the cavern layer (also loose, with pots) | Buried cabins were only found by digging; the reference game's caves are dotted with cabins and gold chests. Generator version 9. |
| 2026-09-30 | Human clips are pose functions sampled every frame; characters with the same skeleton share an animation set (`assets/models/creatures/anim`), played through a code-built `AnimationTree` (locomotion + upper-body actions + held light + flinch) and an aim modifier | Layered, Terraria-like motion (swing while running, legs follow movement, torso to the aim) without re-exporting 98 character meshes; sets rebuild in seconds. |
| 2026-09-30 | Player-feedback overhaul: fix the reported bugs first (targeting, swings, hooks, bows, exposure, torches, terrain), then lag spikes, a faithful Terraria UI, item and feel polish, bosses and a 1:1 Dungeon, complete town NPCs and a guide book, in waves W0–W11. The per-feature status with evidence and planned fixes is [`docs/AUDIT.md`](AUDIT.md), refreshed at every phase gate | The player reported jagged terrain, fast-forwarded swings, broken hooks, a flashbang desert, tiny torch light, wrong facing and lag spikes, and asked for a full audit. Old worlds are disposable; the body always faces the crosshair; the UI follows the Terraria look while the world stays realistic 3D |
| 2026-09-30 | Bosses are rebuilt from metaball bodies with sculpted, painted detail and small rigs, translucent `:clear` parts, and renderer-drawn arms (`armModel`); centred models are raised onto their feet by their bounds | The primitive-built bosses did not read as the reference game's; eyes and brains were drawn half a hitbox low. |
| 2026-10-01 | Exposure is a data-driven, deterministic model (`data/config/lighting.json`, Core `ExposureModel`): a cave and night boost and a per-biome `exposureBias`, eased asymmetrically (fast darken, slow brighten) on every preset; Godot's auto exposure only meters within a narrow window around it. Glow, fog, SSIL, volumetrics, torch lights and the block-light curve are data in the same file | The old auto-exposure window could only brighten (1.65–3.3×), so the desert and snow flashbanged and leaving a cave was a white frame; tuning needed numbers, not code edits |
| 2026-10-01 | The body always faces the crosshair in third person: chest, arms and head follow the camera's yaw, the legs follow the movement (forward, strafe and backpedal clips, at most 70° of twist); the crosshair ray starts at the eye plane, so nothing behind the player is targeted | Aiming and talking turned the body the wrong way through the shoulder-camera offset; the player asked for the body to face where they look |
| 2026-10-01 | `GenVersion` 10: smooth terrain. Relief is data (`relief` per biome) sampled with cubic B-splines and soft ridges; the surface is a heightfield with overhangs only in rare cliff zones; caves keep a roof under the true surface; ground vertices take their height from nearby column tops (no stair ripples) | The player found the terrain jagged and unpredictable (fins, floating blobs, pits, 4 m creases); old worlds are disposable and are marked "old terrain" in the world list |
| 2026-10-01 | Lean mode for the overhaul: no software-rendered (lavapipe) tuning loops, at most one short rendered check per work package, smoke only what a change touches (the wave gate runs everything), no optional extras | The player's call: W1a took about 8.5 hours, 6+ of them rendered lighting tuning |
| 2026-10-01 | The overhaul runs as parallel cloud sessions, each owning a disjoint slice and branch (integration, Bosses + Dungeon, Art, NPCs + Guide book, World & life), merged into `claude/clever-goodall-kgex45` with full CI; rules in [`docs/overhaul/PARALLEL.md`](overhaul/PARALLEL.md) | Cuts wall-clock time; slices avoid each other's hub files so merges stay mechanical |

## 3. System status
Legend: ✅ complete for its milestone · 🟡 partial · ⬜ missing

This table is the summary. Feature-level status, known defects with `file:line` evidence and the planned fix for each
are in [`AUDIT.md`](AUDIT.md).

| Area | System | Status | Notes |
|---|---|---|---|
| Foundation | Solution, projects, CI | ✅ | Core tests (Linux; +Windows on PR/main), content/asset/reference checks, Godot headless smoke job |
| Foundation | Determinism | ✅ | SplitMix/PCG32, name-derived seeds, no trig/pow in worldgen, golden hashes |
| Content | Loader, registries, validator, manifest | ✅ | Strict JSON, typed specs, DAG checks, obtainability, conventional asset paths |
| Content | Reference DBs + coverage | ✅ | 1,025 concepts / 12 DBs (incl. liquids), `Strata.Tools coverage`; 100 % covered, but the list is hand-picked (no Magic Missile, Golden Key, spikes, pylons…, see AUDIT §3) |
| Voxels | Chunks, edits, raycast, tile objects | ✅ | COW chunks, EditBatch, multi-cell objects with containers |
| Persistence | Regions, codec, id tables, migrations, players | ✅ | Atomic writes + .bak, v0→v1 migration, player saves, autosave |
| WorldGen | 20 passes, plan + chunk tiers | ✅ | `GenVersion` 10: smooth relief from data, heightfield surface with rare cliff zones, roofed caves, worm cave mouths. Structures: cabins, mineshafts, living trees, pyramid, crypt (zone + locked walls + gate landmark) |
| WorldGen | Report tool | ✅ | Maps, sections, ore/depth charts |
| Lighting | Sky + RGB block light, exposure | ✅ | Stateless per chunk; outdoor shade under floating roofs; liquid light (lava) from data; sampled by props/creatures via LightProbe; biome fog/sky/ambient looks from data; data-driven exposure model with per-biome bias (no desert flashbang); torches light 8–10 m of cave on every preset (`data/config/lighting.json`) |
| Meshing | Surface nets + crisp shapes + liquids + collision + grass scatter | ✅ | Ground heights rebuilt from column tops (no stair ripples); far terrain is the horizon heightfield (no coarse voxel chunks) |
| Rendering | Physical sky + clouds, sky-lit PBR, graphics presets, terrain materials, water/lava, grass, leaf-card trees, character shader, horizon | ✅ | See `docs/architecture/rendering.md`. Cave culling, particles polish ⬜ |
| Streaming | Jobs, chunk streamer, colliders | ✅ | Adaptive completion budget; diagnostics (`stream`) |
| Player | Controller, camera, avatar, carried light | ✅ | Step-up, variable jump, per-liquid swimming, breath/drowning, fall damage, knockback, blink; the body always faces the crosshair, legs strafe and backpedal |
| Interaction | Targeting, mining, placing, objects | ✅ | Brushes, falling blocks, support loss, doors/chests/beds; eye-plane crosshair ray with decor and canopy filters, the Reach stat, crosshair names for creatures, bosses, worm segments and residents |
| Items | Use table, tooltips, dropped items | ✅ | Inferred actions + explicit behaviours; one use animation per swing (speed scales it), hooks thrown by click or key that hold on natural terrain, shots from the hand to the crosshair, bows drawn with a nocked arrow, swords that shoot also swing, failed uses say why; 18 prefixes (invented names) and Goblin Tinkerer reforging (no reforge slot). Yoyos, flails, whips and per-hook visuals are stand-ins (AUDIT §8) |
| Crafting | Recipes, stations, environment | ✅ | Liquids/biome/time conditions |
| UI | HUD, inventory, equipment, chests, crafting, console, perf overlay, menus, settings, character/world select, map | ✅ | Generic dark theme with no UI font; no hearts/stars, buff icons, coin/ammo slots, chat log or housing menu (AUDIT §9) |
| Combat | Damage model, melee arcs, projectiles (friendly + hostile), buffs, contact/death effects | ✅ | Summons ✅; whips, yoyos and flails are stand-ins 🟡 (AUDIT §8) |
| AI | Slime, walker, flyer, swimmer, lunger, caster (blink), boss brains, worm, crypt warden (free hands); ranged attacks; voxel A* | ✅ | A* used by residents; enemy path following, utility AI ⬜ |
| Spawning | Data-driven spawn rules (ground or liquid placement), biome + zone rosters, exclusive zones, per-rule caps, player-position rules, despawn conditions, town suppression | ✅ | Closed doors stop creatures |
| Assets | Blender pipeline, icons, synthesised textures | ✅ | 103 object models, 660 item models, 777 icons, 216 creature models; 97 procedural humans with faces, hands, hair, clothing, 52-bone rig. Weapon classes share one mesh, icons unoutlined, some creatures use the wrong archetype (AUDIT §11) |
| NPCs | Town residents, 3D housing, shops, services, happiness, landmark residents | ✅ | All 29 of the reference game's town NPCs; rescues, housing menu, residents fighting back, day-only move-in and 8 empty shops ⬜ (AUDIT §14) |
| Liquids | Flow, mixing, contact effects, buckets | ✅ | Active-cell automaton per liquid; pressure/U-tubes, sponges, shimmer ⬜ |
| Bosses | Framework + bosses | ✅ | 29 bosses (5 hand-written boss brains, the rest on the data-driven `pattern` brain): King Slime, The Eye of Cthulhu, Eater of Worlds (tunnels), Brain of Cthulhu, Queen Bee, Skeletron (hands, spin, day rage), Deerclops, The Wall of Flesh (sweeping barrier, eye lasers, tethered hungry maws), Queen Slime, Twins (paired eyes, phase-2 lasers and flamethrower), Destroyer (50-segment flying worm, part lasers, probes), Skeletron Prime (four tools, gun arms fire), Plantera, Golem, Duke Fishron, Empress of Light, Lunatic Cultist, pillars, Moon Lord and event bosses; arenas, boss bar, boss music. Parts share one life pool except Moon Lord's; no Expert behaviour or treasure bags (AUDIT §12) |
| Progression | Flags, auto-set cascade, announcements, incineration summons | ✅ | `ProgressionSystem.Raised` is the hook for hardmode mutations |
| Events | Event director | ✅ | Blood Moon, Slime Rain, 7 invasions (Goblin Army, Pirates, Martian Madness, Old One's Army, Frost Legion, Pumpkin Moon, Frost Moon), Solar Eclipse, lunar events, rain, wind, sandstorm, party, lantern night; no progress bar ⬜ |
| Dungeon | Crypt zone, locked walls, guardian, roster, keeper + curse | ✅ | Boxy room grid; chests roll cave-cabin loot; locked chests and Golden Key, dungeon furniture, books, spike traps ⬜ (AUDIT §13) |
| Hardmode | Trigger, lazy world mutations, spread, ore tiers, rosters, stations, mechanical bosses | ✅ | Wall of Flesh → `hardmode` → 3 ore tiers (2 alternates each), radiant + blighted wedges (zones), spreading biomes; evil/cinder/hardmode metal families (pick chain 65 → 100 → 110 → 150 → 190 → 200); 12 hardmode creatures; hellforge, hardmode anvils and forges; Twins, Destroyer, Skeletron Prime → radiant gear; 14 wings (13 hardmode); Plantera, chlorophyte, the Jungle Temple and Golem; hardmode variants of old creatures ⬜ |
| Audio | Director + sounds | ✅ | Music director (boss > event > Blood Moon > biome > layer, 31 tracks) and 146 SFX files: 177 sound files. Generic creature hit sounds, silent object hits and an unused ambient bus 🟡 (AUDIT §10) |

## 4. Technical debt
* Creatures use AABB voxel collision while the terrain renders smooth: feet can float up to ~0.25 m on slopes (visual offset fix: ray down to the collider in the renderer).
* `TileObjectRenderer` rebuilds a chunk's MultiMeshes when its light changes; fine now, could update custom data in place.
* Screenshot runs on software Vulkan (llvmpipe) are slow (frame-rate-bound job completion); budget is adaptive now.
* Streaming once failed to reach idle in a rendered run (not reproduced since); `ChunkStreamer.DebugState()` is logged by the screenshot tool to catch it.
* Item held-in-hand orientation is generic (+90° about X) except ranged weapons, which aim at the crosshair (bows
  upright, guns along the view, staffs tipped); per-item grip sockets would improve swords and tools (P3-05, P3-06).
* Reforging is a Goblin Tinkerer service on the held item: no reforge slot, and the 18 prefixes use invented names
  (P2-28, P3-31).
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
| Core unit/integration tests (574 at the W3 gate, incl. graphics presets, grass scatter, horizon, hardmode life, mechanical bosses, nights, cabins, legacy saves, use timing, targeting, hooks, terrain shape, slots and coins) | `tests/Strata.Core.Tests` | ✅ CI |
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
| Engine smoke: grappling hooks on crisp, natural and platform anchors, rocket boots, wings, extra jumps, dashing | `movement.cmds` | ✅ CI |
| Engine smoke: climbing rope, minecart track, wiring (lever, door, pressure plate, dart trap) | `builder.cmds` | ✅ CI |
| Engine smoke: Terraria slot clicks, throwing, crates, chest operations; theme, font, UI scale, combat text, health bars; spike counters | `inventory.cmds`, `ui.cmds`, `perf.cmds` | ✅ CI |
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
> Superseded on 2026-09-30 by the player-feedback overhaul (waves W0–W11, tracked in [`AUDIT.md`](AUDIT.md)). Of the
> list below, wings, Plantera, chlorophyte, the temple, more residents and the audio director are done. Residents that
> fight back and Dungeon keys/locked chests are planned (P5-10, P4-21). Cave culling, hardmode wedges on the horizon
> and hardmode variants of cavern/underworld creatures are not planned yet (AUDIT §17).

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
