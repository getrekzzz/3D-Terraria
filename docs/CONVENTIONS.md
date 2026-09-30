# Strata — Engineering Conventions

These rules keep a very large sandbox game maintainable. They are enforced by tests, validators and CI
wherever possible.

## Units & coordinates
| Concept | Value |
|---|---|
| Voxel cell | **0.5 m** cube. The player is ~2 × 3.6 cells (1 × 1.8 m) — the same tile:player ratio as the 2D reference game. |
| Chunk | **32³ cells** (16 m). Cubic chunks, vertical chunking. |
| World axes | Godot convention: **+Y up**. Cell `(0,0,0)` is the bottom (underworld floor) corner. |
| Godot units | 1 unit = 1 m. `world = cell * 0.5`. |
| Simulation tick | **60 ticks/s** fixed. Data (use times, buff durations, cooldowns) is authored in **ticks**. |
| Distances in data | Authored in **cells** unless a field name ends in `M` (meters). |

## Code layout
* `src/Strata.Core` — engine-agnostic C#. **Never reference Godot here.** Everything deterministic or
  testable belongs here (worldgen, voxels, meshing to arrays, lighting, content, combat math, AI logic,
  saves, commands).
* `game/` — Godot adapters only: nodes, rendering upload, physics shapes, input, UI, audio.
* One responsibility per file. `tools/ci/check_file_length.py` fails CI above 400 lines (aim < 300).
  Vendored code lives in `ThirdParty/` folders (exempt).
* Hub classes are split into partials by concern (`GameWorld.Saving`/`.PlayerFiles`, `ItemUseSystem.Shooting`/
  `.Consume`, `NpcSystem.Death`, `ContentValidator.Enemies`/`.Projectiles`, `WorldLauncher.Assets`/`.Effects`/
  `.Player`/`.Ui`, `PlayerController.Fall`, `InteractionController.Talk`). New code goes into the partial for its
  concern, or a new one, not into the main file.
* No giant switch statements over content. Behaviour is attached through registries keyed by string
  type names (`BehaviorRegistry`, `ConditionRegistry`, pass registries, command registry).

## Content IDs
* String ids, `snake_case`, unique per category (`copper_ore`, `king_slime`).
* Runtime numeric ids are dense and assigned in manifest order **per session**; they are never
  persisted. Saves store per-save id tables (string lists) and remap on load. Unknown ids become
  placeholders that keep their original string so data is never silently lost.
* Every content entry may declare `"covers": ["terraria:<concept>"]` linking it to the reference DB.
  Coverage is computed by `Strata.Tools coverage`.
* Names follow the reference game (a personal build, the player's choice on 2026-09-26): ids and display names are
  Terraria's (`king_slime`, "King Slime"). Renamed ids are listed in `data/config/aliases.json` so older saves load;
  `tools/content/terraria_names.py` holds the mapping the migration used.

## Determinism (world generation)
* All randomness derives from the world seed through `SeedTree` using **stable name hashes** of the
  pass/feature, never from call order. Adding a pass must not shift other passes' output.
* **No `Math.Sin/Cos/Pow/Exp/Log` in generation code** (platform C runtimes differ in the last bit).
  Allowed: `+ - * /`, `Math.Floor`, `Math.Sqrt`, `Math.Abs`, `Math.Min/Max`, lookup tables, and the
  vendored FastNoiseLite (verified free of transcendental calls).
* Chunk passes are **pure**: they read only the plan, pure field functions and the feature index —
  never another chunk's buffer. A test asserts generating a chunk alone equals generating it amid
  neighbours.
* `WorldGenVersion` is bumped whenever generator output changes; golden-hash tests pin output and run
  on Linux and Windows CI.

## Threading
* All voxel **writes** happen on the main thread through `EditBatch`.
* Voxel arrays are copy-on-write; background jobs hold array snapshots and results carry the chunk
  version they were built from. Stale results are dropped.
* Godot `RenderingServer`/physics calls happen on the main thread under a per-frame budget.

## Errors
* Never swallow exceptions silently. Log with context (`ILog`) and surface content/validation errors
  at startup; CI treats any logged error in the smoke test as failure.

## Saves
* Every save artifact carries a format id + version. Migrations are explicit classes in
  `Strata.Core.Persistence.Migrations` with fixture tests. Writes are atomic (temp + rename) with a
  rotating backup.

## Assets
* Blender generators live in `tools/blender/`, are deterministic from parameters, and export GLB to
  `assets/models/`. `assets/manifest.json` lists every asset; the validator cross-checks content refs.
* Textures are synthesized by `tools/textures/` into texture-array atlases.
* The Blender asset spec is `tools/blender/assets.json` plus the drop-in files `tools/blender/assets.d/*.json`; read
  it through `load_spec()` in `tools/blender/asset_spec.py`, never by opening `assets.json` directly.

## Testing
* Core logic is covered by xunit tests in `tests/Strata.Core.Tests`; engine behaviour by command scripts in
  `game/Testing/Scripts/*.cmds`, which CI runs headless one by one (each must print `SMOKE PASS`).
* Scripts check state through probes (`assert`, `wait_until`). A new probe goes in a probe-source file (see
  *Extension points*); existing probes keep their names, since scripts depend on them.
* A block appended to a shared `.cmds` file goes at the end, under a marker line with its work-package id, in
  work-package order, so parallel branches merge by concatenation:
  ```
  # --- P2-06 ---
  give torch 5
  assert inv.torch >= 5
  ```

## Extension points
Parallel work adds a new file at one of these points instead of editing a shared hub file.

| What | Add | Picked up by |
|---|---|---|
| Probes (Core) | A `static class` marked `[ProbeSource]` in `src/Strata.Core` with `static void Register(CommandContext ctx)` (e.g. `Commands/TownProbes.cs`) | Every `CommandContext`, after `StandardProbes` |
| Probes (engine) | A `[ProbeSource]` static class in `game/` whose `Register(CommandContext ctx, …)` may also take the `LaunchedWorld` and the `GodotLog` (e.g. `game/Debug/PlayerProbes.cs`) | `GodotCommandHost.CreateContext`, after the core engine counters |
| Sound effects | `tools/audio/sfx_recipes_<topic>.py` defining `RECIPES` (and optionally `GAIN`) like `sfx_recipes.py` | `sfx_recipes.py`, which merges them by file name |
| Blender asset entries | `tools/blender/assets.d/<topic>.json` with the `assets.json` schema (see its README) | `asset_spec.load_spec()` (`build_all.py`, `preview.py`) |
| Blender generators | `tools/blender/bgen/<module>.py` with functions registered by `@generator` from `bgen/__init__.py` | `bgen.load_all()` in `build_all.py` and `preview.py`, alongside the per-module `GENERATORS` dicts |
| Smoke-script blocks | A `# --- <WP id> ---` block at the end of a shared `.cmds` file (see *Testing*) | The script runner (`#` lines are comments) |

Probe sources register in full-type-name order; a `Register` asking for a service the host does not pass fails at
start-up. A duplicated probe name, sound id, asset id or generator name is an error, never a silent override.
