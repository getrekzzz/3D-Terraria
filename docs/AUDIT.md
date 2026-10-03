# Strata audit: what works, what is broken, what is missing

> An audit of `main` at M4 (commit `15bf3f4`), written in W0 of the player-feedback overhaul (2026-09-30). It answers three questions: what works, what doesn't, and what's missing. Each row names the
> planned fix. It is refreshed at every phase gate. When a work package (WP) lands, update its rows' status and
> evidence. The summary table in [`3D_TERRARIA_MASTER_PLAN.md`](3D_TERRARIA_MASTER_PLAN.md) §3 points here.
>
> Last refresh: the final integration (2026-10-03), after the World, Art and Bosses/NPC slices merged with the
> integration branch. Every row the overhaul touched carries its final status; rows still 🟡, ❌ or ⬜ are known gaps
> (also listed in [`overhaul/FINAL.md`](overhaul/FINAL.md)) or out of scope (§17).

## 1. How to read

| Status | Meaning |
|---|---|
| ✅ | Works: does its job, close enough to the reference game, no known defect |
| 🟡 | Partial or stand-in: exists, but is simplified, placeholder or wrong in details |
| ❌ | Broken: exists, but does not do what a player expects |
| ⬜ | Missing: not in the game |

* **Evidence** is `path:line`, relative to the repo root. Lines refer to the W0 tree: `15bf3f4` plus the P0-01 file
  splits and P1b-01.
  - P0-01 moved code into partials: `ItemUseSystem.Shooting.cs`, `ItemUseSystem.Consume.cs`, `NpcSystem.Death.cs`,
    `BossInfo.cs`, `PlayerEntity.cs`, `GameWorld.Saving.cs`, `PlayerController.Fall.cs` and
    `InteractionController.Talk.cs`.
  - If a line has drifted, search for the symbol the row names.
* **Fix** names the planned WP and its wave, e.g. `P1-03 (W1a)`. `—` means nothing is planned (see §17).
  - A prime (`P3-17'`) marks a WP whose scope the integration review reduced.
  - `a`/`b` mark a WP that was split in two.
* **Counts** come from `data/**` (excluding the reference DBs in `data/reference/`) and `assets/**`.
* **Sources**:
  - Four read-only code audits: player and items; terrain, lighting and performance; NPCs, bosses and the Dungeon; UI,
    item presentation, game feel and art.
  - A completeness critic (21 extra findings).
  - Spot checks of the claims against the code. Where an audit and the code disagreed, the code wins.

| Wave | Theme |
|---|---|
| W0 | Prep: hub-file splits, extension points, this audit, terrain metrics, spike instrumentation |
| W1a, W1b | The reported bugs: targeting, swings, hooks, bows, exposure, torches, facing, names, terrain |
| W2 | Lag spikes; metal colours, icons and weapon silhouettes |
| W3, W4 | Terraria UI foundations, then the HUD, inventory, chests, crafting, NPC chat and shop |
| W5, W6 | Weapons and juice, then feel and gameplay loops |
| W7 | Boss framework, early bosses and Skeletron; worn armour, creature archetypes |
| W8 | The 1:1 Dungeon |
| W9 | Hardmode bosses; event-boss models |
| W10 | Town NPCs |
| W11 | Guide book, bestiary, achievements and the final gate |

## 2. Reported problems

| # | Complaint | Root cause (evidence) | Fix | Acceptance check |
|---|---|---|---|---|
| 1 | The desert flashbangs on every preset except low | Auto-exposure is on from medium up. Its ISO window (85–170) pins the tonemap multiplier to 1.65–3.3×, so it can only brighten (`game/Ambience/EnvironmentController.cs:94-97`, `data/config/graphics.json:22`). Glow uses an HDR threshold of 1.0, Screen blend and level weights summing to 4.5 (`EnvironmentController.cs:63,90`). Fog starts about 149 m out on medium and up (`game/Ambience/EnvironmentController.Frame.cs:135-136`, `game/World/WorldRuntime.cs:140`). The horizon is fully lit. | ✅ P1-02, P1-03 (W1a) | `shot_stats.py --check` passes: medium desert 0.39 mean luma, 0 % clipped (was 0.89, 3.9 %); cave exit +0.5 s 0.54, 0 % (was 99.8 % clipped) |
| 2 | Torches light only a tiny radius | 4-bit baked light loses 1 level per 0.5 m cell (`src/Strata.Core/Lighting/LightEngine.cs:131`). The fill is octahedral, so diagonals reach about 4 m. The `x*sqrt(x)` curve (`shaders/include/voxel_light.gdshaderinc:26-37`) adds to the falloff. Placed-light energy is 0.79 with attenuation 1.1 (`game/Rendering/DynamicLights.cs:65,127`). The pool is 0 lights on low (`data/config/graphics.json:12`). | ✅ P1-04 (W1a); P1b-17 (W2, optional) | Lighting tour: the 10 m end wall of a torch corridor gets 29 % of the 1 m wall's light (was 0.3 %); low has 4 real lights |
| 3 | Terrain is jagged and unpredictable | A 3D overhang density `1.5 + rug*11` over 3 octaves (`src/Strata.Core/WorldGen/Fields/TerrainField.cs:33-42`). Ridged 5-octave mountains on a bilinear 8-cell grid (`src/Strata.Core/WorldGen/Passes/BiomeRelief.cs:20,57-58`, `src/Strata.Core/WorldGen/Plan/WorldPlan.cs:14`). Biome ramps at `Membership*3` (`BiomeRelief.cs:40`). Caves are carved from the 2D height. A binary solid/air threshold gives stair ripples. | ✅ P1b-02, P1b-03, P1b-04 (W1b) | `worldgen-report --compare` against the W0 baseline (§5): roughness 0.53 → 0.31, lip 0.71 → 0.13 %, floating 205 → 91, breach 3.88 → 0.35 % |
| 4 | Tool swings look fast-forwarded | The avatar replays the clip whenever `UseAnimation` jumps back up (`game/Player/PlayerAvatar.Motion.cs:44-47`). A new use starts every `useTime` ticks (`src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs:161-162`), so tools restart at 71–79 % of the clip. Speed bonuses shorten only the cooldown (`ItemUseSystem.cs:97,110,158`). The strike is about 90 ms of the clip (`tools/blender/bgen/human/actions.py`). The first-person phase snaps (`game/Player/ViewModel.cs:51`). | ✅ P1-10, P1-12 (W1a); P1-11 (W1b) | UseTimingTests: one clip per swing, no restart; mining speed leaves `useAnimation` unchanged; `combat.cmds` sees no action dips in chained swings |
| 5 | Grappling hooks don't work | `ItemAction.Grapple` is parsed (`src/Strata.Core/Gameplay/Items/ItemUseTable.cs:159`), but `ItemUseSystem.Use` has no branch for it, so clicking does nothing. On F, the anchor test nudges 5 cm into a voxel, while the smooth collider sits up to 0.25 m off, so the hook lets go at once on natural terrain (`game/Player/PlayerController.Grapple.cs:106-111`). | ✅ P1-08 (W1a), P1-09 (W1b); per-hook heads and chains P3-17' (W5) | `movement.cmds` latches on a natural slope and stays latched while reeling in |
| 6 | The bow has no animation | The arrow spawns on tick 0 from the eye (`src/Strata.Core/Gameplay/Entities/ProjectileSystem.cs:89`), but the clip releases at t 0.6. First person is an 8 cm lift (`game/Player/ViewModel.cs:69-72`). No ammo, no mana and Blocked are silent (`ItemUseSystem.cs:160`). | ✅ P1-13, P1-15 (W1a); P1-14 (W1b) | First-person draw-pose probe (`fp.arrow`, `fp.aim_error_deg`); a no-ammo shot sets `msg.last` and `sfx.last` |
| 7 | Swords that shoot never melee | `Shot` fails the melee gate (`ItemUseSystem.cs:154`). Zenith's returning blades make the one-at-a-time rule return `Blocked` (`src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Shooting.cs:27`). | ✅ P1-16 (W1a) | Core test: Starfury swings and shoots; Zenith never blocks |
| 8 | The body faces the wrong way when aiming or interacting | The aim ray starts at the camera, 4.2 m behind and 0.55 m right of the player (`game/Player/CameraRig.cs:17,24`). It accepts hits behind the player, such as grass, bushes and tree boxes the camera probe ignores (`game/Player/InteractionController.cs:101-110`). The body then turns past 55° (`game/Player/PlayerAvatar.Motion.cs:84`). | ✅ P1-05 (W1a), P1-06 (W1b) | `player.facing_error_deg < 3` after `camera orbit 90`; a target behind the player is never selected |
| 9 | The crosshair doesn't name mobs or bosses | The HUD names only blocks and objects within reach (`game/UI/Hud.cs:211`). There is no ray-vs-entity picking. | ✅ P1-07 (W1b) | `hud.target_name` for a slime, a worm segment and a resident |
| 10 | Lag spikes | Synchronous autosave every 120 s (`src/Strata.Core/Session/GameWorld.cs:25,157-160`) rewrites whole region files (`src/Strata.Core/Persistence/RegionFile.cs:42-69`). Other causes: unbudgeted near-player collider builds, tile objects rebuilt twice per remesh, grass churn, first-use GLB loads, per-projectile materials and lights, per-frame LINQ, large-object-heap churn and dynamic-resolution oscillation (§6). | ✅ P1b-05 (W0); P1b-06…P1b-14, P1b-16 (W2); P1b-15 (W5) | `perf.cmds` (headless, small world): frames ≥ 33 ms 95 → 25, p99 30.5 → 16.0 ms, max 492.6 → 64.2 ms, gen-2 GCs 30 → 7 (Art slice notes) |
| 11 | No guide book | Nothing exists: no bestiary, journal or achievements (§16). | ✅ P5-24a (W3); P5-24b, P5-25, P5-26, P5-27, P5-30…P5-32 (W11) | `guide.cmds`: the Guide book (K) opens on every tab, the bestiary unlocks after a kill, achievements pop |
| 12 | Bosses and the Dungeon should match the reference game, Skeletron 1:1 | Boss parts share one life pool (`src/Strata.Core/Gameplay/Entities/Npc.cs:132-140`). The Dungeon is a grid of boxy rooms that rolls cave-cabin loot (`src/Strata.Core/WorldGen/Structures/DungeonBuilder.cs:116`). See §12 and §13. | ✅ P4-01…P4-38 (W7–W9), X-11 | SkeletronTests, DungeonGenerationTests, LockedChestTests, `dungeon.cmds` route, `bosses.cmds`, `mechs.cmds` |
| 13 | The NPC system should be complete | Move-in rules are approximate, there are no rescues, no housing UI, residents can't fight back, and many shops are empty (§14). | ✅ P5-01…P5-23, P5-28, P5-29, P5-34 (W10, W11), X-05, X-10 | `town.cmds` and `rescue.cmds`: day-only move-in, rescue, housing query/assign/evict, residents defending, Nurse heal |
| 14 | UI, items and feel should be much more like Terraria (the top priority) | A generic dark-slate Godot UI with no font, progress-bar vitals and text buffs. Items share generic poses and meshes. Most "juice" is missing (§8–§11). | ✅ P2-* (W2–W4), P3-* (W2, W4–W7), X-* | UI tour at 1280×720 and 1920×1080, UI scale 1.0 and 1.5, `layout.overlaps == 0`; `ui.cmds`, `juice.cmds`, `combat.cmds` |

