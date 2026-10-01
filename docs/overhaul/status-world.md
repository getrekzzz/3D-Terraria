# World & life slice — status (`claude/ovh-world`)

Owner: World & life session. Order: X-01 → X-08 (+P3-49) → X-06 → X-13 → X-15 → X-14 → X-03 → X-07.

## Push 1 — weather and ambience
| WP | State | Notes |
|---|---|---|
| X-01 weather | done | Core `Gameplay/Weather` (deterministic hourly rolls per seed, rain/thunderstorm/windy/sandstorm, snow and blizzards over snow biomes, nothing underground), `weather` condition + `ConditionContext.Weather/Windy`, mirrored `phase: "weather"` events (rain/thunderstorm/windy_day/sandstorm), saved in `WorldMeta.Weather`. Engine: GPU rain/snow/sand/splash particles in a camera box cut off by the sky map (`SkyHeights` texture), per-preset budgets in `data/config/weather.json`, terrain wetness + puddles, overcast sky, wind on clouds/grass/foliage/flames, sandstorm/blizzard haze, lightning + thunder, weather loops on the new `Outdoors` bus. |
| X-08 ambience (+P3-49) | done | `data/config/ambience.json` + Core `AmbienceConfig.Select` (tested): 16 synthesised beds (birds, crickets, frogs, surf, wind, caves, cavern, lava, mushroom, evil, hallow, dungeon, sky, hive); cave reverb and outdoor low-pass by enclosure/depth (`AudioEnvironment`); pooled GPU dust/spores/embers/leaves (from real nearby tree canopies)/fireflies per preset; cave drips from real ceilings; music variants (rain, town day/night, night versions, underground jungle/ice/blight/rot/radiant, space); fishing bite sound. |

CI: see the latest push section below.

### Gaps / notes for integration
* The boss-defeat music stinger is left to X-02 (integration owns "victory sting"); `boss_defeated` SFX still plays.
* Props (furniture, trees) do not get wet — only terrain (prop_common is shared with the art session); glass and
  other light-passing blocks do not stop rain (the sky map counts only light-blocking cells).
* One combined commit covers X-01 engine + X-08 (shared audio/particle infrastructure).
* New probes: `weather.*` (kind, local, intensity, wind, windy, sandstorm, particles, budget, wetness, strikes),
  `ambience.loops`, `ambience.particles`, `audio.reverb`, `audio.muffle`; new smoke script `weather.cmds`; tour
  `Tours/weather.cmds`.
* `MusicDirector.cs` got two small hooks (weather events are skipped as event music; `Variant`/`LayerVariant` from
  the new `MusicDirector.Variants.cs`); `PerfOverlay.cs` one F3 line; `WorldRuntime` gained `Weather`/`Ambience`.
