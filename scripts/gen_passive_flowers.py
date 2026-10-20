"""生成被动产能花的贴图：太阳花（金）与夜颠茄（暗紫）。128x128 RGBA。"""
from PIL import Image, ImageDraw
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(ROOT, "..", "Textures", "Things", "Building", "Botania"))
os.makedirs(OUT, exist_ok=True)


def petal_flower(path, petal, petal_dark, center, center_hi, stem=(74, 96, 58, 255), glow=None):
    size = 128
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 茎
    d.line([(64, 78), (64, 112)], fill=stem, width=5)
    # 两片叶子
    d.ellipse([40, 92, 62, 102], fill=stem)
    d.ellipse([66, 100, 88, 110], fill=stem)
    # 六瓣
    petals = [
        (64, 40), (44, 50), (40, 68), (64, 74), (88, 68), (84, 50),
    ]
    for cx, cy in petals:
        d.ellipse([cx - 18, cy - 16, cx + 18, cy + 16], fill=petal)
    for cx, cy in petals:
        d.ellipse([cx - 10, cy - 9, cx + 10, cy + 9], fill=petal_dark)
    # 花心
    d.ellipse([54, 54, 74, 74], fill=center)
    d.ellipse([58, 58, 66, 66], fill=center_hi)
    if glow:
        d.ellipse([34, 30, 94, 90], outline=glow, width=2)
    img.save(path)
    print(path)


petal_flower(
    os.path.join(OUT, "Daybloom.png"),
    petal=(246, 206, 92, 255), petal_dark=(232, 158, 52, 255),
    center=(255, 244, 208, 255), center_hi=(255, 255, 240, 255),
    glow=(255, 228, 130, 120),
)
petal_flower(
    os.path.join(OUT, "Nightshade.png"),
    petal=(104, 78, 148, 255), petal_dark=(66, 46, 104, 255),
    center=(198, 220, 236, 255), center_hi=(232, 240, 248, 255),
    stem=(56, 74, 60, 255), glow=(150, 170, 220, 90),
)
