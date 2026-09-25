# Persistence

* `user://worlds/<name>/world.json` — metadata (seed, size, generator version, plan hash, clock, flags,
  per-save id tables, spawn) with a JSON migration chain (`SaveMigrations`, v0→v1).
* Region files hold 8³ chunks; only modified chunks are saved (`ChunkCodec`: sectioned palette + RLE +
  zlib with a format byte and the applied world-mutation level; tile objects with state, containers and
  pending loot). Unmodified chunks regenerate from the seed.
* `user://players/<name>.player.json` — `PlayerSave` (format `strata-player` v1): inventory, equipment,
  buffs, permanent upgrades, deaths, bed spawn per world. Unknown item ids become placeholders that keep
  their string so re-saving never loses data.
* All writes are atomic (temp file + rename) with a `.bak` of the previous version read as a fallback.
* Autosave every 2 minutes and on exit; chunks leaving the streaming range are saved when modified.