## 3. Content at a glance

W0 counts (the audit's baseline). At the final integration `validate` loads 1,522 items (with derived block and object
items), 250 enemies, 591 tile objects, 239 projectiles, 57 buffs, 83 prefixes, 21 events, 32 town NPCs (with pets and
visitors), 49 achievements and 659 recipes; `assets/` holds 881 item and 243 creature models, 287 SFX, 55 music and
21 ambience files (363 under `assets/audio/`).

| Content | Count | Notes |
|---|---|---|
| Items | 667 (plus derived block and object items) | 660 item GLBs (`assets/models/items/`), 777 icons (`assets/icons/items/`) |
| Enemies | 229 | 216 creature GLBs (`assets/models/creatures/`), including boss parts and humans, plus 29 shared animation sets in `assets/models/creatures/anim/` |
| Bosses | 29 reference bosses | 13 brain types (`src/Strata.Core/Content/Specs/BrainSpecs.cs:6-18`). Five are hand-written boss brains; the other bosses run the data-driven `pattern` brain |
| Town residents | 29 | `data/town.json` (6) and `data/town_residents.json` (23) |
| Events | 16 | Seven invasions (Goblin Army, Pirates, Martian Madness, Old One's Army, Frost Legion, Pumpkin Moon, Frost Moon), Blood Moon, Slime Rain, Solar Eclipse, lunar events and five ambient events (`data/events.json`) |
| Wings | 14 | 13 hardmode wings plus Fledgling Wings |
| Buffs, prefixes, tile objects | 39, 18, 71 | 103 object GLBs (`assets/models/objects/`) |
| Audio | 146 SFX files (75 sound ids) and 31 music tracks | 177 files under `assets/audio/` |
| Reference coverage | 100 % of 1,025 concepts | The reference list is hand-picked. Magic Missile, Aqua Scepter, Valor, the Golden Key, the Bone Welder, spikes, bookcases and pylons are not on it, so 100 % hides real gaps |
| Core tests | 325 at W0; 574 at the W3 gate; 1,766 at the final integration | `tests/Strata.Core.Tests` |

## 4. Rendering and lighting

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Physical sky, clouds, sky-lit PBR, graphics presets as data | ✅ | `game/Ambience/EnvironmentController.cs`, `data/config/graphics.json` | — |
| Water, lava, grass, leaf-card trees, character shader, horizon heightfield | ✅ | `docs/architecture/rendering.md` | — |
| Exposure: a deterministic model (cave and night boost, biome bias) that darkens fast and brightens slowly, on every preset | ✅ | `src/Strata.Core/Presentation/ExposureModel.cs`, `game/Ambience/EnvironmentController.Exposure.cs`, `data/config/lighting.json` | P1-02, P1-03 (W1a) |
| Glow: normalised levels, HDR threshold, softness and luminance cap from data | ✅ | `data/config/lighting.json` (`glow`), `game/Ambience/EnvironmentController.cs` | P1-03 (W1a) |
| Fog reach: fog starts within `fog.nearCap` even when the horizon pushes its end out; deep caves fog the sky background | ✅ | `game/Ambience/EnvironmentController.Frame.cs`, `data/config/lighting.json` (`fog`) | P1-03 (W1a) |
| Horizon mesh: land albedo clamped to luminance 0.42, AO 0.8 | ✅ | `shaders/horizon.gdshader` | P1-03 (W1a) |
| SSIL and volumetric fog strengths from data (volumetric albedo 0.5) | ✅ | `data/config/lighting.json`, `game/Ambience/EnvironmentController.cs` | P1-03 (W1a) |
| Per-biome exposure bias (desert 0.45, snow, sky, hallow and ocean dimmed) | ✅ | `data/biomes.json` (`exposureBias`), `src/Strata.Core/Content/Defs/BiomeDef.cs` | P1-02 (W1a) |
| Baked block light: flatter curve (`level * mix(linear, 1, level)` times gain, from data) carries torch light to the 7.5 m edge; still 1 level per cell, 15-cell cap | ✅ | `shaders/include/voxel_light.gdshaderinc`, `data/config/lighting.json` (`blockLight`) | P1-04 (W1a); longer reach P1b-17 (W2, optional) |
| Placed dynamic lights: a torch reaches 13.5 m with a flatter falloff (from data); pool 4/8/16/24 by preset | ✅ | `game/Rendering/DynamicLights.cs`, `data/config/lighting.json` (`torch`), `data/config/graphics.json` | P1-04 (W1a) |
| Held torch: energy 1.8, range 16 m, from data (a real light, not baked into voxel light) | ✅ | `game/Player/PlayerLight.cs`, `data/config/lighting.json` (`torch.held*`) | P1-04 (W1a) |
| Torch, fire and lava flicker; flame shader; embers | ✅ | `game/Rendering/DynamicLights.cs:141-149`, `game/Rendering/Flames.cs`, `game/Rendering/LavaEffects.cs` | — |
| Held items and worn wings take the body's eased voxel light; held blocks are real lit cubes | ✅ | `game/Rendering/CreatureVisual.Lit.cs`, `game/Items/BlockItemMeshes.cs` | P3-06 (W5) |
| Block chips, dust and death puffs are pooled voxel-lit bursts with a per-preset budget | ✅ | `game/Rendering/EffectKit.cs`, `shaders/particle_lit.gdshader` | P3-08, P3-20 (W5) |
| Projectile trails: tapering pooled ribbons, bolts with a 6 cm core and a halo, spinning thrown weapons | ✅ | `game/Entities/ProjectileRenderer.Trails.cs`, `game/Entities/ProjectileRenderer.Visuals.cs` | P3-11 (W5) |
| Muzzle flash, staff tip sparks, impact flashes, pooled flash lights | ✅ | `game/Rendering/MuzzleFlashes.cs`, `game/Rendering/FlashLights.cs`, `game/Entities/CombatEffects.Muzzle.cs` | P3-08, P3-10 (W5) |
| Buff and debuff visuals on bodies (tints, looping particles, lights) for 13 buffs; only the main body of a multi-part creature is tinted | ✅ | `game/Rendering/StatusVisuals.cs`, `game/Rendering/EffectKit.Status.cs` | P3-47 (W6) |
| Dynamic resolution follows measured GPU time with hysteresis; without GPU timing it never steps down | ✅ | `game/Rendering/DynamicResolution.cs` | P1b-16 (W2) |
| Cave culling: chunks record which faces see each other and a flood fill from the camera draws only what it can see into (since 2026-09-27; the W0 audit missed it) | ✅ | `game/World/WorldRuntime.cs` (`CaveCulling`), `cull on|off` | — |
| Horizon heightfield samples generator fields only: no caves, structures, edits or hardmode wedges | 🟡 | `src/Strata.Core/Presentation/HorizonField.cs` | — |

## 5. Terrain

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Generation pipeline: 20 passes, plan and chunk tiers, structures, ores, liquids, hardmode mutations | ✅ | `src/Strata.Core/WorldGen/`, `docs/architecture/worldgen.md` | — |
| Determinism: golden chunk hashes (block ids by name, spawn forest, tundra, jungle and desert surfaces, Dungeon halls, vault and keep), `GenVersion` 14 | ✅ | `src/Strata.Core/WorldGen/GenVersion.cs`, `tests/Strata.Core.Tests/WorldGen/WorldGenTests.cs` | 10 P1b-02 (W1b), 11 the Dungeon P4-15 (W8), 12 secret seeds X-07, 13 Strange Plants P5-22 (W10), 14 Don't Dig Up (X-07) |
| Terrain metrics: SurfaceProfile, TerrainStats, `worldgen-report` terrain section and `--compare` | ✅ | `src/Strata.Core/WorldGen/Analysis/SurfaceProfile.cs`, `src/Strata.Core/WorldGen/Analysis/TerrainStats.cs` | P1b-01 (W0) |
| Surface: a heightfield with at most ~2 cells of overhang noise; terraced cliffs and undercuts only in rare cliff zones (~5 % of land) | ✅ | `src/Strata.Core/WorldGen/Fields/TerrainTuning.cs`, `src/Strata.Core/WorldGen/Fields/TerrainField.cs` | P1b-03 (W1b) |
| Mountains and tundra: rounded soft ridges sampled with a cubic B-spline (no grid creases) | ✅ | `src/Strata.Core/WorldGen/Passes/BiomeRelief.cs`, `src/Strata.Core/WorldGen/Plan/Grid2D.cs` (`SampleSmooth`) | P1b-02 (W1b) |
| Biome relief blends with wide smoothstep ramps and blurred relief; no walls at biome borders | ✅ | `src/Strata.Core/WorldGen/Passes/BiomeRelief.cs` | P1b-02 (W1b) |
| Relief is data (`relief` per biome, validated) | ✅ | `data/biomes.json`, `src/Strata.Core/Content/Defs/BiomeDef.cs` (`ReliefSpec`) | P1b-02 (W1b) |
| Caves measure depth from the lowest the true surface can dip, so they keep a roof; worm tunnels start as cave mouths in hillsides or buried | ✅ | `src/Strata.Core/WorldGen/Fields/CaveField.cs`, `src/Strata.Core/WorldGen/Passes/CaveGenerationPass.cs` | P1b-03 (W1b) |
| Smooth ground: up-facing vertices take their height from nearby column tops after Taubin smoothing, so gentle slopes have no stair ripples (RMS 3.19 → 1.67 cm) | ✅ | `src/Strata.Core/Meshing/HeightfieldSmoothing.cs`, `src/Strata.Core/Meshing/SurfaceNetsMesher.cs` | P1b-04 (W1b) |
| Old worlds after a generator change: the world list marks them "old terrain" and asks before playing; edited chunks still seam (old worlds are disposable) | ✅ | `src/Strata.Core/Persistence/SaveCatalog.cs`, `src/Strata.Core/Session/WorldSession.cs` | P1b-02 (W1b) |

W0 baseline: seed 1234, Small, a 512×512-column window at spawn. Reproduce it, or compare a later build against a saved
stats.json with `--compare`, using:

```
dotnet run --project tools/Strata.Tools -c Debug -- worldgen-report --seed=1234 --size=small --out=build/wg
```

| Metric (definition in `TerrainStats.cs`) | All | Forest | Jungle | Corruption |
|---|---|---|---|---|
| Step p99 / max (cells between neighbouring columns) | 2 / 26 | 2 / 26 | 4 / 9 | 4 / 26 |
| Lip % (thin overhanging top) | 0.71 | 0.66 | 1.01 | 1.63 |
| Floating cells per 10k columns | 205 | 202 | 191 | 283 |
| Breach % (top far below the surface height) | 3.88 | 3.56 | 9.62 | 8.11 |
| Roughness (mean second difference) | 0.53 | 0.52 | 0.68 | 0.67 |
| Relief (std-dev of Top in blocks) | 8.0 | 7.5 | 12.7 | 5.7 |

After W1b (P1b-02, P1b-03; same window, all biomes): step p99 1, lip 0.13 %, floating 91 per 10k columns, breach
0.35 %, roughness 0.31, relief 6.36 (79 % of before). Outside the rare cliff zones the whole Small map has no lips or
breaches.

## 6. Performance

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| F3 overlay timers (`gd.mesh_uploads`, `gd.colliders`, `gd.objects`, `gd.grass`, `tick.*`) | ✅ | `game/UI/PerfOverlay.cs` | — |
| Spike log, GC counters, perf probes, a perf.cmds smoke baseline | ✅ | `game/Testing/Scripts/perf.cmds` | P1b-05 (W0) |
| Autosave snapshots on the tick and writes region files on a background thread (atomic, no store lock on the tick); the HUD shows a save spinner | ✅ | `src/Strata.Core/Persistence/WorldSaver.cs`, `src/Strata.Core/Session/GameWorld.Saving.cs` | P1b-06, P1b-07 (W2); spinner X-12 (W5i) |
| Unload flushes go through the background saver | ✅ | `src/Strata.Core/Persistence/WorldSaver.cs`, `src/Strata.Core/Streaming/ChunkStreamer.cs` | P1b-07 (W2) |
| Streaming scheduler: no LINQ or per-frame allocation; budgets in main-thread CPU time | ✅ | `src/Strata.Core/Streaming/ChunkStreamer.Scheduling.cs` | P1b-08 (W2) |
| Colliders: every build within the budget (near chunks first), bodies built off the main thread, light- and liquid-only remeshes keep their colliders | ✅ | `game/World/ColliderManager.cs`, `game/World/ColliderManager.Bodies.cs` | P1b-09 (W2) |
| Tile objects rebuild once per change, to a time budget | ✅ | `game/World/TileObjectRenderer.cs` | P1b-10 (W2) |
| Grass is built to a budget with distance hysteresis | ✅ | `game/World/GrassRenderer.cs` | P1b-11 (W2) |
| Pooled scratch arrays and mesh uploads; gen-2 GCs 30 → 7 in perf.cmds | ✅ | `src/Strata.Core/WorldGen/Passes/CaveGenerationPass.cs`, `game/World/MeshUpload.cs` | P1b-12 (W2) |
| Models load on a threaded asset cache with preloading and a shader warm-up | ✅ | `game/Rendering/AssetCache.cs`, `game/Rendering/AssetPreloader.cs`, `game/Rendering/ShaderWarmup.cs` | P1b-13, P1b-14 (W2) |
| Projectiles share materials and pooled views; a preset-sized light pool goes to the shots nearest the camera | ✅ | `game/Entities/ProjectileRenderer.Pool.cs`, `game/Entities/ProjectileRenderer.Lights.cs` | P1b-15 (W5) |
| Dynamic-resolution oscillation fixed (GPU time, hysteresis) | ✅ | `game/Rendering/DynamicResolution.cs` | P1b-16 (W2) |
| Liquid-only remeshes keep their colliders | ✅ | `game/World/ColliderManager.cs` | P1b-09 (W2) |
| Housing room scans are cached per room until an edit touches it | ✅ | `src/Strata.Core/Gameplay/Housing/HousingCache.cs` | P5-02 (W10) |
| Measured on the player's GPU | ⬜ | master plan §7 | — (lavapipe only) |

## 7. Player movement, camera and targeting

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Controller: step-up, variable jump, coyote time, swimming, breath, fall damage, knockback, i-frame blink | ✅ | `game/Player/PlayerController.cs`, `data/config/movement.json` | — |
| Jump: a full held jump is about 5.4 cells (Terraria proportions; `player.jump_apex_cells`) | ✅ | `data/config/movement.json`, `game/Player/PlayerController.Air.cs` | P3-46 (W6) |
| Boots build run speed up over 1.2 s (`runSpeed` stat); Sprint multiplies only that bonus | ✅ | `data/items/mobility.json`, `data/config/movement.json` | P3-46 (W6) |
| Aim ray starts at the eye plane (`Targeting.CastView`), so nothing behind the player is selected | ✅ | `src/Strata.Core/Gameplay/Interaction/Targeting.cs`, `game/Player/InteractionController.cs` | P1-05 (W1a), P1-06 (W1b) |
| Targeting filters decor and tree canopies by held item (trees stop the ray only in their trunk column, as their colliders do) | ✅ | `src/Strata.Core/Gameplay/Interaction/Targeting.cs` (`TargetFilter`), `game/World/ObjectColliders.cs` | P1-05 (W1a) |
| The body always faces the crosshair; legs follow the movement with strafe and backpedal clips | ✅ | `src/Strata.Core/Presentation/BodyFacing.cs`, `game/Player/PlayerAvatar.Motion.cs` | P1-06 (W1b) |
| Talking: the hovered resident within talking range is who right-click talks to | ✅ | `game/Player/InteractionController.Talk.cs`, `src/Strata.Core/Gameplay/Interaction/EntityPicking.cs` | P1-06, P1-07 (W1b) |
| Cursor reach and use share `ItemUseSystem.ReachMeters` (item reach + Reach stat) | ✅ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs`, `game/Player/InteractionController.cs` | P1-05 (W1a) |
| Crosshair names creatures, bosses, worm segments and residents with their life at any distance (walls hide names), else the block or object in reach; drawn small beside the cross as Terraria's mouse text, bosses in purple | ✅ | `src/Strata.Core/Gameplay/Interaction/EntityPicking.cs`, `game/UI/Hud.cs` (`TargetLabel`) | P1-07 (W1b); W4-POLISH (W5i) |
| Smart Dig (look down with a pickaxe) | ✅ | `game/Player/InteractionController.Talk.cs:15` | — |
| Smart Cursor (X or RS toggles it): pickaxes snap to the nearest minable block near the aim, axes to the nearest tree, the pointer tints; no auto-select | ✅ | `src/Strata.Core/Gameplay/Interaction/SmartTargeting.cs`, `game/Player/InteractionController.Smart.cs` | P2-29 (W4), X-04 (W5i) |
| The inventory and NPC chat are overlays: the player walks, jumps and switches hotbar slots, and a click on empty screen throws, uses or turns the camera; auto-pause (off by default) holds the player | ✅ | `game/UI/InputGate.cs`, `game/UI/WorldClickCatcher.cs` | P2-11 (W4) |
| Mouse-wheel hotbar goes through the rebindable `hotbar_next`/`hotbar_prev` (not over a panel) | ✅ | `game/Player/InteractionController.cs`, `game/Settings/KeyBindings.cs` | P2-11 (W4) |
| Controller: pad bindings for movement, combat, building, the hotbar, inventory, map, mount and Smart Cursor (rebound in their own column; quick heal/mana/buff, the Guide book, emotes and photo mode have no default button, see §17); curved stick look with dead zone, sensitivity, inverted look and gentle aim assist (never on the mouse); a gold-framed cursor on the inventory, chests, shops, crafting and NPC chat; D-pad focus on menus; prompts show the pad button | ✅ | `src/Strata.Core/Presentation/InputConfig.cs`, `src/Strata.Core/Gameplay/Interaction/AimAssist.cs`, `game/Player/CameraRig.Pad.cs`, `game/UI/PadCursor.cs`, `game/UI/PadInput.cs` | X-04 (W5i) |
| First person: a red hurt vignette (setting) and camera punches on heavy hits | ✅ | `game/UI/HurtOverlay.cs`, `game/Player/CameraShake.cs` | P3-25 (W6) |

## 8. Items and weapons

All 667 items load. Every behaviour type is registered and every projectile and ammo class resolves. The failures
below are in behaviour and presentation, not in data.

| Category | Status | Evidence | Fix |
|---|---|---|---|
| Use-style vocabulary: Terraria's styles (swing, thrust, holdUp, eat, drink, throw, yoyo, flail, whip, staff, tome, rapier, spear and more), each with its own body clip, grip frame and first-person pose | ✅ | `data/config/use_styles.json`, `src/Strata.Core/Presentation/UseStyleFp.cs`, `game/Player/ViewModel.Poses.cs` | P3-02, P3-03 (W4), P3-06, P3-07 (W5); per-style hand frames need a tuning pass |
| Use timing: one animation per swing (never restarts), speed prefixes and melee speed scale it, mining speed scales only use time | ✅ | `src/Strata.Core/Gameplay/Items/UseTiming.cs`, `data/config/use_styles.json` (`clips`) | P1-10 (W1a); bursts P3-01' (W5) |
| Tools (86 pickaxes, axes, hammers and drills): every block has 100 health and a pick damage scale (copper: dirt 2 hits, stone 3), staged cracks | ✅ | `src/Strata.Core/Gameplay/Mining/MiningRules.cs`, `game/Rendering/BlockCracks.cs` | P3-24 (W5) |
| Swords (41): hitbox, trail and first-person arc share one swing curve from `use_styles.json`; damaging angles capped so wide arcs never hit behind | ✅ | `src/Strata.Core/Gameplay/Entities/SwingCurve.cs`, `game/Entities/SwingTrail.cs`, `game/Player/ViewModel.Swing.cs` | P3-04 (W5) |
| Swords that also shoot (Starfury, Terra Blade, Zenith and 12 more) | ✅ | The 14 swords swing with every shot; Zenith is `noMelee` with `maxActive: 0` and never blocks (`ItemUseSystem.cs:155-163`, `ItemUseSystem.Shooting.cs:27-29`; `ArsenalTests`, `combat.cmds`) | P1-16 (W1a) |
| Spears and shortswords (22): thrusts hit only near full extension and the model reaches the hitbox; shortswords take melee prefixes | ✅ | `src/Strata.Core/Gameplay/Entities/MeleeSystem.cs`, `src/Strata.Core/Gameplay/Items/Prefixes.cs` | P3-04 (W5), P3-06 (W5) |
| Bows (23): arrows leave the nock and converge on the crosshair, each bow class has its own mesh, Daedalus Stormbow rains from the sky | ✅ | `src/Strata.Core/Gameplay/Entities/ShotGeometry.cs`, `tools/blender/bgen/` | P1-13, P1-14 (W1), P3-39 (W2), P3-09' (W5) |
| Guns (17): muzzle flash and smoke, real bursts (Clockwork Assault Rifle), ammo count on the slot, Minishark/Megashark ammo saving, icons drawn side-on | ✅ | `game/Entities/CombatEffects.Muzzle.cs`, `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Shooting.cs` | P3-38 (W2), P2-06 (W3), P3-01', P3-09', P3-10 (W5) |
| Magic (38): mana, mana sickness, staff and tome styles, tip sparks, glowing bolts with trails | ✅ | `game/Entities/ProjectileRenderer.Trails.cs`, `data/config/use_styles.json` | P3-02 (W4), P3-10, P3-11 (W5) |
| Thrown (shuriken, knives, bombs, grenades): throw style, spinning in flight | ✅ | `data/config/use_styles.json`, `game/Entities/ProjectileRenderer.Visuals.cs` | P3-02, P3-03 (W4), P3-11 (W5) |
| Boomerangs: flight, return, one at a time, throw style, flat spin | ✅ | `game/Entities/ProjectileRenderer.Visuals.cs` | P3-02 (W4), P3-11 (W5) |
| Yoyos (5): a string, cursor control within range while held, re-strikes, reel-in on release | ✅ | `src/Strata.Core/Gameplay/Entities/ProjectileSystem.Special.cs`, `game/Entities/ProjectileRenderer.Tethers.cs` | P3-12 (W5); held-model hiding P3-06 (W6) |
| Flails (5): the ball whirls while held, flies along the aim on release, drags and is reeled in on a chain | ✅ | `game/Rendering/ChainStrip.cs`, `game/Entities/ProjectileRenderer.Tethers.cs` | P3-13 (W5); Solar Eruption stays a returning projectile |
| Whips (Leather Whip, Snapthorn): a Bézier lash with a crack, tag damage that focuses minions, Snapthorn poison; Feral Claws let them repeat | ✅ | `src/Strata.Core/Gameplay/Entities/WhipSystem.cs`, `game/Entities/WhipRenderer.cs` | P3-14, P3-15 (W5) |
| Summons (15): minions and sentries with the minion buff; they appear at the crosshair with a puff | ✅ | `src/Strata.Core/Gameplay/Entities/ShotGeometry.cs`, `game/Entities/CombatEffects.Muzzle.cs` | P3-09', P3-10 (W5), P2-14 (W4) |
| Grappling hooks (9): each hook's own head model, metal links, the Ivy Whip's vine and glowing gem chains; the Misc-slot hook first | ✅ | `game/Player/GrappleVisual.cs`, `src/Strata.Core/Gameplay/Player/GrappleHook.cs` | P1-08, P1-09 (W1), P3-17' (W5) |
| Potions and consumables | ✅ | Healing, mana, buffs and crystals work. Food eats, crystals and crowns hold up, potions drink (P3-02); stack sizes follow 1.4. Life Fruit needs 400 max life and a mechanical boss down (P3-33, `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Consume.cs`). Tooltips now say what every buff potion does and for how long (P2-04, `src/Strata.Core/Gameplay/Buffs/BuffText.cs`). | P3-02 (W4), P3-33 (W5i) |
| Quick heal (H), quick mana (J) and quick buff (B) use potions from anywhere in the backpack without changing the held item: the least-overheal healing potion, Potion Sickness named, food only while no food buff is on; no default controller button (bind one in Settings > Controls) | ✅ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Quick.cs`, `game/Player/InteractionController.Quick.cs` | P3-33 (W5i) |
| Accessories (66) | ✅ | Stat, flight, dash and info effects; set bonuses (`src/Strata.Core/Content/Specs/EffectSpecs.cs`); up to 7 slots as the world unlocks them, each with a vanity slot (P2-05, `src/Strata.Core/Gameplay/Items/PlayerInventory.Equipment.cs`) | — |
| Armour (98 pieces): stats, set bonuses and worn models (rigid per bone; vanity shows over armour) | ✅ | `game/Player/PlayerAvatar.Armor.cs`, `game/Rendering/WornAssets.cs` | P3-41, P3-42 (W7) |
| Wings (14) | ✅ | Flight works and wings are drawn | — |
| Mounts: Minecart, Slimy Saddle, Fuzzy Carrot, Honeyed Goggles, Cosmic Car Key; the Misc mount slot and quick mount (G, D-pad up) | ✅ | `src/Strata.Core/Gameplay/Mounts/MountRules.cs`, `game/Player/PlayerController.Mounts.cs` | X-14 |
| Pets and light pets (Shadow Orb, Crimson Heart, Fairy Bell, Magic Lantern, Wisp, Baby Penguin, Zephyr Fish, Baby Truffle) in the Misc slots; light pets take pooled lights | ✅ | `src/Strata.Core/Gameplay/Entities/ProjectileSystem.Pets.cs`, `game/Entities/ProjectileRenderer.Pets.cs` | P3-18 (W5) |
| Vanity and dyes: vanity and dye slots, seasonal vanity and the Dye Trader's dyes exist; dyes are not drawn on the avatar | 🟡 | `src/Strata.Core/Gameplay/Items/PlayerInventory.Equipment.cs`, `src/Strata.Core/Gameplay/Town/TownServices.DyeTrader.cs` | P2-16 (W4), P5-16, P5-22 (W10); drawing dyes is a known gap |
| Prefixes: Terraria 1.4.4's 83 (data, groups per Terraria), rolled per unit on crafts, drops, chests and shop buys; old ids load through aliases | ✅ | `data/prefixes.json`, `src/Strata.Core/Gameplay/Items/Prefixes.cs` | P3-31 (W6) |
| Rarity colours: every tier, plus Quest, Expert (rainbow) and Master (fiery pulse), in Core; the Expert and Master names are coloured once, not animated yet | ✅ | `src/Strata.Core/Presentation/RarityColors.cs`, `game/UI/UiTheme.cs` | P2-01 (W3); animated names with the tooltip follower P2-10 (W4) |
| Coins: four coin slots that convert upwards and show in the inventory's coin column; the wallet spans the coin slots, the backpack, then the Piggy Bank and Safe | ✅ | `src/Strata.Core/Gameplay/Items/PlayerInventory.Purse.cs`, `src/Strata.Core/Gameplay/Items/Coins.Purse.cs` | P2-05, P2-06 (W3); coin slot UI P2-15 (W4) |
| Ammo: four ammo slots fire first, crafting counts arrows in them, and they show in the ammo column | ✅ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Shooting.cs`, `src/Strata.Core/Gameplay/Items/PlayerInventory.Purse.cs` | P2-06 (W3); ammo slot UI P2-15 (W4) |
| Per-item use sounds: item `soundSet` over the use style's sound and heavy sound; a voice pool steals the oldest | ✅ | `src/Strata.Core/Presentation/ItemSounds.cs`, `src/Strata.Core/Presentation/SfxVoicePool.cs` | P3-27 (W6) |
| Autoswing: every melee weapon repeats, so `autoReuse` is dead data for melee | ✅ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs:89` | Whip and Feral Claws rule: P3-15 (W5) |
| Failed uses say why once per press ("Needs Wooden Arrow", "Not enough mana", Potion Sickness) with a dry click, mana fizzle or tink | ✅ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs` (`Failed`), `game/Audio/SoundEvents.cs` | P1-15 (W1a) |
| Shots leave the weapon hand and converge on the crosshair (the eye only when the hand is inside terrain) | ✅ | `src/Strata.Core/Gameplay/Entities/ShotGeometry.cs` | P1-13 (W1a); muzzle sockets P3-05 (W2), P3-09' (W5) |
| Held-item grip: per use style, the hand frame times the inverse of the model's `grip` socket; ranged weapons aim at the crosshair | 🟡 | `game/Player/PlayerAvatar.Held.cs`, `game/Rendering/PropLibrary.Sockets.cs` | P3-05 (W2), P3-06, P3-07 (W5); swing, gun, throw, holdUp, eat, yoyo, flail and whip hand frames still need a tuning pass |
| Held blocks: real 0.2 m lit cubes with the terrain layers (glass draws opaque) | ✅ | `game/Items/BlockItemMeshes.cs`, `game/Player/ViewModel.Block.cs` | P3-06 (W5) |
| Dropped items: real 3D models (block cubes), spin and bob, rarity halo, preset-capped lights | ✅ | `game/Items/DroppedItemRenderer.cs`, `game/Items/DroppedItemRenderer.Visuals.cs`, `shaders/drop_halo.gdshader` | P3-36 (W6) |
| The item magnet pulls only what fits; Hearts and Stars heal on touch and never take a slot | ✅ | `src/Strata.Core/Gameplay/Items/DroppedItems.cs`, `data/config/pickups.json` | P3-29 (W6) |
| Treasure bags and trophies for every boss (Expert), opened with a right click | ✅ | `data/items/boss_bags.json`, `data/loot/bosses.json` | P4-04 (W7), P4-25 (W9) |

## 9. UI

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Theme: translucent Terraria blue panels, slot kinds and gold hover text as one global theme; check boxes keep Godot's default icons | ✅ | `game/UI/UiTheme.cs`, `game/UI/UiTheme.Build.cs`, `assets/ui/strata_theme.tres` | P2-01 (W3) |
| UI font: Comic Neue Bold (SIL OFL) with Andika Bold as the glyph fallback, everywhere, with a UI-scale guard | ✅ | `assets/fonts/`, `game/UI/UiFonts.cs` | P2-01 (W3) |
| UI sprites (hearts, stars, bubbles, slot silhouettes, buttons, pointer, frames) and 39 buff and 16 event icons, generated, validated and drawn by the HUD and inventory | ✅ | `tools/textures/build_ui.py`, `game/UI/UiSprites.cs`, `game/UI/StatusIcons.cs` | P2-02, P2-03 (W3); shown by P2-10, P2-13, P2-14, P2-23 (W4) |
| **HUD**: Terraria hearts (golden past 400) and mana stars, or bars by setting | ✅ | `game/UI/Hud/LifeHearts.cs`, `game/UI/Hud/ManaStars.cs`, `src/Strata.Core/Presentation/VitalsDisplay.cs` | P2-13 (W4) |
| Breath bubbles over the player's head while under water | ✅ | `game/UI/Hud/BreathBubbles.cs` | P2-13 (W4) |
| Hotbar top-left with the selected slot enlarged and the held item's name over it, shown once (the inventory's top row when open) | ✅ | `game/UI/Hud/HotbarBar.cs`, `game/UI/Hud/ItemNameLabel.cs` | P2-12 (W4) |
| Buff bar: icons with timers, hover names and text, right-click cancel, the minion buff | ✅ | `game/UI/Hud/BuffBar.cs`, `src/Strata.Core/Presentation/BuffDisplay.cs` | P2-14 (W4) |
| Boss bar: Terraria frame with the head icon, life numbers and a damage trail; one bar per boss group alive (up to three), stacked bottom-centre, fading in and out | ✅ | `game/UI/Hud/BossBar.cs`, `src/Strata.Core/Presentation/BossBarModel.cs` | P2-24 (W2), P2-23 (W4), P4-03 (W7), W4-POLISH (W5i) |
| Invasion and event progress panel | ✅ | `game/UI/Hud/EventProgressPanel.cs` | P2-23 (W4) |
| Status messages: a chat log in Terraria colours that fades, with the history on the chat key | ✅ | `game/UI/Hud/ChatLog.cs`, `src/Strata.Core/Presentation/ChatHistory.cs` | P2-21 (W4) |
| Pickup feedback: merged popups over the player's head | ✅ | `game/UI/Hud/PickupPopups.cs`, `src/Strata.Core/Presentation/PickupFeed.cs` | P2-22 (W4) |
| Floating combat text: Terraria colours, pop-in, fixed screen size, green heal and blue mana numbers | ✅ | `src/Strata.Core/Presentation/CombatText.cs`, `game/Entities/CombatEffects.cs` | P2-19 (W3); heart and star pickups P3-29 (W6) |
| Enemy health: a small green-to-red billboard bar under hurt enemies (bosses keep the HUD bar) | ✅ | `game/Entities/NpcRenderer.HealthBar.cs`, `shaders/ui_healthbar.gdshader` | P2-20 (W3) |
| Death screen: dim, "You were slain...", the cause, the respawn countdown; death messages name what killed you | ✅ | `game/UI/Hud/DeathScreen.cs`, `src/Strata.Core/Presentation/DeathMessages.cs` | P2-25 (W4) |
| Custom pointer (pink; Smart Cursor tint); held stack at the pointer on every screen | ✅ | `game/UI/UiCursor.cs`, `game/UI/CursorItemLayer.cs` | P2-10 (W4) |
| Info accessories readout in its own layout region, clear of the F3 overlay and minimap | ✅ | `game/UI/Hud/InfoPanel.cs`, `src/Strata.Core/Presentation/UiLayout.cs` | P2-12 (W4) |
| Layout: the HUD and inventory sit on Core `UiLayout` regions that never overlap from 1024×600 up at any UI scale (`ui.overlaps == 0` in the smoke); what each widget draws stays inside its region (`layout.overlaps == 0` at 1280×720 and 1920×1080, UI scales 1 and 1.5, chest or shop open) | ✅ | `game/UI/InventoryScreen.cs`, `game/UI/CraftingPanel.cs`, `src/Strata.Core/Presentation/UiLayout.cs` | P2-12, P2-15 (W4), W4-POLISH (W5i) |
| **Inventory**: 50-slot backpack, Loot all / Deposit all, split and merge | ✅ | `src/Strata.Core/Gameplay/Items/SlotActions.cs` | — |
| Terraria slot clicks: right-click takes one (held: faster), equips, swaps vanity and opens crates; Ctrl trashes, Alt favourites, Shift quick-moves | ✅ | `src/Strata.Core/Gameplay/Items/SlotActions.cs`, `game/UI/InventoryScreen.Slots.cs` | P2-07 (W3); Shift-sell in shops P2-27 (W4) |
| Trash slot is saved with the player; Ctrl+click trashes, Shift+click quick-moves | ✅ | `src/Strata.Core/Gameplay/Items/PlayerInventory.cs`, `game/UI/InventoryScreen.cs` | P2-05, P2-07 (W3); placement P2-15 (W4) |
| Coin and ammo slots: coin and ammo columns beside the backpack | ✅ | `game/UI/Inventory/CoinAmmoColumns.cs` | P2-15 (W4) |
| Sort (favourites stay put), quick stack, restock, Loot All, Deposit All, saved chest names, favourites (gold wash), quick stack to nearby chests | ✅ | `src/Strata.Core/Gameplay/Items/InventoryOps.cs`, `game/UI/Inventory/ChestPanel.cs` | P2-07, P2-08 (W3); chest panel look P2-17 (W4) |
| Throw the cursor item by clicking outside the inventory (right click: one) | ✅ | `game/UI/InventoryScreen.Slots.cs` (`ThrowCursor`) | P2-07 (W3) |
| Equipment: Dye \| Vanity \| Equip columns with silhouettes and accessory eyes, the Misc tab (pet, light pet, minecart, mount, hook), the defence shield; a locked accessory slot's tooltip says what unlocks it | ✅ | `src/Strata.Core/Gameplay/Items/PlayerInventory.Equipment.cs`, `game/UI/InventoryScreen.cs` | P2-05 (W3); P2-16 (W4), W4-POLISH (W5i) |
| **Crafting**: a recipe column that rebuilds only when something changes, with the materials and Required Objects of the selected recipe | ✅ | `game/UI/CraftingPanel.cs`, `game/UI/RecipeDetail.cs` | P2-18 (W4) |
| Crafting tooltips and the Guide name stations ("Iron Anvil or Lead Anvil"), also on the Required Objects line | ✅ | `src/Strata.Core/Content/Index/StationNames.cs`, `game/UI/CraftingPanel.cs:51` | P2-04 (W3); P2-18 (W4) |
| Craft to cursor, material slots, hold-to-craft | ✅ | `game/UI/CraftingPanel.Recipe.cs` | P2-18 (W4) |
| **Chests**: Terraria's chest panel (text links, rename field, closing creak, closes out of reach); the lid opens | ✅ | `game/UI/Inventory/ChestPanel.cs` | P2-17 (W4), P3-48 (W6) |
| Piggy Bank and Safe: shared 40-slot banks per player, paid from after the purse | ✅ | `src/Strata.Core/Gameplay/Items/Coins.Purse.cs`, `src/Strata.Core/Content/Defs/TileObjectDef.cs` (`SharedStorage`) | P3-35 (W6) |
| **Tooltips**: speed and knockback words match the reference game | ✅ | `src/Strata.Core/Gameplay/Items/ItemTooltip.cs` | — |
| Tooltips show prefix-adjusted stats with green/red modifier lines, buff text and duration, and the shop's coin-tier price | ✅ | `src/Strata.Core/Gameplay/Items/ItemTooltip.cs`, `src/Strata.Core/Gameplay/Items/ItemTooltip.Extras.cs` | P2-04 (W3); Terraria prefixes P3-31 (W6) |
| Tooltips appear at once and follow the pointer, on slots, shop items and buffs | ✅ | `game/UI/TooltipFollower.cs` | P2-10 (W4) |
| **NPC chat**: Terraria's box at the top centre with text buttons, fitted to its layout region | ✅ | `game/UI/TownDialog.cs` | P2-26 (W4) |
| Shop: Terraria grid under the backpack; buy to cursor, sell by click or drop, buyback | ✅ | `game/UI/Inventory/ShopPanel.cs`, `src/Strata.Core/Gameplay/Town/TownCommerce.Trade.cs` | P2-27 (W4) |
| Reforge: Goblin Tinkerer's reforge slot with the coin-coloured cost, hammer and clang; Shift-click puts a backpack item into the slot | ✅ | `game/UI/Inventory/ServicePanel.cs` | P2-28 (W4), W4-POLISH (W5i) |
| Housing menu in the inventory's Housing tab: banners, '?' room query, assign, swap, evict; banners hang in the rooms while it is open | ✅ | `game/UI/HousingPanel.cs`, `game/Entities/HousingBannerRenderer.cs`, `src/Strata.Core/Gameplay/Town/HousingQuery.cs` | P5-05, P5-29 (W10) |
| **Map**: full map and minimap with markers, per player per world; the land around a fresh spawn is revealed at once | ✅ | `game/UI/WorldMapScreen.cs`, `game/UI/MapView.cs`, `src/Strata.Core/Gameplay/Map/MapSystem.cs` | W4-POLISH (W5i) |
| Map framing and zoom buttons; markers are icons (spawn flag, bed, resident and boss heads) with the name on hover | ✅ | `game/UI/WorldMapScreen.cs`, `game/UI/MapMarkers.cs` | P2-12 (W4), W4-POLISH (W5i) |
| **Menus**: swaying logo, hover-grow words, blue select rows, skinned settings, Terraria pause panel; controller focus (D-pad or stick, B for Back) and an Accessibility tab | ✅ | `game/Menu/MenuLogo.cs`, `game/Menu/MenuKit.cs`, `game/Menu/SettingsScreen.cs`, `game/UI/PauseMenu.cs` | P2-30 (W4), X-04, X-12 (W5i) |
| Item slot rendering: Terraria slot sprites, icon fit, outlined counts, grab sound; mipmapped icons stay crisp when drawn small | ✅ | `game/UI/ItemSlotView.cs`, `game/UI/ItemSlotView.Draw.cs`, `tools/textures/icon_mipmaps.py` | P2-10 (W4), W4-POLISH (W5i) |
| **Accessibility**: colour-blind modes (protanopia, deuteranopia, tritanopia) with a strength slider: a filter over the world view and tuned interface palettes that `validate` keeps apart | ✅ | `src/Strata.Core/Presentation/ColorVision.cs`, `game/Rendering/ColorBlindFilter.cs`, `data/config/color_vision.json` | X-12 (W5i) |
| Subtitles for important sounds (bosses, arrivals, weather, nearby creatures) above the chat log, with an arrow toward positional sounds | ✅ | `src/Strata.Core/Presentation/CaptionFeed.cs`, `game/UI/Hud/CaptionsPanel.cs`, `data/config/captions.json` | X-12 (W5i) |
| An autosave spinner in the bottom-right corner (or the nearest free spot) while a save is written | ✅ | `game/UI/Hud/SaveSpinner.cs`, `src/Strata.Core/Presentation/HudOverlays.cs` | X-12 (W5i) |

## 10. Game feel and audio

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Music director: boss > event > Blood Moon > biome > layer; 54 tracks with per-boss themes for ten bosses, a victory sting, 2 s crossfade | ✅ | `game/Audio/MusicDirector.cs` | X-02 (W5i) |
| Music variants: rain, town day and night, night versions, underground jungle, ice and evil biomes, space; the defeat fade | ✅ | `game/Audio/MusicDirector.Variants.cs` | P3-49 with X-08 |
| 16 ambience beds by biome and enclosure, cave reverb, outdoor low-pass, cave drips, fishing bite sound | ✅ | `src/Strata.Core/Presentation/AmbienceConfig.cs`, `game/Ambience/AmbienceDirector.cs`, `data/config/ambience.json` | P3-49 with X-08 |
| Per-material dig, break and footstep sounds; positional SFX pool | ✅ | `game/Audio/SoundEvents.cs`, `game/Player/PlayerController.Sound.cs` | — |
| Creature materials: hit and death sounds and hit dust per material (flesh, slime, bone, metal…); damage-over-time ticks are quieter | ✅ | `data/config/creature_materials.json`, `src/Strata.Core/Presentation/CreatureMaterialsConfig.cs` | P3-19 (W5) |
| Water entry splashes, projectile impacts, tool whoosh and per-item sets sound; the voice pool steals the oldest rather than dropping | ✅ | `game/Player/PlayerDust.cs`, `src/Strata.Core/Presentation/SfxVoicePool.cs`, `src/Strata.Core/Presentation/ItemSounds.cs` | P3-11 (W5), P3-26, P3-27 (W6) |
| Hit dust per material, death dust and pooled gore that bounces; human creatures ragdoll on death within the preset budget | ✅ | `game/Entities/GoreSystem.cs`, `game/Rendering/Ragdoll.cs` | P3-19, P3-20 (W5), X-16 |
| Chopping and hitting objects: hit sounds, chips, leaves and crack progress; pots shatter; felled trees wobble, topple and crash | ✅ | `game/World/TreeEffects.cs`, `src/Strata.Core/Gameplay/Interaction/WorldEditor.Objects.cs` | P3-21, P3-22 (W5) |
| Staged crack decals on every damaged block within 24 m, fading over the last second | ✅ | `game/Rendering/BlockCracks.cs`, `shaders/block_crack.gdshader` | P3-24 (W5) |
| Swords and friendly shots break pots and cut grass, flowers and mushrooms; furniture stays | ✅ | `src/Strata.Core/Gameplay/Interaction/WorldEditor.Objects.cs` (`SmashObject`) | P3-23 (W5) |
| Hurt feedback: player hurt sound, red number, i-frames, flinch | ✅ | `src/Strata.Core/Gameplay/Entities/NpcSystem.cs:26,257-270` | — |
| Fall and debuff damage show numbers through Core; a quieter periodic hurt sound; a red hurt vignette; knockback away and up, scaled by resistance | ✅ | `src/Strata.Core/Session/GameWorld.cs` (`HurtPlayerDirect`), `game/UI/HurtOverlay.cs` | P3-25 (W6) |
| Camera punches with roll: boss arrival and defeat, heavy hits, hard landings, heavy guns | ✅ | `game/Player/CameraShake.cs` | P3-25 (W6), P4-03 (W7), X-02 (W5i) |
| Enemy hit flash and knockback | ✅ | `src/Strata.Core/Gameplay/Entities/NpcSystem.cs:281`, `game/Entities/NpcRenderer.cs:141-143` | Squash and pop: P3-25 (W6) |
| Landing chips in the ground colour, jump, dash and sprint puffs, splash rings by plunge speed | ✅ | `game/Player/PlayerDust.cs`, `game/Player/PlayerController.Events.cs` | P3-26 (W6) |
| Block hit and break dust: voxel-lit, coloured per block | ✅ | `game/Rendering/EffectKit.cs` | P3-08 (W5), P3-24 (W5) |
| Hearts and mana Stars from kills by Terraria 1.4.4's rules; 5–9 Hearts on a boss defeat | ✅ | `data/config/pickups.json`, `src/Strata.Core/Gameplay/Entities/NpcSystem.Death.cs` | P3-29 (W6) |
| Hold-to-mine, placement rate, footsteps | ✅ | `game/Player/InteractionController.cs`, `src/Strata.Core/Content/Loading/DerivedContent.cs` | — |

## 11. Art and models

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Blender pipeline: deterministic generators, icons, synthesised textures | ✅ | `tools/blender/build_all.py`, `tools/blender/bgen/` | — |
| Rebuilt bosses read as the reference game's (King Slime, Eye, Brain, Twins, Skeletron, Wall of Flesh, Deerclops; 7–18k triangles) | ✅ | `tools/blender/bgen/bosses_pre.py`, `tools/blender/bgen/detail.py` | — |
| Humans: 97 procedural characters with faces, hands, clothing, 52-bone rig, shared animation sets | ✅ | `tools/blender/bgen/human/` | Themed humanoids: P3-50 (W7) |
| Weapons and tools: a silhouette per class and tier (swords, bows, guns, staffs, tools) | ✅ | `tools/blender/bgen/` | P3-39, P3-40 (W2) |
| Metal colours: a neutral metal slot tinted per metal (copper, palladium and hellstone read apart); glowing ores | ✅ | `tools/textures/props.json`, `data/blocks/` (`glow`) | P3-37, P3-37b (W2) |
| Icons: outlined, framed to fill the slot, guns side-on | ✅ | `tools/blender/bgen/export.py`, `tools/textures/build_icons.py` | P3-38 (W2) |
| Items have their own archetypes (herbs, hats, rings) | ✅ | `tools/blender/bgen/items.py` | P3-38, P3-40 (W2) |
| Held-item sockets (grip, muzzle, tip, nock, string, chain, handle end) | ✅ | `game/Rendering/PropLibrary.Sockets.cs`, `game/Player/HeldSockets.cs` | P3-05 (W2) |
| Worn armour (98 pieces) and vanity | ✅ | `game/Player/PlayerAvatar.Armor.cs` | P3-41, P3-42 (W7) |
| Creatures with their own bodies: canines, spiders, the Mimic chest, shark, flinx, tortoise, lizard, winged serpent, bone serpent, eater of souls, crimera, elementals, antlion | ✅ | `tools/blender/bgen/beasts.py`, `tools/blender/bgen/fauna.py` | P3-43, P3-44 (W7) |
| Themed enemies (mummy, granite golem, casters) have their own bodies or robes | ✅ | `tools/blender/bgen/human/accessories.py` | P3-50 (W7) |
| Common enemies rebuilt at higher fidelity (slimes, demon eyes, bats) | ✅ | `tools/blender/bgen/creatures.py` | P3-45 (W7) |
| Event and late bosses rebuilt and rigged (Golem, Plantera, Mourning Wood, Pumpking, Everscream, Santa-NK1, Betsy, Mothron, Martian Saucer, Flying Dutchman, Celestial Pillars; 6–12k triangles) | ✅ | `tools/blender/bgen/bosses_moons*.py`, `tools/blender/bgen/bosses_events*.py` | P4-29 (W7), P4-35, P4-36, P4-37 (W9) |
| Fur, chitin and bone texture layers | ✅ | `tools/textures/props.json` | P3-43 (W7) |
| Furniture and stations are bevelled; 14 furniture families of 17 pieces | ✅ | `tools/blender/bgen/furniture.py`, `tools/blender/bgen/furniture_family.py` | P4-38 (W9), X-03 |
| Boss head icons, NPC head portraits and bestiary thumbnails (outlined; the boss bar, map heads, housing banners and the Guide book use them) | ✅ | `game/Items/PortraitIcons.cs`, `assets/icons/bosses/`, `assets/icons/npcs/`, `assets/icons/creatures/` | P2-24 (W2), P5-28 (W10), Art slice |

## 12. Bosses

Shared framework (W7–W9, done): parts with their own life (`src/Strata.Core/Gameplay/Entities/NpcSystem.Parts.cs`,
`NpcSystem.Segments.cs`), an Expert context and enrage (`NpcSystem.Enrage.cs`), pattern upgrades
(`src/Strata.Core/AI/PatternBrain.Moves.cs`), presentation through the chat log, boss bar, camera punches and X-02's
title cards, treasure bags and trophies (`data/items/boss_bags.json`), Expert-only drops that Journey does not pass, and
natural arrivals (`data/events.json`). Gaps: treasure bags drop one per kill (no per-player bags); Moon Lord and the event
bosses run pattern loops rather than Terraria's full AI.

| Boss | Status | What works / what's missing (evidence) | Fix |
|---|---|---|---|
| King Slime | ✅ | Charged hops, teleport, sheds slimes, Expert spiked slimes; Slime Rain ends in King Slime; natural arrival; bag and trophy (`src/Strata.Core/AI/BossBrains.cs`, `data/events.json`) | P4-05, P4-06 (W7) |
| Eye of Cthulhu | ✅ | Circles, dash bursts, servants, phase 2 at 50 % with Expert rapid dashes; the "evil presence" natural spawn (`src/Strata.Core/AI/BossBrains.cs`, `data/events.json`) | P4-05, P4-06 (W7) |
| Eater of Worlds | ✅ | Tunnels with segments that each take their own life and split the worm, Vile Spit (`src/Strata.Core/Gameplay/Entities/NpcSystem.Segments.cs`, `src/Strata.Core/AI/WormBrain.cs`) | P4-07 (W7) |
| Brain of Cthulhu | ✅ | Invulnerable while Creepers live; illusions and confusion (`src/Strata.Core/AI/BossBrains.cs`, `game/Entities/NpcRenderer.Illusions.cs`); illusions are darkened, not translucent | P4-08 (W7) |
| Queen Bee | ✅ | Dashes, stingers, bee summons, Terraria tuning and enrage (`data/enemies/hive.json`) | P4-09 (W7) |
| Deerclops | 🟡 | Walks, hops, ice volleys, shadow hands and debris (`data/enemies/deerclops.json`). No screen-darkening insanity | P4-09 (W7) |
| Skeletron | ✅ | Hands are separate killable enemies, spin, day rage, Expert skull volleys, Terraria loot and bag (`src/Strata.Core/AI/HandBrain.cs`, `src/Strata.Core/AI/DungeonBrains.cs`, `data/enemies/dungeon.json`) | P4-10, P4-11 (W7) |
| Wall of Flesh | ✅ | 8000 life, The Tongued pull, Hungry, Leeches, faster lasers (`src/Strata.Core/AI/MawwallBrain.cs`); sweeps along X only | P4-24 (W9) |
| Queen Slime | ✅ | Hops and crystals, then a winged flying phase (`data/enemies/hallow_ocean.json`) | P4-26 (W9) |
| The Twins | ✅ | Paired eyes, phase-2 lasers and flamethrower, one defeat flag (`data/enemies/mechs.json:40`) | Strength tuning: P4-27 (W9) |
| The Destroyer | ✅ | 80000 shared life, segments that turn dark as plates break, probes and lasers (`src/Strata.Core/Gameplay/Entities/NpcSystem.Segments.cs`, `data/enemies/mechs.json`) | P4-27 (W9) |
| Skeletron Prime | ✅ | Saw, vice, cannon and laser as killable parts (`src/Strata.Core/Gameplay/Entities/NpcSystem.Parts.cs`, `data/enemies/mechs.json`) | P4-28 (W9) |
| Plantera | ✅ | Hooks, tentacles, seed and thorn volleys, phase swap, jungle enrage; rebuilt model (`src/Strata.Core/AI/PlantBrains.cs`) | P4-29 (W7), P4-30 (W9) |
| Golem | ✅ | Sealed body, a head that frees itself, punching fists; rebuilt model (`data/enemies/temple.json`, `src/Strata.Core/AI/HandBrain.Styles.cs`) | P4-29 (W7), P4-31 (W9) |
| Duke Fishron | ✅ | Dash, bubbles, Sharknadoes, phase 3 and the ocean rage (`data/enemies/hallow_ocean.json`) | P4-32 (W9) |
| Empress of Light | ✅ | Bolts and lances; lethal by day instead of leaving (`data/enemies/hallow_ocean.json`, `src/Strata.Core/Gameplay/Entities/NpcSystem.Enrage.cs`) | P4-32 (W9) |
| Lunatic Cultist, Celestial Pillars | ✅ | Ritual, pillar shields and kill goals; rebuilt pillar models; bags (`data/enemies/lunar.json`); the shielded pillar variant model is not swapped in | P4-37 (W9), P4-25 (W9) |
| Moon Lord | 🟡 | Hands and head are separate parts that shield the core, and each frees a True Eye of Cthulhu when it falls; bag (`data/enemies/lunar.json`). Attacks are pattern loops, not Terraria's full AI | — (bags: P4-25) |
| Pumpkin and Frost Moon bosses (Mourning Wood, Pumpking, Everscream, Santa-NK1, Ice Queen) | ✅ | Pattern loops; rebuilt rigged models with walk, fly, idle and attack clips (`data/enemies/moons.json`, `tools/blender/bgen/bosses_moons*.py`) | P4-35 (W9) |
| Betsy, Mothron, Martian Saucer, Flying Dutchman, Dark Mage, Ogre | ✅ | Pattern loops; rebuilt rigged models (`data/enemies/invasions.json`, `tools/blender/bgen/bosses_events*.py`) | P4-36, P4-37 (W9) |
| Boss bar, announcements, music, intros | ✅ | Terraria boss bar (P2-23), one per boss group (W4-POLISH), chat-log announcements (P2-21), boss music by priority. A title card with cinema bars and camera framing on arrival, a "Defeated" card with brief slow motion, per-boss themes and a victory sting (X-02, `game/UI/BossIntro.cs`, `src/Strata.Core/Presentation/BossIntroModel.cs`) | P2-23 (W4), P4-03 (W7), X-02 (W5i) |

## 13. Dungeon

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Zone, brick colour per world, gatehouse, descent shaft, locked bricks until Skeletron (pick power 65) | ✅ | `src/Strata.Core/WorldGen/Structures/DungeonBuilder.cs`, `src/Strata.Core/Gameplay/Interaction/WorldEditor.cs:197` | — |
| Old Man at the gate, curse, Skeletron summon, Clothier after the defeat | ✅ | `data/town.json:99`, `src/Strata.Core/Gameplay/Town/TownSystem.Landmarks.cs` | — |
| Dungeon Guardian: invulnerable, 1000 damage, leaves when you exit | ✅ | `data/enemies/dungeon.json:38` | — |
| Enemies: Angry Bones, Dark Caster, Cursed Skull, Dungeon Slime; Paladin, Necromancer, Skeleton Sniper, Dungeon Spirit after Plantera | ✅ | `data/enemies/dungeon.json`, `data/enemies/bestiary.json` | Variants: P4-20 (W8) |
| Layout: a keep with winding sloped halls, stairs and shafts, rooms by role (GenVersion 11) | ✅ | `src/Strata.Core/WorldGen/Structures/Dungeon/DungeonPlanner.cs`, `src/Strata.Core/WorldGen/Structures/DungeonBuilder.Halls.cs` | P4-12, P4-14, P4-15, P4-16 (W8) |
| Wall variety: brick, slab and tiled wall panels | ✅ | `src/Strata.Core/WorldGen/Structures/Dungeon/DungeonStyle.cs`, `data/blocks/built.json` | P4-13 (W8) |
| Furnishing: blue, green and pink furniture sets per room role | ✅ | `src/Strata.Core/WorldGen/Structures/Dungeon/DungeonDecorator.Rooms.cs` | P4-17, P4-18, P4-19 (W8) |
| Spikes, bookcases with books (Water Bolt), chandeliers, banners, statues, wired dart traps (flat halls only) | ✅ | `src/Strata.Core/WorldGen/Structures/Dungeon/DungeonDecorator.Halls.cs`, `tools/blender/bgen/dungeon_decor.py` | P4-18, P4-19 (W8) |
| Chest loot: Terraria's dungeon tables | ✅ | `data/loot/containers.json` | P4-20, P4-23 (W8) |
| Locked Gold Chests, Golden and Shadow Keys, Shadow Chests | ✅ | `src/Strata.Core/Gameplay/Interaction/ObjectInteraction.cs` | P4-20, P4-21 (W8) |
| Dungeon items: Magic Missile, Aqua Scepter, Valor, Bone Welder, Clothier Voodoo Doll | ✅ | `data/items/`, `data/loot/` | P4-20 (W8) |
| Biome chests and keys in the vault (Plantera lock) | ✅ | `src/Strata.Core/WorldGen/Structures/Dungeon/DungeonPlanner.Roles.cs` | P4-33, P4-34 (W8) |
| Bound Mechanic in the Dungeon's `bound_npc` room, freed by talking | ✅ | `src/Strata.Core/Gameplay/Town/TownSystem.Rescues.cs`, `src/Strata.Core/Gameplay/Town/SiteFinder.cs` | P5-06b (W10) |
| Smoke route from the entrance to the vault (keeper, Skeletron, guardian, shaft, library, spikes, a locked chest) | ✅ | `game/Testing/Scripts/dungeon.cmds` | P4-23 (W8) |

## 14. Town NPCs

Shared rules:
* Housing: enclosed flood fill, floor and headroom minimums, door, light, table and chair. A room counts when at least
  50 % of its boundary is player-built, which stands in for background walls. Homes are rechecked every 900 ticks
  (`src/Strata.Core/Gameplay/Housing/HousingScanner.cs`), cached per room (`HousingCache.cs`). Houses in an evil biome are rejected (P5-02).
* Residents walk home at night through doors, take hits, and are announced when they arrive and die
  (`src/Strata.Core/Gameplay/Town/TownSystem.Movement.cs`, `src/Strata.Core/Gameplay/Town/TownSystem.Combat.cs`).

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Move-ins by day only, Terraria's rules per resident | ✅ | `src/Strata.Core/Gameplay/Town/TownSystem.cs`, `data/town/*.json` | P5-01 (W10) |
| Resident counts skip the dead and the Old Man | ✅ | `src/Strata.Core/Gameplay/Town/TownSystem.cs` | P5-01 (W10) |
| Dead residents return at dawn | ✅ | `src/Strata.Core/Gameplay/Town/TownSystem.Life.cs` | P5-01 (W10) |
| Housing: evil biomes rejected, Terraria's query messages, cached room scans | ✅ | `src/Strata.Core/Gameplay/Town/HousingQuery.cs`, `src/Strata.Core/Gameplay/Housing/HousingCache.cs` | P5-02 (W10) |
| Rooms assigned, swapped and evicted from the housing menu | ✅ | `src/Strata.Core/Gameplay/Town/TownSystem.Housing.cs`, `game/UI/HousingPanel.cs` | P5-05, P5-29 (W10) |
| Condition types: rescue, has dye, bestiary %, season, angler quests | ✅ | `src/Strata.Core/Conditions/ConditionCompiler.cs` | P5-03 (W10) |
| Happiness: biome and neighbour love and hate, crowding, solitude, the Princess, happiness dialogue, pylon gating, prices | ✅ | `src/Strata.Core/Gameplay/Town/TownHappiness.cs`, `data/config/town.json` | P5-12 (W10) |
| Services framework: several services per resident (heal, reforge, haircut, dyes, status, Angler quests) | ✅ | `src/Strata.Core/Gameplay/Town/TownServices.cs`, `game/UI/TownDialog.Services.cs` | P5-13 (W10) |
| Residents fight back by their own attack styles; enemies target residents | ✅ | `src/Strata.Core/Gameplay/Town/TownSystem.Defense.cs`, `src/Strata.Core/Gameplay/Town/TownSystem.Combat.cs` | P5-09, P5-10 (W10) |
| Residents sit in chairs and sleep in beds | ✅ | `src/Strata.Core/Gameplay/Town/TownSystem.Seating.cs` | P5-07, P5-11 (W10) |
| Pylons: travel from the map between working pylons; the one-of-each limit and the boss/invasion block are not enforced | 🟡 | `src/Strata.Core/Gameplay/Town/PylonNetwork.cs`, `game/UI/PylonMap.cs` | P5-34 (W10) |
| Town pets (cat, dog, bunny) that can be petted | ✅ | `src/Strata.Core/Gameplay/Town/TownSystem.Pets.cs` | P5-20 (W10) |

Per resident: the move-in rule here compared with the reference game, then the shop or service. The link after each
name is the resident's definition.

| NPC | Move-in | Shop / service | Status | Fix |
|---|---|---|---|---|
| Guide (`data/town/pre_hardmode.json`) | Starter ✅ | Crafting help through the Guide book's recipes and steps; tips by progress | ✅ | P5-14 (W10), P5-27 (W11) |
| Merchant | 50 silver (purse, piggy bank and safe count purse only) ✅ | Terraria's inventory | ✅ | P5-15 (W10) |
| Nurse | Max life ≥ 120 and the Merchant ✅ | Heal and cure at Terraria's price tiers | ✅ | P5-13 (W10) |
| Dryad | Any pre-hardmode boss ✅ | Terraria's inventory; world status (evil and Hallow share) | ✅ | P5-04, P5-13, P5-15 (W10) |
| Old Man | Landmark at the Dungeon gate ✅ | Skeletron summon | ✅ | — |
| Clothier | Skeletron defeated ✅ | Terraria's inventory | ✅ | P5-15 (W10) |
| Arms Dealer | Gun or bullets ✅ | Terraria's inventory | ✅ | P5-15 (W10) |
| Demolitionist | Explosive and the Merchant ✅ | Terraria's inventory | ✅ | P5-04, P5-15 (W10) |
| Goblin Tinkerer | Rescued in the caverns after the Goblin Army ✅ | Terraria's inventory; reforge slot | ✅ | P5-06, P5-15 (W10), P2-28 (W4) |
| Witch Doctor | Queen Bee ✅ | Terraria's inventory; no Imbuing Station | 🟡 | P5-15 (W10) |
| Wizard | Rescued in the caverns in hardmode ✅ | Terraria's inventory | ✅ | P5-06, P5-18 (W10) |
| Mechanic | Rescued in the Dungeon's `bound_npc` room ✅ | Terraria's inventory | ✅ | P5-06b, P5-15 (W10) |
| Steampunker | Any mechanical boss ✅ | Terraria's inventory; no Clentaminator | 🟡 | P5-18 (W10) |
| Cyborg | Plantera ✅ | Terraria's inventory | ✅ | P5-18 (W10) |
| Truffle | Hardmode and a mushroom biome ✅ | Terraria's inventory | ✅ | P5-18 (W10) |
| Dye Trader | A dye or dye material ✅ | Dyes; Strange Plant trades (Strange Plants grow since GenVersion 13) | ✅ | P5-04, P5-16, P5-22 (W10) |
| Painter | 8 residents ✅ | Terraria's inventory | ✅ | P5-16 (W10) |
| Stylist | Rescued from a spider nest ✅ | Haircut panel and hair dyes | ✅ | P5-06, P5-21 (W10) |
| Zoologist | Bestiary 10 % ✅ | Terraria's inventory by bestiary share | ✅ | P5-03, P5-04 (W10), P5-17 (W11) |
| Golfer | Rescued in the underground desert ✅ | Golf gear; golf has no scoring | 🟡 | P5-06 (W10), P5-17 (W11) |
| Tavernkeep | Rescued after an evil boss ✅ | Old One's Army gear; the Defender sentries and armour do not exist yet | 🟡 | P5-06, P5-16 (W10) |
| Party Girl | 14 residents ✅ | Terraria's inventory | ✅ | P5-16 (W10) |
| Tax Collector | Purifying the Tortured Soul ✅ | Taxes ✅ | ✅ | P5-06 (W10) |
| Pirate | Pirate Invasion ✅ | Terraria's inventory | ✅ | P5-18 (W10) |
| Santa Claus | Frost Legion, in December ✅ | Terraria's inventory | ✅ | P5-03, P5-18 (W10), X-06 |
| Princess | Every other resident ✅ | Terraria's inventory | ✅ | P5-04, P5-18 (W10) |
| Angler | Found asleep at the ocean ✅ | Quests with a counter and the rewards table | ✅ | P5-06, P5-23 (W10) |
| Travelling Merchant | Visits at dawn, rotating stock ✅ | Rotating stock | ✅ | P5-19 (W10) |
| Skeleton Merchant | Underground visitor ✅ | Terraria's inventory | ✅ | P5-19 (W10) |

Resident data lives in `data/town/pre_hardmode.json`, `data/town/hardmode.json`, `data/town/visitors.json` and
`data/town/pets.json`; shops are tabbed (P5-15…P5-18). Some sold items do not exist yet (sentries and Defender armour,
the Clentaminator, the Toolbelt, the Imbuing Station); they are left out of the shops.

The `shop`-tagged items nobody sold at W0 (the Leather Whip and Rally) are sold now (P5-15, P5-17).

## 15. Progression and loops

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Progression flags, auto-set cascade, announcements; hardmode mutations, spread, ore tiers | ✅ | `src/Strata.Core/Gameplay/Progression/ProgressionSystem.cs`, `data/progression.json` | — |
| Station chain: furnace, anvils, hellforge, hardmode anvils and forges, ancient manipulator | ✅ | `data/tileobjects/stations.json`, `data/items/lunar.json` | — |
| Events: 7 invasions, moons, eclipse, Blood Moon, Slime Rain, ambient events; kill goals and the progress panel | ✅ | `data/events.json`, `src/Strata.Core/Gameplay/Events/EventDirector.cs` | Progress bar: P2-23 (W4) |
| Plantera, the Jungle Temple, Golem and the lunar endgame | ✅ | `data/enemies/temple.json`, `data/enemies/lunar.json` | Fights: §12 |
| Life Crystals, Mana Crystals, mana sickness; Life Fruit needs 400 max life and a mechanical boss down | ✅ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Consume.cs` | P3-33 (W5i) |
| Fishing and crates: fishing power (rod, bait, gear, weather, time, moon, pool size), bait use, 10 biome crates, lava fishing, 22 quest fish | ✅ | `src/Strata.Core/Gameplay/Fishing/FishingPower.cs`, `data/config/fishing.json` | X-13; bite sound P3-49 |
| Paint: 16 paints and tools | ✅ | `data/items/paint.json` | — |
| Wiring: red, blue, green and yellow wires, actuators, timers, teleporters, statues, logic gates, a wire view per colour | ✅ | `src/Strata.Core/Gameplay/Wiring/WireSystem.cs`, `src/Strata.Core/Gameplay/Wiring/WireSystem.Devices.cs` | X-15 |
| Liquids: flow, mixing, buckets; no pressure | 🟡 | `src/Strata.Core/Simulation/Liquids/LiquidSimulator.cs` | — |
| Fallen Stars fall at night (about a dozen a night near surface players) and fade at dawn | ✅ | `src/Strata.Core/Gameplay/Events/StarfallDirector.cs`, `data/config/starfall.json` | P3-30 (W6) |
| Bed sleep: using a bed sets the spawn and lies down; the clock runs 5× while every player sleeps safely | ✅ | `src/Strata.Core/Session/GameWorld.Sleep.cs` | P3-34 (W6) |
| Natural boss arrivals (Eye, King Slime, mechanical warnings) | ✅ | `data/events.json` | P4-05 (W7) |
| Buffs and debuffs: Shadowflame, food tiers, mount, fishing, banner and crate buffs added; still missing Darkness, Silenced, Broken Armor, Venom and others | 🟡 | `data/combat.json` and per-file buff lists | — |
| Hardmode variants of cavern and underworld creatures | ⬜ | `data/enemies/hardmode.json` (adds 12 new creatures, no variants of old ones) | — |
| Journey mode: listed as a difficulty only | 🟡 | `src/Strata.Core/World/WorldSettings.cs:19` | Powers out of scope (§17) |

## 16. Guide book, bestiary and achievements

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| In-game help: the Guide book's Tips and controls tab, onboarding hints and loading tips; the Guide's crafting help | ✅ | `game/UI/GuideBook/TipsTab.cs`, `game/UI/HintToast.cs`, `game/UI/LoadingTips.cs`, `data/guide/tips.json` | P5-14 (W10), P5-27 (W11), X-10 |
| Item-source reverse index: a Core `ItemSourceIndex` that the obtainability check uses | ✅ | `src/Strata.Core/Content/Index/ItemSources.cs` | P5-24a (W3) |
| Drop-rate computation for the bestiary and item sources | ✅ | `src/Strata.Core/Gameplay/Loot/DropRates.cs` | P5-24b (W11) |
| Player journal: kills, seen, caught, met, obtained, crafted, saved with the player | ✅ | `src/Strata.Core/Gameplay/Guide/PlayerJournal.cs`, `src/Strata.Core/Gameplay/Guide/JournalSystem.cs` | P5-25 (W11) |
| Achievements and popups | ✅ | `src/Strata.Core/Gameplay/Guide/AchievementTracker.cs`, `game/UI/AchievementToast.cs` | P5-26 (W11) |
| Bestiary: unlocks by kills, with drop rates, a live 3D preview and thumbnails | ✅ | `src/Strata.Core/Gameplay/Guide/Bestiary.cs`, `game/UI/GuideBook/BestiaryTab.cs`, `game/UI/GuideBook/CreaturePreview.cs` | P5-31 (W11), P5-28 |
| Guide book on K: Items, Bestiary, Progress, NPCs, Tips and Achievements tabs (no controller binding yet) | ✅ | `game/UI/GuideBook/GuideBookScreen.cs` | P5-30a/b, P5-31, P5-32 (W11) |

## 17. Recorded out of scope

| Item | Status | Note |
|---|---|---|
| Mounts beyond the minecart | ✅ | Done after all: Slimy Saddle, Fuzzy Carrot, Honeyed Goggles, Cosmic Car Key (X-14) |
| Default controller buttons for quick heal/mana/buff, the Guide book, emotes and photo mode | ⬜ | Every standard button is taken and bindings are single buttons (no hold-modifier combos); players bind a spare button (Share, paddles, touchpad) in Settings > Controls. A modifier layer or radial is future work |
| Background walls | ⬜ | Voxel rooms already enclose space; the Dungeon uses wall panels (P4-13) |
| Journey mode powers | ⬜ | Still out of scope; the wiring expansion once listed here was done after all (X-15) |
| Moon Lord and event-boss behaviour beyond pattern loops | 🟡 | Not in the plan; their models were rebuilt in W9 |
| Liquid pressure, the missing buffs and debuffs, hardmode variants of old creatures, a horizon with caves and hardmode wedges, measuring on the player's GPU | ⬜ / 🟡 | Not in the plan (rows marked `—` in §4, §6 and §15); cave culling existed since 2026-09-27 (§4) |

## 18. Stale statements fixed in the master plan

On 2026-09-30, `docs/3D_TERRARIA_MASTER_PLAN.md` still said:
* 6 residents; there are 29.
* Reforging is missing; the Goblin Tinkerer reforges.
* Invasions are missing; there are 7.
* Wings are missing; there are 14.
* Audio is missing; there is a music director and 177 sound files.
* There are no hellforge or hardmode anvils; both exist.
* Plantera and the temple are next targets; both are done.
* Coverage is 54.3 %.
* The hardmode station chain starts at a "cinder forge"; it is the hellforge.

W0 corrected §3, the stale debt lines in §4 and §8, and added a decision-log entry.
