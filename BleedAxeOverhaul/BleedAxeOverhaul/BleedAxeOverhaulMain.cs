#nullable disable

using dc;
using dc.en;
using dc.en.bu;
using dc.hl.types;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using Hashlink.Proxy.Objects;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BleedAxeOverhaul
{
    /// <summary>
    /// BleedAxe（流血斧 / Hache sanglante）强化模组
    /// =====================================================================
    /// 需求与实现对照：
    ///
    ///   1) 一次丢出十把斧子
    ///      → Hook_BleedAxeWeapon.onExecute：原版跑完后补 9 把克隆斧，
    ///        10 把一起铺在 ±FAN/2 的扇面里（原版那把正好落在扇面正中）。
    ///
    ///   2) 无视墙体
    ///      → Hook_Bullet.fixedUpdate 里对每把 BleedAxeBullet 设 ignoreWalls/ignoreOneWays；
    ///        再 Hook_Bullet.blockOnCollision 把撞墙分支整个跳过。
    ///        （只设 ignoreWalls 不够：Bullet.onTouchGround() 是**无条件**调
    ///          blockOnCollision() 的，落墙照样走 onHitWall → block(false) → 销毁。）
    ///
    ///   3) 自动追击敌人
    ///      → Hook_Bullet.fixedUpdate：每帧把速度矢量 (dx,dy) 朝最近的敌人
    ///        限速转一点（保持速率不变），与游戏自带 HomingSoul 同思路。
    ///        索敌用游戏自己的 TargetHelper（Team.get_targetHelper()），
    ///        刻意**不**调 filterBySight —— 斧子已经穿墙，隔墙锁敌才一致。
    ///
    ///   4) 范围 ×2
    ///      → 原版 maxDist 是 BleedAxeWeapon.onExecute 里算的常量：
    ///            int num = 11;  num4 = num * 0.66;  maxDist = num4 * 24 = 174.24px
    ///        所以由代码侧对每把斧子的 maxDist ×2（= 7.26 格 → 14.52 格）。
    ///        和数值开关联动：关掉 Range 就把 maxDist 还原成原版值。
    ///
    ///   5) 伤害 ×2
    ///      → 由 res.pak 数据补丁实现（patch_bleedaxe_data.py 把 item.props.dps ×2）。
    ///        反编译证据：BleedAxeWeapon 的命中闭包用
    ///            e.setAffectS(89, duration, ref scaleHeroValueToTier(props.dps, tier), null)
    ///        所以 props.dps 就是斧子唯一的伤害基数，×2 即命中伤害 ×2。
    ///        本文件只负责 EnableDamage 开关：关掉时运行期把 dps 切回原版值。
    ///
    ///   6) 撞击伤害沿用原版
    ///      → 克隆斧的 onBulletHit / beforeHit 回调用的是原版同一套 affect：
    ///            affect 89 = 斧子伤害（base = scaleHeroValueToTier(props.dps, tier) × (1 + getDamageBonus())）
    ///            affect  8 = 眩晕（23 递减）
    ///            affect 23 = 击退位移（原版 get_curSkillInf().props.bump）
    ///        数值全部从「武器 + 物品数据」现算，不依赖原版那把斧子的存活状态。
    ///
    ///   7) 扇形角度为什么用 atan2 反推
    ///      → 原版传进来的 ang 是 (0 或 π)，必须用 Hero.dir 判左右：
    ///            double baseAng = (hero.dir >= 0) ? 0.0 : Math.PI;
    ///        （照抄 BleedAxeWeapon.onExecute；原版写的是 num = (dir != 1) ? 3.14 : 0.0）
    ///
    ///   8) 弹药
    ///      → 只有原版那把走 consumeAmmo()（在 orig 里），克隆斧**不消耗弹药**；
    ///        BleedAxe 的 CDB 带 AmmoComesBackImmediately，斧子回手即回弹药。
    ///
    /// 【总开关】仿 DamageAuraBoost 的 AuraFeatures：
    ///   开关状态存在 coremod/config/BleedAxeOverhaul.json，
    ///   可在 游戏内「选项 → 模组 → 流血斧强化」里切换，或配置热键（默认不绑键）。
    /// =====================================================================
    public class BleedAxeOverhaulMain : ModBase, IOnGameExit, IOnAfterLoadingAssets, IOnHeroUpdate,
                                       IOnGameInit, IModMenu
    {
        // ============================ 可调参数 ============================

        /// <summary>一次丢出的斧子总数（需求：十把）。</summary>
        private const int AXE_COUNT = 10;

        /// <summary>射程倍率（需求：原版二倍）。</summary>
        private const double RANGE_MULT = 2.0;

        /// <summary>伤害倍率 —— 只用于开关还原时复算，真实值由 res.pak 数据补丁写入。</summary>
        private const double DAMAGE_MULT = 2.0;

        /// <summary>索敌半径（格）。穿墙斧也给一个有限的锁定范围，避免满地图乱拐。</summary>
        private const double HOMING_RANGE_CELLS = 16.0;

        /// <summary>每固定帧最大转向弧度（0.22 rad ≈ 12.6°；60fps 下约 756°/秒）。</summary>
        private const double HOMING_MAX_TURN = 0.22;

        /// <summary>日志采样上限，避免刷屏。</summary>
        private const int LOG_SAMPLES = 8;

        // ===== 原版数值（游戏 v35 模板 data.cdb，用于数值开关关闭时还原）=====
        private const double VanillaDps = 25.0;
        private const double VanillaDuration = 3.0;

        /// <summary>原版 maxDist = 11 × 0.66 × 24 = 174.24 px（= 7.26 格）。</summary>
        private const double VanillaMaxDist = 11.0 * 0.66 * 24.0;

        /// <summary>克隆斧的 onDispose 回调（只登记一次，避免重复分配）。
        /// 注意：这是 **Hook 委托**（签名带 orig，用于 Hook_Bullet.onDispose），
        /// 与 Bullet.onDispose 那个 HaxeProxy 的 HlAction 不是一回事。</summary>
        private Hook_Bullet.hook_onDispose _onDisposeHook;

        // ============================ 运行状态 ============================

        private sealed class BulletStat
        {
            /// <summary>第一帧的一次性初始化（穿透上限等）是否已做。</summary>
            public bool Initialized;

            /// <summary>这把斧子的 maxDist 当前处在哪一档（null = 还没记过基准值）。</summary>
            public bool? RangeBoosted;

            /// <summary>原版 maxDist 基准（= onExecute 里写的 174.24px，供还原用）。</summary>
            public double VanillaMaxDist = BleedAxeOverhaulMain.VanillaMaxDist;
        }

        private readonly Dictionary<int, BulletStat> _bullets = new();

        /// <summary>克隆斧光照半径的随机扰动用（原版用 _Math.random()，这里用 .NET 的等价物）。</summary>
        private static readonly Random _random = new();

        // 强化数值缓存：首次从 res.pak 应用后的物品数据里读取，避免与脚本常量重复维护
        private double _boostDps = VanillaDps * DAMAGE_MULT;
        private double _boostDuration = VanillaDuration * 2.0;
        private bool _boostCaptured;

        /// <summary>当前「伤害强化」是否已应用到 item 数据（null = 还没应用过）。</summary>
        private bool? _damageAppliedState;

        private int _fanSpawns;      // 已发动的「十把斧」次数
        private int _clonesSpawned;  // 已生成的克隆斧总数
        private int _tracked;        // 已登记的斧子
        private int _homingTurns;    // 已发生的索敌转向次数
        private int _failures;       // 异常次数

        private string _lastDebug;

        public BleedAxeOverhaulMain(ModInfo info) : base(info) { }

        /// <summary>同时写控制台与模组日志文件（coremod/logs/log_latest.log）。</summary>
        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        // ==================================================================
        //  初始化 / 卸载
        // ==================================================================

        public override void Initialize()
        {
            base.Initialize();

            // ① 十把斧子：原版 onExecute 跑完后补 9 把克隆
            Try("Hook_BleedAxeWeapon.onExecute", () =>
                Hook_BleedAxeWeapon.onExecute += OnBleedAxeExecute);

            // ② 穿墙（落墙路径）+ ③ 索敌 + ④ 射程：全部挂在 Bullet 基类的唯一出口上
            Try("Hook_Bullet.fixedUpdate", () =>
                Hook_Bullet.fixedUpdate += OnBulletFixedUpdate);

            // ② 穿墙（撞墙路径）：Bullet.onTouchGround() 无条件调 blockOnCollision()
            Try("Hook_Bullet.blockOnCollision", () =>
                Hook_Bullet.blockOnCollision += OnBulletBlockOnCollision);

            // ② 兜底：某些路径会直接调 onHitWall()
            Try("Hook_Bullet.onHitWall", () =>
                Hook_Bullet.onHitWall += OnBulletHitWall);

            // 斧子回收时清掉追踪表，避免字典无限增长
            _onDisposeHook = OnBulletDispose;
            Try("Hook_Bullet.onDispose", () =>
                Hook_Bullet.onDispose += _onDisposeHook);

            Write($"[BleedAxeOverhaul] 已加载 —— 一次 {AXE_COUNT} 把 / 穿墙 / 索敌 / " +
                  $"范围 ×{RANGE_MULT:0.##} / 伤害 ×{DAMAGE_MULT:0.##}（伤害走 res.pak 数据补丁）");
        }

        /// <summary>挂 Hook 的包一层，单独失败不影响其它 Hook。</summary>
        private void Try(string what, Action act)
        {
            try { act(); }
            catch (Exception ex)
            {
                _failures++;
                Write($"[BleedAxeOverhaul] {what} 挂载失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_BleedAxeWeapon.onExecute -= OnBleedAxeExecute; } catch { }
            try { Hook_Bullet.fixedUpdate -= OnBulletFixedUpdate; } catch { }
            try { Hook_Bullet.blockOnCollision -= OnBulletBlockOnCollision; } catch { }
            try { Hook_Bullet.onHitWall -= OnBulletHitWall; } catch { }
            try { if (_onDisposeHook != null) Hook_Bullet.onDispose -= _onDisposeHook; } catch { }

            _bullets.Clear();
            BleedAxeFeatures.ResetHotkeyState();

            Write($"[BleedAxeOverhaul] 游戏退出（十把斧 {_fanSpawns} 次 / 克隆 {_clonesSpawned} 把 / " +
                  $"追踪 {_tracked} / 索敌转向 {_homingTurns} / 异常 {_failures}）");
        }

        /// <summary>资源加载完成：手动加载模组自带的 res.pak（dps ×2 数据补丁）。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(BleedAxeOverhaulMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(pakPath.AsHaxeString());
                    Write($"[BleedAxeOverhaul] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Write($"[BleedAxeOverhaul] 未找到 res.pak: {pakPath}（伤害 ×2 不会生效）");
                }
            }
            catch (Exception ex)
            {
                Write($"[BleedAxeOverhaul] res.pak 加载失败: {ex.Message}");
            }
        }

        /// <summary>开局自检：写下当前开关状态，并把默认配置落盘。</summary>
        void IOnGameInit.OnGameInit()
        {
            try
            {
                var cfg = BleedAxeKeys.Config.Value;
                Write("[BleedAxeOverhaul] 开关状态 —— " +
                      $"总开关={cfg.EnableMod} 十把斧={cfg.EnableMultiThrow} 穿墙={cfg.EnableWallPierce} " +
                      $"索敌={cfg.EnableHoming} 范围×2={cfg.EnableRange} 伤害×2={cfg.EnableDamage}");
                Write($"[BleedAxeOverhaul] 配置文件: {BleedAxeKeys.Config.ConfigPath}" +
                      "（也可在游戏的 选项 → 模组 → 流血斧强化 菜单里直接改）");
                try { BleedAxeKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Write($"[BleedAxeOverhaul] 写配置失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 每帧
        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            try { BleedAxeFeatures.PollHotkeys(Write); } catch { }

            // 伤害开关 ↔ item 数据 同步（开关变化时即时生效，之后重新丢出的斧子读新数值）
            SyncDamage();
        }

        // ==================================================================
        //  ① 十把斧子：Hook_BleedAxeWeapon.onExecute
        // ==================================================================

        /// <summary>
        /// 原版 onExecute 只 new 一把 BleedAxeBullet。这里在它跑完之后，
        /// 用**完全相同的参数**再补 AXE_COUNT-1 把，并把 10 把均匀铺在扇面里。
        ///
        /// 为什么可以这样补：每把 BleedAxeBullet 都是独立实体，
        /// onExecute 里对原版那把做的事（init / enableHeroFalling / shootFromWeapon /
        /// maxDist / tail / 光照 / 命中回调）这里逐项等价复刻一遍。
        /// </summary>
        private bool OnBleedAxeExecute(Hook_BleedAxeWeapon.orig_onExecute orig, BleedAxeWeapon self)
        {
            bool ret = orig(self);

            if (!BleedAxeFeatures.IsOn(BleedAxeFeature.MultiThrow)) return ret;

            try
            {
                Hero hero = self.owner;
                if (hero == null || hero.destroyed || hero.life <= 0) return ret;
                if (self.item == null) return ret;

                ItemProps props = ReadProps(self);
                if (props == null) return ret;

                var skillInf = self.get_curSkillInf();
                if (skillInf == null) return ret;

                int colorIn = NullableToInt(skillInf.fxProps?.fxInnerColor, DefaultGlowColor);
                int colorOut = NullableToInt(skillInf.fxProps?.fxOuterColor, DefaultGlowColor);

                // 原版：num3 = (dir != 1) ? 3.14 : 0.0；这里用 Hero.dir 判左右，语义一致
                double baseAngle = (hero.dir >= 0) ? 0.0 : System.Math.PI;
                double totalFan = BleedAxeFeatures.FanTotalRadians();
                double step = AXE_COUNT > 1 ? totalFan / (AXE_COUNT - 1) : 0.0;
                double halfCount = (AXE_COUNT - 1) / 2.0;

                int made = 0;
                for (int i = 0; i < AXE_COUNT; i++)
                {
                    double ang = baseAngle + (i - halfCount) * step;
                    try
                    {
                        SpawnAxe(hero, self, ang, props, colorIn, colorOut);
                        made++;
                    }
                    catch (Exception ex)
                    {
                        Fail($"补第 {i + 1} 把斧子失败", ex);
                    }
                }

                _fanSpawns++;
                _clonesSpawned += made;
                if (_fanSpawns <= LOG_SAMPLES)
                {
                    Debug($"[BleedAxeOverhaul] 十把斧 #{_fanSpawns}：共 {made} 把、扇面 " +
                          $"{Deg(totalFan):0.#}°、单把间隔 {Deg(step):0.##}°、" +
                          $"dps={props.Dps:0.##} speed={props.Speed:0.###}");
                }
            }
            catch (Exception ex)
            {
                Fail("十把斧生成失败", ex);
            }

            return ret;
        }

        /// <summary>
        /// 造一把与 onExecute 里那把等价的流血斧。
        /// 逐行对应原版 onExecute 第 375~437 行。
        /// </summary>
        private void SpawnAxe(Hero hero, BleedAxeWeapon axe, double ang,
                              ItemProps props, int colorIn, int colorOut)
        {
            // 原版：atk = _AttackUtils.createFromHeroWeapon(this, null)
            // 反编译里是 static，但 GameProxy 里只有 HlFunc 形态的实例包装，
            // 所以走 AttackUtils.Class.createFromHeroWeapon（与 ThrowableStuff / ShockerRing 一致）
            AttackData atk = AttackUtils.Class.createFromHeroWeapon.Invoke(axe, null);
            if (atk == null) return;

            BleedAxeBullet bullet = new BleedAxeBullet(hero, atk, ang, props.Speed, colorIn, colorOut);
            bullet.init();

            // 原版：enableHeroFalling(0.5)
            bullet.enableHeroFalling(0.5);

            // 原版：shootFromWeapon(this, ...) —— 用武器口把原点摆到英雄手上
            bullet.shootFromWeapon(axe, Ref<bool>.Null, Ref<double>.Null, Ref<double>.Null);

            // 原版：maxDist = (11 * 0.66) * 24
            bullet.maxDist = props.VanillaDistance;

            // 原版：tail = new BulletTail.Line(glowColor, 0.4, null)
            bullet.tail = new BulletTail.Line(colorIn, 0.4, null);

            // 原版：createLight(glowColor, 1.2 * gravMul, 5 * (0.9 + rand*0.2), 0.35)
            double gravMul = 2.0;                       // 原版 endGravMul 默认 2.0
            double intensity = 1.2 * gravMul;
            double radiusCase = 5.0 * (0.9 + _random.NextDouble() * 0.2);
            bullet.createLight(colorIn, intensity, radiusCase, 0.35);

            // 命中回调：等价复刻原版 beforeHit / onBulletHit 闭包（ArrowFunctionEntry_36229）
            HlAction<Entity, AttackData> onHit = MakeHitCallback(axe, props);
            bullet.beforeHit = onHit;
            bullet.onBulletHit = onHit;

            // 原版 BleedAxeWeapon.onExecute 没设 onBulletKill，而
            // Bullet.onTouchValidTarget 在「这一击把敌人打死」时会无条件 Invoke 它。
            // 克隆斧这里复用同一套命中结算：纯粹是防御性的，
            // 因为 onBulletKill 只在致死那一帧触发，不会造成重复叠伤。
            bullet.onBulletKill = onHit;

            // 回收时的清理由全局的 Hook_Bullet.onDispose 负责（见 OnBulletDispose），
            // 不需要给每把克隆斧单独挂 onDispose。
        }

        /// <summary>
        /// 复刻 BleedAxeWeapon.onExecuteContext_22794.ArrowFunctionEntry_36229：
        ///   击中时 → 免费血花特效 + 击退 + affect 8（眩晕）+ affect 89（斧子伤害）
        /// 数值全部从「武器 + 物品数据」现算，不引用原版那把斧子。
        /// </summary>
        private HlAction<Entity, AttackData> MakeHitCallback(BleedAxeWeapon axe, ItemProps props)
        {
            return new HlAction<Entity, AttackData>((dc.Entity e, AttackData a) =>
            {
                try
                {
                    if (a == null || !a.isSuccess()) return;
                    if (e == null || e.destroyed) return;
                    if (axe.owner?._level == null) return;

                    // --- 血花（fx.bloodHitFree(e 中心, 子弹 dir)）---
                    dc.Fx fx = axe.owner._level.fx;
                    double hx = ((double)e.cx + e.xr) * 24.0;
                    double hy = ((double)e.cy + e.yr) * 24.0 - e.hei * 0.5;
                    fx.bloodHitFree(hx, hy, axe.owner.dir);

                    // --- 击退：bump(dir * props.bump, 0) ---
                    double bump = props.Bump;
                    if (bump != 0.0)
                    {
                        e.bump(axe.owner.dir * bump, 0.0, null);
                    }

                    // --- affect 8 = 眩晕（原版 getDiminishingFactor(23, 1, 5) × props.duration）---
                    double stunSec = props.Duration * e.getDiminishingFactor(23, 1, 5, null);
                    e.setAffectS(8, stunSec, Ref<double>.Null, null);

                    // --- affect 89 = 斧子伤害 ---
                    // 原版：dx = props.duration; xr = scaleHeroValueToTier(props.dps, tier) * (1 + getDamageBonus())
                    double dmg = ScaleDps(props.Dps, axe);
                    double bonus = 1.0 + axe.item.getDamageBonus();
                    dmg *= bonus;
                    e.setAffectS(89, props.Duration, Ref<double>.From(ref dmg), null);
                }
                catch (Exception ex)
                {
                    Fail("斧子命中结算失败", ex);
                }
            });
        }

        /// <summary>复刻 `_Const.scaleHeroValueToTier(props.dps, owner.getRelevantTierFor(item))`。</summary>
        private double ScaleDps(double dps, Weapon w)
        {
            try
            {
                return dc.Const.Class.scaleHeroValueToTier.Invoke(dps, w.owner.getRelevantTierFor(w.item));
            }
            catch (Exception ex)
            {
                Fail("dps 缩放失败，改用原值", ex);
                return dps;
            }
        }

        // ==================================================================
        //  ②③④ 每把斧子：穿墙 / 索敌 / 射程 / 伤害开关
        // ==================================================================

        /// <summary>
        /// 挂在 Bullet 基类的唯一 fixedUpdate 槽上（子类 BleedAxeBullet.fixedUpdate
        /// 只是 `base.fixedUpdate()`，挂子类不保证被调到 —— 见 SonicCrossbowOverhaul 的踩坑记录）。
        ///
        /// 放在 orig 之前：让本帧的穿墙 / 索敌立刻生效（原版 fixedUpdate 会读 ignoreWalls）。
        /// </summary>
        private void OnBulletFixedUpdate(Hook_Bullet.orig_fixedUpdate orig, Bullet self)
        {
            if (self is BleedAxeBullet axe && !axe.destroyed)
            {
                try { ApplyPerFrame(axe); }
                catch (Exception ex) { Fail("斧子逐帧改写失败", ex); }
            }

            orig(self);
        }

        private void ApplyPerFrame(BleedAxeBullet axe)
        {
            int key = RuntimeHelpers.GetHashCode(axe);
            if (!_bullets.TryGetValue(key, out BulletStat st))
            {
                st = new BulletStat();
                _bullets[key] = st;
                _tracked++;
            }

            // ---- ② 穿墙：ignoreWalls 会让 Bullet.fixedUpdate 的墙体分支直接跳过，
            //      ignoreOneWays 一并打开，避免被单向平台挡住 ----
            if (BleedAxeFeatures.IsOn(BleedAxeFeature.WallPierce))
            {
                if (!axe.ignoreWalls) axe.ignoreWalls = true;
                if (!axe.ignoreOneWays) axe.ignoreOneWays = true;
            }

            if (!st.Initialized)
            {
                st.Initialized = true;

                // 穿透上限：斧子命中敌人后继续飞，不会一撞就消失
                if (axe.pierceCount < 1) axe.pierceCount = int.MaxValue;

                // ---- ④ 射程：记下这一刻的原版基准值（此时原版 onExecute 已经写过 maxDist）----
                if (axe.maxDist > 0.0)
                {
                    st.VanillaMaxDist = axe.maxDist;
                    st.RangeBoosted = false;
                }
            }

            // ---- ④ 射程开关实时联动：先按原版基准还原，需要强化再乘一次 ----
            // （用「基准值 × 倍率」而不是「当前值 × 倍率」，所以反复开关不会越乘越大）
            if (st.RangeBoosted.HasValue && st.VanillaMaxDist > 0.0)
            {
                bool want = BleedAxeFeatures.IsOn(BleedAxeFeature.Range);
                if (st.RangeBoosted.Value != want || axe.maxDist <= 0.0)
                {
                    axe.maxDist = want ? st.VanillaMaxDist * RANGE_MULT : st.VanillaMaxDist;
                    st.RangeBoosted = want;
                }
            }

            // ---- ③ 索敌 ----
            if (BleedAxeFeatures.IsOn(BleedAxeFeature.Homing))
            {
                HomeAxe(axe);
            }
        }

        /// <summary>
        /// 核心转向：把速度矢量 (dx,dy) 朝最近敌人限速转一点、保持速率不变
        /// （与游戏自带 HomingSoul.fixedUpdate 同思路，多一个每帧转角上限）。
        /// </summary>
        private void HomeAxe(BleedAxeBullet axe)
        {
            double vx = axe.dx + axe.bdx;
            double vy = axe.dy + axe.bdy;
            double speed = System.Math.Sqrt(vx * vx + vy * vy);
            if (!(speed > 0.0)) return;

            dc.Entity target = FindTarget(axe);
            if (target == null || target.destroyed || target.life <= 0) return;

            // 敌人身体中心（与 Homing / HomingSoul 取法一致）
            double tx = ((double)target.cx + target.xr) * 24.0;
            double ty = ((double)target.cy + target.yr) * 24.0 - target.hei * 0.5;
            double sx = ((double)axe.cx + axe.xr) * 24.0;
            double sy = ((double)axe.cy + axe.yr) * 24.0 - axe.hei * 0.5;

            double cur = System.Math.Atan2(vy, vx);
            double want = System.Math.Atan2(ty - sy, tx - sx);

            double turn = NormalizeAngle(want - cur);
            if (turn > HOMING_MAX_TURN) turn = HOMING_MAX_TURN;
            else if (turn < -HOMING_MAX_TURN) turn = -HOMING_MAX_TURN;

            double ang = cur + turn;
            axe.dx = System.Math.Cos(ang) * speed;
            axe.dy = System.Math.Sin(ang) * speed;

            _homingTurns++;
            if (_homingTurns <= LOG_SAMPLES)
            {
                Debug($"[BleedAxeOverhaul] 索敌转向 {Deg(turn):0.0}° → 目标 cx={target.cx} cy={target.cy}");
            }
        }

        /// <summary>
        /// 就近找一个怪当追踪目标。直接用游戏自己的 TargetHelper：
        /// `_team.get_targetHelper()` 返回本队（英雄队）的 helper，currents 由 Team 维护，
        /// 内容正是敌方目标列表（与 LanternOverhaul / ThrowableStuff 用的是同一套）。
        /// 刻意**不**调 filterBySight —— 斧子穿墙，隔墙索敌才一致。
        /// </summary>
        private static dc.Entity FindTarget(BleedAxeBullet axe)
        {
            Team team = axe._team;
            if (team == null) return null;

            TargetHelper helper = team.get_targetHelper().init(axe);
            helper.filterByDistance(HOMING_RANGE_CELLS, null, null);
            helper.filterUntargetables();
            helper.removeBreakables();
            helper.@is(dc.en.Mob.Class, 10.0);          // 怪物加权，优先锁怪

            dc.Entity best = helper.getBest();
            return (best is dc.en.Mob) ? best : null;   // 范围内没怪就不锁
        }

        /// <summary>把角度归一化到 [-π, π]，用于算最短转向方向。</summary>
        private static double NormalizeAngle(double a)
        {
            while (a < -System.Math.PI) a += System.Math.PI * 2.0;
            while (a > System.Math.PI) a -= System.Math.PI * 2.0;
            return a;
        }

        // ==================================================================
        //  ② 穿墙的另外两条路径
        // ==================================================================

        /// <summary>
        /// `Bullet.onTouchGround()` 是**无条件**调用 blockOnCollision() 的：
        ///     public override void onTouchGround() { base.onTouchGround(); blockOnCollision(); }
        /// 而 blockOnCollision() → onHitWall() → block(false) → destroy()，
        /// 所以光设 ignoreWalls 还不够 —— 斧子一旦落到墙/地面上照样被打掉。
        /// 这里直接跳过撞击处理，让它一路飞到底（超出 maxDist 时照常 reachMaxDist 回收）。
        /// 命中敌人不受影响 —— 走的是另一条 onTouchValidTarget() 路径。
        /// </summary>
        private void OnBulletBlockOnCollision(Hook_Bullet.orig_blockOnCollision orig, Bullet self)
        {
            if (self is BleedAxeBullet && BleedAxeFeatures.IsOn(BleedAxeFeature.WallPierce))
            {
                return;
            }
            orig(self);
        }

        /// <summary>兜底：某些路径会直接调 onHitWall()，同样对斧子跳过。</summary>
        private void OnBulletHitWall(Hook_Bullet.orig_onHitWall orig, Bullet self)
        {
            if (self is BleedAxeBullet && BleedAxeFeatures.IsOn(BleedAxeFeature.WallPierce))
            {
                return;
            }
            orig(self);
        }

        /// <summary>斧子回收 → 清掉追踪记录（字典不会无限增长）。</summary>
        private void OnBulletDispose(Hook_Bullet.orig_onDispose orig, Bullet self)
        {
            ForgetBullet(self);
            orig(self);
        }

        /// <summary>把一把斧子从追踪表里去掉。</summary>
        private void ForgetBullet(dc.Entity self)
        {
            try
            {
                if (self is BleedAxeBullet axe)
                {
                    _bullets.Remove(RuntimeHelpers.GetHashCode(axe));
                }
            }
            catch { }
        }

        // ==================================================================
        //  ⑤ 伤害 ×2 开关（数值本体由 res.pak 数据补丁写入）
        // ==================================================================

        /// <summary>
        /// 把 item.props.dps / props.duration 切到「强化值」或「原版值」。
        /// 强化值首次从 res.pak 应用后的数据里读取缓存；原版值用常量。
        /// 幂等：状态没变就直接返回；CDB 还没就绪就下一帧再试。
        /// </summary>
        private void SyncDamage()
        {
            bool want = BleedAxeFeatures.IsOn(BleedAxeFeature.Damage);
            if (_damageAppliedState == want) return;

            dynamic itemData = Data.Class.item.byId.get(ToHaxeString(ItemId));
            if (itemData == null || itemData.props == null) return;

            try
            {
                dynamic props = itemData.props;

                if (!_boostCaptured)
                {
                    try
                    {
                        dynamic dps0 = props.dps;
                        if (dps0 != null && dps0.length > 0) _boostDps = (double)dps0.getDyn(0);
                        else if (dps0 != null) _boostDps = (double)dps0;
                        _boostDuration = (double)props.duration;
                    }
                    catch { }
                    _boostCaptured = true;
                }

                ApplyDamage(props, want ? _boostDps : VanillaDps,
                                   want ? _boostDuration : VanillaDuration);

                _damageAppliedState = want;
                Debug($"[BleedAxeOverhaul] 伤害{(want ? "强化" : "还原")}: " +
                      $"dps={props.dps} duration={props.duration}");
            }
            catch (Exception ex)
            {
                Fail("伤害开关切换失败", ex);
            }
        }

        private static void ApplyDamage(dynamic props, double dps, double duration)
        {
            dynamic dpsArr = props.dps;
            if (dpsArr != null && dpsArr.length > 0)
            {
                dpsArr.setDyn(0, dps);
            }
            else
            {
                props.dps = dps;
            }
            props.duration = duration;
        }

        // ==================================================================
        //  选项菜单（IModMenu）——「选项 → 模组 → 流血斧强化」
        // ==================================================================

        /// <summary>这一页在「选项 → 模组」里的名字。</summary>
        public string GetName() => "流血斧强化";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("BLEED AXE OVERHAUL 设置"));
                b.createScroller(0.0);

                foreach (var f in BleedAxeFeatures.All)
                {
                    var feature = f;                       // 闭包捕获：别直接用循环变量
                    bool on = BleedAxeFeatures.RawGet(feature);
                    string key = BleedAxeFeatures.KeyName(feature);
                    string hint = BleedAxeFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(BleedAxeFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !BleedAxeFeatures.RawGet(feature);
                            BleedAxeFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                b.updateScroller();
                Write($"[BleedAxeOverhaul] 选项菜单已建立：{BleedAxeFeatures.All.Length} 个功能开关");
            }
            catch (Exception ex)
            {
                Write($"[BleedAxeOverhaul] 建立选项菜单失败: {ex.Message}");
            }
        }

        // ==================================================================
        //  工具
        // ==================================================================

        private const string ItemId = "BleedAxe";

        /// <summary>skillInf.fxProps 取不到颜色时的兜底光色（与 CDB 里斧子配色接近的青色）。</summary>
        private const int DefaultGlowColor = 10962048;

        /// <summary>从武器上把本次施放用到的数值一次性读出来，避免闭包里反复走代理。</summary>
        private sealed class ItemProps
        {
            public double Speed;
            public double Bump;
            public double Duration;
            public double Dps;
            /// <summary>原版 onExecute 里写死的 maxDist（(11 * 0.66) * 24）。</summary>
            public double VanillaDistance = VanillaMaxDist;
        }

        private static ItemProps ReadProps(BleedAxeWeapon axe)
        {
            var p = axe.itemInf?.props;
            if (p == null) return null;
            return new ItemProps
            {
                Speed = ToDouble(p.speed, 1.1),
                Bump = ToDouble(p.bump, 0.0),
                Duration = ToDouble(p.duration, VanillaDuration),
                Dps = ToDouble(p.dps, VanillaDps),
            };
        }

        /// <summary>props.dps 既可能是标量，也可能是长度 1 的 ArrayBytes_Float，两种都吃。</summary>
        private static double ToDouble(dynamic v, double fallback)
        {
            try
            {
                if (v == null) return fallback;

                // 数组形态（CDB 的 dps 列在多数武器上是 [v] ）
                if (v is ArrayObj arr)
                {
                    if (arr.length <= 0) return fallback;
                    return (double)arr.getDyn(0);
                }

                return (double)v;
            }
            catch { return fallback; }
        }

        private static int NullableToInt(int? v, int fallback) => v.HasValue ? v.Value : fallback;

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        private static string Deg(double rad) => (rad * 180.0 / System.Math.PI).ToString("0.0");

        /// <summary>带相邻去重的调试日志（避免刷屏）。</summary>
        private void Debug(string message)
        {
            if (message == _lastDebug) return;
            _lastDebug = message;
            Write(message);
        }

        private void Fail(string what, Exception ex)
        {
            _failures++;
            if (_failures <= 10) Write($"[BleedAxeOverhaul] {what}: {ex.Message}");
        }
    }
}
