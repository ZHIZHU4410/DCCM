using dc;
using dc.level;
using dc.libs;
using dc.pr;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;

namespace PrisonCorruptDepthstest.Core.Interfaces;

public interface ILevel
{
    string LevelId { get; }
    string DisplayName { get; }
    string Biome { get; }
    string DynamicBiome { get; }

    LevelStruct CreateLevelStruct(User user, virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ levelData, Rand rng);
    dc.level.LevelDisp CreateLevelDisplay(dc.pr.Level level, dc.level.LevelMap map);

    void InitializeLevel(dc.pr.Level level);

    object GetConfig();

    void RegisterHooks();

    void UnregisterHooks();
}
