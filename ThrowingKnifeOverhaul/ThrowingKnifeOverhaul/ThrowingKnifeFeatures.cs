using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ThrowingKnifeOverhaul
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum KnifeFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>飞刀无视墙体。</summary>
        WallPierce,

        /// <summary>攻速 ×N。</summary>
        AttackSpeed,

        /// <summary>锁定追击敌人。</summary>
        Homing,

        /// <summary>追击射程 ×N。</summary>
        HomingRange,

        /// <summary>无限飞刀（弹药不减）。</summary>
        InfiniteAmmo,
    }

    /// <summary>
    /// 功能开关总控：把"开关值 / 热键名 / 菜单显示名 / 说明"集中在一张表里
    /// （与 DamageAuraBoost 的 AuraFeatures、AdeleScytheOverhaul 的 AdeleFeatures 同构）。
    ///
    /// 用法（调用点）：
    ///
    ///     if (!KnifeFeatures.IsOn(KnifeFeature.Homing)) return;
    ///
    /// **不要**直接去读 `KnifeConfig.EnableXxx` —— 那样会绕过总开关。
    ///
    /// 总开关语义：
    ///   · `EnableMod = false` → `IsOn()` 对**除 Mod 自己以外**的一切返回 false；
    ///   · 但 Mod 自己始终可切换（热键 + 菜单复选框都照常），否则就没法从游戏里开回来。
    /// </summary>
    public static class KnifeFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly KnifeFeature[] All =
        {
            KnifeFeature.Mod,
            KnifeFeature.WallPierce,
            KnifeFeature.AttackSpeed,
            KnifeFeature.Homing,
            KnifeFeature.HomingRange,
            KnifeFeature.InfiniteAmmo,
        };

        /// <summary>菜单里那一行的标题。</summary>
        public static string Label(KnifeFeature f) => f switch
        {
            KnifeFeature.Mod => "总开关",
            KnifeFeature.WallPierce => "飞刀无视墙体",
            KnifeFeature.AttackSpeed => "攻速 ×3",
            KnifeFeature.Homing => "锁定追击敌人",
            KnifeFeature.HomingRange => "追击射程 ×2.5",
            KnifeFeature.InfiniteAmmo => "无限飞刀",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(KnifeFeature f) => f switch
        {
            KnifeFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            KnifeFeature.WallPierce => "ignoreWalls/ignoreOneWays + 跳过撞墙处理，飞刀直穿地形",
            KnifeFeature.AttackSpeed => "出招动画 ×3、武器冷却 0.25s ÷3（倍率见下面滑条）",
            KnifeFeature.Homing => "每帧朝最近的怪限速转向，隔墙也能锁",
            KnifeFeature.HomingRange => "原版只飞 5.5 格，放大后才追得到人（倍率见滑条）",
            KnifeFeature.InfiniteAmmo => "投掷前把弹药补满，否则 8 把刀配 ×3 攻速一瞬打空",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(KnifeFeature f)
        {
            try
            {
                var c = KnifeKeys.Config.Value;
                return f switch
                {
                    KnifeFeature.Mod => c.EnableMod,
                    KnifeFeature.WallPierce => c.EnableWallPierce,
                    KnifeFeature.AttackSpeed => c.EnableAttackSpeed,
                    KnifeFeature.Homing => c.EnableHoming,
                    KnifeFeature.HomingRange => c.EnableHomingRange,
                    KnifeFeature.InfiniteAmmo => c.EnableInfiniteAmmo,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：写回配置并落盘。</summary>
        public static void Set(KnifeFeature f, bool on)
        {
            try
            {
                var c = KnifeKeys.Config.Value;
                switch (f)
                {
                    case KnifeFeature.Mod: c.EnableMod = on; break;
                    case KnifeFeature.WallPierce: c.EnableWallPierce = on; break;
                    case KnifeFeature.AttackSpeed: c.EnableAttackSpeed = on; break;
                    case KnifeFeature.Homing: c.EnableHoming = on; break;
                    case KnifeFeature.HomingRange: c.EnableHomingRange = on; break;
                    case KnifeFeature.InfiniteAmmo: c.EnableInfiniteAmmo = on; break;
                }
                KnifeKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(KnifeFeature f)
        {
            try
            {
                var c = KnifeKeys.Config.Value;
                return f switch
                {
                    KnifeFeature.Mod => c.KeyToggleMod,
                    KnifeFeature.WallPierce => c.KeyToggleWallPierce,
                    KnifeFeature.AttackSpeed => c.KeyToggleAttackSpeed,
                    KnifeFeature.Homing => c.KeyToggleHoming,
                    KnifeFeature.HomingRange => c.KeyToggleHomingRange,
                    KnifeFeature.InfiniteAmmo => c.KeyToggleInfiniteAmmo,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 功能是否生效。**统一入口** —— 调用点一律用它，别直接读配置字段，
        /// 否则总开关就形同虚设。
        ///
        /// 总开关特例：`KnifeFeature.Mod` 自己不受总开关影响（否则关掉就再也开不回来了）。
        /// </summary>
        public static bool IsOn(KnifeFeature f)
        {
            bool own = RawGet(f);
            if (f == KnifeFeature.Mod) return own;

            try { if (!KnifeKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        /// <summary>每个热键上一次的按下状态（边沿检测，按住不会连发）。</summary>
        private static readonly Dictionary<KnifeFeature, bool> _wasDown = new();

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
                    int vk = KnifeKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;                              // 只在"刚按下"那一帧动作

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[ThrowingKnifeOverhaul] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        /// <summary>换关 / 卸载时清掉按键状态，避免"上一关按着、下一关一进来就触发"。</summary>
        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
