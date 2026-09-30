#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
SeismicStompSlam 一键构建 / 安装 / 校验脚本
================================================
对应需求：震地冲击（Telluric Shock / 内部 ID SeismicStomp）
    · 原版：使用技能 → 先向上跃起 → 重重砸地（石块 + 冲击特效 + 范围伤害）
    · 本模组：水平每移动一格就自动触发「下砸」；不触发跃起；下砸无前摇后摇
    · 命中额外 +9999999 伤害
    · 选项菜单滑条改 hero 模型大小
    · hero 血量翻 20 倍

参考 DamageAuraBoost 的目录排版：
    SeismicStompSlam/
      SeismicStompSlam/
        SeismicStompSlam.csproj                          <- MDK 模组工程（Debug 自动安装）
        SeismicStompSlamMain.cs                          <- 触发/下砸/+9999999/选项滑条（代码）
        patch_seismicstomp_cdb.py                        <- 生成数据补丁 data.cdb
        Assets/data.cdb_/item/SeismicStomp.json          <- 由 csproj 自动生成（effectCD）
        Assets/data.cdb_/hero/Beheaded.json              <- 由 csproj 自动生成（heroLife ×20）
        Assets/data.cdb_/hero/Richter.json               <- 由 csproj 自动生成（heroLife ×20）
        data.cdb                                         <- 由 patch 脚本自动生成
    安装结果：coremod/mods/SeismicStompSlam/{dll, modinfo.json, res.pak}

脚本流程（全自动，一条命令搞定）：
    1. 检查 MDK 环境
    2. python patch_seismicstomp_cdb.py   生成 data.cdb（effectCD + heroLife×20）
    3. dotnet build                       编译 + CDB diff + 打包 res.pak + 自动安装
    4. 校验 Assets 数据补丁（item 只动 effectCD；hero 只动 heroLife 且 = 原版×20）
    5. 校验 res.pak 内容 / 安装目录 / modinfo.json 入口
    6. 打印"是否可直接进游戏测试"的结论

用法：
    python build_seismicstomp_slam.py
