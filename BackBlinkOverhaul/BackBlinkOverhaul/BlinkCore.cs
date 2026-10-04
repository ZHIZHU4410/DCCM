#nullable disable

using dc;
using dc.en;
using dc.en.mob.boss;
using dc.en.mob;
using dc.haxe.ds;
using dc.hl.types;
using dc.hxd;
using dc.hxd.res;
using dc.tool;
using dc.tool.atk;
using HashlinkNET.Native.Impl;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using ModCore.Utilities;
using System;
using System.Collections.Generic;

namespace BackBlinkOverhaul
{
    using Mob = dc.en.Mob;
    using LevelMap = dc.level.LevelMap;
    using Math = System.Math;

    /// <summary>一次传送尝试的结果，用于后续的视觉 / 声音 / 冲击波 / 连锁结算。</summary>
    internal sealed class BlinkCast
    {
        /// <summary>施法者。</summary>
        public Hero Hero;

        /// <summary>使用的物品（可能为 null）。</summary>
        public InventItem Item;

        /// <summary>本次传送希望的落点（世界像素坐标）。</summary>
        public double TargetX, TargetY;

        /// <summary>本次传送的实际起点（世界像素坐标）。</summary>
        public double FromX, FromY;

        /// <summary>实际落点（世界像素坐标）。</summary>
        public double LandX, LandY;

        /// <summary>锁定的敌人（可能为 null = 盲闪）。</summary>
        public Mob Target;

        /// <summary>是否发生了穿墙（起点与落点之间没有无墙通路）。</summary>
        public bool PiercedWall;

        /// <summary>传送距离（格）。</summary>
        public double DistanceCells;

        /// <summary>连锁层数（结算时的快照）。</summary>
        public int ChainStacks;
    }

    /// <summary>
    /// BackBlink 改造的核心逻辑（与 <see cref="BackBlinkOverhaulMain"/> 分离，便于阅读）。
    ///
    /// 对外暴露的静态方法：
    ///   · <see cref="FindNearestEnemy"/>—— 锁敌（含无视距离）
    ///   · <see cref="SafeLanding"/>      —— 落点判定与安全位置修正
    ///   · <see cref="PlayCastFx"/>       —— 拖尾 / 残影 / 慢动作 / 音效 / 震屏
    ///   · <see cref="Shockwave"/>        —— 落地冲击波 Blink Strike
    ///   · <see cref="ShowChainHud"/>     —— 连锁状态提示
    /// </summary>
    internal static class BlinkCore
    {
        // ---------------------------------------------------------------- 常量

        /// <summary>一格 = 24 世界像素。</summary>
        public const double Cell = 24.0;

        /// <summary>BackBlink 物品 id。</summary>
        public const string ItemId = "BackBlink";

        /// <summary>落点最多向外搜索的格数。</summary>
        public const int LandingSearchRadius = 6;

        /// <summary>冲击波最多结算的敌人数（性能保护）。</summary>
        public const int MaxShockwaveTargets = 64;

        public const int FallbackTrailColor = 0x8A2BE2;   // 蓝紫
        public const int FallbackWaveColor = 0x8A2BE2;
        public const int ChainWaveColor = 0xFFD24A;       // 连锁用金色

        /// <summary>当前游戏时间（秒）。由主模组每帧刷新。</summary>
        public static double GameTime;

        // ---------------------------------------------------------------- 小工具

        public static dc.String Hx(string s) => StringUtils.AsHaxeString(s);

        /// <summary>把 "#RRGGBB" / "#AARRGGBB" / "0xRRGGBB" 解析成颜色整数。</summary>
        public static int ColorOf(string hex, int fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                string s = hex.Trim();
                if (s.StartsWith("#")) s = s.Substring(1);
                else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
                if (s.Length == 6) s = "FF" + s;
                if (s.Length != 8) return fallback;
                return Convert.ToInt32(s, 16);
            }
            catch { return fallback; }
        }

