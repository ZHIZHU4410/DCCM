#nullable disable
using System;
using dc;
using dc.en;
using dc.tool;
using dc.tool.atk;

using SysMath = System.Math;

namespace GojoLimitless.Abilities
{
    /// <summary>
    /// 无下限（Infinity）—— 被动。
    ///
    /// 原作设定：任何接近五条悟的东西都会被无限分割的"距离"吃掉，永远到不了终点。
    /// 这里的实现分三条：
    ///
    /// 1) **屏障吸收**（<see cref="TryAbsorb"/>，由 <c>Hook_Entity.applyAttackResult</c> 调用）：
    ///    敌人打向英雄的攻击先过"咒力储量"。储量够 → 伤害清零（攻击"永远没到"）；
    ///    储量被打穿 → 这一发实打实落在身上，并触发碎裂表现 + 0.6 秒充能停顿。
    ///    储量按秒快速回充，所以小怪连续输出基本无伤，Boss 大伤害能打穿。
    ///
    /// 2) **迟滞领域**（<see cref="UpdateField"/> 里每 0.1 秒刷一次）：
    ///    半径内所有敌人按"距离越近越慢"被压低移速，脚下会一直有一个青色光圈。
    ///
    ///    ⚠️ 减速**不走 affect**：原版 affect 133 是"冰冻/解冻"计数器
    ///    （<c>get_slowPerStack() = 1.0 / thawMaxStacks</c>，而 <c>thawMaxStacks = 99999</c>，
    ///    每层只减 0.001%），而且 <c>Entity.setAffectS</c> 的层数是由 value 参数决定的、
    ///    133 还有"桶非空即早退"的判定 —— 两条路都堵死。
    ///    所以改成 hook <c>Mob.getMoveSpeedMul()</c>，见 <see cref="SlowAura"/>。
    ///
    /// 3) **湮灭弹幕**：飞进半径的敌方子弹直接消失（InfinityEraseBullets=true 时）——
    ///    这就是"什么都不会碰到他"最直观的表现。
    /// </summary>
    public sealed class InfinityAbility : GojoAbilityBase
    {
        public override string Id => "infinity";
        public override string DisplayName => "无下限";
        protected override double CooldownSeconds => 0.0;

        /// <summary>被动：没有自己的 AbilityCfg 段（开关在 <c>Infinity.Enabled</c>）。</summary>
        protected override AbilityCfg CfgOf(Configs c) => null;

        /// <summary>被动：没有按键。</summary>
        public override int TriggerKey => 0;

        protected override bool Activate(Hero hero, double dt) => false;

        // ------------------------------------------------------------ 静态状态
        //
        // 英雄同时只会有一个，直接用静态字段最简单；
        // 但**必须**在换关/重开时复位，否则上一局的储量会串到下一局。

        private const double SlowRefreshInterval = 0.10;
        private const double BreakPenaltySeconds = 0.60;
        private const double LightNameRadiusCase = 7.0;

        private static double _reserve;
        private static double _maxReserve = -1.0;
        private static double _breakPenalty;
        private static double _blockFlash;
        private static double _breakFlash;
        private static double _slowTimer;
        private static int _blockedCount;
        private static int _brokenCount;
        private static int _erasedBullets;
        private static bool _logged;
        private static bool _lightTried;
        private static dc.tool.EntityLight _light;
        private static int _lastLoggedBlocks = -100;
        private static int _lastLoggedSlows = -1000;
        private static int _lastLoggedErased = 0;
        private static int _slowedTotal;

