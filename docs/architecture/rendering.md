# Rendering

Everything is drawn by Godot 4's Forward+ renderer with custom spatial shaders (`shaders/`). The look is set by
data: graphics presets (`data/config/graphics.json`), grass kinds (`data/config/grass.json`), biome fog and sky
tints (`data/biomes.json`) and the generated texture arrays.

## Light model
| Light | Source | How it reaches a surface |
|---|---|---|
| Sun / moon | `DirectionalLight3D`, colour and irradiance from `Atmosphere.cs` (CPU port of the sky's scattering) | Custom `light()`: GGX (height-correlated Smith, Schlick, Burley diffuse) in `voxel_brdf.gdshaderinc`, gated by the baked sky exposure (`strata_sun_gate`) and by cloud shadows (`clouds.gdshaderinc`) |
| Sky ambient and reflections | Godot environment, ambient = sky, reflections = sky radiance cubemap | Multiplied per surface by `AO = strata_sky_visibility(voxel light)`, so a sealed cave gets none; Godot also derives specular occlusion from it |
| Block light (torches, lava, glowing ores and fungi) | Baked RGB voxel light per vertex / instance | `EMISSION = strata_block_light(albedo, light, ao)` |
| Carried torch | `OmniLight3D` on the player (shadows on high/ultra) | Regular Godot light through the same `light()` |
| Screen space | SSAO, SSIL (colour bleeding), SSR, volumetric fog with light shafts, auto exposure, glow | Environment settings per preset |

`EnvironmentController(.Frame).cs` drives time of day: sun and moon arcs, atmosphere-coloured light, night
desaturation, fog (aerial perspective above ground, close biome-tinted gloom below), cloud cover drifting with the
weather and the ground wind (`strata_wind`) that grass and foliage share.

## Sky (`sky.gdshader`)
Single-scattering atmosphere (Rayleigh, Mie, ozone; quadratic sample spacing) evaluated in the quarter-resolution
pass, a procedural cloud layer (coverage and wind from `strata_clouds`, noise from the shared `strata_cloud_noise`
texture — the same field casts the cloud shadows on the ground), stars, a lit moon with phases and the sun disc.
The cubemap pass adds the ambient tint used for image-based lighting.

## Surfaces
| Shader | Used for | Notes |
|---|---|---|
| `terrain.gdshader` | Opaque chunk meshes | Triplanar texture array; three surfaces per triangle blended by height (albedo alpha) + barycentrics; detail array grain; macro colour variation; crisp tiles for built blocks |
| `terrain_cutout.gdshader`, `terrain_glass.gdshader` | Leaf/web blocks, glass | Foliage translucency |
| `liquid.gdshader` | Water, lava, honey | Water: screen-space refraction, Beer–Lambert absorption by thickness, in-scatter colour, sky Fresnel, shallow foam, caustics on the bed. Lava: drifting crust plates (cellular noise) over glowing cracks and pools, surface-gradient bump. Honey: thick amber |
| `prop.gdshader` | Generated props, items, creatures | Object-space triplanar of the props array, vertex-colour tint, sway |
| `foliage_card.gdshader` | Tree crowns and bushes (`card:<layer>` slots) | UV-mapped alpha-tested sprig cards, crown-shaped normals on both faces, sway + flutter, translucency, mip-aware alpha |
| `character.gdshader` | People (`char:<kind>` slots) | Skin (pores from the detail array, subsurface scattering, wrapped diffuse), eyes, Kajiya–Kay hair highlights along exported tangents, nails, cloth sheen, leather, metal, bone, glow. Detail is sampled in the rest pose rebuilt from the two UV sets, so it sticks to the animated body |
| `grass.gdshader` | Instanced grass and flowers | See below |
| `sprite_lit.gdshader` | Billboards and particles | Uses `strata_ambient` because it disables environment light |

## Grass
Mesh jobs scatter clumps over the upward faces of grass surfaces (`GrassScatter`, Core, unit-tested): density and
look per grass block from `grass.json`; placement hashed from world position (rebuilds regrow the same grass);
clumps thin out across material borders like the texture blend, stay off steep faces and out of blocks above;
light is interpolated from the mesh. The output is shuffled so any prefix is an even thinning of the chunk.

`GrassRenderer` keeps one MultiMesh of clumps (and one of flowers) per chunk inside the preset's grass distance:
near chunks draw the detailed clump (9 curved blades), far ones a 5-blade clump and a shrinking prefix; the shader
widens far blades so the cover stays even, sinks them into the ground at the distance limit, bends them with gusts
travelling downwind and parts them around the player (`strata_trample`). Grass casts no shadows.

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

## Global shader parameters (`project.godot`)
`strata_sky_ambient`, `strata_block_light_color`, `strata_daylight`, `strata_time`, `strata_light_dir`,
`strata_clouds`, `strata_cloud_noise`, `strata_wind`, `strata_trample`.
