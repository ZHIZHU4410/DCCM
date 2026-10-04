using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BackBlinkOverhaul
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum BlinkFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        // 1.1 传送规则
        WallPierce,
        InfiniteRange,
        AnyEnemy,
        LandingFix,
        FallbackBlink,

        // 1.2 冷却与充能
        NoCooldown,
        InfiniteCharges,

        // 1.3 视觉表现
        Trail,
        AfterImage,
        SpeedFeel,

        // 1.4 音效与反馈
        Feedback,

        // 2. 落地冲击波
        Shockwave,
        IgnoreShield,

        // 3. 连锁传送
        Chain,
        ChainHud,

        // 便捷功能
        ScrollHotkey,
    }

    /// <summary>
    /// 功能开关总控：把"开关值 / 热键名 / 菜单显示名"集中在一张表里
    /// （与 DamageAuraBoost 的 AuraFeatures 同一套做法）。
    ///
    /// 用法（调用点）：<c>if (!BlinkFeatures.IsOn(BlinkFeature.Shockwave)) return;</c>
    /// **不要**直接去读 <see cref="BlinkConfig.EnableXxx"/> —— 那样会绕过总开关。
    ///
    /// 总开关语义：
    ///   · <c>EnableMod = false</c> → <c>IsOn()</c> 对**除 Mod 自己以外**的一切返回 false；
    ///   · 但 Mod 自己始终可切换（热键 + 菜单复选框都照常），否则就没法从游戏里开回来。
    /// </summary>
    public static class BlinkFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly BlinkFeature[] All =
        {
            BlinkFeature.Mod,
            BlinkFeature.WallPierce,
            BlinkFeature.InfiniteRange,
            BlinkFeature.AnyEnemy,
            BlinkFeature.LandingFix,
            BlinkFeature.FallbackBlink,
            BlinkFeature.NoCooldown,
            BlinkFeature.InfiniteCharges,
            BlinkFeature.Trail,
            BlinkFeature.AfterImage,
            BlinkFeature.SpeedFeel,
            BlinkFeature.Feedback,
            BlinkFeature.Shockwave,
            BlinkFeature.IgnoreShield,
            BlinkFeature.Chain,
            BlinkFeature.ChainHud,
            BlinkFeature.ScrollHotkey,
        };

        /// <summary>菜单里那一行的标题。</summary>
        public static string Label(BlinkFeature f) => f switch
        {
            BlinkFeature.Mod => "总开关",
            BlinkFeature.WallPierce => "无视墙体传送",
            BlinkFeature.InfiniteRange => "无视距离传送",
            BlinkFeature.AnyEnemy => "锁定任意敌人",
            BlinkFeature.LandingFix => "落点安全修正",
            BlinkFeature.FallbackBlink => "无目标时盲闪",
            BlinkFeature.NoCooldown => "移除冷却",
            BlinkFeature.InfiniteCharges => "无充能限制",
            BlinkFeature.Trail => "传送拖尾",
            BlinkFeature.AfterImage => "残影 / 起点终点特效",
            BlinkFeature.SpeedFeel => "速度感（慢动作）",
            BlinkFeature.Feedback => "音效 / 屏幕震动",
            BlinkFeature.Shockwave => "落地冲击波 Blink Strike",
            BlinkFeature.IgnoreShield => "冲击波无视护盾",
            BlinkFeature.Chain => "连锁传送 Chain Blink",
            BlinkFeature.ChainHud => "连锁状态提示",
            BlinkFeature.ScrollHotkey => "T 键召唤全属性卷轴",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(BlinkFeature f) => f switch
        {
            BlinkFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            BlinkFeature.WallPierce => "传送路径不再需要无遮挡，可穿墙",
            BlinkFeature.InfiniteRange => "锁敌距离放到 9999，整关任意敌人",
            BlinkFeature.AnyEnemy => "不再限制近距离，任意可锁定敌人",
            BlinkFeature.LandingFix => "落点被堵时自动挪到最近的可站立格",
            BlinkFeature.FallbackBlink => "附近没有敌人时朝面向方向盲闪",
            BlinkFeature.NoCooldown => "冷却设为 0，可连续释放",
            BlinkFeature.InfiniteCharges => "释放后立刻清空冷却与充能",
            BlinkFeature.Trail => "起点到落点的彩色拖尾（颜色可配置）",
            BlinkFeature.AfterImage => "残影 + 起点/终点烟雾与冲击",
            BlinkFeature.SpeedFeel => "传送瞬间的慢动作与速度感",
            BlinkFeature.Feedback => "传送 / 落地音效 + 屏幕震动",
            BlinkFeature.Shockwave => "落地瞬间以落点为圆心的范围伤害",
            BlinkFeature.IgnoreShield => "强制给冲击波挂 IgnoreGlobalShield 词条",
            BlinkFeature.Chain => "击杀后窗口内免费再闪一次（可叠层）",
            BlinkFeature.ChainHud => "连锁窗口 / 层数的界面与特效提示",
            BlinkFeature.ScrollHotkey => "按 T 键在脚下掉落一个 AllUp 卷轴（参考 chuansong+shengcheng）",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(BlinkFeature f)
        {
            try
            {
                var c = BlinkKeys.Config.Value;
                return f switch
                {
                    BlinkFeature.Mod => c.EnableMod,
                    BlinkFeature.WallPierce => c.EnableWallPierce,
                    BlinkFeature.InfiniteRange => c.EnableInfiniteRange,
                    BlinkFeature.AnyEnemy => c.EnableAnyEnemy,
                    BlinkFeature.LandingFix => c.EnableLandingFix,
                    BlinkFeature.FallbackBlink => c.EnableFallbackBlink,
                    BlinkFeature.NoCooldown => c.EnableNoCooldown,
                    BlinkFeature.InfiniteCharges => c.EnableInfiniteCharges,
                    BlinkFeature.Trail => c.EnableTrail,
                    BlinkFeature.AfterImage => c.EnableAfterImage,
                    BlinkFeature.SpeedFeel => c.EnableSpeedFeel,
                    BlinkFeature.Feedback => c.EnableFeedback,
                    BlinkFeature.Shockwave => c.EnableShockwave,
                    BlinkFeature.IgnoreShield => c.ForceIgnoreGlobalShield,
                    BlinkFeature.Chain => c.EnableChain,
                    BlinkFeature.ChainHud => c.EnableChainHud,
                    BlinkFeature.ScrollHotkey => c.EnableScrollHotkey,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：写回配置并落盘。</summary>
        public static void Set(BlinkFeature f, bool on)
        {
            try
            {
                var c = BlinkKeys.Config.Value;
                switch (f)
                {
                    case BlinkFeature.Mod: c.EnableMod = on; break;
                    case BlinkFeature.WallPierce: c.EnableWallPierce = on; break;
                    case BlinkFeature.InfiniteRange: c.EnableInfiniteRange = on; break;
                    case BlinkFeature.AnyEnemy: c.EnableAnyEnemy = on; break;
                    case BlinkFeature.LandingFix: c.EnableLandingFix = on; break;
                    case BlinkFeature.FallbackBlink: c.EnableFallbackBlink = on; break;
                    case BlinkFeature.NoCooldown: c.EnableNoCooldown = on; break;
                    case BlinkFeature.InfiniteCharges: c.EnableInfiniteCharges = on; break;
                    case BlinkFeature.Trail: c.EnableTrail = on; break;
                    case BlinkFeature.AfterImage: c.EnableAfterImage = on; break;
                    case BlinkFeature.SpeedFeel: c.EnableSpeedFeel = on; break;
                    case BlinkFeature.Feedback: c.EnableFeedback = on; break;
                    case BlinkFeature.Shockwave: c.EnableShockwave = on; break;
                    case BlinkFeature.IgnoreShield: c.ForceIgnoreGlobalShield = on; break;
                    case BlinkFeature.Chain: c.EnableChain = on; break;
                    case BlinkFeature.ChainHud: c.EnableChainHud = on; break;
                    case BlinkFeature.ScrollHotkey: c.EnableScrollHotkey = on; break;
                }
                BlinkKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(BlinkFeature f)
        {
            try
            {
                var c = BlinkKeys.Config.Value;
                return f switch
                {
                    BlinkFeature.Mod => c.KeyToggleMod,
                    BlinkFeature.WallPierce => c.KeyToggleWallPierce,
                    BlinkFeature.InfiniteRange => c.KeyToggleInfiniteRange,
                    BlinkFeature.AnyEnemy => c.KeyToggleAnyEnemy,
                    BlinkFeature.LandingFix => c.KeyToggleLandingFix,
                    BlinkFeature.FallbackBlink => c.KeyToggleFallbackBlink,
                    BlinkFeature.NoCooldown => c.KeyToggleNoCooldown,
                    BlinkFeature.InfiniteCharges => c.KeyToggleInfiniteCharges,
                    BlinkFeature.Trail => c.KeyToggleTrail,
                    BlinkFeature.AfterImage => c.KeyToggleAfterImage,
                    BlinkFeature.SpeedFeel => c.KeyToggleSpeedFeel,
                    BlinkFeature.Feedback => c.KeyToggleFeedback,
                    BlinkFeature.Shockwave => c.KeyToggleShockwave,
                    BlinkFeature.IgnoreShield => c.KeyToggleIgnoreShield,
                    BlinkFeature.Chain => c.KeyToggleChain,
                    BlinkFeature.ChainHud => c.KeyToggleChainHud,
                    BlinkFeature.ScrollHotkey => c.KeyToggleScrollHotkey,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 功能是否生效。**统一入口** —— 调用点一律用它，别直接读配置字段，
        /// 否则总开关就形同虚设。
        ///
        /// 总开关特例：<see cref="BlinkFeature.Mod"/> 自己不受总开关影响。
        /// </summary>
        public static bool IsOn(BlinkFeature f)
        {
            bool own = RawGet(f);
            if (f == BlinkFeature.Mod) return own;

            try { if (!BlinkKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        private static readonly Dictionary<BlinkFeature, bool> _wasDown = new();

        /// <summary>
        /// 每帧调用（放在 IOnHeroUpdate 里）：检查所有已绑定的热键，按下就切换对应功能。
        /// 边沿检测 —— 只在"刚按下"那一帧动作。
        /// </summary>
        public static void PollHotkeys(Action<string>? log)
        {
            foreach (var f in All)
            {
                bool down = false;
                try
                {
                    string name = KeyName(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;      // 默认没绑键 → 跳过
                    int vk = BlinkKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;                              // 只在"刚按下"那一帧动作

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[BackBlinkOverhaul] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        /// <summary>换关 / 卸载时清掉按键状态。</summary>
        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
