#nullable disable

using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.tool;
using dc.tool.weap;

// dc 命名空间里也有一个 Math 类，直接 using dc 会让 Math 变成二义性引用 → 这里显式别名到 BCL。
using Math = System.Math;

namespace Sukuna
{
    /// <summary>「解」：一次待执行的追加斩击。</summary>
    internal struct PendingSlash
    {
        /// <summary>被斩的目标。</summary>
        public Entity Target;

        /// <summary>还剩几段。</summary>
        public int Remaining;

        /// <summary>距下一段的计时。</summary>
        public double Timer;
    }

    /// <summary>「捌」：一次待执行的贯穿斩线。</summary>
    internal struct PendingCleave
    {
        /// <summary>斩线要穿过的目标。</summary>
        public Entity Target;

        /// <summary>延迟计时（等原版那一刀的动作先播完）。</summary>
        public double Timer;
    }

    /// <summary>
    /// 宿傩斩击引擎。
    /// ================
    /// 全部伤害都走**原版** <see cref="dc.tool.weap.QueenRapier.queenStrike"/>，
    /// 因此伤害公式 / tag(14,7,2) / DamageType.ExtraDamage / 屏幕震动 /
    /// sfx/enm/enm_queen_split_release.wav / fxQueenRapierCut 全部是原版链路，
    /// 宿傩只是重新编排了"在哪里、朝哪个方向、斩几次"。
    ///
    /// 原版机制要点（GamePseudocode/dc.tool.weap/QueenRapier.cs）：
    ///   onExecute()          → 挥剑命中 + queenStrikeTrigger()
    ///   hitFromWeapon(e)     → 等 item.props.duration 秒后对 e 调 queenStrike()
    ///   queenStrike(t,x,y,a) → 造 AttackData（power2 / ExtraDamage）→ _AttackUtils.hit()
    ///                          + 在 (x,y) 处以角度 a 生成 fxQueenRapierCut
    ///   queenStrikeTrigger() → 从英雄出发沿斩击方向画 ±sliceLength 的线段，
    ///                          对线段附近的敌人再 queenStrike()（"斩断现实"的连锁）
    ///
    /// 注意：queenStrike 内部有一把**按目标的 0.1 秒冷却**（key 1390411776），
    /// 所以同一目标的追加斩击间隔必须 &gt; 0.1 秒，否则会被原版直接吞掉。
    /// </summary>
    public sealed class SukunaSlash
    {
        /// <summary>1 格 = 24 像素（原版所有坐标换算都用这个常数）。</summary>
        private const double Tile = 24.0;

        /// <summary>原版 queenStrike 的目标冷却（0.1 秒 × baseFps）。比它慢才打得到。</summary>
        private const double TargetCooldown = 0.1;

        /// <summary>「解」的追加斩击队列。</summary>
        private readonly List<PendingSlash> _slashes = new();

        /// <summary>「捌」的待放斩线队列。</summary>
        private readonly List<PendingCleave> _cleaves = new();

        private readonly Random _rng = new();

        /// <summary>
        /// 复入层数：&gt;0 表示当前这一发 queenStrike 是**我们自己追加**的，
        /// 此时不再连锁出新的斩击（否则会无限套娃）。
        /// </summary>
        private int _depth;

        /// <summary>「伏魔御厨子」剩余时间（秒），&lt;=0 表示领域没开。</summary>
        private double _domainLeft;

        /// <summary>领域下一次结算（斩击）的计时。</summary>
        private double _domainTick;

        /// <summary>领域下一次画死亡球粒子的计时。</summary>
        private double _domainOrbTick;

        /// <summary>领域已经展开了多久（秒），用来算死亡球的当前半径。</summary>
        private double _domainElapsed;

        /// <summary>领域总时长（展开那一刻的快照，用于算增长进度）。</summary>
        private double _domainTotal;

        /// <summary>最近一次用过的女王细剑（领域展开时要用它造 AttackData）。</summary>
        private dc.tool.weap.QueenRapier _rapier;

