"""Generates the game's audio: sound effects (assets/audio/sfx/*.wav) and the looping soundtrack (assets/audio/music/*.ogg).

Needs numpy, scipy and soundfile (run with the Blender venv's python):
  /root/bpyenv/bin/python tools/audio/build_audio.py [--only=a,b*] [--sfx] [--music] [--ambience]
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
from music_tracks import STINGS, TRACKS  # noqa: E402
from synth import SR  # noqa: E402

REPO = pathlib.Path(__file__).resolve().parents[2]
SFX_DIR = REPO / "assets" / "audio" / "sfx"
MUSIC_DIR = REPO / "assets" / "audio" / "music"
STING_DIR = MUSIC_DIR / "stings"
AMBIENCE_DIR = REPO / "assets" / "audio" / "ambience"


def check():
    """Every sound the game code names (Sfx.Play / Sfx.PlayAt), the material families and the data (sound sets, creature
    and use style sounds) name has a recipe."""
    import re
    names = set()
    for path in (REPO / "game").rglob("*.cs"):
        names.update(re.findall(r'Sfx\.Play(?:At)?\("([a-z0-9_]+)"', path.read_text(encoding="utf-8")))
    missing = sorted(n for n in names if n not in sfx_recipes.RECIPES)
    for n in missing:
        print(f"ERROR: game plays sound '{n}' but tools/audio/sfx_recipes*.py has no recipe for it")
    import json
    cfg = re.sub(r"^\s*//.*$", "", (REPO / "data" / "config" / "creature_materials.json").read_text(encoding="utf-8"), flags=re.M)
    for m in json.loads(cfg)["materials"].values():
        names.update((m["hitSound"], m["deathSound"]))
    # Item sound sets, creature sounds and the use styles' sounds named in data (P3-27).
    for path in (REPO / "data").rglob("*.json"):
        if path.parent.name in ("items", "enemies"):
            names.update(re.findall(r'"(?:soundSet|hitSound|deathSound)":\s*"([a-z0-9_]+)"', path.read_text(encoding="utf-8")))
    names.update(re.findall(r'"(?:sound|heavySound)":\s*"([a-z0-9_]+)"', (REPO / "data" / "config" / "use_styles.json").read_text(encoding="utf-8")))
    for n in sorted(n for n in names if n not in sfx_recipes.RECIPES and n not in missing):
        print(f"ERROR: data names sound '{n}' but tools/audio/sfx_recipes*.py has no recipe for it")
    missing = sorted(n for n in names if n not in sfx_recipes.RECIPES)
    loops = set()
    for path in (REPO / "game").rglob("*.cs"):
        loops.update(re.findall(r'AmbienceLoops\.(?:Player|Load)\([^"]*"([a-z0-9_]+)"', path.read_text(encoding="utf-8")))
    for n in sorted(loops - set(ambience.LOOPS)):
        print(f"ERROR: game plays ambience loop '{n}' but tools/audio/ambience.py has no recipe for it")
        missing.append(n)
    tracks = sorted(TRACKS)
    stings = set(re.findall(r'\.Sting\("([a-z0-9_]+)"', "".join(p.read_text(encoding="utf-8") for p in (REPO / "game").rglob("*.cs"))))
    for n in sorted(stings - set(STINGS)):
        print(f"ERROR: game plays music sting '{n}' but tools/audio/music_tracks.py has no STINGS entry for it")
        missing.append(n)
    print(f"{len(names)} sounds referenced, {len(sfx_recipes.RECIPES)} recipes, {len(tracks)} music tracks, {len(STINGS)} stings")
    return 1 if missing else 0


def main(argv):
    if "--check" in argv:
        raise SystemExit(check())
    only = next((a.split("=", 1)[1].split(",") for a in argv if a.startswith("--only=")), None)
    want = (lambda i: any(fnmatch.fnmatch(i, p) for p in only)) if only else (lambda i: True)
    picked = {f for f in ("--sfx", "--music", "--ambience") if f in argv}
    do_sfx = not picked or "--sfx" in picked
    do_music = not picked or "--music" in picked
    do_ambience = not picked or "--ambience" in picked
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
        STING_DIR.mkdir(parents=True, exist_ok=True)
        for name, spec in STINGS.items():
            if not want(name):
                continue
            x = composer.build_sting(spec, seed=hash_id(name))
            sf.write(STING_DIR / f"{name}.ogg", x, SR, format="OGG", subtype="VORBIS")
            print(f"sting {name}: {len(x) / SR:.1f}s")
    if do_ambience:
        AMBIENCE_DIR.mkdir(parents=True, exist_ok=True)
        for name in sorted(ambience.LOOPS):
            if not want(name):
                continue
            x = ambience.render(name, seed=hash_id(name))
            sf.write(AMBIENCE_DIR / f"{name}.ogg", x, ambience.SR, format="OGG", subtype="VORBIS")
            print(f"ambience {name}: {len(x) / ambience.SR:.1f}s")


def hash_id(text):
    h = 2166136261
    for ch in text.encode():
        h = ((h ^ ch) * 16777619) & 0xFFFFFFFF
    return h


if __name__ == "__main__":
    main(sys.argv[1:])
