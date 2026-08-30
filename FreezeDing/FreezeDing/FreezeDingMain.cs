#nullable disable

using dc;
using dc.en;
using dc.hxd;
using dc.hxd.res;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;

namespace FreezeDing
{
    /// <summary>
    /// FreezeDing：敌人被冰冻时播放 dingding.wav
    /// ===========================================
    /// 触发点：Hook_Fx.freeze
    ///   原版 Entity.onAffectChange(23, true)（冰冻 affect 生效那一刻）只会调用一次
    ///   level.fx.freeze(e, c) 来播冰霜粒子 —— 所以这个钩子就是"敌人获得冰冻 buff"
    ///   的精确时刻，不会每帧触发。
    ///
    /// 音效：dingding.wav 已打进本模组 res.pak（pak 内路径 sfx/dingding.wav），
    ///   运行时 loadPak 挂载后用 _Res 读取，再用关卡音频系统在冰冻位置播放。
    /// </summary>
    public class FreezeDingMain : ModBase, IOnAfterLoadingAssets, IOnGameExit
    {
        /// <summary>音效在 res.pak 内的路径（Assets/sfx/dingding.wav → sfx/dingding.wav）。</summary>
        private const string SoundPath = "sfx/dingding.wav";

        /// <summary>
        /// 两次 ding 的最小间隔（毫秒），防止冰雷/冰弓等一次性冻住多个敌人时声音叠爆。
        /// 想每次冰冻都单独响一声，改成 0 即可。
        /// </summary>
        private const long MinIntervalMs = 90;

        /// <summary>缓存已加载的音效（懒加载，第一次冰冻时才读取）。</summary>
        private Sound _ding;

        /// <summary>上次播放时间戳（Environment.TickCount64，毫秒）。</summary>
        private long _lastPlayMs;

        public FreezeDingMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_Fx.freeze += OnFreeze;
            Console.WriteLine("[FreezeDing] 已加载：敌人获得冰冻 buff 时播放 dingding.wav");
        }

        /// <summary>资源加载完成：挂载本模组的 res.pak 并预加载音效。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string pakPath = Info.ModRoot!.GetFilePath("res.pak");
                FsPak.Instance.FileSystem.loadPak(pakPath.AsHaxeString());
                Logger.Information($"[FreezeDing] res.pak 已加载: {pakPath}");

                var loader = Res.Class.get_loader();
                dc.String path = SoundPath.AsHaxeString();
                if (loader.exists(path))
                {
                    _ding = (Sound)loader.loadCache(path, Sound.Class);
                }
                else
                {
                    Logger.Warning($"[FreezeDing] res.pak 中未找到 {SoundPath}，冰冻将不会播放音效");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[FreezeDing] res.pak 加载失败");
            }
        }

        /// <summary>
        /// 原版 Fx.freeze 只在实体被施加冰冻 affect(23) 的那一刻调用一次，
        /// 因此这里是"敌人被上冰冻 buff"的精确触发点。
        /// </summary>
        private void OnFreeze(Hook_Fx.orig_freeze orig, Fx self, Entity e, int c)
        {
            orig(self, e, c); // 保留原版冰霜粒子特效

            try
            {
                if (e is not dc.en.Mob mob) return;        // 只对敌人生效（Boss 继承自 Mob）
                if (mob.destroyed || mob._level == null) return;
                if (_ding == null) return;

                // 简易防刷屏：同一瞬间冻住 N 个敌人时，控制声音间隔
                long now = Environment.TickCount64;
                if (now - _lastPlayMs < MinIntervalMs) return;
                _lastPlayMs = now;

                // 在冰冻位置播放（世界坐标：1 格 = 24 像素）
                dc.level.LevelAudio lAudio = mob._level.lAudio;
                double x = ((double)mob.cx + mob.xr) * 24.0;
                double y = ((double)mob.cy + mob.yr) * 24.0 - mob.hei * 0.5;
                lAudio.playEventAt(_ding, x, y, null, null, null);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[FreezeDing] 播放音效失败");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Fx.freeze -= OnFreeze;
            Console.WriteLine("[FreezeDing] 已卸载");
        }
    }
}
