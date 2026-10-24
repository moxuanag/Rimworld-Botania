"""生成启命英相关的贴图：肉床地面、启命英本体、血肉之种。"""
from PIL import Image, ImageDraw
import os
import random

ROOT = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.normpath(os.path.join(ROOT, ".."))
TEX = os.path.join(PROJ, "Textures")

random.seed(7)


def flesh_bed():
    path = os.path.join(TEX, "Terrain", "Surfaces")
    os.makedirs(path, exist_ok=True)
    img = Image.new("RGBA", (256, 256), (96, 34, 40, 255))
    d = ImageDraw.Draw(img)
    # 肉纹：暗红底 + 血管 + 高光
    for i in range(260):
        x = random.randint(0, 255)
        y = random.randint(0, 255)
        r = random.randint(4, 16)
        shade = random.randint(-16, 16)
        d.ellipse([x - r, y - r, x + r, y + r],
                  fill=(max(60, 96 + shade), max(20, 34 + shade // 2), max(24, 40 + shade // 2), 255))
    for i in range(14):
        x = random.randint(0, 255)
        y = random.randint(0, 255)
        pts = [(x, y)]
        for _ in range(5):
            x += random.randint(-40, 40)
            y += random.randint(-40, 40)
            pts.append((x, y))
        d.line(pts, fill=(140, 52, 58, 220), width=2)
    for i in range(40):
        x = random.randint(0, 255)
        y = random.randint(0, 255)
        d.ellipse([x, y, x + 3, y + 3], fill=(180, 92, 96, 200))
    img.save(os.path.join(path, "Botania_FleshBed.png"))
    print("Botania_FleshBed.png")


def life_orchid():
    path = os.path.join(TEX, "Things", "Building", "Botania")
    os.makedirs(path, exist_ok=True)
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 茎
    d.line([(64, 84), (64, 114)], fill=(78, 60, 44, 255), width=5)
    # 肉色花座
    d.ellipse([30, 84, 98, 112], fill=(112, 48, 54, 255))
    d.ellipse([40, 88, 88, 106], fill=(146, 62, 68, 255))
    # 花瓣：粉紫 + 肉红交替
    petals = [(64, 40), (40, 54), (44, 78), (64, 86), (84, 78), (88, 54)]
    for i, (cx, cy) in enumerate(petals):
        color = (196, 128, 176, 255) if i % 2 == 0 else (176, 74, 92, 255)
        d.ellipse([cx - 18, cy - 16, cx + 18, cy + 16], fill=color)
        d.ellipse([cx - 10, cy - 8, cx + 10, cy + 8], fill=(232, 186, 214, 255))
    # 花心：发光的细胞核
    d.ellipse([54, 52, 74, 72], fill=(120, 42, 60, 255))
    for px, py in [(60, 58), (67, 61), (63, 67), (70, 66)]:
        d.ellipse([px - 3, py - 3, px + 3, py + 3], fill=(255, 214, 120, 255))
    img.save(os.path.join(path, "LifeOrchid.png"))
    print("LifeOrchid.png")


def flesh_seed():
    path = os.path.join(TEX, "Things", "Item", "Botania")
    os.makedirs(path, exist_ok=True)
    img = Image.new("RGBA", (128, 128), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse([34, 40, 94, 92], fill=(122, 46, 54, 255))
    d.ellipse([42, 46, 86, 84], fill=(158, 66, 74, 255))
    d.ellipse([52, 54, 76, 76], fill=(196, 96, 100, 255))
    # 血管
    for pts in [[(40, 60), (56, 66), (66, 60)], [(88, 62), (72, 70), (62, 80)], [(50, 84), (62, 74), (78, 80)]]:
        d.line(pts, fill=(94, 32, 40, 255), width=2)
    img.save(os.path.join(path, "FleshSeed.png"))
    print("FleshSeed.png")


flesh_bed()
life_orchid()
flesh_seed()
