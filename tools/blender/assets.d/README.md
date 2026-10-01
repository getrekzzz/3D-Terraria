# Drop-in asset specs

Every `*.json` file in this folder is merged into `tools/blender/assets.json` by `load_spec()` in
`tools/blender/asset_spec.py`, which `build_all.py` and `preview.py` read (`rename_to_terraria.py` rewrites these
files too). Add a file here (for example `p3-38-guns.json`, named after the work package or topic) instead of editing
`assets.json`, so parallel branches do not collide on one file.

* Same schema as `assets.json`, with only these keys: `comment`, `metals`, `objects`, `items.patterns`,
  `items.explicit` and `creatures`.

  ```json
  {
   "comment": "What this file adds and for which work package.",
   "items": {"explicit": {"<new_item_id>": {"generator": "gun", "params": {"style": "pistol"}}}},
   "creatures": {"<new_creature_id>": {"generator": "humanoid", "params": {"skin": "#6a9a4a", "height": 1.35}}}
  }
  ```
* Files merge in file-name order. An id defined in `assets.json` or an earlier file is an error; so is any other key.
* Check with `python3 tools/blender/build_all.py --check`; build with `--only=<ids>`.
* A new generator for these entries goes in its own `tools/blender/bgen/<module>.py`, registered with
  `@generator` from `bgen/__init__.py` (no edit to `build_all.py` needed; `@generator(view="item")` for a long
  weapon or tool, whose icon is laid diagonally).
