# -*- coding: utf-8 -*-
"""
DamageAura (撕裂光环) 强化数据补丁脚本
=====================================
把游戏数据 data.cdb 中 DamageAura 物品条目改为:
    - 范围 distance  x2 (RANGE_MULT)
    - 攻速 tick      /4 (攻击间隔变为 1/4, 即攻速 x4)
    - dps            x4 (保持每次命中伤害不变: dps*tick 恒定)
    - 持续时间 duration x4 (DURATION_MULT)

只生成修改版 data.cdb（输出到 csproj 同级目录），
res.pak 的生成与安装全部由 `dotnet build` 自动完成：
    cdb diff + pak merge -> res.pak -> 打包安装到 coremod/mods/DamageAuraBoost/

用法:
    python patch_aura_data.py [输出目录]
    输出目录默认: 本脚本同级的 DamageAuraBoost/ (csproj 所在目录)

可选参数:
    脚本顶部常量 RANGE_MULT / TICK_DIV / DPS_MULT / DURATION_MULT 可改。
    改完数值后: python patch_aura_data.py && dotnet build -c Debug
"""

import json
import os
import sys

# ===== 可调参数 =====
RANGE_MULT = 2.0      # 范围倍率 (用户选定 x2)
TICK_DIV = 4.0        # 攻速倍率(攻击间隔除数)
DPS_MULT = 4.0        # dps 倍率(与攻速抵消, 保持单次伤害不变)
DURATION_MULT = 4.0   # 持续时间倍率 (用户选定 x4)
CAST_CD = 4.0         # 冷却时间(秒), 原版 12 (用户指定 4)
# ====================

GAME_ROOT = r"D:\steama\steamapps\common\Dead Cells"
TEMPLATE_CDB = os.path.join(GAME_ROOT, r"coremod\core\mdk\databases\v35\data.cdb")
MOD_ID = "DamageAura"
MOD_NAME = "DamageAuraBoost"

HERE = os.path.dirname(os.path.abspath(__file__))


def main():
    # 输出到 csproj 同级目录（默认），dotnet build 会自动用 data.cdb 生成 res.pak 并安装
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "DamageAuraBoost")
    os.makedirs(out_dir, exist_ok=True)

    print("加载模板: " + TEMPLATE_CDB)
    with open(TEMPLATE_CDB, encoding="utf-8") as f:
        data = json.load(f)

    found = False
    for sheet in data["sheets"]:
        if sheet.get("name") != "item":
            continue
        for line in sheet["lines"]:
            if line.get("id") != MOD_ID:
                continue
            p = line["props"]
            old = dict(p)
            old_cast_cd = line.get("castCD")

            p["distance"] = round(p["distance"] * RANGE_MULT, 4)
            p["tick"] = round(p["tick"] / TICK_DIV, 4)
            p["dps"] = [round(d * DPS_MULT, 4) for d in p["dps"]]
            p["duration"] = round(p["duration"] * DURATION_MULT, 4)
            line["castCD"] = CAST_CD

            print("修改 " + MOD_ID + ":")
            for k in ("distance", "tick", "dps", "duration"):
                print("    %s: %s -> %s" % (k, old[k], p[k]))
            print("    castCD: %s -> %s" % (old_cast_cd, line["castCD"]))
            found = True
            break
        break

    if not found:
        raise SystemExit("未在 item sheet 中找到 " + MOD_ID)

    mod_cdb = os.path.join(out_dir, "data.cdb")
    with open(mod_cdb, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=True, separators=(",", ":"))
    print("写出 mod 数据: " + mod_cdb + " (ensure_ascii=True 防 DCCMTool 读码问题)")

    print("")
    print("OK data.cdb 已生成: " + mod_cdb)
    print("提示: 运行 dotnet build -c Debug 会自动完成 cdb diff -> res.pak -> 安装到 coremod/mods/DamageAuraBoost/")


if __name__ == "__main__":
    main()
