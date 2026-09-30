#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
DashOverhaul 一键构建 / 安装 / 校验脚本
================================================
对应需求：Dash（突击 Assault）技能
    1. 只能释放 Dash（原版用完会变身 BackDash，再按就是反向冲刺）；
    2. 按方向键可以八方向冲刺；
    3. 冲刺伤害 +500%；
    4. 无视墙体；
    5. 冲刺拖尾改成淡蓝色；
    6. castCD = 0。

参考 DamageAuraBoost 的目录排版：
    DashOverhaul/
      DashOverhaul/
        DashOverhaul.csproj                 <- MDK 模组工程（Debug 自动安装）
        DashOverhaulMain.cs                 <- 8 方向 / 穿墙 / 淡蓝拖尾 / 路径伤害（代码钩子）
        patch_dash_cdb.py                   <- 生成数据补丁 data.cdb
        Assets/data.cdb_/item/Dash.json     <- 由 csproj 自动生成
        data.cdb                            <- 由 patch 脚本自动生成
    安装结果：coremod/mods/DashOverhaul/{dll, modinfo.json, res.pak}

脚本流程（全自动，一条命令搞定）：
    1. 检查 MDK / 工程 / 源码 / 数值脚本
    2. python patch_dash_cdb.py     生成 data.cdb（castCD=0 / power x6 / 不变身 BackDash）
    3. dotnet build                 编译 + CDB diff + 打包 res.pak + 自动安装
    4. 校验 Assets 数据补丁数值
    5. 校验 res.pak 内容 / 安装目录 / modinfo.json 入口
    6. 打印"是否可直接进游戏测试"的结论

用法：
    python build_dash_overhaul.py
