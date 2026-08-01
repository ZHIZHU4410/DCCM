"""修改 data.cdb — 追加 PrisonCourtyardTest (混乱大道) + PrisonCourtyardTestBiome。

说明：壁垒门 T_Roof 的休息区已由模组代码（T_RoofModLevelStruct）改为
完整的标准休息区布局，泉水之后直接分叉两个出口（壁垒 / 混乱大道），
因此不再需要单独的 T_PrisonCourtyardTest 过渡关卡。
"""
import json, copy, os, sys, shutil

CDB_PATH = os.path.join(os.path.dirname(__file__), "Assets", "data.cdb")
if not os.path.exists(CDB_PATH):
    print(f"ERROR: {CDB_PATH} not found!")
    sys.exit(1)

print(f"Loading {CDB_PATH}...")
with open(CDB_PATH, 'r', encoding='utf-8') as f:
    cdb = json.load(f)

# ── 找到需要的 sheet ──
biome_sheet = level_sheet = None
for s in cdb['sheets']:
    if s['name'] == 'biome':
        biome_sheet = s
    elif s['name'] == 'level':
        level_sheet = s

if not all([biome_sheet, level_sheet]):
    print("ERROR: Could not find all required sheets!")
    sys.exit(1)

changes = 0

def exists(sheet_lines, key, value):
    return any(line.get(key) == value for line in sheet_lines)

# ═══════════════════════════════════════════
# 1. 追加 biome: PrisonCourtyardTestBiome
# ═══════════════════════════════════════════
if exists(biome_sheet['lines'], 'id', 'PrisonCourtyardTestBiome'):
    print("[biome] SKIP — PrisonCourtyardTestBiome already exists")
else:
    prison_courtyard_biome = None
    for line in biome_sheet['lines']:
        if line.get('id') == 'PrisonCourtyard':
            prison_courtyard_biome = copy.deepcopy(line)
            break

    if prison_courtyard_biome:
        new_biome = prison_courtyard_biome
        new_biome['id'] = 'PrisonCourtyardTestBiome'
        new_biome['atlasName'] = 'prisonCourtyardx'
        # 虚空腐化配色：与原版 PrisonCourtyard 环境形成明显差异，
        # 配合运行时环境互换 + 花屏特效营造维度撕裂氛围
        new_biome['fog'] = 3871584            # 0x3B1360 深紫黑
        new_biome['fogScale'] = 0.75
        new_biome['ambient'] = 6970061         # 0x6A5ACD 暗紫
        new_biome['celShadow'] = 10173168      # 0x9B3AF0 亮紫影
        new_biome['smoke'] = 1968950           # 0x1E0B36
        new_biome['water'] = 3533000           # 0x35E8C8 毒青
        new_biome['waterLight'] = 3204863      # 0x30E6FF
        new_biome['smokeShader'] = {'speed': 0.12, 'power': 1.6, 'alpha': 0.8, 'mode': 2, 'contrib': 1.2}
        new_biome['camEffects'] = {'lensDustBigAlpha': 2.2, 'lensDustSmallAlpha': 2.2, 'camFogBotAlpha': 2.5, 'camFogTopAlpha': 1.4}
        new_biome['lightColors'] = [
            {'conf': 'Hero', 'color': 10980351},      # 0xA78BFF
            {'conf': 'Lantern', 'color': 16731096},   # 0xFF4BD8
            {'conf': 'Candle', 'color': 5892095}      # 0x59E7FF
        ]
        biome_sheet['lines'].append(new_biome)
        changes += 1
        print("[biome] Added PrisonCourtyardTestBiome (atlas: prisonCourtyardx)")
    else:
        print("ERROR: PrisonCourtyard biome not found!")

# ═══════════════════════════════════════════
# 2. 追加 level: PrisonCourtyardTest (混乱大道)
#    clone from PrisonCourtyard
# ═══════════════════════════════════════════
if exists(level_sheet['lines'], 'id', 'PrisonCourtyardTest'):
    print("[level] SKIP — PrisonCourtyardTest already exists")
