# Strata audit: what works, what is broken, what is missing

> An audit of `main` at M4 (commit `15bf3f4`), written in W0 of the player-feedback overhaul (2026-09-30). It answers three questions: what works, what doesn't, and what's missing. Each row names the
> planned fix. It is refreshed at every phase gate. When a work package (WP) lands, update its rows' status and
> evidence. The summary table in [`3D_TERRARIA_MASTER_PLAN.md`](3D_TERRARIA_MASTER_PLAN.md) §3 points here.

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
| 1 | The desert flashbangs on every preset except low | Auto-exposure is on from medium up. Its ISO window (85–170) pins the tonemap multiplier to 1.65–3.3×, so it can only brighten (`game/Ambience/EnvironmentController.cs:94-97`, `data/config/graphics.json:22`). Glow uses an HDR threshold of 1.0, Screen blend and level weights summing to 4.5 (`EnvironmentController.cs:63,90`). Fog starts about 149 m out on medium and up (`game/Ambience/EnvironmentController.Frame.cs:135-136`, `game/World/WorldRuntime.cs:140`). The horizon is fully lit. | P1-02, P1-03 (W1a) | Planned shot_stats.py (P1-03): desert and snow noon mean luminance in [0.35, 0.6] with under 2 % clipped pixels |
| 2 | Torches light only a tiny radius | 4-bit baked light loses 1 level per 0.5 m cell (`src/Strata.Core/Lighting/LightEngine.cs:131`). The fill is octahedral, so diagonals reach about 4 m. The `x*sqrt(x)` curve (`shaders/include/voxel_light.gdshaderinc:26-37`) adds to the falloff. Placed-light energy is 0.79 with attenuation 1.1 (`game/Rendering/DynamicLights.cs:65,127`). The pool is 0 lights on low (`data/config/graphics.json:12`). | P1-04 (W1a); P1b-17 (W2, optional) | Lighting tour: a torch room lit to about 8–10 m |
| 3 | Terrain is jagged and unpredictable | A 3D overhang density `1.5 + rug*11` over 3 octaves (`src/Strata.Core/WorldGen/Fields/TerrainField.cs:33-42`). Ridged 5-octave mountains on a bilinear 8-cell grid (`src/Strata.Core/WorldGen/Passes/BiomeRelief.cs:20,57-58`, `src/Strata.Core/WorldGen/Plan/WorldPlan.cs:14`). Biome ramps at `Membership*3` (`BiomeRelief.cs:40`). Caves are carved from the 2D height. A binary solid/air threshold gives stair ripples. | P1b-02, P1b-03, P1b-04 (W1b) | `worldgen-report --compare` against the W0 baseline (§5) shows lower roughness, lip, floating and breach |
| 4 | Tool swings look fast-forwarded | The avatar replays the clip whenever `UseAnimation` jumps back up (`game/Player/PlayerAvatar.Motion.cs:44-47`). A new use starts every `useTime` ticks (`src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs:161-162`), so tools restart at 71–79 % of the clip. Speed bonuses shorten only the cooldown (`ItemUseSystem.cs:97,110,158`). The strike is about 90 ms of the clip (`tools/blender/bgen/human/actions.py`). The first-person phase snaps (`game/Player/ViewModel.cs:51`). | P1-10, P1-12 (W1a); P1-11 (W1b) | UseTimingTests: one clip per swing, no restart; mining speed leaves `useAnimation` unchanged |
| 5 | Grappling hooks don't work | `ItemAction.Grapple` is parsed (`src/Strata.Core/Gameplay/Items/ItemUseTable.cs:159`), but `ItemUseSystem.Use` has no branch for it, so clicking does nothing. On F, the anchor test nudges 5 cm into a voxel, while the smooth collider sits up to 0.25 m off, so the hook lets go at once on natural terrain (`game/Player/PlayerController.Grapple.cs:106-111`). | P1-08 (W1a), P1-09 (W1b), P3-17' (W5) | `movement.cmds` latches on a natural slope and stays latched while reeling in |
| 6 | The bow has no animation | The arrow spawns on tick 0 from the eye (`src/Strata.Core/Gameplay/Entities/ProjectileSystem.cs:89`), but the clip releases at t 0.6. First person is an 8 cm lift (`game/Player/ViewModel.cs:69-72`). No ammo, no mana and Blocked are silent (`ItemUseSystem.cs:160`). | P1-13, P1-15 (W1a); P1-14 (W1b) | First-person draw-pose probe; a no-ammo shot sets `msg.last` and `sfx.last` |
| 7 | Swords that shoot never melee | `Shot` fails the melee gate (`ItemUseSystem.cs:154`). Zenith's returning blades make the one-at-a-time rule return `Blocked` (`src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Shooting.cs:27`). | P1-16 (W1a) | Core test: Starfury swings and shoots; Zenith never blocks |
| 8 | The body faces the wrong way when aiming or interacting | The aim ray starts at the camera, 4.2 m behind and 0.55 m right of the player (`game/Player/CameraRig.cs:17,24`). It accepts hits behind the player, such as grass, bushes and tree boxes the camera probe ignores (`game/Player/InteractionController.cs:101-110`). The body then turns past 55° (`game/Player/PlayerAvatar.Motion.cs:84`). | P1-05 (W1a), P1-06 (W1b) | `player.facing_error_deg < 3` after `camera orbit 90`; a target behind the player is never selected |
| 9 | The crosshair doesn't name mobs or bosses | The HUD names only blocks and objects within reach (`game/UI/Hud.cs:211`). There is no ray-vs-entity picking. | P1-07 (W1b) | `hud.target_name` for a slime, a worm segment and a resident |
| 10 | Lag spikes | Synchronous autosave every 120 s (`src/Strata.Core/Session/GameWorld.cs:25,157-160`) rewrites whole region files (`src/Strata.Core/Persistence/RegionFile.cs:42-69`). Other causes: unbudgeted near-player collider builds, tile objects rebuilt twice per remesh, grass churn, first-use GLB loads, per-projectile materials and lights, per-frame LINQ, large-object-heap churn and dynamic-resolution oscillation (§6). | P1b-05 (W0); P1b-06…P1b-16 (W2) | Planned perf.cmds counters (P1b-05); before/after `perf spikes` p99 and max |
| 11 | No guide book | Nothing exists: no bestiary, journal or achievements (§16). | P5-24a (W3); P5-24b, P5-25, P5-26, P5-27, P5-30a/b, P5-31, P5-32 (W11) | Planned ui.cmds book tabs; the bestiary unlocks after a kill |
| 12 | Bosses and the Dungeon should match the reference game, Skeletron 1:1 | Boss parts share one life pool (`src/Strata.Core/Gameplay/Entities/Npc.cs:132-140`). The Dungeon is a grid of boxy rooms that rolls cave-cabin loot (`src/Strata.Core/WorldGen/Structures/DungeonBuilder.cs:116`). See §12 and §13. | P4-01…P4-38 (W7–W9) | SkeletronTests, DungeonGenerationTests, LockedChestTests, `dungeon.cmds` route |
| 13 | The NPC system should be complete | Move-in rules are approximate, there are no rescues, no housing UI, residents can't fight back, and many shops are empty (§14). | P5-01…P5-16, P5-18…P5-23, P5-28, P5-29, P5-34 (W10); P5-17 (W11) | `town.cmds`: day-only move-in, rescue, housing query/assign/evict, residents defending, Nurse heal |
| 14 | UI, items and feel should be much more like Terraria (the top priority) | A generic dark-slate Godot UI with no font, progress-bar vitals and text buffs. Items share generic poses and meshes. Most "juice" is missing (§8–§11). | P2-* (W2–W4), P3-* (W2, W4–W7) | UI tour at 1280×720 and 1920×1080, UI scale 1.0 and 1.5, `layout.overlaps == 0` |

## 3. Content at a glance

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
| Core tests | 325 | `tests/Strata.Core.Tests`, W0 lane B |

