#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ForgeQualityCells — data.cdb 生成脚本（Python，秒级完成，无需 CastleDB 编辑器）

以 MDK v35 模板 data.cdb（JSON 格式）为底，在 `truelle` 表里新增一行：

    id      = ForgeQualityCellCost
    value0  = [1, 2, 3, 4, 5, 6, 8, 10]     ← 每一档"品质"要花的细胞数
    value1  = 1

`truelle`（"trowel / 小铲子"）是原版专门放全局调参常量的表，
`ForgeRerollCost`（刷新词条的金币比例）/ `ForgeRefineCost`（提升品质的金币比例）
都在这一张表里 —— 我们照抄它们的形状，只是把"金币"换成"细胞"。

生成的数据由 csproj 里的 BuildResPak 目标走
    DCCMTool cdb diff -> diff.pak -> pak unpack
落到 `Assets/data.cdb_/truelle/ForgeQualityCellCost.json`，
再由 MDK 打包成 res.pak 装进游戏。

⚠️ Python 侧只负责"数据"。真正的行为（面板里多一行、扣细胞、加 QualityUp）
   全部在 C# 侧：ForgeQualityCellsMain.cs / ForgeQualityForgeRow.cs。

用法:
    python patch_forge_quality_cdb.py            # 生成 ./data.cdb
    python patch_forge_quality_cdb.py --check    # 只校验并打印结果
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE = r"D:\steama\steamapps\common\Dead Cells\coremod\core\mdk\databases\v35\data.cdb"
OUT = os.path.join(HERE, "data.cdb")

ROW_ID = "ForgeQualityCellCost"

# 每一档要花的细胞数。**下标 = 物品当前的品质档位**，也就是 getUpgradeLevel()：
#
#   下标 0 → 无印  → 买一次变成 "+"       50
#   下标 1 → "+"   → 变成 "++"           100
#   下标 2 → "++"  → 变成 "S"            200
#   下标 3 → "S"   → 变成 "L"（传奇）     400   ← S→L 这一档
#   下标 4 → 已经是 L（到顶，这一行会直接隐藏；留着只是让表形状和原版
#            ForgeRerollCost 的 5 档（0/+/++/S/L）对齐）
#
# 倍率统一 ×2（400 → 200 → 100 → 50），想改价直接改这里；
# 列表不够长时 C# 侧会退回"最后一档 + 每级递增"。
CELL_COSTS = [50, 100, 200, 400, 800]

# 图标沿用原版锻造那条（cardIcons.png 第 (55,21) 格，和 ForgeCellCosts 同一格）
ICON = {"x": 55, "y": 21, "file": "cardIcons.png", "size": 24}

COMMENT = "Cells a investir par niveau de qualite, index = palier actuel, [3] = passage en Legendaire (mod ForgeQualityCells)"


def main():
    check = "--check" in sys.argv

    with open(TEMPLATE, encoding="utf-8") as f:
        data = json.load(f)

    sheets = {s["name"]: s for s in data["sheets"]}
    if "truelle" not in sheets:
        raise SystemExit("ERROR: 模板 data.cdb 里没有 truelle 表")

    sheet = sheets["truelle"]
    lines = sheet["lines"]

    # 去重（脚本可以重复执行）
    before = len(lines)
    lines[:] = [l for l in lines if not (isinstance(l, dict) and l.get("id") == ROW_ID)]
    if len(lines) != before:
        print("已移除旧的 %s 行" % ROW_ID)

    row = {
        "id": ROW_ID,
        "tile": dict(ICON),
        "comment": COMMENT,
        # 数组 => 加载后是 ArrayBytes_Float（和 ForgeRerollCost.value0 同一形状）
        "value0": [float(v) for v in CELL_COSTS],
        "value1": 1,
    }
    lines.append(row)

    print("truelle 行: %d（追加 %s = %s 细胞/档）" %
          (len(lines), ROW_ID, CELL_COSTS))

    if check:
        print("--check: 不写文件")
        return

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print("done -> %s (%d bytes)" % (OUT, os.path.getsize(OUT)))


if __name__ == "__main__":
    main()
