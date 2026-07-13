using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;
using PrisonCourtyardtest.Utils;
using Serilog;

namespace PrisonCourtyardtest.Levels.T_RoofMod.Structure;

/// <summary>
/// T_Roof 模组布局：入口后插入 Combat 分叉点，Combat 房间支持多个子节点，
/// 从而在休息区域提供两个出口：电梯（PrisonRoof）和门（T_PrisonCourtyardTest → 混乱大道）。
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

        // ── 入口（children=1，满足模板约束）──
        RoomNode start = base.createNode("Entrance".AsHlxStr(), null, null, "start".AsHlxStr());
        Log.Debug("[PrisonCourtyardtest] T_RoofMod Entrance created");

        // ── Combat 分叉点：Combat 房间支持多个子节点 ──
        RoomNode fork = base.createNode("Combat".AsHlxStr(), null, null, "fork".AsHlxStr());
        fork.set_parent(start);
        Log.Debug("[PrisonCourtyardtest] T_RoofMod Fork created");

        // ── 原版出口：电梯 → PrisonRoof ──
        RoomNode exitRoof = base.createExit("PrisonRoof".AsHlxStr(), "ExitLiftUp".AsHlxStr(), null, "exit_roof".AsHlxStr());
        exitRoof.set_parent(fork);
        Log.Debug("[PrisonCourtyardtest] T_RoofMod ExitLiftUp -> PrisonRoof");

        // ── 新增出口：门 → T_PrisonCourtyardTest（混乱大道）──
        string targetLevel = GameConstants.Levels.T_PrisonCourtyardTest;
        RoomNode exitCourtyard = base.createExit(targetLevel.AsHlxStr(), "Exit_LR".AsHlxStr(), null, "exit_courtyard".AsHlxStr());
        exitCourtyard.set_parent(fork);
        Log.Debug("[PrisonCourtyardtest] T_RoofMod Exit_LR -> " + targetLevel);

        Log.Debug("[PrisonCourtyardtest] T_RoofMod buildMainRooms complete (Entrance + Fork + 2 Exits)");
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
