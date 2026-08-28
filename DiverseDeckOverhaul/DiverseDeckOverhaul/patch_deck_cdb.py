#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
为 DiverseDeckOverhaul 生成 data.cdb：
以 MDK v35 模板为基础，将四张万智牌组卡牌的 castCD 改为 0（切牌无冷却）。
输出与本脚本同级的 data.cdb，供 csproj 的 BuildResPak 目标生成 CDB diff 并打包进 res.pak。
用法: python patch_deck_cdb.py
"""
import json
import os

SRC = r"D:\steama\steamapps\common\Dead Cells\coremod\core\mdk\databases\v35\data.cdb"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data.cdb")

DECK_IDS = {
    "DiverseDeckJuggernaut",
    "DiverseDeckCatalyst",
    "DiverseDeckElectro",
    "DiverseDeckWatcher",
}


def main() -> None:
    with open(SRC, encoding="utf-8") as f:
        data = json.load(f)

    changed = 0
    for sheet in data.get("sheets", []):
        if sheet.get("name") != "item":
            continue
        for line in sheet.get("lines", []):
            if isinstance(line, dict) and line.get("id") in DECK_IDS:
                old = line.get("castCD")
                line["castCD"] = 0
                print(f"  {line['id']}: castCD {old} -> 0")
                changed += 1

    if changed != 4:
        raise SystemExit(f"ERROR: 预期修改 4 张牌，实际 {changed} 张，请检查模板结构")

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print(f"done -> {OUT} ({os.path.getsize(OUT)} bytes)")


if __name__ == "__main__":
    main()