## 4. Rendering and lighting

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Physical sky, clouds, sky-lit PBR, graphics presets as data | ✅ | `game/Ambience/EnvironmentController.cs`, `data/config/graphics.json` | — |
| Water, lava, grass, leaf-card trees, character shader, horizon heightfield | ✅ | `docs/architecture/rendering.md` | — |
| Auto-exposure: only brightens (1.65–3.3×), slow adaptation (speed 0.3) | ❌ | `game/Ambience/EnvironmentController.cs:94-97` | P1-02, P1-03 (W1a) |
| Glow: threshold 1.0, Screen blend, level weights sum to 4.5 | ❌ | `game/Ambience/EnvironmentController.cs:63,90` | P1-03 (W1a) |
| Fog reach: `ViewDistance = max(stream, 0.85*horizon)` puts fog about 149 m out in the desert on medium and up | 🟡 | `game/World/WorldRuntime.cs:140`, `game/Ambience/EnvironmentController.Frame.cs:135-136` | P1-03 (W1a) |
| Horizon mesh paints biome `mapColor` fully lit (AO 1) | 🟡 | `shaders/horizon.gdshader` | P1-03 (W1a) |
| SSIL (radius 6, intensity 1.2) and volumetric fog (albedo 0.92) brighten bright biomes on high and ultra | 🟡 | `game/Ambience/EnvironmentController.cs:75-81`, `game/Ambience/EnvironmentController.Frame.cs:141` | P1-03 (W1a) |
| Per-biome exposure bias | ⬜ | `data/biomes.json` (the desert entry only tints colours) | P1-02 (W1a) |
| Baked block light: 1 level per cell, octahedral spread, `x*sqrt(x)` curve, 15-cell cap | 🟡 | `src/Strata.Core/Lighting/LightEngine.cs:131`, `shaders/include/voxel_light.gdshaderinc:26-37` | P1-04 (W1a); P1b-17 (W2, optional) |
| Placed dynamic lights: energy `0.85*peak`, range `3 + 8.5*peak`, attenuation 1.1; pool 0/8/16/24 by preset | 🟡 | `game/Rendering/DynamicLights.cs:65,127`, `data/config/graphics.json:12` | P1-04 (W1a) |
| Held torch: energy 1.26, range 15 m, not baked into voxel light | 🟡 | `game/Player/PlayerLight.cs:31,64,70` | P1-04 (W1a) |
| Torch, fire and lava flicker; flame shader; embers | ✅ | `game/Rendering/DynamicLights.cs:141-149`, `game/Rendering/Flames.cs`, `game/Rendering/LavaEffects.cs` | — |
| Held item and worn wings ignore voxel light (sky-lit in caves) | ❌ | `game/Player/PlayerAvatar.cs:52`, `game/Rendering/CreatureVisual.cs:63-72` | P3-06 (W5) |
| Block chips and death puffs are not voxel-lit (death puff is unshaded) | 🟡 | `game/Entities/BlockEffects.cs:23-24`, `game/Entities/CombatEffects.cs:94` | P3-08 (W5), P3-20 (W5) |
| Projectile trails (`ProjectileDef.Trail`) never drawn; bolts are 9 cm spheres | ⬜ | `src/Strata.Core/Content/Defs/ProjectileDef.cs:32`, `game/Entities/ProjectileRenderer.cs:125-134` | P3-11 (W5) |
| Muzzle flash, impact flashes, pooled flash lights | ⬜ | `game/Entities/CombatEffects.cs` | P3-08, P3-10 (W5) |
| Buff and debuff visuals (fire, poison, frost) on bodies | ⬜ | `game/Entities/NpcRenderer.cs:141-143` | P3-47 (W6) |
| Dynamic resolution: on by default; CPU spikes step the scale down, so it can oscillate | 🟡 | `game/Rendering/DynamicResolution.cs:64`, `src/Strata.Core/Presentation/GraphicsConfig.cs:48` | P1b-16 (W2) |
| Cave culling | ⬜ | master plan §8 | — |
| Horizon heightfield samples generator fields only: no caves, structures, edits or hardmode wedges | 🟡 | `src/Strata.Core/Presentation/HorizonField.cs` | — |

## 5. Terrain

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Generation pipeline: 20 passes, plan and chunk tiers, structures, ores, liquids, hardmode mutations | ✅ | `src/Strata.Core/WorldGen/`, `docs/architecture/worldgen.md` | — |
| Determinism: golden chunk hashes, `GenVersion` 9 | ✅ | `src/Strata.Core/WorldGen/GenVersion.cs:8`, `tests/Strata.Core.Tests/WorldGen/WorldGenTests.cs` | Bumped by P1b-02 (W1b), P4-15 (W8), P5-22 (W10) |
| Terrain metrics: SurfaceProfile, TerrainStats, `worldgen-report` terrain section and `--compare` | ✅ | `src/Strata.Core/WorldGen/Analysis/SurfaceProfile.cs`, `src/Strata.Core/WorldGen/Analysis/TerrainStats.cs` | P1b-01 (W0) |
| 3D overhang density: amplitude `1.5 + rug*11` cells, 3 octaves, y stretched 1.4×; floating blobs, fins and holes | ❌ | `src/Strata.Core/WorldGen/Fields/TerrainField.cs:33-42` | P1b-03 (W1b) |
| Mountains and tundra: ridged fractals (5 and 4 octaves) on a bilinear 8-cell grid; creases every 4 m | 🟡 | `src/Strata.Core/WorldGen/Passes/BiomeRelief.cs:16-20,57-58`, `src/Strata.Core/WorldGen/Plan/WorldPlan.cs:14`, `src/Strata.Core/WorldGen/Plan/Grid2D.cs:25-26` | P1b-02 (W1b) |
| Biome relief blends over a third of a region (`Membership*3`); walls between forest and tundra | 🟡 | `src/Strata.Core/WorldGen/Passes/BiomeRelief.cs:40` | P1b-02 (W1b) |
| Relief is code constants, not data | 🟡 | `src/Strata.Core/WorldGen/Passes/BiomeRelief.cs:15-20` | P1b-02 (W1b) |
| Caves use the 2D column height, not the true surface, so tunnels break through as pits | 🟡 | `src/Strata.Core/WorldGen/Fields/CaveField.cs:41`, `src/Strata.Core/WorldGen/Passes/CaveGenerationPass.cs:128` | P1b-03 (W1b) |
| Binary solid/air threshold, then Taubin smoothing with drift ≤ 0.45 cells; gentle slopes keep 0.25 m stair ripples | 🟡 | `src/Strata.Core/Meshing/SurfaceNetsMesher.cs:16-21` | P1b-04 (W1b) |
| Old worlds after a generator change: a plan-hash mismatch only logs a warning, edited chunks seam | 🟡 | `src/Strata.Core/Session/WorldSession.cs` | P1b-02 (W1b) (outdated-world warning in the menu) |

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

