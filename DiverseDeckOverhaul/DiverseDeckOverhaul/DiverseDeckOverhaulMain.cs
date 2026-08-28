using dc;
using dc.en;
using dc.pow;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.Collections.Generic;
using System.IO;

namespace DiverseDeckOverhaul
{
    /// <summary>
    /// 万智牌组（DiverseDeck）强化：
    ///  1. 切牌 CD = 0：通过 data.cdb 将四张牌的 castCD 改为 0（见 patch_deck_cdb.py + Assets res.pak 打包）
    ///  2. DiverseDeckElectro 电球改为五颜六色（彩虹色盘）
    ///  3. 初始召唤 5 颗电球（含历史击杀奖励）
    ///  4. 每击杀 10 个敌人 +1 颗电球（击杀数跨切牌累计，切回电球时结算）
    ///  5. 五圈电球：第一圈10颗、第二圈18颗、第三圈34颗、第四圈58颗、第五圈无上限；
    ///     第一圈半径与原版一致，半径逐圈外扩；每圈电球在该圈内均匀分布，
    ///     已满的圈固定槽位（加球不挤动内圈）；外圈位置每帧按 lastAng 直接重算
    ///  6. 电球伤害提升：电球触伤、闪电连发、感电 DOT 全部翻倍
    ///  7. 启动时加载本模组 res.pak（内含 data.cdb 补丁），使切牌 CD=0 生效
    /// </summary>
    public class DiverseDeckOverhaulMain : ModBase, IOnGameExit, IOnAfterLoadingAssets
    {
        // ===== 可调参数 =====
        private const int START_BALLS = 5;            // 初始电球数
        private const int KILLS_PER_BALL = 10;        // 每击杀 N 个敌人 +1 球
        private const double DAMAGE_MULT = 2.0;       // 电球/闪电/DOT 伤害倍率

        // 前四圈容量：第一圈10、第二圈18、第三圈34、第四圈58（累计 120），第五圈无上限
        private static readonly int[] RING_CAPS = { 10, 18, 34, 58 };
        private const int CAPPED_RINGS_TOTAL = 10 + 18 + 34 + 58; // 120

        // 每圈半径倍数（相对原版 distance=2.5 格）：第一圈与原版一致，逐圈外扩
        private static readonly double[] RING_RADII = { 1.0, 1.6, 2.2, 2.8, 3.4 };

        // 彩虹色盘（五颜六色）
        private static readonly int[] RAINBOW =
        {
            0xFF5A5A, // 红
            0xFFA64D, // 橙
            0xFFE94D, // 黄
            0x5AFF5A, // 绿
            0x4DFFFF, // 青
            0x5A9BFF, // 蓝
            0xA64DFF, // 紫
            0xFF4DFF, // 品红
            0xFFFFFF, // 白
            0xFFB3B3, // 粉
        };

        // 每颗球对应的半径倍数，按球的 __uid（原生稳定 id）索引；initBalls 重建所有球时整体重建
        private static readonly Dictionary<int, double> _ballRing = new();

        // 每个英雄的累计击杀数（按英雄 __uid 索引，跨切牌/跨关卡保留）
        private sealed class HeroState { public int Kills; }
        private static readonly Dictionary<int, HeroState> _heroKills = new();

        // 电球基础伤害值（首次捕获，避免重复翻倍）
        private static double _baseBallPower = -1;
        private static double _baseBoltPower = -1;
        private static double _baseDps = -1;

        public DiverseDeckOverhaulMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_Hero.onMobDeath += OnHeroKillMob;                        // 杀敌计数 → +1 球
            Hook_DiverseDeckElectro.init += OnElectroInit;                // 初始 5 球 + 伤害翻倍
            Hook_DiverseDeckElectro.initBalls += OnElectroInitBalls;      // 内外双圈 + 全圆分布 + 彩虹色
            Hook_DiverseDeckLightningBall.postUpdate += OnBallPostUpdate; // 外圈球保持放大半径
            System.Console.WriteLine("[DiverseDeckOverhaul] 已加载：切牌CD=0(data.cdb) / 电球初始5颗 / 杀10敌+1球 / 五圈(10/18/34/58/∞) / 彩虹色 / 伤害x2");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.onMobDeath -= OnHeroKillMob;
            Hook_DiverseDeckElectro.init -= OnElectroInit;
            Hook_DiverseDeckElectro.initBalls -= OnElectroInitBalls;
            Hook_DiverseDeckLightningBall.postUpdate -= OnBallPostUpdate;
            System.Console.WriteLine("[DiverseDeckOverhaul] 已卸载");
        }

