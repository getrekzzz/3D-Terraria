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
│ UI (see ui.md): UiTheme, UiFonts, Hud and its layout widgets,     │
│     InventoryScreen (chest, shop, reforge, crafting, equipment),  │
│     TownDialog, TooltipFollower, CursorItemLayer, PauseMenu,      │
│     DebugConsole, PerfOverlay · Menu: MainMenu, SettingsScreen    │
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
* Completions drain on the main thread within a per-frame budget. `FrameBudgetPlanner` (Core) sets the budgets for
  jobs, mesh uploads, colliders, tile objects and grass each frame: generous while the player is frozen behind the
  loading screen (12/6/4/3/2 ms), about 8 ms in total during play, 5 ms for jobs on a big backlog, and scaled by 0.6
  after a slow frame. `WorldRuntime.Budgets` exposes them to presentation nodes.
* `ChunkStreamer` schedules from two incrementally maintained sets (unscheduled loads, mesh candidates) sorted with a
  static comparer (priority, then X, Y, Z); chunks blocked on missing neighbours wait for the next chunk to become
  Ready. Loaded and meshed counts are kept as counters, so a settled frame allocates nothing.
* Everything in `GameWorld.Tick` is main-thread only.

## Frame flow
1. `WorldRuntime._Process`: stream around the focus, drain jobs, upload meshes (time-budgeted), update
   colliders near the player, environment (sun/moon/fog).
2. `_PhysicsProcess`: `PlayerController` moves the body and syncs the Core `PlayerEntity`;
   `InteractionController` targets and uses items; `GameWorld.Tick()` advances clock, drops, players,
   liquids (flow + contact effects), events, spawner, creatures, town residents, melee, projectiles, autosave.
3. Presentation nodes (`TileObjectRenderer`, `NpcRenderer`, `DroppedItemRenderer`, HUD) read Core state and
   react to Core events (object added/removed, damage, kills, pickups).
4. `FrameProbe` processes last and, while profiling is on (F3 or `perf start`), closes the frame for the spike log
   (see rendering.md, *Measuring hitches*).
