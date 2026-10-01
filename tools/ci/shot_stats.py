#!/usr/bin/env python3
"""Luminance report for screenshots (the lighting tour, game/Testing/Tours/lighting.cmds).

For each PNG: mean Rec.709 luma of the 8-bit sRGB values (0-1), the percentage of clipped pixels (luma > 0.98) and,
with --profile, a 12-bin luma profile along the horizontal centre line (left to right).

--check GLOB (repeatable) turns it into an acceptance test: every file whose name matches one of the globs must have
a mean luma within --mean (default 0.35-0.6) and fewer clipped pixels than --max-clipped percent (default 2); the
exit status is 1 when a file fails or a glob matches nothing. The lighting acceptance (desert and snow at noon and
the frames after leaving a cave, on medium, high and ultra):

    python3 tools/ci/shot_stats.py shots/*.png --check '[mhu]*_desert*' --check '[mhu]*_tundra*' --check '[mhu]*_exit_*'
"""
import argparse
import fnmatch
import pathlib
import sys

import numpy as np
from PIL import Image

CLIP_LUMA = 0.98
PROFILE_BINS = 12


def luma(path: pathlib.Path) -> np.ndarray:
    """Rec.709 luma (0-1) of the gamma-encoded pixels, alpha ignored."""
    rgb = np.asarray(Image.open(path).convert("RGB"), dtype=np.float32) / 255.0
    return rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def stats(y: np.ndarray) -> tuple[float, float]:
    """Mean luma and the percentage of clipped pixels."""
    return float(y.mean()), float((y > CLIP_LUMA).mean() * 100.0)


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
    args = ap.parse_args()

    lo, hi = args.mean
    failures = []
    matched = {glob: 0 for glob in args.check}
    print(f"{'file':<28} {'mean':>6} {'clip%':>6}")
    for path in sorted(args.files):
        y = luma(path)
        mean, clipped = stats(y)
        checked = [g for g in args.check if fnmatch.fnmatch(path.name, g)]
        for glob in checked:
            matched[glob] += 1
        verdict = ""
        if checked:
            ok = lo <= mean <= hi and clipped < args.max_clipped
            verdict = "ok" if ok else "FAIL"
            if not ok:
                failures.append(path.name)
        print(f"{path.stem:<28} {mean:6.3f} {clipped:6.2f} {verdict}")
        if args.profile:
            print("  profile " + " ".join(f"{v:.2f}" for v in profile(y)))
    for glob, count in matched.items():
        if count == 0:
            failures.append(f"(no file matches {glob})")
    if args.check:
        limits = f"mean {lo}-{hi}, clipped < {args.max_clipped}%"
        print(f"FAIL ({limits}): {', '.join(failures)}" if failures else f"PASS ({limits})")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