        /// <summary>统计：本局总共追加了多少段斩击（日志采样用）。</summary>
        private int _extraSlashes;

        // ---------------------------------------------------------------- 斩击特效染色状态
        //
        // 原版 queenStrike() 里那句 fx.allocMultiBatch(batchGroup, t, x, y, ...) 就是在
        // (x,y) 生成 fxQueenRapierCut 那颗斩痕粒子。我们没法改原版代码，
        // 于是 Hook dc.Fx.allocMultiBatch：
        //   进入 queenStrike 前记下这一刀的 (x,y) 并置 _tintActive，
        //   在 Hook 里比对坐标，命中就把粒子的 r/g/b 改成黑红。

        /// <summary>是否正在"女王细剑斩击"的调用里（用于精确识别那颗斩痕粒子）。</summary>
        private bool _tintActive;

        /// <summary>这一刀斩击的位置（原版正是用同一个 x,y 去 allocMultiBatch）。</summary>
        private double _tintX, _tintY;

        /// <summary>已经染色过多少颗斩痕粒子（日志采样用）。</summary>
        private int _tintedParticles;

        /// <summary>领域是否正在展开。</summary>
        public bool DomainActive => _domainLeft > 0.0;

        /// <summary>领域剩余秒数（HUD / 日志用）。</summary>
        public double DomainLeft => _domainLeft;

        // ================================================================ 对外入口

        /// <summary>
        /// 由 Hook_QueenRapier.queenStrike 在调用完 orig 之后回调。
        /// 这里只负责"排队"，真正的追加斩击在 Update() 里按节奏放出去。
        /// </summary>
        public void OnVanillaStrike(dc.tool.weap.QueenRapier self, Entity target, double x, double y, double angle)
        {
            if (self == null) return;
            _rapier = self;

            // 我们自己追加的那一发：只记武器，不再连锁
            if (_depth > 0) return;

            if (target == null || target.destroyed || target.life <= 0) return;

            var hero = self.owner;
            if (hero == null || hero.destroyed || hero.life <= 0) return;
            if (target._level == null || target._level != hero._level) return;
            if (target._team == null || target._team != target._level.teamMob) return;

            // ---- 「解」：在目标周围随机位置追加多段斩击 ----
            if (SukunaFeatures.IsOn(SukunaFeature.Dismantle) && !HasPendingSlash(target))
            {
                var cfg = Cfg();
                int count = Math.Max(0, cfg?.DismantleSlashes ?? 8);
                if (count > 0)
                {
                    _slashes.Add(new PendingSlash
                    {
                        Target = target,
                        Remaining = count,
                        Timer = Math.Max(TargetCooldown, cfg?.DismantleInterval ?? 0.12),
                    });
                }
            }

            // ---- 「捌」：对强者补一条贯穿斩线 ----
            if (SukunaFeatures.IsOn(SukunaFeature.Cleave) && IsStrong(target) && !HasPendingCleave(target))
            {
                double delay = Math.Max(TargetCooldown, Cfg()?.CleaveDelay ?? 0.18);
                _cleaves.Add(new PendingCleave { Target = target, Timer = delay });
            }
        }

        /// <summary>
        /// 每帧（IOnHeroUpdate）：推进「解」的追加斩击、「捌」的斩线、以及「伏魔御厨子」。
        /// </summary>
        public void Update(double dt, Hero hero)
        {
            if (hero == null || hero.destroyed || hero.life <= 0 || hero._level == null)
            {
                Clear();
                return;
            }

            var rapier = ResolveRapier(hero);
            if (rapier == null)
            {
                // 手里没有女王细剑 → 清掉待发队列（避免换武器后突然"延迟爆炸"）
                _slashes.Clear();
                _cleaves.Clear();
                _domainLeft = 0.0;
                return;
            }

            TickDismantle(dt, rapier);
            TickCleave(dt, hero, rapier);
            TickDomain(dt, hero, rapier);
        }

