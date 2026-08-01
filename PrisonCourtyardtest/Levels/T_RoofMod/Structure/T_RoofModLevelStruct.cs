using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;
using PrisonCourtyardtest.Utils;
using Serilog;

namespace PrisonCourtyardtest.Levels.T_RoofMod.Structure;

/// <summary>
/// T_Roof 模组布局：完整的标准休息区（BasicEntrance_R -> Collector -> PerkShop -> Healing），
/// 泉水之后分叉两个出口：电梯（PrisonRoof / 壁垒）和门（PrisonCourtyardTest / 混乱大道）。
/// 原来单独的 T_PrisonCourtyardTest 过渡关卡已合并到这里。
/// </summary>
public class T_RoofModLevelStruct : LevelStruct
{
    public T_RoofModLevelStruct(
        User user,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
        Rand rng
    ) : base(user, level, rng)
    {
        this.defaultGroup = 1;
    }

    public override RoomNode buildMainRooms()
    {
        Log.Debug("[PrisonCourtyardtest] T_RoofMod buildMainRooms start");

        // ── 标准休息区入口（和原版 T_Roof 一致）──
        RoomNode start = base.createNode(null, "BasicEntrance_R".AsHlxStr(), null, "start".AsHlxStr());
        Log.Debug("[PrisonCourtyardtest] T_RoofMod Entrance (BasicEntrance_R) created");

        // ── 收藏家 → 商店 → 泉水 ──
        RoomNode collector = base.createNode("Collector".AsHlxStr(), null, null, null);
        collector.set_parent(start);

        RoomNode perkShop = base.createNode(null, "PerkShop".AsHlxStr(), null, null);
        perkShop.set_parent(collector);

        RoomNode healing = base.createNode("Healing".AsHlxStr(), null, null, null);
        healing.set_parent(perkShop);
        Log.Debug("[PrisonCourtyardtest] T_RoofMod Rest area built (Collector -> PerkShop -> Healing)");

        // ── 泉水房间模板只支持 1 个子出口，所以先接一个 Combat 分叉点，
        //    再由分叉点引出两个出口 ──
        RoomNode fork = base.createNode("Combat".AsHlxStr(), null, null, "fork".AsHlxStr());
        fork.set_parent(healing);
        Log.Debug("[PrisonCourtyardtest] T_RoofMod Fork (after Healing) created");

        // ── 出口 1：电梯 → PrisonRoof（壁垒）──
        RoomNode exitRoof = base.createExit("PrisonRoof".AsHlxStr(), "ExitLiftUp".AsHlxStr(), null, "exit_roof".AsHlxStr());
        exitRoof.set_parent(fork);
        Log.Debug("[PrisonCourtyardtest] T_RoofMod ExitLiftUp -> PrisonRoof");

        // ── 出口 2：门 → PrisonCourtyardTest（混乱大道），直达，不再经过单独过渡关卡 ──
        string targetLevel = GameConstants.Levels.PrisonCourtyardTest;
        RoomNode exitCourtyard = base.createExit(targetLevel.AsHlxStr(), "Exit_LR".AsHlxStr(), null, "exit_courtyard".AsHlxStr());
        exitCourtyard.set_parent(fork);
        Log.Debug("[PrisonCourtyardtest] T_RoofMod Exit_LR -> " + targetLevel);

        Log.Debug("[PrisonCourtyardtest] T_RoofMod buildMainRooms complete (Rest area + 2 Exits)");
        return base.nodes.get("start".AsHlxStr());
    }

    public override void buildSecondaryRooms()
    {
        Log.Debug("[PrisonCourtyardtest] T_RoofMod buildSecondaryRooms");
        base.buildSecondaryRooms();
    }

    public override void buildTimedDoors() { base.buildTimedDoors(); }
    public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
    public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }

    public override void finalize()
    {
        Log.Debug("[PrisonCourtyardtest] T_RoofMod finalize");
        base.finalize();
    }
}
