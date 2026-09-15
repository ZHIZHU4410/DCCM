#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ChronoBlade — data.cdb 生成脚本（Python，秒级完成，无需 CastleDB 编辑器）

以 MDK v35 模板 data.cdb（JSON 格式）为底，追加：
  1) item   表一行  id="ChronoBlade"  —— 物品定义（图标沿用 Katana 卡片图标，零新资源）
  2) weapon 表一行  item="ChronoBlade" —— 武器定义，3 段 strikeChain：

     第 1a  ChronoSlash1   参考 Katana 斩击：AtkKatanaA 前冲斩，斩击路径上的敌人依次被斩
     第 2a  ChronoShuriken 参考 TimeKeeper levelUpRadius：原地向一周发射飞镖
     第 3a  ChronoSwordRain 参考 TimeKeeper swordRain：背景时钟 + 向怪物砸剑

  代码侧（ChronoBladeMain / ChronoBlade.cs）用 set_cycle() 识别 0/1/2 段并附加特效与伤害。

用法:
    python patch_chronoblade_cdb.py            # 生成 ./data.cdb
    python patch_chronoblade_cdb.py --check    # 只校验并打印结果
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
TEMPLATE = r"D:\steama\steamapps\common\Dead Cells\coremod\core\mdk\databases\v35\data.cdb"
OUT = os.path.join(HERE, "data.cdb")

ITEM_ID = "ChronoBlade"
ITEM_NAME = "Chrono Blade"          # 英文名（游戏内文本由 lang/*.mo 覆盖，缺失时用这个）
ITEM_NAME_CN = "时之刃"
ITEM_DESC = ("Combo 1: slash that engraves crossed enemies with Roman numerals. "
             "Combo 2: throw shurikens in a full circle. "
             "Combo 3: the Time Keeper's clock falls and swords rain from the sky.")

# 第二把武器：Zaphkiel（十二之弹枪）—— 以原版 Pistol 为模板，数据 1:1 复刻
# 注意：PISTOL_ID 是 CDB 里的 item id，**故意不改名**（旧存档里已拿到的枪会变成未知物品）；
#       玩家看到的名字由 PISTOL_NAME 决定，也就是 Zaphkiel。
PISTOL_ID = "TimeBullet"
PISTOL_NAME = "Zaphkiel"
PISTOL_DESC = ("Twelve bullets of time. Fire the loaded bullet and it pops its own Roman "
               "numeral; press the reload key to cycle to the next bullet. Each bullet "
               "applies a different time effect to whatever it hits.")

# 时间守护者主题配色（取自 data.cdb 里 TimeKeeper 技能配色）
CLOCK_INNER = 16773535     # 金色内圈
CLOCK_OUTER = 16530435     # 深金外圈
SHURIKEN_COLOR = 0xFFD24A  # 飞镖发光色

# ---------------------------------------------------------------- 传奇词条（legendAffixes）
#
# 时之刃：直接用原版的"无视防御盾"（IgnoreGlobalShield，affix 表 group=3 / index=87）。
#   原版这一条的 props 是空的 —— 效果写在游戏代码里（按 affix id 判定），
#   所以只要把 id 挂进 item.legendAffixes 就自动生效，不需要我们实现任何东西。
BLADE_LEGEND_AFFIX = "IgnoreGlobalShield"

# 刻刻帝（Zaphkiel）：原版没有任何"让子弹效果翻倍"的词条，所以**在 affix 表里新建一条**。
#   ⚠️ 和 IgnoreGlobalShield 一样，affix 的 props 是空壳，真正的行为由模组代码实现：
#      ChronoBullets 会按"这把枪是否带着这个词条"把 12 发子弹的效果整体翻倍。
#      CDB 里这一条只负责：出现在传奇物品的说明里、能被 legendAffixes 引用、有名字和图标。
PISTOL_LEGEND_AFFIX = "ChronoBulletDouble"
# 说明文字直接写在 CDB 的 desc 字段里（不去碰 lang/*.mo —— 那会把整张文本表替换掉）
PISTOL_LEGEND_AFFIX_DESC = "每个弹药效果增强"
# 图标沿用原版卡片图集（不新增美术），取 DoubleSpeed 那一格
PISTOL_LEGEND_AFFIX_ICON = {"x": 57, "y": 0, "file": "cardIcons.png", "size": 24}

