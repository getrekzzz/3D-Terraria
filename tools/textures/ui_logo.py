"""The title-screen logo (assets/ui/logo.png), rendered with Pillow from the UI font.

The text (the main menu's title, a build_ui.py parameter: --logo-text=...) gets a grass-green to earth-brown gradient
fill, a small bevel (lit top-left edges, shaded bottom-right ones), a 6 px dark outline and a soft drop shadow, on a
transparent canvas cropped to the drawing. Output is deterministic for a given Pillow build. The game sways and tints it
at runtime (game/Menu/MenuLogo.cs).
"""
from content_data import REPO

FONT = REPO / "assets" / "fonts" / "ComicNeue-Bold.otf"
OUT = REPO / "assets" / "ui" / "logo.png"
DEFAULT_TEXT = "3D Terraria"

SIZE = 150              # font size in pixels
OUTLINE_PX = 6          # dark outline width
OUTLINE = (34, 20, 12)  # dark earth
SHADOW_OFFSET = (7, 9)
SHADOW_ALPHA = 0.55
BEVEL_PX = 3
# Vertical fill stops (fraction of the glyph height, colour): grass on top, earth below.
GRADIENT = (
    (0.00, (168, 236, 96)),
    (0.30, (92, 196, 60)),
    (0.48, (54, 140, 44)),
    (0.56, (150, 104, 56)),
    (1.00, (98, 62, 32)),
)


def _gradient(height, top, bottom):
    import numpy as np
    col = np.zeros((height, 3), np.float32)
    span = max(1, bottom - top)
    for y in range(height):
        t = min(1.0, max(0.0, (y - top) / span))
        for (t0, c0), (t1, c1) in zip(GRADIENT, GRADIENT[1:]):
            if t <= t1:
                k = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
                col[y] = [a + (b - a) * k for a, b in zip(c0, c1)]
                break
    return col


def render(text=DEFAULT_TEXT):
    """The logo as an RGBA numpy array."""
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter, ImageFont

    font = ImageFont.truetype(str(FONT), SIZE)
    left, top, right, bottom = font.getbbox(text)
    pad = OUTLINE_PX + max(SHADOW_OFFSET) + 8
    w, h = right - left + 2 * pad, bottom - top + 2 * pad
    mask_img = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask_img).text((pad - left, pad - top), text, font=font, fill=255)
    mask = np.asarray(mask_img, np.float32) / 255.0
    outline = np.asarray(mask_img.filter(ImageFilter.MaxFilter(2 * OUTLINE_PX + 1)), np.float32) / 255.0
    shadow_img = Image.fromarray((outline * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))
    shadow = np.roll(np.asarray(shadow_img, np.float32) / 255.0, SHADOW_OFFSET[::-1], axis=(0, 1)) * SHADOW_ALPHA

    # Bevel: where the glyph ends just below-right (lit) or just above-left (shaded) of a pixel.
    lit = np.clip(mask - np.roll(mask, (BEVEL_PX, BEVEL_PX), axis=(0, 1)), 0, 1)
    shade = np.clip(mask - np.roll(mask, (-BEVEL_PX, -BEVEL_PX), axis=(0, 1)), 0, 1)

    fill = np.repeat(_gradient(h, pad, h - pad)[:, None, :], w, axis=1)
    fill = fill + (255 - fill) * (lit[..., None] * 0.45)
    fill = fill * (1 - shade[..., None] * 0.35)

    rgb = np.zeros((h, w, 3), np.float32)
    alpha = shadow.copy()                                        # shadow: black
    rgb = rgb * (1 - outline[..., None]) + np.array(OUTLINE, np.float32) * outline[..., None]
    alpha = alpha * (1 - outline) + outline
    rgb = rgb * (1 - mask[..., None]) + fill * mask[..., None]
    alpha = np.maximum(alpha, mask)

    out = np.dstack([np.clip(rgb, 0, 255), np.clip(alpha * 255, 0, 255)]).round().astype(np.uint8)
    ys, xs = np.nonzero(out[..., 3] > 2)
    return out[max(0, ys.min() - 2):ys.max() + 3, max(0, xs.min() - 2):xs.max() + 3]


def build(text=DEFAULT_TEXT, path=OUT):
    from PIL import Image
    path.parent.mkdir(parents=True, exist_ok=True)
    Image.fromarray(render(text), "RGBA").save(path, optimize=True)
    return path


def check():
    """Errors for the CI check (no Pillow needed)."""
    errors = []
    if not FONT.exists():
        errors.append(f"missing font {FONT.relative_to(REPO)} for the logo")
    if not OUT.exists():
        errors.append(f"missing {OUT.relative_to(REPO)} (run build_ui.py)")
    return errors