## 6. Performance

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| F3 overlay timers (`gd.mesh_uploads`, `gd.colliders`, `gd.objects`, `gd.grass`, `tick.*`) | ✅ | `game/UI/PerfOverlay.cs` | — |
| Spike log, GC counters, perf probes, a perf.cmds smoke baseline | ⬜ | `game/Testing/Scripts/` (no perf script yet) | P1b-05 (W0) |
| Autosave every 120 s runs inside the physics tick and rewrites whole region files under the store lock | ❌ | `src/Strata.Core/Session/GameWorld.cs:157-160`, `src/Strata.Core/Persistence/RegionFile.cs:42-69` | P1b-06, P1b-07 (W2) |
| Unload flushes rewrite regions on the main thread | ❌ | `src/Strata.Core/Streaming/ChunkStreamer.cs:99,118`, `src/Strata.Core/Session/WorldSession.cs:169` | P1b-07 (W2) |
| Streaming scheduler: LINQ `Where`/`OrderBy`/`ToList` over every entry, every frame | 🟡 | `src/Strata.Core/Streaming/ChunkStreamer.Scheduling.cs:42,66-67,101-102` | P1b-08 (W2) |
| Colliders: near-player builds skip the 2 ms budget; Jolt BVH built on the main thread; liquid- and light-only remeshes rebuild colliders | ❌ | `game/World/ColliderManager.cs:75-76,118` | P1b-09 (W2) |
| Tile objects rebuilt on `MeshReady` and again on `BodyBuilt`; 8 chunks per frame by count, not time | ❌ | `game/World/TileObjectRenderer.cs:46-47,64-68` | P1b-10 (W2) |
| Grass created and freed with no budget or distance hysteresis | 🟡 | `game/World/GrassRenderer.cs:112,140` | P1b-11 (W2) |
| Large-object-heap churn: cave scratch arrays per chunk, `ToArray` mesh uploads; gen2 GC pauses | 🟡 | `src/Strata.Core/WorldGen/Passes/CaveGenerationPass.cs:139`, `game/World/MeshUpload.cs:19-21` | P1b-12 (W2) |
| Creature, prop and animation GLBs loaded synchronously on first use | ❌ | `game/Rendering/CreatureVisual.cs:34`, `game/Entities/NpcRenderer.cs:86-94` | P1b-13, P1b-14 (W2) |
| Projectiles: a new material per projectile and an OmniLight3D per lit projectile | 🟡 | `game/Entities/ProjectileRenderer.cs:131,147` | P1b-15 (W2) |
| Dynamic-resolution oscillation | 🟡 | `game/Rendering/DynamicResolution.cs:64` | P1b-16 (W2) |
| Liquid steps commit a remesh and collider rebuild even when no solid changed | 🟡 | `src/Strata.Core/Simulation/Liquids/LiquidSimulator.cs` | P1b-09 (W2) |
| Housing room scans repeated per check | 🟡 | `src/Strata.Core/Gameplay/Housing/HousingScanner.cs` | P5-02 (W10) |
| Measured on the player's GPU | ⬜ | master plan §7 | — (lavapipe only) |

## 7. Player movement, camera and targeting

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Controller: step-up, variable jump, coyote time, swimming, breath, fall damage, knockback, i-frame blink | ✅ | `game/Player/PlayerController.cs`, `data/config/movement.json` | — |
| Jump about half as high as the reference game relative to the body (apex 2.4 m = 1.35 bodies); auto-step clears 0.5 m, not a 1 m block | 🟡 | `data/config/movement.json` | P3-46 (W6) |
| Hermes Boots are an instant +45 %; Shift sprint speeds everyone up | 🟡 | `data/items/mobility.json`, `data/config/movement.json` | P3-46 (W6) |
| Aim ray starts at the camera and accepts hits behind the player | ❌ | `game/Player/InteractionController.cs:101-110` | P1-05 (W1a), P1-06 (W1b) |
| Targeting hits decor, grass and tree boxes that the camera probe and colliders ignore | ❌ | `src/Strata.Core/Gameplay/Interaction/Targeting.cs:28,39`, `game/World/ObjectColliders.cs:95` | P1-05 (W1a) |
| Body turns to the aim point only past 55°; close targets pull the yaw right through the 0.55 m shoulder offset | ❌ | `game/Player/PlayerAvatar.Motion.cs:84`, `game/Player/CameraRig.cs:17` | P1-06 (W1b) (body always faces the crosshair, strafe clips) |
| Talking is blocked by anything the ray hits first | 🟡 | `game/Player/InteractionController.Talk.cs:29` | P1-06 (W1b) |
| Cursor reach ignores the Reach stat (Builder), so the highlight dims where use still works | ❌ | `game/Player/InteractionController.cs:101`, `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs:94` | P1-05 (W1a) |
| Crosshair names: blocks and objects only | 🟡 | `game/UI/Hud.cs:211` | P1-07 (W1b) |
| Smart Dig (look down with a pickaxe) | ✅ | `game/Player/InteractionController.Talk.cs:15` | — |
| Smart Cursor and auto-select | ⬜ | `data/config/input.json` | P2-29 (W4) |
| Opening the inventory freezes the player (no moving, hotbar keys or use) while the world keeps running; auto-pause is off by default | ❌ | `game/UI/InputGate.cs:13`, `game/Player/PlayerController.cs:141`, `game/Settings/UserSettings.cs:27` | P2-11 (W4) |
| Mouse-wheel hotbar is hard-coded; the rebindable `hotbar_next`/`hotbar_prev` do nothing | ❌ | `game/Player/InteractionController.cs:139-140` | P2-11 (W4) |
| First person: no hurt visual (the avatar is hidden) | ⬜ | `game/Player/PlayerAvatar.cs:47` | P3-25 (W6) |

## 8. Items and weapons

All 667 items load. Every behaviour type is registered and every projectile and ammo class resolves. The failures
below are in behaviour and presentation, not in data.

