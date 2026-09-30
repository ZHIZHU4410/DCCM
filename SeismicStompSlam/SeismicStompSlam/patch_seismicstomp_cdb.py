#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 SeismicStompSlam 用的 data.cdb（供 csproj 走 CDB diff 打进 res.pak）。

本补丁只动两张表：

  A) item 表 id = SeismicStomp（震地冲击 / Telluric Shock）这一行的 props.effectCD
  B) hero 表（Beheaded / Richter）的 props.heroLife ×20  → hero 血量翻 20 倍

其它字段、其它表一律原样保留。

=====================================================================
【A. 为什么只需要改 effectCD】
    震地冲击的「砸地伤害」全部在 dc.pow.SeismicStomp.stompHit 里结算：
        · 特效/石块：fx.khStomp(x, y, fxRc, fxC, fxBigRockTile, fxSmallRockTile)
        · 音效    ：sfx/active/stomp_char3.wav
        · 震屏    ：viewport.shakeS(0.0, 0.3, 1.0)
        · 范围伤害：_AttackUtils.createFromHeroItem(hero, item, props.power) + _AttackUtils.hit
    其中「同一个敌人多久才能再吃一次这次伤害」由 props.effectCD 控制（原版 1.5 秒）。
    原版一次技能只在同一帧里调两次 stompHit（左右各一格），1.5 秒的间隔是为了防止
    同一次砸地因为覆盖两列而对同一个敌人重复结算。

    但本模组是「每移动一格就砸一次」：跑动时大约每 2~4 帧就跨过一格。
    如果 effectCD 还是 1.5 秒，那么同一个敌人 1.5 秒内只会吃到一次伤害 ——
    表现就是「特效一直在炸，但敌人几乎不掉血」，看起来像模组坏了。
    所以这里把它降到 0.05 秒（60fps 下约 3 帧），让连续下砸真的能持续结算伤害。

    这个改动不会让原版技能变成「一次打两下」：
    onOwnerDiveAttackLand 里左右两次 stompHit 是**同一帧**调用的，
    第一次调用就把该敌人的 CdInst 置成 3 帧，第二次调用时 fastCheck 里已存在 → 直接跳过。
    所以原版一次技能对同一敌人仍然只结算一次伤害。

=====================================================================
【B. hero 血量 ×20 为什么放在数据里，而不是 hook】
    hero 的最大生命只在一个地方算出来 —— Hero.updateMaxLife()：
        int num4 = _Const.scaleHeroLifeToTier(get_infos().props.heroLife,
                                              inventory, brutalityTier, tacticTier, survivalTier);
        base.maxLife = num4;
        ... 然后按 life/maxLife、bonusLife/maxLife、rally/maxLife 三个比例重建当前值
    而 dc._Const.scaleHeroLifeToTier 内部只有一行关键算式：
        double num5 = @base * lifeScalingFromTier;      <-- 对 heroLife 完全线性
    也就是说 heroLife 就是「0 层卷轴时的基础血量」，后面所有卷轴/装备/关卡加成都是在它之上乘算。

    所以只要把 CDB 里 heroLife 从 100 改成 2000，之后**任何**触发 updateMaxLife 的时机
    （吃卷轴、换装、换关、复活…）出来的最大生命天然就是 20 倍：
      · 不需要 hook，也不会出现「×20 之后被游戏重算覆盖」；
      · 更不会出现「每次重算都再乘一次」的复利爆炸。
    updateMaxLife 内部是按比例重建当前血量/护盾的，所以血量百分比也不会跳变。

    两张 hero 行都要改：
      · Beheaded —— 普通流程用的 hero 数据
      · Richter  —— 用里希特（Castlevania）角色时的 hero 数据
    只改 heroLife，runSpd / rally* / gravityFactor / coyoteTime 等全部保持原版。

【伤害数值为什么不在这里改】
    和 DamageAuraBoost / WreckingBallOverhaul 的分工保持一致：
    「数值」放数据补丁（Assets/data.cdb_/...），「逻辑」放 C# 代码（SeismicStompSlamMain.cs）。
    想调下砸伤害就改这里的 power，想调触发节奏就改 effectCD，
    想调血量倍率就改 HERO_LIFE_MUL，重新跑构建脚本即可。

