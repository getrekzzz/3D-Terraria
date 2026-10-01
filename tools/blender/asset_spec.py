"""The Blender asset spec: tools/blender/assets.json merged with every drop-in file tools/blender/assets.d/*.json.

A drop-in file has the same schema as assets.json and may add entries to "metals", "objects", "creatures", "projectiles",
"items.patterns" and "items.explicit" (plus a free-form "comment"). Files merge in file-name order; an id that is
already defined (in assets.json or an earlier drop-in) is an error, as is any other key. Every tool that reads the
spec goes through load_spec() so they all see the same set; plain Python, no Blender needed.
"""
import json
import pathlib

HERE = pathlib.Path(__file__).resolve().parent
BASE = HERE / "assets.json"
DROP_IN = HERE / "assets.d"

# Id maps a drop-in file may add to, as key paths into the spec.
SECTIONS = (("metals",), ("objects",), ("items", "patterns"), ("items", "explicit"), ("creatures",), ("projectiles",))


class SpecError(ValueError):
    """A drop-in file that does not fit the spec (unknown key, wrong type or duplicate id)."""


def spec_files():
    """assets.json, then the drop-in files in merge order."""
    return [BASE, *sorted(DROP_IN.glob("*.json"), key=lambda p: p.name)]


def load_spec():
    spec = json.loads(BASE.read_text(encoding="utf-8"))
    owner = {(section, key): BASE.name for section in SECTIONS for key in _section(spec, section)}
    for path in spec_files()[1:]:
        name = f"assets.d/{path.name}"
        try:
            extra = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            raise SpecError(f"{name}: {e}") from e
        _check_keys(extra, name)
        for section in SECTIONS:
            entries = _lookup(extra, section)
            if entries is None:
                continue
            if not isinstance(entries, dict):
                raise SpecError(f"{name}: '{'.'.join(section)}' must be an object of id -> entry")
            target = _section(spec, section)
            for key, entry in entries.items():
                if (section, key) in owner:
                    raise SpecError(f"{name}: {'.'.join(section)} '{key}' is already defined in {owner[section, key]}")
                owner[section, key] = name
                target[key] = entry
    return spec


def _check_keys(extra, name):
    if not isinstance(extra, dict):
        raise SpecError(f"{name}: expected a JSON object")
    allowed = {"comment", *(s[0] for s in SECTIONS)}
    unknown = sorted(set(extra) - allowed)
    items = extra.get("items", {})
    if not isinstance(items, dict):
        raise SpecError(f"{name}: 'items' must be an object with 'patterns' and/or 'explicit'")
    unknown += sorted(f"items.{k}" for k in set(items) - {s[1] for s in SECTIONS if len(s) == 2})
    if unknown:
        raise SpecError(f"{name}: unknown key(s) {', '.join(unknown)} (allowed: comment, {', '.join('.'.join(s) for s in SECTIONS)})")


def _lookup(doc, section):
    for key in section:
        if not isinstance(doc, dict) or key not in doc:
            return None
        doc = doc[key]
    return doc


def _section(spec, section):
    doc = spec
    for key in section:
        doc = doc.setdefault(key, {})
    return doc
