#nullable disable

using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;

namespace dc.level
{
    public class TestCorruptPlusLevelStruct : LevelStruct
    {
        public TestCorruptPlusLevelStruct(
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
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[PrisonCorruptDepths] V25 主关卡 buildMainRooms 开始");

            RoomNode start = base.createNode(TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("Entrance"), null, null, TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("start"));
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[PrisonCorruptDepths] 已创建 Entrance");

            RoomNode combat1 = base.createNode(TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("Combat"), null, null, TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("combat_1"));
            combat1.set_parent(start);
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[PrisonCorruptDepths] 已创建 Combat 1");

            RoomNode combat2 = base.createNode(TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("Combat"), null, null, TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("combat_2"));
            combat2.set_parent(combat1);
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[PrisonCorruptDepths] 已创建 Combat 2");

            // 出口 → Boss 房间 (MimicKingArena)
            RoomNode exit = base.createExit(TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("DeathArena"), null, null, TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("exit"));
            exit.set_parent(combat2);
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[PrisonCorruptDepths] 已创建 Exit -> DeathArena (Boss)");

            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[PrisonCorruptDepths] V25 主关卡 buildMainRooms 完成");

            return base.nodes.get(TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString("start"));
        }

        public override void buildSecondaryRooms()
        {
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[PrisonCorruptDepths] buildSecondaryRooms");
            base.buildSecondaryRooms();
        }
        public override void buildTimedDoors()
        {
            base.buildTimedDoors();
        }
        public override void buildZChallengeDoors()
        {
            base.buildZChallengeDoors();
        }
        public override void buildTriggeredDoors(ArrayObj combatRooms)
        {
            base.buildTriggeredDoors(combatRooms);
        }
        public override void finalize()
        {
            TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log("[PrisonCorruptDepths] finalize → base.finalize()");
            base.finalize();
        }
    }
}
