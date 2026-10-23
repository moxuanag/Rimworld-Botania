"""生成三朵新花的贴图：噬草花（病绿）、苦丽花（暗红）、明心莲（白蓝）。"""
from PIL import Image, ImageDraw
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(ROOT, "..", "Textures", "Things", "Building", "Botania"))
os.makedirs(OUT, exist_ok=True)


def base(stem=(70, 82, 54, 255)):
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(64, 84), (64, 112)], fill=stem, width=5)
    d.ellipse([38, 96, 60, 106], fill=stem)
    d.ellipse([68, 102, 90, 112], fill=stem)
    return img, d


def blight_eater():
    img, d = base((82, 74, 48, 255))
    # 病态的绿，花瓣边缘发褐
    for cx, cy in [(64, 40), (44, 54), (46, 74), (64, 82), (84, 74), (86, 54)]:
        d.ellipse([cx - 17, cy - 15, cx + 17, cy + 15], fill=(96, 128, 68, 255))
        d.ellipse([cx - 10, cy - 9, cx + 10, cy + 9], fill=(132, 160, 84, 255))
    d.ellipse([54, 56, 74, 76], fill=(64, 52, 40, 255))
    # 病斑
    for px, py in [(50, 48), (80, 60), (62, 74), (74, 44)]:
        d.ellipse([px - 4, py - 4, px + 4, py + 4], fill=(96, 72, 52, 200))
    img.save(os.path.join(OUT, "BlightEater.png"))
    print("BlightEater.png")


def bitter_bloom():
    img, d = base((68, 62, 44, 255))
    # 暗红重瓣
    d.ellipse([34, 32, 94, 92], fill=(120, 44, 52, 255))
    d.ellipse([42, 40, 86, 84], fill=(150, 60, 66, 255))
    d.ellipse([50, 48, 78, 76], fill=(180, 84, 84, 255))
    d.ellipse([57, 55, 71, 69], fill=(96, 40, 44, 255))
    # 刺
    for ang, (x, y) in enumerate([(64, 24), (32, 58), (96, 58), (64, 96)]):
        d.line([(64, 64), (x, y)], fill=(84, 36, 40, 160), width=2)
    img.save(os.path.join(OUT, "BitterBloom.png"))
    print("BitterBloom.png")


def clear_mind_lotus():
    img, d = base((72, 96, 88, 255))
    # 莲座：两层白瓣 + 淡蓝边
    for cx, cy in [(64, 44), (40, 56), (44, 78), (64, 84), (84, 78), (88, 56)]:
        d.polygon([(cx, cy - 22), (cx + 13, cy + 6), (cx, cy + 18), (cx - 13, cy + 6)],
                  fill=(232, 240, 244, 255), outline=(176, 208, 220, 255))
    d.ellipse([52, 50, 76, 74], fill=(208, 226, 236, 255))
    d.ellipse([58, 56, 70, 68], fill=(150, 196, 214, 255))
    d.ellipse([62, 60, 66, 64], fill=(244, 252, 255, 255))
    img.save(os.path.join(OUT, "ClearMindLotus.png"))
    print("ClearMindLotus.png")


blight_eater()
bitter_bloom()
clear_mind_lotus()
