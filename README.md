# Strata (working title)

A native-3D evolution of the 2D sandbox-progression genre: enormous volumetric procedural worlds,
mining and building, deep underground exploration, data-driven items, huge bosses, NPC settlements,
world events and a world that transforms as you progress.

* Engine: **Godot 4.7 (.NET / C#)** · assets: **Blender 5.2** (procedural `bpy` scripts)
* Design & status: [`docs/3D_TERRARIA_MASTER_PLAN.md`](docs/3D_TERRARIA_MASTER_PLAN.md)
* Rules: [`docs/CONVENTIONS.md`](docs/CONVENTIONS.md)

## Running
1. Install **Godot 4.7.x .NET** and the **.NET 8 SDK**.
2. Open `project.godot` in Godot and press Play (F5), or from a terminal:
   `godot --path .`

## Developing
```bash
dotnet build Strata.sln                      # compile game layer + core + tests + tools
dotnet test tests/Strata.Core.Tests          # core unit/determinism/content tests
dotnet run --project tools/Strata.Tools -- validate          # content + asset validation
dotnet run --project tools/Strata.Tools -- worldgen-report   # PNG maps/sections of a seed
godot --headless --path . res://game/Testing/SmokeTest.tscn  # engine smoke test
```
Assets: `python tools/textures/build_textures.py` and
`blender --background --python tools/blender/build_all.py` (or `python3.13` with the `bpy` module).