        /// <summary>
        /// 尝试吸收一次指向英雄的攻击。
        ///
        /// <paramref name="self"/> 就是**被打的人**：<c>Entity.applyAttackResult</c> 定义在
        /// 受击方上（Mob 的 override 一进来就给自己叠 affect 49、并调 base），
        /// 所以调用点用 <c>self is Hero</c> 就能确定是英雄在挨打。
        ///
        /// 返回 true = 已经完全挡下（此时伤害字段已被清零，原函数会因为
        /// <c>finalDmg &lt;= 0</c> 直接 return）。
        /// </summary>
        public static bool TryAbsorb(Hero hero, AttackData atk, int dmg)
        {
            if (hero == null || atk == null) return false;
            if (!GojoHub.AbilitiesActive) return false;
            if (dmg <= 0) return false;
            if (hero.destroyed || hero.life <= 0) return false;

            InfinityCfg ic = Cfg.V.Infinity;
            if (ic != null && !ic.Enabled) return false;

            // 已经死了/游戏没开始时不介入
            try { if (hero._level?.game == null) return false; } catch { return false; }

            EnsureInit(hero);

            if (_breakPenalty > 0.0)
            {
                return false;   // 碎裂后的洞口：这一段打进来就是实伤（停顿由 Update 推进）
            }

            // 单次最多吃掉 MaxDrainPerHit 比例的储量：既不会"一发吸干"，
            // 也让"储量见底 = 下一发必穿"这个节奏成立。
            double drainRatio = ic != null ? ic.MaxDrainPerHit : 0.35;
            if (drainRatio <= 0.0) drainRatio = 0.35;
            if (drainRatio > 1.0) drainRatio = 1.0;

            double absorbCap = _maxReserve > 0.0
                ? SysMath.Min(_reserve, _maxReserve * drainRatio)
                : _reserve;

            if (dmg <= absorbCap)
            {
                _reserve -= dmg;
                _blockFlash = 0.35;
                _blockedCount++;
                TryZeroDamage(atk);
                _Status = $"屏障挡下 {dmg} 点伤害（余 {_reserve:0}）";
                LogThrottled();
                return true;
            }

            // 打穿：这一发照常吃，然后碎裂 + 短暂充能停顿。
            // 注意把储量清零（而不是扣成负数），不然下一发会连着穿。
            _brokenCount++;
            _reserve = 0.0;
            _breakPenalty = ic != null ? ic.BreakPenalty : 0.60;
            _breakFlash = 1.0;
            _Status = $"[!] 屏障被打穿（本发 {dmg} 点）";
            Log.Debug($"无下限被打穿！本发伤害 {dmg}（累计穿透 {_brokenCount} 次）");
            return false;
        }

        /// <summary>最近一次屏障事件（HUD 用，人类可读）。</summary>
        private static string _Status = "";

        /// <summary>HUD 显示用：最近一条无下限事件。</summary>
        public static string LastStatus => _Status;

        /// <summary>本帧残余的碎裂停顿（由 Update 推进，避免在静态里乱扣时间）。</summary>
        private static void TickBreakPenalty(double dt, InfinityCfg ic)
        {
            if (_breakPenalty <= 0.0) return;
            _breakPenalty -= dt;
            if (_breakPenalty < 0.0) _breakPenalty = 0.0;
        }

        /// <summary>把一发攻击的伤害彻底清零（伤害在 applyAttackResult 里读 finalDmg）。</summary>
        private static void TryZeroDamage(AttackData atk)
        {
            try { atk.finalDmg = 0; } catch { }
            try { atk.rawFinalDmg = 0.0; } catch { }
            try { atk.inflictedDmg = 0; } catch { }
            try { atk.volleyDmg = 0; } catch { }
            try { atk.finalMissedDmg = 0.0; } catch { }
        }

        // ------------------------------------------------------------ 每帧

        protected override void OnUpdate(Hero hero, double dt)
        {
            if (hero.destroyed || hero.life <= 0) { DisposeLight(); return; }

            Configs cfg = Cfg.V;
            InfinityCfg ic = cfg.Infinity;
            bool active = GojoHub.AbilitiesActive && (ic == null || ic.Enabled);
            EnsureInit(hero);

            if (_blockFlash > 0.0) _blockFlash -= dt;
            if (_breakFlash > 0.0) _breakFlash -= dt;
            TickBreakPenalty(dt, ic);

            // 迟滞的尾巴计时：每个敌人自己衰减，离开领域后平滑恢复
            SlowAura.Tick(dt);
            TickRadiusCache(dt);

            if (active && ic != null)
            {
                double regen = ic.RegenPerSecond
                               * (_breakPenalty > 0.0 ? ic.BreakRegenFactor : 1.0);
                _reserve += regen * dt;
                if (_reserve > _maxReserve) _reserve = _maxReserve;
            }

            if (!active)
            {
                DisposeLight();
                return;
            }

            _breath += dt;
            if (_breath > 3600.0) _breath -= 3600.0;

            UpdateLight(hero, ic);
            UpdateField(hero, dt, ic);
        }

