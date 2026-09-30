#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
LightningWhipBoost 一键构建 / 安装 / 校验脚本
=============================================
对应需求：LightningWhip（闪电鞭）的【敌人间连锁电击】——
          无视墙体、范围 ×10、伤害 ×10。

参考 DamageAuraBoost 的目录排版：
    LightningWhipBoost/
      LightningWhipBoost/
        LightningWhipBoost.csproj          <- MDK 模组工程（Debug 自动安装）
        LightningWhipBoostMain.cs          <- 连锁穿墙（代码钩子）
        patch_lightningwhip_cdb.py         <- 生成数值补丁 data.cdb
        Assets/data.cdb_/item/LightningWhip.json   <- 由 csproj 自动生成
        data.cdb                           <- 由 patch 脚本自动生成
    安装结果：coremod/mods/LightningWhipBoost/{dll, modinfo.json, res.pak}

脚本流程（全自动，一条命令搞定）：
    1. 检查 MDK 环境
    2. python patch_lightningwhip_cdb.py     生成 data.cdb（range 2->20, prct 0.5->5.0）
    3. dotnet build                          编译 + 生成 res.pak + 自动安装
    4. 校验 Assets 数据补丁数值 / res.pak 内容 / 安装目录 / modinfo.json 入口
    5. 打印"是否可直接进游戏测试"的结论

用法：
    python build_lightning_whip_boost.py
"""

import json
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
MOD_NAME = "LightningWhipBoost"
MOD_DIR = os.path.join(ROOT, MOD_NAME, MOD_NAME)
CSPROJ = os.path.join(MOD_DIR, MOD_NAME + ".csproj")
MOD_SRC = os.path.join(MOD_DIR, MOD_NAME + "Main.cs")
PATCH_PY = os.path.join(MOD_DIR, "patch_lightningwhip_cdb.py")
ASSET_JSON = os.path.join(MOD_DIR, "Assets", "data.cdb_", "item", "LightningWhip.json")

GAME_ROOT = r"D:\steama\steamapps\common\Dead Cells"
DCCM_ROOT = os.path.join(GAME_ROOT, "coremod")
MDK_ROOT = os.environ.get("DCCM_MDK_ROOT", os.path.join(DCCM_ROOT, "core", "mdk"))
DCCM_TOOL = os.path.join(MDK_ROOT, "tools", "DCCMTool.dll")
HOST_EXE = os.path.join(DCCM_ROOT, "core", "host", "startup", "DeadCellsModding.exe")

MOD_MAIN = f"{MOD_NAME}.{MOD_NAME}Main"
INSTALL_DIR = os.path.join(DCCM_ROOT, "mods", MOD_NAME)
OUTPUT_DIR = os.path.join(MOD_DIR, "bin", "Debug", "net10.0", "output", MOD_NAME)

WANT_RANGE = 20
WANT_PRCT = 5.0

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
    print("LightningWhipBoost 构建脚本 —— 连锁电击：穿墙 + 范围 ×10 + 伤害 ×10")
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
    print("\n[2/6] 生成数值补丁 data.cdb（item/LightningWhip: range -> %d, prct -> %s）"
          % (WANT_RANGE, WANT_PRCT))
    r = run([sys.executable, PATCH_PY])
    if r.returncode != 0:
        fail("patch_lightningwhip_cdb.py 执行失败")
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
    print("\n[4/6] 校验数据补丁（Assets/data.cdb_/item/LightningWhip.json）")
    if not os.path.exists(ASSET_JSON):
        fail(f"未生成 {ASSET_JSON}")
    else:
        with open(ASSET_JSON, "r", encoding="utf-8") as f:
            props = json.load(f).get("props", {})
        print(f"      props = {props}")
        if props.get("range") == WANT_RANGE:
            print(f"      [OK] 连锁范围 range = {props.get('range')}（原版 2，×10）")
        else:
            fail(f"range 应为 {WANT_RANGE}，实际 {props.get('range')}")
        if props.get("prct") == WANT_PRCT:
            print(f"      [OK] 连锁伤害 prct = {props.get('prct')}（原版 0.5，×10）")
        else:
            fail(f"prct 应为 {WANT_PRCT}，实际 {props.get('prct')}")
        if props.get("count") == 10 and props.get("dps2") == [25]:
            print("      [OK] 连锁跳数 count=10、电击 dps2=[25] 保持原版未动")
        else:
            fail(f"保持原版的字段被意外改动: count={props.get('count')}, dps2={props.get('dps2')}")

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

    # res.pak 里必须只有 LightningWhip 这一条数据补丁
    pak = os.path.join(INSTALL_DIR, "res.pak")
    if os.path.exists(pak) and os.path.exists(DCCM_TOOL):
        import tempfile
        with tempfile.TemporaryDirectory() as td:
            r = subprocess.run(["dotnet", DCCM_TOOL, "pak", "unpack", "-i", pak, "-o", td],
                               capture_output=True, text=True)
            entries = []
            for dirpath, _dirs, files in os.walk(td):
                for fn in files:
                    entries.append(os.path.relpath(os.path.join(dirpath, fn), td).replace("\\", "/"))
            print(f"      res.pak 内容 = {entries}")
            if entries == ["data.cdb_/item/LightningWhip.json"]:
                print("      [OK] res.pak 仅含 item/LightningWhip 数据补丁（未误改武器本体/动作/特效）")
            else:
                fail("res.pak 内容与预期 data.cdb_/item/LightningWhip.json 不一致")

    # ---------- 6. 结论 ----------
    print("\n[6/6] 结论")
    if ok:
        print("      [OK] 模组已构建并安装完成，可直接进游戏测试")
        print(f"      启动器: {HOST_EXE}")
        print("      生效内容：")
        print("        · 连锁范围 ×10    item/LightningWhip.props.range  2 -> 20（res.pak 数据补丁）")
        print("        · 连锁伤害 ×10    item/LightningWhip.props.prct   0.5 -> 5.0（res.pak 数据补丁）")
        print("        · 连锁无视墙体    Hook_LightningWhip.getNextTarget + Hook_TargetHelper.filterBySight")
        print("      说明：从主角找第一个目标仍走原版视线判定，只有「敌人 -> 敌人」的连锁跳穿墙。")
    else:
        print("      [警告] 存在问题，请按上方提示修复")
    return finish()


def finish():
    print("=" * 72)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
