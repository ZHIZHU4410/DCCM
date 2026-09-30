#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 DashOverhaul 用的 data.cdb（供 csproj 走 CDB diff 打进 res.pak）。

只改 item 表里 id = Dash 这一行（其它 645 条 item、weapon、mob、level... 全部保持原样）。

原版 Dash 数据（res/data.cdb -> sheets.item -> id == "Dash"）：
    "props": {"speed": 5, "range": 13, "power": [100], "cooldown": 2, "speed2": 0.25,
              "buff": 0.2, "height": 0.75, "limit": 0.75}
    "castCD": 10
    "commonProps": {"autoTransformAfter": 0.25, "item": "BackDash"}

本补丁做 3 件事：
  1) castCD 10 -> 0
     依据：InventItem.getActiveCooldown() 返回的就是 `_itemData.castCD * 减耗系数`，
     主动技能每次释放后走 Hero.startCooldownForItem(item, castCD)。
     castCD = 0 => 技能冷却 0 秒，可以连续释放。
     （注意 castCD 同时还被 InventItem.getHeartsCost() 用作 castCD/3，
       所以 Dash 的 "hearts cost" 会变成 0 —— 只影响 BossRush 的标价，不影响伤害。）

  2) props.power [100] -> [600]
     +500% 就是最终伤害 = 原版 x6。
     依据：Dash.fixedUpdate 命中敌人时走
         _AttackUtils.createFromHeroItem(owner, item, props.power)
     直接把 props.power 当 baseDmg 用，所以改数据即可整体放大。
     本模组自己实现的冲刺路径伤害沿用了同一条结算链
     （createFromHeroItem -> hit），因此数据补丁同样生效。
     每次冲刺对同一个敌人只结算一次，不存在重复乘算滚雪球的问题。

  3) commonProps.item "BackDash" -> "Dash"
     依据：Dash.onEnd() 在冲刺结束时调用 hero.transformInventoryItem(item, null)，
     而 transformInventoryItem 内部读的是 `i._itemData.commonProps.item` 作为变身目标，
     再 i.clone(true, 目标id) 替换掉手里的技能。
     原版由此变成 BackDash（反向冲刺），再使用就是反方向冲刺。
     改成指向自己 => 永远只是重新 clone 一个 Dash，技能栏里永远是 Dash，
     也就实现了「只能释放 Dash」。

其余字段（speed / range / cooldown / speed2 / buff / height / limit / icon /
gameplayDesc / tags / tier1 / tier2 / legendAffixes ...）一律原样保留，
冲刺速度与冲刺距离因此保持原版手感。

用法: python patch_dash_cdb.py
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

ITEM_ID = "Dash"
NEW_CAST_CD = 0.0          # 需求：castCD = 0
POWER_MULT = 6.0           # 需求：伤害 +500%  =>  x6
STAY_ITEM_ID = "Dash"      # 需求：只能释放 Dash（永不变成 BackDash）
EXPECT_BACKDASH = "BackDash"


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


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    src = _pick_source()
    print(f"基准 CDB: {src}")

    with open(src, encoding="utf-8") as f:
        data = json.load(f)

    item = _sheet(data, "item")
    row = None
    for line in item.get("lines", []):
        if isinstance(line, dict) and line.get("id") == ITEM_ID:
            row = line
            break
    if row is None:
        raise SystemExit(f"ERROR: item 表中未找到 id={ITEM_ID} 的行")

    props = row.get("props")
    if not isinstance(props, dict):
        raise SystemExit("ERROR: Dash.props 不是对象")

    common = row.get("commonProps")
    if not isinstance(common, dict):
        raise SystemExit("ERROR: Dash.commonProps 不是对象")

    changed = 0

    # ---- 1) castCD -> 0 ----
    old_cd = row.get("castCD")
    if old_cd != NEW_CAST_CD:
        print(f"  castCD              {old_cd} -> {NEW_CAST_CD}      (技能冷却 0 秒)")
        row["castCD"] = NEW_CAST_CD
        changed += 1

    # ---- 2) props.power x6（+500%）----
    power = props.get("power")
    if not isinstance(power, list) or not power:
        raise SystemExit(f"ERROR: Dash.props.power 预期是非空数组，实际 {power!r}")
    new_power = [float(p) * POWER_MULT for p in power]
    if new_power != power:
        print(f"  props.power         {power} -> {new_power}   (伤害 +500%)")
        props["power"] = new_power
        changed += 1

    # ---- 3) 变身目标指回自己 -> 永远只能放 Dash ----
    old_item = common.get("item")
    if old_item != STAY_ITEM_ID:
        print(f"  commonProps.item    {old_item} -> {STAY_ITEM_ID}    (不再变身 BackDash)")
        common["item"] = STAY_ITEM_ID
        changed += 1

    if changed == 0:
        print("  已是目标数值，无需修改")

    # ---- 断言：结构没被破坏 ----
    assert row["castCD"] == NEW_CAST_CD, "castCD 必须为 0"
    assert row["props"]["power"] == new_power, "power 倍率写入失败"
    assert row["commonProps"]["item"] == STAY_ITEM_ID, "变身目标写入失败"
    # 冲刺手感相关的字段必须原样保留
    for k in ("speed", "range", "cooldown", "speed2", "buff", "height", "limit"):
        assert k in row["props"], f"props.{k} 丢失"
    assert row["props"]["speed"] == 5, "冲刺速度被意外改动"
    assert row["props"]["range"] == 13, "冲刺距离被意外改动"
    # 不能误改 BackDash 自己那一行
    for line in item.get("lines", []):
        if isinstance(line, dict) and line.get("id") == "BackDash":
            assert line["commonProps"]["item"] == ITEM_ID, "BackDash 被意外改动"
            break

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print(f"done -> {OUT} ({os.path.getsize(OUT)} bytes)")


if __name__ == "__main__":
    main()
