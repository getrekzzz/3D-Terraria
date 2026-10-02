# Persistence

* `user://worlds/<name>/world.json` — metadata (seed, size, generator version, plan hash, clock, flags,
  per-save id tables, spawn, town residents, the world-mutation log) with a JSON migration chain
  (`SaveMigrations`, v0→v1). The mutation log (`mutations: [{id, seed}]`, `mutationLevel` = its length) lists the
  world changes that have happened (hardmode ores and wedges); older saves simply have an empty log.
* Region files hold 8³ chunks; only modified chunks are saved (`ChunkCodec`: sectioned palette + RLE +
  zlib with a format byte and the applied world-mutation level — a chunk behind the log catches up when it loads; tile objects with state, containers and
  pending loot). Unmodified chunks regenerate from the seed. An object record whose state byte has bit 4 is followed
  (after `PendingLoot`) by an object-meta list of `tag, length, payload` entries ending in tag 0: tag 1 is the chest's
  `CustomName`. Readers skip unknown tags and older chunks never set the bit, so later fields (chest locks) append a
  tag without a format bump.
* `user://players/<name>.player.json` — `PlayerSave` (format `strata-player` v1): inventory, equipment,
  buffs, permanent upgrades, deaths, bed spawn per world. Unknown item ids become placeholders that keep
  their string so re-saving never loses data. Slot lists (`backpack`, `armor`, `accessories`, and since the
  full slot model `coins`, `ammo`, `trash`, `vanityArmor`, `vanityAccessories`, `dyes`, `misc`, `miscDyes`,
  `service`) hold `{slot, item, count, prefix, favorite}` with `favorite` written only when true;
  `hiddenAccessories` is a bit mask. All are additive at v1: older files load with empty lists.
* All writes are atomic (temp file + rename) with a `.bak` of the previous version read as a fallback.
* Autosave every 2 minutes and on exit; chunks leaving the streaming range are saved when modified.
