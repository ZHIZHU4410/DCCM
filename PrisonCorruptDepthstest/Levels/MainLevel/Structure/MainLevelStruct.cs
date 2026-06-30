using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;
using PrisonCorruptDepthstest.Utils;
using Serilog;

namespace PrisonCorruptDepthstest.Levels.MainLevel.Structure;

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
        Log.Debug("[PrisonCorruptDepthstest] buildMainRooms start");

        RoomNode start = base.createNode("Entrance".AsHlxStr(), null, null, "start".AsHlxStr());
        Log.Debug("[PrisonCorruptDepthstest] Entrance created");

        RoomNode combat1 = base.createNode("Combat".AsHlxStr(), null, null, "combat_1".AsHlxStr());
        combat1.set_parent(start);
        Log.Debug("[PrisonCorruptDepthstest] Combat 1 created");

        RoomNode combat2 = base.createNode("Combat".AsHlxStr(), null, null, "combat_2".AsHlxStr());
        combat2.set_parent(combat1);
        Log.Debug("[PrisonCorruptDepthstest] Combat 2 created");

        RoomNode combat3 = base.createNode("Combat".AsHlxStr(), null, null, "combat_3".AsHlxStr());
        combat3.set_parent(combat2);
        RoomNode combat4 = base.createNode("Combat".AsHlxStr(), null, null, "combat_4".AsHlxStr());
        combat4.set_parent(combat3);
        RoomNode combat5 = base.createNode("Combat".AsHlxStr(), null, null, "combat_5".AsHlxStr());
        combat5.set_parent(combat4);
        RoomNode combat6 = base.createNode("Combat".AsHlxStr(), null, null, "combat_6".AsHlxStr());
        combat6.set_parent(combat5);
        RoomNode combat7 = base.createNode("Combat".AsHlxStr(), null, null, "combat_7".AsHlxStr());
        combat7.set_parent(combat6);
        RoomNode combat8 = base.createNode("Combat".AsHlxStr(), null, null, "combat_8".AsHlxStr());
        combat8.set_parent(combat7);
        RoomNode combat9 = base.createNode("Combat".AsHlxStr(), null, null, "combat_9".AsHlxStr());
        combat9.set_parent(combat8);
        RoomNode combat10 = base.createNode("Combat".AsHlxStr(), null, null, "combat_10".AsHlxStr());
        combat10.set_parent(combat9);
        Log.Debug("[PrisonCorruptDepthstest] Combat 3-10 created");

        RoomNode exit = base.createExit("DeathArena".AsHlxStr(), null, null, "exit".AsHlxStr());
        exit.set_parent(combat10);
        Log.Debug("[PrisonCorruptDepthstest] Exit -> DeathArena created");

        Log.Debug("[PrisonCorruptDepthstest] buildMainRooms complete");
        return base.nodes.get("start".AsHlxStr());
    }

    public override void buildSecondaryRooms()
    {
        Log.Debug("[PrisonCorruptDepthstest] buildSecondaryRooms");
        base.buildSecondaryRooms();
    }

    public override void buildTimedDoors() { base.buildTimedDoors(); }
    public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
    public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }

    public override void finalize()
    {
        Log.Debug("[PrisonCorruptDepthstest] finalize");
        base.finalize();
    }
}