| Category | Status | Evidence | Fix |
|---|---|---|---|
| Use-style vocabulary: 7 styles (`hold` unused); crowns, crystals, mirrors and crates "drink"; thrown items and boomerangs play the gun kick | ❌ | `src/Strata.Core/Content/Defs/ItemDef.cs:15-18`, `game/Player/PlayerAvatar.Motion.cs:124-133` | P3-02, P3-03 (W4) |
| Use timing: the animation restarts mid-swing; speed bonuses don't scale `useAnimation` | ❌ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs:97,110,158,161-162` | P1-10 (W1a), P3-01' (W5) |
| Tools (86 pickaxes, axes, hammers and drills) | 🟡 | Mining works. Swings restart, and stone and ores take 1.7–2× the reference hits (`src/Strata.Core/Gameplay/Mining/MiningRules.cs:11`). | P1-10 (W1a), P3-24 (W5) |
| Swords (41) | 🟡 | Hitbox and trail sweep flat and linear from tick 0 while the arm winds up (`src/Strata.Core/Gameplay/Entities/MeleeSystem.cs`, `game/Entities/SwingTrail.cs`) | P1-10 (W1a), P1-11 (W1b), P3-04 (W5) |
| Swords that also shoot (Starfury, Terra Blade, Zenith and 12 more) | ✅ | The 14 swords swing with every shot; Zenith is `noMelee` with `maxActive: 0` and never blocks (`ItemUseSystem.cs:155-163`, `ItemUseSystem.Shooting.cs:27-29`; `ArsenalTests`, `combat.cmds`) | P1-16 (W1a) |
| Spears and shortswords (22) | 🟡 | Thrust works, but the model never reaches the 1.6–2.1 m hitbox (`src/Strata.Core/Gameplay/Entities/MeleeSystem.cs`) | P3-04 (W5) |
| Bows (23) | ❌ | Arrow leaves before the draw; all metal bows are one mesh; Daedalus Stormbow fires from the eye | P1-13 (W1a), P1-14 (W1b), P3-39 (W2), P3-09' (W5) |
| Guns (17) | 🟡 | Fire and recoil work. Missing: muzzle flash, real bursts, ammo count on the slot, Megashark/Minishark ammo saving (`ItemUseSystem.Shooting.cs:18-23`). Icons render edge-on. | P3-38 (W2), P2-06 (W3), P3-01', P3-09', P3-10 (W5) |
| Magic (38) | 🟡 | Works (mana, mana sickness). Bolts are tiny spheres, trails are never drawn, magic guns use the cast pose. | P3-02 (W4), P3-11 (W5) |
| Thrown (shuriken, knives, bombs, grenades) | 🟡 | Work, but play the gun pose; shuriken don't spin (`game/Entities/ProjectileRenderer.cs:85`) | P3-02, P3-03 (W4), P3-11 (W5) |
| Boomerangs | 🟡 | Flight, return and one-at-a-time work; the pose is wrong and they tumble end-over-end | P3-02 (W4), P3-11 (W5) |
| Yoyos (5) | ❌ | Stand-in: fire-and-return projectile, no string, no cursor control (`src/Strata.Core/Gameplay/Entities/ProjectileSystem.Special.cs:29-39`) | P3-12 (W5) |
| Flails (5) | ❌ | Stand-in: the whole flail model flies and tumbles, no chain | P3-13 (W5) |
| Whips (2) | ❌ | Wide melee arcs; "summons focus struck enemies" and Snapthorn's poison exist only in the descriptions (`data/items/arsenal.json`) | P3-14 (W5), P3-15 (W5) |
| Summons (15) | 🟡 | Minions and sentries work; they spawn at the eye, with no minion buff or count in the HUD | P3-10 (W5), P2-14 (W4) |
| Grappling hooks (9) | ❌ | Click does nothing; the hook drops on natural terrain; one generic chain and claw | P1-08 (W1a), P1-09 (W1b), P3-17' (W5) |
| Potions and consumables | 🟡 | Healing, mana, buffs and crystals work. Every use is "drink". 19 of 21 buff potions don't say what they do or for how long. Stack sizes mix versions. Life Fruit works before 400 max life (`src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Consume.cs:14`). | P2-04 (W3), P3-02 (W4), P3-33 (W6) |
| Quick heal, quick mana, quick buff keys | ⬜ | `data/config/input.json` | P3-33 (W6) |
| Accessories (66) | ✅ | Stat, flight, dash and info effects; set bonuses (`src/Strata.Core/Content/Specs/EffectSpecs.cs`) | 7 slots: P2-05 (W3) |
| Armour (98 pieces) | 🟡 | Stats and set bonuses work; never drawn on the character | P3-41, P3-42 (W7) |
| Wings (14) | ✅ | Flight works and wings are drawn | — |
| Mounts | 🟡 | Minecart only (`src/Strata.Core/Gameplay/Mounts/Minecart.cs`) | Slot: P2-05 (W3); more mounts out of scope (§17) |
| Pets and light pets | ⬜ | `Pet` category exists, zero items (`src/Strata.Core/Content/Defs/ItemDef.cs:3-8`) | P3-18 (W5) |
| Vanity and dyes | ⬜ | Categories exist, zero items | Slots: P2-05 (W3), P2-16 (W4); dyes: P5-16 (W10) |
| Prefixes: 18 with invented names; rolled only on crafted items | 🟡 | `data/combat.json`, `src/Strata.Core/Gameplay/Crafting/RecipeBook.cs:56`, `src/Strata.Core/Gameplay/Loot/LootRoller.cs:57,80`, `src/Strata.Core/Gameplay/Town/TownCommerce.cs:117` | P3-31 (W6) |
| Rarity colours: tiers 9 and 10 (52 items) show as tier-8 yellow; no Expert/Master/Quest tiers | ❌ | `game/UI/UiTheme.cs:51` | P2-01 (W3) |
| Coins never auto-convert; no coin slots | ⬜ | `src/Strata.Core/Gameplay/Items/Coins.cs`, `src/Strata.Core/Gameplay/Items/PlayerInventory.cs` | P2-05, P2-06 (W3) |
| Ammo: searched hotbar-first; no ammo slots | 🟡 | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Shooting.cs:44-46` | P2-06 (W3) |
| Per-item use sounds (`ItemDef.SoundSet` has no readers) | ⬜ | `src/Strata.Core/Content/Defs/ItemDef.cs:118` | P3-27 (W6) |
| Autoswing: every melee weapon repeats, so `autoReuse` is dead data for melee | ✅ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs:89` | Whip and Feral Claws rule: P3-15 (W5) |
| Failed uses (no ammo, no mana, blocked) give no feedback | ❌ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs:160` | P1-15 (W1a) |
| Shots spawn at the eye, not the weapon | ❌ | `src/Strata.Core/Gameplay/Entities/ProjectileSystem.cs:89` | P1-13 (W1a), P3-05 (W2), P3-09' (W5) |
| Held-item grip: one +90° rotation for everything; first-person guns point skyward | 🟡 | `game/Player/PlayerAvatar.cs:90-102`, `game/Player/ViewModel.cs:69-72` | P3-05 (W2), P3-06, P3-07 (W5) |
| Held blocks: a flat unlit icon card | 🟡 | `game/Player/PlayerAvatar.cs:96-102`, `game/Player/ViewModel.cs:98-101` | P3-06 (W5) |
| Dropped items: flat billboards, no spin, faint rarity glow, no light | 🟡 | `game/Items/DroppedItemRenderer.cs:50-56` | P3-36 (W6) |
| The item magnet pulls items in when the inventory is full (they orbit the player) | ❌ | `src/Strata.Core/Gameplay/Items/DroppedItems.cs:69` | P3-29 (W6) |
| Treasure bags | ⬜ | Expert loot drops loose (`data/loot/enemies.json`) | P4-04 (W7), P4-25 (W9) |

## 9. UI

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Theme: dark slate (0.07, 0.09, 0.14) instead of translucent royal blue; covers only 4 control types; attached per root | 🟡 | `game/UI/UiTheme.cs:8-16,54-71` | P2-01 (W3) |
| UI font: none in the repo; Godot's fallback font everywhere | ⬜ | `project.godot`, `game/UI/UiTheme.cs` | P2-01 (W3) |
| UI sprites (hearts, stars, bubbles, slot silhouettes, cursor, frames) | ⬜ | `assets/` (no UI sprites) | P2-02 (W3) |
| **HUD**: life and mana are 260 px progress bars, not hearts (golden past 400) and stars | 🟡 | `game/UI/Hud.cs:68-77` | P2-13 (W4) |
| Breath is a third bar that overlaps the minimap | 🟡 | `game/UI/Hud.cs:75-76` | P2-13 (W4) |
| Hotbar: bottom-centre, no selected-slot scale, name fades, shown twice with the inventory open | 🟡 | `game/UI/Hud.cs:48-62,171-189` | P2-12 (W4) |
| Buffs: text chips with dead tooltips; no icons or right-click cancel; overlaps the FPS label | 🟡 | `game/UI/Hud.cs:249-267`, `src/Strata.Core/Content/Defs/BuffDef.cs` | P2-03 (W3), P2-14 (W4) |
| Boss bar: plain bar, no head icon, life numbers discarded | 🟡 | `game/UI/Hud.cs:128` | P2-24 (W2), P2-23 (W4), P4-03 (W7) |
| Invasion and event progress bar | ⬜ | `src/Strata.Core/Gameplay/Events/EventDirector.cs` (kills are tracked) | P2-23 (W4) |
| Status messages: big centred banners, non-Terraria colours, no chat log | 🟡 | `game/UI/Hud.cs:133-160` | P2-21 (W4) |
| Pickup feedback: corner toasts ("+1 Dirt Block" ×30) instead of merged popups | 🟡 | `game/UI/Hud.cs:161-164,269-279` | P2-22 (W4) |
| Floating combat text: no pop, no heal or mana numbers, non-Terraria colours | 🟡 | `game/Entities/CombatEffects.cs:38-72` | P2-19 (W3) |
| Enemy health: red "life/max" text instead of a small bar | 🟡 | `game/Entities/NpcRenderer.cs:145-150` | P2-20 (W3) |
| Death screen: plain red text, no dim, no cause | 🟡 | `game/UI/Hud.cs:114-118,213-217` | P2-25 (W4) |
| Custom cursor; held item at the pointer on every screen | ⬜ | `game/UI/Hud.cs:321-344`, `game/UI/InventoryScreen.cs:146-156` | P2-10 (W4) |
| Info accessories readout overlaps the F3 overlay | 🟡 | `game/UI/Hud.cs:108-112` | P2-09 (W3), P2-12 (W4) |
| Layout: absolute pixels; the crafting panel covers the minimap below 1340 logical px | ❌ | `game/UI/InventoryScreen.cs:45-93`, `game/UI/CraftingPanel.cs:23-24` | P2-09 (W3), P2-15 (W4) |
| **Inventory**: 50-slot backpack, Loot all / Deposit all, split and merge | ✅ | `src/Strata.Core/Gameplay/Items/SlotActions.cs` | — |
| Right-click follows Minecraft rules: takes half or places one; can't equip or open crates; no hold to take more | ❌ | `src/Strata.Core/Gameplay/Items/SlotActions.cs:46-47` | P2-07 (W3) |
| Trash slot lives in the UI only (lost on exit); shift-click silently trashes | 🟡 | `game/UI/InventoryScreen.cs:29,206` | P2-05, P2-07 (W3), P2-15 (W4) |
| Coin and ammo slots; coins as a text label | ⬜ | `game/UI/InventoryScreen.cs:145` | P2-06 (W3), P2-15 (W4) |
| Sort, quick stack, restock, rename, favourites, quick stack to nearby chests | ⬜ | `game/UI/InventoryScreen.cs:216-230` | P2-08 (W3), P2-17 (W4) |
| Throw the cursor item by clicking outside the inventory | ⬜ | `game/UI/InventoryScreen.cs:126-135` | P2-11 (W4) |
| Equipment: 3 armour and 5 accessory slots, no silhouettes, text defense; no vanity, dye or misc slots | 🟡 | `src/Strata.Core/Gameplay/Items/PlayerInventory.cs`, `game/UI/InventoryScreen.cs:52-74` | P2-05 (W3), P2-16 (W4) |
| **Crafting**: rebuilds every 0.5 s, destroying its own tooltips (the only place ingredients show) | ❌ | `game/UI/CraftingPanel.cs:41-51` | P2-18 (W4) |
| Crafting shows raw station ids (`hardmode_anvil`, `alchemy`) | ❌ | `game/UI/CraftingPanel.cs:49,90`, `game/UI/TownDialog.cs:186` | P2-04 (W3), P2-18 (W4) |
| Craft to cursor, material slots, hold-to-craft | ⬜ | `game/UI/CraftingPanel.cs:72-82` | P2-18 (W4) |
| **Chests**: open, Loot all, Deposit all; chest lid stays shut | 🟡 | `game/UI/InventoryScreen.cs:76-89`, `game/World/ObjectPlacement.cs:26-30` | P2-17 (W4), P3-48 (W6) |
| Piggy Bank and Safe | ⬜ | no `piggy` anywhere in `data/` | P3-35 (W6) |
| **Tooltips**: speed and knockback words match the reference game | ✅ | `src/Strata.Core/Gameplay/Items/ItemTooltip.cs` | — |
| Tooltips ignore prefix stats and value; no green/red modifier lines; sell price disagrees with the shop | ❌ | `src/Strata.Core/Gameplay/Items/ItemTooltip.cs:20-43` | P2-04 (W3), P3-31 (W6) |
| Tooltips on Labels (shop rows, buff row) can never appear; default 0.5 s delay; not following the mouse | ❌ | `game/UI/TownDialog.cs:148`, `game/UI/Hud.cs:265` | P2-10 (W4) |
| **NPC chat and shop**: bottom-anchored box spills off-screen; framed buttons | 🟡 | `game/UI/TownDialog.cs:37-38` | P2-26 (W4) |
| Shop: a list, sell held stack only, no buyback | 🟡 | `game/UI/TownDialog.cs:138-166` | P2-27 (W4) |
| Reforge: acts on the held item, text reply, no slot | 🟡 | `game/UI/TownDialog.cs:197-208` | P2-28 (W4) |
| Housing menu, query, banners | ⬜ | `src/Strata.Core/Commands/TownCommands.cs` (console only) | P5-05, P5-29 (W10) |
| **Map**: full map and minimap with markers, per player per world | ✅ | `game/UI/WorldMapScreen.cs`, `game/UI/MapView.cs` | — |
| Map framing, zoom buttons, NPC head icons | 🟡 | `game/UI/WorldMapScreen.cs:121-158`, `game/UI/MapView.cs:81-101` | P2-12 (W4), P5-28 (W10) |
| **Menus**: parallax backdrop, hover-gold buttons, sounds; text logo; unthemed settings widgets | 🟡 | `game/Menu/MenuKit.cs`, `game/Menu/SettingsScreen.cs` | P2-30 (W4) |
| Item slot rendering: 72 % icon inset, count styling, no grab sound | 🟡 | `game/UI/ItemSlotView.cs:40-89` | P2-10 (W4) |

## 10. Game feel and audio

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Music director: boss > event > Blood Moon > biome > layer; 31 tracks, 2 s crossfade | ✅ | `game/Audio/MusicDirector.cs` | — |
| Music gaps: rain, town, underground-biome and night variants, boss-defeat fade | 🟡 | `game/Audio/MusicDirector.cs:78-104` | P3-49 (W6) |
| Ambient bus plays nothing; fishing bite is silent | ⬜ | `game/Audio/Sfx.cs:105` | P3-49 (W6) |
| Per-material dig, break and footstep sounds; positional SFX pool | ✅ | `game/Audio/SoundEvents.cs`, `game/Player/PlayerController.Sound.cs` | — |
| One generic hit sound for every creature; `EnemyDef.HitSound`/`DeathSound` and `hit_squish` unused; damage-over-time ticks play hit sounds | 🟡 | `game/Audio/SoundEvents.cs:42`, `src/Strata.Core/Content/Defs/EnemyDef.cs:84-85`, `src/Strata.Core/Gameplay/Entities/NpcSystem.cs:156` | P3-19 (W5) |
| Silent: water entry, projectile impacts, hotbar switch, tool whoosh; one `place` sound; SFX pool drops sounds silently | 🟡 | `game/Audio/SoundEvents.cs`, `game/Player/PlayerController.cs:131`, `game/Audio/Sfx.cs` | P3-11 (W5), P3-26, P3-27 (W6) |
| Hit dust, blood and gel; death is a white unshaded puff; no gore | 🟡 | `game/Entities/CombatEffects.cs:16-17,74-98` | P3-19, P3-20 (W5) |
| Chopping trees and hitting objects: silent, no chips, no progress; trees vanish; everything breaks with `break_wood` | ⬜ | `src/Strata.Core/Gameplay/Interaction/WorldEditor.Objects.cs`, `game/Audio/SoundEvents.cs:30` | P3-21, P3-22 (W5) |
| Crack overlay only on the block under the cursor | 🟡 | `game/Player/TargetHighlight.cs:52,83-98` | P3-24 (W5) |
| Swords and projectiles can't break pots or cut grass | ⬜ | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.cs:107` (tool path only) | P3-23 (W5) |
| Hurt feedback: player hurt sound, red number, i-frames, flinch | ✅ | `src/Strata.Core/Gameplay/Entities/NpcSystem.cs:26,257-270` | — |
| Fall and debuff damage are silent (no number); no first-person hurt cue; weak knockback both ways | 🟡 | `game/Player/PlayerController.Fall.cs:8,29`, `src/Strata.Core/Session/GameWorld.cs:193`, `src/Strata.Core/Gameplay/Entities/NpcSystem.cs:260`, `src/Strata.Core/Gameplay/Entities/NpcSystem.Knockback.cs:19` | P3-25 (W6) |
| Screen shake only on explosions; no boss or heavy-hit punch | 🟡 | `game/Player/CameraShake.cs`, `game/Entities/CombatEffects.cs:35` | P3-25 (W6), P4-03 (W7) |
| Enemy hit flash and knockback | ✅ | `src/Strata.Core/Gameplay/Entities/NpcSystem.cs:281`, `game/Entities/NpcRenderer.cs:141-143` | Squash and pop: P3-25 (W6) |
| Jump, land and run dust; water and lava splashes | ⬜ | `game/Player/PlayerController.Sound.cs` (sound only) | P3-26 (W6) |
| Block hit and break dust: single colour, not voxel-lit | 🟡 | `game/Entities/BlockEffects.cs:23-50` | P3-08 (W5), P3-24 (W5) |
| Hearts and mana stars from kills | ⬜ | `src/Strata.Core/Gameplay/Entities/NpcSystem.Death.cs:14` | P3-29 (W6) |
| Hold-to-mine, placement rate, footsteps | ✅ | `game/Player/InteractionController.cs`, `src/Strata.Core/Content/Loading/DerivedContent.cs` | — |

