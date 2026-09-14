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

                // 表现层：金色法阵 + 一圈飞镖贴图
                ChronoFx.CastShurikenCircle(hero, ShurikenCount, ShurikenRadius);

                // ★ 实体层：真正的投射物（会飞、会打伤害）
                int spawned = SpawnShurikenEntities(hero, ShurikenCount, ShurikenPower);

                Log($"技能一 一周飞镖 已释放（{from}）：实体飞镖 {spawned}/{ShurikenCount} 枚");
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

                var targets = ChronoMobFinder.Pick(hero, 22.0, SwordCount);
                if (targets.Count == 0)
                {
                    targets.Add(((hero.cx + hero.xr) * 24.0 + hero.dir * 120.0,
                                 (hero.cy + hero.yr) * 24.0));
                }

                // 表现层：背景时钟 + 砸下来的剑影
                ChronoFx.CastSwordRain(hero, SwordCount, 22.0, targets);

                // ★ 实体层：真正的落剑（从上方砸下来、会打伤害）
                int spawned = SpawnSwordRainEntities(hero, targets, SwordPower);

                Log($"技能二 时钟剑雨 已释放（{from}）：目标 {targets.Count} 个，实体落剑 {spawned} 把");
            }
            catch (Exception ex)
            {
                Log($"技能二失败: {ex}");
            }
        }

        // ================================================================ 实体层
        //
        // ⚠️ 为什么不用原版时之守护者那两个实体：
        //   · `dc.en.bu.TimeKeeperShuriken` 的发光逻辑要 `be.onions`（boss 专属贴图池）、
        //     取色要 `be._infos`；
        //   · `dc.en.mob.boss.TimeKeeperSword` 的构造函数里就 `be.getOldSkillInfos("swordRain").props.duration`，
        //     update 里还要 `be.brutalityTier` / `be.cy` / `be.get_tmod()`。
        //   两者的构造参数类型都是 `TimeKeeper`，普通关卡里没有 boss 实例 → 传 Hero 必 NPE。
        //
        // 所以改用**玩家可拥有**的同类投射物：构造函数只收泛型 `Entity from`。
        //   · 飞镖 → `dc.en.bu.Saw`（旋转刃，视觉最接近飞镖）
        //   · 落剑 → `dc.en.bu.Stalactite`（从天而降）

        /// <summary>技能一每枚飞镖的基础伤害。</summary>
        private const int ShurikenPower = 20;

        /// <summary>技能二每把落剑的基础伤害。</summary>
        private const int SwordPower = 55;

        /// <summary>技能一一次甩出几枚。</summary>
        public const int ShurikenCount = 12;

        /// <summary>技能一的法阵半径（格）。</summary>
        public const double ShurikenRadius = 9.0;

        /// <summary>技能二一次落几把剑。</summary>
        public const int SwordCount = 6;

        /// <summary>
        /// 造一个"以英雄为施法者"的 AttackData。
        ///
        /// 走原版的统一入口 `_AttackUtils.createFromHero()` —— 它会把 `useHeroScaling` 打开，
        /// 也就是**伤害自动跟英雄的属性 / 等级 / 变异走**，我们只需要给一个基础值，
        /// 不用自己算缩放，也不会漏掉暴击、减抗这些链路。
        /// </summary>
        private static dc.tool.atk.AttackData? MakeHeroAttack(Hero hero, int power)
        {
            try
            {
                object baseDmg = power;             // 原签名是 dynamic
                int? tier = null;                   // null = 用英雄自己的 tier
                // 注意：Haxe 的静态成员在 GameProxy 里挂在"类对象"上，
                // 要写成 `AttackUtils.Class.<方法>`（`_AttackUtils` 那个实例）。
                return dc.tool.atk.AttackUtils.Class.createFromHero(hero, baseDmg, tier);
            }
            catch (Exception ex)
            {
                Log($"构造 AttackData 失败（技能这一下不会造成伤害）: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 技能一的真飞镖：以英雄为中心，向一整圈甩出 `count` 枚 `Saw`。
        ///
        /// 用法照原版陷阱 `dc.en.ltrap.Shooter`：
        ///     `new Saw(this, attackData, ang, 0.5).init();`
        /// 投射物会从施法者（这里是英雄）的位置朝 `ang` 方向飞出去。
        /// </summary>
        private static int SpawnShurikenEntities(Hero hero, int count, int power)
        {
            var atk = MakeHeroAttack(hero, power);
            if (atk == null) return 0;

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    double ang = i * (Math.PI * 2.0) / count;
                    var saw = new dc.en.bu.Saw(hero, atk, ang, 0.5);
                    saw.init();                       // 必须调用，否则不进场
                    spawned++;
                }
                catch (Exception ex)
                {
                    Log($"第 {i + 1} 枚飞镖实体生成失败: {ex.Message}");
                    break;
                }
            }
            return spawned;
        }

        /// <summary>
        /// 技能二的真落剑：在每个目标点上方生成一柄 `Stalactite`。
        ///
        /// 用法照原版 `dc.en.mob.boss.Giant`：
        ///     `var st = new Stalactite(this, attackData); st.init(); st.initOrigin(x, y);`
        /// `initOrigin(x, y)` 指定它从哪个坐标砸下来。
        /// </summary>
        private static int SpawnSwordRainEntities(Hero hero, IList<(double px, double py)> targets, int power)
        {
            var atk = MakeHeroAttack(hero, power);
            if (atk == null) return 0;

            int spawned = 0;
            foreach (var (px, py) in targets)
            {
                try
                {
                    var st = new dc.en.bu.Stalactite(hero, atk);
                    st.init();
                    st.initOrigin(px, py);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Log("落剑实体生成失败: " + ex.Message);
                    break;
                }
            }
            return spawned;
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