        /// <summary>释放「伏魔御厨子」。返回 false = 没放出（没开开关 / 手里没有女王细剑）。</summary>
        public bool CastDomain(Hero hero)
        {
            if (hero == null || hero.destroyed || hero.life <= 0) return false;
            if (!SukunaFeatures.IsOn(SukunaFeature.Domain)) return false;

            var rapier = ResolveRapier(hero);
            if (rapier == null) return false;

            var cfg = Cfg();
            _domainTotal = Math.Max(0.1, cfg?.DomainDuration ?? 6.0);
            _domainLeft = _domainTotal;
            _domainElapsed = 0.0;
            _domainTick = 0.0;                 // 立刻结算第一下
            _domainOrbTick = 0.0;              // 立刻画一次死亡球

            // 领域展开的瞬间：红色屏幕染色 + 闪光（原版 SlowOrb 爆炸用的也是这两个）
            if (cfg == null || cfg.DomainScreenMask)
            {
                int col = OrbColor(cfg);
                DomainScreenEffect(hero, col, _domainTotal);
            }

            return true;
        }

        /// <summary>清空全部状态（换关 / 卸载 / 英雄死亡）。</summary>
        public void Clear()
        {
            _slashes.Clear();
            _cleaves.Clear();
            _domainLeft = 0.0;
            _domainTick = 0.0;
            _domainOrbTick = 0.0;
            _domainElapsed = 0.0;
            _domainTotal = 0.0;
            _depth = 0;
        }

        /// <summary>换关时调用：丢掉武器缓存以外的全部状态。</summary>
        public void ResetForNewLevel()
        {
            Clear();
            _rapier = null;
        }

        // ================================================================ 「解」

        private void TickDismantle(double dt, dc.tool.weap.QueenRapier rapier)
        {
            if (_slashes.Count == 0) return;

            var cfg = Cfg();
            double interval = Math.Max(TargetCooldown, cfg?.DismantleInterval ?? 0.12);
            double radius = Math.Max(0.0, cfg?.DismantleRadius ?? 230.0);

            for (int i = _slashes.Count - 1; i >= 0; i--)
            {
                var p = _slashes[i];
                var e = p.Target;

                if (e == null || e.destroyed || e.life <= 0)
                {
                    _slashes.RemoveAt(i);
                    continue;
                }

                p.Timer -= dt;
                if (p.Timer > 0.0)
                {
                    _slashes[i] = p;
                    continue;
                }

                // 目标中心（世界像素坐标，和原版 queenStrikeTrigger 的算法一致）
                double ex = CenterX(e);
                double ey = CenterY(e);

                // 在目标周围随机落点 + 随机斩击角度（角度只影响 fxQueenRapierCut 的朝向）
                double a = _rng.NextDouble() * Math.PI * 2.0;
                double dist = _rng.NextDouble() * radius;
                double px = ex + Math.Cos(a) * dist;
                double py = ey + Math.Sin(a) * dist;
                double ang = _rng.NextDouble() * Math.PI * 2.0;

                Strike(rapier, e, px, py, ang);

                p.Remaining--;
                p.Timer = interval;
                if (p.Remaining <= 0) _slashes.RemoveAt(i);
                else _slashes[i] = p;
            }
        }

        private bool HasPendingSlash(Entity e)
        {
            for (int i = 0; i < _slashes.Count; i++)
                if (ReferenceEquals(_slashes[i].Target, e)) return true;
            return false;
        }

        // ================================================================ 「捌」

        private void TickCleave(double dt, Hero hero, dc.tool.weap.QueenRapier rapier)
        {
            if (_cleaves.Count == 0) return;

            for (int i = _cleaves.Count - 1; i >= 0; i--)
            {
                var c = _cleaves[i];
                var t = c.Target;
                if (t == null || t.destroyed || t.life <= 0)
                {
                    _cleaves.RemoveAt(i);
                    continue;
                }

                c.Timer -= dt;
                if (c.Timer > 0.0)
                {
                    _cleaves[i] = c;
                    continue;
                }

                _cleaves.RemoveAt(i);
                DoCleave(hero, rapier, CenterX(t), CenterY(t), _rng.NextDouble() * Math.PI * 2.0);
            }
        }

