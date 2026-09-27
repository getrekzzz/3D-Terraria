#!/usr/bin/env python3
"""Fails when a hand-written source file grows past the modularity limit.

Keeps systems split into focused files (directive: no enormous monolithic files).
Vendored third-party code and generated/data files are exempt.
"""
import pathlib
import sys

LIMIT = 400  # hard limit; aim for < 300
ROOTS = ["src", "game", "tools", "tests"]
EXTS = {".cs", ".py", ".gdshader", ".gdshaderinc"}
EXEMPT_PARTS = {"ThirdParty", "obj", "bin", "_cache", "__pycache__"}


def main() -> int:
    repo = pathlib.Path(__file__).resolve().parents[2]
    offenders = []
    for root in ROOTS:
        for path in (repo / root).rglob("*"):
            if path.suffix not in EXTS or EXEMPT_PARTS.intersection(path.parts):
                continue
            lines = sum(1 for _ in path.open(encoding="utf-8"))
            if lines > LIMIT:
                offenders.append((lines, path.relative_to(repo)))
    for lines, path in sorted(offenders, reverse=True):
        print(f"{path}: {lines} lines (limit {LIMIT})")
    return 1 if offenders else 0


if __name__ == "__main__":
    sys.exit(main())
