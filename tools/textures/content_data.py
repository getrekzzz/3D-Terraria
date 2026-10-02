"""Reads the game's content JSON (comments and trailing commas allowed) without third-party modules."""
import json
import pathlib
import re

REPO = pathlib.Path(__file__).resolve().parents[2]


def load_json(path):
    text = re.sub(r"^\s*//.*$", "", path.read_text(encoding="utf-8"), flags=re.M)
    return json.loads(re.sub(r",(\s*[}\]])", r"\1", text))


def content(category):
    """Every definition of a category ("blocks", "buffs", "events"…) in manifest load order."""
    manifest = load_json(REPO / "data" / "manifest.json")
    defs = []
    for rel in manifest["files"]:
        defs.extend(load_json(REPO / "data" / rel).get(category, []))
    return defs
