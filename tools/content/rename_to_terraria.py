#!/usr/bin/env python3
"""One-off migration to the reference game's names (see terraria_names.py).

Renames ids and model names (whole tokens inside quoted strings) across content data, asset definitions, the asset
manifest, test/smoke scripts and C# string literals; renames asset files (with their .import side files); sets display
names; rewrites free text; writes data/config/aliases.json so older saves still load.

Never touched: the reference DB (data/reference, "covers" lists), texture layer names ("textures" objects and
namespaced "prop:"/"card:"/"char:" strings) and Blender generator function names ("generator").

  python3 tools/content/rename_to_terraria.py            # dry run: report what would change
  python3 tools/content/rename_to_terraria.py --apply
"""
import json
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
from terraria_names import BRAINS, IDS, NAMES, TEXT  # noqa: E402

TOKEN = re.compile(r"(?<![A-Za-z0-9_])([a-z][a-z0-9_]*)(?![A-Za-z0-9_])")
STRING = re.compile(r'"((?:[^"\\]|\\.)*)"')
MASKS = [
    re.compile(r'"textures"\s*:\s*\{[^}]*\}', re.S),
    re.compile(r'"covers"\s*:\s*(\[[^\]]*\]|\{[^}]*\})', re.S),
    re.compile(r'"generator"\s*:\s*"[^"]*"'),
    re.compile(r'"(music|sound|layer|material|texture)"\s*:\s*"[^"]*"'),
]
BRAIN = re.compile(r'("brain"\s*:\s*\{\s*"type"\s*:\s*")(\w+)(")')


METALS = {"umbrite", "sanguite", "cinder", "cerulite", "rosalite", "verdite", "orchidite", "crimsite", "stellite"}
PREFIXES = sorted([k for k in IDS if "_" in k or k in METALS], key=len, reverse=True)


def rename_token(tok: str) -> str:
    """Whole ids; model variants (<id>_v2); metal families and multi-word ids used as a prefix (cinder_bar,
    gelatin_crown_platinum). Single-word ids never match inside other words (sprite_lit is a shader)."""
    if tok in IDS:
        return IDS[tok]
    m = re.match(r"^(.*)(_v\d+)$", tok)
    if m and m.group(1) in IDS:
        return rename_token(m.group(1)) + m.group(2)
    for key in PREFIXES:
        if tok.startswith(key + "_"):
            return IDS[key] + tok[len(key):]
    return tok


def rename_in_string(s: str) -> str:
    if ":" in s and s.split(":", 1)[0] in ("terraria", "prop", "card", "char", "key", "mouse", "res", "user", "http", "https"):
        return s
    return TOKEN.sub(lambda m: rename_token(m.group(1)), s)


def masked_spans(text: str):
    spans = []
    for rx in MASKS:
        spans += [(m.start(), m.end()) for m in rx.finditer(text)]
    return sorted(spans)


def rename_json_text(text: str) -> str:
    text = BRAIN.sub(lambda m: m.group(1) + BRAINS.get(m.group(2), m.group(2)) + m.group(3), text)
    spans = masked_spans(text)
    out, last = [], 0
    for m in STRING.finditer(text):
        if any(a <= m.start() < b for a, b in spans):
            continue
        new = rename_in_string(m.group(1))
        if new != m.group(1):
            out.append(text[last:m.start(1)])
            out.append(new)
            last = m.end(1)
    out.append(text[last:])
    return "".join(out)


NAME_FIELD = re.compile(r'("id"\s*:\s*"([a-z0-9_]+)"(?:(?!"id"\s*:).)*?"(?:name|title)"\s*:\s*")((?:[^"\\]|\\.)*)(")', re.S)
TITLE_FIELD = re.compile(r'("flag"\s*:\s*"([a-z0-9_]+)"[^}]*?)|("title"\s*:\s*")((?:[^"\\]|\\.)*)(")')


def set_names(text: str) -> str:
    """Display names: the "name" (or "title") following each "id" in the same entry."""
    def fix(m):
        new = NAMES.get(m.group(2))
        return m.group(1) + (new if new is not None else m.group(3)) + m.group(4)
    return NAME_FIELD.sub(fix, text)


FREE_TEXT_FIELDS = re.compile(r'("(?:description|failMessage|announce|text|lines|tooltip|help|message|name|title)"\s*:\s*)(\[[^\]]*\]|"(?:[^"\\]|\\.)*")', re.S)