        private bool HasPendingCleave(Entity e)
        {
            for (int i = 0; i < _cleaves.Count; i++)
                if (ReferenceEquals(_cleaves[i].Target, e)) return true;
            return false;
        }

        /// <summary>
        /// 「捌」——一条贯穿全场的斩线。
        /// 判定复刻原版 Queen.doCutLineAttack(cutLine, width, power)：
        ///   目标中心到"过 (ox,oy)、方向 angle 的**无限长直线**"的距离
        ///   &lt; 目标半径 + width*24*0.5  →  命中
        /// 视觉：线上每个受击目标都用**同一个角度**放原版斩击，
        ///       于是 fxQueenRapierCut 会连成一条方向一致的贯穿斩。
        /// </summary>
        private void DoCleave(Hero hero, dc.tool.weap.QueenRapier rapier, double ox, double oy, double angle)
        {
            var cfg = Cfg();
            double width = Math.Max(0.0, cfg?.CleaveWidth ?? 1.2);
            double half = width * Tile * 0.5;

            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);

            // 先收集，再结算 —— 避免一边遍历 team.members 一边让游戏改它
            var victims = new List<Entity>();
            CollectOpponents(hero, victims);

            for (int i = 0; i < victims.Count; i++)
            {
                var e = victims[i];
                double ex = CenterX(e);
                double ey = CenterY(e);
                double dx = ex - ox;
                double dy = ey - oy;
                // 点到直线的距离 = |cross(P - O, u)|
                double lineDist = Math.Abs(dx * sin - dy * cos);

                if (e.radius + half > lineDist)
                {
                    // 斩线穿过它 —— 用原版斩击结算（位置用它自己的中心，角度统一）
                    Strike(rapier, e, ex, ey, angle);
                }
            }
        }

        // ================================================================ 「伏魔御厨子」

        private void TickDomain(double dt, Hero hero, dc.tool.weap.QueenRapier rapier)
        {
            if (_domainLeft <= 0.0) return;

            _domainLeft -= dt;
            _domainElapsed += dt;
            if (_domainLeft <= 0.0)
            {
                _domainLeft = 0.0;
                return;
            }

            var cfg = Cfg();
            double radius = DomainRadiusNow(cfg);

            // ---- 领域外观：红色死亡球，随时间慢慢变大 ----
            // 用的是原版「死亡球」(dc.en.bu.Orb) 同一套 fx.orb(x, y, dx, radius, color)：
            // 沿半径撒一圈闪电粒子，原版是紫蓝色，这里换成红色。
            _domainOrbTick -= dt;
            if (_domainOrbTick <= 0.0)
            {
                _domainOrbTick = Math.Max(0.02, cfg?.DomainOrbTick ?? 0.08);

                // 让球体有点"呼吸"感：主球 + 略小的一圈
                DrawDeathOrb(hero, radius, OrbColor(cfg));
                if (radius > Tile * 2.0) DrawDeathOrb(hero, radius * 0.55, OrbColor(cfg));
            }

            // ---- 领域伤害：范围内全体敌人各吃一发「解」----
            _domainTick -= dt;
            if (_domainTick > 0.0) return;
            _domainTick = Math.Max(TargetCooldown, cfg?.DomainInterval ?? 0.25);

            var victims = new List<Entity>();
            CollectOpponents(hero, victims);

            double hx = CenterX(hero);
            double hy = CenterY(hero);

            for (int i = 0; i < victims.Count; i++)
            {
                var e = victims[i];
                double ex = CenterX(e);
                double ey = CenterY(e);
                double ddx = ex - hx;
                double ddy = ey - hy;
                if (Math.Sqrt(ddx * ddx + ddy * ddy) > radius + e.radius) continue;

                Strike(rapier, e, ex, ey, _rng.NextDouble() * Math.PI * 2.0);
            }
        }

