"""Generates the game's audio: sound effects (assets/audio/sfx/*.wav), the looping soundtrack (assets/audio/music/*.ogg)
and the looping biome ambience (assets/audio/ambient/*.ogg).

Needs numpy, scipy and soundfile (run with the Blender venv's python):
  /root/bpyenv/bin/python tools/audio/build_audio.py [--only=a,b*] [--sfx] [--music] [--ambient]
The output is deterministic (fixed seeds), so rebuilding only changes what the recipes change.
"""
import fnmatch
import pathlib
import sys

import numpy as np
import soundfile as sf

sys.path.insert(0, str(pathlib.Path(__file__).parent))
import ambience  # noqa: E402
import composer  # noqa: E402
import sfx_recipes  # noqa: E402
from music_tracks import TRACKS  # noqa: E402
from synth import SR  # noqa: E402

REPO = pathlib.Path(__file__).resolve().parents[2]
SFX_DIR = REPO / "assets" / "audio" / "sfx"
MUSIC_DIR = REPO / "assets" / "audio" / "music"
AMBIENT_DIR = REPO / "assets" / "audio" / "ambient"


def check():
    """Every sound the game code names (Sfx.Play / Sfx.PlayAt, and the material families) has a recipe, and every
    ambience a biome names has a loop."""
    import re
    names = set()
    for path in (REPO / "game").rglob("*.cs"):
        names.update(re.findall(r'Sfx\.Play(?:At)?\("([a-z0-9_]+)"', path.read_text(encoding="utf-8")))
    missing = sorted(n for n in names if n not in sfx_recipes.RECIPES)
    for n in missing:
        print(f"ERROR: game plays sound '{n}' but tools/audio/sfx_recipes.py has no recipe for it")
    import json
    biomes = json.loads((REPO / "data" / "biomes.json").read_text(encoding="utf-8"))["biomes"]
    beds = {b["ambience"] for b in biomes if b.get("ambience")}
    unknown = sorted(b for b in beds if b not in ambience.BEDS)
    for b in unknown:
        print(f"ERROR: data/biomes.json names ambience '{b}' but tools/audio/ambience.py has no loop for it")
    tracks = sorted(TRACKS)
    print(f"{len(names)} sounds referenced, {len(sfx_recipes.RECIPES)} recipes, {len(tracks)} music tracks, {len(ambience.BEDS)} ambience loops")
    return 1 if missing or unknown else 0


def main(argv):
    if "--check" in argv:
        raise SystemExit(check())
    only = next((a.split("=", 1)[1].split(",") for a in argv if a.startswith("--only=")), None)
    want = (lambda i: any(fnmatch.fnmatch(i, p) for p in only)) if only else (lambda i: True)
    picked = {k for k in ("--sfx", "--music", "--ambient") if k in argv} or {"--sfx", "--music", "--ambient"}
    do_sfx, do_music, do_ambient = "--sfx" in picked, "--music" in picked, "--ambient" in picked
    if do_sfx:
        SFX_DIR.mkdir(parents=True, exist_ok=True)
        count = 0
        for sid, (_, variants) in sorted(sfx_recipes.RECIPES.items()):
            if not want(sid):
                continue
            for v in range(variants):
                x = sfx_recipes.render(sid, v, seed=hash_id(sid) + v * 7919)
                name = sid if variants == 1 else f"{sid}_{v + 1}"
                sf.write(SFX_DIR / f"{name}.wav", x, sfx_recipes.SFX_SR, subtype="PCM_16")
                count += 1
        print(f"wrote {count} sound effects to {SFX_DIR.relative_to(REPO)}")
    if do_music:
        MUSIC_DIR.mkdir(parents=True, exist_ok=True)
        for name, spec in TRACKS.items():
            if not want(name):
                continue
            x = composer.build(spec, seed=hash_id(name))
            sf.write(MUSIC_DIR / f"{name}.ogg", x, SR, format="OGG", subtype="VORBIS")
            print(f"music {name}: {len(x) / SR:.1f}s")
    if do_ambient:
        AMBIENT_DIR.mkdir(parents=True, exist_ok=True)
        for name in ambience.BEDS:
            if not want(name):
                continue
            x = ambience.build(name, seed=hash_id(name))
            sf.write(AMBIENT_DIR / f"{name}.ogg", x, SR, format="OGG", subtype="VORBIS")
            print(f"ambience {name}: {len(x) / SR:.1f}s")


def hash_id(text):
    h = 2166136261
    for ch in text.encode():
        h = ((h ^ ch) * 16777619) & 0xFFFFFFFF
    return h


if __name__ == "__main__":
    main(sys.argv[1:])
