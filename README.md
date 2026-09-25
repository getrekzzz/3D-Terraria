# Strata (working title)

A native-3D evolution of the 2D sandbox-progression genre: enormous volumetric procedural worlds,
mining and building, deep underground exploration, data-driven items, huge bosses, NPC settlements,
world events and a world that transforms as you progress.

* Engine: **Godot 4.7 (.NET / C#)** · assets: **Blender 5.2** (procedural `bpy` scripts)
* Design & status: [`docs/3D_TERRARIA_MASTER_PLAN.md`](docs/3D_TERRARIA_MASTER_PLAN.md) · architecture: [`docs/architecture/`](docs/architecture)
* Rules: [`docs/CONVENTIONS.md`](docs/CONVENTIONS.md)

![Player in a generated forest](docs/screenshots/m1_player_forest.png)

## Running
1. Install **Godot 4.7.x .NET** and the **.NET 8 SDK**.
2. Open `project.godot` in Godot and press Play (F5), or from a terminal: `godot --path .`

Command-line options (after `--`): `--world=NAME --seed=N --size=tiny|small|medium|large --new --player=NAME
--fresh-player --view=CHUNKS --hour=H --free-camera --exec="cmd;cmd" --screenshot=PATH --smoke=SCRIPT`.

### Controls
| Key | Action |
|---|---|
| WASD, Space, Shift, Ctrl | move, jump (hold for height), sprint, drop through platforms |
| Mouse / wheel / 1–0 | look / hotbar |
| LMB / RMB or E | use held item (mine, chop, place, attack, drink) / interact (doors, chests, beds) |
| T / Shift+T, R, Alt | cycle build shape, rotate, precision (single-cell) mining |
| Q (Ctrl+Q) | drop one (stack) |
| Tab or I | inventory, equipment, crafting, open chest |
| V, Ctrl+wheel | first/third person, camera distance |
| ` or F1 / F3 / Esc | console / performance overlay / release mouse |

Console commands include `help`, `give`, `spawn`, `boss`, `event`, `tp`, `biome`, `time`, `flag`, `setblock`,
`fill`, `place`, `liquid`, `liquids`, `regen`, `inspect`, `probe`, `assert`, `save`, `stream`, `ui`, `camera`
(`help <command>` for usage).

## Developing
```bash
dotnet build Strata.sln                                   # game layer + core + tests + tools
dotnet test tests/Strata.Core.Tests                       # core unit, determinism, gameplay, content tests
dotnet run --project tools/Strata.Tools -- validate       # content + asset validation
dotnet run --project tools/Strata.Tools -- coverage       # Terraria concept coverage (data/reference)
dotnet run --project tools/Strata.Tools -- worldgen-report --seed=1234 --size=tiny
godot --headless --path . --import                        # import assets once
godot --headless --path . -- --world=smoke --size=tiny --new --player=smoke --fresh-player \
      --view=2 --smoke=res://game/Testing/Scripts/basic.cmds   # engine smoke test (prints SMOKE PASS)
```

### Asset pipelines
```bash
python3 tools/textures/build_textures.py            # terrain + props texture arrays (numpy, Pillow)
python3 tools/textures/build_icons.py               # block item icons
blender --background --python tools/blender/build_all.py -- [--only=GLOB] [--no-icons]
python3.13 tools/blender/build_all.py               # same, with `pip install bpy==5.2.2`
python3 tools/blender/build_all.py --check          # coverage check only (no Blender)
python3 tools/content/gen_reference.py              # regenerate data/reference
```
On Windows: `"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --python tools/blender/build_all.py`.
