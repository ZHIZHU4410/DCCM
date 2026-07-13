using dc;
using dc.level;
using dc.libs;
using dc.pr;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using PrisonCourtyardtest.Core.Interfaces;
using PrisonCourtyardtest.Levels.T_RoofMod.Structure;
using PrisonCourtyardtest.Utils;

namespace PrisonCourtyardtest.Levels.T_RoofMod;

public class T_RoofModLevel : ILevel
{
    public static ModCore.Storage.Config<T_RoofModConfig> Config { get; } = new("T_RoofModConfig");

    public string LevelId => Config.Value.LevelId;
    public string DisplayName => "监狱屋顶（模组）";
    public string Biome => Config.Value.Biome;
    public string DynamicBiome => string.Empty;

    public LevelStruct CreateLevelStruct(User user, virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_ levelData, Rand rng)
    {
        return new T_RoofModLevelStruct(user, levelData, rng);
    }

    public dc.level.LevelDisp CreateLevelDisplay(dc.pr.Level level, dc.level.LevelMap map)
    {
        return null!;
    }

    public void InitializeLevel(dc.pr.Level level) { }
    public object GetConfig() => Config;
    public void RegisterHooks() { }
    public void UnregisterHooks() { }
}
