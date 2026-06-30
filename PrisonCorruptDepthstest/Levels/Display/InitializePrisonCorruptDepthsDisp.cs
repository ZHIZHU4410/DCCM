using dc;
using dc.h2d;
using dc.hl.types;
using dc.level;
using dc.libs;
using dc.pr;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace PrisonCorruptDepthstest.Levels.Display;

/// <summary>
/// PrisonCorruptDepthsDisp 工厂方法，仿 dlc/Midjourney 的 InitializeGardenDisp。
/// </summary>
public static class InitializePrisonCorruptDepthsDisp
{
    public static LevelDisp Create(Level level, LevelMap map, string biomeId)
    {
        // Blend 配置
        var blendConfig = new virtual_blendAmbient_blendAmbientFog_blendCamDust_blendCamFog_blendGroundSmoke_blendLights_blendShadows_();
        blendConfig.blendLights = true;
        blendConfig.blendCamDust = true;
        blendConfig.blendCamFog = true;
        blendConfig.blendAmbientFog = false;
        blendConfig.blendAmbient = false;
        blendConfig.blendGroundSmoke = true;
        blendConfig.blendShadows = false;

        // 获取 parallax 信息（从 CDB level 条目）
        ArrayObj parallax = ((HaxeDynObj)Data.Class.level.byId.get(biomeId.AsHaxeString()))
            .ToVirtual<LevelInfo>().parallax;

        // 创建 disp（主 biome + 副 biome 用 PrisonCorrupt 风格）
        var disp = new PrisonCorruptDepthsDisp(
            level, map,
            biomeId.AsHaxeString(),
            "PrisonCorrupt".AsHaxeString(),
            blendConfig,
            parallax
        );

        // 配置 junk + torch 特效
        disp.junkMode = new JunkMode.OnlyInside();
        disp.fxTorch = "fxTorchYellow".AsHaxeString();
        disp.fxCauldron = "fxTorchYellow".AsHaxeString();
        disp.fxBrasero = "fxTorchYellow".AsHaxeString();

        return disp;
    }
}
