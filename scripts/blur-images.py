"""Blurs the listed regions of public works into copies beside them.

Reads scripts/blur-regions.json. For each entry, the source picture under Resources/Images is
copied to the output path with every region (an ellipse, as fractions of the picture's width
and height) replaced by a strong Gaussian blur of itself, its edge feathered so the blur fades
into the picture. Everything outside the regions is left as it was.

    python scripts/blur-images.py [--resources PATH] [--force]

Needs Pillow. An output already there is kept unless --force is given.
"""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent

# Of the picture's longer side: how far the blur spreads, and how wide the feathered edge is.
BLUR = 0.03
FEATHER = 0.012


def blur(source: Path, output: Path, regions: list[list[float]]) -> None:
    picture = Image.open(source)
    picture.load()
    exif = picture.info.get("exif")
    picture = picture.convert("RGB")
    width, height = picture.size
    side = max(width, height)

    # Twice, so that no line of what is beneath survives inside the region.
    blurred = picture.filter(ImageFilter.GaussianBlur(side * BLUR)).filter(ImageFilter.GaussianBlur(side * BLUR))

    mask = Image.new("L", picture.size, 0)
    draw = ImageDraw.Draw(mask)
    for left, top, right, bottom in regions:
        draw.ellipse([left * width, top * height, right * width, bottom * height], fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(side * FEATHER))

    output.parent.mkdir(parents=True, exist_ok=True)
    keep = {"quality": 92}
    if exif:
        keep["exif"] = exif
    Image.composite(blurred, picture, mask).save(output, **keep)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--resources", default=HERE.parent / "Resources", type=Path)
    parser.add_argument("--force", action="store_true")
    arguments = parser.parse_args()

    images = arguments.resources / "Images"
    listed = json.loads((HERE / "blur-regions.json").read_text(encoding="utf-8"))["images"]
    made = 0
    for entry in listed:
        source, output = images / entry["source"], images / entry["output"]
        if output.exists() and not arguments.force:
            continue
        if not source.exists():
            print(f"{entry['entity']}: {source} is missing; run scripts/fetch-images.ps1", file=sys.stderr)
            return 1
        blur(source, output, entry["regions"])
        made += 1
        print(f"{entry['entity']}: {output}")
    print(f"{made} blurred copies made; {len(listed)} listed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
