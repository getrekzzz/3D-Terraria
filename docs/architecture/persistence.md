# Persistence

* `user://worlds/<name>/world.json` — metadata (seed, size, generator version, plan hash, clock, flags,
  per-save id tables, spawn, town residents, the world-mutation log) with a JSON migration chain
  (`SaveMigrations`, v0→v1). The mutation log (`mutations: [{id, seed}]`, `mutationLevel` = its length) lists the
  world changes that have happened (hardmode ores and wedges); older saves simply have an empty log.
* Region files hold 8³ chunks; only modified chunks are saved (`ChunkCodec`: sectioned palette + RLE +
  zlib with a format byte and the applied world-mutation level — a chunk behind the log catches up when it loads; tile objects with state, containers and
  pending loot). Unmodified chunks regenerate from the seed.
* `user://players/<name>.player.json` — `PlayerSave` (format `strata-player` v1): inventory, equipment,
  buffs, permanent upgrades, deaths, bed spawn per world. Unknown item ids become placeholders that keep
  their string so re-saving never loses data.
* All writes are atomic (temp file + rename) with a `.bak` of the previous version read as a fallback.
* Autosave every 2 minutes and on exit; chunks leaving the streaming range are saved when modified. Both are
  asynchronous: `GameWorld.BeginAutosave` (timed as `tick.autosave`) only captures chunk snapshots, world state, player
  JSON and deep copies of changed maps (`ExploredMap.Dirty`/`Snapshot`); encoding, map compression and disk writes run
  on the save thread, and `Autosaved` fires on the main thread when the batch lands (a new autosave waits for the last
  one). Unloaded chunks are queued without blocking (`stream.unload_flush`). `SaveAll` (the `save` command, Save &
  Quit) and `Shutdown` (waits for a running autosave, saves, unloads, flushes) stay synchronous; `save async` runs
  the autosave path.
* Single writer: every world file (regions, `world.json`, and the player and map files written during play) goes through `WorldSaver`, one background thread (`strata-saver`). The main thread only captures
  `ChunkSnapshot`s (immutable voxel data + the encoded object section, `WorldSession.Capture`); the saver encodes them,
  publishes the blobs to `WorldSaveStore`'s region cache (copy-on-write, authoritative for this session, so loads
  never wait on disk), writes region files, then `world.json` with the current id tables (after the blobs, so every id
  a blob uses is in the table; unload-only batches rewrite it when encoding interned new ids), then extra files.
  Until a snapshot reaches the cache it stays readable through `WorldSaver.TryGetPending`, and `WorldSession.Produce`
  builds a chunk streamed back in from it. The newest capture of a chunk always wins (`Seq`); failed writes are
  logged and retried with the next batch. `WorldSession.SaveAll`/`SaveMetaOnly` stay synchronous (enqueue + flush).