# 刻刻帝本体的图标：TIMEZHANJI 的第 0 帧，由 `make_icon_sheet.py` 嫁接进 cardIcons.png。
#   CDB 的 icon 只会用 (x, y, size) 从 cardIcons.png 里切一格 —— `file` 是死数据，
#   详见 make_icon_sheet.py 的头部注释和下面 pist_item_row["icon"] 处的说明。
PISTOL_ICON = {"x": 36, "y": 0, "file": "cardIcons.png", "size": 24}

# 第 1a 居合前冲斩的数值，直接沿用 Katana 项目的调参结果
DASH_RANGE = 20        # 居合距离（格），原版 6；满蓄力再 ×1.5 ≈ 30 格
DASH_POWER = 220       # 居合单段伤害（原版 88）
DASH_DURATION = 0.2    # 居合残影/冷却窗口（原版 0.5）
DASH_CD = 0.05         # 攻击间隔冷却（原版 0.4）

# 新行的目标分组名（近战武器）。
# 名字必须与模板 item 表 props.separatorTitles 里的条目一致，且**行的插入位置必须
# 落在该分组的行区间内** —— DCCMTool 是"按位置"反解 separator 元数据的：
# 只有把行插进 Melee 段里，输出的 data.cdb_/*.json 才会带上
#   __separator_group_Name = "Melee" / __separator_group_ID = 4
# 而 CDBManager 加载时又会用这个名字反查分组 ID 覆盖 group 字段。
# 之前直接 append 到表尾 → 被算进 BossRushStatueUnlock(16)，武器被背包/装备系统无视。
SEP_GROUP_NAME = "Melee"
SEP_GROUP_ID = 4
# 插入到 Melee 段的第几个位置（段内偏移，避免插在段首影响 DCCMTool 的分段判断）
SEP_INSERT_OFFSET = 32


def _skill(anim, fx, props, *, power, cd, hit_frame, area, sfx_rel, sfx_chg,
           sfx_hit, charge, lock=0.0, can_crit=False, crit=1.0, glow=13270783,
           breach=0.5, dyn=0, fx_props=None, extra=None):
    """构造一条 strikeChain 条目（字段顺序/命名与 v35 weapon 表一致）。"""
    st = {
        "coolDown": cd,
        "power": [power],
        "fxProps": fx_props or {},
        "lockCtrlAfter": lock,
        "props": props,
        "breachBonus": breach,
        "dynamicCharge": dyn,
        "sfxCharge": sfx_chg,
        "charge": charge,
        "sfxProps": {},
        "canCrit": can_crit,
        "area": area,
        "sfxRelease": sfx_rel,
        "fxId": fx,
        "animId": anim,
        "sfxHit": sfx_hit,
        "hitFrame": hit_frame,
        "critMul": crit,
        "glowColor": glow,
        "animSpd": 1,
    }
    if extra:
        st.update(extra)
    return st


def build_strike_chain(katana_chain):
    """时之刃本体的 4 段连击。

    段位分配（对应代码里的 _cycle）：
      第 1a (cycle 0)：**居合前冲斩** —— 参考 Katana 项目的向前斩击。
                       代码在 cycle 0 时置 nextIsChargeAtk=true，原版 Katana.onExecute
                       就会走"瞬移前冲 + 斩击路径上敌人依次被斩"的居合分支。
                       所以这一段直接照搬原版 Katana 的**第 4 段（居合段）**，并补上
                       animId=AtkKatanaA（原版第 4 段没有 animId，因为原作靠代码播这个动画）。
      第 2a (cycle 1)：普通平砍（照搬原版第 1 段）→ 代码叠"一周飞镖"
      第 3a (cycle 2)：普通平砍（照搬原版第 2 段）→ 代码叠"背景时钟 + 剑雨"
      第 4a (cycle 3)：普通平砍（照搬原版第 3 段）—— 玩家按住攻击键时基类会 set_cycle(3)，
                       必须有这一段，否则 get_curSkill() 返回 null → Null access .chargeF 崩游戏。

    ⚠️ 除第 1 段按居合需要补 animId 外，其余字段一律沿用原版数值
    （hitFrame / charge / dynamicCharge / props 结构都不动），这是踩了多次崩溃后的结论。
    """
    import copy

    # ---- 第 1a：居合前冲斩 = 原版 Katana 第 4 段（居合段）+ Katana 项目的强化数值 ----
    dash = copy.deepcopy(katana_chain[3] if len(katana_chain) > 3 else katana_chain[0])
    dash["animId"] = "AtkKatanaA"          # 原版居合分支会自己播这个动画，显式写上更保险
    dash["fxId"] = "fxBeheadedKatanaB"
    dash["power"] = [DASH_POWER]           # 220（原版 88）
    dash["hitFrame"] = 1                   # 按下即执行（原版 6）
    dash["coolDown"] = DASH_CD             # 0.05（原版 0.4）
    dash["lockCtrlAfter"] = 0              # 不留后摇
    dash["glowColor"] = CLOCK_INNER
    dash["sfxRelease"] = "sfx/inter/clock_bell6.wav"
    dash.setdefault("props", {})
    dash["props"]["range"] = DASH_RANGE            # 20 格（原版 6）；满蓄力再 ×1.5 ≈ 30 格
    dash["props"]["duration"] = DASH_DURATION      # 0.2（原版 0.5）：缩短居合残影/冷却窗口

    # ---- 第 2a / 3a / 4a：普通平砍（照搬原版第 1/2/3 段，一个数值都不改）----
    slash_a = copy.deepcopy(katana_chain[0])
    slash_b = copy.deepcopy(katana_chain[1] if len(katana_chain) > 1 else katana_chain[0])
    slash_c = copy.deepcopy(katana_chain[2] if len(katana_chain) > 2 else katana_chain[0])

    # 第 2a 只改发光色与收招音（招式本体仍是平砍）
    slash_b["glowColor"] = SHURIKEN_COLOR
    slash_b["sfxRelease"] = "sfx/weapon/weapon_katana_release3.wav"
    # 第 3a 只改发光色
    slash_c["glowColor"] = CLOCK_INNER

    return [dash, slash_a, slash_b, slash_c]


