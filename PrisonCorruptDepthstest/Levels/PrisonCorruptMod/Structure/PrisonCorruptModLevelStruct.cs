using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;
using PrisonCorruptDepthstest.Utils;
using Serilog;

namespace PrisonCorruptDepthstest.Levels.PrisonCorruptMod.Structure;

public class PrisonCorruptModLevelStruct : LevelStruct
{
    public PrisonCorruptModLevelStruct(
        User user,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
        Rand rng
    ) : base(user, level, rng)
    {
        this.addCorridorsBeforeRunicZDoors = true;
    }

    public override RoomNode buildMainRooms()
    {
        Log.Debug("[PrisonCorruptDepthstest] PrisonCorruptMod buildMainRooms start");

        RoomNode start = base.createNode("Entrance".AsHlxStr(), null, null, "start".AsHlxStr());

        RoomNode c1 = base.createNode("Combat".AsHlxStr(), null, null, "c1".AsHlxStr());
        c1.set_parent(start);

        RoomNode c2 = base.createNode("Combat".AsHlxStr(), null, null, "c2".AsHlxStr());
        c2.set_parent(c1);

        if (AddCorruptDepthsBranch(c2))
            Log.Debug("[PrisonCorruptDepthstest] branch: c2 -> T_PrisonCorruptDepths");

        RoomNode c3 = base.createNode("Combat".AsHlxStr(), null, null, "c3".AsHlxStr());
        c3.set_parent(c2);

        RoomNode exitSewer = base.createExit("T_SewerDepthsAfterPrison".AsHlxStr(), null, null, "exit_sewer".AsHlxStr());
        exitSewer.set_parent(c3);

        RoomNode exitRoof = base.createExit("T_RoofAfterPrison".AsHlxStr(), null, null, "exit_roof".AsHlxStr());
        exitRoof.set_parent(c3);

        RoomNode exitDooku = base.createExit("T_DookuCastle".AsHlxStr(), null, null, "exit_dooku".AsHlxStr());
        exitDooku.set_parent(c3);

        Log.Debug("[PrisonCorruptDepthstest] PrisonCorruptMod buildMainRooms complete");
        return base.nodes.get("start".AsHlxStr());
    }

    private bool AddCorruptDepthsBranch(RoomNode c2)
    {
        try
        {
            if (Data.Class.level?.byId == null) return false;
            if (!Data.Class.level.byId.exists("PrisonCorruptDepths".AsHlxStr())) return false;

            RoomNode branch = base.createExit("T_PrisonCorruptDepths".AsHlxStr(), null, null, "branch_corrupt_depths".AsHlxStr());
            branch.set_parent(c2);
            return true;
        }
        catch { return false; }
    }

    public override void buildSecondaryRooms() { base.buildSecondaryRooms(); }
    public override void buildTimedDoors() { base.buildTimedDoors(); }
    public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
    public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }
    public override void finalize() { base.finalize(); }
}
