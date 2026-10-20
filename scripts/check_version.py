#!/usr/bin/env python3
"""CI 用：校验 About.xml 的 <modVersion> 与即将发布的 tag 一致。

版本号是发布包的唯一标识，tag 与 modVersion 不一致会让人分不清下载的是哪版，
所以在打包前直接让它失败。
"""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ABOUT = ROOT / "About" / "About.xml"
ASSEMBLY = ROOT / "Assemblies" / "Botania.dll"


def main() -> int:
    expected = sys.argv[1] if len(sys.argv) > 1 else None
    if not expected:
        print("用法: check_version.py <version>")
        return 2

    if not ABOUT.is_file():
        print("[X] 缺少 About/About.xml")
        return 1

    meta = ET.parse(ABOUT).getroot()
    mod_version = (meta.findtext("modVersion") or "").strip()
    package_id = (meta.findtext("packageId") or "").strip()

    print(f"packageId   : {package_id or '<缺失>'}")
    print(f"modVersion  : {mod_version or '<缺失>'}")
    print(f"tag version : {expected}")

    ok = True
    if not package_id:
        print("[X] About.xml 缺少 packageId，游戏会把模组判为无效")
        ok = False
    if not mod_version:
        print("[X] About.xml 缺少 modVersion")
        ok = False
    elif mod_version.lstrip("v") != expected.lstrip("v"):
        print(f"[X] modVersion({mod_version}) 与 tag({expected}) 不一致")
        ok = False

    if not ASSEMBLY.is_file() or ASSEMBLY.stat().st_size == 0:
        print("[X] Assemblies/Botania.dll 缺失或为空，先在本地编译再提交")
        ok = False

    print("[PASS] 版本与程序集检查通过" if ok else "[FAIL] 检查未通过")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