def build_item_row(katana_item, group_index, item_id, item_name):
    row = dict(katana_item)          # 以 Katana 物品行为模板（保证字段齐全）
    row["id"] = item_id
    row["name"] = item_name
    row["gameplayDesc"] = ITEM_DESC
    row["moneyCost"] = 1800
    row["cellCost"] = 60
    row["group"] = group_index       # 4 = Melee（近战武器）
    row["droppable"] = True
    row["tier1"] = "Brutality"
    # tier2 也补上：留成 null 时某些"按颜色筛选"的池子（自定游戏选武器、商店分类）
    # 会把它过滤掉或读空值 —— Zaphkiel 从原版 Pistol 复刻过来就带着 Tactic。
    row["tier2"] = "Tactic"
    row["props"] = {"duration": 1.5}
    row["commonProps"] = {}
    row["synergy"] = []
    # ⚠️ tags 必须留空、cellCost 必须是 0，才能"和 Zaphkiel 一样可以直接选用"。
    #    之前这里带了 [{"tag": "UnlockInPublicEvent"}]（蓝图靠公共事件掉落），
    #    结果就是：itemMeta 里虽然解锁了，但它依然不进可选/掉落池 ——
    #    表现就是"Zaphkiel 能在自定游戏里选，ChronoBlade 只能用热键召出来"。
    row["tags"] = []
    row["cellCost"] = 0
    # 传奇词条：无视防御盾（原版已有实现，挂 id 即可）
    row["legendAffixes"] = [{"affix": BLADE_LEGEND_AFFIX}]
    # separator 元数据留给 DCCMTool 按插入位置计算（这里先填上分组名以备参考）
    row["__separator_group_Name"] = SEP_GROUP_NAME
    row["__separator_group_ID"] = group_index
    # 图标沿用 Katana 的 UI 卡片图标，不新增贴图（卡片图标每格 24px）
    row["icon"] = {"x": 24, "y": 0, "file": "cardIcons.png", "size": 24}
    return row


