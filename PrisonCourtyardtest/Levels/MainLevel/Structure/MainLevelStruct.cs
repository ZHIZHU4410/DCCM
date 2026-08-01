using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using PrisonCourtyardtest.Utils;
using Serilog;

namespace PrisonCourtyardtest.Levels.MainLevel.Structure;

public class MainLevelStruct : LevelStruct
{
    public MainLevelStruct(
        User user,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
        Rand rng
    ) : base(user, level, rng)
    {
        this.defaultGroup = 1;
        this.addCorridorsBeforeRunicZDoors = true;
    }

    public override RoomNode buildMainRooms()
    {
        Log.Debug("[PrisonCourtyardtest] buildMainRooms start");

        // 左右相反：关卡为右→左布局，入口使用天然左门模板 BasicEntrance_L
        RoomNode start = base.createNode(null, "BasicEntrance_L".AsHlxStr(), null, "start".AsHlxStr())
            .addFlag(new RoomFlag.Outside());
        Log.Debug("[PrisonCourtyardtest] Entrance created (BasicEntrance_L, R2L)");

        // ── 前半段：5 个战斗房间（PrisonCourtyard 大道地形，group 35）──
        RoomNode combat1 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_1".AsHlxStr());
        combat1.set_parent(start);

        RoomNode combat2 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_2".AsHlxStr());
        combat2.set_parent(combat1);

        RoomNode combat3 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_3".AsHlxStr());
        combat3.set_parent(combat2);

        RoomNode combat4 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_4".AsHlxStr());
        combat4.set_parent(combat3);

        RoomNode combat5 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_5".AsHlxStr());
        combat5.set_parent(combat4);

        // ── 中段分叉点（Combat，group 1，已证实支持多出口）──
        RoomNode fork = base.createNode("Combat".AsHlxStr(), null, null, "fork".AsHlxStr());
        fork.set_parent(combat5);
        Log.Debug("[PrisonCourtyardtest] Mid fork created");

        // ── 分支 A → PrisonDepths（监狱深处）──
        RoomNode combat6 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_6".AsHlxStr());
        combat6.set_parent(fork);

        RoomNode combat7 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_7".AsHlxStr());
        combat7.set_parent(combat6);

        RoomNode exitDepths = base.createExit("PrisonDepths".AsHlxStr(), null, null, "exit_depths".AsHlxStr());
        exitDepths.set_parent(combat7);
        Log.Debug("[PrisonCourtyardtest] Exit -> PrisonDepths");

        // ── 分支 B → PrisonCorrupt（腐化监狱）──
        RoomNode combat8 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_8".AsHlxStr());
        combat8.set_parent(fork);

        RoomNode combat9 = base.createNode("Combat".AsHlxStr(), null, 35, "combat_9".AsHlxStr());
        combat9.set_parent(combat8);

        RoomNode exitCorrupt = base.createExit("PrisonCorrupt".AsHlxStr(), null, null, "exit_corrupt".AsHlxStr());
        exitCorrupt.set_parent(combat9);
        Log.Debug("[PrisonCourtyardtest] Exit -> PrisonCorrupt");

        // ── 商店：武器 + 技能（前半段侧支路）──
        RoomNode shopWeapon = base.createNode("Shop".AsHlxStr(), null, null, "shop_weapon".AsHlxStr());
        var weaponMerchant = new virtual_forcedMerchantType_();
        weaponMerchant.forcedMerchantType = new MerchantType.Weapons();
        shopWeapon.addGenData(weaponMerchant.ToVirtual<virtual_altarItemGroup_brLegendaryMultiTreasure_broken_cells_doorCost_doorCurse_flaskRefill_forcedMerchantType_forcePauseTimer_isCliffPath_itemInWall_itemLevelBonus_killsMultiTreasure_locked_maxPerks_mins_noHealingShop_shouldBeFlipped_specificBiome_subTeleportTo_timedMultiTreasure_zDoorLock_zDoorType_>());
        shopWeapon.branchBetween("combat_2".AsHlxStr(), "combat_4".AsHlxStr(), null, null);
        Log.Debug("[PrisonCourtyardtest] Weapon shop added (branch between combat_2 and combat_4)");

        RoomNode shopSkill = base.createNode("Shop".AsHlxStr(), null, null, "shop_skill".AsHlxStr());
        var skillMerchant = new virtual_forcedMerchantType_();
        skillMerchant.forcedMerchantType = new MerchantType.Actives();
        shopSkill.addGenData(skillMerchant.ToVirtual<virtual_altarItemGroup_brLegendaryMultiTreasure_broken_cells_doorCost_doorCurse_flaskRefill_forcedMerchantType_forcePauseTimer_isCliffPath_itemInWall_itemLevelBonus_killsMultiTreasure_locked_maxPerks_mins_noHealingShop_shouldBeFlipped_specificBiome_subTeleportTo_timedMultiTreasure_zDoorLock_zDoorType_>());
        shopSkill.branchBetween("combat_3".AsHlxStr(), "combat_5".AsHlxStr(), null, null);
        Log.Debug("[PrisonCourtyardtest] Skill shop added (branch between combat_3 and combat_5)");

        Log.Debug("[PrisonCourtyardtest] buildMainRooms complete (Mid fork -> PrisonDepths | PrisonCorrupt)");
        return base.nodes.get("start".AsHlxStr());
    }

    public override void buildSecondaryRooms()
    {
        Log.Debug("[PrisonCourtyardtest] buildSecondaryRooms");
        base.buildSecondaryRooms();
    }

    public override void buildTimedDoors() { base.buildTimedDoors(); }
    public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
    public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }

    public override void finalize()
    {
        Log.Debug("[PrisonCourtyardtest] finalize");
        base.finalize();
    }
}
