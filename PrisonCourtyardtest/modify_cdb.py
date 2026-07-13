"""修改 data.cdb — 追加 PrisonCourtyardTest (混乱大道) + T_PrisonCourtyardTest (过渡关)"""
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

# ═══════════════════════════════════════════
# 1. 追加 biome: PrisonCourtyardTestBiome
# ═══════════════════════════════════════════
prison_courtyard_biome = None
for line in biome_sheet['lines']:
    if line.get('id') == 'PrisonCourtyard':
        prison_courtyard_biome = copy.deepcopy(line)
        break

if prison_courtyard_biome:
    new_biome = prison_courtyard_biome
    new_biome['id'] = 'PrisonCourtyardTestBiome'
    new_biome['atlasName'] = 'prisonCourtyardx'
    biome_sheet['lines'].append(new_biome)
    changes += 1
    print(f"[biome] Added PrisonCourtyardTestBiome (atlas: prisonCourtyardx)")
else:
    print("ERROR: PrisonCourtyard biome not found!")

# ═══════════════════════════════════════════
# 2. 追加 level: T_PrisonCourtyardTest (过渡关/休息房)
#    clone from T_PrisonDepths (自带 Collector + PerkShop + Healing)
# ═══════════════════════════════════════════
t_depths_template = None
for line in level_sheet['lines']:
    if line.get('id') == 'T_PrisonDepths':
        t_depths_template = copy.deepcopy(line)
        break

if t_depths_template:
    t_trans = t_depths_template
    t_trans['id'] = 'T_PrisonCourtyardTest'
    t_trans['name'] = '通往混乱大道'
    t_trans['biome'] = 'PrisonCourtyard'
    t_trans['nextLevels'] = [{"gates": 0, "level": "PrisonCourtyardTest"}]
    t_trans['group'] = 0
    t_trans['worldDepth'] = 2
    t_trans['mapDepth'] = 2

    # Ensure world map visibility: canLevelBeDisplayed requires (metaFlags & 4) != 0
    if 'flagsProps' in t_trans and 'metaFlags' in t_trans['flagsProps']:
        t_trans['flagsProps']['metaFlags'] |= 4
    else:
        if 'flagsProps' not in t_trans:
            t_trans['flagsProps'] = {}
        t_trans['flagsProps']['metaFlags'] = 4
    print(f"[level] T_PrisonCourtyardTest metaFlags = {t_trans['flagsProps'].get('metaFlags', 'N/A')}")

    level_sheet['lines'].append(t_trans)
    changes += 1
    print("[level] Added T_PrisonCourtyardTest (transition -> PrisonCourtyardTest)")
else:
    print("ERROR: T_PrisonDepths template not found!")

# ═══════════════════════════════════════════
# 3. 追加 level: PrisonCourtyardTest (混乱大道)
#    clone from PrisonCourtyard
# ═══════════════════════════════════════════
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
    main_level['nextLevels'] = [{"gates": 0, "level": "T_PrisonDepths"}]

    # Keep PrisonCourtyard's mob/loot balance but tweak
    main_level['mobDensity'] = 0.9
    main_level['minGold'] = 1500
    main_level['eliteWanderChance'] = 0.15
    main_level['eliteRoomChance'] = 0.5

    # 卷轴设置：20 个卷轴（10 个三选一 + 10 个双选）
    main_level['baseLootLevel'] = 5
    main_level['tripleUps'] = 10
    main_level['doubleUps'] = 10
    main_level['quarterUpsBC3'] = 0
    main_level['quarterUpsBC4'] = 0
    main_level['cellBonus'] = 0.5

    # Ensure world map visibility: canLevelBeDisplayed requires (metaFlags & 4) != 0
    if 'flagsProps' in main_level and 'metaFlags' in main_level['flagsProps']:
        main_level['flagsProps']['metaFlags'] |= 4
    else:
        # If flagsProps missing, create minimal structure
        if 'flagsProps' not in main_level:
            main_level['flagsProps'] = {}
        main_level['flagsProps']['metaFlags'] = 4
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
# 4. 修改 PrisonCourtyard 的 nextLevels — 追加 T_PrisonCourtyardTest
# ═══════════════════════════════════════════
pc_found = False
for line in level_sheet['lines']:
    if line.get('id') == 'PrisonCourtyard':
        next_levels = line.get('nextLevels', [])
        # Check if already added
        already = any(nl.get('level') == 'T_PrisonCourtyardTest' for nl in next_levels)
        if not already:
            next_levels.append({"gates": 0, "level": "T_PrisonCourtyardTest"})
            line['nextLevels'] = next_levels
            changes += 1
            print("[level] PrisonCourtyard nextLevels += T_PrisonCourtyardTest")
        else:
            print("[level] SKIP — T_PrisonCourtyardTest already in PrisonCourtyard nextLevels")
        pc_found = True
        break

if not pc_found:
    print("ERROR: PrisonCourtyard level not found in sheet!")

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
print("  level: +2 (PrisonCourtyardTest, T_PrisonCourtyardTest)")
print("  mod:   PrisonCourtyard nextLevels += T_PrisonCourtyardTest")
