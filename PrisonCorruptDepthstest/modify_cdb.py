"""修改 data.cdb — 追加 PrisonCorruptDepths 深层腐化牢房 + DeathArena Boss 关卡"""
import json, copy, os, sys

CDB_PATH = os.path.join(os.path.dirname(__file__), "Assets", "data.cdb")
if not os.path.exists(CDB_PATH):
    print(f"ERROR: {CDB_PATH} not found!")
    sys.exit(1)

print(f"Loading {CDB_PATH}...")
with open(CDB_PATH, 'r', encoding='utf-8') as f:
    cdb = json.load(f)

# ── 找到需要的 sheet ──
biome_sheet = level_sheet = room_sheet = None
for s in cdb['sheets']:
    if s['name'] == 'biome':
        biome_sheet = s
    elif s['name'] == 'level':
        level_sheet = s
    elif s['name'] == 'room':
        room_sheet = s

if not all([biome_sheet, level_sheet, room_sheet]):
    print("ERROR: Could not find all required sheets!")
    sys.exit(1)

changes = 0

# ═══════════════════════════════════════════
# 1. 追加 biome: PrisonCorruptDepthsBiome
# ═══════════════════════════════════════════
prison_corrupt_biome = None
for line in biome_sheet['lines']:
    if line.get('id') == 'PrisonCorrupt':
        prison_corrupt_biome = copy.deepcopy(line)
        break

if prison_corrupt_biome:
    new_biome = prison_corrupt_biome
    new_biome['id'] = 'PrisonCorruptDepthsBiome'
    new_biome['atlasName'] = 'jidufuh'
    biome_sheet['lines'].append(new_biome)
    changes += 1
    print(f"[biome] Added PrisonCorruptDepthsBiome (atlas: jidufuh, cloned from PrisonCorrupt)")
else:
    print("ERROR: PrisonCorrupt biome not found!")

# ═══════════════════════════════════════════
# 2. 追加 level: T_PrisonCorruptDepths (过渡关卡)
# ═══════════════════════════════════════════
t_trans = {
    "eliteRoomChance": 0,
    "props": {
        "viewerOffsetX": -1,
        "perfectKillsDoor": 1,
        "musicLoop": "music/default/dc_colletor2.ogg",
        "zDoorColor": 389267,
        "viewerY": 4,
        "biomeType": 2
    },
    "transitionTo": "PrisonCorruptDepths",
    "biome": "PrisonCorrupt",
    "doubleUps": 0,
    "group": 1,
    "quarterUpsBC3": 0,
    "quarterUpsBC4": 0,
    "eliteWanderChance": 0,
    "id": "T_PrisonCorruptDepths",
    "worldDepth": 2,
    "cellBonus": 0,
    "mobs": [],
    "name": "通往深层腐化牢房",
    "mobDensity": 0,
    "specificLoots": [],
    "mapDepth": 0,
    "specificSubBiome": [],
    "minGold": 0,
    "tripleUps": 0,
    "nextLevels": [
        {"gates": 0, "level": "PrisonCorruptDepths"}
    ],
    "flagsProps": {
        "lootFlags": 27,
        "visualFlags": 0,
        "metaFlags": 0,
        "gameplayFlags": 0,
        "genFlags": 16384
    },
    "baseLootLevel": 3,
    "loreDescriptions": [],
    "parallax": [],
    "icon": {"x": 49, "y": 23, "file": "cardIcons.png", "size": 24}
}
level_sheet['lines'].append(t_trans)
changes += 1
print("[level] Added T_PrisonCorruptDepths (transition)")

# ═══════════════════════════════════════════
# 3. 追加 level: PrisonCorruptDepths (深层腐化牢房)
# ═══════════════════════════════════════════
prison_courtyard = None
for line in level_sheet['lines']:
    if line.get('id') == 'PrisonCourtyard':
        prison_courtyard = copy.deepcopy(line)
        break