## 11. Art and models

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Blender pipeline: deterministic generators, icons, synthesised textures | ✅ | `tools/blender/build_all.py`, `tools/blender/bgen/` | — |
| Rebuilt bosses read as the reference game's (King Slime, Eye, Brain, Twins, Skeletron, Wall of Flesh, Deerclops; 7–18k triangles) | ✅ | `tools/blender/bgen/bosses_pre.py`, `tools/blender/bgen/detail.py` | — |
| Humans: 97 procedural characters with faces, hands, clothing, 52-bone rig, shared animation sets | ✅ | `tools/blender/bgen/human/` | Themed humanoids: P3-50 (W7) |
| Every weapon and tool class is one recoloured mesh (all 42 models from the sword generator are the same 88-triangle blade) | ❌ | `tools/blender/bgen/items.py:16-78` | P3-39, P3-40 (W2) |
| Metal colours muddy: the tint multiplies a grey iron texture (copper, palladium and hellstone all brown) | ❌ | `tools/blender/build_all.py`, `tools/textures/props.json` | P3-37 (W2) |
| Icons: soft renders with no outline; weapons cover 2–8 % of the frame; guns render edge-on | ❌ | `tools/blender/bgen/export.py`, `tools/blender/bgen/gear.py:70-90` | P3-38 (W2) |
| Items using an unrelated archetype (herbs are quills; the wizard hat is a metal dome; 14 rings) | 🟡 | `tools/blender/bgen/items.py:168-242` | P3-38, P3-40 (W2) |
| Held-item sockets (grip, muzzle, tip, string, chain) | ⬜ | `tools/blender/bgen/export.py` | P3-05 (W2) |
| Worn armour | ⬜ | `game/Player/PlayerAvatar.Gear.cs` (wings and boot flames only) | P3-41, P3-42 (W7) |
| Wrong-archetype creatures: wolf and Headless Horseman are horned unicorns, Black Recluse is a crab, Mimic is a gift box, Flying Snake is a vulture, Bone Serpent is the Eater head | ❌ | `tools/blender/bgen/beasts.py`, `tools/blender/bgen/fauna.py` | P3-43, P3-44 (W7) |
| Non-human themed enemies are recoloured humans (mummy, granite golem, Tim); four hat kinds | 🟡 | `tools/blender/bgen/human/accessories.py:28-60` | P3-50 (W7) |
| The most-seen enemies are the lowest fidelity (slimes 420 triangles, demon eyes 550, bats 192) | 🟡 | `tools/blender/bgen/creatures.py` | P3-45 (W7) |
| Event and late bosses are primitive assemblies with no rig (Golem 928 triangles, Pumpking, Betsy, Martian Saucer, pillars) | ❌ | `tools/blender/bgen/festive.py`, `tools/blender/bgen/bosses_lunar.py` | P4-29 (W7), P4-35, P4-36, P4-37 (W9) |
| Fur, feathers and chitin use the woven cloth texture; no bone, scale or fur layers | 🟡 | `tools/textures/props.json` | P3-43 (W7) |
| Furniture and stations are unbevelled box assemblies | 🟡 | `tools/blender/bgen/furniture.py` | P4-38 (W9) |
| Boss head icons, NPC head portraits, bestiary thumbnails | ⬜ | `assets/icons/` (items only) | P2-24 (W2), P5-28 (W10) |