"""

import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.abspath(__file__))
MOD_NAME = "DashOverhaul"
# 本脚本既放在工作区根目录、也随模组提交到 DashOverhaul/ 下，两种位置都能用：
#   根目录     -> ROOT/DashOverhaul/DashOverhaul/  (外层+内层工程目录)
#   DashOverhaul/ -> ROOT/DashOverhaul/            (只剩内层工程目录)
if os.path.isdir(os.path.join(ROOT, MOD_NAME, MOD_NAME)):
    MOD_DIR = os.path.join(ROOT, MOD_NAME, MOD_NAME)
else:
    MOD_DIR = os.path.join(ROOT, MOD_NAME)
CSPROJ = os.path.join(MOD_DIR, MOD_NAME + ".csproj")
MOD_SRC = os.path.join(MOD_DIR, MOD_NAME + "Main.cs")
PATCH_PY = os.path.join(MOD_DIR, "patch_dash_cdb.py")
ASSET_JSON = os.path.join(MOD_DIR, "Assets", "data.cdb_", "item", "Dash.json")

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

# 期望的数据补丁数值
WANT_CAST_CD = 0.0
WANT_POWER = [600.0]
WANT_STAY_ITEM = "Dash"

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
    print("DashOverhaul 构建脚本 —— 只能放Dash / 八方向冲刺 / 伤害+500% / 穿墙 / 淡蓝拖尾 / castCD=0")
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

    # 关键：DCCM 不会自动把模组目录下的 res.pak 合进游戏资源，
    # 少了 IOnAfterLoadingAssets + loadPak，数据补丁等于没写
    # （表现就是"用完 Dash 依然变成 BackDash"）。
    with open(MOD_SRC, "r", encoding="utf-8", errors="replace") as f:
        src = f.read()
    if "IOnAfterLoadingAssets" in src and "loadPak" in src:
        print("      [OK] Main.cs 里有 IOnAfterLoadingAssets + loadPak（res.pak 会被真正挂载）")
    else:
        fail("Main.cs 缺少 IOnAfterLoadingAssets / loadPak —— 数据补丁不会被加载，Dash 用完仍会变成 BackDash")

    # ---------- 2. 生成数据补丁 data.cdb ----------
    print("\n[2/6] 生成数值补丁 data.cdb（item/Dash：castCD、power、commonProps.item）")
    r = run([sys.executable, PATCH_PY])
    if r.returncode != 0:
        fail("patch_dash_cdb.py 执行失败")
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
    print("\n[4/6] 校验数据补丁（Assets/data.cdb_/item/Dash.json）")
    if not os.path.exists(ASSET_JSON):
        fail(f"未生成 {ASSET_JSON}")
    else:
        with open(ASSET_JSON, "r", encoding="utf-8") as f:
            dash = json.load(f)

        print(f"      id          = {dash.get('id')}")
        print(f"      castCD      = {dash.get('castCD')}")
        print(f"      props.power = {dash.get('props', {}).get('power')}")
        print(f"      props.speed = {dash.get('props', {}).get('speed')}")
        print(f"      props.range = {dash.get('props', {}).get('range')}")
        print(f"      commonProps = {dash.get('commonProps')}")

        if dash.get("id") != "Dash":
            fail("id 不是 Dash")
        if dash.get("castCD") != WANT_CAST_CD:
            fail(f"castCD 应为 {WANT_CAST_CD}，实际 {dash.get('castCD')}")
        else:
            print("      [OK] castCD = 0（技能无冷却）")

        if dash.get("props", {}).get("power") != WANT_POWER:
            fail(f"power 应为 {WANT_POWER}（+500%），实际 {dash.get('props', {}).get('power')}")
        else:
            print("      [OK] power = [600.0]（伤害 +500%）")

        if dash.get("commonProps", {}).get("item") != WANT_STAY_ITEM:
            fail(f"commonProps.item 应为 {WANT_STAY_ITEM}（永不变身 BackDash），实际 {dash.get('commonProps', {}).get('item')}")
        else:
            print("      [OK] commonProps.item = Dash（只能释放 Dash）")

        # 手感相关字段必须保持原版
        if dash.get("props", {}).get("speed") == 5 and dash.get("props", {}).get("range") == 13:
            print("      [OK] speed/range 保持原版（冲刺手感不变）")
        else:
            fail("speed/range 被意外改动")

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

    # res.pak 里必须只有 item/Dash 这一条数据补丁
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
            if entries == ["data.cdb_/item/Dash.json"]:
                print("      [OK] res.pak 仅含 item/Dash 数据补丁（未误改其它 645 条 item / 武器 / 怪物 / 关卡）")
            else:
                fail("res.pak 内容与预期 data.cdb_/item/Dash.json 不一致")

    # ---------- 6. 结论 ----------
    print("\n[6/6] 结论")
    if ok:
        print("      [OK] 模组已构建并安装完成，可直接进游戏测试")
        print(f"      启动器: {HOST_EXE}")
        print("      生效内容：")
        print("        · castCD 0       res.pak 数据补丁 item/Dash.castCD 10 -> 0")
        print("                         （InventItem.getActiveCooldown 直接返回 castCD，技能 0 冷却）")
        print("        · 伤害 +500%     res.pak 数据补丁 item/Dash.props.power [100] -> [600]")
        print("                         （Dash 命中走 _AttackUtils.createFromHeroItem(owner,item,props.power)，")
        print("                           本模组路径伤害沿用同一条链，所以整体 x6）")
        print("        · 只能放 Dash    res.pak 数据补丁 item/Dash.commonProps.item BackDash -> Dash")
        print("                         （Dash.onEnd -> transformInventoryItem 读的就是这个字段，")
        print("                           指回自己 => 技能栏永远是 Dash，不会变成反向冲刺）")
        print("                         运行时还有一层兜底：Hook_Dash.onEnd 之后若发现技能是 BackDash，")
        print("                         立刻再 transformInventoryItem 一次转回 Dash（BackDash 的目标就是 Dash）")
        print("        · res.pak 挂载   IOnAfterLoadingAssets 里 FsPak.loadPak(res.pak)")
        print("                         ⚠ 少了这一步数据补丁完全不生效（castCD/伤害/变身目标全是原版）")
        print("        · 八方向冲刺     Hook_Dash.fixedUpdate 完全接管原版逐帧逻辑（不调 orig）")
        print("                         起手瞬间采样 方向键/WASD -> 单位向量，整段锁死方向；")
        print("                         原版十几处沿 X 轴判定（含 didntMoveLastFrame->endDash）全部绕过")
        print("        · 无视墙体       冲刺期间 collisionMode=IgnoreWalls、hasGravity=false、dx/dy 清零，")
        print("                         位移由本模组直接写 cx/xr/cy/yr（位移与命中统一用『格』做单位），")
        print("                         结束后还原重力/碰撞模式，并做一次卡墙自救")
        print("        · 伤害判定       每次 tick 用「上一帧位置->当前位置」线段对敌方做命中，")
        print("                         半径 = hero.radius/24 + mob.radius/24 + 0.25 格，")
        print("                         同一敌人一次冲刺只结算一次")
        print("        · 淡蓝拖尾       Hook_Dash.postUpdate 不执行原版（橙色 0xEE9B11 / 0xFFFF33），")
        print("                         改画 0x99E5FF 的 tailLine + tailLineFree/timeKeeperDash + OnionSkin 残影")
        print("      测试要点：拿一个 Dash 技能，按住 上/下/左/右/四个斜向 之一再按技能键，")
        print("                应当朝该方向冲刺并穿过墙体；连续按技能键不应变成反向冲刺。")
    else:
        print("      [警告] 存在问题，请按上方提示修复")
    return finish()


def finish():
    print("=" * 74)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
