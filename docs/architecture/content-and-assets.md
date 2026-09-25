# Content and assets

## Content (`data/`)
* `manifest.json` lists files in load order (`Strata.Tools manifest` regenerates it; `reference/` and
  `config/` are excluded). Strict JSON with comments; unknown members are errors.
* Registries give dense runtime ids; saves store string ids with per-save tables and placeholders.
* Derived definitions: placeable blocks and tile objects imply their items.
* Open-ended behaviour uses typed specs: item `behaviors`, enemy `brain`, buff `effects`, projectile `onHit`,
  recipe/spawn `conditions` — validated against `SpecCatalog`.
* `ContentValidator`: references, specs, DAG cycles in progression, obtainability, texture layers and
  conventional asset paths (every tile-object variant, item icon/model, creature and projectile model).
* `data/reference/*.json`: Terraria concepts (names only) generated from `tools/content/reference/`.
  Content links to them with `covers`; `Strata.Tools coverage` reports coverage and CI publishes it.

## Assets
| Asset | Tool | Output |
|---|---|---|
| Terrain texture array (71 layers) | `tools/textures/build_textures.py --set=terrain` | `assets/textures/terrain/` |
| Prop texture array (bark, foliage, metals, cloth…) | `--set=props` | `assets/textures/props/` |
| Block item icons | `tools/textures/build_icons.py` | `assets/icons/items/<block>.png` |
| Tile objects, items, creatures + icons | `tools/blender/build_all.py` (+ `assets.json`) | `assets/models/{objects,items,creatures}/*.glb`, icons, `assets/manifest.json` |

Blender conventions: 1 unit = 1 m, Z up, front faces +Y (glTF −Z); tile objects have their origin at the
footprint's bottom centre, items at the grip with the business end along +Z. Material slots are named
`prop:<layer>[:glow]`; vertex colour tints; the game's prop shader samples the props texture array in object
space (1 m tiles, same as Blender's box projection) and applies voxel light, sway and tint. Creatures are
rigidly skinned; actions are exported as glTF animations. `build_all.py --check` verifies every tile object,
item and enemy has a generator (no Blender needed).