        /// <summary>
        /// 领域当前的半径：从 DomainStartRadius 在 DomainGrowTime 秒内**缓入**（先慢后快）长到
        /// DomainRadius，之后保持不变。**伤害半径和死亡球半径共用这个值**，所以是"边展开边斩"。
        ///
        /// 曲线：radius = start + (full - start) * t^DomainGrowEase
        ///   t = elapsed / growTime（夹到 0~1）
        ///   DomainGrowEase = 1 匀速，2 二次缓入（默认），3 更极端的"先慢后快"。
        /// </summary>
        private double DomainRadiusNow(SukunaConfig cfg)
        {
            double full = Math.Max(Tile, cfg?.DomainRadius ?? 360.0);
            double start = Math.Max(0.0, cfg?.DomainStartRadius ?? 60.0);
            double grow = Math.Max(0.0, cfg?.DomainGrowTime ?? 2.5);

            if (start >= full) return full;
            if (grow <= 0.0) return full;

            double t = Math.Min(1.0, _domainElapsed / grow);
            double ease = Math.Max(1.0, cfg?.DomainGrowEase ?? 2.0);

            // 缓入：t^ease —— 起步慢，后段冲得快
            return start + (full - start) * Math.Pow(t, ease);
        }

        /// <summary>配置里的死亡球颜色（0xRRGGBB），默认正红 0xFF2020。</summary>
        private static int OrbColor(SukunaConfig cfg)
        {
            return SukunaKeys.ParseColor(cfg?.DomainOrbColor, 0xFF2020);
        }

        /// <summary>
        /// 画一层"死亡球"粒子。完全是原版 <c>dc.Fx.orb(x, y, dx, radius, c)</c>
        /// —— 原版 dc.en.bu.Orb.postUpdate() 就是这么画那颗紫蓝色死亡球的；
        /// dx = 0 表示球不朝任何方向偏移（领域是原地张开的）。
        /// </summary>
        private static void DrawDeathOrb(Hero hero, double radiusPx, int color)
        {
            try
            {
                var fx = hero?._level?.fx;
                if (fx == null) return;
                fx.orb(CenterX(hero), CenterY(hero), 0.0, radiusPx, color);
            }
            catch { }
        }

        /// <summary>
        /// 领域展开瞬间的屏幕效果：红色染色 + 闪光（原版 SlowOrb 爆炸 / 女王斩线同款 API）。
        /// 染色只在"展开"那一瞬间（约 0.4~1.5 秒），持续期间的视觉交给红色死亡球。
        /// </summary>
        private static void DomainScreenEffect(Hero hero, int color, double domainDurationS)
        {
            try
            {
                var fx = hero?._level?.fx;
                if (fx == null) return;

                // 屏幕染色的时长：取领域总时长的 20%，夹在 0.4 ~ 1.5 秒之间
                double maskS = Math.Min(1.5, Math.Max(0.4, domainDurationS * 0.2));

                // 红色屏幕染色：淡入 0.15s → 保持 maskS → 淡出 0.5s
                fx.customMask(color, 0.35, 0.15, maskS, 0.5, null);

                // 展开瞬间闪一下
                fx.multiFlashBangS(color, 0.55, 0.05, 0.28);
            }
            catch { }
        }

        // ================================================================ 斩击特效染色

        /// <summary>
        /// 在调用原版 <c>queenStrike</c> 之前调用：记下这一刀的位置，开始等着抓斩痕粒子。
        /// </summary>
        public void BeginVanillaSlashFx(double x, double y)
        {
            _tintX = x;
            _tintY = y;
            _tintActive = true;
        }

        /// <summary>原版 <c>queenStrike</c> 返回后调用：停止抓粒子。</summary>
        public void EndVanillaSlashFx()
        {
            _tintActive = false;
        }

