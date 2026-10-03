"""Overlays the mod title on a Workshop preview draft and writes About/Preview.png.

Usage:
    python title_preview.py --in drafts/workshop_preview/v01.png --out ../../About/Preview.png
"""
import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

FONT = r"C:\Windows\Fonts\bahnschrift.ttf"
LINES = ["DEATHREST", "UNBOUND"]
SIZE = (960, 540)  # Steam rejects previews over 1 MB


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="src", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    img = Image.open(args.src).convert("RGB").resize(SIZE, Image.LANCZOS)
    font = ImageFont.truetype(FONT, 80)
    font.set_variation_by_name("Bold Condensed")

    # Soft dark glow behind the text for legibility, then crisp white text with a dark outline.
    x, y, gap = 42, 40, 4
    shadow = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    text = Image.new("RGBA", SIZE, (0, 0, 0, 0))
    td = ImageDraw.Draw(text)
    cy = y
    for line in LINES:
        sd.text((x, cy), line, font=font, fill=(0, 0, 0, 200), stroke_width=8, stroke_fill=(0, 0, 0, 200))
        td.text((x, cy), line, font=font, fill=(245, 242, 238, 255), stroke_width=3, stroke_fill=(20, 16, 18, 255))
        cy += font.getbbox(line)[3] + gap
    img = img.convert("RGBA")
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(10)))
    img.alpha_composite(text)
    out = Path(args.out)
    img.convert("RGB").save(out, optimize=True)
    print(out, Image.open(out).size, f"{out.stat().st_size / 1024:.0f} KB")


if __name__ == "__main__":
    main()
