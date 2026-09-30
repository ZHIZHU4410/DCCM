#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 LightningWhipBoost 用的 data.cdb（供 csproj 走 CDB diff 打进 res.pak）。

只改 item 表里 id = LightningWhip 这一行的 props（武器本体、动作、特效均不动）：

    props.range   2   -> 20     连锁索敌范围 ×10
    props.prct    0.5 -> 5.0    连锁命中伤害 ×10

依据（见 GamePseudocode/dc.tool.weap/LightningWhip.cs）：
  · 连锁范围：onExecute 里 `Entity nextTarget = getNextTarget(arrayObj, base.itemInf.props.range);`
    —— 连锁每一跳的搜索范围就是 item 的 props.range（原版 2）。
  · 连锁伤害：连锁命中时
        attackData2.overrideBaseDamage(get_curSkillInf().power * base.itemInf.props.prct);
    —— 即「本段武器 power × item 的 props.prct」（原版 0.5），故 prct 改 5.0 即连锁伤害 ×10。

注意：item.props.range 只被连锁搜索使用；首次从主角索敌用的 range 来自
weapon 表 strikeChain[].props.range（原版 6），所以本补丁不会改变鞭子本身的攻击距离。

用法: python patch_lightningwhip_cdb.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WORKSPACE = os.path.abspath(os.path.join(HERE, "..", ".."))

# 基准 CDB：优先用工作区里解包出来的游戏资源，找不到再退回 MDK v35 模板（两者内容一致）
CANDIDATES = [
    os.path.join(WORKSPACE, "res", "data.cdb"),
    r"D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS\res\data.cdb",
    r"D:\steama\steamapps\common\Dead Cells\coremod\core\mdk\databases\v35\data.cdb",
]

OUT = os.path.join(HERE, "data.cdb")

ITEM_ID = "LightningWhip"

RANGE_OLD, RANGE_NEW = 2, 20       # 连锁范围 ×10
PRCT_OLD, PRCT_NEW = 0.5, 5.0      # 连锁伤害 ×10


def _pick_source() -> str:
    for p in CANDIDATES:
        if os.path.exists(p):
            return p
    raise SystemExit("ERROR: 找不到基准 data.cdb，试过:\n  " + "\n  ".join(CANDIDATES))


def _sheet(data, name):
    for s in data.get("sheets", []):
        if s.get("name") == name:
            return s
    raise SystemExit(f"ERROR: 未找到 sheet: {name}")


def _line(sheet, key, value):
    for line in sheet.get("lines", []):
        if isinstance(line, dict) and line.get(key) == value:
            return line
    return None


def main() -> None:
    src = _pick_source()
    print(f"基准 CDB: {src}")

    with open(src, encoding="utf-8") as f:
        data = json.load(f)

    item = _line(_sheet(data, "item"), "id", ITEM_ID)
    if item is None:
        raise SystemExit(f"ERROR: item 表中未找到 id={ITEM_ID} 的行")

    props = item.setdefault("props", {})
    changed = False

    if props.get("range") != RANGE_NEW:
        print(f"  props.range  {props.get('range')} -> {RANGE_NEW}    (连锁范围 ×10)")
        props["range"] = RANGE_NEW
        changed = True

    if props.get("prct") != PRCT_NEW:
        print(f"  props.prct   {props.get('prct')} -> {PRCT_NEW}    (连锁伤害 ×10)")
        props["prct"] = PRCT_NEW
        changed = True

    if not changed:
        print("  已是目标数值，无需修改")

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print(f"done -> {OUT} ({os.path.getsize(OUT)} bytes)")


if __name__ == "__main__":
    main()