def main():
    check = "--check" in sys.argv
    with open(TEMPLATE, encoding="utf-8") as f:
        data = json.load(f)

    sheets = {s["name"]: s for s in data["sheets"]}

    # 找模板里的 Katana 行作为模板（物品行 + 武器 strikeChain 都照搬）
    katana_item = None
    for ln in sheets["item"]["lines"]:
        if isinstance(ln, dict) and ln.get("id") == "Katana":
            katana_item = ln
            break
    if katana_item is None:
        raise SystemExit("ERROR: item 表里没有 Katana 模板行")

    katana_weapon = None
    for ln in sheets["weapon"]["lines"]:
        if isinstance(ln, dict) and ln.get("item") == "Katana":
            katana_weapon = ln
            break
    if katana_weapon is None:
        raise SystemExit("ERROR: weapon 表里没有 Katana 模板行")

    katana_chain = katana_weapon.get("strikeChain") or []
    if not katana_chain:
        raise SystemExit("ERROR: Katana 的 strikeChain 为空")
    print("原版 Katana strikeChain: %d 段，第 1 段 = hitFrame %s / charge %s / area.shape %s" %
          (len(katana_chain), katana_chain[0].get("hitFrame"), katana_chain[0].get("charge"),
           (katana_chain[0].get("area") or {}).get("shape")))

    item_lines = sheets["item"]["lines"]
    weapon_lines = sheets["weapon"]["lines"]

    # 去重（支持重复执行）
    def drop_rows(rows, key, value):
        rows[:] = [l for l in rows if not (isinstance(l, dict) and l.get(key) == value)]

    drop_rows(item_lines, "id", ITEM_ID)
    drop_rows(weapon_lines, "item", ITEM_ID)
    drop_rows(item_lines, "id", PISTOL_ID)
    drop_rows(weapon_lines, "item", PISTOL_ID)

    # 插到 Melee 段内部，让 DCCMTool 算出 __separator_group_Name="Melee" / group=4。
    # 否则武器会落进表尾的 BossRushStatueUnlock 分组（group=16），背包/装备系统会直接无视它。
    item_sheet = sheets["item"]
    titles = item_sheet.get("props", {}).get("separatorTitles") or []
    if SEP_GROUP_NAME not in titles:
        raise SystemExit(f"ERROR: item 表没有 {SEP_GROUP_NAME} 分组，separatorTitles={titles}")
    group_index = titles.index(SEP_GROUP_NAME)
    starts = item_sheet.get("separators") or []
    if group_index >= len(starts):
        raise SystemExit("ERROR: item 表 separators 与 separatorTitles 长度不匹配")
    seg_start = starts[group_index]
    insert_at = min(seg_start + SEP_INSERT_OFFSET, len(item_lines))
    item_lines.insert(insert_at, build_item_row(katana_item, group_index, ITEM_ID, ITEM_NAME))

    def add_weapon(item_id, chain):
        weapon_lines.append({
            "allowCrouch": False,
            "isACancel": False,
            "item": item_id,
            "strikeChain": chain,
            "airControlAlways": False,
            "cannotBeCanceledByWeapon": True,
            "__separator_group_Name": "",
            "__separator_group_ID": -1,
            "__original_Index": len(weapon_lines),
        })

    chain = build_strike_chain(katana_chain)
    add_weapon(ITEM_ID, chain)

    # ---------- 时之弹：以原版 Pistol 为模板，1:1 复刻它的 item + weapon 行 ----------
    pist_item = None
    for ln in item_lines:
        if isinstance(ln, dict) and ln.get("id") == "Pistol":
            pist_item = ln
            break
    if pist_item is None:
        raise SystemExit("ERROR: item 表里没有 Pistol 模板行")

    pist_weapon = None
    for ln in weapon_lines:
        if isinstance(ln, dict) and ln.get("item") == "Pistol":
            pist_weapon = ln
            break
    if pist_weapon is None:
        raise SystemExit("ERROR: weapon 表里没有 Pistol 模板行")

    import copy as _copy
    pist_item_row = _copy.deepcopy(pist_item)
    pist_item_row["id"] = PISTOL_ID
    pist_item_row["name"] = PISTOL_NAME
    pist_item_row["gameplayDesc"] = PISTOL_DESC
    pist_item_row["droppable"] = True          # 允许掉落，方便测试
    pist_item_row["group"] = group_index
    pist_item_row["__separator_group_Name"] = SEP_GROUP_NAME
    pist_item_row["__separator_group_ID"] = group_index
    # 传奇词条：本模组自己新建的那一条（子弹效果翻倍）
    pist_item_row["legendAffixes"] = [{"affix": PISTOL_LEGEND_AFFIX}]

    # 刻刻帝的图标：用 `make_icon_sheet.py` 把 TIMEZHANJI 的帧嫁接进 cardIcons.png 的空格里。
    #
    # ⚠️ CDB 的 `icon.file` 是**死数据** —— `dc._Assets.getItem()` 里根本不读它：
    #       tile2 = Assets.itemIcons            # 全局唯一那张表（就是 cardIcons.png）
    #       return tile2.sub(icon.x*size, icon.y*size, size, size)
    #    所以只用了 x / y / size，含义是"从 cardIcons.png 切第 (x,y) 格"。
    #    而 `Assets.itemIcons` 由 `loader.loadCache("cardIcons.png")` 得到 ——
    #    本模组的 pak 里带着同名的 cardIcons.png，会覆盖原版，于是直接改那张图就行。
    #
    # 坐标取自 `_icon_cells.txt`（脚本跑完会写出来）：idle_0000 → (36, 0)。
    # 想换成别的帧/别的观感，改这两个数即可（每一帧的坐标都在那个表里）。
    pist_item_row["icon"] = PISTOL_ICON

    item_lines.insert(insert_at + 1, pist_item_row)

    # ---------- 新建 affix 表行：刻刻帝的"子弹效果翻倍"传奇词条 ----------
    #
    # affix 表和 item 表一样是"按位置反解分组"的：
    #   props.separatorTitles = [Tier, Special, Basic, Advanced, LegendaryOnly]
    #   separators            = [0, 4, 22, 87, 127]
    # 传奇词条都在最后一个分组 LegendaryOnly(group=4)，它一直延伸到表尾 ——
    # 所以**追加到表尾**就会被 DCCMTool 算成 group=4，不需要手工插到中间
    # （这点和 item 表不同：item 表最后一个分组是 BossRushStatueUnlock，追加会落错组）。
    affix_lines = sheets["affix"]["lines"]
    drop_rows(affix_lines, "id", PISTOL_LEGEND_AFFIX)

    tpl_affix = next((r for r in affix_lines
                      if isinstance(r, dict) and r.get("id") == "DoubleSpeed"), None)
    if tpl_affix is None:
        tpl_affix = next((r for r in affix_lines
                          if isinstance(r, dict) and r.get("group") == 4), None)
    if tpl_affix is None:
        raise SystemExit("ERROR: affix 表里找不到 LegendaryOnly(group=4) 的模板行")

    max_affix_index = max((r.get("index") or 0) for r in affix_lines if isinstance(r, dict))
    affix_row = _copy.deepcopy(tpl_affix)
    affix_row.update({
        "id": PISTOL_LEGEND_AFFIX,
        "desc": PISTOL_LEGEND_AFFIX_DESC,      # 直接写英文文本（不去碰 lang/*.mo）
        "icon": dict(PISTOL_LEGEND_AFFIX_ICON),
        "index": max_affix_index + 1,
        "group": 4,
        # props 保持空壳：真正的行为在模组代码里（ChronoBullets 判定这把枪有没有这个词条）
        "props": {},
        "commonProps": {},
        "synergy": [],
        "requiredTags": [],
        "forbiddenTags": [],
        "forbiddenAffixes": [],
        "bonusItem": [],
        "forbiddenItem": [],
        "itemRestriction": 0,     # 0 = 不限制物品类型（只由 item.legendAffixes 决定谁 Roll 得到）
        "chance": 0,
        "costImpact": 0.5,
        "allowStacking": False,
        "keepOnReroll": False,
        "sortPriority": 0,
    })
    affix_lines.append(affix_row)

    print("affix 行: %d（新增 %s，index=%d, group=%d）" %
          (len(affix_lines), PISTOL_LEGEND_AFFIX, affix_row["index"], affix_row["group"]))
    print("传奇词条: %s -> %s ; %s -> %s" %
          (ITEM_ID, BLADE_LEGEND_AFFIX, PISTOL_ID, PISTOL_LEGEND_AFFIX))

    pist_weapon_row = _copy.deepcopy(pist_weapon)
    pist_weapon_row["item"] = PISTOL_ID
    pist_weapon_row["__separator_group_Name"] = ""
    pist_weapon_row["__separator_group_ID"] = -1
    pist_weapon_row["__original_Index"] = len(weapon_lines)
    weapon_lines.append(pist_weapon_row)

    print("item  行: %d（新增 %s + %s）" % (len(item_lines), ITEM_ID, PISTOL_ID))
    print("weapon 行: %d（%s %d 段 / %s %d 段，照搬原版 Pistol）" %
          (len(weapon_lines), ITEM_ID, len(chain), PISTOL_ID, len(pist_weapon_row.get("strikeChain") or [])))
    for i, st in enumerate(chain, 1):
        print("   第 %da anim=%s fx=%s power=%s hitFrame=%s charge=%s dyn=%s cd=%s" %
              (i, st.get("animId"), st.get("fxId"), st.get("power"),
               st.get("hitFrame"), st.get("charge"), st.get("dynamicCharge"), st.get("coolDown")))
    pw = (pist_weapon_row.get("strikeChain") or [{}])[0]
    print("   时之弹第 1 发 anim=%s fx=%s power=%s area.shape=%s" %
          (pw.get("animId"), pw.get("fxId"), pw.get("power"), (pw.get("area") or {}).get("shape")))

    if check:
        return

    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    print("done -> %s (%d bytes)" % (OUT, os.path.getsize(OUT)))


if __name__ == "__main__":
    main()