        /// <summary>迟滞领域 + 湮灭弹幕（每 0.1 秒一次，避免每帧碰所有实体）。</summary>
        private void UpdateField(Hero hero, double dt, InfinityCfg ic)
        {
            _slowTimer += dt;
            if (_slowTimer < SlowRefreshInterval) return;
            _slowTimer = 0.0;

            double radiusTiles = RadiusTiles(hero);
            double radiusPx = radiusTiles * GojoUtil.TILE_PX;
            bool eraseBullets = ic == null || ic.EraseBullets;

            if (hero._level?.entities == null) return;

            double minMul = ic != null ? ic.SlowMinMultiplier : 0.25;
            double linger = ic != null ? ic.SlowLinger : 0.30;

            int slowed = 0;

            // 先快照：destroy() 会改动实体表
            var mobs = GojoUtil.SnapshotEnemies(hero);

            foreach (Mob mob in mobs)
            {
                double dist = GojoUtil.DistancePx(hero, mob);
                if (double.IsNaN(dist) || dist > radiusPx) continue;

                // 里层最慢、外圈线性回升（平方衰减：越近越离谱）
                double ratio = 1.0 - dist / radiusPx;
                double falloff = ratio * ratio;
                double mul = 1.0 - (1.0 - minMul) * falloff;

                SlowAura.Mark(mob, mul, SlowRefreshInterval * 2.5 + linger);
                slowed++;
                _slowedTotal++;
            }

            if (eraseBullets)
            {
                EraseBullets(hero, radiusPx);
            }

            if (slowed != _lastLoggedSlows)
            {
                _lastLoggedSlows = slowed;
                if (slowed > 0)
                {
                    Log.Debug($"迟滞领域中敌人 {slowed} 个（半径 {radiusTiles:0.#} 格，"
                              + $"最慢 ×{minMul:0.##}）");
                }
            }

            // HUD：把"当前屏障状态"实时喂给它（这些是不值得写日志文件的高频信息）
            Log.Hud(BuildHudStatus(radiusTiles, minMul));
        }

        /// <summary>拼一行给 HUD 的无下限状态。</summary>
        private static string BuildHudStatus(double radiusTiles, double minMul)
        {
            string s = $"无下限 半径{radiusTiles:0.#}格  最慢×{minMul:0.##}  挡下{_blockedCount} 穿透{_brokenCount}";
            int slowed = SlowAura.ActiveCount;
            if (slowed > 0) s += $"  迟滞{slowed}";
            if (_breakPenalty > 0.0) s += $"  破裂中{_breakPenalty:0.0}s";
            return s;
        }

        /// <summary>湮灭飞进半径的敌方子弹。</summary>
        private static void EraseBullets(Hero hero, double radiusPx)
        {
            dc.hl.types.ArrayObj entities;
            try { entities = hero._level?.entities; }
            catch { return; }
            if (entities == null) return;

            System.Collections.Generic.List<Bullet> victims = null;
            int len = entities.length;
            for (int i = 0; i < len; i++)
            {
                object raw;
                try { raw = entities.getDyn(i); }
                catch { break; }

                if (raw is not Bullet b) continue;
                if (b.destroyed) continue;

                try
                {
                    if (!hero.isOpponent(b)) continue;
                }
                catch { continue; }

                double dist = GojoUtil.DistancePx(hero, b);
                if (double.IsNaN(dist) || dist > radiusPx) continue;

                (victims ??= new System.Collections.Generic.List<Bullet>()).Add(b);
            }

            if (victims == null) return;

            foreach (Bullet b in victims)
            {
                try
                {
                    if (b.destroyed) continue;
                    b.vanish();          // 走原版消失流程（尾巴特效/弹药回收都正常）
                    _erasedBullets++;
                }
                catch
                {
                    try { b.destroy(); } catch { }
                }
            }

            // 每 25 发报一次（用"跨过 25 的倍数"判断，避免刷屏）
            if (_erasedBullets / 25 != _lastLoggedErased)
            {
                _lastLoggedErased = _erasedBullets / 25;
                _Status = $"湮灭弹幕累计 {_erasedBullets} 发";
                Log.Hud(_Status);
            }
        }