## 12. Bosses

Shared gaps:
* Boss parts share one life pool. The `Parts` mechanism exists (`src/Strata.Core/Gameplay/Entities/NpcSystem.Parts.cs:29`,
  `src/Strata.Core/Content/Defs/BossInfo.cs:78`), but only Moon Lord uses it.
* There is no Expert-only behaviour: difficulty only scales life and damage
  (`src/Strata.Core/Gameplay/Combat/DamageModel.cs:26-31`).
* Expert-gated drops use `difficulty min 1`, which Journey (3) also passes
  (`src/Strata.Core/Conditions/ConditionContext.cs:52-53`, `data/loot/enemies.json:9-12`).
* There are no treasure bags, no trophies and no natural boss arrivals.

Planned framework: P4-01 (parts, Expert context, enrage), P4-02 (pattern upgrades), P4-03 (presentation), P4-04 and
P4-25 (bags, trophies) and P4-05 (natural arrivals), all in W7 except P4-25 (W9).

| Boss | Status | What works / what's missing (evidence) | Fix |
|---|---|---|---|
| King Slime | 🟡 | Hand-written brain: charged hops, teleport, sheds slimes (`src/Strata.Core/AI/BossBrains.cs:13-90`). Missing: Expert spiked slimes; Slime Rain never ends in King Slime (`data/events.json:13`). | P4-05, P4-06 (W7) |
| Eye of Cthulhu | 🟡 | Hand-written brain: circles, dash bursts, servants, phase 2 at 50 % (`src/Strata.Core/AI/BossBrains.cs:97-157`). Missing: Expert rapid dashes, the natural "evil presence" spawn; the transformation spin is visual only. | P4-05, P4-06 (W7) |
| Eater of Worlds | ❌ | Tunnels through terrain with 18 segments (`src/Strata.Core/AI/WormBrain.cs`). Segments share one life, so it never splits; no Vile Spit (`src/Strata.Core/Gameplay/Entities/Npc.cs:132-140`). | P4-07 (W7) |
| Brain of Cthulhu | 🟡 | Invulnerable while Creepers live (`data/enemies/evil.json:106`). No illusions or confusion. | P4-08 (W7) |
| Queen Bee | 🟡 | Dashes, stingers, bee summons (`data/enemies/hive.json:54`). Tuning and enrage differ. | P4-09 (W7) |
| Deerclops | 🟡 | Walks, hops, ice volleys (`data/enemies/deerclops.json:40`). No shadow hands, insanity or debris. | P4-09 (W7) |
| Skeletron | ❌ | Old Man summon at night, hovering skull, swiping hands, spin every 540 ticks, day rage ×4 damage ×2.5 speed (`data/enemies/dungeon.json:28-35`, `src/Strata.Core/AI/DungeonBrains.cs:13-95`). Hands are not separate enemies; no Expert skull volleys; loot is only Book of Skulls, bones and potions (`data/enemies/dungeon.json:20`). | P4-10, P4-11 (W7) |
| Wall of Flesh | 🟡 | Sweeping barrier, eye lasers, tethered Hungry, effigy summon (`src/Strata.Core/AI/MawwallBrain.cs`). No Tongued pull, no Leeches; sweeps along X only. | P4-24 (W9) |
| Queen Slime | 🟡 | Hops and crystals; the winged phase swaps the model but still hops (`data/enemies/hallow_ocean.json:75`). | P4-26 (W9) |
| The Twins | ✅ | Paired eyes, phase-2 lasers and flamethrower, one defeat flag (`data/enemies/mechs.json:40`) | Strength tuning: P4-27 (W9) |
| The Destroyer | ❌ | 50 segments, probes, segment lasers (`data/enemies/mechs.json:61`). Shared life; segments can't be destroyed. | P4-27 (W9) |
| Skeletron Prime | ❌ | Four tools, cannon and laser fire (`data/enemies/mechs.json:79`). Arms can't be broken. | P4-28 (W9) |
| Plantera | 🟡 | Seed and thorn volleys, phase swap (`data/enemies/temple.json:134`). No hooks or tentacles; no enrage outside the jungle. | P4-29 (W7), P4-30 (W9) |
| Golem | ❌ | Fireballs and lasers on a ground pattern (`data/enemies/temple.json:148`). No separate head or fists; primitive model. | P4-29 (W7), P4-31 (W9) |
| Duke Fishron | 🟡 | Dash, bubbles, teleport (`data/enemies/hallow_ocean.json:90`). No Sharknadoes, no phase 3, no ocean enrage. | P4-32 (W9) |
| Empress of Light | ❌ | Bolts and lances (`data/enemies/hallow_ocean.json:108`). Leaves at day instead of turning lethal. | P4-32 (W9) |
| Lunatic Cultist, Celestial Pillars | 🟡 | Ritual, pillar shields and kill goals (`data/enemies/lunar.json:64`); primitive pillar models | Models: P4-37 (W9); bags: P4-25 (W9) |
| Moon Lord | 🟡 | Hands and head are separate parts (`data/enemies/lunar.json:173`). No True Eye phase. | — (bags: P4-25) |
| Pumpkin and Frost Moon bosses (Mourning Wood, Pumpking, Everscream, Santa-NK1, Ice Queen) | 🟡 | Pattern loops (`data/enemies/moons.json:77-90`); primitive models | P4-35 (W9) |
| Betsy, Mothron, Martian Saucer, Flying Dutchman, Dark Mage, Ogre | 🟡 | Pattern loops (`data/enemies/invasions.json:64-81`, `data/enemies/moons.json:71`); primitive models | P4-36, P4-37 (W9) |
| Boss bar, announcements, music | 🟡 | Plain bar and banners; boss music by priority | P2-23 (W4), P4-03 (W7) |

