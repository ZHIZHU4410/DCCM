#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
WreckingBallOverhaul 一键构建 / 安装 / 校验脚本
================================================
对应需求：WreckingBall（流星锤）
    1. 删除前 2a（两段平A），直接从 3a 起手；
    2. 3a 丢出的流星锤可以一直留在原地；
    3. 4a 收回时流星锤体型变大 5 倍、伤害变大 5 倍；
    4. 3a / 4a 的前摇减少。

参考 DamageAuraBoost 的目录排版：
    WreckingBallOverhaul/
      WreckingBallOverhaul/
        WreckingBallOverhaul.csproj                  <- MDK 模组工程（Debug 自动安装）
        WreckingBallOverhaulMain.cs                  <- 段位钳制/常驻/×5（代码钩子）
        patch_wreckingball_cdb.py                    <- 生成数据补丁 data.cdb（只改前摇）
        Assets/data.cdb_/weapon/WreckingBall.json    <- 由 csproj 自动生成
        data.cdb                                     <- 由 patch 脚本自动生成
    安装结果：coremod/mods/WreckingBallOverhaul/{dll, modinfo.json, res.pak}

脚本流程（全自动，一条命令搞定）：
    1. 检查 MDK 环境
    2. python patch_wreckingball_cdb.py     生成 data.cdb（第3a/4a 前摇：charge↓ animSpd↑）
    3. dotnet build                         编译 + CDB diff + 打包 res.pak + 自动安装
    4. 校验 Assets 数据补丁数值 / 连段条数 / res.pak 内容 / 安装目录 / modinfo.json 入口
    5. 打印"是否可直接进游戏测试"的结论

用法：
    python build_wreckingball_overhaul.py
