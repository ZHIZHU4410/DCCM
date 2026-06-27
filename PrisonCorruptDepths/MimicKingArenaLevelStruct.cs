#nullable disable

using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;

namespace dc.level
{
    /// <summary>
    /// Boss Arena — 劫持 DeathArena 的房间模板和 TMX
    /// 布局：DAEntrance → DAMiddle (BossSpot: Death) → DAExit → T_Bridge
    /// </summary>
    public class MimicKingArenaLevelStruct : LevelStruct
    {
        public MimicKingArenaLevelStruct(
            User user,
            virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
            Rand rng) : base(user, level, rng)
        {
            this.defaultGroup = 1;
        }

        public override RoomNode buildMainRooms()
        {
            var S = TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString;

            RoomNode entrance = base.createNode(null, S("DAEntrance"), null, S("start"));
            entrance.addFlag(new RoomFlag.Outside());

            RoomNode bossRoom = base.createNode(null, S("DAMiddle"), null, S("boss"));
            bossRoom.addFlag(new RoomFlag.Outside());
            bossRoom.set_parent(entrance);

            RoomNode exit = base.createExit(S("T_Bridge"), S("DAExit"), null, S("exit"));
            exit.addFlag(new RoomFlag.Outside());
            exit.set_parent(bossRoom);

            return base.getId(S("start"));
        }

        public override void buildSecondaryRooms() { base.buildSecondaryRooms(); }
        public override void buildTimedDoors() { base.buildTimedDoors(); }
        public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
        public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }
        public override void finalize() { base.finalize(); }
        public override void addTeleports() { }
    }
}
