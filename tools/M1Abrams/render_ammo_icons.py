"""Render distinct 256px M1 ammunition icons for the game's item atlas."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parents[2] / 'ZZ-PZAEC_M1Abrams/UIAtlases/ItemIconAtlas'
S = 4
FONT = Path('C:/Windows/Fonts/arialbd.ttf')


def point(xy):
    return [(round(x * S), round(y * S)) for x, y in xy]


def rounded(draw, bounds, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(tuple(round(v * S) for v in bounds), radius * S,
                           fill=fill, outline=outline, width=width * S)


def polygon(draw, coords, fill, outline=None):
    draw.polygon(point(coords), fill=fill, outline=outline, width=2 * S)


def line(draw, coords, fill, width):
    draw.line(point(coords), fill=fill, width=width * S, joint='curve')


def label(draw, letters, color):
    font = ImageFont.truetype(str(FONT), 37 * S)
    box = draw.textbbox((0, 0), letters, font=font, stroke_width=0)
    x = (256 * S - (box[2] - box[0])) // 2
    y = 201 * S - box[1]
    draw.text((x, y), letters, font=font, fill=color,
              stroke_width=2 * S, stroke_fill=(8, 13, 19, 255))


def base(kind):
    img = Image.new('RGBA', (256 * S, 256 * S))
    d = ImageDraw.Draw(img)
    palettes = {
        'HE': ((76, 42, 17, 225), (246, 167, 48, 255)),
        'AP': ((23, 45, 62, 225), (94, 211, 241, 255)),
        'AA': ((13, 50, 57, 225), (102, 248, 222, 255)),
    }
    background, accent = palettes[kind]
    rounded(d, (9, 9, 247, 247), 28, background, (190, 210, 209, 180), 3)
    rounded(d, (17, 184, 239, 239), 17, (8, 13, 19, 215), accent, 2)
    return img, d, accent


def render_he():
    img, d, accent = base('HE')
    polygon(d, [(110, 38), (146, 38), (164, 91), (160, 174), (96, 174), (92, 91)],
            (132, 113, 69, 255), (229, 210, 145, 255))
    polygon(d, [(110, 38), (146, 38), (158, 78), (98, 78)],
            (248, 164, 51, 255), (255, 222, 135, 255))
    rounded(d, (94, 129, 162, 144), 3, (43, 51, 39, 255), accent, 2)
    rounded(d, (92, 166, 164, 180), 2, (176, 125, 50, 255), (247, 200, 99, 255), 2)
    line(d, [(112, 82), (110, 119)], (243, 223, 172, 240), 4)
    label(d, 'HE', accent)
    return img


def render_ap():
    img, d, accent = base('AP')
    polygon(d, [(126, 21), (141, 65), (148, 96), (148, 171), (108, 171), (108, 96), (115, 65)],
            (115, 143, 156, 255), (215, 228, 233, 255))
    polygon(d, [(126, 21), (141, 65), (137, 82), (119, 82), (115, 65)],
            (115, 224, 246, 255), (231, 252, 255, 255))
    rounded(d, (107, 108, 149, 117), 2, (14, 64, 91, 255), accent, 1)
    rounded(d, (106, 146, 150, 166), 2, (39, 55, 67, 255), (213, 236, 240, 255), 2)
    line(d, [(116, 94), (116, 140)], (218, 241, 249, 225), 3)
    label(d, 'AP', accent)
    return img


def render_aa():
    img, d, accent = base('AA')
    # Finned guided missile silhouette, deliberately unlike the vertical cannon shells.
    polygon(d, [(35, 127), (87, 112), (157, 112), (211, 126), (157, 140), (87, 140)],
            (184, 199, 199, 255), (236, 252, 244, 255))
    polygon(d, [(166, 114), (211, 126), (166, 138)],
            (70, 234, 213, 255), (212, 255, 244, 255))
    polygon(d, [(100, 113), (133, 82), (145, 82), (136, 114)],
            (51, 125, 133, 255), accent)
    polygon(d, [(100, 139), (133, 170), (145, 170), (136, 138)],
            (39, 111, 119, 255), accent)
    polygon(d, [(47, 124), (26, 99), (56, 113), (61, 126), (56, 139), (26, 153)],
            (65, 122, 128, 255), accent)
    line(d, [(73, 119), (158, 119)], (250, 255, 250, 255), 4)
    rounded(d, (164, 119, 174, 133), 3, (241, 255, 247, 255))
    label(d, 'AA', accent)
    return img


for name, render in [('pzM1Shell', render_he), ('pzM1ShellAP', render_ap),
                     ('pzM1AAMissile', render_aa)]:
    render().resize((256, 256), Image.Resampling.LANCZOS).save(OUT / (name + '.png'))
    print(OUT / (name + '.png'))