        /// <summary>
        /// 资源加载完成后：把本模组 res.pak（含 data.cdb 补丁：四张牌 castCD=0）挂载进 FsPak，
        /// 游戏的 CDBManager 会在首次关卡生成/重载资源时合并 data.cdb_ 补丁。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(DiverseDeckOverhaulMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    System.Console.WriteLine($"[DiverseDeckOverhaul] res.pak 已加载: {pakPath}");
                }
                else
                {
                    System.Console.WriteLine($"[DiverseDeckOverhaul] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DiverseDeckOverhaul] res.pak 加载失败: {ex.Message}");
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        // ---------- 2) 每击杀 10 个敌人 +1 球 ----------
        private void OnHeroKillMob(Hook_Hero.orig_onMobDeath orig, Hero self, Mob m)
        {
            orig(self, m);
            try
            {
                if (!_heroKills.TryGetValue(self.__uid, out HeroState? st))
                {
                    st = new HeroState();
                    _heroKills[self.__uid] = st;
                }
                st.Kills++;
                if (st.Kills % KILLS_PER_BALL == 0)
                {
                    DiverseDeckElectro? electro = FindElectro(self);
                    if (electro != null)
                    {
                        electro.endLightningCount += 1;
                        electro.initBalls();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DiverseDeckOverhaul] 杀敌加球失败: {ex.Message}");
            }
        }

        private static DiverseDeckElectro? FindElectro(Hero hero)
        {
            try
            {
                if (hero.activeSkillsManager == null) return null;
                dynamic powers = hero.activeSkillsManager.passivePowers;
                if (powers == null) return null;
                int n = (int)powers.length;
                for (int i = 0; i < n; i++)
                {
                    object? raw = powers.getDyn(i);
                    if (raw is DiverseDeckElectro electro) return electro;
                }
            }
            catch { }
            return null;
        }

        // ---------- 3) 初始 5 颗（+ 历史击杀奖励）+ 6) 伤害翻倍 ----------
        private void OnElectroInit(Hook_DiverseDeckElectro.orig_init orig, DiverseDeckElectro self)
        {
            orig(self);
            try
            {
                int bonus = 0;
                if (self.owner is Hero hero && _heroKills.TryGetValue(hero.__uid, out HeroState? st))
                {
                    bonus = st.Kills / KILLS_PER_BALL;
                }
                int target = START_BALLS + bonus;
                if (self.endLightningCount < target)
                {
                    self.endLightningCount = target;
                    self.initBalls();
                }

                ApplyDamageBoost(self);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DiverseDeckOverhaul] 初始球数/伤害设置失败: {ex.Message}");
            }
        }

        private static void ApplyDamageBoost(DiverseDeckElectro self)
        {
            try
            {
                if (self.item == null) return;
                dynamic itemData = self.item._itemData;
                if (itemData == null) return;
                dynamic props = itemData.props;
                if (props == null) return;

                // 电球触伤：props.power 为数组 [8]
                dynamic power = props.power;
                if (power != null)
                {
                    if (_baseBallPower < 0) _baseBallPower = (double)power.getDyn(0);
                    power.setDyn(0, (int)(_baseBallPower * DAMAGE_MULT));
                }

                // 闪电连发：props.power2
                if (_baseBoltPower < 0) _baseBoltPower = (double)props.power2;
                props.power2 = (int)(_baseBoltPower * DAMAGE_MULT);

                // 感电 DOT：props.dps
                if (_baseDps < 0) _baseDps = (double)props.dps;
                props.dps = (int)(_baseDps * DAMAGE_MULT);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DiverseDeckOverhaul] 伤害翻倍失败: {ex.Message}");
            }
        }

        // ---------- 4) 五圈电球：第一圈10、第二圈18、第三圈34、第四圈58、第五圈无上限 ----------
        private void OnElectroInitBalls(Hook_DiverseDeckElectro.orig_initBalls orig, DiverseDeckElectro self)
        {
            orig(self);
            try
            {
                _ballRing.Clear();
                dynamic balls = self.ddLightningBalls;
                if (balls == null) return;
                int n = (int)balls.length;

                for (int i = 0; i < n; i++)
                {
                    object? raw = balls.getDyn(i);
                    if (raw is not DiverseDeckLightningBall ball) continue;

                    int ring;      // 0..3 = 前四圈，4 = 第五圈
                    int ringStart; // 该圈第一颗球的全局下标
                    int ringCount; // 该圈当前球数

                    if (i < CAPPED_RINGS_TOTAL)
                    {
                        int acc = 0;
                        ring = 0;
                        ringStart = 0;
                        ringCount = 0;
                        for (int r = 0; r < RING_CAPS.Length; r++)
                        {
                            acc += RING_CAPS[r];
                            if (i < acc)
                            {
                                ring = r;
                                ringStart = acc - RING_CAPS[r];
                                // 该圈球数 = 本圈容量 与 剩余球数 取小（不满时均匀铺满整圈）
                                ringCount = System.Math.Min(RING_CAPS[r], n - ringStart);
                                break;
                            }
                        }
                    }
                    else
                    {
                        // 第五圈：无上限，容纳 120 之后的全部电球
                        ring = RING_CAPS.Length;
                        ringStart = CAPPED_RINGS_TOTAL;
                        ringCount = n - CAPPED_RINGS_TOTAL;
                    }

                    // 在所在圈内均匀分布；已满的圈固定槽位，加球只影响更外圈（不会挤动内圈）
                    int j = i - ringStart;
                    double ang = (2.0 * (j + 0.5)) / ringCount;
                    double factor = RING_RADII[ring];

                    ball.lastAng = ang;
                    _ballRing[ball.__uid] = factor;

                    // 五颜六色
                    ball.colorize(RAINBOW[i % RAINBOW.Length], null);
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DiverseDeckOverhaul] 五圈/配色失败: {ex.Message}");
            }
        }

        // ---------- 5) 第二~五圈电球每帧按角度直接重算半径（永不与内圈重合） ----------
        private void OnBallPostUpdate(Hook_DiverseDeckLightningBall.orig_postUpdate orig, DiverseDeckLightningBall self)
        {
            orig(self);
            try
            {
                if (!_ballRing.TryGetValue(self.__uid, out double factor) || factor <= 1.001) return;
                if (self.power == null || self.power.owner is not Hero hero) return;

                // 与原版一致：以英雄精灵/格子位置为圆心
                double hx, hy;
                if (hero.spr != null) { hx = hero.spr.x; hy = hero.spr.y; }
                else { hx = (hero.cx + hero.xr) * 24.0; hy = (hero.cy + hero.yr) * 24.0; }

                // 读取原版轨道距离（格 → 像素）
                double baseDist = 2.5;
                double offsetY = 0.0;
                try
                {
                    dynamic? itemData = self.power.item?._itemData;
                    if (itemData != null)
                    {
                        dynamic props = itemData.props;
                        baseDist = (double)props.distance;
                        offsetY = (double)props.offsetY;
                    }
                }
                catch { /* 读取失败用默认值 */ }

                double ang = System.Math.PI * self.lastAng;
                double distPx = baseDist * factor * 24.0;
                double ox = hx + System.Math.Cos(ang) * distPx;
                double oy = hy + System.Math.Sin(ang) * distPx - offsetY * 24.0;
                self.setPosPixel(ox, oy);
            }
            catch
            {
                // 单帧定位失败忽略，下帧重试
            }
        }
    }
}
