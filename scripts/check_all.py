#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""一条命令跑完所有交付前检查（交付闸门）。

为什么需要它：以前的坑（XML 字段名 / 向量元数 / ParentName / 贴图缺失 / 组件字段不匹配）
分散在 5 个脚本里，人（和模型）很容易漏跑其中一个，然后「进游戏才炸」。

用法：
    python scripts/check_all.py            # 跑全部
    python scripts/check_all.py --fast     # 跳过 dotnet build（只跑静态检查）

退出码 0 = 全部通过；非 0 = 有失败项（此时不许部署/提交）。
"""
import argparse
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEV = REPO.parent / "_botania-dev"
PY = sys.executable


def find_python():
    """优先用带 Pillow 的 venv（贴图检查需要）。"""
    candidates = [
        Path(r"E:\01 All In One\00 AI\AItool\venv\Scripts\python.exe"),
        Path(r"C:\Python313\python.exe"),
    ]
    for c in candidates:
        if c.exists():
            return str(c)
    return PY


def run(label, cmd, cwd=None, timeout=900):
    print(f"\n{'=' * 70}\n[{label}] {' '.join(str(c) for c in cmd)}\n{'=' * 70}")
    try:
        proc = subprocess.run(
            [str(c) for c in cmd],
            cwd=str(cwd or REPO),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
        )
    except subprocess.TimeoutExpired:
        print("  超时")
        return False
    out = (proc.stdout or "") + (proc.stderr or "")
    # 只打印最后若干行，避免刷屏；失败时全打
    lines = [ln for ln in out.splitlines() if ln.strip()]
    keep = lines if proc.returncode != 0 else lines[-6:]
    for ln in keep:
        print("  " + ln)
    ok = proc.returncode == 0
    print(f"  -> {'PASS' if ok else 'FAIL'}")
    return ok


def main(argv):
    ap = argparse.ArgumentParser(description="Botania 全量交付前检查")
    ap.add_argument("--fast", action="store_true", help="跳过 dotnet build")
    ap.add_argument("--no-deploy", action="store_true", help="（默认就不部署，此参数仅为兼容保留）")
    args = ap.parse_args(argv)

    py = find_python()
    results = []

    if not args.fast:
        results.append(("编译", run("1/6 编译", ["dotnet", "build", "-c", "Release", "--nologo"], cwd=REPO / "Source")))

    checks = [
        ("2/6 validate_mod", [py, DEV / "validate_mod.py"]),
        ("3/6 check_comp_fields", [py, DEV / "check_comp_fields.py"]),
        ("4/6 check_xml_fields", [py, DEV / "check_xml_fields.py"]),
        ("5/6 check_def_refs", [py, DEV / "check_def_refs.py"]),
        ("6/6 check_component_rules", [py, DEV / "check_component_rules.py"]),
    ]
    for label, cmd in checks:
        if not Path(cmd[1]).exists():
            print(f"\n[!] 找不到检查脚本 {cmd[1]}，跳过")
            continue
        results.append((label, run(label, cmd)))

    print("\n" + "=" * 70)
    failed = [name for name, ok in results if not ok]
    if failed:
        print(f"[FAIL] {len(failed)} 项未通过：{', '.join(failed)}")
        print("不许部署 / 不许提交。先修到全绿。")
        return 1
    print(f"[PASS] 全部 {len(results)} 项通过。可以部署：powershell -File deploy.ps1")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
