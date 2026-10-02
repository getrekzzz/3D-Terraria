# Rendering

Everything is drawn by Godot 4's Forward+ renderer with custom spatial shaders (`shaders/`). The look is set by
data: graphics presets (`data/config/graphics.json`), grass kinds (`data/config/grass.json`), biome fog and sky
tints (`data/biomes.json`) and the generated texture arrays.

## Light model
| Light | Source | How it reaches a surface |
|---|---|---|
| Sun / moon | `DirectionalLight3D`, colour and irradiance from `Atmosphere.cs` (CPU port of the sky's scattering) | Custom `light()`: GGX (height-correlated Smith, Schlick, Burley diffuse) in `voxel_brdf.gdshaderinc`, gated by the baked sky exposure (`strata_sun_gate`) and by cloud shadows (`clouds.gdshaderinc`) |
| Sky ambient and reflections | Godot environment, ambient = sky, reflections = sky radiance cubemap | Multiplied per surface by `AO = strata_sky_visibility(voxel light)`, so a sealed cave gets none; Godot also derives specular occlusion from it |
| Block light (torches, lava, glowing ores and fungi) | Baked RGB voxel light per vertex / instance | `EMISSION = strata_block_light(albedo, light, ao)`; its curve (`strata_block_curve`: level × mix(`linear`, 1, level) × `gain`, from `lighting.json` `blockLight` through the `strata_block_response` shader global) is much flatter than the sky's, so a torch's glow carries across a cave with a soft tail out to the 7.5 m edge of the baked light; surfaces in full daylight lose `daylitSuppression` of it |
| Nearest light sources | `DynamicLights`: a pool of shadowless `OmniLight3D` (size = preset `dynamicLights`: 4 on low, 8/16/24 above) given each ¼ s to the nearest placed lights, lava hotspots (`LavaEffects`) and glowing creatures (`NpcRenderer.Glows`, fireflies blink); ranking the candidates allocates nothing | Regular Godot lights: longer reach and directional shading on top of the baked glow; a placed torch's light reaches about 13 m (`lighting.json` `torch.placed*`: energy and range scale with the light's peak channel, each source has its own attenuation); a firefly swarm never costs more than the pool |
| Carried torch | `PlayerLight`: an `OmniLight3D` at the flame in the hand (first or third person), about 16 m reach, at least a placed torch's energy (`torch.held*`), a fire's flicker (shadows on high/ultra) | Regular Godot light through the same `light()` |
| Screen space | SSAO, SSIL (colour bleeding), SSR, volumetric fog with light shafts | Environment settings per preset; SSIL strength and the fog's albedo from `lighting.json` `screenSpace` |
| Eye adaptation | `EnvironmentController.Exposure.cs` steps `ExposureModel` (Core) each frame from the camera's unsmoothed sky exposure, the daylight and the eased biome `exposureBias`, and writes the camera's exposure multiplier when it moves by more than 0.002. Presets with `autoExposure` add Godot's metering, confined to `autoMin`–`autoMax` (`ExposureModel.AutoWindow`). Godot applies the multiplier to lights, sky and emission before it meters, so the metering key and window are scaled by the multiplier too: Godot meters the scene as if at exposure 1 and its correction multiplies with the model instead of undoing it | Scales all light before AgX tone mapping and before the glow threshold. The same on every preset: a cave opens the eye up to `caveBoost` at `brightenPerSecond`, leaving it closes down at `darkenPerSecond` (no flashbang), by day desert and snow are dimmed by their bias. Probe `render.exposure` |
| Glow | Environment glow from `lighting.json` `glow`: HDR `threshold`, `softness` (HDR scale), `intensity`, `luminanceCap`, normalized level weights; screen blend | Only light above the threshold after exposure blooms (flames, lava, magic, glowing ores); sunlit sand and snow stay under it |