if prison_courtyard:
    main_level = prison_courtyard
    main_level['id'] = 'PrisonCorruptDepths'
    main_level['name'] = '深层腐化牢房'
    main_level['biome'] = 'PrisonCorruptDepthsBiome'
    main_level['group'] = 0
    main_level['worldDepth'] = 2
    main_level['mapDepth'] = 3
    main_level['mobDensity'] = 1.1
    main_level['baseLootLevel'] = 3
    main_level['minGold'] = 3000
    main_level['cellBonus'] = 0.2
    main_level['tripleUps'] = 2
    main_level['doubleUps'] = 2
    main_level['quarterUpsBC3'] = 2
    main_level['quarterUpsBC4'] = 1
    main_level['eliteWanderChance'] = 0.2
    main_level['eliteRoomChance'] = 0.8

    main_level['props'] = {
        "doorColor": 10104763,
        "musicLoop": "music/default/prisoncorrupt_loop.ogg",
        "musicIntro": "music/default/prisoncorrupt_intro.ogg",
        "chromaColor": 16711793,
        "biomeType": 2,
        "viewerY": 5,
        "viewerOffsetX": -1,
        "zDoorColor": 2801540,
        "loadingColor": 3148076,
        "loadingDescColor": 12753375,
        "levelTrapFrequency": 0.3,
        "levelSpecificTrap": 2
    }

    main_level['nextLevels'] = [
        {"gates": 0, "level": "DeathArena"}
    ]
    main_level['flagsProps'] = {
        "lootFlags": 0, "visualFlags": 0, "metaFlags": 0,
        "gameplayFlags": 0, "genFlags": 0
    }
    main_level['specificLoots'] = []
    main_level['loreDescriptions'] = [
        {"text": "Après les émeutes de la Nuit Sanglante, les gardiens ont décidé de condamner toute une aile de la prison pour y balancer les corps."},
        {"text": "Une épaisse couche de cendre a recouvert les murs de cet endroit. La crasse s'est déposée jusque dans les moindres recoins."},
        {"text": "Un des pires endroits de l'île... Ou l'un des plus sûrs, selon certains."}
    ]
    main_level['parallax'] = []
    main_level['specificSubBiome'] = []
    if 'transitionTo' in main_level:
        del main_level['transitionTo']
    if 'dlc' in main_level:
        main_level['dlc'] = None
    main_level['bonusTripleScrollAfterBC'] = 'VeryHard'

    level_sheet['lines'].append(main_level)
    changes += 1
    print("[level] Added PrisonCorruptDepths (biome: PrisonCorruptDepthsBiome, exit -> DeathArena)")
else:
    print("ERROR: PrisonCourtyard level template not found!")

# ═══════════════════════════════════════════
# 4. 追加 level: DeathArena (Boss 竞技场)
# ═══════════════════════════════════════════
death_arena = {
    "eliteRoomChance": 0,
    "props": {
        "doorColor": 15393813,
        "viewerColor": 51036,
        "musicLoop": "music/default/death_arena_amb.ogg",
        "zDoorColor": 6897664,
        "chromaColor": 62719,
        "biomeType": 2,
        "loadingDescColor": 4312202,
        "loadingColor": 399638
    },
    "biome": "PrisonCorruptDepthsBiome",
    "doubleUps": 0,
    "group": 0,
    "quarterUpsBC3": 0,
    "quarterUpsBC4": 0,
    "eliteWanderChance": 0,
    "id": "DeathArena",
    "worldDepth": 3,
    "cellBonus": 0,
    "mobs": [],
    "name": "死亡竞技场",
    "mobDensity": 0,
    "specificLoots": [],
    "mapDepth": 4,
    "specificSubBiome": [],
    "minGold": 2500,
    "tripleUps": 0,
    "nextLevels": [
        {"gates": 0, "level": "T_Bridge"}
    ],
    "flagsProps": {
        "lootFlags": 0,
        "visualFlags": 0,
        "metaFlags": 1,
        "gameplayFlags": 4,
        "genFlags": 1
    },
    "baseLootLevel": 5,
    "loreDescriptions": [],
    "parallax": [],
    "icon": {"x": 64, "y": 3, "file": "cardIcons.png", "size": 24}
}
level_sheet['lines'].append(death_arena)
changes += 1
print("[level] Added DeathArena (biome: PrisonCorruptDepthsBiome, exit -> T_Bridge)")

# ═══════════════════════════════════════════
# 5. 追加 room: DAEntrance, DAMiddle, DAExit
# ═══════════════════════════════════════════
# Check if rooms already exist
existing_room_ids = {line.get('id') for line in room_sheet['lines']}
rooms_added = 0
for room in [
    {"group": 129, "id": "DAEntrance", "flags": 6, "type": "Entrance", "active": True},
    {"group": 129, "id": "DAMiddle",   "flags": 6, "type": "Boss",     "active": True},
    {"group": 129, "id": "DAExit",     "flags": 6, "type": "Exit",     "active": True},
]:
    if room['id'] not in existing_room_ids:
        room_sheet['lines'].append(room)
        rooms_added += 1
        print(f"[room] Added {room['id']} (group: 129, type: {room['type']})")
    else:
        print(f"[room] SKIP {room['id']} (already exists)")

if rooms_added > 0:
    changes += rooms_added

# ═══════════════════════════════════════════
# 保存
# ═══════════════════════════════════════════
if changes == 0:
    print("No changes made.")
    sys.exit(0)

backup = CDB_PATH + ".backup"
if not os.path.exists(backup):
    import shutil
    shutil.copy2(CDB_PATH, backup)
    print(f"Backup saved to {backup}")

with open(CDB_PATH, 'w', encoding='utf-8') as f:
    json.dump(cdb, f, indent='\t', ensure_ascii=False)

print(f"\nDone! {changes} changes saved to {CDB_PATH}")
print("Summary:")
print("  biome: +1 (PrisonCorruptDepthsBiome, atlas=jidufuh)")
print("  level: +3 (T_PrisonCorruptDepths, PrisonCorruptDepths, DeathArena)")
print(f"  room:  +{rooms_added} (DAEntrance, DAMiddle, DAExit)")
