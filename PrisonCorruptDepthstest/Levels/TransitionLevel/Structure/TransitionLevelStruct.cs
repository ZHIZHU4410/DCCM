using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;
using PrisonCorruptDepthstest.Utils;
using Serilog;

namespace PrisonCorruptDepthstest.Levels.TransitionLevel.Structure;

public class TransitionLevelStruct : LevelStruct
{
    public TransitionLevelStruct(
        User user,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
        Rand rng
    ) : base(user, level, rng)
    {
        this.defaultGroup = 1;
    }

    public override RoomNode buildMainRooms()
    {
        Log.Debug("[PrisonCorruptDepthstest] Transition buildMainRooms start");

        RoomNode start = base.createNode("Entrance".AsHlxStr(), null, null, "start".AsHlxStr());
        RoomNode exit = base.createExit("PrisonCorruptDepths".AsHlxStr(), null, null, "exit".AsHlxStr());
        exit.set_parent(start);

        Log.Debug("[PrisonCorruptDepthstest] Transition buildMainRooms complete");
        return base.nodes.get("start".AsHlxStr());
    }

    public override void buildSecondaryRooms() { base.buildSecondaryRooms(); }
    public override void buildTimedDoors() { base.buildTimedDoors(); }
    public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
    public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }
    public override void finalize() { base.finalize(); }
}
