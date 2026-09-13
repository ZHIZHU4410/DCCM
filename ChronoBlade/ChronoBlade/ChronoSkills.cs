using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using dc.en;

namespace ChronoBlade
{
    /// <summary>
    /// 时之刃的两个技能（第 2a / 第 3a）—— 独立于武器本体触发。
    ///
    /// ✅ 按键**不写死**：全部读 <see cref="ChronoKeys.Config"/>（coremod/config/ChronoBlade.json）。
    ///    当前默认 KeySkill1 = "U"、KeySkill2 = "I"，改成任意字母 / 功能键 / 特殊键名都行。
    ///    支持 A~Z、0~9、F1~F12、Space/Tab/Shift/Backslash/RightBracket… 或直接写十六进制 "0x51"。
    ///
    /// 定位：技能一 = 一周飞镖（参考 TimeKeeper levelUpRadius）；
    ///       技能二 = 背景时钟 + 落剑（参考 TimeKeeper swordRain）。
    /// </summary>
    public static class ChronoSkills
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        private static double _cd1;
        private static double _cd2;
        private static bool _k1Down;
        private static bool _k2Down;
        private static bool _hintLogged;

        /// <summary>每帧调用（由 ChronoBladeMain 驱动）。</summary>
        public static void Update(double dt)
        {
            var cfg = ChronoKeys.Config.Value;

            if (_cd1 > 0) _cd1 -= dt;
            if (_cd2 > 0) _cd2 -= dt;

            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero.destroyed || hero._level == null)
            {
                _k1Down = _k2Down = false;
                return;
            }

            if (!cfg.EnableSkills)
            {
                _k1Down = _k2Down = false;
                return;
            }

            double cd = cfg.SkillCooldownS > 0 ? cfg.SkillCooldownS : 0.1;
            int vk1 = ChronoKeys.Resolve(cfg.KeySkill1, 0x55);   // 默认 U
            int vk2 = ChronoKeys.Resolve(cfg.KeySkill2, 0x49);   // 默认 I

            LogHintOnce(cfg.KeySkill1, cfg.KeySkill2);

            bool k1 = (GetAsyncKeyState(vk1) & 0x8000) != 0;
            if (k1 && !_k1Down && _cd1 <= 0)
            {
                _cd1 = cd;
                CastShuriken(hero, $"键{cfg.KeySkill1}");
            }
            _k1Down = k1;

            bool k2 = (GetAsyncKeyState(vk2) & 0x8000) != 0;
            if (k2 && !_k2Down && _cd2 <= 0)
            {
                _cd2 = cd;
                CastSwordRain(hero, $"键{cfg.KeySkill2}");
            }
            _k2Down = k2;
        }

        /// <summary>技能一：一周飞镖。</summary>
        public static void CastShuriken(Hero hero, string from)
        {
            try
            {
                // 先在英雄位置播放释放特效（TIMEZHANJI）
                ChronoFx.PlayCastEffect(hero);

                ChronoFx.CastShurikenCircle(hero, 12, 9.0);
                Log($"技能一 一周飞镖 已释放（{from}）");
            }
            catch (Exception ex)
            {
                Log($"技能一失败: {ex}");
            }
        }

        /// <summary>技能二：背景时钟 + 落剑。</summary>
        public static void CastSwordRain(Hero hero, string from)
        {
            try
            {
                // 先在英雄位置播放释放特效（TIMEZHANJI）
                ChronoFx.PlayCastEffect(hero);

                var targets = ChronoMobFinder.Pick(hero, 22.0, 6);
                if (targets.Count == 0)
                {
                    targets.Add(((hero.cx + hero.xr) * 24.0 + hero.dir * 120.0,
                                 (hero.cy + hero.yr) * 24.0));
                }
                ChronoFx.CastSwordRain(hero, 6, 22.0, targets);
                Log($"技能二 时钟剑雨 已释放（{from}，目标 {targets.Count} 个）");
            }
            catch (Exception ex)
            {
                Log($"技能二失败: {ex}");
            }
        }

        private static void LogHintOnce(string? k1, string? k2)
        {
            if (_hintLogged) return;
            _hintLogged = true;
            Log($"主动技能就绪：技能一 = {k1}（一周飞镖），技能二 = {k2}（时钟剑雨）——" +
                "键位可在 coremod/mods/ChronoBlade/ChronoBlade.json 里改");
        }

        private static void Log(string msg)
        {
            string line = $"[ChronoBlade] {msg}";
            System.Console.WriteLine(line);
            try { _logger?.Information(line); } catch { }
        }

        private static Serilog.ILogger? _logger;

        /// <summary>由主模块注入，让技能日志也写进 logs\log_latest.log。</summary>
        public static void AttachLogger(Serilog.ILogger logger) => _logger = logger;
    }
}
