"""Creature surfaces (P3-43): fur (fine overlapping strands in tufts), chitin (glossy plates and segment seams, also
scales) and bone (pale, porous, with fine cracks). Neutral-ish palettes: models tint them by vertex colour."""
import numpy as np

import synth as s
from gen_built import streaks
from gen_natural import finish


def fur(r, seed):
    """Fur: strands combed in one direction, broken into tufts with darker roots and lighter tips."""
    base, dark, light = s.palette(r)
    strands = streaks(seed, 0.6, 9.0)                        # long along y, fine across x
    tufts = s.spectral_noise(seed + 1, falloff=2.2)
    tuft_dir = s.warp(strands, seed + 2, 6.0)
    t = np.clip(tuft_dir * 0.6 + tufts * 0.4, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.45, base), (1.0, light)])
    height = tuft_dir * 0.7 + tufts * 0.3
    rough = np.full_like(t, r.get("roughness", 0.9))
    return finish(rgb, height, 0.25, sigma=0.8), height, rough, np.zeros_like(t)


def chitin(r, seed):
    """Chitin: smooth bulging plates with dark seams, a glossy sheen; ``cells`` sets the plate (scale) size."""
    base, dark, light = s.palette(r)
    f1, edge, cid = s.voronoi(seed, r.get("cells", 60), warp_amount=1.2)
    bulge = np.sqrt(np.clip(1 - f1 * 1.3, 0, 1))
    seam = np.clip(1 - edge / 0.06, 0, 1)
    tone = s.rng(seed + 3).random(len(np.unique(cid)) + r.get("cells", 60))[cid % r.get("cells", 60)]
    t = np.clip(bulge * 0.55 + tone * 0.25 + s.spectral_noise(seed + 4, falloff=2.4) * 0.2, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.55, base), (1.0, light)]) * (1 - seam * 0.5)[..., None]
    height = bulge * 0.7 - seam * 0.3
    rough = np.clip(r.get("roughness", 0.3) + seam * 0.3, 0, 1)
    return finish(rgb, height, 0.3, sigma=1.0), height, rough, np.zeros_like(t)


def bone(r, seed):
    """Bone: pale and dense, with fine pores, faint growth lines and hairline cracks."""
    base, dark, light = s.palette(r)
    pores = s.spectral_noise(seed, falloff=0.9)
    lines = streaks(seed + 1, 1.5, 6.0)
    _, crack, _ = s.voronoi(seed + 2, 14, warp_amount=3.0)
    cracks = np.clip(1 - crack / 0.012, 0, 1)
    t = np.clip(0.6 + lines * 0.25 - (pores > 0.82) * 0.4 + s.spectral_noise(seed + 3, falloff=2.5) * 0.15, 0, 1)
    rgb = s.gradient_map(t, [(0.0, dark), (0.6, base), (1.0, light)]) * (1 - cracks * 0.45)[..., None]
    height = 0.6 + lines * 0.2 - (pores > 0.82) * 0.3 - cracks * 0.3
    rough = np.full_like(t, r.get("roughness", 0.6))
    return finish(rgb, height, 0.3, sigma=1.0), height, rough, np.zeros_like(t)
