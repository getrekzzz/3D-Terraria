#!/usr/bin/env python3
"""Luminance report for screenshots (the lighting tour, game/Testing/Tours/lighting.cmds).

For each PNG: mean Rec.709 luma of the 8-bit sRGB values (0-1), the percentage of clipped pixels (luma > 0.98), the
mean luma of the centre (the middle tenth of the width and height) and, with --profile, a 12-bin luma profile along
the horizontal centre line (left to right).

--check GLOB (repeatable) turns it into an acceptance test: every file whose name matches one of the globs must have
a mean luma within --mean (default 0.35-0.6) and fewer clipped pixels than --max-clipped percent (default 2); the
exit status is 1 when a file fails or a glob matches nothing. The lighting acceptance (desert and snow at noon and
the frames after leaving a cave, on medium, high and ultra):

    python3 tools/ci/shot_stats.py shots/*.png --check '[mhu]*_desert*' --check '[mhu]*_tundra*' --check '[mhu]*_exit_*'

--ratio A B MIN MAX (repeatable) requires centre(A) / centre(B) within [MIN, MAX] and --centre-min NAME MIN a centre
of at least MIN, naming files by stem. The torch corridor acceptance (an end wall 1-10 m from a torch):

    python3 tools/ci/shot_stats.py shots/*_corridor_*.png --ratio medium_corridor_8m medium_corridor_1m 0.2 1 \
        --ratio medium_corridor_10m medium_corridor_1m 0.1 1 --ratio low_corridor_4m medium_corridor_4m 0.7 1.3 \
        --ratio low_corridor_8m medium_corridor_8m 0.7 1.3 --centre-min medium_corridor_6m 0.25 \
        --centre-min low_corridor_6m 0.25
"""
import argparse
import fnmatch
import pathlib
import sys

import numpy as np
from PIL import Image

CLIP_LUMA = 0.98
PROFILE_BINS = 12
CENTRE = 0.1


def luma(path: pathlib.Path) -> np.ndarray:
    """Rec.709 luma (0-1) of the gamma-encoded pixels, alpha ignored."""
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    return rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def stats(y: np.ndarray) -> tuple[float, float]:
    """Mean luma and the percentage of clipped pixels."""
    return float(y.mean()), float((y > CLIP_LUMA).mean() * 100.0)


def centre(y: np.ndarray) -> float:
    """Mean luma of the middle CENTRE share of the width and height."""
    h, w = y.shape
    dh, dw = max(1, int(h * CENTRE / 2)), max(1, int(w * CENTRE / 2))
    return float(y[h // 2 - dh:h // 2 + dh, w // 2 - dw:w // 2 + dw].mean())


def relations(centres: dict[str, float], ratios: list[list[str]], minimums: list[list[str]]) -> list[str]:
    """Checks --ratio and --centre-min against the centre lumas by file stem; returns the failures."""
    failures = []
    for a, b, lo, hi in ratios:
        if a not in centres or b not in centres:
            failures.append(f"(ratio {a}/{b}: missing file)")
            continue
        r = centres[a] / max(centres[b], 1e-6)
        ok = float(lo) <= r <= float(hi)
        print(f"ratio {a}/{b} = {r:.3f} (want {lo}-{hi}) {'ok' if ok else 'FAIL'}")
        if not ok:
            failures.append(f"{a}/{b}")
    for name, lo in minimums:
        if name not in centres:
            failures.append(f"(centre {name}: missing file)")
            continue
        ok = centres[name] >= float(lo)
        print(f"centre {name} = {centres[name]:.3f} (want >= {lo}) {'ok' if ok else 'FAIL'}")
        if not ok:
            failures.append(f"centre {name}")
    return failures


def profile(y: np.ndarray) -> list[float]:
    """Mean luma of PROFILE_BINS equal spans of the middle row (left to right)."""
    row = y[y.shape[0] // 2]
    return [float(span.mean()) for span in np.array_split(row, PROFILE_BINS)]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("files", nargs="+", type=pathlib.Path, help="PNG screenshots")
    ap.add_argument("--profile", action="store_true", help="also print the centre-line luma profile")
    ap.add_argument("--check", action="append", default=[], metavar="GLOB", help="file names that must pass the limits")
    ap.add_argument("--mean", nargs=2, type=float, default=(0.35, 0.6), metavar=("MIN", "MAX"), help="mean luma range")
    ap.add_argument("--max-clipped", type=float, default=2.0, metavar="PCT", help="clipped pixel limit in percent")
    ap.add_argument("--ratio", nargs=4, action="append", default=[], metavar=("A", "B", "MIN", "MAX"),
                    help="centre luma of A over B must be within MIN-MAX (file stems)")
    ap.add_argument("--centre-min", nargs=2, action="append", default=[], metavar=("NAME", "MIN"),
                    help="centre luma of NAME (file stem) must be at least MIN")
    args = ap.parse_args()

    lo, hi = args.mean
    failures = []
    matched = {glob: 0 for glob in args.check}
    centres = {}
    print(f"{'file':<28} {'mean':>6} {'clip%':>6} {'centre':>6}")
    for path in sorted(args.files):
        y = luma(path)
        mean, clipped = stats(y)
        centres[path.stem] = centre(y)
        checked = [g for g in args.check if fnmatch.fnmatch(path.name, g)]
        for glob in checked:
            matched[glob] += 1
        verdict = ""
        if checked:
            ok = lo <= mean <= hi and clipped < args.max_clipped
            verdict = "ok" if ok else "FAIL"
            if not ok:
                failures.append(path.name)
        print(f"{path.stem:<28} {mean:6.3f} {clipped:6.2f} {centres[path.stem]:6.3f} {verdict}")
        if args.profile:
            print("  profile " + " ".join(f"{v:.2f}" for v in profile(y)))
    for glob, count in matched.items():
        if count == 0:
            failures.append(f"(no file matches {glob})")
    failures += relations(centres, args.ratio, args.centre_min)
    if args.check or args.ratio or args.centre_min:
        parts = [f"mean {lo}-{hi}, clipped < {args.max_clipped}%"] if args.check else []
        limits = ", ".join(parts + (["centre luma"] if args.ratio or args.centre_min else []))
        print(f"FAIL ({limits}): {', '.join(failures)}" if failures else f"PASS ({limits})")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
