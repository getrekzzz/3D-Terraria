# Architecture overview

```
┌────────────────────────── Godot (game/) ──────────────────────────┐
│ Bootstrap: GameRoot, WorldLauncher, input, command line           │
│ World: WorldRuntime, ChunkRenderer, GrassRenderer,                │
│        HorizonRenderer, ColliderManager, ObjectColliders,         │
│        TileObjectRenderer                                         │
│ Player: PlayerController, CameraRig, PlayerAvatar,                │
│         InteractionController, TargetHighlight                    │
│ Entities: NpcRenderer, TownRenderer, ProjectileRenderer, effects  │
│ UI: Hud, InventoryScreen, CraftingPanel, TownDialog, DebugConsole,│
│     PerfOverlay                                                   │
│ Rendering: PropLibrary, CharacterMaterials, CreatureVisual,       │
│        LightProbe, GraphicsSettings, shaders/ (see rendering.md)  │
│ Ambience: EnvironmentController, Atmosphere, SkyNoise             │
│ Testing: ScriptRunner (.cmds), ScreenshotCapture                  │
└──────────────────────────────┬────────────────────────────────────┘
                               │ reads state, feeds input, renders
┌──────────────────────────────▼────────────── Strata.Core (src/) ──┐
│ Session.GameWorld: streaming + jobs + clock + WorldEditor +       │
│   DroppedItemSystem + players + NpcSystem + MeleeSystem +         │
│   ProjectileSystem + SpawnDirector + ItemUseSystem + interaction +│
│   EventDirector + LiquidSimulator + LiquidEffects + TownSystem +  │
│   TownCommerce + HousingScanner + VoxelNavigator                  │
│ WorldSession: content, generator, VoxelWorld, sky map, saves      │
│ Content · Voxels · WorldGen · Lighting · Meshing · Streaming      │
│ Gameplay (items, crafting, combat, stats, buffs, mining, building)│
│ AI (brains) · Commands (registry, probes) · Persistence           │
└───────────────────────────────────────────────────────────────────┘
```

## Principles
* **Core is engine-agnostic.** Everything that decides *what happens* lives in `Strata.Core` and runs in unit
  tests and headless tools. Godot code presents state and turns input into Core calls.
* **Fixed 60 Hz simulation.** `WorldRuntime._PhysicsProcess` calls `GameWorld.Tick()`; rendering interpolates.
* **Data-driven.** Items, blocks, objects, recipes, enemies, bosses, events, liquids, spawn rules, buffs, loot,
  progression, movement and spawn tuning are JSON in `data/`. Code implements mechanics (`behaviors`, `brain`, `conditions`, effect
  specs), never content tables.
* **One command surface.** `CommandRegistry` serves the console, CI smoke scripts and screenshot automation.

## Threading
* Worker threads (`JobSystem`) generate/load chunks and build meshes + light. Chunk data is copy-on-write,
  so jobs read neighbours lock-free; results carry the chunk version and stale ones are dropped.
* Completions drain on the main thread within a per-frame budget (adaptive to the backlog).
* Everything in `GameWorld.Tick` is main-thread only.

## Frame flow
1. `WorldRuntime._Process`: stream around the focus, drain jobs, upload meshes (time-budgeted), update
   colliders near the player, environment (sun/moon/fog).
2. `_PhysicsProcess`: `PlayerController` moves the body and syncs the Core `PlayerEntity`;
   `InteractionController` targets and uses items; `GameWorld.Tick()` advances clock, drops, players,
   liquids (flow + contact effects), events, spawner, creatures, town residents, melee, projectiles, autosave.
3. Presentation nodes (`TileObjectRenderer`, `NpcRenderer`, `DroppedItemRenderer`, HUD) read Core state and
   react to Core events (object added/removed, damage, kills, pickups).