`EnvironmentController(.Frame).cs` drives time of day: sun and moon arcs, atmosphere-coloured light, night
desaturation, fog (aerial perspective above ground, close biome-tinted gloom below; the gloom closes in at
`fog.closePerSecond` when the camera loses the sky and clears at the faster `fog.clearPerSecond`; deep in a cave it
also covers the sky background that shows past the streamed chunks, which the cave-adapted eye would see white),
cloud cover drifting with the weather and the ground wind (`strata_wind`) that grass and foliage share.

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
macro variation and rock on steep slopes, and lets the fog's aerial perspective melt it into the sky. Land albedo is
clamped to luminance 0.42 and its ambient occlusion to 0.8, so distant sand and snow never outshine the streamed
terrain; the fog starts within `fog.nearCap` metres (divided by the biome's fog density) on every preset. The preset's
`horizonDistance` (0 = off) also sets the fog reach and the camera's far plane. It samples fields, not voxels:
caves, structures, edits and hardmode wedges are not in it.

## Presets
`GraphicsConfig` (Core, validated with ranges, tested) → `GraphicsSettings` (game) applies them: render scale and
upscaler (FSR 1/2), anti-aliasing (FXAA/SMAA/TAA/MSAA), anisotropy, shadow atlas, distance and filter, sun size
(PCSS), torch shadows, SSAO/SSIL/SSR quality, volumetric fog grid, sky radiance size, cloud steps, auto exposure,
grass density and distance, mesh LOD threshold, horizon distance. `graphics <preset>` / `--graphics=` switch at runtime; the choice is
remembered in `user://graphics.cfg`. CI screenshots use `medium`; lavapipe QA uses `high`. Auto exposure (medium and
up) only fine-tunes within the narrow window from `lighting.json`; cave adaptation and the biome bias come from the
CPU model on every preset, so desert and snow read the same on low and ultra. The lighting tour
(`game/Testing/Tours/lighting.cmds`, HUD hidden with `hud off`, run with `--fixed-fps 10` so frames land at exact game
times however slowly lavapipe draws) shoots forest, desert, snow and a cave on each preset plus the frames 0.5 s and
2 s after the walls of a sealed room vanish in the desert (at a quarter time scale, so remeshing the room costs game
time as at 40 fps); `tools/ci/shot_stats.py --check` holds desert, snow and exit frames on medium, high and ultra to a
mean luma of 0.35–0.6 with under 2% clipped pixels. Its torch part builds a sealed 2.5 m stone corridor with a torch
at the player's feet and an end wall 1, 4, 6, 8 and 10 m ahead (`*_corridor_*`, plus the held torch alone at 8 m);
`shot_stats.py --ratio/--centre-min` compare the luma of the frame's middle tenth (the end wall): on medium the 8 m
wall keeps at least 20% of the 1 m wall and the 10 m wall 10%, the 6 m wall reads (0.25) on low and medium, and low
stays within 30% of medium. Further stops check that the flatter block-light curve does not wash out a lava pool in
the underworld, the glowing mushroom caverns or a sandstone room under the held torch, and that a torch by a sunlit
wall at noon stays a faint tint. Edits in tours are relative to the feet cell, so the tour centres the player in it
(`tp ~ ~ ~`, relative coordinates) once the colliders of a freshly built room are in.

Light tuning shared by every preset is `LightingConfig` (Core, `data/config/lighting.json`, every value required and
range-checked by `Strata.Tools validate`): eye adaptation, glow threshold/softness/levels, the fog near cap and
cave-gloom rates, SSIL and volumetric strengths, placed and held torch lights, and the block-light curve.
`ExposureModel` (Core, pure) gives the exposure the eye wants (`caveBoost` sealed from the sky; in the open
`nightBoost` at night and the biome's `exposureBias` by day, below 1 for desert, snow, sky, hallow and ocean) and
eases toward it, opening up slowly and closing down fast so leaving a cave never flashes. `ExposureModel.AutoWindow`
turns the `autoMin`–`autoMax` multiplier range into Godot's auto exposure ISO window (luminance = ISO × 12.5 / 100 /
100 at sensitivity 100). `GraphicsSettings` loads and validates the file at start (a bad value stops the game, like a
bad `graphics.json`); `EnvironmentController` takes exposure, glow, the fog section, SSIL and the volumetric albedo
from it, `DynamicLights` and `PlayerLight` the torch section, and `EnvironmentController` sets the
`strata_block_response` shader global (linear share, gain, daylit suppression) from the block-light section.

Frame-time budget (ultra aims at 60 fps at 1080p on an RX 6600 / RTX 2060-class GPU):
* Chunks sealed from the sky sit on their own visual layer (`ChunkLayers.Sealed`) that the sun's and moon's
  `ShadowCasterMask` skips: they never draw into the 4-cascade sun shadow map, yet torches still cast their shadows.
* Only one sky light casts shadows at a time (the moon waits for sunset).
* SSAO/SSIL can run at half resolution (`ssaoHalf`/`ssilHalf`); high and ultra do.
* `DynamicResolution` lowers the 3D resolution (FSR, down to 60%) only when the measured GPU time shows the GPU is the
  bottleneck and frames fall under ~57 fps, and raises it again when the GPU has headroom (works with vsync).
* F3 shows GPU and render-CPU milliseconds, the current 3D scale and the preset.

## Measuring hitches
Every lag fix is proven with the frame profiler (`Metrics.Frame`, Core) and its spike log (`SpikeLog`). Timer
scopes (`Metrics.Frame.Time("gd.colliders.build")`) cost nothing until profiling is on; while it is, each recorded
timer also adds into the current frame, and `game/Debug/FrameProbe.cs` (process priority `int.MaxValue`, added by
`WorldLauncher.Launch`) closes every frame with its real length from `Time.GetTicksUsec()` deltas. Frames of 33 ms or
more are kept (last 64) with their five most expensive timers and the gen0/1/2 collections and GC pause during them;
counters keep frames ≥33/≥50/≥100 ms, the worst frame and a frame-time histogram for p50/p95/p99 (0.5 ms steps to
100 ms, 10 ms steps to 10 s). A spike lists causes: `frame.delta`, `tick.total` and `gd.world_runtime` (enclosing
totals) are left out, as is a parent timer whose dotted child took all but 1 ms of it. On a software renderer
(llvmpipe/lavapipe draws a frame in seconds, so every frame would be a spike) `FrameProbe` measures from the end of
the last draw instead, i.e. the main thread's own work, and the summary says "frames (main thread, draw excluded)".
* **F3** turns profiling on while it is open (a session `perf start` began, before or while it was open, keeps
  running when it closes) and adds
  `spikes >33 ms N (>50 M, worst X) of F frames · p99 … · GC a/b/c pause y ms` and `last: 72.0 ms: tick.autosave 40.1,
  gd.colliders.build 12.0 · GC gen2 pause 8.0 ms`.
* **Console**: `perf start` (reset, on), `perf stop`, `perf report [n]` (spike/GC summary, then the costliest timers
  per frame or tick with average, worst and call count), `perf spikes [n]` (summary, then the last n spikes, newest
  first).
* **Probes** (`Commands/PerfProbes.cs`): `perf.frames`, `perf.frames_over_33|50|100`, `perf.worst_ms`, `perf.p99_ms`,
  `perf.gc0|1|2`, `perf.gc_pause_ms`, and per timer `perf.max.<timer>` (longest call, ms), `perf.avg.<timer>`,
  `perf.calls.<timer>` and `perf.max_calls.<timer>` (most calls in one frame); a timer that never ran reads 0.
* Suspected sources carry their own timers: `gd.colliders.build`, `gd.objects.rebuild`, `gd.grass.create`,
  `gd.npcs.create` (a creature-model pool miss), `gd.projectiles.create`, `tick.autosave`, `stream.unload_flush`,
  `gd.dynres.change`.
* **`game/Testing/Scripts/perf.cmds`** drives the usual hitch sources: three far teleports, a save, ten torches, flowing
  water, twenty spell casts, eight creatures, then `perf report 40` and `perf spikes 10`, and finally the cost of one
  block edit from a fresh profile. CI runs it headless with every other script, where frame times are CPU-only and
  noisy, so it asserts only deterministic counters (frames closed, work that must have happened, `errors == 0`);
  later work packages add structural bounds (rebuilds per edit, gen2 collections during travel). Timing evidence
  comes from a rendering run, on a dev GPU (whole frames) or lavapipe (main-thread work, see above):
  `godot --path . --rendering-driver vulkan -- --world=perf --size=small --new --player=perf --fresh-player --view=6
  --graphics=medium --smoke=res://game/Testing/Scripts/perf.cmds`.
* **Before/after**: run the same rendering command on the parent commit and on the fix, on the same machine, and
  compare the `perf spikes` summary (p99, max, spike counts, GC) and which timers still appear in the spike list.
  Lavapipe timings only compare with other lavapipe runs (they also share the CPU with the software rasteriser, and
  physics runs its 8-step catch-up every frame). The W0 baseline, before any lag fix, is `docs/perf/perf-before.txt`.

## Global shader parameters (`project.godot`)
`strata_sky_ambient`, `strata_block_light_color`, `strata_daylight`, `strata_time`, `strata_camera`, `strata_light_dir`,
`strata_clouds`, `strata_cloud_noise`, `strata_wind`, `strata_trample`, `strata_weather` (wetness, puddles, lightning
flash, snow; set by `WeatherRenderer`).

## Effects (EffectKit, FlashLights)
`EffectKit` (game/Rendering) is the one particle path for hits, mining and deaths: per recipe (Chips, Dust, Sparks,
Puff, Splash, Leaves) a pool of one-shot CPU emitters sharing `particle_lit.gdshader`, each burst lit by the voxel light
at its point (instance uniform `voxel_light`, so chips in a dark cave are dark) and varied between a lighter and darker
shade. Counts × preset `effectsScale`; at most 8 bursts start per frame and about 600 particles live (the oldest emitter
is reused). `FlashLights` is a pool of `flashLights` short omni lights that fade linearly (explosions); a separate pool
because DynamicLights re-ranks its providers only a few times a second. Creature materials
(data/config/creature_materials.json, Core `CreatureMaterialsConfig`: the enemy's `material`, else its model, else its
tags, else flesh) pick each creature's hit and death sounds, hit dust (blood, gel, bone chips, sparks, leaves) and death
gore (`GoreSystem`: up to 16–96 pooled chunks by preset, simulated against the voxels, voxel-lit through MultiMesh custom
data, faded after 2.5 s); `DamageEvent` carries `NpcId` and `Kind` (Hit, Periodic, Player…) so damage over time stays quiet. SoundEvents is split into World, Weapons,
Creatures and Player partials, each wired once.

## Weather and ambience
`WeatherRenderer` (game/Ambience) draws rain streaks, splashes, snow and blowing sand as GPU particles
(`weather_particles.gdshader` + `weather_drop.gdshader`) in a box that follows the camera. `SkyHeights` uploads the sky
map around the camera as a 96×96 float texture (refreshed on a 4-cell move or twice a second); particles die below it
and splashes are born on it, so rain stops at roofs and cave ceilings with no raycasts. Counts are the preset budget
(`weather.json`) × the weather's strength via `amount_ratio` (no restarts); emitters stop when nothing falls. Wetness
(`weather.gdshaderinc`) darkens and glosses sky-exposed terrain and pools puddles on flat tops. The weather feeds
`EnvironmentController.Weather.cs`: overcast cover dims the sun, the wind drives clouds/grass/foliage/flames, sandstorms
and blizzards pull the fog into a tinted haze, and lightning pulses a shadowless light and the exposure.
`AmbienceDirector` picks sound beds and ambient particles by biome, layer and time from `data/config/ambience.json`
(`AmbienceConfig.Select` in Core): loops (`tools/audio/ambience*.py`) fade on the Ambient bus or the Outdoors bus,
which `AudioEnvironment` low-passes as the sky is lost; world sounds on the Sound bus get a reverb by enclosure and
depth. `AmbientParticles` keeps one pooled GPU emitter per kind (dust, spores, embers, leaves, fireflies; counts ×
preset scale) and `AmbientDrips` drips water from real ceilings near the camera.

Look: AgX tonemapping with colour adjustment (saturation 1.22 by day, 0.85 at night; contrast 1.06) for the reference
game's rich palette over realistic materials; glow in Screen blend so HDR emitters (flames, lava, glints, magic) bloom
without hazing the image; a moonlit night keeps a blue ambient floor so shapes and creatures stay readable.

## Creature animation
Generated humans (the player, town folk, humanoid enemies) play their shared animation set through a
`HumanoidAnimator` (an `AnimationTree` built in code): a cross-faded locomotion state (idle, walk, run, sprint,
strafe_left, strafe_right, jump, fall, land, swim, climb, glide, death; gait and playback rate chosen from ground speed
so feet do not slide, negative when backpedalling), an upper-body action blend for item actions, a right-arm blend
that holds a carried torch up, and a hurt flinch. Each use animation (a new `PlayerEntity.UseSerial`; Core never
restarts one mid-arc) plays its clip once, stretched to the use after speed bonuses (so you can swing while running):
an `AnimationNodeTimeSeek` under the clip's time scale seeks it to its start, or, when the use is quicker than
`maxClipRate` times the clip's authored speed, past the wind-up to the clip's `strikeStart`, and the rest plays over
the use, so the strike lands inside it and the clip still ends with it. The action is a plain `Blend2` whose weight
`HumanoidAnimator` eases (in over 0.05 s, out over 0.16 s): it stays fully in while the simulation's use runs
(`Sustain`; the simulation can fall behind real time) and for 0.08 s (and two frames) after, so a chained autoswing
or chop starts from the pose the last one ended in instead of dipping toward the locomotion pose (probe
`player.action_dips` counts chained uses that did dip; creatures' actions are timed alone). The swing, chop and thrust start and end in
their raised or ready pose; their strike windows and the bow's release match `data/config/use_styles.json`. The tree blends each bone by the
shortest turn from its rest pose, so the action clips keep the upper arm short of straight up wherever they can be
faded (an arm past it would flip round for a frame) and give every body, undead included, a living person's arms.
In first person the `ViewModel` poses the held item on the same timing: the use's phase is
`(UseAnimationMax − UseAnimation + physics interpolation fraction) / UseAnimationMax`, so it moves every frame between
ticks at any frame rate and never runs backwards. Blades and tools rise from half raised to over the shoulder
through the wind-up, come down and across by `UseTiming.Sweep` in the strike window, carry on (a blade) or bounce
off the block (a tool), and return to half raised for the next use; thrusts pull back, lunge in the strike window and
hold through the follow-through. An engage weight eases the item out of its rest pose (0.06 s) and back (0.15 s), and a
new use while engaged crossfades from the pose on screen (0.05 s). The `SwingTrail` arc is drawn from the strike start
only, its head at the sweep the melee system tests hits against (the same eased window, the prefix-sized reach), so the
damage number appears as the arc passes; it fades after the follow-through and takes the swing's `trailColor` when the
item sets one.

Ranged weapons are held aimed in first person (`ViewModel.Ranged`), in use or not. Their poses are in an aim frame
(the view yawed and pitched from the item's rest position to the crosshair point at `AimDistance`, kept within
2–60 m), so a barrel or an arrow laid along −Z points at what the crosshair is on. A bow stands upright right of
centre, its back to the target, with an arrow on the string: the first arrows in the backpack (`ItemUseSystem.FindAmmo`,
looked up four times a second and at once when a bow is taken out, in either view, so the model is loaded before the
first shot). The arrow is gone from the bow clip's `release` to its `redrawStart` while the bow jolts 3 cm forward, then
is laid on ahead of the string and slid back onto it by `redrawEnd`, so auto-fire keeps the arrow in step with the
shots. Guns point along the view and kick 6° up and 4 cm back on every shot (each `ItemUse.Used` with a `Shot`, so a
burst kicks per round), settling to about 5 % in 0.12 s. Staffs, wands and tomes lean their tip toward the crosshair and
push toward it on a cast. In third person `PlayerAvatar.HeldPose` grips a bow by its riser, upright with its back on
the aim while it is drawn (set 23° forward in the fist against the bow clip's raised hand); everything else keeps the
point-forward grip. Probes `fp.arrow`, `fp.aim_error_deg`, `player.held_tilt_deg` and `player.held_aim_deg` report the
result.

The strafe clips run `HumanoidAnimator.StrafeAngle` (45°) to the side of the hips: with the legs turned that far
toward the movement, the feet travel straight sideways.

The player's body always faces the crosshair (`PlayerAvatar.Motion`, rule in Core `Presentation/BodyFacing`): the
chest, arms and head face the camera's yaw (`CameraRig.Yaw`), and the legs (the model's yaw) turn toward the movement
by at most `BodyFacing.MaxTwist` (70°) and pick the stride that carries the feet along it: the forward gait within
22.5° of the facing, `strafe_left`/`strafe_right` (playback rate `HumanoidAnimator.StrafeRate`, run's ground per
cycle) out to 112.5°, the gait played backwards beyond; a playing stride keeps its band 8° longer so a move along an
edge does not flicker. W+A runs the strafe clip with the hips square, A or D turns the legs 45°, S backpedals. Standing,
in the air, in water, on a rope or in a minecart the legs ease onto the facing (12/s), and however fast the camera
turns the legs are dragged to within the twist limit, so the chest never leaves the crosshair. An `AimModifier`
(`SkeletonModifier3D`) then twists the torso back onto the facing (the spine and chest share the twist, so the
shoulders, arms and head face it exactly), bends the spine and head to the aim's pitch, and brings the arms up to the
aim for guns, bows, staffs and spears. Probes `player.facing_error_deg`, `player.twist_deg` and `player.stride`
report the result.

`NpcRenderer.Motion` adds presence to everything else: `facing` look (eyes stare at their target) or velocity (fish),
fliers banking into turns and pitching with their climb, gels squashing, a quiver before a charge, a stretch along a
dash, a shrink on teleport, a spin when a boss changes form; humanoid enemies swing or shoot when they attack, flinch
when hit and fall dead (corpses sink after a moment, other creatures shrink away). Models built around their centre
are raised onto their feet by their bounds (`CreatureVisual.LiftFor`, only for centred, box-sized models). `NpcRenderer.Arms` stretches a boss's `armModel`
from its shoulders to each free-flying hand with an outward elbow.

