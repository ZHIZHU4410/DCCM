using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AdeleScytheOverhaul
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum AdeleFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>只保留最后一击（连击序号永远钉在最后一段）。</summary>
        LastHit,

        /// <summary>范围 ×3。</summary>
        Range,

        /// <summary>伤害 ×3。</summary>
        Damage,

        /// <summary>攻速 ×3。</summary>
        AttackSpeed,

        /// <summary>取消攻击后摇：挥完立刻能再挥（攻击间隔 → 0）。</summary>
        NoRecovery,

        /// <summary>无视墙体。</summary>
        WallPierce,

        /// <summary>刀光特效 ×3（纯视觉）。</summary>
        FxScale,
    }

    /// <summary>
    /// 功能开关总控：把"开关值 / 热键名 / 菜单显示名"集中在一张表里（与 DamageAuraBoost 的 AuraFeatures 同构）。
    ///
    /// 用法（调用点）：
    ///
    ///     if (!AdeleFeatures.IsOn(AdeleFeature.WallPierce)) return;
    ///
    /// **不要**直接去读 `AdeleConfig.EnableXxx` —— 那样会绕过总开关。
    ///
    /// 总开关语义：
    ///   · `EnableMod = false` → `IsOn()` 对**除 Mod 自己以外**的一切返回 false；
    ///   · 但 Mod 自己始终可切换（热键 + 菜单复选框都照常），否则就没法从游戏里开回来。
    ///
    /// 热键：每个功能一个 `KeyToggleXxx` 配置，**默认全部留空（不绑键）**；
    ///       空字符串 = 不监听，所以默认不会和游戏本体 / 其它模组撞键。
    /// </summary>
    public static class AdeleFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly AdeleFeature[] All =
        {
            AdeleFeature.Mod,
            AdeleFeature.LastHit,
            AdeleFeature.Range,
            AdeleFeature.Damage,
            AdeleFeature.AttackSpeed,
            AdeleFeature.NoRecovery,
            AdeleFeature.WallPierce,
            AdeleFeature.FxScale,
        };

        /// <summary>菜单里那一行的标题。</summary>
        public static string Label(AdeleFeature f) => f switch
        {
            AdeleFeature.Mod => "总开关",
            AdeleFeature.LastHit => "只保留最后一击",
            AdeleFeature.Range => "范围 ×3",
            AdeleFeature.Damage => "伤害 ×3",
            AdeleFeature.AttackSpeed => "攻速 ×3",
            AdeleFeature.NoRecovery => "取消攻击后摇（间隔→0）",
            AdeleFeature.WallPierce => "无视墙体",
            AdeleFeature.FxScale => "刀光特效（纯视觉）",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(AdeleFeature f) => f switch
        {
            AdeleFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            AdeleFeature.LastHit => "连击永远播第 4 段（最后一击），前三段不再出现",
            AdeleFeature.Range => "最后一击的圆形攻击框直径 ×3（半径 102px → 306px）",
            AdeleFeature.Damage => "最后一击的 power 80 → 240",
            AdeleFeature.AttackSpeed => "最后一击的施展/收招速度 ×3（追判间隔 6 帧 → 2 帧）",
            AdeleFeature.NoRecovery => "把 lockCtrlAfter 0.37s 抹掉 → 挥完立刻能再挥，感受不到攻击间隔",
            AdeleFeature.WallPierce => "命中判定不再检查墙体/视线/门，隔墙也能砍到",
            AdeleFeature.FxScale => "最后一击的刀光放大，并把可见中心拉回人物身上（倍率/位置见下面三条滑条）",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(AdeleFeature f)
        {
            try
            {
                var c = AdeleKeys.Config.Value;
                return f switch
                {
                    AdeleFeature.Mod => c.EnableMod,
                    AdeleFeature.LastHit => c.EnableLastHit,
                    AdeleFeature.Range => c.EnableRange,
                    AdeleFeature.Damage => c.EnableDamage,
                    AdeleFeature.AttackSpeed => c.EnableAttackSpeed,
                    AdeleFeature.NoRecovery => c.EnableNoRecovery,
                    AdeleFeature.WallPierce => c.EnableWallPierce,
                    AdeleFeature.FxScale => c.EnableFxScale,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：写回配置并落盘。</summary>
        public static void Set(AdeleFeature f, bool on)
        {
            try
            {
                var c = AdeleKeys.Config.Value;
                switch (f)
                {
                    case AdeleFeature.Mod: c.EnableMod = on; break;
                    case AdeleFeature.LastHit: c.EnableLastHit = on; break;
                    case AdeleFeature.Range: c.EnableRange = on; break;
                    case AdeleFeature.Damage: c.EnableDamage = on; break;
                    case AdeleFeature.AttackSpeed: c.EnableAttackSpeed = on; break;
                    case AdeleFeature.NoRecovery: c.EnableNoRecovery = on; break;
                    case AdeleFeature.WallPierce: c.EnableWallPierce = on; break;
                    case AdeleFeature.FxScale: c.EnableFxScale = on; break;
                }
                AdeleKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(AdeleFeature f)
        {
            try
            {
                var c = AdeleKeys.Config.Value;
                return f switch
                {
                    AdeleFeature.Mod => c.KeyToggleMod,
                    AdeleFeature.LastHit => c.KeyToggleLastHit,
                    AdeleFeature.Range => c.KeyToggleRange,
                    AdeleFeature.Damage => c.KeyToggleDamage,
                    AdeleFeature.AttackSpeed => c.KeyToggleAttackSpeed,
                    AdeleFeature.NoRecovery => c.KeyToggleNoRecovery,
                    AdeleFeature.WallPierce => c.KeyToggleWallPierce,
                    AdeleFeature.FxScale => c.KeyToggleFxScale,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 功能是否生效。**统一入口** —— 调用点一律用它，别直接读配置字段，
        /// 否则总开关就形同虚设。
        ///
        /// 总开关特例：`AdeleFeature.Mod` 自己不受总开关影响（否则关掉就再也开不回来了）。
        /// </summary>
        public static bool IsOn(AdeleFeature f)
        {
            bool own = RawGet(f);
            if (f == AdeleFeature.Mod) return own;

            try { if (!AdeleKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        /// <summary>每个热键上一次的按下状态（边沿检测，按住不会连发）。</summary>
        private static readonly Dictionary<AdeleFeature, bool> _wasDown = new();

        /// <summary>
        /// 每帧调用（放在 IOnHeroUpdate 里）：检查所有已绑定的热键，按下就切换对应功能。
        /// 边沿检测 —— 只在"刚按下"那一帧动作。
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
                    int vk = AdeleKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;                              // 只在"刚按下"那一帧动作

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[AdeleScytheOverhaul] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        /// <summary>换关 / 卸载时清掉按键状态，避免"上一关按着、下一关一进来就触发"。</summary>
        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
