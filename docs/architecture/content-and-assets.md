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
weight, age, sex; muscle and fat above average add visible mass), metaball body, a head sculpted as a signed
distance field (`headshape.py` puts together `faceforms.py`: skull, cheekbones, cheeks and a lofted lower face with
a clean jaw line, softer and narrower on women, fuller with weight, sagging with age; `nosemouth.py`: a nose in one
piece with wings and nostrils, lips with a cupid's bow; `earshape.py`: rolled ears; `creature.py`; almond eyelids with
creases; meshed by `sdf.py`/`fieldmesh.py` with the meshes cached in `tools/blender/_cache`, not committed; creature
params for goblin and elf ears, long or wide noses, wide mouths, heavy brows and jaws, an underbite with tusks and a
reptile's snout) with separate eyeballs, brows and lashes; hands
with jointed fingers and nails; painted skin (lips, flush, knuckles, stubble); hair sculpted from traced locks into
one surface with grooves between the clumps (`locks.py` traces where each lock roots and how it flows: a neat cut
with a side-swept fringe and an even edge, messy hair with tufts standing up, a woman's short hair as a chin-length
bob; `hairsdf.py` blends them into lens-shaped clumps that stay full round bends and end in rounded or blunt tips,
with UVs along the flow, `hairpaint.py`; long hair falls over a smooth dome over each ear, `hair.py`, and with
ponytails hangs onto the shoulders, back and clothes, `longhair.py`, `hairdrape.py`, `hairtails.py`; buns are a
rope coiled round a core; hair under a hat is pressed flat inside it, comes out from under its rim no thicker than
the hat and is cut along the hat's own edge, `underhat.py`);
beards in styles (`beard.py`: where the beard grows, a feathered cheek line up to the sideburns, the lips left
clear; `beardlocks.py` lays a combed mass over the face that grows out of the skin at a ragged edge, with the
moustache rising out of it over the upper lip; `beardbib.py` hangs one mass below the jaw, from the chin and the
throat down to a few big tapered locks with soft grooves, resting on the neck and the clothes; `beardsdf.py` melts
them into one mesh); clothing cut from body shells and tailored
(`tailoring.py`, `drape.py`: eased off the skin, bridged across clefts and settled over hollows, hanging from the
chest, cinched under the belt and boot tops, folded inside the elbows and knees) then finished (`finishing.py`:
pressed seams, slim collars and cuffs, hems, turned-down boot rims, soles, buttons): shirts, trousers, coats, robes
as one loose garment with a skirt and a tied sash (`robes.py`, `skirts.py`), torn undead clothing (`tatters.py`: a
few large rips and ragged hems with turned edges over skin), body suits for creatures, bib aprons tied at the neck
and waist (`apron.py`, `straps.py`), belts with buckles, boots, masks (`facemask.py`), caps and hoods sculpted as
cloth fields (`headwear.py`, `capshapes.py`; `hood.py` drapes the cowl over whatever is under it), wide hats
(`hats.py`), wreaths and circlets resting on the hair (`crowns.py`), horns, wings and tails (`features.py`,
`wings.py`, `icewings.py`, `tails.py`, `serpent.py`; roots out through hemmed slits in the clothes, `wingroot.py`)
and bird talons (`talons.py`). Monster faces are sculpted into the head's field (`monsterface.py`: a cyclops eye, a
stitched brute, a grimacing ghoul, a faceless void) with their own parts (`monsterparts.py`); Medusa wears snakes for
hair (`snakehair.py`), and a shaman's mask, headdress, beads, leaf skirt and paint (`tribal.py`, `tribalmask.py`)
and a ghost's wisping hem (`spectre.py`) are put on through `creaturewear.py`. Ambient occlusion is baked into the vertex alpha
(`occlusion.py`) for the character shader. A 52-bone rig (fingers included) with heat/chain weights; long hair
follows the torso, not the arms. Skeletons are sculpted bone fields on the same rig (`skulls.py` with orbits, teeth
and a hinged jaw; `skeltorso.py`, `skelgirdle.py`, `skellimbs.py` one connected column, ribcage, pelvis and limbs)
weighted to stay joined in every action, and dressed from a slim body when their params give an outfit
(`skeldress.py`, `skelfit.py`). Bodies of other stuff share the rig and actions too (`matter.py`): a granite golem
of chipped, cracked blocks (`golem.py`), a gnarled wooden creature with fissured bark, twig fingers, branches and
leaves (`woodfolk.py`, `twigs.py`), a gingerbread man with icing and gumdrops (`cookie.py`) and an empty suit of
plate armour glowing inside (`plate.py`, `gauntlet.py`); a scarecrow has a stuffed burlap sack for a head, straw
in its cuffs, collar and boots, patched clothes and a rope belt (`scarecrow.py`, `straw.py`, `patches.py`,
`rope.py`). Params per character live in `assets.json` (documented at the top of
`bgen/human/__init__.py`). They export rest-pose UVs and tangents for the character shader.

Machines and aliens (`human/scifi.py`, worn after the clothes) get a grey alien's or an insect's head with big
glossy eyes and antennae (`alienhead.py`, on the head's distance field), armour plates over a body suit (`plates.py`
fits each plate's core to the clothes it covers and keeps it off them; `armor.py`, `armorparts.py`, `armorsets.py`
lay out the styles with panel seams and glowing strips; `solarblades.py` adds a Selenian's flame blades), a glass
dome or a diver's helmet on a neck ring, a closed helm with lit eye slits, a cyborg's skull implant and a jetpack
(`helmets.py`, `diverhelm.py`, `solarhelm.py`, `implant.py`). Glass uses the `char:glass` slot, drawn see-through by
`shaders/character_glass.gdshader` and left out of the baked occlusion.

Human motion (`human/motion.py`, `human/actions.py`) is authored as pose functions sampled every frame at 30 fps
(exact curves, eased keys, seamless loops): idle, walk, run, sprint, jump, fall, land, swim, climb, glide, hurt,
death (plus an undead shamble), and upper-body item actions — swing, mine, thrust, shoot, bow, cast, drink, use,
hold. The right hand is a closed fist around a handle at `motion.GRIP`, where the game attaches held items; attacks
have a wind-up, a coil, a whip through to the wrist and a follow-through, and strike where the melee system lands
its hits (see actions.py). Characters with the same skeleton (body, height, gait, arm spread) share one animation set,
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
