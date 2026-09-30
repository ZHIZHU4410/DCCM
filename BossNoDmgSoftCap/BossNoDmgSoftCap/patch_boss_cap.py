#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
BossNoDmgSoftCap 数据补丁生成器
================================
把 mob sheet 里所有 Boss 的 props.dmgSoftCap 删掉，从而移除 Boss 的伤害软上限。

【为什么要删掉整个字段】
    dc.en._Mob.__inst_construct__ 的逻辑：
        if (_infos.props.dmgSoftCap == null) {
            // 直接 return，dmgSoftCapMin/Max 保持 _Entity 构造时的 -1
        } else {
            dmgSoftCapMin = dmgSoftCap[0];
            dmgSoftCapMax = dmgSoftCap[1];
        }
    dc.Entity.getCappedFinalDamage() 的调用点（dc.Entity 第 7660 行）有守卫：
        val = dmgSoftCapMax;
        if (!isNaN(val) && dmgSoftCapMax > 0) { ... 走 getCappedFinalDamage ... }
    所以 dmgSoftCapMax 保持 -1 时，软上限分支根本不会进入 —— 删字段即可彻底去掉软上限。

【怎么判定 Boss】
    res/data.cdb 里全部 135 个 mob 中，props.dmgSoftCap 非空的正好 17 个，
    而且它们的 group 全部 = 5（Boss 组）。因此判据用：
        group == 5 且 props.dmgSoftCap 非空
    既精确命中这 17 个 Boss，也不会误伤普通怪。

输出：与本脚本同目录的 data.cdb，供 csproj 生成 diff 并打进 res.pak。

用法: python patch_boss_cap.py
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
WORKSPACE = os.path.abspath(os.path.join(HERE, "..", ".."))

CANDIDATES = [
    os.path.join(WORKSPACE, "res", "data.cdb"),
    r"D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS\res\data.cdb",
    r"D:\steama\steamapps\common\Dead Cells\coremod\core\mdk\databases\v35\data.cdb",
]

OUT = os.path.join(HERE, "data.cdb")

BOSS_GROUP = 5


def _pick_source() -> str:
    for p in CANDIDATES:
        if os.path.exists(p):
            return p
    raise SystemExit("ERROR: 找不到基准 data.cdb，试过:\n  " + "\n  ".join(CANDIDATES))


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    src = _pick_source()
    print(f"基准 CDB: {src}")

    with open(src, encoding="utf-8") as f:
        data = json.load(f)

    mob = None
    for s in data.get("sheets", []):
        if s.get("name") == "mob":
            mob = s
            break
    if mob is None:
        raise SystemExit("ERROR: 未找到 sheet: mob")

    changed = []
    for line in mob.get("lines", []):
        if not isinstance(line, dict):
            continue
        props = line.get("props")
        if not isinstance(props, dict):
            continue
        if line.get("group") != BOSS_GROUP:
            continue
        if "dmgSoftCap" not in props:
            continue
        cap = props.pop("dmgSoftCap")
        changed.append((line.get("id"), cap))

    for mid, cap in changed:
        print(f"  {mid:20s} 删除 dmgSoftCap {cap}")

    if not changed:
        print("  没有需要修改的 Boss（可能已处理过）")
    print(f"共处理 {len(changed)} 个 Boss")

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print(f"done -> {OUT} ({os.path.getsize(OUT)} bytes)")


if __name__ == "__main__":
    main()
