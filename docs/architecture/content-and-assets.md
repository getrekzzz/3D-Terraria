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
| Terrain texture array (71 layers; height in albedo alpha) + detail array | `tools/textures/build_textures.py --set=terrain` | `assets/textures/terrain/`, `assets/textures/detail/` |
| Prop texture array (bark, leaf-card sprigs, metals, cloth, leather…) | `--set=props` | `assets/textures/props/` |
| Block item icons | `tools/textures/build_icons.py` | `assets/icons/items/<block>.png` |
| Tile objects, items, creatures + icons | `tools/blender/build_all.py` (+ `assets.json`) | `assets/models/{objects,items,creatures}/*.glb`, icons, `assets/manifest.json` |

Textures are synthesised in numpy (`tools/textures/gen_{natural,rock,built,plants,detail}.py` on `synth.py`):
stones, soils, bark, bricks, planks, metals, cloth and leather with their height (albedo alpha for opaque layers),
roughness and metalness; cut-out and translucent layers keep opacity in alpha instead.

Blender conventions: 1 unit = 1 m, Z up, front faces +Y (glTF −Z); tile objects have their origin at the
footprint's bottom centre, items at the grip with the business end along +Z. Material slots are named
`prop:<layer>[:glow]` (object-space triplanar props shader), `card:<layer>` (alpha-tested leaf cards with UVs and
crown-shaped custom normals: tree crowns and bushes) or `char:<kind>` (people); vertex colour tints (written as
sRGB). Creatures are rigidly skinned; actions are exported as glTF animations. `build_all.py --check` verifies
every tile object, item and enemy has a generator (no Blender needed).

People (`generator: humanoid`) are built by `tools/blender/bgen/human/`: a proportioned frame (height, build,
weight, age, sex), metaball body, a head sculpted as a signed distance field (`headshape.py`, `earshape.py`, meshed
by `sdf.py`/`fieldmesh.py` with the meshes cached in `tools/blender/_cache`, not committed: skull, cheekbones, jaw,
almond eyelids with creases, a nose with wings and nostrils, lips with a cupid's bow, rolled ears) with separate
eyeballs, brows and lashes; hands with jointed fingers and nails; painted skin (lips, flush, knuckles, stubble);
hair sculpted from traced locks into one surface with grooves between the clumps (`locks.py` traces where each lock
roots and how it flows, `hairsdf.py` blends them into clumps with UVs along the flow) and beards as strand shells;
clothing cut from body shells and tailored (`tailoring.py`: eased off the skin, spanned across hollows, cinched under
the belt and boot tops, folded inside the elbows and knees and stacked above the cuffs) then finished
(`finishing.py`: pressed seams, ribbed collars and cuffs, hems, turned-down boot rims, soles, buttons): shirts,
trousers, dresses, coats, robes with sashes, aprons, belts with buckles, boots, hats, masks. Ambient occlusion is baked
into the vertex alpha (`occlusion.py`) for the character shader. A 52-bone rig (fingers included) with heat/chain
weights; long hair follows the torso, not the arms. Skeletons are separate bones on the same rig. Params per
character live in `assets.json`. They export rest-pose UVs and tangents for the character shader.

Human motion (`human/motion.py`, `human/actions.py`) is authored as pose functions sampled every frame at 30 fps
(exact curves, eased keys, seamless loops): idle, walk, run, sprint, jump, fall, land, swim, climb, glide, hurt,
death (plus an undead shamble), and upper-body item actions — swing, mine, thrust, shoot, bow, cast, drink, use,
hold. The right hand is a closed fist around a handle at `motion.GRIP`, where the game attaches held items; attacks
have a wind-up, a coil, a whip through to the wrist and a follow-through, and strike where the melee system sweeps
its hits (see actions.py). Characters with the same skeleton (body, height, gait) share one animation set,
`assets/models/creatures/anim/<set>.glb`, with `index.json` naming each character's set; rebuild them with
`build_all.py --anims [--shard=I/N]` (seconds each, no character meshes touched).

Bosses and other detailed creatures (`bgen/bosses_pre*.py`, `bosses_hard.py`, `bosses_lunar.py`) build organic
bodies from metaballs (`bgen/detail.py`: sculpted and painted per vertex, branching veins, teeth rings and rows),
translucent parts in `prop:<layer>:clear` slots (slime gel, cornea, wings), and small rigs through
`bosses_pre2.clips` (mandibles, jaws, wings, a stinger, Deerclops' walk and swipe). Free-flying boss hands are joined
to the head by an `armModel` the game stretches between them. `tools/blender/preview.py` renders any creature (or a
raw `generator:params`) as a Workbench contact sheet of views or clip frames, without the game.

Rendering-only settings: `data/config/graphics.json` (presets) and `data/config/grass.json` (grass kinds per grass
block) are validated by `Strata.Tools validate` and at boot.
