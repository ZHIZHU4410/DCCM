#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 WreckingBallOverhaul 用的 data.cdb（供 csproj 走 CDB diff 打进 res.pak）。

只改 weapon 表里 item = WreckingBall 这一行的 strikeChain（武器本体/动作/特效/贴图均不动）。

原版 strikeChain（4 段连击）：
    [0] AtkWreckingballA  power 200  平A（运行期跳过）
    [1] AtkWreckingballB  power 180  平A（运行期跳过）
    [2] AtkWreckingballC  power 120  第3a：丢出流星锤
    [3] AtkWreckingballD  power 250  第4a：收回流星锤

【为什么不能删数组条目】
    _Weapon.__inst_construct__ 里有两个循环：
        for i in 0..skills.length:  skills.push(WeaponSkill(strikeChain[i]))
        for i in 0..skills.length:  areas[i] = Area(strikeChain[i].area)
    而 Weapon.get_curSkillInf() 直接用 get_cycle() 索引 strikeChain，
    WreckingBall.onExecute 又硬编码 `if (cycle == 2) {...丢球...}`。
    所以 strikeChain 必须保持 4 条、索引 2/3 仍是 C/D，否则第二个循环会越界读、
    或 cycle==2 再也取不到丢球段。连击的钳制改由 C# 代码完成
    （WreckingBallOverhaulMain 里的 incrementCycle Hook），效果等价且不会崩。

本补丁做的事（只负责「前摇」与「丢出可暴击」）：
  1) 第3a 前摇减少 —— 降低 charge（起手到可出手的时间）、提高 animSpd（动作播放速度）：
     · 第3a 丢球：charge 0.7 -> 0.25，animSpd 1.0 -> 1.6
  2) 第3a 丢出可暴击 —— canCrit false -> true。
     原版只有第4a（收回段 AtkWreckingballD）是 canCrit=true，
     丢出段 AtkWreckingballC 是 canCrit=false，所以原版丢出去永远不暴击。
     依据：WreckingBall 丢球时
         AttackData attackData = _AttackUtils.createFromHeroWeapon(this, null);
         attackData.setTag(2, get_curSkillInf().canCrit);
     这里的 canCrit 直接取自 strikeChain 的 canCrit 字段；
     _AttackUtils.createFromHero 会据此走暴击结算（命中结果为 Critical 时乘 critMul）。

【伤害 ×5 为什么不放这里】
    丢出伤害走 AttackData.baseDmg = 当前段 power，而 _AttackUtils.hit 是同步算完
    finalDmg 的。如果在 CDB 里把 power 改成 600，运行期再也拿不到「原始 120」这个
    基准，一旦重复乘就会滚雪球（一次丢出会沿路径命中多个敌人）。
    因此伤害倍率统一由 C# 侧 Hook_WreckingBall.hitFromWeapon 处理：
    命中前 power ×5、命中后立刻还原，每次命中都精确 ×5 且不会累积。
    数据侧只保留原版 power（120），职责单一。

其余字段（animId / hitFrame / area / limit / pct / scale / power / critMul / 前两段）全部原样保留。

用法: python patch_wreckingball_cdb.py
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

ITEM_ID = "WreckingBall"

# 前摇调整：animId -> (charge_new, animSpd_new)
WINDUP = {
    "AtkWreckingballC": (0.25, 1.6),   # 第3a 丢出：charge 0.7 -> 0.25, animSpd 1.0 -> 1.6
}

# 丢出段开启暴击（原版只有收回段 canCrit=true）
CRIT_ANIM_ID = "AtkWreckingballC"


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

    weapon = _sheet(data, "weapon")
    row = None
    for line in weapon.get("lines", []):
        if isinstance(line, dict) and line.get("item") == ITEM_ID:
            row = line
            break
    if row is None:
        raise SystemExit(f"ERROR: weapon 表中未找到 item={ITEM_ID} 的行")

    chain = row.get("strikeChain")
    if not isinstance(chain, list) or len(chain) != 4:
        raise SystemExit(f"ERROR: {ITEM_ID}.strikeChain 预期长度 4，实际 "
                         f"{len(chain) if isinstance(chain, list) else type(chain)}")
    print(f"  连段: {[c.get('animId') for c in chain]}")

    changed = 0
    for c in chain:
        anim = c.get("animId")
        if anim in WINDUP:
            charge_new, spd_new = WINDUP[anim]
            charge_old = c.get("charge")
            spd_old = c.get("animSpd")
            if charge_old != charge_new:
                print(f"  {anim}  charge   {charge_old} -> {charge_new}   (前摇减少)")
                c["charge"] = charge_new
                changed += 1
            if spd_old != spd_new:
                print(f"  {anim}  animSpd  {spd_old} -> {spd_new}   (动作加快)")
                c["animSpd"] = spd_new
                changed += 1
        if anim == CRIT_ANIM_ID and c.get("canCrit") is not True:
            print(f"  {anim}  canCrit  {c.get('canCrit')} -> True   (丢出可暴击)")
            c["canCrit"] = True
            changed += 1

    if changed == 0:
        print("  已是目标数值，无需修改")

    # 断言：连段条数与索引必须保持原样，否则游戏自身构造 Weapon 时会崩
    assert len(row["strikeChain"]) == 4, "strikeChain 长度必须保持 4"
    assert row["strikeChain"][2]["animId"] == "AtkWreckingballC"
    assert row["strikeChain"][3]["animId"] == "AtkWreckingballD"
    assert row["strikeChain"][2]["canCrit"] is True, "丢出段 must be canCrit"

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print(f"done -> {OUT} ({os.path.getsize(OUT)} bytes)")


if __name__ == "__main__":
    main()