else:
    prison_courtyard_level = None
    for line in level_sheet['lines']:
        if line.get('id') == 'PrisonCourtyard':
            prison_courtyard_level = copy.deepcopy(line)
            break

    if prison_courtyard_level:
        main_level = prison_courtyard_level
        main_level['id'] = 'PrisonCourtyardTest'
        main_level['name'] = '混乱大道'
        main_level['biome'] = 'PrisonCourtyardTestBiome'
        main_level['group'] = 0
        main_level['worldDepth'] = 2
        main_level['mapDepth'] = 3
        # 中段分叉：两个出口分别指向监狱深处与腐化监狱
        main_level['nextLevels'] = [
            {"gates": 0, "level": "PrisonDepths"},
            {"gates": 0, "level": "PrisonCorrupt"}
        ]
        main_level.pop('index', None)

        # Keep PrisonCourtyard's mob/loot balance but tweak
        main_level['mobDensity'] = 0.9
        main_level['minGold'] = 1500
        main_level['eliteWanderChance'] = 0.15
        main_level['eliteRoomChance'] = 0.5

        # 卷轴设置：3 个三选一 + 1 个双选
        main_level['baseLootLevel'] = 5
        main_level['tripleUps'] = 3
        main_level['doubleUps'] = 1
        main_level['quarterUpsBC3'] = 0
        main_level['quarterUpsBC4'] = 0
        main_level['cellBonus'] = 0.5

        # World map visibility: canLevelBeDisplayed 要求 group==0 且 metaFlags 第 2 位为 0。
        # 第 2 位置 1 反而会隐藏该关卡，所以这里确保清除第 2 位。
        if 'flagsProps' not in main_level:
            main_level['flagsProps'] = {}
        main_level['flagsProps']['metaFlags'] = main_level['flagsProps'].get('metaFlags', 0) & ~4
        # 左右相反：genFlags 第 0 位 = 右→左布局（配合左门入口 BasicEntrance_L）
        main_level['flagsProps']['genFlags'] = main_level['flagsProps'].get('genFlags', 0) | 1
        print(f"[level] PrisonCourtyardTest metaFlags = {main_level['flagsProps'].get('metaFlags', 'N/A')}")

        # Remove DLC field if present
        if 'dlc' in main_level:
            del main_level['dlc']
        if 'bonusTripleScrollAfterBC' in main_level:
            del main_level['bonusTripleScrollAfterBC']

        level_sheet['lines'].append(main_level)
        changes += 1
        print("[level] Added PrisonCourtyardTest (biome: PrisonCourtyardTestBiome, exit -> T_PrisonDepths)")
    else:
        print("ERROR: PrisonCourtyard level template not found!")

# ═══════════════════════════════════════════
# 3. 清理：确保旧的 T_PrisonCourtyardTest 过渡关卡不再存在，
#    也不出现在 PrisonCourtyard 的 nextLevels 里
# ═══════════════════════════════════════════
before = len(level_sheet['lines'])
level_sheet['lines'] = [line for line in level_sheet['lines'] if line.get('id') != 'T_PrisonCourtyardTest']
if len(level_sheet['lines']) != before:
    changes += 1
    print("[level] Removed legacy T_PrisonCourtyardTest entry")

for line in level_sheet['lines']:
    if line.get('id') == 'PrisonCourtyard':
        next_levels = line.get('nextLevels', [])
        filtered = [nl for nl in next_levels if nl.get('level') != 'T_PrisonCourtyardTest']
        if len(filtered) != len(next_levels):
            line['nextLevels'] = filtered
            changes += 1
            print("[level] PrisonCourtyard nextLevels -= T_PrisonCourtyardTest")
        break

# ═══════════════════════════════════════════
# 保存
# ═══════════════════════════════════════════
if changes == 0:
    print("No changes made.")
    sys.exit(0)

backup = CDB_PATH + ".backup"
if not os.path.exists(backup):
    shutil.copy2(CDB_PATH, backup)
    print(f"Backup saved to {backup}")

with open(CDB_PATH, 'w', encoding='utf-8') as f:
    json.dump(cdb, f, indent='\t', ensure_ascii=False)

print(f"\nDone! {changes} changes saved to {CDB_PATH}")
print("Summary:")
print("  biome: +1 (PrisonCourtyardTestBiome, atlas=prisonCourtyardx)")
print("  level: +1 (PrisonCourtyardTest)")
print("  legacy T_PrisonCourtyardTest removed from CDB")
