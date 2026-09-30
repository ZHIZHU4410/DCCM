#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
BossNoDmgSoftCap 一键构建 / 安装 / 校验脚本
============================================
对应需求：把《死亡细胞》所有 Boss 的伤害软上限（props.dmgSoftCap）删掉。

参考 MonsterDensity400 的目录格式与 Assets + cs 搭配方式：
    BossNoDmgSoftCap/
      BossNoDmgSoftCap/
        BossNoDmgSoftCap.csproj          <- MDK 模组工程（Debug 自动安装）
        BossNoDmgSoftCapMain.cs          <- 挂载 res.pak
        patch_boss_cap.py                <- 生成数据补丁 data.cdb
        Assets/data.cdb_/mob/*.json      <- 由 csproj 自动生成（17 个 Boss）
        data.cdb                         <- 由 patch 脚本自动生成
    安装结果：coremod/mods/BossNoDmgSoftCap/{dll, modinfo.json, res.pak}

脚本流程（全自动，一条命令搞定）：
    1. 检查 MDK 环境
    2. python patch_boss_cap.py       生成 data.cdb（删掉 Boss 的 props.dmgSoftCap）
    3. dotnet build                   编译 + CDB diff + 打包 res.pak + 自动安装
    4. 校验数据补丁（Boss 数量 / dmgSoftCap 是否真的没了 / 其它字段是否被动过）
    5. 校验 res.pak 内容 / 安装目录 / modinfo.json 入口
    6. 打印结论

用法：
    python build_boss_cap.py
"""

import json
import os
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.abspath(__file__))
MOD_NAME = "BossNoDmgSoftCap"
MOD_DIR = os.path.join(ROOT, MOD_NAME, MOD_NAME)
CSPROJ = os.path.join(MOD_DIR, MOD_NAME + ".csproj")
MOD_SRC = os.path.join(MOD_DIR, MOD_NAME + "Main.cs")
PATCH_PY = os.path.join(MOD_DIR, "patch_boss_cap.py")
MOD_CDB = os.path.join(MOD_DIR, "data.cdb")
ASSET_DIR = os.path.join(MOD_DIR, "Assets", "data.cdb_", "mob")

GAME_ROOT = r"D:\steama\steamapps\common\Dead Cells"
DCCM_ROOT = os.path.join(GAME_ROOT, "coremod")
MDK_ROOT = os.environ.get("DCCM_MDK_ROOT", os.path.join(DCCM_ROOT, "core", "mdk"))
DCCM_TOOL = os.path.join(MDK_ROOT, "tools", "DCCMTool.dll")
GAME_CDB = os.path.join(DCCM_ROOT, "DCCMDEAD CELLS", "res", "data.cdb")
TEMPLATE_CDB = os.path.join(MDK_ROOT, "databases", "v35", "data.cdb")

MOD_MAIN = f"{MOD_NAME}.{MOD_NAME}Main"
INSTALL_DIR = os.path.join(DCCM_ROOT, "mods", MOD_NAME)
OUTPUT_DIR = os.path.join(MOD_DIR, "bin", "Debug", "net10.0", "output", MOD_NAME)

ok = True


def run(cmd, cwd=None):
    print("      >>>", " ".join(cmd))
    return subprocess.run(cmd, cwd=cwd, shell=False)


def fail(msg):
    global ok
    print(f"      [错误] {msg}")
    ok = False


def load_mobs(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    for s in data.get("sheets", []):
        if s.get("name") == "mob":
            return {l["id"]: l for l in s.get("lines", [])
                    if isinstance(l, dict) and "id" in l}
    return {}


def main():
    global ok
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    print("=" * 72)
    print("BossNoDmgSoftCap 构建脚本 —— 删除所有 Boss 的伤害软上限")
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
    print("\n[2/6] 生成数值补丁 data.cdb（删除 Boss 的 props.dmgSoftCap）")
    r = run([sys.executable, PATCH_PY])
    if r.returncode != 0:
        fail("patch_boss_cap.py 执行失败")
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
    print("\n[4/6] 校验数据补丁")
    src = GAME_CDB if os.path.exists(GAME_CDB) else TEMPLATE_CDB
    if not os.path.exists(src):
        fail(f"找不到基准 data.cdb: {src}")
    elif not os.path.exists(MOD_CDB):
        fail(f"未生成 {MOD_CDB}")
    else:
        a = load_mobs(src)
        b = load_mobs(MOD_CDB)
        cap_orig = {k for k, v in a.items() if (v.get("props") or {}).get("dmgSoftCap") is not None}
        cap_new = {k for k, v in b.items() if (v.get("props") or {}).get("dmgSoftCap") is not None}
        print(f"      原版带软上限的 Boss: {len(cap_orig)} 个")
        print(f"      补丁后仍带软上限的  : {len(cap_new)} 个 {sorted(cap_new) if cap_new else ''}")
        if cap_new:
            fail("仍有 Boss 带 dmgSoftCap")
        else:
            print(f"      [OK] 全部 {len(cap_orig)} 个 Boss 的 dmgSoftCap 已删除")

        # 其它字段不能被动
        other = []
        for k in cap_orig:
            oa = dict(a[k]); ob = dict(b[k])
            pa = dict(oa.pop("props", {}) or {}); pb = dict(ob.pop("props", {}) or {})
            if oa != ob:
                other.append(k)
            elif set(pa) - set(pb) != {"dmgSoftCap"} or set(pb) - set(pa):
                other.append(k + "(props)")
        if other:
            fail(f"除 dmgSoftCap 外还有字段被改动: {other}")
        else:
            print("      [OK] 除 dmgSoftCap 外没有任何字段被改动")

    if not os.path.isdir(ASSET_DIR):
        fail(f"未生成 Assets 补丁目录: {ASSET_DIR}")
    else:
        files = sorted(os.listdir(ASSET_DIR))
        bad = []
        for fn in files:
            with open(os.path.join(ASSET_DIR, fn), encoding="utf-8") as f:
                j = json.load(f)
            if "dmgSoftCap" in (j.get("props") or {}):
                bad.append(fn)
        print(f"      Assets 补丁文件 {len(files)} 个；仍含 dmgSoftCap 的: {bad if bad else '无'}")
        if bad:
            fail("有补丁文件仍含 dmgSoftCap")
        else:
            print("      [OK] 17 个 Boss 的补丁文件均已删除 dmgSoftCap")

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
        if info.get("main") != MOD_MAIN:
            fail(f"main 不是 {MOD_MAIN}")
        else:
            print("      [OK] 主类入口正确")

    # res.pak 里必须只有 mob 的补丁
    pak = os.path.join(INSTALL_DIR, "res.pak")
    if os.path.exists(pak) and os.path.exists(DCCM_TOOL):
        with tempfile.TemporaryDirectory() as td:
            subprocess.run(["dotnet", DCCM_TOOL, "pak", "unpack", "-i", pak, "-o", td],
                           capture_output=True, text=True)
            entries = []
            for dirpath, _dirs, files in os.walk(td):
                for fn in files:
                    entries.append(os.path.relpath(os.path.join(dirpath, fn), td).replace("\\", "/"))
            print(f"      res.pak 条目数 = {len(entries)}，样例 = {entries[:3]}")
            bad = [e for e in entries if not e.startswith("data.cdb_/mob/")]
            if bad:
                fail(f"res.pak 含非 mob 补丁: {bad[:5]}")
            else:
                print(f"      [OK] res.pak 仅含 data.cdb_/mob/ 补丁（{len(entries)} 个 Boss）")

    # ---------- 6. 结论 ----------
    print("\n[6/6] 结论")
    if ok:
        print("      [OK] 模组已构建并安装完成，可直接进游戏测试")
        print("      生效内容：")
        print("        · 删除所有 Boss 的 props.dmgSoftCap（17 个：Behemoth / Beholder /")
        print("          BeholderTtcl / MamaTick / TimeKeeper / KingsHand / GiantEye /")
        print("          GiantHand / Collector / GardenerBoss / AmazonSurvival / AmazonTactic /")
        print("          AmazonBrutal / Queen / Death / Dooku / DookuBeast）")
        print("        · 原理：_Mob.__inst_construct__ 在 dmgSoftCap == null 时跳过赋值，")
        print("          dmgSoftCapMin/Max 保持 -1；Entity 里 getCappedFinalDamage() 的调用点")
        print("          有 dmgSoftCapMax > 0 守卫，于是软上限分支永不进入。")
    else:
        print("      [警告] 存在问题，请按上方提示修复")
    return finish()


def finish():
    print("=" * 72)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
