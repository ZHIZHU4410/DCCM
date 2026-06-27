"""修改 data.cdb — 添加 PrisonCorruptDepths 关卡（mobs=Zombie）"""
import json, copy, sys, os

CDB_PATH = "coremod/mods/TestCorruptPlusLevel/data.cdb"
if not os.path.exists(CDB_PATH):
    CDB_PATH = os.path.join("..", CDB_PATH)

print(f"Loading {CDB_PATH}...")
with open(CDB_PATH, 'r', encoding='utf-8') as f:
    cdb = json.load(f)

# ── 找到 PrisonCourtyard 模板 ──
level_sheet = None
mob_sheet = None
for s in cdb['sheets']:
    if s['name'] == 'level':
        level_sheet = s
    elif s['name'] == 'mob':
        mob_sheet = s

# ── 添加 PZombie 到 mob sheet（复制 Zombie，改 id + name + life） ──
zombie_tpl = None
for line in mob_sheet['lines']:
    if line.get('id') == 'Zombie':
        zombie_tpl = copy.deepcopy(line)
        break

if zombie_tpl:
    pzombie = zombie_tpl
    pzombie['id'] = 'PZombie'
    pzombie['name'] = '腐化拟态怪PZ'
    pzombie['life'] = [150]
    pzombie['score'] = 8
    pzombie['weight'] = 15
    # 移除 blueprints（避免和原版冲突）
    pzombie['blueprints'] = []
    mob_sheet['lines'].append(pzombie)
    print("Added PZombie to mob sheet (Zombie clone with custom stats)")
else:
    print("WARNING: Zombie not found in mob sheet!")

# ── 添加 PrisonCorruptDepths 关卡 ──
prison_tpl = None
for line in level_sheet['lines']:
    if line.get('id') == 'PrisonCourtyard':
        prison_tpl = copy.deepcopy(line)
        break

if prison_tpl:
    new_level = prison_tpl
    new_level['id'] = 'PrisonCorruptDepths'
    new_level['name'] = '深层腐化牢房'
    new_level['worldDepth'] = 2
    new_level['mapDepth'] = 2
    new_level['biome'] = 'PrisonCorrupt'
    new_level['group'] = 0
    new_level['cellBonus'] = 0
    new_level['tripleUps'] = 0
    new_level['doubleUps'] = 0
    new_level['quarterUpsBC3'] = 0
    new_level['quarterUpsBC4'] = 0
    new_level['eliteWanderChance'] = 0
    new_level['eliteRoomChance'] = 0
    new_level['mobDensity'] = 0.8

    # 修改 props：使用腐化牢房的音乐/颜色
    new_level['props'] = {
        "levelTrapFrequency": 0.3,
        "doorColor": 10104763,
        "musicIntro": "music/default/prisoncorrupt_intro.ogg",
        "viewerColor": 7037069,
        "viewerOffsetX": -1,
        "musicLoop": "music/default/prisoncorrupt_loop.ogg",
        "zDoorColor": 2801540,
        "noAdditionalElite": False,
        "viewerY": 5,
        "chromaColor": 16711793,
        "biomeType": 2,
        "loadingDescColor": 12753375,
        "levelSpecificTrap": 2,
        "loadingColor": 3148076
    }

    # mobs: 只用 Zombie（_Mob.create 能识别的名称）
    new_level['mobs'] = [
        {"singleRoom": False, "props": {}, "spawnWith": [],
         "mob": "Zombie", "minDifficulty": 0, "quantityFactor": 0.7, "minCombatRoomsBefore": 0},
        {"singleRoom": False, "props": {}, "spawnWith": [],
         "mob": "Zombie", "minDifficulty": 0, "quantityFactor": 0.5, "minCombatRoomsBefore": 1},
        {"singleRoom": False, "props": {}, "spawnWith": [],
         "mob": "Zombie", "minDifficulty": 0, "quantityFactor": 0.3, "minCombatRoomsBefore": 2},
    ]

    # 修正 nextLevels
    new_level['nextLevels'] = [{"gates": 0, "level": "T_Bridge"}]

    # 修正 lore + flags
    new_level['loreDescriptions'] = [
        {"text": "Donner son corps à la science prend un tout autre sens lorsqu'on est encore vivant."},
        {"text": "Qui aurait pu penser que même la pierre pouvait être infectée ?"},
        {"text": "Les gardiens furent les premiers touchés. À moins que personne ne remarqua la différence sur les prisonniers ?"}
    ]
    new_level['flagsProps'] = {"lootFlags": 0, "visualFlags": 0, "metaFlags": 0, "gameplayFlags": 0, "genFlags": 0}
    new_level['specificLoots'] = []
    new_level['parallax'] = []
    new_level['minGold'] = 500
    new_level['specificSubBiome'] = []
    new_level['baseLootLevel'] = 3

    level_sheet['lines'].append(new_level)
    print("Added PrisonCorruptDepths to level sheet")

    # ── 添加 T_PrisonCorruptDepths 过渡关卡 ──
    trans_tpl = copy.deepcopy(new_level)
    trans_tpl['id'] = 'T_PrisonCorruptDepths'
    trans_tpl['name'] = '通往深层腐化牢房'
    trans_tpl['group'] = 1
    trans_tpl['transitionTo'] = 'PrisonCorruptDepths'
    trans_tpl['worldDepth'] = 0
    trans_tpl['mapDepth'] = 0
    trans_tpl['mobs'] = []
    trans_tpl['mobDensity'] = 0
    trans_tpl['minGold'] = 0
    trans_tpl['nextLevels'] = [{"gates": 0, "level": "PrisonCorruptDepths"}]
    trans_tpl['props'] = {
        "viewerOffsetX": -1,
        "perfectKillsDoor": 1,
        "musicLoop": "music/default/dc_colletor2.ogg",
        "zDoorColor": 389267,
        "viewerY": 4,
        "biomeType": 2
    }
    trans_tpl['flagsProps'] = {"lootFlags": 27, "visualFlags": 0, "metaFlags": 0, "gameplayFlags": 0, "genFlags": 16384}
    trans_tpl['loreDescriptions'] = []
    level_sheet['lines'].append(trans_tpl)
    print("Added T_PrisonCorruptDepths to level sheet")

# ── 保存 ──
backup = CDB_PATH + ".backup"
if not os.path.exists(backup):
    import shutil
    shutil.copy2(CDB_PATH, backup)
    print(f"Backup saved to {backup}")

with open(CDB_PATH, 'w', encoding='utf-8') as f:
    json.dump(cdb, f, indent='\t', ensure_ascii=False)
print(f"Saved modified CDB to {CDB_PATH}")
