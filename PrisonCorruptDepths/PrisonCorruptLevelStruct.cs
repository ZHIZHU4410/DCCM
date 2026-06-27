#nullable disable

using dc;
using dc.hl.types;
using dc.level;
using dc.libs;
using Hashlink.Virtuals;

namespace dc.level
{
    /// <summary>
    /// 替换原版 PrisonCorrupt（腐化牢房）。
    /// 布局：Entrance → C1 → C2 → C3 → 3 原版出口
    ///                          \→ 分支门 → PrisonCorruptDepths（仅在已注入时创建）
    /// </summary>
    public class PrisonCorruptLevelStruct : LevelStruct
    {
        public PrisonCorruptLevelStruct(
            User user,
            virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ level,
            Rand rng) : base(user, level, rng)
        {
            this.defaultGroup = 1;
            this.addCorridorsBeforeRunicZDoors = true;
        }

        public override RoomNode buildMainRooms()
        {
            var L = TestCorruptPlusLevel.TestCorruptPlusLevelMain.Log;
            var S = TestCorruptPlusLevel.TestCorruptPlusLevelMain.ToHLString;

            L("[PrisonCorrupt] buildMainRooms 开始");

            RoomNode start = base.createNode(S("Entrance"), null, null, S("start"));
            RoomNode c1 = base.createNode(S("Combat"), null, null, S("c1"));
            c1.set_parent(start);
            RoomNode c2 = base.createNode(S("Combat"), null, null, S("c2"));
            c2.set_parent(c1);

            // 分支门：仅在 PrisonCorruptDepths 已注入时创建
            bool hasDepths = false;
            try
            {
                if (Data.Class.level?.byId != null)
                    hasDepths = Data.Class.level.byId.exists(S("PrisonCorruptDepths"));
            }
            catch { }
            L("[PrisonCorrupt] PrisonCorruptDepths exists=" + hasDepths);

            if (hasDepths)
            {
                RoomNode branch = base.createExit(S("PrisonCorruptDepths"), null, null, S("branch_depths"));
                branch.set_parent(c2);
                L("[PrisonCorrupt] 分支门已创建");
            }

            RoomNode c3 = base.createNode(S("Combat"), null, null, S("c3"));
            c3.set_parent(c2);

            // 原版三个出口
            base.createExit(S("T_SewerDepthsAfterPrison"), null, null, S("exit_sewer")).set_parent(c3);
            base.createExit(S("T_RoofAfterPrison"), null, null, S("exit_roof")).set_parent(c3);
            base.createExit(S("T_DookuCastle"), null, null, S("exit_dooku")).set_parent(c3);

            L("[PrisonCorrupt] buildMainRooms 完成");
            return base.nodes.get(S("start"));
        }

        public override void buildSecondaryRooms() { base.buildSecondaryRooms(); }
        public override void buildTimedDoors() { base.buildTimedDoors(); }
        public override void buildZChallengeDoors() { base.buildZChallengeDoors(); }
        public override void buildTriggeredDoors(ArrayObj combatRooms) { base.buildTriggeredDoors(combatRooms); }
        public override void finalize() { base.finalize(); }
    }
}