## 13. Dungeon

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Zone, brick colour per world, gatehouse, descent shaft, locked bricks until Skeletron (pick power 65) | ✅ | `src/Strata.Core/WorldGen/Structures/DungeonBuilder.cs`, `src/Strata.Core/Gameplay/Interaction/WorldEditor.cs:197` | — |
| Old Man at the gate, curse, Skeletron summon, Clothier after the defeat | ✅ | `data/town.json:99`, `src/Strata.Core/Gameplay/Town/TownSystem.Landmarks.cs` | — |
| Dungeon Guardian: invulnerable, 1000 damage, leaves when you exit | ✅ | `data/enemies/dungeon.json:38` | — |
| Enemies: Angry Bones, Dark Caster, Cursed Skull, Dungeon Slime; Paladin, Necromancer, Skeleton Sniper, Dungeon Spirit after Plantera | ✅ | `data/enemies/dungeon.json`, `data/enemies/bestiary.json` | Variants: P4-20 (W8) |
| Layout: a 3D grid of boxy rooms joined by straight corridors and wells, not long winding sloped halls | 🟡 | `src/Strata.Core/WorldGen/Structures/DungeonBuilder.cs:33-110` | P4-12, P4-14, P4-15, P4-16 (W8) |
| Wall variety (brick, slab, tiled panels) | ⬜ | `data/blocks/built.json` | P4-13 (W8) |
| Furnishing: only a candle, pots, cobwebs and a 30 % unlocked gold chest | 🟡 | `src/Strata.Core/WorldGen/Structures/DungeonBuilder.cs:112-125` | P4-17, P4-18, P4-19 (W8) |
| Spikes, bookcases with books (Water Bolt), chandeliers, banners, statues, dart traps | ⬜ | `data/tileobjects/` | P4-18, P4-19 (W8) |
| Chest loot: dungeon chests roll the cave-cabin table, with dungeon items weighted in by biome | ❌ | `src/Strata.Core/WorldGen/Structures/DungeonBuilder.cs:116`, `data/loot/containers.json:22-36` | P4-20, P4-23 (W8) |
| Locked chests, Golden Key, Shadow Key, Shadow Chests | ⬜ | `src/Strata.Core/Gameplay/Interaction/ObjectInteraction.cs:69-72` (chests ignore keys) | P4-20, P4-21 (W8) |
| Dungeon items: Magic Missile, Aqua Scepter, Valor, Bone Welder, Clothier Voodoo Doll | ⬜ | `data/items/` | P4-20 (W8) |
| Biome chests and keys in the vault | ⬜ | — | P4-33, P4-34 (W8) |
| Bound Mechanic | ⬜ | — | P5-06b (W10) |
| Smoke route from the entrance to the vault | 🟡 | `game/Testing/Scripts/dungeon.cmds` (gate, guardian, summon only) | P4-23 (W8) |

## 14. Town NPCs

Shared rules:
* Housing: enclosed flood fill, floor and headroom minimums, door, light, table and chair. A room counts when at least
  50 % of its boundary is player-built, which stands in for background walls. Homes are rechecked every 900 ticks
  (`src/Strata.Core/Gameplay/Housing/HousingScanner.cs`). Houses in an evil biome are not rejected.
* Residents walk home at night through doors, take hits, and are announced when they arrive and die
  (`src/Strata.Core/Gameplay/Town/TownSystem.Movement.cs`, `src/Strata.Core/Gameplay/Town/TownSystem.Combat.cs`).

| Rule | Status | Evidence | Fix |
|---|---|---|---|
| Move-ins checked every 600 ticks at any hour (the reference game: by day only) | 🟡 | `src/Strata.Core/Gameplay/Town/TownSystem.cs:67`, `data/config/town.json` | P5-01 (W10) |
| Resident count includes dead residents and the Old Man | ❌ | `src/Strata.Core/Gameplay/Town/TownSystem.cs:141` | P5-01 (W10) |
| Respawn after a flat 21600 ticks at any time, not at dawn | 🟡 | `data/config/town.json` (`respawnTicks`) | P5-01 (W10) |
| Housing: evil biomes, Terraria query messages, cached scans | 🟡 | `src/Strata.Core/Gameplay/Housing/HousingScanner.cs` | P5-02 (W10) |
| Room assignment by chair order; no assign, swap or evict | ⬜ | `src/Strata.Core/Gameplay/Town/TownSystem.cs:74-133` | P5-05, P5-29 (W10) |
| Missing condition types: rescue, has dye, bestiary %, season, angler quests | ⬜ | `src/Strata.Core/Conditions/ConditionCompiler.cs` | P5-03 (W10) |
| Happiness: biome and neighbour love/hate, crowding, homeless +10 %, prices 0.75–1.5× | 🟡 | `src/Strata.Core/Gameplay/Town/TownCommerce.cs:78-99`. Missing: solitude, the Princess, happiness dialogue. | P5-12 (W10) |
| One service per resident | 🟡 | `src/Strata.Core/Content/Defs/TownNpcDef.cs:71` | P5-13 (W10) |
| Residents fight back; enemies target residents | ⬜ | `src/Strata.Core/AI/Brain.cs:14` (players only) | P5-09, P5-10 (W10) |
| Sit in chairs, sleep in beds | ⬜ | — | P5-07, P5-11 (W10) |
| Pylons | ⬜ | — | P5-34 (W10) |
| Town pets (cat, dog, bunny) | ⬜ | — | P5-20 (W10) |

Per resident: the move-in rule here compared with the reference game, then the shop or service. The link after each
name is the resident's definition.

