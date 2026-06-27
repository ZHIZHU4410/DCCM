#nullable disable

using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;

namespace dc.level
{
    public class TestCorruptPlusTransitionStruct : LevelStruct
    {
        public TestCorruptPlusTransitionStruct(
            User user,
            virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
            Rand rng
        ) : base(user, level, rng)
        {
            this.defaultGroup = 1;
        }

        public override RoomNode buildMainRooms()
        {
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[T_PrisonCorruptDepths] V25 过渡关卡 buildMainRooms 开始");

            RoomNode start = base.createNode(TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("Entrance"), null, null, TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("start"));
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[T_PrisonCorruptDepths] 已创建 Entrance");

            RoomNode exit = base.createExit(TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("PrisonCorruptDepths"), null, null, TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("exit"));
            exit.set_parent(start);
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[T_PrisonCorruptDepths] 已创建 Exit -> PrisonCorruptDepths");

            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[T_PrisonCorruptDepths] V25 过渡关卡 buildMainRooms 完成");

            return base.nodes.get(TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("start"));
        }

        public override void buildSecondaryRooms() { base.buildSecondaryRooms(); }
        public override void buildTimedDoors() { base.buildTimedDoors(); }
        public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
        public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }
        public override void finalize() { base.finalize(); }
    }
}
