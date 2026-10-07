"""Draws the paxpanel icon: a dark rounded tile with a 270-degree gauge whose colour runs through the
panel's temperature palette (blue, green, yellow, red) and a white fan in the middle.
Writes src/PaxPanel/paxpanel.ico (16-256 px) and docs/icon.png. Needs Pillow: pip install pillow"""
import colorsys
import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
S = 1024  # drawn large, then scaled down for each icon size


def hsl(h, s=0.95, l=0.60):
    r, g, b = colorsys.hls_to_rgb(h / 360, l, s)
    return int(r * 255), int(g * 255), int(b * 255), 255


def hue_at(t):
    """t in 0..1 along the arc -> the panel's hue stops: 210 blue, 135 green, 0 red."""
    return 210 - 75 * (t / 0.4) if t < 0.4 else 135 - 135 * ((t - 0.4) / 0.6)


def draw():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([24, 24, S - 24, S - 24], radius=200, fill=(31, 31, 31, 255), outline=(70, 70, 70, 255), width=12)

    # gauge: 270 degrees, gap at the bottom, drawn as many short coloured pieces
    cx, cy, r, w = S / 2, S / 2 + 20, 360, 110
    box = [cx - r, cy - r, cx + r, cy + r]
    d.arc(box, start=135, end=405, fill=(58, 14, 14, 255), width=w)  # track
    steps = 180
    for i in range(steps):
        t0, t1 = i / steps, (i + 1.6) / steps
        d.arc(box, start=135 + 270 * t0, end=135 + 270 * min(1, t1), fill=hsl(hue_at(i / steps)), width=w)

    # fan: 7 curved blades and a hub
    blades = 7
    for k in range(blades):
        a = 2 * math.pi * k / blades
        pts = []
        for j in range(24):  # petal outline: out along one curve, back along another
            u = j / 23
            rad = 40 + 170 * u
            ang = a + 0.55 * u
            pts.append((cx + rad * math.cos(ang), cy + rad * math.sin(ang)))
        for j in range(24):
            u = 1 - j / 23
            rad = 40 + 170 * u
            ang = a + 0.55 * u + 0.42 * math.sin(math.pi * u) + 0.08
            pts.append((cx + rad * math.cos(ang), cy + rad * math.sin(ang)))
        d.polygon(pts, fill=(245, 245, 245, 255))
    d.ellipse([cx - 52, cy - 52, cx + 52, cy + 52], fill=(31, 31, 31, 255), outline=(245, 245, 245, 255), width=18)
    return img


def main():
    img = draw()
    sizes = [16, 24, 32, 48, 64, 128, 256]
    ico = ROOT / "src" / "PaxPanel" / "paxpanel.ico"
    img.resize((256, 256), Image.LANCZOS).save(ico, sizes=[(n, n) for n in sizes])
    img.resize((256, 256), Image.LANCZOS).save(ROOT / "docs" / "icon.png")
    print("wrote", ico)


if __name__ == "__main__":
    main()
