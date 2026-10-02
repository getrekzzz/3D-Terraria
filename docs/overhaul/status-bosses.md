# Bosses + Dungeon slice — status (`claude/ovh-bosses`)

## W7 — boss framework, early bosses, Skeletron (pushed)
WPs done: P4-01, P4-02, P4-03' (reduced: no HUD), P4-04 (+P3-32 treasure bags), P4-05, P4-06, P4-07, P4-08, P4-09,
P4-10, P4-11 (+Clothier Voodoo Doll item and use). Adversarial reviews: P4-01, P4-02/08/09, P4-10 (P4-06/07 in progress).
CI: full headless CI on the merged branch — file length, build, all Core tests, ore tiers, manifest, validate,
gen_reference, build_all --check, coverage, Godot import, all 14 smoke scripts SMOKE PASS, menu tour MENU PASS.

Hand-offs for other sessions:
* Core `NpcSystem.Messaged(text, MessageKind)` + `MessageColors` (boss purple #af4bff): every creature announcement
  plus "<Title> has awoken!" / "has been defeated!". Nothing in game/ listens yet — ChatLog (P2-21) should, and Hud
  can drop its own awoken/defeated lines then.
* Boss bar (P2-23): `BossPart.InBossBar`, `NpcSystem.GroupLife` / probe `boss.<id>.total` (Eater pieces); Skeletron's
  hands are not in the bar (Terraria). `Npc.VisualScale`, `Mirrored`, `Illusions` are drawn by NpcRenderer.
* `ICombatTarget` (P4-01) for P5-09; `BrainContext.TargetPlayer` for player-only effects.
* Rarity -12 (Expert rainbow) for bags/Expert items is not drawn: UiTheme belongs to the UI session.
* Camera kicks go through the existing `CameraShake` (BossEffects); swap to P3-25's camera punch when it lands.

Gaps: Deerclops has no screen-darkening insanity (HUD); illusions are darkened, not translucent; Desert Tiger flies
rather than pounces; Spiked Slime reuses the gel model; Demon Heart/Bone Helm Expert items come later (P4-25);
treasure bags drop one per kill (no per-player bags); Angry Bones variants reuse the skeleton model (helmet geometry
needs an --anims rebuild owned by the integration session).

## W8 — 1:1 Dungeon (pushed)
WPs done: P4-12, P4-13, P4-14, P4-15, P4-16, P4-17, P4-18, P4-19 (`bound_npc` site: `RoomKind.Mechanic` +
`MechanicCell`, a kept-clear 5x5x5 room documented in worldgen.md for P5-06b), P4-20 (+Clothier Voodoo Doll drop:
`clothier_death` table, `wearing red_hat`), P4-21, P4-33, P4-34, P4-23 (minus the bound Mechanic, which is P5-06b).
Merged onto Phase 1 terrain from origin/claude/clever-goodall-kgex45: GenVersion 10 → 11, golden hashes re-pinned
(dungeon halls, vault and keep chunks pinned). Adversarial review on the new relief: keep site picks the flattest
spot and cuts a terraced clearing (seeds 4/11 sat in hillsides); seeds 1–12 Tiny + 1–2 Small walk every room.
CI: full headless CI PASS (993 Core tests, all 14 smokes incl. the full dungeon route, menu tour).
Gaps: perf walk skipped (lean); dart traps only in flat halls; decor tuned from numbers (no render loop).

Reusable Blender helpers (for the World & life session): `tools/blender/bgen/dungeon_furniture.py` — every generator
takes `palette` (a `PALETTES` name or an inline dict: body/bodyTint, trim/trimTint, metal/metalTint, wax, books);
generators dungeon_door, dungeon_chair, dungeon_table, bookcase, candelabra, chandelier, floor_lamp, vase; helpers
`resolve_palette`, `plinth`, `candle_cup`, `flame_points`. `tools/blender/bgen/dungeon_decor.py` — `banner` (cloth,
trim, emblem colours; shape, hem, rod), `statue` (pose, stone, tint, base), `book`, `spike_strip`; helpers
`emblem_outline`, `hanging_cloth`.

## W9 — hardmode bosses + X-11 + P4-38: done on a lane branch, merged after the W8 push.
