"""
Makes ahead of time the smaller copies of the pictures that the API serves to a list, a card or an
avatar. The API makes each copy itself on the first request for it and keeps it, so nothing needs
this; a copy made here is found beside the pictures and served instead, which spares the first
reader of each picture the few dozen milliseconds of making it.

A portrait is about a quarter of a megabyte and an avatar draws it twenty-eight pixels across, so
every picture under Resources/Images is copied at each width in WIDTHS it is wider than, as WebP,
keeping its proportions:

    Resources/Images/sized/<width>/<its path, without its extension>.<digest>.webp

The digest is the first twelve hex digits of the SHA-256 of the picture it was made from, the one
the corpus's picture rows carry and the API's addresses end in, so a replaced picture never reaches
a reader through a copy of the old one. A copy already there is left alone, and running it again
makes only what a new or changed picture needs. `forge publish` sends the copies with the pictures.
They are made as the API makes its own (ImageEndpoints, PictureCopies): the same widths, the same
proportions, WebP at the same quality.

  python scripts/picture-sizes.py [--resources FOLDER] [--dry]

--resources points at the Resources folder, which a worktree has none of: give the main checkout's.
The widths are the API's (ImageEndpoints.SizedWidths); a test holds the two lists together.
"""

import argparse, hashlib, os, sys

from PIL import Image, ImageOps

WIDTHS = (128, 256, 512, 1024)

SIZED = 'sized'
EXTENSIONS = {'.jpg', '.jpeg', '.png', '.webp'}
DIGEST_LENGTH = 12

QUALITY = 80


def pictures(folder):
    """Every picture under the folder, by its path there with forward slashes; the copies are not pictures."""
    for directory, subdirectories, files in os.walk(folder):
        if os.path.normcase(os.path.abspath(directory)) == os.path.normcase(os.path.abspath(folder)):
            subdirectories[:] = [d for d in subdirectories if d != SIZED]
        for name in files:
            if os.path.splitext(name)[1].lower() in EXTENSIONS:
                yield os.path.relpath(os.path.join(directory, name), folder).replace(os.sep, '/')


def sized_path(file, digest, width):
    """Where a copy is kept, relative to the pictures' folder — as ImageEndpoints.SizedPath says."""
    return f'{SIZED}/{width}/{os.path.splitext(file)[0]}.{digest}.webp'


def copy(image, width, target):
    height = max(1, round(image.height * width / image.width))
    smaller = image.resize((width, height), Image.Resampling.LANCZOS)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    partial = target + '.part'
    smaller.save(partial, 'WEBP', quality=QUALITY, method=6)
    os.replace(partial, target)


def main():
    parser = argparse.ArgumentParser(description='Makes the smaller copies of the pictures the API serves.')
    parser.add_argument('--resources', default=os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Resources'))
    parser.add_argument('--dry', action='store_true', help='count what would be made, and make nothing')
    arguments = parser.parse_args()

    folder = os.path.join(arguments.resources, 'Images')
    if not os.path.isdir(folder):
        sys.exit(f'{folder} is not there. Give --resources the Resources folder of the checkout that holds the pictures.')

    made = there = unreadable = 0
    written = 0
    for file in sorted(pictures(folder)):
        with open(os.path.join(folder, file), 'rb') as stream:
            digest = hashlib.sha256(stream.read()).hexdigest()[:DIGEST_LENGTH]

        try:
            # Turned as its camera recorded, which is how a browser draws the picture itself.
            image = ImageOps.exif_transpose(Image.open(os.path.join(folder, file)))
        except (OSError, ValueError) as error:
            print(f'{file}: not read ({error})', file=sys.stderr)
            unreadable += 1
            continue

        if image.mode not in ('RGB', 'RGBA'):
            image = image.convert('RGBA' if 'A' in image.getbands() or 'transparency' in image.info else 'RGB')

        for width in (w for w in WIDTHS if w < image.width):
            target = os.path.join(folder, sized_path(file, digest, width))
            if os.path.exists(target):
                there += 1
                continue
            made += 1
            if not arguments.dry:
                copy(image, width, target)
                written += os.path.getsize(target)

    verb = 'would make' if arguments.dry else 'made'
    print(f'{verb} {made} copies ({written / 1e6:.1f} MB), {there} already there, {unreadable} pictures not read')


if __name__ == '__main__':
    main()
