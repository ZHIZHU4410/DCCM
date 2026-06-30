using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;
using PrisonCorruptDepthstest.Utils;
using Serilog;

namespace PrisonCorruptDepthstest.Levels.DeathArena.Structure;

public class DeathArenaLevelStruct : LevelStruct
{
    public DeathArenaLevelStruct(
        User user,
        virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
        Rand rng
    ) : base(user, level, rng)
    {
        this.defaultGroup = 1;
    }

    public override RoomNode buildMainRooms()
    {
        Log.Debug("[PrisonCorruptDepthstest] DeathArena buildMainRooms start");

        RoomNode entrance = base.createNode(null, "DAEntrance".AsHlxStr(), null, "start".AsHlxStr());
        entrance.addFlag(new RoomFlag.Outside());

        RoomNode bossRoom = base.createNode(null, "DAMiddle".AsHlxStr(), null, "boss".AsHlxStr());
        bossRoom.addFlag(new RoomFlag.Outside());
        bossRoom.set_parent(entrance);

        RoomNode exit = base.createExit("T_Bridge".AsHlxStr(), "DAExit".AsHlxStr(), null, "exit".AsHlxStr());
        exit.addFlag(new RoomFlag.Outside());
        exit.set_parent(bossRoom);

        Log.Debug("[PrisonCorruptDepthstest] DeathArena buildMainRooms complete");
        return base.getId("start".AsHlxStr());
    }

    public override void buildSecondaryRooms() { base.buildSecondaryRooms(); }
    public override void buildTimedDoors() { base.buildTimedDoors(); }
    public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
    public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }
    public override void finalize() { base.finalize(); }
    public override void addTeleports() { }
}