"""

import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.abspath(__file__))
MOD_NAME = "WreckingBallOverhaul"
MOD_DIR = os.path.join(ROOT, MOD_NAME, MOD_NAME)
CSPROJ = os.path.join(MOD_DIR, MOD_NAME + ".csproj")
MOD_SRC = os.path.join(MOD_DIR, MOD_NAME + "Main.cs")
PATCH_PY = os.path.join(MOD_DIR, "patch_wreckingball_cdb.py")
ASSET_JSON = os.path.join(MOD_DIR, "Assets", "data.cdb_", "weapon", "WreckingBall.json")

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

# 期望的数据补丁数值：animId -> (charge, animSpd)
WANT_WINDUP = {
    "AtkWreckingballC": (0.25, 1.6),
}
# 丢出段必须可暴击
CRIT_ANIM_ID = "AtkWreckingballC"
WANT_ANIMS = ["AtkWreckingballA", "AtkWreckingballB", "AtkWreckingballC", "AtkWreckingballD"]

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

    print("=" * 72)
    print("WreckingBallOverhaul 构建脚本 —— 删前2a / 流星锤常驻 / 3a·4a 模型×5 伤害×5 / 前摇减少")
    print("=" * 72)

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
    print("\n[2/6] 生成数值补丁 data.cdb（weapon/WreckingBall.strikeChain 前摇）")
    r = run([sys.executable, PATCH_PY])
    if r.returncode != 0:
        fail("patch_wreckingball_cdb.py 执行失败")
        return finish()
    print("      [OK] data.cdb 已生成")

    # ---------- 3. dotnet build ----------
    print("\n[3/6] dotnet build（编译 + CDB diff + 打包 res.pak + 自动安装）")
    r = run(["dotnet", "build", CSPROJ, "-v", "q", "--nologo"])
    if r.returncode != 0:
        fail("构建失败，请查看上方编译错误")
        return finish()
    print("      [OK] 构建成功")

    # ---------- 4. 校验数据补丁数值 ----------
    print("\n[4/6] 校验数据补丁（Assets/data.cdb_/weapon/WreckingBall.json）")
    if not os.path.exists(ASSET_JSON):
        fail(f"未生成 {ASSET_JSON}")
    else:
        with open(ASSET_JSON, "r", encoding="utf-8") as f:
            weapon = json.load(f)
        chain = weapon.get("strikeChain") or []
        anims = [c.get("animId") for c in chain]
        print(f"      strikeChain = {anims}")
        # 连段条数必须保持 4（索引 2/3 是丢球/收回，砍短会让游戏自身构造 Weapon 时越界）
        if anims == WANT_ANIMS:
            print(f"      [OK] 连段条数 {len(anims)} 保持原版（索引 2/3 = 丢球/收回）")
        else:
            fail(f"strikeChain 应为 {WANT_ANIMS}，实际 {anims}")

        by_anim = {c.get("animId"): c for c in chain}
        for anim, (charge, spd) in WANT_WINDUP.items():
            c = by_anim.get(anim)
            if c is None:
                fail(f"缺少连段 {anim}")
                continue
            if c.get("charge") == charge and c.get("animSpd") == spd:
                print(f"      [OK] {anim}: charge={charge} animSpd={spd}（前摇减少）")
            else:
                fail(f"{anim} 前摇应为 charge={charge} animSpd={spd}，实际 "
                     f"charge={c.get('charge')} animSpd={c.get('animSpd')}")

        # 前两段必须原样保留（只在运行期跳过，不改数据）
        for anim, want_power in (("AtkWreckingballA", [200]), ("AtkWreckingballB", [180])):
            c = by_anim.get(anim)
            if c and c.get("power") == want_power:
                print(f"      [OK] {anim} 原样保留（power={c.get('power')}，仅运行期跳过）")
            else:
                fail(f"{anim} 被意外改动: {c.get('power') if c else None}")

        if by_anim.get("AtkWreckingballD", {}).get("power") == [250]:
            print("      [OK] 第4a power 保持原版 250（已取消回收，该段不再使用）")
        else:
            fail(f"第4a power 不应改动: {by_anim.get('AtkWreckingballD', {}).get('power')}")
        c_crit = by_anim.get(CRIT_ANIM_ID, {})
        if c_crit.get("canCrit") is True:
            print(f"      [OK] {CRIT_ANIM_ID} canCrit=True（丢出可暴击）")
        else:
            fail(f"{CRIT_ANIM_ID} canCrit 应为 True，实际 {c_crit.get('canCrit')}")
        if c_crit.get("power") == [120]:
            print("      [OK] 第3a power 保持原版 120（×10 由代码在命中瞬间放大，避免滚雪球）")
        else:
            fail(f"第3a power 不应改动: {c_crit.get('power')}")

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

    # res.pak 里必须只有 weapon/WreckingBall 这一条数据补丁
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
            print(f"      res.pak 内容 = {entries}")
            if entries == ["data.cdb_/weapon/WreckingBall.json"]:
                print("      [OK] res.pak 仅含 weapon/WreckingBall 数据补丁（未误改武器本体/动作/特效）")
            else:
                fail("res.pak 内容与预期 data.cdb_/weapon/WreckingBall.json 不一致")

    # ---------- 6. 结论 ----------
    print("\n[6/6] 结论")
    if ok:
        print("      [OK] 模组已构建并安装完成，可直接进游戏测试")
        print(f"      启动器: {HOST_EXE}")
        print("      生效内容：")
        print("        · 删除前2a + 取消收回  Hook_WreckingBall.{onExecute,incrementCycle,set_cycle}")
        print("                          段位永远钉在 cycle 2(3a)，按攻击键就是再丢一颗，")
        print("                          永不进入 1a/2a，也永不进入收回段")
        print("        · 飞到最远点停下   maxDist 11格 -> 40格；Hook_Bullet.reachMaxDist 改成")
        print("                          「清速度停住」而不是原版的 vanish() 销毁")
        print("        · 丢球无视墙体     ignoreWalls / ignoreOneWays / CollisionMode.IgnoreWalls")
        print("                          （球不吐弹药、不会被墙挡下、不会自动消失）")
        print("        · 丢出可暴击       数据补丁 AtkWreckingballC canCrit false -> true")
        print("                          （原版只有收回段 canCrit=true，所以丢出永远不暴击）")
        print("        · 身份判定正确     先比 HashlinkObj 包装器、再退回比 HashlinkPointer，")
        print("                          不用 ReferenceEquals（会假失败导致放大不生效）")
        print("        · 丢出 模型×5      球 sprScaleX/Y ×5（initGfx 记基准 + 每帧锁定）")
        print("        · 丢出 判定×5      Entity.radius 18 -> 90（resolveCircularCollisions 用它算")
        print("                          命中半径，跟着放大后的模型走）")
        print("        · 丢出 伤害×10     Hook__AttackUtils.hit 命中结算时把 dmgMultiplier ×10")
        print("                          （+1000%）后还原 —— 球的命中走的是")
        print("                          Bullet.onTouchValidTarget → _AttackUtils.hit，")
        print("                          不经过 WreckingBall.hitFromWeapon，所以之前改")
        print("                          strikeChain.power 完全无效）")
        print("        · 3a前摇减少       res.pak 数据补丁 charge 0.7→0.25，animSpd 1.0→1.6")
    else:
        print("      [警告] 存在问题，请按上方提示修复")
    return finish()


def finish():
    print("=" * 72)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
