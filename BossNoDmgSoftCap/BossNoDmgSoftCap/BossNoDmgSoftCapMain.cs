using dc;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using System;

namespace BossNoDmgSoftCap
{
    /// <summary>
    /// Boss 伤害软上限移除：
    ///   data.cdb（mob sheet）把所有 Boss（group == 5 且 props.dmgSoftCap 非空）的
    ///   props.dmgSoftCap 整个字段删掉，由 csproj 生成 data.cdb_ 补丁打进 res.pak；
    ///   本类在资源加载完成后把 res.pak 挂载进 FsPak，
    ///   游戏 CDBManager 会在关卡生成时合并 data.cdb_ 补丁 → Boss 不再有伤害软上限。
    ///
    /// 原理（见 patch_boss_cap.py 的详细说明）：
    ///   dc.en._Mob.__inst_construct__ 在 props.dmgSoftCap == null 时会直接跳过赋值，
    ///   dmgSoftCapMin / dmgSoftCapMax 保持 _Entity 构造时的 -1；
    ///   而 dc.Entity 里 getCappedFinalDamage() 的调用点有 `dmgSoftCapMax > 0` 守卫，
    ///   所以 -1 时软上限分支根本不会进入 —— 删字段即可彻底去掉软上限。
    ///
    /// 参考 MonsterDensity400 的目录格式与 Assets + cs 搭配方式。
    /// </summary>
    public class BossNoDmgSoftCapMain : ModBase, IOnAfterLoadingAssets
    {
        public BossNoDmgSoftCapMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Logger.Information("[BossNoDmgSoftCap] 已加载：移除所有 Boss 的伤害软上限（data.cdb 补丁）");
        }

        /// <summary>
        /// 资源加载完成后：把本模组 res.pak（含 data.cdb_ 补丁）挂载进 FsPak，
        /// 游戏的 CDBManager 会在首次关卡生成/重载资源时合并 data.cdb_ 补丁。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(BossNoDmgSoftCapMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[BossNoDmgSoftCap] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[BossNoDmgSoftCap] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BossNoDmgSoftCap] res.pak 加载失败");
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
