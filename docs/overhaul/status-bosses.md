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

## W8 — 1:1 Dungeon (in progress)
Done on lane branches: P4-12 (blueprint op index, NoZone), P4-14 (layout planner; `RoomKind.Mechanic` +
`MechanicCell` is the reserved `bound_npc` site for P5-06b), P4-17 furniture, P4-18 decor/hazards, P4-20 dungeon loot
(+Clothier Voodoo Doll drop, red_hat), P4-13 wall panels, P4-33 biome chest weapons. In progress: P4-15/P4-16.
GenVersion: Phase 1 terrain was not on origin when W8 started, so P4-15 sets GenVersion 11 directly (10 reserved for
Phase 1); golden hashes are re-pinned again after merging Phase 1.

Reusable Blender helpers (for the World & life session): `tools/blender/bgen/dungeon_furniture.py` — every generator
takes `palette` (a `PALETTES` name or an inline dict: body/bodyTint, trim/trimTint, metal/metalTint, wax, books);
generators dungeon_door, dungeon_chair, dungeon_table, bookcase, candelabra, chandelier, floor_lamp, vase; helpers
`resolve_palette`, `plinth`, `candle_cup`, `flame_points`. `tools/blender/bgen/dungeon_decor.py` — `banner` (cloth,
trim, emblem colours; shape, hem, rod), `statue` (pose, stone, tint, base), `book`, `spike_strip`; helpers
`emblem_outline`, `hanging_cloth`.

## W9 — hardmode bosses (in progress on a lane branch). X-11 (mimics, Nymph, Wyvern) queued after W9.