        // ------------------------------------------------------------ 光圈（呼吸 / 变色）

        /// <summary>呼吸相位（秒）。</summary>
        private static double _breath;

        /// <summary>上次设过的光圈颜色（避免每帧都 setColor → needUpdate）。</summary>
        private static int _lastLightColor = int.MinValue;

        private static void UpdateLight(Hero hero, InfinityCfg ic)
        {
            bool wantFx = ic == null || ic.AuraFx;

            if (!wantFx)
            {
                DisposeLight();
                return;
            }

            if (!_lightTried)
            {
                _lightTried = true;
                try
                {
                    _light = hero.createLight(GojoPalette.Infinity, 0.85, LightNameRadiusCase, 0.55);
                    Log.Info("无下限光圈已挂载（createLight）");
                }
                catch (Exception ex)
                {
                    Log.Warn("无下限光圈创建失败（不影响判定）: " + ex.Message);
                    _light = null;
                }
            }

            if (_light == null || _light.killed) return;

            try
            {
                double baseR = SysMath.Max(2.0, RadiusTiles(hero));
                double breathAmp = ic != null ? ic.AuraBreath : 1.0;
                bool inDomain = UnlimitedVoidAbility.IsActive;

                // ---- 呼吸：半径 ±20% × breathAmp，相位 0.9 Hz ----
                double wave = SysMath.Sin(_breath * 2.0 * SysMath.PI * 0.9);
                double radiusMul = 1.0 + 0.20 * breathAmp * wave;
                if (radiusMul < 0.4) radiusMul = 0.4;

                _light.l.set_range(baseR * radiusMul * GojoUtil.TILE_PX);

                // ---- 强度 ----
                double intensity = 0.75 + 0.20 * breathAmp * wave;
                if (_blockFlash > 0.0) intensity = 2.2;
                if (_breakFlash > 0.0) intensity = 0.25;
                if (inDomain) intensity *= 1.35;          // 领域期间更亮更"厚"
                _light.intensity = intensity;
                _light.l.shader.intensity__ = intensity * _light.globalAlpha;

                // ---- 颜色 ----
                // 碎裂闪红 > 领域变紫黑 > 咒力低变红 > 正常青蓝
                int col;
                if (_breakFlash > 0.0)
                {
                    col = GojoPalette.Red;
                }
                else if (inDomain)
                {
                    col = GojoPalette.Void;
                }
                else
                {
                    double lowRatio = ic != null ? ic.AuraLowReserveRatio : 0.30;
                    double ratio = _maxReserve > 0.0 ? _reserve / _maxReserve : 1.0;
                    col = ratio <= lowRatio ? GojoPalette.Red : GojoPalette.Infinity;
                }

                // ⚠️ 必须走 PointLight.setColor（内部会置 needUpdate=true）——
                //    直接改 l.color 这个 Vector 的话，PointLight.sync() 里的
                //    `if (needUpdate) { volume.col = color; ... }` 不会触发，
                //    颜色改了也白改（PointLight.cs:50-55 与 154-169）。
                if (col != _lastLightColor)
                {
                    _lastLightColor = col;
                    _light.l.setColor(col);
                }
            }
            catch { }
        }

        private static void DisposeLight()
        {
            if (_light == null) return;
            try { _light.dispose(); } catch { }
            _light = null;
            _lightTried = false;
        }

        // ------------------------------------------------------------ 参数 / 复位