用法: python patch_seismicstomp_cdb.py
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

ITEM_ID = "SeismicStomp"

# 同一敌人的重复命中间隔（秒）。0.05 ≈ 60fps 下的 3 帧，跑动时约等于「每格都能结算」。
EFFECT_CD_NEW = 0.05

# hero 血量倍率
HERO_LIFE_MUL = 20
HERO_IDS = ("Beheaded", "Richter")

# 断言用的原版基准值（防止误改其它字段）
WANT_POWER = [150]
WANT_BUMP = 1
WANT_DISTANCE = 10
WANT_HERO_LIFE_BASE = 100


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


def _patch_item(data) -> None:
    """A) item/SeismicStomp.props.effectCD → 0.05"""
    print("\n[A] item 表：SeismicStomp.props.effectCD")
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
        raise SystemExit(f"ERROR: {ITEM_ID}.props 不是字典")

    print(f"  {ITEM_ID}: power={props.get('power')} bump={props.get('bump')} "
          f"distance={props.get('distance')} effectCD={props.get('effectCD')}")

    old = props.get("effectCD")
    if old == EFFECT_CD_NEW:
        print(f"  effectCD 已是 {EFFECT_CD_NEW}，无需修改")
    else:
        props["effectCD"] = EFFECT_CD_NEW
        print(f"  effectCD  {old} -> {EFFECT_CD_NEW}   (每格下砸都能结算伤害)")

    # ---- 断言：除 effectCD 外一个字段都不能动 ----
    assert row["props"]["effectCD"] == EFFECT_CD_NEW, "effectCD 写入失败"
    assert row["props"]["power"] == WANT_POWER, f"power 被意外改动: {row['props']['power']}"
    assert row["props"]["bump"] == WANT_BUMP, f"bump 被意外改动: {row['props']['bump']}"
    assert row["props"]["distance"] == WANT_DISTANCE, \
        f"distance 被意外改动: {row['props']['distance']}"
    assert row.get("group") == 3, f"group 被意外改动: {row.get('group')}"
    assert row.get("tier1") == "Brutality" and row.get("tier2") == "Survival", \
        f"tier 被意外改动: {row.get('tier1')}/{row.get('tier2')}"


def _patch_hero_life(data) -> None:
    """B) hero/*.props.heroLife ×20"""
    print(f"\n[B] hero 表：heroLife ×{HERO_LIFE_MUL}")
    hero = _sheet(data, "hero")

    patched = []
    for line in hero.get("lines", []):
        if not isinstance(line, dict) or line.get("id") not in HERO_IDS:
            continue
        props = line.get("props")
        if not isinstance(props, dict):
            continue

        old = props.get("heroLife")
        if old is None:
            continue
        if not isinstance(old, int):
            raise SystemExit(f"ERROR: hero/{line['id']}.heroLife 不是整数: {old!r}")

        snapshot = dict(props)          # 改之前先拍一张，改完逐字段比对
        new = old * HERO_LIFE_MUL
        props["heroLife"] = new
        patched.append(line["id"])
        print(f"  hero/{line['id']}: heroLife  {old} -> {new}   (血量翻 {HERO_LIFE_MUL} 倍)")

        # 断言：除了 heroLife，其它 props 字段一个都不能变
        for k, v in snapshot.items():
            if k == "heroLife":
                continue
            assert props[k] == v, f"hero/{line['id']}.{k} 被意外改动: {v} -> {props[k]}"
        assert set(snapshot.keys()) == set(props.keys()), \
            f"hero/{line['id']} 的 props 字段集合被改动"

    if set(patched) != set(HERO_IDS):
        raise SystemExit(f"ERROR: hero 表里只找到了 {patched}，期望 {list(HERO_IDS)}")
    for hid in HERO_IDS:
        print(f"  [OK] hero/{hid} 已处理")


def main() -> None:
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    src = _pick_source()
    print(f"基准 CDB: {src}")

    with open(src, encoding="utf-8") as f:
        data = json.load(f)

    _patch_item(data)
    _patch_hero_life(data)

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print(f"\ndone -> {OUT} ({os.path.getsize(OUT)} bytes)")


if __name__ == "__main__":
    main()
