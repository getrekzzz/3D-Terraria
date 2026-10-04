# Rendering

Everything is drawn by Godot 4's Forward+ renderer with custom spatial shaders (`shaders/`). The look is set by
data: graphics presets (`data/config/graphics.json`), grass kinds (`data/config/grass.json`), biome fog and sky
tints (`data/biomes.json`) and the generated texture arrays.

## Light model
| Light | Source | How it reaches a surface |
|---|---|---|
| Sun / moon | `DirectionalLight3D`, colour and irradiance from `Atmosphere.cs` (CPU port of the sky's scattering) | Custom `light()`: GGX (height-correlated Smith, Schlick, Burley diffuse) in `voxel_brdf.gdshaderinc`, gated by the baked sky exposure (`strata_sun_gate`) and by cloud shadows (`clouds.gdshaderinc`) |
| Sky ambient and reflections | Godot environment, ambient = sky, reflections = sky radiance cubemap | Multiplied per surface by `AO = strata_sky_visibility(voxel light)`, so a sealed cave gets none; Godot also derives specular occlusion from it |
| Block light (torches, lava, glowing ores and fungi) | Baked RGB voxel light per vertex / instance | `EMISSION = strata_block_light(albedo, light, ao)`; its curve (`strata_block_curve`, level^1.5) is gentler than the sky's so a torch's glow carries across a room |
| Nearest light sources | `DynamicLights`: a pool of shadowless `OmniLight3D` (size = preset `dynamicLights`, 0 on low) given each ¼ s to the nearest placed lights, lava hotspots (`LavaEffects`) and glowing creatures (`NpcRenderer.Glows`, fireflies blink) | Regular Godot lights: longer reach and directional shading on top of the baked glow; a firefly swarm never costs more than the pool |
| Carried torch | `PlayerLight`: an `OmniLight3D` at the flame in the hand (first or third person), ~15 m reach, a fire's flicker (shadows on high/ultra) | Regular Godot light through the same `light()` |
| Screen space | SSAO, SSIL (colour bleeding), SSR, volumetric fog with light shafts, auto exposure, glow | Environment settings per preset |

`EnvironmentController(.Frame).cs` drives time of day: sun and moon arcs, atmosphere-coloured light, night
desaturation, fog (aerial perspective above ground, close biome-tinted gloom below), cloud cover drifting with the
weather and the ground wind (`strata_wind`) that grass and foliage share.

## Sky (`sky.gdshader`)
Single-scattering atmosphere (Rayleigh, Mie, ozone; quadratic sample spacing) evaluated in the quarter-resolution
pass, a procedural cloud layer (coverage and wind from `strata_clouds`, noise from the shared `strata_cloud_noise`
texture — the same field casts the cloud shadows on the ground), stars, a lit moon with phases and the sun disc.
The cubemap pass adds the ambient tint used for image-based lighting. Changing a sky material parameter re-renders
the radiance cubemap, so `EnvironmentController` updates the material four times a second; per-frame values (star
twinkle, cloud parallax) read the `strata_time` and `strata_camera` globals instead.

## Surfaces
| Shader | Used for | Notes |
|---|---|---|
| `terrain.gdshader` | Opaque chunk meshes | Triplanar texture array; three surfaces per triangle blended by height (albedo alpha) + barycentrics; detail array grain; macro colour variation; crisp tiles for built blocks; ore glints (layer table `.z`, set for `*_ore` layers) twinkle within ~12 m, brighter in torchlight |
| `terrain_cutout.gdshader`, `terrain_glass.gdshader` | Leaf/web blocks, glass | Foliage translucency |
| `liquid.gdshader` | Water, honey | Water: screen-space refraction, Beer–Lambert absorption by thickness, in-scatter colour, sky Fresnel, shallow foam, caustics on the bed. Honey: thick amber |
| `lava.gdshader` | Lava (its own opaque surface from `LiquidMesher`, no screen copy) | A churning temperature field decides where the melt skins over: warped two-scale crust plates of dark basalt split by glowing seams and cooling fissures, open hot rivers in blackbody colours (HDR emission, so it blooms), bubbles that swell and burst, a slow downward heave, streaming falls. `LavaEffects` adds embers from the lava surface cells the mesher lists (`LavaTops`) and a flickering heat light per lava chunk |
| `flame.gdshader` | Torch and candle flames (`flame` spec on the object; held torches too) | An upright billboard of rising turbulence through a teardrop: white-hot core, orange body, flickering red licks; additive HDR. Placed flames are one MultiMesh per chunk and colour (`Flames`), held ones add a few embers in third person |
| `prop.gdshader` | Generated props, items, creatures | Object-space triplanar of the props array, vertex-colour tint, sway (body in `include/prop_common.gdshaderinc`) |
| `prop_clear.gdshader` | Translucent parts (`prop:<layer>:clear`): slime gel, corneas, wings | The prop shader with alpha: see-through face on, thicker at the rim, vertex alpha per part; drawn after opaque geometry so what is inside shows |
| `foliage_card.gdshader` | Tree crowns and bushes (`card:<layer>` slots) | UV-mapped alpha-tested sprig cards, crown-shaped normals on both faces, sway + flutter, translucency, mip-aware alpha |
| `character.gdshader` | People (`char:<kind>` slots) | Skin (pores from the detail array, subsurface scattering, diffuse that wraps furthest in red so shadow edges go warm), eyes, Kajiya–Kay hair highlights along exported tangents (shifted primary and secondary, jittered per strand), nails, cloth sheen, leather, metal, bone, glow. Vertex alpha is ambient occlusion baked by the generator (eye sockets, between locks, inside folds): it darkens ambient light fully, direct light partly and highlights fully. Detail is sampled in the rest pose rebuilt from the two UV sets, so it sticks to the animated body |
| `blade_trail.gdshader` | The held blade's trail (`BladeTrail`) | Additive ribbon from part way up the blade to its tip: clear at the inner edge, whitest just inside the tip, faded along its length by vertex alpha, tinted to the item and dimmed in the dark (block light, or sky light scaled by the time of day) |
| `grass.gdshader` | Instanced grass and flowers | See below |
| `sprite_lit.gdshader` | Billboards and particles | Uses `strata_ambient` because it disables environment light |

## Grass
Mesh jobs scatter clumps over the upward faces of grass surfaces (`GrassScatter`, Core, unit-tested): density and
look per grass block from `grass.json`; placement hashed from world position (rebuilds regrow the same grass);
clumps thin out across material borders like the texture blend, stay off steep faces and out of blocks above;
light is interpolated from the mesh. The output is sorted by a rank hashed per clump, so any prefix is an even
thinning of the chunk, and which clumps a density draws never depends on the rest of the chunk: digging a block only
changes the grass beside it.

`GrassRenderer` keeps one MultiMesh of clumps (and one of flowers) per chunk inside the preset's grass distance:
near chunks draw the detailed clump (9 curved blades), far ones a 5-blade clump and a shrinking prefix; the shader
widens far blades so the cover stays even, sinks them into the ground at the distance limit, bends them with gusts
travelling downwind and parts them around the player (`strata_trample`). A chunk's grass switches when its terrain
mesh is uploaded, never before. Per-clump looks in the shader (dry tips) are keyed on the clump's position, not its
slot in the buffer, which moves whenever an edit rebuilds the chunk. Grass casts no shadows.

## Horizon
Beyond the streamed chunks the world continues as a heightfield (`HorizonField`, Core, tested): terrain height from
the generator's `TerrainField`, the surface biome's map colour, and the sea over low ground and past the world's edge.
`Build` makes a polar mesh whose rings widen with distance (a continuous level of detail: about 3 m cells at the
streamed edge, tens of metres at the horizon). `HorizonRenderer` rebuilds it on a worker thread when the focus drifts
48 m from its centre; `horizon.gdshader` discards it inside the loaded chunk box (real voxels take over there), adds
macro variation and rock on steep slopes, and lets the fog's aerial perspective melt it into the sky. The preset's
`horizonDistance` (0 = off) also sets the fog reach and the camera's far plane. It samples fields, not voxels:
caves, structures, edits and hardmode wedges are not in it.

## Presets
`GraphicsConfig` (Core, validated with ranges, tested) → `GraphicsSettings` (game) applies them: render scale and
upscaler (FSR 1/2), anti-aliasing (FXAA/SMAA/TAA/MSAA), anisotropy, shadow atlas, distance and filter, sun size
(PCSS), torch shadows, SSAO/SSIL/SSR quality, volumetric fog grid, sky radiance size, cloud steps, auto exposure,
grass density and distance, mesh LOD threshold, horizon distance. `graphics <preset>` / `--graphics=` switch at runtime; the choice is
remembered in `user://graphics.cfg`. CI screenshots use `medium`; lavapipe QA uses `high`.

Frame-time budget (ultra aims at 60 fps at 1080p on an RX 6600 / RTX 2060-class GPU):
* Chunks sealed from the sky sit on their own visual layer (`ChunkLayers.Sealed`) that the sun's and moon's
  `ShadowCasterMask` skips: they never draw into the 4-cascade sun shadow map, yet torches still cast their shadows.
* Only one sky light casts shadows at a time (the moon waits for sunset).
* SSAO/SSIL can run at half resolution (`ssaoHalf`/`ssilHalf`); high and ultra do.
* `DynamicResolution` lowers the 3D resolution (FSR, down to 60%) only when the measured GPU time shows the GPU is the
  bottleneck and frames fall under ~57 fps, and raises it again when the GPU has headroom (works with vsync).
* F3 shows GPU and render-CPU milliseconds, the current 3D scale and the preset.

## Global shader parameters (`project.godot`)
`strata_sky_ambient`, `strata_block_light_color`, `strata_daylight`, `strata_time`, `strata_camera`, `strata_light_dir`,
`strata_clouds`, `strata_cloud_noise`, `strata_wind`, `strata_trample`.

Look: AgX tonemapping with colour adjustment (saturation 1.22 by day, 0.85 at night; contrast 1.06) for the reference
game's rich palette over realistic materials; glow in Screen blend so HDR emitters (flames, lava, glints, magic) bloom
without hazing the image; a moonlit night keeps a blue ambient floor so shapes and creatures stay readable.

## Creature animation
Generated humans (the player, town folk, humanoid enemies) play their shared animation set through a
`HumanoidAnimator` (an `AnimationTree` built in code): a cross-faded locomotion state (idle, walk, run, sprint, jump,
fall, land, swim, climb, glide, death; gait and playback rate chosen from ground speed so feet do not slide, negative
when backpedalling), an upper-body `OneShot` for item actions stretched to the item's use time and restarted on
every use (so you can swing while running), a right-arm blend that holds a carried torch up, and a hurt flinch. For
the player an `AimModifier` (`SkeletonModifier3D`) then turns the torso and head toward the aim while the legs face
the way they walk, and brings the arms up to the aim for guns, bows, staffs and spears.

The held item hangs on a `BoneAttachment3D` at `hand.R`, placed where the handle runs through the closed fist
(`PlayerAvatar.Grip`, matching `GRIP` in `tools/blender/bgen/human/motion.py`) and leaving it past the thumb; guns are
tipped 70 degrees toward the fingers so the barrel lies along the forearm (`HeldTilt`, `actions.TILT`). The actions
are authored for that grip, so blades swing through level at the strike and spears and torches stay upright or level
without folding the wrist. Between uses the `AimModifier` also turns the hand to carry the item (`PlayerAvatar.Blade`):
blades and tools tip forward and down beside the leg, fanned out from it (long ones less steeply so the tip stays off
the ground), spears, staffs, bows and rods tip up, guns stay as gripped; the turn drops out as an action fades in and
returns as it fades out. Attacks are timed like a real blow: a settle and a wind-up, a coil where the torso starts
back while the blade still drags, a whip that reaches the wrist last, then follow-through, overshoot and settle. A
swing or chop strikes between 0.36 and 0.56 of its clip and a thrust drives out between 0.33 and 0.52, which is where
`MeleeSystem` lands its hits (a swing sweeps right to left, `SweepProgress`; nothing connects in the wind-up or the
follow-through), where `SwingTrail` draws the reach arc and where `BladeTrail` lets the blade leave a ribbon:
sampled from the hand bone once the skeleton is posed, so it starts at the blade, smoothed with Catmull-Rom between
frames and fading back along the path (fainter for tool chops, a streak for thrusts). It reads the clip's own
position (`HumanoidAnimator.ActionPhase`), so a long frame cannot put it out of step with the pose.
`game/Testing/Tours/wielding.cmds` photographs the carry and each action close up (`camera distance`), the swing also
from behind mid-strike.

`NpcRenderer.Motion` adds presence to everything else: `facing` look (eyes stare at their target) or velocity (fish),
fliers banking into turns and pitching with their climb, gels squashing, a quiver before a charge, a stretch along a
dash, a shrink on teleport, a spin when a boss changes form; humanoid enemies swing or shoot when they attack, flinch
when hit and fall dead (corpses sink after a moment, other creatures shrink away). Models built around their centre
are raised onto their feet by their bounds (`CreatureVisual.LiftFor`, only for centred, box-sized models). `NpcRenderer.Arms` stretches a boss's `armModel`
from its shoulders to each free-flying hand with an outward elbow.

