"""生成三朵进阶产能花的贴图：炉心蕙（灼红）、电暴花（蓝电）、阿卡纳蔷薇（紫）。"""
from PIL import Image, ImageDraw
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(ROOT, "..", "Textures", "Things", "Building", "Botania"))
os.makedirs(OUT, exist_ok=True)

STEM = (70, 82, 54, 255)


def base(size=128):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.line([(64, 80), (64, 112)], fill=STEM, width=5)
    d.ellipse([38, 94, 60, 104], fill=STEM)
    d.ellipse([68, 100, 90, 110], fill=STEM)
    return img, d


def furnace_orchid():
    img, d = base()
    # 六片尖瓣，灼红
    for cx, cy in [(64, 38), (42, 50), (38, 70), (64, 78), (90, 70), (86, 50)]:
        d.polygon([(cx, cy - 20), (cx + 14, cy + 4), (cx, cy + 16), (cx - 14, cy + 4)], fill=(206, 74, 46, 255))
        d.polygon([(cx, cy - 12), (cx + 8, cy + 4), (cx, cy + 10), (cx - 8, cy + 4)], fill=(240, 132, 62, 255))
    d.ellipse([52, 52, 76, 76], fill=(255, 214, 120, 255))
    d.ellipse([58, 58, 70, 70], fill=(255, 248, 214, 255))
    # 热浪
    d.arc([30, 24, 98, 92], 300, 360, fill=(255, 170, 90, 130), width=2)
    img.save(os.path.join(OUT, "FurnaceOrchid.png"))
    print("FurnaceOrchid.png")


def storm_bloom():
    img, d = base()
    for cx, cy in [(64, 40), (44, 52), (42, 70), (64, 76), (86, 70), (84, 52)]:
        d.ellipse([cx - 16, cy - 14, cx + 16, cy + 14], fill=(86, 132, 208, 255))
        d.ellipse([cx - 9, cy - 8, cx + 9, cy + 8], fill=(132, 186, 238, 255))
    d.ellipse([56, 56, 72, 72], fill=(226, 244, 255, 255))
    # 电弧
    d.line([(64, 18), (52, 44), (66, 46), (54, 74)], fill=(240, 250, 255, 220), width=3)
    d.arc([26, 26, 102, 102], 20, 80, fill=(150, 200, 255, 120), width=2)
    img.save(os.path.join(OUT, "StormBloom.png"))
    print("StormBloom.png")


def rosa_arcana():
    img, d = base()
    # 玫瑰式的层叠花瓣
    d.ellipse([36, 34, 92, 90], fill=(96, 52, 120, 255))
    d.ellipse([42, 40, 86, 84], fill=(132, 70, 154, 255))
    d.ellipse([50, 48, 78, 76], fill=(168, 96, 176, 255))
    d.ellipse([56, 54, 72, 70], fill=(206, 140, 200, 255))
    d.ellipse([61, 59, 67, 65], fill=(244, 206, 240, 255))
    # 被抽走的思想：细小的丝线
    for sx, sy in [(30, 40), (98, 46), (34, 84), (94, 80)]:
        d.line([(64, 64), (sx, sy)], fill=(196, 150, 220, 110), width=1)
    img.save(os.path.join(OUT, "RosaArcana.png"))
    print("RosaArcana.png")


furnace_orchid()
storm_bloom()
rosa_arcana()
