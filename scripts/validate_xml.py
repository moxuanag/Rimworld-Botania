#!/usr/bin/env python3
"""CI 用的轻量自检：确认仓库里所有 XML 都能被解析。

本地开发用的完整校验器（需要游戏本体 Defs）不放进仓库，
这里只做与游戏无关的语法级检查，防止把坏 XML 打进发布包。
"""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCAN_DIRS = ("About", "Defs", "Patches", "Languages")


def main() -> int:
    files = []
    for d in SCAN_DIRS:
        base = ROOT / d
        if base.is_dir():
            files.extend(sorted(base.rglob("*.xml")))

    if not files:
        print("[X] 没有找到任何 XML，检查目录结构")
        return 1

    failed = []
    for path in files:
        try:
            ET.parse(path)
        except ET.ParseError as exc:
            failed.append((path, exc))

    print(f"扫描 {len(files)} 个 XML")
    if failed:
        for path, exc in failed:
            print(f"[X] {path.relative_to(ROOT)}: {exc}")
        return 1
    print("[PASS] 全部 XML 语法正确")
    return 0


if __name__ == "__main__":
    sys.exit(main())