def rewrite_text(text: str) -> str:
    def fix(m):
        body = m.group(2)
        for old, new in TEXT:
            body = re.sub(rf"(?<![A-Za-z]){re.escape(old)}(?![a-z])", new, body)
        return m.group(1) + body
    return FREE_TEXT_FIELDS.sub(fix, text)


CS_STRING = re.compile(r'(?<![@$])"((?:[^"\\\n]|\\.)*)"')


def rename_cs(text: str, path: pathlib.Path) -> str:
    table = BRAINS if path.name == "BrainSpecs.cs" else None

    def fix(m):
        s = m.group(1)
        if table is not None:
            return '"' + table.get(s, s) + '"'
        return '"' + rename_in_string(s) + '"'
    return CS_STRING.sub(fix, text)


def rename_cmds(text: str) -> str:
    lines = []
    for line in text.splitlines(keepends=True):
        if line.lstrip().startswith("#"):
            lines.append(line)
        else:
            lines.append(TOKEN.sub(lambda m: rename_token(m.group(1)), line))
    return "".join(lines)


def asset_renames():
    """(old path, new path) for model and icon files whose base name contains a renamed token."""
    out = []
    for folder in ("assets/models/objects", "assets/models/items", "assets/models/creatures", "assets/icons/items"):
        for f in sorted((REPO / folder).glob("*")):
            if f.suffix not in (".glb", ".png"):
                continue
            stem = f.stem
            m = re.match(r"^(.*?)(_v\d+)?$", stem)
            base, variant = m.group(1), m.group(2) or ""
            new_base = rename_token(base)
            if new_base != base:
                out.append((f, f.with_name(new_base + variant + f.suffix)))
    return out


def files():
    data = [p for p in (REPO / "data").rglob("*.json") if "reference" not in p.parts]
    extra = [REPO / "tools/blender/assets.json", REPO / "tools/content/ore_tiers.json", REPO / "assets/manifest.json"]
    cs = [p for d in ("src", "game", "tests", "tools/Strata.Tools") for p in (REPO / d).rglob("*.cs")
          if "ThirdParty" not in p.parts and "obj" not in p.parts and "bin" not in p.parts]
    cmds = list((REPO / "game/Testing").rglob("*.cmds"))
    return data + extra, cs, cmds


def main() -> int:
    apply = "--apply" in sys.argv
    changed = 0
    json_files, cs_files, cmd_files = files()
    for p in json_files:
        text = p.read_text(encoding="utf-8")
        new = rename_json_text(text)
        if p.parent.name != "reference" and "assets" not in p.parts:
            new = rewrite_text(set_names(new))
        if new != text:
            changed += 1
            report(p, text, new)
            if apply:
                p.write_text(new, encoding="utf-8")
    for p in cs_files:
        text = p.read_text(encoding="utf-8")
        new = rename_cs(text, p)
        if new != text:
            changed += 1
            report(p, text, new)
            if apply:
                p.write_text(new, encoding="utf-8")
    for p in cmd_files:
        text = p.read_text(encoding="utf-8")
        new = rename_cmds(text)
        if new != text:
            changed += 1
            report(p, text, new)
            if apply:
                p.write_text(new, encoding="utf-8")
    moves = asset_renames()
    for old, new in moves:
        print(f"  move {old.relative_to(REPO)} -> {new.name}")
        if apply:
            old.rename(new)
            imp = old.with_name(old.name + ".import")
            if imp.exists():
                text = imp.read_text(encoding="utf-8").replace(old.name, new.name)
                new.with_name(new.name + ".import").write_text(text, encoding="utf-8")
                imp.unlink()
    aliases = {old: new for old, new in IDS.items() if old != new}
    alias_doc = {"comment": "Content ids renamed to the reference game's names (tools/content/terraria_names.py). Saves "
                            "written before the rename resolve old ids through this table.", "aliases": aliases}
    if apply:
        (REPO / "data/config/aliases.json").write_text(json.dumps(alias_doc, indent=2) + "\n", encoding="utf-8")
    print(f"{changed} files, {len(moves)} asset files, {len(aliases)} aliases{'' if apply else ' (dry run)'}")
    return 0


def report(path, old, new):
    old_lines, new_lines = old.splitlines(), new.splitlines()
    diffs = [(a, b) for a, b in zip(old_lines, new_lines) if a != b]
    print(f"--- {path.relative_to(REPO)} ({len(diffs)} lines)")
    if "--verbose" in sys.argv:
        for a, b in diffs[:400]:
            print(f"   - {a.strip()[:150]}\n   + {b.strip()[:150]}")


if __name__ == "__main__":
    sys.exit(main())