        // 半径缓存：光照每帧都要问半径，而读配置要走一层 Config<T>；
        // 缓存 0.5 秒 —— 菜单里调半径时最多滞后半秒，但完全没有每帧开销。
        private const double RadiusCacheSeconds = 0.5;
        private static double _radiusCache = -1.0;
        private static double _radiusCacheAge = 999.0;

        /// <summary>屏障半径（格）。**纯配置** —— 自建道具已删除，不再从道具读。</summary>
        public static double RadiusTiles(Hero hero)
        {
            if (_radiusCache > 0.0 && _radiusCacheAge < RadiusCacheSeconds) return _radiusCache;

            double r = Cfg.V.Infinity != null ? Cfg.V.Infinity.Radius : 6.0;
            if (r < 0.5) r = 0.5;

            _radiusCache = r;
            _radiusCacheAge = 0.0;
            _ = hero;
            return r;
        }

        /// <summary>半径缓存的计时推进（每帧一次）。</summary>
        private static void TickRadiusCache(double dt)
        {
            if (_radiusCacheAge < 1000.0) _radiusCacheAge += dt;
        }

        private static void EnsureInit(Hero hero)
        {
            if (_maxReserve < 0.0)
            {
                // 没有"英雄等级"这种字段，用三个属性等级之和近似进度（和原版 Power 的算法一致）
                int tiers = 0;
                try { tiers = hero.brutalityTier + hero.survivalTier + hero.tacticTier; } catch { }

                InfinityCfg ic = Cfg.V.Infinity;
                double baseReserve = ic != null ? ic.Reserve : 420.0;
                double perLevel = ic != null ? ic.ReservePerLevel : 12.0;

                _maxReserve = baseReserve + perLevel * SysMath.Max(0, tiers);
                _reserve = _maxReserve;
                if (!_logged)
                {
                    _logged = true;
                    Log.Info($"无下限屏障就绪：储量上限 {_maxReserve:0}（属性等级和 {tiers}），"
                             + $"回充 {(ic != null ? ic.RegenPerSecond : 0.0):0}/s");
                }
            }
        }

        /// <summary>重置储量（配置改了上限之后调一次，让它下一帧按新上限重算）。</summary>
        public static void InvalidateReserve()
        {
            _maxReserve = -1.0;
            _logged = false;
        }

        public static void ResetAll()
        {
            _maxReserve = -1.0;
            _reserve = 0.0;
            _breakPenalty = 0.0;
            _blockFlash = 0.0;
            _breakFlash = 0.0;
            _slowTimer = 0.0;
            _breath = 0.0;
            _blockedCount = 0;
            _brokenCount = 0;
            _erasedBullets = 0;
            _slowedTotal = 0;
            _lastLoggedBlocks = -100;
            _lastLoggedSlows = -1000;
            _lastLoggedErased = 0;
            _logged = false;
            _radiusCache = -1.0;
            _radiusCacheAge = 999.0;
            _lastLightColor = int.MinValue;
            SlowAura.Reset();
            DisposeLight();
        }

        public static void Shutdown() => DisposeLight();

        /// <summary>把统计信息写进日志（换关时调一次）。</summary>
        public static void LogSummary()
        {
            if (_blockedCount == 0 && _brokenCount == 0 && _erasedBullets == 0 && _slowedTotal == 0) return;
            Log.Info($"本关无下限统计：挡下 {_blockedCount} 次，被打穿 {_brokenCount} 次，"
                     + $"湮灭弹幕 {_erasedBullets} 发，迟滞累计 {_slowedTotal} 次");
        }

        private static void LogThrottled()
        {
            if (_blockedCount - _lastLoggedBlocks < 10) return;
            _lastLoggedBlocks = _blockedCount;
            Log.Debug($"无下限挡下第 {_blockedCount} 次攻击（剩余储量 {_reserve:0}/{_maxReserve:0}）");
        }

        /// <summary>供调试：当前储量。</summary>
        public static double Reserve => _reserve;

        public static double MaxReserve => _maxReserve;
    }
}
