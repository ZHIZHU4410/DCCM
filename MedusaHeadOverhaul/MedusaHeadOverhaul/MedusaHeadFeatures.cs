using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MedusaHeadOverhaul
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum MedusaFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>爆炸范围 × N。</summary>
        Radius,

        /// <summary>翻滚距离 × N（默认 1 = 原版）。</summary>
        RollRange,

        /// <summary>石化时长 × N。</summary>
        Petrify,

        /// <summary>Boss 也能被石化。</summary>
        BossPetrify,

        /// <summary>爆炸无视墙体。</summary>
        WallIgnore,
    }

    /// <summary>
    /// 功能开关总控（与 KatanaParry / AdeleScytheOverhaul / TombstonePassive 同一套做法）。
    /// 调用点统一用 <c>MedusaFeatures.IsOn(MedusaFeature.Xxx)</c>，别直接读 Config.EnableXxx。
    /// </summary>
    public static class MedusaFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly MedusaFeature[] All =
        {
            MedusaFeature.Mod,
            MedusaFeature.Radius,
            MedusaFeature.RollRange,
            MedusaFeature.Petrify,
            MedusaFeature.BossPetrify,
            MedusaFeature.WallIgnore,
        };

        public static string Label(MedusaFeature f) => f switch
        {
            MedusaFeature.Mod => "总开关",
            MedusaFeature.Radius => "爆炸范围 ×N",
            MedusaFeature.RollRange => "翻滚距离 ×N",
            MedusaFeature.Petrify => "石化时长 ×N",
            MedusaFeature.BossPetrify => "Boss 也能石化",
            MedusaFeature.WallIgnore => "爆炸无视墙体",
            _ => f.ToString(),
        };

        public static string Hint(MedusaFeature f) => f switch
        {
            MedusaFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            MedusaFeature.Radius => "爆炸半径倍数（原版 5 格，×5 = 25 格）；只影响爆炸，不影响滚多远",
            MedusaFeature.RollRange => "头能滚多远（原版 20 格）；默认 1 = 原版",
            MedusaFeature.Petrify => "石化挂到目标身上的时长倍数（不改武器 CD）",
            MedusaFeature.BossPetrify => "无视目标对石化的抗性（Boss 普遍 100% 免疫）",
            MedusaFeature.WallIgnore => "爆炸后按纯距离补一遍石化：隔着墙的敌人也会被石化",
            _ => "",
        };

        public static bool RawGet(MedusaFeature f)
        {
            try
            {
                var c = MedusaKeys.Config.Value;
                return f switch
                {
                    MedusaFeature.Mod => c.EnableMod,
                    MedusaFeature.Radius => c.EnableRadius,
                    MedusaFeature.RollRange => c.EnableRollRange,
                    MedusaFeature.Petrify => c.EnablePetrify,
                    MedusaFeature.BossPetrify => c.EnableBossPetrify,
                    MedusaFeature.WallIgnore => c.EnableWallIgnore,
                    _ => true,
                };
            }
            catch { return true; }
        }

        public static void Set(MedusaFeature f, bool on)
        {
            try
            {
                var c = MedusaKeys.Config.Value;
                switch (f)
                {
                    case MedusaFeature.Mod: c.EnableMod = on; break;
                    case MedusaFeature.Radius: c.EnableRadius = on; break;
                    case MedusaFeature.RollRange: c.EnableRollRange = on; break;
                    case MedusaFeature.Petrify: c.EnablePetrify = on; break;
                    case MedusaFeature.BossPetrify: c.EnableBossPetrify = on; break;
                    case MedusaFeature.WallIgnore: c.EnableWallIgnore = on; break;
                }
                MedusaKeys.Config.Save();
            }
            catch { }
        }

        public static string KeyName(MedusaFeature f)
        {
            try
            {
                var c = MedusaKeys.Config.Value;
                return f switch
                {
                    MedusaFeature.Mod => c.KeyToggleMod,
                    MedusaFeature.Radius => c.KeyToggleRadius,
                    MedusaFeature.RollRange => c.KeyToggleRollRange,
                    MedusaFeature.Petrify => c.KeyTogglePetrify,
                    MedusaFeature.BossPetrify => c.KeyToggleBossPetrify,
                    MedusaFeature.WallIgnore => c.KeyToggleWallIgnore,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>功能是否生效（总开关对除 Mod 自己以外的一切生效）。</summary>
        public static bool IsOn(MedusaFeature f)
        {
            bool own = RawGet(f);
            if (f == MedusaFeature.Mod) return own;

            try { if (!MedusaKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        private static readonly Dictionary<MedusaFeature, bool> _wasDown = new();

        public static void PollHotkeys(Action<string> log)
        {
            foreach (var f in All)
            {
                bool down = false;
                try
                {
                    string name = KeyName(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;      // 默认没绑键 → 跳过
                    int vk = MedusaKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[MedusaHeadOverhaul] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