        public static int TrailColor() => ColorOf(BlinkKeys.Config.Value.TrailColor, FallbackTrailColor);
        public static int WaveColor() => ColorOf(BlinkKeys.Config.Value.ShockwaveColor, FallbackWaveColor);
        public static int AfterImageColor() => ColorOf(BlinkKeys.Config.Value.AfterImageColor, 0xFFFFFF);

        /// <summary>实体中心的像素 X 坐标。</summary>
        public static double PixelX(Entity e) => ((double)e.cx + e.xr) * Cell;

        /// <summary>实体中心（半高位置）的像素 Y 坐标。</summary>
        public static double PixelY(Entity e) => ((double)e.cy + e.yr) * Cell - e.hei * 0.5;

        /// <summary>
        /// 原版的"距离"算法（与 _BackBlink / DecisionHelper 里完全一致）：
        /// 以两实体中心为基准，Y 方向按高度修正 1/48。
        /// </summary>
        public static double DistanceCells(Entity a, Entity b)
        {
            const double k = 1.0 / 48.0;
            double dx = ((double)a.cx + a.xr) - ((double)b.cx + b.xr);
            double ay = (double)a.cy + a.yr - k * a.hei;
            double by = (double)b.cy + b.yr - k * b.hei;
            double dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>这个敌人能不能被我们锁定。</summary>
        public static bool IsValidTarget(Hero hero, Mob m)
        {
            try
            {
                if (m == null || hero == null) return false;
                if (m.destroyed) return false;
                if (m._level == null || hero._level == null) return false;
                if (m._level != hero._level) return false;
                if (!m._targetable) return false;
                if (!m.canBeDetected()) return false;
                if (!hero.isOpponent(m)) return false;
                // 距离 / 视线之外的过滤交给调用方；
                // 门 / 门座 / 装饰物这类"非战斗目标"Mob，_targetable 与 canBeDetected 已经能挡掉
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 1.1.2 / 1.1.3 锁敌：找最近的敌人。
        /// <paramref name="useInfiniteRange"/> = true 时无视原版的"近距离"限制（整关搜索），
        /// 否则退化为原版语义（限制在原版 props.distance 范围内）。
        /// </summary>
        public static Mob FindNearestEnemy(Hero hero, InventItem item, bool useInfiniteRange, out double distanceCells)
        {
            distanceCells = 0.0;
            try
            {
                if (hero == null || hero._level == null) return null;

                double limit = 0.0;
                if (!useInfiniteRange)
                {
                    limit = ItemPropDouble(item, "distance", 11.0);
                }

                Mob best = null;
                double bestDist = double.MaxValue;

                ArrayObj list = hero._level.entities;
                if (list == null) return null;

                int n = list.length;
                for (int i = 0; i < n; i++)
                {
                    if (i >= list.length) break;
                    var e = list.getDyn(i) as Entity;
                    if (e == null || e.destroyed) continue;
                    if (!(e is Mob m)) continue;
                    if (!IsValidTarget(hero, m)) continue;

                    double d = DistanceCells(hero, m);
                    if (!useInfiniteRange && d > limit) continue;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = m;
                    }
                }

                if (best != null) distanceCells = bestDist;
                return best;
            }
            catch { return null; }
        }

        /// <summary>从物品数据里安全地读一个 double 属性。</summary>
        public static double ItemPropDouble(InventItem item, string key, double fallback)
        {
            try
            {
                if (item == null) return fallback;
                dynamic data = item._itemData;
                if (data == null) return fallback;
                dynamic props = data.props;
                if (props == null) return fallback;
                object v = props.getDyn(Hx(key));
                if (v == null) return fallback;
                return Convert.ToDouble(v);
            }
            catch { return fallback; }
        }

        /// <summary>从物品数据里安全地读第一个 power 值。</summary>
        public static double ItemPower(InventItem item, double fallback)
        {
            try
            {
                if (item == null) return fallback;
                dynamic data = item._itemData;
                if (data == null) return fallback;
                dynamic props = data.props;
                if (props == null) return fallback;
                dynamic arr = props.power;
                if (arr == null || arr.length <= 0) return fallback;
                return Convert.ToDouble(arr.getDyn(0));
            }
            catch { return fallback; }
        }

        // ---------------------------------------------------------------- 1.1.4 落点判定与安全位置修正

        /// <summary>
        /// 这一格是不是"墙"（直接读地图碰撞位图，不依赖实体查询）。
        /// bit 0 = 实心墙。这是最可靠的"卡在墙里"判据。
        /// </summary>
        public static bool IsWall(LevelMap map, int x, int y)
        {
            try
            {
                if (map == null) return true;
                if (x < 0 || y < 0 || x >= map.wid || y >= map.hei) return true;
                var cols = map.collisions;
                if (cols == null) return false;
                int idx = y * map.wid + x;
                if (idx < 0 || idx >= cols.length) return true;
                int v = cols.getDyn(idx);
                return (v & 1) != 0;
            }
            catch { return false; }
        }

        /// <summary>实体中心当前是否卡在墙里。</summary>
        public static bool IsStuck(Entity e)
        {
            try
            {
                if (e == null || e._level == null) return false;
                return IsWall(e._level.map, e.cx, e.cy);
            }
            catch { return false; }
        }

        /// <summary>这一格是不是空的（不撞墙）。</summary>
        public static bool IsFree(Entity e, LevelMap map, int x, int y)
        {
            try
            {
                if (e == null || map == null) return false;
                if (x < 0 || y < 0 || x >= map.wid || y >= map.hei) return false;
                if (IsWall(map, x, y)) return false;
                return e.isPositionEmpty(x, y);
            }
            catch { return false; }
        }

        /// <summary>
        /// 1.1.4 安全落点搜索：从 (cx, cy) 起由近到远找第一个空位。
        /// 找不到时返回 false（= 传送失败，1.1.5 会按失败处理）。
        /// </summary>
        public static bool FindSafeCell(Entity e, LevelMap map, int cx, int cy, out int ox, out int oy, int maxRadius = LandingSearchRadius)
        {
            ox = cx; oy = cy;
            if (map == null) return false;

            if (IsFree(e, map, cx, cy)) return true;

            for (int r = 1; r <= maxRadius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        // 只看"环"上，避免重复检查内部
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int x = cx + dx;
                        int y = cy + dy;
                        if (!IsFree(e, map, x, y)) continue;
                        ox = x; oy = y;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 落点修正：如果落点被堵，把角色挪到最近的可站立格。
        /// 返回 true 表示做过修正。带上 ignoreColl = true 保证穿墙语义。
        /// </summary>
        public static bool SafeLanding(Hero hero, int wantCx, int wantCy, bool fixEnabled = true)
        {
            try
            {
                if (hero == null || hero._level == null) return false;
                var map = hero._level.map;
                if (map == null) return false;

                // 只要没卡在墙格里就认为落点可接受（不卡墙 = 至少能站住）
                if (!IsWall(map, hero.cx, hero.cy)) return false;

                // 关掉落点修正时只尝试"落点那一格"，不做大范围搜索
                int radius = fixEnabled ? LandingSearchRadius : 1;

                int tx, ty;
                if (!FindSafeCell(hero, map, wantCx, wantCy, out tx, out ty, radius))
                {
                    if (!FindSafeCell(hero, map, hero.cx, hero.cy, out tx, out ty, radius)) return false;
                }

                if (tx == hero.cx && ty == hero.cy) return false;

                double xr = (Math.Abs(hero.xr) > 0.6) ? hero.xr : 0.5;
                hero.safeTpTo(tx, ty, xr, hero.yr, true);
                return true;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- 1.3 视觉表现 / 1.4 音效与反馈

        /// <summary>1.3.1 拖尾 + 1.3.3 残影与起点终点特效 + 1.4 音效与震屏。</summary>
        public static void PlayCastFx(BlinkCast c)
        {
            try
            {
                var cfg = BlinkKeys.Config.Value;
                var hero = c.Hero;
                if (hero == null || hero._level == null) return;
                var fx = hero._level.fx;
                if (fx == null) return;

                int trailCol = TrailColor();

                // ---- 1.3.1 拖尾：起点 → 落点的一条发光带 ----
                if (BlinkFeatures.IsOn(BlinkFeature.Trail))
                {
                    try
                    {
                        fx.tailLineFree(c.FromX, c.FromY, c.LandX, c.LandY,
                                        trailCol, cfg.TrailAlpha, cfg.TrailThickness,
                                        (double?)1.0);
                    }
                    catch { }
                }

                // ---- 1.3.4 速度感：原版 shadowStep ----
                try { fx.shadowStep(c.FromX, c.FromY, c.LandX, c.LandY, trailCol); } catch { }

                if (BlinkFeatures.IsOn(BlinkFeature.AfterImage))
                {
                    // 起点：烟雾
                    TrySmoke(fx, c.FromX, c.FromY, 40.0, trailCol);
                    // 起点 → 落点的传送光柱
                    TryEntityTeleport(fx, c.FromX, c.FromY, c.LandX, c.LandY, AfterImageColor());
                    // 残影
                    TryOnionSkin(hero, cfg);
                    // 落地烟尘
                    try { fx.landSmoke(c.LandX, c.LandY, (int?)10); } catch { }
                }

                if (BlinkFeatures.IsOn(BlinkFeature.SpeedFeel))
                {
                    try { Boot.Class.ME.slowMo(cfg.SlowMoDurationS, cfg.SlowMoScale, null, null); } catch { }
                }

                if (BlinkFeatures.IsOn(BlinkFeature.Feedback))
                {
                    PlaySfx(hero, cfg.TeleportSfx, cfg.SfxVolume);
                    Shake(hero, cfg.ShakePower, cfg.ShakeDurationS);
                }
            }
            catch { }
        }

        /// <summary>2.4 / 3.5 冲击波与连锁的视觉。</summary>
        public static void PlayWaveFx(Hero hero, double x, double y, double radiusPx, bool chain)
        {
            try
            {
                if (hero == null || hero._level == null) return;
                var cfg = BlinkKeys.Config.Value;
                var fx = hero._level.fx;
                if (fx == null) return;

                int col = chain ? ChainWaveColor : WaveColor();

                // 2.4.1 冲击波特效
                try { fx.shockwave(x, y, radiusPx); } catch { }
                // 2.4.2 地面裂痕 / 光环
                try { fx.groundStones(x, y, (int?)col, (double?)(radiusPx / Cell / 3.0)); } catch { }
                try { fx.impact(x, y, (int)radiusPx, col, 1.0); } catch { }
                try { fx.smokeBomb(x, y, radiusPx, col, null, (double?)0.7); } catch { }

                // 2.4.3 / 2.4.4 命中反馈 + 屏幕震动
                if (BlinkFeatures.IsOn(BlinkFeature.Feedback))
                {
                    try { fx.landHeavy(x, y, (int?)col, (int?)null); } catch { }
                    Shake(hero, cfg.ShakePower * 1.6, cfg.ShakeDurationS * 1.6);
                }
            }
            catch { }
        }

        /// <summary>1.4.2 落地音效。</summary>
        public static void PlayLandingSfx(Hero hero)
        {
            try
            {
                if (!BlinkFeatures.IsOn(BlinkFeature.Feedback)) return;
                var cfg = BlinkKeys.Config.Value;
                PlaySfx(hero, cfg.LandingSfx, cfg.SfxVolume);
            }
            catch { }
        }

        /// <summary>播放一个 res 内的音效（跟随实体）。</summary>
        public static void PlaySfx(Hero hero, string path, double volume)
        {
            try
            {
                if (hero == null || hero._level == null) return;
                if (string.IsNullOrWhiteSpace(path)) return;

                var lAudio = hero._level.lAudio;
                if (lAudio == null) return;

                var loader = dc.hxd.Res.Class.get_loader();
                if (loader == null) return;
                if (loader == null) return;

                var pathHx = Hx(path);
                if (!loader.exists(pathHx)) return;

                var res = loader.loadCache(pathHx, Sound.Class);
                var snd = res as Sound;
                if (snd == null) return;

                lAudio.playEventOn(snd, hero, (double?)volume, (double?)1.0, null);
            }
            catch { }
        }

        /// <summary>3.5.2 连锁重置特效：在角色脚下打一圈金色光环（不是一个完整的传送特效）。</summary>
        public static void PlayChainFx(Hero hero, int stacks)
        {
            try
            {
                if (hero == null || hero._level == null) return;
                var fx = hero._level.fx;
                if (fx == null) return;

                double x = PixelX(hero);
                double y = PixelY(hero);
                double r = 34.0 + Math.Min(6, stacks) * 4.0;

                try { fx.impact(x, y, (int)r, ChainWaveColor, 0.9); } catch { }
                try { fx.smokeBomb(x, y, r, ChainWaveColor, null, (double?)0.5); } catch { }
                try { fx.groundStones(x, y, (int?)ChainWaveColor, (double?)0.6); } catch { }
            }
            catch { }
        }

        /// <summary>1.4.3 屏幕震动。</summary>
        public static void Shake(Hero hero, double power, double durationS)
        {
            try
            {
                if (hero == null || hero._level == null) return;
                if (power <= 0.0 || durationS <= 0.0) return;
                var vp = hero._level.viewport;
                if (vp == null) return;
                vp.shakeS(power, power * 0.35, durationS);
            }
            catch { }
        }

        private static void TrySmoke(Fx fx, double x, double y, double r, int col)
        {
            try { fx.smokeBomb(x, y, r, col, null, (double?)0.6); } catch { }
        }

        private static void TryEntityTeleport(Fx fx, double fromX, double fromY, double toX, double toY, int col)
        {
            try
            {
                bool botLayer = false;
                bool circleFx = true;
                bool opaque = true;
                fx.entityTeleport(fromX, fromY - 20.0, toX, toY - 20.0, col, ref botLayer, ref circleFx, ref opaque);
            }
            catch { }
        }

        private static void TryOnionSkin(Hero hero, BlinkConfig cfg)
        {
            try
            {
#pragma warning disable CS0612
                var onion = OnionSkin.Class.fromEntity(
                    hero, null, (int?)AfterImageColor(),
                    Ref<double>.In(0.85), Ref<double>.In(cfg.AfterImageDurationS),
                    Ref<bool>.Null, Ref<bool>.Null, Ref<double>.Null);
#pragma warning restore CS0612
                if (onion == null) return;
                onion.offset(-hero.dir * 6.0, 0.0);
                onion.frict = 0.87;
            }
            catch { }
        }

        // ---------------------------------------------------------------- 2. 落地冲击波 Blink Strike

        /// <summary>
        /// 2. 落地冲击波 Blink Strike：以落点为圆心的范围伤害。
        ///
        /// 2.1.3"每个敌人单次传送只结算一次"：一次传送只调用一次本方法，内部再用
        /// HashSet 按实体对象去重。
        ///
        /// <paramref name="markForChain"/> 会在**造成伤害之前**对每个命中的敌人调用，
        /// 这样这些敌人被打死时就能被 3. 连锁传送识别为"本次传送造成的击杀"。
        ///
        /// <paramref name="forceIgnoreShield"/> = true 时给每发冲击波补挂
        /// <c>IgnoreGlobalShield</c> 词条（走原版 <c>AttackData.addAffix</c>，
        /// 与 KingsSpear / 岩浆同一条链路：<c>AttackTargetImpl</c> 里
        /// <c>hasTag(8) || hasAffix("IgnoreGlobalShield")</c> 才跳过全局护盾格挡）。
        ///
        /// 返回本次冲击波造成的击杀数。
        /// </summary>
        public static int Shockwave(BlinkCast c, List<Mob> outKilled, Action<Mob> markForChain = null,
                                    bool forceIgnoreShield = true)
        {
            int kills = 0;
            try
            {
                var cfg = BlinkKeys.Config.Value;
                var hero = c.Hero;
                if (hero == null || hero._level == null) return 0;

                double radiusCells = Math.Max(0.5, cfg.ShockwaveRadius);
                double radiusPx = radiusCells * Cell;

                // ---- 2.2 伤害计算 ----
                double basePower = ItemPower(c.Item, 70.0);
                double dmg = basePower * Math.Max(0.0, cfg.ShockwaveDamageMult);

                // 2.2.2 基于暴虐属性缩放 / 2.2.3 基于战术属性缩放
                double brutal = Math.Max(0, hero.brutalityTier) * Math.Max(0.0, cfg.BrutalityScaling);
                double tactic = Math.Max(0, hero.tacticTier) * Math.Max(0.0, cfg.TacticScaling);
                dmg *= (1.0 + brutal + tactic);

                // 3.4.2 连续击杀奖励 / 3.4.4 连锁上限与衰减
                dmg *= ChainDamageMult(c.ChainStacks, cfg);

                if (dmg <= 0.0) return 0;

                PlayWaveFx(hero, c.LandX, c.LandY, radiusPx, c.ChainStacks > 0);
                PlayLandingSfx(hero);

                // ---- 2.1.2 以落点为圆心找敌人 ----
                var hitSet = new HashSet<Mob>();
                ArrayObj list = hero._level.entities;
                if (list == null) return 0;

                int n = list.length;
                int processed = 0;
                for (int i = 0; i < n && processed < MaxShockwaveTargets; i++)
                {
                    if (i >= list.length) break;
                    var e = list.getDyn(i) as Entity;
                    if (e == null || e.destroyed) continue;
                    if (!(e is Mob m)) continue;
                    if (!IsValidTarget(hero, m)) continue;

                    double dx = PixelX(m) - c.LandX;
                    double dy = PixelY(m) - c.LandY;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    // 2.3.1 冲击波半径（把敌人半径算进去，手感更宽松）
                    if (dist > radiusPx + m.radius * Cell) continue;

                    // 2.1.3 每个敌人单次传送只结算一次
                    if (!hitSet.Add(m)) continue;
                    processed++;

                    // 2.3.2 是否穿墙（关掉时做一次视线判定）
                    if (!cfg.ShockwavePierceWall)
                    {
                        try
                        {
                            bool ignoreOneWay = false;
                            if (!hero.sightCheckCase(m.cx, m.cy, ref ignoreOneWay, null)) continue;
                        }
                        catch { }
                    }

                    // 2.3.3 对 Boss / 精英的伤害衰减
                    double mul = 1.0;
                    if (IsBoss(m)) mul *= Math.Max(0.0, cfg.BossDamageMult);
                    else if (m.elite) mul *= Math.Max(0.0, cfg.EliteDamageMult);

                    int lifeBefore = m.life;

                    // 3.3.1 先挂连锁标记，再造成伤害 ——
                    // 这样这次伤害打死的敌人会被 OnApplyAttackResult 认成"本次传送的击杀"
                    try { markForChain?.Invoke(m); } catch { }

                    // 2.2.4 伤害类型 / 元素 / 暴击判定：完全走原版攻击管线
                    //（_AttackUtils.createFromHero → useItemAffixes → hit）
                    int tier = 0;
                    try { tier = hero.getRelevantPerkTier(Hx("BackBlink")); } catch { }
#pragma warning disable CS0612
                    AttackData atk = dc.tool.atk.AttackUtils.Class.createFromHero.Invoke(hero, (dynamic)(dmg * mul), (int?)tier);
#pragma warning restore CS0612
                    if (atk == null) continue;
                    if (cfg.ShockwaveTag > 0)
                    {
                        try { atk.addTag(cfg.ShockwaveTag); } catch { }
                    }
                    try { atk.useItemAffixes(c.Item); } catch { }

                    // 2.3.4 无视全局护盾：物品数据里已经加了 { affix: "IgnoreGlobalShield" }，
                    //        但原版 legendAffixes 只对传说品质实例生效，这里再补一道保险。
                    if (forceIgnoreShield)
                    {
                        try
                        {
                            if (!atk.hasAffix(Hx("IgnoreGlobalShield")))
                                atk.addAffix(Hx("IgnoreGlobalShield"));
                        }
                        catch { }
                    }

                    // 2.2.5 与背刺、处决等机制联动：原版 hit 内部会走 onDirectHitFromHero 等链路
#pragma warning disable CS0612
                    dc.tool.atk.AttackUtils.Class.hit.Invoke(atk, m);
#pragma warning restore CS0612

                    // 3.1.1 / 3.1.2 目标死亡判定（本段伤害造成的击杀）
                    if (lifeBefore > 0 && (m.life <= 0 || m.destroyed))
                    {
                        kills++;
                        try { outKilled?.Add(m); } catch { }
                    }
                }
            }
            catch { }
            return kills;
        }

        /// <summary>3.4.2 / 3.4.4 连锁伤害倍率。</summary>
        public static double ChainDamageMult(int stacks, BlinkConfig cfg)
        {
            if (stacks <= 0) return 1.0;
            double per = Math.Max(0.0, cfg.ChainDamageBonusPerStack);
            double mult = 1.0 + per * stacks;
            if (cfg.ChainDamageFalloff > 0.0 && cfg.ChainDamageFalloff < 1.0 && stacks > 1)
            {
                mult *= Math.Pow(cfg.ChainDamageFalloff, stacks - 1);
            }
            return Math.Max(0.05, mult);
        }

        /// <summary>判断是不是 Boss（用原版 Boss 基类 + 常见 Boss 类型做类型判断）。</summary>
        public static bool IsBoss(Mob m)
        {
            try
            {
                if (m == null) return false;
                if (m is Boss) return true;
                if (m is Beholder) return true;
                if (m is dc.en.mob.boss.TimeKeeper) return true;
                if (m is Giant) return true;
                if (m is KingsHand) return true;
                if (m is MamaTick) return true;
                if (m is Queen) return true;
                if (m is Dooku) return true;
                if (m is DookuBeast) return true;
                if (m is Behemoth) return true;
                if (m is GardenerBoss) return true;
                if (m is dc.en.mob.boss.death.Death) return true;
                return false;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------------- 3.5 连锁视觉提示

        /// <summary>3.5.3 连杀计数 / UI 反馈：把连锁层数与伤害倍率显示在右上角的连击 UI 上。</summary>
        public static void ShowChainHud(Hero hero, int stacks, double remainS)
        {
            try
            {
                if (hero == null || hero._level == null) return;
                dynamic hud = hero._level.game?.hud;
                if (hud == null || hud.comboCount == null) return;

                if (stacks <= 0)
                {
                    hud.comboCount.reset();
                    return;
                }

                // 复用原版连击 UI：主数字 = 当前冲击波伤害倍率(%), 右侧 xN = 连锁层数
                double mult = ChainDamageMult(stacks, BlinkKeys.Config.Value);
                hud.comboCount.setValue((int)Math.Round(mult * 100.0), stacks);
            }
            catch { }
        }

        /// <summary>刷新游戏时间（主模组每帧调用）。</summary>
        public static void UpdateGameTime(Hero hero)
        {
            try
            {
                if (hero == null || hero._level == null) return;
                dynamic g = hero._level.game;
                if (g == null) return;
                dynamic data = g.data;
                if (data == null) return;
                GameTime = Convert.ToDouble((object)data.gameTimeS);
            }
            catch { }
        }
    }
}
