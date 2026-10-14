using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BleedAxeOverhaul
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum BleedAxeFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>一次丢出十把斧子。</summary>
        MultiThrow,

        /// <summary>斧子无视墙体。</summary>
        WallPierce,

        /// <summary>斧子自动追击敌人。</summary>
        Homing,

        /// <summary>射程 ×2。</summary>
        Range,

        /// <summary>伤害 ×2。</summary>
        Damage,
    }

    /// <summary>
    /// 功能开关总控：把「开关值 / 热键名 / 菜单显示名」集中在一张表里
    /// （仿 DamageAuraBoost 的 AuraFeatures）。
    ///
    /// 用法（调用点）：
    ///
    ///     if (!BleedAxeFeatures.IsOn(BleedAxeFeature.Homing)) return;
    ///
    /// **不要**直接去读 `BleedAxeConfig.EnableXxx` —— 那样会绕过总开关。
    ///
    /// 总开关语义：
    ///   · `EnableMod = false` → `IsOn()` 对**除 Mod 自己以外**的一切返回 false；
    ///   · 但 Mod 自己始终可切换，否则就没法从游戏里开回来。
    /// </summary>
    public static class BleedAxeFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly BleedAxeFeature[] All =
        {
            BleedAxeFeature.Mod,
            BleedAxeFeature.MultiThrow,
            BleedAxeFeature.WallPierce,
            BleedAxeFeature.Homing,
            BleedAxeFeature.Range,
            BleedAxeFeature.Damage,
        };

        /// <summary>菜单里那一行的标题。</summary>
        public static string Label(BleedAxeFeature f) => f switch
        {
            BleedAxeFeature.Mod => "总开关",
            BleedAxeFeature.MultiThrow => "一次丢出十把斧子",
            BleedAxeFeature.WallPierce => "无视墙体",
            BleedAxeFeature.Homing => "自动追击敌人",
            BleedAxeFeature.Range => "范围 ×2",
            BleedAxeFeature.Damage => "伤害 ×2",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(BleedAxeFeature f) => f switch
        {
            BleedAxeFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            BleedAxeFeature.MultiThrow => "原版一次 1 把，现在一次 10 把扇形散开",
            BleedAxeFeature.WallPierce => "撞墙不再被销毁，可直接穿过墙体 / 单向平台",
            BleedAxeFeature.Homing => "每帧朝最近的敌人限速转向（隔墙也能锁）",
            BleedAxeFeature.Range => "飞行距离 7.26 格 → 14.52 格（重新丢出后生效）",
            BleedAxeFeature.Damage => "命中伤害 ×2（重新丢出后生效）",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(BleedAxeFeature f)
        {
            try
            {
                var c = BleedAxeKeys.Config.Value;
                return f switch
                {
                    BleedAxeFeature.Mod => c.EnableMod,
                    BleedAxeFeature.MultiThrow => c.EnableMultiThrow,
                    BleedAxeFeature.WallPierce => c.EnableWallPierce,
                    BleedAxeFeature.Homing => c.EnableHoming,
                    BleedAxeFeature.Range => c.EnableRange,
                    BleedAxeFeature.Damage => c.EnableDamage,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：写回配置并落盘。</summary>
        public static void Set(BleedAxeFeature f, bool on)
        {
            try
            {
                var c = BleedAxeKeys.Config.Value;
                switch (f)
                {
                    case BleedAxeFeature.Mod: c.EnableMod = on; break;
                    case BleedAxeFeature.MultiThrow: c.EnableMultiThrow = on; break;
                    case BleedAxeFeature.WallPierce: c.EnableWallPierce = on; break;
                    case BleedAxeFeature.Homing: c.EnableHoming = on; break;
                    case BleedAxeFeature.Range: c.EnableRange = on; break;
                    case BleedAxeFeature.Damage: c.EnableDamage = on; break;
                }
                BleedAxeKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(BleedAxeFeature f)
        {
            try
            {
                var c = BleedAxeKeys.Config.Value;
                return f switch
                {
                    BleedAxeFeature.Mod => c.KeyToggleMod,
                    BleedAxeFeature.MultiThrow => c.KeyToggleMultiThrow,
                    BleedAxeFeature.WallPierce => c.KeyToggleWallPierce,
                    BleedAxeFeature.Homing => c.KeyToggleHoming,
                    BleedAxeFeature.Range => c.KeyToggleRange,
                    BleedAxeFeature.Damage => c.KeyToggleDamage,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>扇形总张角（度）→ 弧度。</summary>
        public static double FanTotalRadians()
        {
            double deg = 51.6;
            try
            {
                var c = BleedAxeKeys.Config.Value;
                if (c != null && c.FanTotalDegrees > 0.0 && c.FanTotalDegrees <= 360.0)
                {
                    deg = c.FanTotalDegrees;
                }
            }
            catch { }
            return deg * System.Math.PI / 180.0;
        }

        /// <summary>
        /// 功能是否生效。**统一入口** —— 调用点一律用它，别直接读配置字段。
        /// 总开关特例：`BleedAxeFeature.Mod` 自己不受总开关影响。
        /// </summary>
        public static bool IsOn(BleedAxeFeature f)
        {
            bool own = RawGet(f);
            if (f == BleedAxeFeature.Mod) return own;

            try { if (!BleedAxeKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        /// <summary>每个热键上一次的按下状态（边沿检测，按住不会连发）。</summary>
        private static readonly Dictionary<BleedAxeFeature, bool> _wasDown = new();

        /// <summary>
        /// 每帧调用（放在 IOnHeroUpdate 里）：检查所有已绑定的热键，按下就切换对应功能。
        /// 边沿检测 —— 只在「刚按下」那一帧动作。
        /// </summary>
        public static void PollHotkeys(Action<string> log)
        {
            foreach (var f in All)
            {
                bool down = false;
                try
                {
                    string name = KeyName(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;      // 默认没绑键 → 跳过
                    int vk = BleedAxeKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;                              // 只在「刚按下」那一帧动作

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[BleedAxeOverhaul] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        /// <summary>换关 / 卸载时清掉按键状态，避免「上一关按着、下一关一进来就触发」。</summary>
        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