| NPC | Move-in | Shop / service | Status | Fix |
|---|---|---|---|---|
| Guide (`data/town.json:6`) | Starter ✅ | Crafting help: one text line for the held item, raw station ids | 🟡 | P5-14 (W10), P5-27 (W11) |
| Merchant (`data/town.json:24`) | 50 silver ✅ (backpack only) | 13 items | ✅ | P5-15 (W10) |
| Nurse (`data/town.json:54`) | Max life ≥ 120 and Merchant ✅ | Heal and cure, flat 15 copper/HP; sells a potion (the reference Nurse doesn't) | 🟡 | P5-13 (W10) |
| Dryad (`data/town.json:72`) | King Slime, Eye or Eater (not any boss) 🟡 | 7 items; no world-status check | 🟡 | P5-04, P5-13, P5-15 (W10) |
| Old Man (`data/town.json:99`) | Landmark at the Dungeon gate ✅ | Skeletron summon | ✅ | — |
| Clothier (`data/town.json:115`) | Skeletron defeated ✅ | 3 items | 🟡 | P5-15 (W10) |
| Arms Dealer (`data/town_residents.json:5`) | Gun or bullets ✅ | 5 items | ✅ | P5-15 (W10) |
| Demolitionist (`data/town_residents.json:6`) | Explosive, no Merchant check 🟡 | 3 items | 🟡 | P5-04, P5-15 (W10) |
| Goblin Tinkerer (`data/town_residents.json:7`) | Goblin Army flag, no rescue 🟡 | 3 items; reforge on the held item | 🟡 | P5-06, P5-15 (W10), P2-28 (W4) |
| Witch Doctor (`data/town_residents.json:8`) | Queen Bee ✅ | 3 items; no imbuing | 🟡 | P5-15 (W10) |
| Wizard (`data/town_residents.json:9`) | Hardmode, no rescue 🟡 | 2 items | 🟡 | P5-06, P5-18 (W10) |
| Mechanic (`data/town_residents.json:10`) | Skeletron flag, not rescued in the Dungeon 🟡 | 7 items | 🟡 | P5-06b, P5-15 (W10) |
| Steampunker (`data/town_residents.json:11`) | Any mechanical boss ✅ | 1 item (no Clentaminator) | 🟡 | P5-18 (W10) |
| Cyborg (`data/town_residents.json:12`) | Plantera ✅ | 2 items | 🟡 | P5-18 (W10) |
| Truffle (`data/town_residents.json:13`) | Hardmode and a mushroom biome ✅ | 3 items | 🟡 | P5-18 (W10) |
| Dye Trader (`data/town_residents.json:14`) | 4 residents (should be dye or dye material) ❌ | none; no Strange Plant trade | ❌ | P5-04, P5-16, P5-22 (W10) |
| Painter (`data/town_residents.json:15`) | 8 residents ✅ | 19 items | ✅ | P5-16 (W10) |
| Stylist (`data/town_residents.json:16`) | Eye and 3 residents (should be a spider-nest rescue) ❌ | none; no haircuts | ❌ | P5-06, P5-21 (W10) |
| Zoologist (`data/town_residents.json:17`) | 4 residents (should be bestiary 10 %) ❌ | none | ❌ | P5-03, P5-04 (W10), P5-17 (W11) |
| Golfer (`data/town_residents.json:18`) | 5 residents (should be an underground-desert rescue) ❌ | none | ❌ | P5-06 (W10), P5-17 (W11) |
| Tavernkeep (`data/town_residents.json:19`) | Evil boss (should be a rescue after it) 🟡 | 1 item; no event button | 🟡 | P5-06, P5-16 (W10) |
| Party Girl (`data/town_residents.json:20`) | 14 residents ✅ | none | ❌ | P5-16 (W10) |
| Tax Collector (`data/town_residents.json:21`) | Hardmode (should be purifying the Tortured Soul) ❌ | Taxes ✅ | 🟡 | P5-06 (W10) |
| Pirate (`data/town_residents.json:22`) | Pirate Invasion ✅ | none | ❌ | P5-18 (W10) |
| Santa Claus (`data/town_residents.json:23`) | Frost Legion, no December check 🟡 | none | ❌ | P5-03, P5-18 (W10) |
| Princess (`data/town_residents.json:24`) | 20 residents (should be every other NPC) 🟡 | none | ❌ | P5-04, P5-18 (W10) |
| Angler (`data/town_residents.json:25`) | Owns a fishing pole (should be found asleep at the ocean) ❌ | Quests (`day % 9`); no counter, no rewards shop | 🟡 | P5-06, P5-23 (W10) |
| Travelling Merchant (`data/town_residents.json:26`) | A permanent resident (should visit at dawn) ❌ | Katana and Lucky Horseshoe, no rotation | ❌ | P5-19 (W10) |
| Skeleton Merchant (`data/town_residents.json:27`) | A permanent resident (should be underground) ❌ | 2 items | ❌ | P5-19 (W10) |

Items tagged `shop` that nobody sells: Leather Whip and Rally (P5-15, P5-17).

## 15. Progression and loops

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| Progression flags, auto-set cascade, announcements; hardmode mutations, spread, ore tiers | ✅ | `src/Strata.Core/Gameplay/Progression/ProgressionSystem.cs`, `data/progression.json` | — |
| Station chain: furnace, anvils, hellforge, hardmode anvils and forges, ancient manipulator | ✅ | `data/tileobjects/stations.json`, `data/items/lunar.json` | — |
| Events: 7 invasions, moons, eclipse, Blood Moon, Slime Rain, ambient events; kill goals | ✅ | `data/events.json`, `src/Strata.Core/Gameplay/Events/EventDirector.cs` | Progress bar: P2-23 (W4) |
| Plantera, the Jungle Temple, Golem and the lunar endgame | ✅ | `data/enemies/temple.json`, `data/enemies/lunar.json` | Fights: §12 |
| Life Crystals, Mana Crystals, mana sickness; Life Fruit also works before 400 max life | 🟡 | `src/Strata.Core/Gameplay/Interaction/ItemUseSystem.Consume.cs:14` | Life Fruit gating: P3-33 (W6) |
| Fishing and crates | ✅ | `src/Strata.Core/Gameplay/Entities/ProjectileSystem.Special.cs` | Bite sound: P3-49 (W6) |
| Paint: 16 paints and tools | ✅ | `data/items/paint.json` | — |
| Wiring: red wire, lever, switch, pressure plate, dart trap; no actuators, timers or teleporters | 🟡 | `src/Strata.Core/Gameplay/Wiring/WireSystem.cs` | Expansion out of scope (§17) |
| Liquids: flow, mixing, buckets; no pressure | 🟡 | `src/Strata.Core/Simulation/Liquids/LiquidSimulator.cs` | — |
| Fallen Stars fall at night (their own description says so) | ⬜ | no `fallen_star` in `src/` | P3-30 (W6) |
| Bed sleep skips time (`PreventsSleep` exists, nothing reads it) | ⬜ | `src/Strata.Core/Gameplay/Events/EventDirector.cs:75` | P3-34 (W6) |
| Natural boss arrivals (Eye, King Slime, mechanical warnings) | ⬜ | `data/events.json` | P4-05 (W7) |
| Buffs and debuffs: 39; missing Darkness, Silenced, Broken Armor, Weak, Venom, Shadowflame and others | 🟡 | `data/combat.json` and per-file buff lists | — |
| Hardmode variants of cavern and underworld creatures | ⬜ | `data/enemies/hardmode.json` (adds 12 new creatures, no variants of old ones) | — |
| Journey mode: listed as a difficulty only | 🟡 | `src/Strata.Core/World/WorldSettings.cs:19` | Powers out of scope (§17) |

## 16. Guide book, bestiary and achievements

| Feature | Status | Evidence | Fix |
|---|---|---|---|
| In-game help: the Guide's one-line help and item tooltips | 🟡 | `game/UI/TownDialog.cs:174-187` | P5-14 (W10), P5-27 (W11) |
| Item-source reverse index (lives in the validator only) | 🟡 | `src/Strata.Core/Content/Validation/ContentValidator.World.cs:73` | P5-24a (W3) |
| Drop-rate computation | ⬜ | `src/Strata.Core/Gameplay/Loot/LootRoller.cs` (rolls only) | P5-24b (W11) |
| Player journal: kills, seen, caught, met, obtained, crafted | ⬜ | — | P5-25 (W11) |
| Achievements and popups | ⬜ | no `achievement` in `src/`, `game/` or `data/` | P5-26 (W11) |
| Bestiary (`data/enemies/bestiary.json` is an enemy pack, not a feature) | ⬜ | — | P5-31 (W11) |
| Guide book: Items, Bestiary, Bosses and progression, NPCs, Tips and controls, Achievements tabs | ⬜ | — | P5-30a/b, P5-31, P5-32 (W11) |

## 17. Recorded out of scope

| Item | Status | Note |
|---|---|---|
| Mounts beyond the minecart | ⬜ | The data model gets a mount slot (P2-05); the Slimy Saddle would come first |
| Background walls | ⬜ | Voxel rooms already enclose space; the Dungeon uses wall panels (P4-13) |
| Journey mode powers, wiring expansion | ⬜ | — |
| Moon Lord's True Eye phase, event-boss behaviour beyond pattern upgrades | 🟡 | Not in the plan; models are rebuilt in W9 |
| Cave culling, liquid pressure, the missing buffs and debuffs, hardmode variants of old creatures, a horizon with caves and hardmode wedges, measuring on the player's GPU | ⬜ / 🟡 | Not in the plan yet (rows marked `—` in §4, §6 and §15); cave culling and the variants are left over from master plan §8 |

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
