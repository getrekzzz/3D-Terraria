#!/usr/bin/env python3
"""Emote bubble icons (X-05): a white speech bubble with a dark outline and a tail, and a glyph per emote, drawn at 4x
and downsampled for smooth edges.

Usage: python3 tools/textures/build_emotes.py [--check]
Output: assets/icons/emotes/<id>.png (64x64). Ids must match data/config/town.json "emotes" (EmoteConfig.Ids).
"""
import math
import pathlib
import sys

from PIL import Image, ImageDraw

REPO = pathlib.Path(__file__).resolve().parents[2]
OUT = REPO / "assets" / "icons" / "emotes"
SIZE, K = 64, 4
INK = (40, 30, 48, 255)


def bubble(d):
    s = K
    d.rounded_rectangle((3 * s, 3 * s, 61 * s, 49 * s), radius=14 * s, fill=(255, 255, 255, 255), outline=INK, width=3 * s)
    d.polygon([(22 * s, 47 * s), (34 * s, 47 * s), (20 * s, 61 * s)], fill=INK)
    d.polygon([(24 * s, 45 * s), (31 * s, 45 * s), (22 * s, 55 * s)], fill=(255, 255, 255, 255))


def heart(d, cx, cy, r, color):
    s = K
    pts = []
    for k in range(64):
        t = k / 64 * 2 * math.pi
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append(((cx + x * r / 16) * s, (cy - y * r / 16) * s))
    d.polygon(pts, fill=color, outline=INK)


def face(d, mood):
    s = K
    d.ellipse((18 * s, 10 * s, 46 * s, 38 * s), fill=(255, 214, 80, 255), outline=INK, width=2 * s)
    if mood == "sleepy":
        d.line((24 * s, 22 * s, 29 * s, 22 * s), fill=INK, width=2 * s)
        d.line((35 * s, 22 * s, 40 * s, 22 * s), fill=INK, width=2 * s)
    else:
        for x in (26, 38):
            d.ellipse(((x - 2) * s, 19 * s, (x + 2) * s, 24 * s), fill=INK)
    if mood in ("happy", "laugh"):
        d.chord((24 * s, 22 * s, 40 * s, 33 * s), 0, 180, fill=INK if mood == "laugh" else None, outline=INK, width=2 * s)
    elif mood == "sad":
        d.arc((25 * s, 28 * s, 39 * s, 36 * s), 200, 340, fill=INK, width=2 * s)
        d.ellipse((40 * s, 24 * s, 44 * s, 30 * s), fill=(90, 170, 255, 255))
    elif mood == "angry":
        d.line((22 * s, 15 * s, 29 * s, 19 * s), fill=INK, width=2 * s)
        d.line((42 * s, 15 * s, 35 * s, 19 * s), fill=INK, width=2 * s)
        d.arc((25 * s, 28 * s, 39 * s, 36 * s), 200, 340, fill=INK, width=2 * s)
    elif mood == "sleepy":
        d.ellipse((30 * s, 28 * s, 34 * s, 32 * s), outline=INK, width=s * 2)


def text(d, glyph, color, size=26):
    s = K
    # Block letters from strokes, so no font is needed.
    if glyph == "?":
        d.arc((24 * s, 9 * s, 40 * s, 25 * s), 180, 90, fill=color, width=4 * s)
        d.line((32 * s, 25 * s, 32 * s, 31 * s), fill=color, width=4 * s)
        d.ellipse((30 * s, 35 * s, 34 * s, 39 * s), fill=color)
    elif glyph == "!":
        d.line((32 * s, 9 * s, 32 * s, 30 * s), fill=color, width=6 * s)
        d.ellipse((29 * s, 34 * s, 35 * s, 40 * s), fill=color)
    elif glyph == "z":
        for x, y, h in ((20, 26, 10), (32, 18, 8), (42, 10, 6)):
            d.line([(x * s, y * s), ((x + h) * s, y * s), (x * s, (y + h) * s), ((x + h) * s, (y + h) * s)], fill=color, width=2 * s)


EMOTES = {
    "happy": lambda d: face(d, "happy"),
    "laugh": lambda d: face(d, "laugh"),
    "sad": lambda d: face(d, "sad"),
    "angry": lambda d: (face(d, "angry"), d.line((46 * K, 8 * K, 52 * K, 14 * K), fill=(230, 50, 50, 255), width=3 * K)),
    "love": lambda d: heart(d, 32, 24, 16, (240, 70, 110, 255)),
    "sleepy": lambda d: text(d, "z", (70, 90, 200, 255)),
    "question": lambda d: text(d, "?", (60, 120, 230, 255)),
    "alarm": lambda d: text(d, "!", (230, 60, 50, 255)),
    "chat": lambda d: [d.ellipse(((x - 4) * K, 22 * K, (x + 4) * K, 30 * K), fill=INK) for x in (20, 32, 44)],
    "music": lambda d: (d.line((26 * K, 12 * K, 26 * K, 32 * K), fill=INK, width=3 * K), d.line((40 * K, 9 * K, 40 * K, 29 * K), fill=INK, width=3 * K),
                        d.polygon([(26 * K, 12 * K), (40 * K, 9 * K), (40 * K, 14 * K), (26 * K, 17 * K)], fill=INK),
                        d.ellipse((18 * K, 29 * K, 27 * K, 36 * K), fill=(150, 80, 220, 255), outline=INK),
                        d.ellipse((32 * K, 26 * K, 41 * K, 33 * K), fill=(150, 80, 220, 255), outline=INK)),
    "idea": lambda d: (d.ellipse((23 * K, 8 * K, 41 * K, 28 * K), fill=(255, 230, 90, 255), outline=INK, width=2 * K),
                       d.rectangle((28 * K, 28 * K, 36 * K, 36 * K), fill=(150, 150, 160, 255), outline=INK)),
}


def build(emote):
    img = Image.new("RGBA", (SIZE * K, SIZE * K), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    bubble(d)
    EMOTES[emote](d)
    return img.resize((SIZE, SIZE), Image.LANCZOS)


def main(argv):
    if "--check" in argv:
        missing = [e for e in EMOTES if not (OUT / f"{e}.png").exists()]
        print("missing emote icons:", ", ".join(missing) if missing else "none")
        return 1 if missing else 0
    OUT.mkdir(parents=True, exist_ok=True)
    for e in EMOTES:
        build(e).save(OUT / f"{e}.png")
    print(f"wrote {len(EMOTES)} emote icons to {OUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
