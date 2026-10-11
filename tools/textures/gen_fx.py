"""Effect textures: the four-stage crack atlas drawn over damaged blocks (assets/textures/fx/cracks.png).

Each stage is a white-on-transparent alpha mask; every stage keeps the cracks of the one before and adds longer and
new branches, so a block reads as breaking further with each hit.
"""
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

SIZE = 128
STAGES = 4


def _walk(rng, draw, x, y, angle, length, width, depth):
    """One crack: a jittered polyline that may fork."""
    for _ in range(int(length)):
        angle += rng.normal(0, 0.35)
        nx, ny = x + np.cos(angle) * 4.0, y + np.sin(angle) * 4.0
        draw.line([(x, y), (nx, ny)], fill=255, width=max(1, int(round(width))))
        x, y = nx, ny
        width = max(1.0, width * 0.94)
        if depth < 2 and rng.random() < 0.05:
            _walk(rng, draw, x, y, angle + rng.choice([-1, 1]) * rng.uniform(0.6, 1.2), length * 0.45, width * 0.8, depth + 1)
        if not (-8 < x < SIZE + 8 and -8 < y < SIZE + 8):
            break


def build() -> np.ndarray:
    """RGBA atlas STAGES*SIZE wide: white with the crack mask in alpha."""
    rng = np.random.default_rng(7)
    seeds = [(rng.uniform(30, 98), rng.uniform(30, 98), rng.uniform(0, 2 * np.pi)) for _ in range(5)]
    mask = Image.new("L", (SIZE, SIZE), 0)
    draw = ImageDraw.Draw(mask)
    atlas = np.zeros((SIZE, SIZE * STAGES, 4), np.uint8)
    for stage in range(STAGES):
        # Stage 0 starts two short cracks from the middle; later stages lengthen them and add more.
        for i, (x, y, a) in enumerate(seeds[: 2 + stage // 2]):
            srng = np.random.default_rng(100 * stage + i)
            for arm in range(2):
                _walk(srng, draw, x, y, a + arm * 2.3, 4 + stage * 3, 3.2 - stage * 0.4, 0)
        soft = np.asarray(mask.filter(ImageFilter.GaussianBlur(0.6)), np.float32)
        tile = atlas[:, stage * SIZE:(stage + 1) * SIZE]
        tile[..., :3] = 255
        tile[..., 3] = np.clip(soft * 1.2, 0, 255).astype(np.uint8)
    return atlas
