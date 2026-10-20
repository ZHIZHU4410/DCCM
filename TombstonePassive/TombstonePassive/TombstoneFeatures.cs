using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TombstonePassive
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum DoomFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>被动灾厄：每移动一格触发一次。</summary>
        Passive,

        /// <summary>灾厄伤害不递减（绕过 getDiminishingFactor 的"第 3 次归零"）。</summary>
        FullDamage,

        /// <summary>英雄脚下也落一块墓碑（纯视觉）。</summary>
        HeroTombstone,
    }

    /// <summary>
    /// 功能开关总控：把"开关值 / 热键名 / 菜单显示名 / 说明"集中在一张表里
    /// （与 DamageAuraBoost 的 AuraFeatures 同构）。
    ///
    /// 调用点统一用 <c>DoomFeatures.IsOn(DoomFeature.Xxx)</c>，别直接读 `DoomConfig.EnableXxx`，
    /// 否则总开关就形同虚设。
    /// </summary>
    public static class DoomFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly DoomFeature[] All =
        {
            DoomFeature.Mod,
            DoomFeature.Passive,
            DoomFeature.FullDamage,
            DoomFeature.HeroTombstone,
        };

        /// <summary>菜单里那一行的标题。</summary>
        public static string Label(DoomFeature f) => f switch
        {
            DoomFeature.Mod => "总开关",
            DoomFeature.Passive => "被动灾厄（每移动一格）",
            DoomFeature.FullDamage => "灾厄伤害不递减",
            DoomFeature.HeroTombstone => "英雄脚下也落墓碑",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(DoomFeature f) => f switch
        {
            DoomFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            DoomFeature.Passive => "带着墓碑走路，每跨一格以英雄为中心来一次灾厄（无需攻击）",
            DoomFeature.FullDamage => "原版 5 秒内第 3 次起伤害归零；打开后按「灾厄伤害系数」给伤害",
            DoomFeature.HeroTombstone => "纯视觉：英雄位置也砸一块墓碑，走路会刷得比较密",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(DoomFeature f)
        {
            try
            {
                var c = DoomKeys.Config.Value;
                return f switch
                {
                    DoomFeature.Mod => c.EnableMod,
                    DoomFeature.Passive => c.EnablePassive,
                    DoomFeature.FullDamage => c.EnableFullDamage,
                    DoomFeature.HeroTombstone => c.EnableHeroTombstone,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：写回配置并落盘。</summary>
        public static void Set(DoomFeature f, bool on)
        {
            try
            {
                var c = DoomKeys.Config.Value;
                switch (f)
                {
                    case DoomFeature.Mod: c.EnableMod = on; break;
                    case DoomFeature.Passive: c.EnablePassive = on; break;
                    case DoomFeature.FullDamage: c.EnableFullDamage = on; break;
                    case DoomFeature.HeroTombstone: c.EnableHeroTombstone = on; break;
                }
                DoomKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(DoomFeature f)
        {
            try
            {
                var c = DoomKeys.Config.Value;
                return f switch
                {
                    DoomFeature.Mod => c.KeyToggleMod,
                    DoomFeature.Passive => c.KeyTogglePassive,
                    DoomFeature.FullDamage => c.KeyToggleFullDamage,
                    DoomFeature.HeroTombstone => c.KeyToggleHeroTombstone,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 功能是否生效。**统一入口**。
        /// 总开关特例：`DoomFeature.Mod` 自己不受总开关影响（否则关掉就再也开不回来了）。
        /// </summary>
        public static bool IsOn(DoomFeature f)
        {
            bool own = RawGet(f);
            if (f == DoomFeature.Mod) return own;

            try { if (!DoomKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        private static readonly Dictionary<DoomFeature, bool> _wasDown = new();

        /// <summary>每帧调用：检查已绑定的热键，按下（边沿触发）就切换对应功能。</summary>
        public static void PollHotkeys(Action<string> log)
        {
            foreach (var f in All)
            {
                bool down = false;
                try
                {
                    string name = KeyName(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;      // 默认没绑键 → 跳过
                    int vk = DoomKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[TombstonePassive] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        /// <summary>换关 / 卸载时清掉按键状态。</summary>
        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