        /// <summary>
        /// 由 Hook_Fx.allocMultiBatch 回调。
        /// 只有当"这一颗粒子是在女王细剑的 queenStrike 里、并且坐标就是那一刀的落点"时才染色，
        /// 所以不会误伤其它特效。
        /// </summary>
        public void TryTintSlashFx(dc.libs.heaps.HParticle hp, double x, double y)
        {
            if (hp == null || !_tintActive) return;
            if (Math.Abs(x - _tintX) > 0.01 || Math.Abs(y - _tintY) > 0.01) return;

            var cfg = Cfg();
            if (cfg != null && !cfg.TintSlashFx) return;

            int col = SukunaKeys.ParseColor(cfg?.SlashFxColor, 0xE01414);
            if (col <= 0) return;

            try
            {
                hp.r = ((col >> 16) & 0xFF) / 255.0;
                hp.g = ((col >> 8) & 0xFF) / 255.0;
                hp.b = (col & 0xFF) / 255.0;

                _tintedParticles++;
                if (_tintedParticles % 40 == 1)
                {
                    try
                    {
                        SukunaMain.Log($"[Sukuna] 斩击特效已染色 #{_tintedParticles}：#{col:X6}");
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>清空染色计数（换关用）。</summary>
        public void ResetFxTintStats()
        {
            _tintedParticles = 0;
            _tintActive = false;
        }

        // ================================================================ 无视墙体

        /// <summary>
        /// 兜底命中判定：**完全不看地图碰撞**，只按"距离 + 朝向"决定能不能打到。
        /// 原版 <c>Weapon.canHit</c> 会走 <c>map.collisions</c> / <c>sightCheckCase</c> 做遮挡检测，
        /// 所以墙后的敌人砍不到；这里给它补一条穿墙的捷径。
        ///
        /// 范围来自配置（格）：身前 <c>WallIgnoreRange</c> 格、上下 <c>WallIgnoreHeight</c> 格。
        /// </summary>
        public bool InWallIgnoreRange(Hero hero, Entity e)
        {
            try
            {
                if (hero == null || hero.destroyed || hero.life <= 0 || hero._level == null) return false;
                if (e == null || e.destroyed || e.life <= 0) return false;
                if (e._level == null || e._level != hero._level) return false;
                if (e._team == null || e._team != e._level.teamMob) return false;

                var cfg = Cfg();
                double range = Math.Max(Tile, (cfg?.WallIgnoreRange ?? 10.0) * Tile);
                double halfH = Math.Max(Tile * 0.5, (cfg?.WallIgnoreHeight ?? 4.0) * Tile * 0.5);

                double dx = CenterX(e) - CenterX(hero);
                double dy = CenterY(e) - CenterY(hero);

                // 高度带（带上目标半个身高，免得大块头被判定在外）
                if (Math.Abs(dy) > halfH + e.hei * 0.5) return false;

                // 只打身前；身后留 1 格余量，避免贴身时朝向抖动导致漏判
                int dir = hero.dir >= 0 ? 1 : -1;
                if (dx * dir < -Tile) return false;

                return Math.Abs(dx) <= range + e.radius;
            }
            catch { return false; }
        }

        // ================================================================ 随机斩击角度

        /// <summary>
        /// 把原版 <c>getStrikeAngle()</c> 给的角度打散。
        ///
        /// 原版三段连击的斩击角度是写死在 `weapon` 表 `strikeChain[i].props.angle` 里的
        /// （我们这份数据补丁沿用原版：1a = 0.5 斜向下、2a = -0.4 斜向上、3a = 0 水平），
        /// `QueenRapier.getStrikeAngle(c)` 就是拿它乘朝向再补一个 π，所以每一刀的方向都是固定的。
        ///
        /// 这里以"这一刀原本的朝向"为中心，在 <see cref="SukunaConfig.StrikeAngleRangeDeg"/>
        /// 范围内均匀随机：
        ///   360°（默认）→ 完全随机；90° → 正向 ±45°；0° → 等于原版。
        /// 因为范围是 [-half, +half] 的对称区间，取满 360° 时正好覆盖整圈。
        /// </summary>
        public double RandomizeStrikeAngle(double baseAngle)
        {
            var cfg = Cfg();
            double rangeDeg = cfg?.StrikeAngleRangeDeg ?? 360.0;
            if (rangeDeg <= 0.0) return baseAngle;
            if (rangeDeg > 360.0) rangeDeg = 360.0;

            // rangeDeg/2 度 → 弧度
            double half = rangeDeg * Math.PI / 360.0;
            return baseAngle + (_rng.NextDouble() * 2.0 - 1.0) * half;
        }

        // ================================================================ 通用工具

        /// <summary>
        /// 用原版 queenStrike 结算一发斩击。会推高复入层数，
        /// 让 Hook 那边知道"这是我们自己放的"，不再连锁。
        /// </summary>
        private void Strike(dc.tool.weap.QueenRapier rapier, Entity target, double x, double y, double angle)
        {
            if (rapier == null || rapier.destroyed) return;
            if (target == null || target.destroyed || target.life <= 0) return;

            // 原版 queenStrike 内部要读 get_curSkillInf().props.power2；
            // 连击链索引越界时它是 null → 这里先挡掉，免得打到一半崩。
            try { if (rapier.get_curSkillInf() == null) return; } catch { return; }

            _depth++;
            try
            {
                rapier.queenStrike(target, x, y, angle);
                _extraSlashes++;
            }
            catch { /* 单个目标出错不影响其它目标 */ }
            finally
            {
                _depth--;
            }

            if (_extraSlashes % 50 == 1)
            {
                try { SukunaMain.Log($"[Sukuna] 已追加斩击 {_extraSlashes} 段（领域={DomainActive}）"); } catch { }
            }
        }

        /// <summary>把英雄的敌对队伍成员收进列表（同关卡、存活）。</summary>
        private static void CollectOpponents(Hero hero, List<Entity> outList)
        {
            outList.Clear();
            try
            {
                var team = hero._team;
                if (team == null) return;

                var it = team.opponentsIterator.reset(team);
                if (it == null) return;

                while (it.hasNext())
                {
                    var e = it.next();
                    if (e == null || e.destroyed || e.life <= 0) continue;
                    if (e._level == null || e._level != hero._level) continue;
                    outList.Add(e);
                }
            }
            catch { }
        }

        /// <summary>目标中心的世界坐标（像素）——与原版所有坐标换算一致。</summary>
        private static double CenterX(Entity e) => ((double)e.cx + e.xr) * Tile;

        /// <summary>目标中心的世界坐标（像素，脚底往上抬半个身高）。</summary>
        private static double CenterY(Entity e) => ((double)e.cy + e.yr) * Tile - e.hei * 0.5;

        /// <summary>
        /// 是不是"强者"（吃「捌」）：精英 / 体型大 / 血厚。
        /// 对应宿傩「捌」的设定 —— 针对咒力强、体格大的对象自动调整的一击。
        /// </summary>
        private static bool IsStrong(Entity e)
        {
            try
            {
                if (e is Mob m && m.elite) return true;
                if (e.hei >= 2.0) return true;
                int threshold = Cfg()?.CleaveLifeThreshold ?? 260;
                return e.life >= threshold;
            }
            catch { return false; }
        }

        /// <summary>
        /// 找英雄手上的女王细剑：先用手上现成的，再回头用缓存的那把。
        /// 用 C# 类型判断（is QueenRapier）而不是字符串比较，避免 Haxe 字符串的坑。
        /// </summary>
        private dc.tool.weap.QueenRapier ResolveRapier(Hero hero)
        {
            try
            {
                var arr = hero.weaponsManager?.mainWeapons;
                if (arr != null)
                {
                    int n = arr.length;
                    for (int i = 0; i < n; i++)
                    {
                        object o = (object)arr.getDyn(i);
                        if (o is dc.tool.weap.QueenRapier qr && !qr.destroyed && qr.owner == hero)
                        {
                            _rapier = qr;
                            return qr;
                        }
                    }
                }
            }
            catch { }

            if (_rapier != null && !_rapier.destroyed && _rapier.owner == hero) return _rapier;
            return null;
        }

        private static SukunaConfig Cfg()
        {
            try { return SukunaKeys.Config.Value; } catch { return null; }
        }
    }
}
