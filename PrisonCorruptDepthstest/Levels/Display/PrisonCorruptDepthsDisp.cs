using dc;
using dc.h2d;
using dc.hl.types;
using dc.level;
using dc.libs;
using dc.pr;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;

namespace PrisonCorruptDepthstest.Levels.Display;

/// <summary>
/// 深层腐化牢房自定义 LevelDisp。
/// 仿 dlc/Midjourney 的 GardenDisp 模式，继承 DynamicBiomeDisp。
/// 现阶段使用默认渲染，后续可 override decorateZone/decorateRoom 添加自定义装饰。
/// </summary>
public class PrisonCorruptDepthsDisp : DynamicBiomeDisp
{
    public PrisonCorruptDepthsDisp(
        Level level,
        LevelMap map,
        dc.String mainBiomeKind,
        dc.String otherBiomeKind,
        virtual_blendAmbient_blendAmbientFog_blendCamDust_blendCamFog_blendGroundSmoke_blendLights_blendShadows_ blendConfiguration,
        ArrayObj parallaxInfo
    ) : base(level, map, mainBiomeKind, otherBiomeKind, blendConfiguration, parallaxInfo)
    {
    }
}
