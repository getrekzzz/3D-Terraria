# Art slice status (`claude/ovh-art`)

| Push | WPs | CI |
|---|---|---|
| 1 | P3-05 held-item sockets | full headless CI green (416 Core tests, all checks, 14/14 smoke, MENU PASS) |

## P3-05 — held-item sockets (done)
* Every held item GLB carries named empties next to its mesh: `grip` (all held items), `tip` (swords, tools, staves,
  wands, rods, spears…), `muzzle` (guns), `nock` + riser `grip` (bows), `string` (yoyos), `chain` (flails),
  `handle_end` (whips). `assets/manifest.json` lists them per item as `"x y z > forward"` (Y-up metres).
* Game: `PropLibrary.Sockets(path)` / `Socket(path, name)` / `Bounds(path)` (mesh-relative `Transform3D`; basis -Z is
  the socket's forward, +Y its up), cached at model load. Probe `sockets.<item>`.
* Projectile models: `assets.d/p3-05-projectiles.json` (`*_head` yoyo heads, `*_ball` flail balls, centred on the
  origin with a `string`/`chain` socket). Projectile defs point at them from P3-12/P3-13 on (`"model": "wood_yoyo_head"`).
* Gap: torches/candles held in hand reuse tile-object GLBs, which have no sockets (the flame is placed by `Flames`).
