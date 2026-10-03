"""Crops Workshop gallery images out of the dev-harness screenshots.

Refresh the screenshots first (RimWorld closed, Steam running):
    make smoke-novre SCENARIOS=ui,playground-check
then:
    python tools/asset-pipeline/crop_gallery.py
Output: workshop/gallery/*.png (upload these as Workshop screenshots).
Crop boxes assume the harness's 2560x1440 capture.
"""
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SHOTS = ROOT / "tools" / ".smoke-profile" / "DeathrestHarness"
OUT = ROOT / "workshop" / "gallery"

CROPS = {
    "01-deathrest-hall.png": ("playground.png", (1100, 290, 1880, 800)),
    "02-casket-links-tab.png": ("ui-casket-tab.png", (0, 740, 640, 1404)),
    "03-building-links-tab.png": ("ui-building-tab.png", (0, 740, 640, 1404)),
    "04-settings.png": ("ui-settings.png", (830, 371, 1728, 1069)),
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (shot, box) in CROPS.items():
        src = SHOTS / shot
        if not src.exists():
            print(f"missing {src}, skipped")
            continue
        img = Image.open(src).convert("RGB")
        if img.size != (2560, 1440):
            print(f"{shot} is {img.size}, expected 2560x1440; crop boxes may be off")
        img.crop(box).save(OUT / name, optimize=True)
        print(OUT / name, img.crop(box).size)


if __name__ == "__main__":
    main()