"""

import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.abspath(__file__))
MOD_NAME = "SeismicStompSlam"
MOD_DIR = os.path.join(ROOT, MOD_NAME, MOD_NAME)
CSPROJ = os.path.join(MOD_DIR, MOD_NAME + ".csproj")
MOD_SRC = os.path.join(MOD_DIR, MOD_NAME + "Main.cs")
PATCH_PY = os.path.join(MOD_DIR, "patch_seismicstomp_cdb.py")
ASSET_CDB_DIR = os.path.join(MOD_DIR, "Assets", "data.cdb_")
ASSET_ITEM_JSON = os.path.join(ASSET_CDB_DIR, "item", "SeismicStomp.json")
HERO_IDS = ("Beheaded", "Richter")
ASSET_HERO_JSONS = {h: os.path.join(ASSET_CDB_DIR, "hero", h + ".json") for h in HERO_IDS}

GAME_ROOT = r"D:\steama\steamapps\common\Dead Cells"
DCCM_ROOT = os.path.join(GAME_ROOT, "coremod")
MDK_ROOT = os.environ.get("DCCM_MDK_ROOT", os.path.join(DCCM_ROOT, "core", "mdk"))
DCCM_TOOL = os.path.join(MDK_ROOT, "tools", "DCCMTool.exe")
if not os.path.exists(DCCM_TOOL):
    DCCM_TOOL = os.path.join(MDK_ROOT, "tools", "DCCMTool.dll")
HOST_EXE = os.path.join(DCCM_ROOT, "core", "host", "startup", "DeadCellsModding.exe")

MOD_MAIN = f"{MOD_NAME}.{MOD_NAME}Main"
INSTALL_DIR = os.path.join(DCCM_ROOT, "mods", MOD_NAME)
OUTPUT_DIR = os.path.join(MOD_DIR, "bin", "Debug", "net10.0", "output", MOD_NAME)

# ---- 期望的数据补丁内容 ----
WANT_EFFECT_CD = 0.05
WANT_UNCHANGED = {
    "power": [150],
    "bump": 1,
    "distance": 10,
    "effectCD_old_hint": 1.5,   # 仅用于报告，不是断言目标
}
WANT_GROUP = 3

# hero 血量：原版 100 → 2000（×20）
WANT_HERO_LIFE_BASE = 100
WANT_HERO_LIFE_MUL = 20
# 除了 heroLife，这几个字段必须原样（抽查）
WANT_HERO_UNCHANGED = ("runSpd", "gravityFactor", "rallyPerHit", "coyoteTime")

ok = True


def run(cmd, cwd=None):
    print("      >>>", " ".join(cmd))
    return subprocess.run(cmd, cwd=cwd, shell=False)


def fail(msg):
    global ok
    print(f"      [错误] {msg}")
    ok = False


def main():
    global ok
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    print("=" * 74)
    print("SeismicStompSlam 构建脚本 —— 震地冲击：水平每移动一格自动下砸（无跃起 / 无前后摇）")
    print("=" * 74)

    # ---------- 1. 环境检查 ----------
    print("\n[1/6] 环境检查")
    print(f"      模组源码 : {MOD_SRC}")
    print(f"      工程文件 : {CSPROJ}")
    print(f"      数值脚本 : {PATCH_PY}")
    print(f"      MDK_ROOT : {MDK_ROOT}")
    if not os.path.exists(os.path.join(MDK_ROOT, "build", "build.props")):
        fail("未找到 MDK（build/build.props 缺失），请检查 DCCM_MDK_ROOT")
        return finish()
    for label, p in (("csproj", CSPROJ), ("Main.cs", MOD_SRC), ("patch 脚本", PATCH_PY)):
        if not os.path.exists(p):
            fail(f"未找到 {label}: {p}")
            return finish()
    print("      [OK] MDK / 工程 / 源码 / 数值脚本 齐全")

    # ---------- 2. 生成数据补丁 data.cdb ----------
    print("\n[2/6] 生成数值补丁 data.cdb（item/SeismicStomp.props.effectCD）")
    r = run([sys.executable, PATCH_PY])
    if r.returncode != 0:
        fail("patch_seismicstomp_cdb.py 执行失败")
        return finish()
    print("      [OK] data.cdb 已生成")

    # ---------- 3. dotnet build ----------
    print("\n[3/6] dotnet build（编译 + CDB diff + 打包 res.pak + 自动安装）")
    r = run(["dotnet", "build", CSPROJ, "-v", "q", "--nologo"])
    if r.returncode != 0:
        fail("构建失败，请查看上方编译错误")
        return finish()
    print("      [OK] 构建成功")

    # ---------- 4. 校验数据补丁 ----------
    print("\n[4/6] 校验数据补丁（Assets/data.cdb_/item/SeismicStomp.json + hero/*.json）")

    # --- 4a. item/SeismicStomp ---
    print("      --- item/SeismicStomp（下砸伤害节奏）---")
    if not os.path.exists(ASSET_ITEM_JSON):
        fail(f"未生成 {ASSET_ITEM_JSON}")
    else:
        with open(ASSET_ITEM_JSON, "r", encoding="utf-8") as f:
            item = json.load(f)
        props = item.get("props") or {}
        print(f"      id={item.get('id')} group={item.get('group')} "
              f"tier1={item.get('tier1')} tier2={item.get('tier2')}")
        print(f"      props = {json.dumps(props, ensure_ascii=False)}")

        if item.get("id") == "SeismicStomp":
            print("      [OK] 补丁对象是 item/SeismicStomp（震地冲击）")
        else:
            fail(f"补丁对象应为 SeismicStomp，实际 {item.get('id')}")

        if item.get("group") == WANT_GROUP:
            print(f"      [OK] group={WANT_GROUP}（Power 类主动技能）未被改动")
        else:
            fail(f"group 被意外改动: {item.get('group')}")

        if props.get("effectCD") == WANT_EFFECT_CD:
            print(f"      [OK] effectCD = {WANT_EFFECT_CD}"
                  f"（原版 {WANT_UNCHANGED['effectCD_old_hint']}，同一敌人可连续吃到下砸伤害）")
        else:
            fail(f"effectCD 应为 {WANT_EFFECT_CD}，实际 {props.get('effectCD')}")

        for k in ("power", "bump", "distance"):
            want = WANT_UNCHANGED[k]
            if props.get(k) == want:
                print(f"      [OK] {k} = {want} 保持原版（由 C# 代码直接读取）")
            else:
                fail(f"{k} 不应改动：期望 {want}，实际 {props.get(k)}")

        extra = sorted(set(props.keys()) - {
            "speed", "power", "duration2", "speed2", "power2", "buff", "bump",
            "height", "distance", "tick", "effectCD", "duration"})
        if extra:
            print(f"      [注意] props 里出现额外字段: {extra}")

    # --- 4b. hero/*（血量 ×20）---
    print(f"      --- hero/{{{'|'.join(HERO_IDS)}}}（hero 血量 ×{WANT_HERO_LIFE_MUL}）---")
    want_life = WANT_HERO_LIFE_BASE * WANT_HERO_LIFE_MUL
    for hid in HERO_IDS:
        path = ASSET_HERO_JSONS[hid]
        if not os.path.exists(path):
            fail(f"未生成 {path}")
            continue
        with open(path, "r", encoding="utf-8") as f:
            hero = json.load(f)
        hprops = hero.get("props") or {}
        got = hprops.get("heroLife")
        print(f"      hero/{hid}: id={hero.get('id')} heroLife={got}")
        if hero.get("id") != hid:
            fail(f"{path} 的 id 应为 {hid}，实际 {hero.get('id')}")
            continue
        if got == want_life:
            print(f"      [OK] hero/{hid}  heroLife {WANT_HERO_LIFE_BASE} -> {want_life}"
                  f"（翻 {WANT_HERO_LIFE_MUL} 倍）")
        else:
            fail(f"hero/{hid}.heroLife 应为 {want_life}，实际 {got}")

        for k in WANT_HERO_UNCHANGED:
            if k in hprops:
                print(f"      [OK] hero/{hid}.{k} = {hprops[k]} 保持原版")

    # ---------- 5. 校验安装目录 / res.pak / modinfo ----------
    print("\n[5/6] 校验构建产物与安装目录")
    for label, d in (("构建产物", OUTPUT_DIR), ("安装目录", INSTALL_DIR)):
        dll = os.path.join(d, MOD_NAME + ".dll")
        info = os.path.join(d, "modinfo.json")
        pak = os.path.join(d, "res.pak")
        if os.path.exists(dll) and os.path.exists(info) and os.path.exists(pak):
            print(f"      [OK] {label}: {d}")
            print(f"           dll {os.path.getsize(dll)} 字节 / res.pak {os.path.getsize(pak)} 字节")
        else:
            missing = [n for n, p in (("dll", dll), ("modinfo.json", info), ("res.pak", pak))
                       if not os.path.exists(p)]
            fail(f"{label} 缺少 {', '.join(missing)}: {d}")

    info_path = os.path.join(INSTALL_DIR, "modinfo.json")
    if os.path.exists(info_path):
        with open(info_path, "r", encoding="utf-8") as f:
            info = json.load(f)
        print(f"      name       = {info.get('name')}")
        print(f"      main       = {info.get('main')}")
        print(f"      assemblies = {info.get('assemblies')}")
        if info.get("main") != MOD_MAIN:
            fail(f"main 不是 {MOD_MAIN}")
        else:
            print("      [OK] 主类入口正确")

    pak = os.path.join(INSTALL_DIR, "res.pak")
    if os.path.exists(pak) and os.path.exists(DCCM_TOOL):
        with tempfile.TemporaryDirectory() as td:
            if DCCM_TOOL.endswith(".dll"):
                cmd = ["dotnet", DCCM_TOOL, "pak", "unpack", "-i", pak, "-o", td]
            else:
                cmd = [DCCM_TOOL, "pak", "unpack", "-i", pak, "-o", td]
            subprocess.run(cmd, capture_output=True, text=True)
            entries = []
            for dirpath, _dirs, files in os.walk(td):
                for fn in files:
                    entries.append(os.path.relpath(os.path.join(dirpath, fn), td).replace("\\", "/"))
            entries.sort()
            want_entries = sorted(
                ["data.cdb_/item/SeismicStomp.json"] +
                [f"data.cdb_/hero/{h}.json" for h in HERO_IDS])
            print(f"      res.pak 内容 = {entries}")
            if entries == want_entries:
                print("      [OK] res.pak 只含预期数据补丁"
                      "（item/SeismicStomp + hero 血量；未误改技能本体/武器/怪物/关卡等其它表）")
            else:
                fail(f"res.pak 内容与预期不一致\n            期望 = {want_entries}")

    # ---------- 6. 结论 ----------
    print("\n[6/6] 结论")
    if ok:
        print("      [OK] 模组已构建并安装完成，可直接进游戏测试")
        print(f"      启动器: {HOST_EXE}")
        print("      生效内容：")
        print("        · 触发       IOnHeroUpdate.OnHeroUpdate —— 只在英雄格子坐标 cx 变化时触发")
        print("                     （水平方向；|Δcx|>1 视为传送不触发；换关自动重置）")
        print("        · 不跃起     完全不调用 o.bump / ForcedDiveAttack / jump2 音效，")
        print("                     也不 new dc.pow.SeismicStomp（避免信号监听泄漏）")
        print("        · 无前后摇   无 charge 前摇、无 ForcedDiveAttack 状态锁、无 4 秒持续期、无收尾")
        print("        · 下砸表现   复刻 SeismicStomp.stompHit：")
        print("                       fx.khStomp(...)                        石块 + 尘土 + 冲击")
        print("                       sfx/active/stomp_char3.wav             落地音效")
        print("                       viewport.shakeS(0.0, 0.3, 1.0)         震屏")
        print("                       _AttackUtils.createFromHero + hit      ±2 格、cy∈[scy-1,scy] 伤害")
        print("        · 命中伤害   每次命中额外 +9999999")
        print("                     注入点 = Hook_Entity.applyAttackResult（扣血前最后一步，")
        print("                     调用链 hit → applyHitResult → attackTarget.applyHit")
        print("                     → parent.applyAttackResult → Entity.applyAttackResult 读 finalDmg）")
        print("                     只对本模组下砸创建的 AttackData 生效（_pendingFlatBonus 标记），")
        print("                     武器/怪物/原版技能的伤害不受影响；顺带绕过 Boss 的 dmgSoftCap")
        print("        · 数值来源   Assets/data.cdb_/item/SeismicStomp.json 的 props")
        print("                     （power/bump/distance 保持原版；effectCD 1.5 -> 0.05）")
        print("        · hero 血量  Assets/data.cdb_/hero/{Beheaded,Richter}.json")
        print(f"                     props.heroLife {WANT_HERO_LIFE_BASE} -> "
              f"{WANT_HERO_LIFE_BASE * WANT_HERO_LIFE_MUL}（翻 {WANT_HERO_LIFE_MUL} 倍）")
        print("                     依据：Hero.updateMaxLife() → _Const.scaleHeroLifeToTier()")
        print("                     内部是 num5 = @base * lifeScalingFromTier（对 heroLife 线性），")
        print("                     所以卷轴/装备/换关/复活全都自动 20 倍，不需要 hook、不会复利")
        print("        · 站稳判定   只有脚下一格是实心时才会触发（RequireGrounded = true）")
        print("        · 选项菜单   IModMenu.BuildMenu 加了一个滑条「Hero size」")
        print("                     （形式参考 ZoomVision：createScroller → addSliderWidget → updateScroller）")
        print("                     0.25 ~ 5.00，步长 0.05；拖动即时生效，值存进 mod 自己的配置文件")
        print("        · 模型大小   hero.sprScaleX / sprScaleY 每帧维持一次")
        print("                     （实现参考 ShrinkOnKillMain：只改精灵缩放源值，不动碰撞体）")
    else:
        print("      [警告] 存在问题，请按上方提示修复")
    return finish()


def finish():
    print("=" * 74)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
