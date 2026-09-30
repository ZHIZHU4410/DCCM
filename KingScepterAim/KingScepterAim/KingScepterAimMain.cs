#nullable disable

using dc;
using dc.en;
using dc.hl.types;
using dc.tool.atk;
using dc.tool.weap;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace KingScepterAim
{
    /// <summary>
    /// 国王权杖（KingScepter）自动索敌空袭模组
    /// ========================================
    /// 【原版行为】
    ///   攻击 → onExecute 冲刺（dashingF=10.5）→ KingScepter.fixedUpdate 里 checkHit 命中敌人
    ///   → jumpAndStartSpin(e)：spinning=true、spinCount=item.props.effectCharge(2)，
    ///     随后内部的 onOwnerTouch(e) 做两件事：
    ///       ① hero.bump(0, item.props.bump)（bump=-0.9，负值=向上）把英雄顶上天 —— 这就是"起跳"
    ///       ② _AttackUtils.hit(attackData, e) 结算**首次命中伤害**（此时 isFirstSpin 仍为 true，
    ///          函数末尾才置 false）—— 也就是玩家看到的"挥砍伤害"
    ///   下落中"碰到"敌人（原版每敌 0.2s 冷却外）→ 再走 onOwnerTouch：
    ///     spinCount--、再顶一次、此时 isFirstSpin=false → attackData.addTag(7)（暴击段）。
    ///   落地 → onOwnerTouchGround → stopSpin。
    ///
    /// 【本模组】
    ///   1) 取消挥砍伤害 + 暴击三倍：Hook Entity.applyAttackResult（伤害结算总入口，
    ///      Entity.applyAttackResult 内部是用 a.finalDmg 扣血的）：
    ///        · 权杖"挥砍/首次命中"那一下（isFirstSpin==true）→ 不调 orig，不结算伤害；
    ///        · 权杖暴击段（isFirstSpin==false，即踩到敌人那一下）→ 结算前把 a.finalDmg ×3；
    ///      来源识别优先用 AttackData.sourceWeapon is KingScepter。起跳/动画/音效全部照旧。
    ///   1b) IgnoreGlobalShield buff：起跳时给权杖 item 补上原版词缀 IgnoreGlobalShield。
    ///      _AttackUtils.createFromHeroWeapon 会 attackData.useItemAffixes(w.item) 继承武器词缀，
    ///      而 AttackTargetImpl 里带这个词缀就不会因目标护盾类 affect(28/29/86) 被 Block
    ///      → 权杖的每次攻击（含三倍暴击）都无视 GlobalShield。
    ///   2) 起跳瞬间全图索敌（SearchRadiusTiles = 0 即不限距离），取最近的敌人。
    ///   3) 腾空期间接管位移：保持原速度直线飞向目标头顶，CollisionMode.IgnoreWalls 穿墙。
    ///   4) 滞空时间：每次起跳后先 HangSec(0.2s) 悬停（保住向上的起跳弧线），之后下落速度被
    ///      限制在 MaxFallCasesPerFrame 以内 —— 比原版 1.1 倍复利暴跌短促可控。
    ///   5) 一一打败所有敌人：spinCount 续期 → 踩到敌人暴击后**不换目标**，就留在这只身上
    ///      连踩到它死；它死后自动改锁全图最近的下一只，如此往复直到清空。
    ///      盯着一只超过 TargetGiveUpSec 还踩不到（在封闭区域够不到）就拉黑跳过，防止卡死。
    ///      腾空总时限 MaxAirborneSec 仅作泄漏兜底；清空/够不到才收手落地。
    ///
    /// 【关键单位（踩过的坑）】
    ///   原版实体碰撞（dc.pr.Level 的实体碰撞遍历）：
    ///     圆心 = ((cx+xr)*24, (cy+yr)*24 - hei*0.5)   ← hei / radius 都是**像素**
    ///     命中 = 圆心距 ≤ entity.radius + entity2.radius（_Entity: hei=24.0, radius=9.6 → ≈19px）
    ///   而 dx/dy/bdx/bdy 是**格/帧**。两者不要混。
    /// </summary>
    public class KingScepterAimMain : ModBase, IOnHeroUpdate, IOnGameExit
    {
        // ================= 可调参数 =================

        /// <summary>dc.en.Mob 的 CLID（与 ChronoBlade / StarfuryAutoRain 一致）。</summary>
        private const int MobClid = 32068;

        /// <summary>起跳索敌半径（格）。&lt;= 0 = 不限距离（为了"打败所有敌人"）。</summary>
        private const double SearchRadiusTiles = 0.0;

        /// <summary>飞行速度（像素/秒）。调大 = 连跳更快。</summary>
        private const double FallSpeedPxPerSec = 1200.0;

        /// <summary>每次起跳后的悬停时间（秒）——"滞空时间"，调小更短促。</summary>
        private const double HangSec = 0.20;

        /// <summary>悬停结束后的下落速度上限（格/帧）。必须明显小于每帧位移量，否则会栽到地上。</summary>
        private const double MaxFallCasesPerFrame = 0.30;

        /// <summary>盯着一只敌人多久还没踩到就认为够不到，跳过它换下一只（秒）。</summary>
        private const double TargetGiveUpSec = 2.5;

        /// <summary>起跳时把 spinCount 顶到这个值，避免原版 2 次命中就 stopSpin → 可以一直跳。</summary>
        private const int ChainSpinCount = 200;

        /// <summary>连续腾空的总时长兜底（秒）。"可以一直跳" → 放得很大，只防泄漏。</summary>
        private const double MaxAirborneSec = 120.0;

        /// <summary>踩住目标多久算暴击已生效，之后换下一只（必须 &gt; 原版每敌 0.2s 命中冷却）。</summary>
        private const double ContactHoldSec = 0.28;

        /// <summary>spinning 读不到时允许的宽限帧数。</summary>
        private const int SpinMissingGraceFrames = 6;

        /// <summary>同一帧去重门限（秒）。</summary>
        private const double SameFrameEpsSec = 0.002;

        /// <summary>到落点的最近距离（像素），再近就不动，避免除零抖动。</summary>
        private const double MinStepDistPx = 1.0;

        /// <summary>true = 取消权杖挥砍（isFirstSpin==true 那一下）的伤害；false = 恢复原版。</summary>
        private static readonly bool CancelSwingDamage = true;

        /// <summary>踩到敌人的暴击伤害倍率（1 = 原版）。3 = 三倍。</summary>
        private const double CritDamageMul = 3.0;

        /// <summary>true = 给权杖附加原版词缀 IgnoreGlobalShield（攻击无视对方 GlobalShield）。</summary>
        private static readonly bool AddIgnoreGlobalShield = true;

        /// <summary>原版词缀 id（affix 表 index 87，data.cdb: "id": "IgnoreGlobalShield"）。</summary>
        private const string IgnoreGlobalShieldAffix = "IgnoreGlobalShield";

        /// <summary>权杖出手后"挥砍窗口"的长度（秒）。要覆盖近战那一下的结算，又不能太长误伤换武器后的攻击。</summary>
        private const double SwingWindowSec = 0.25;

        /// <summary>true = 踩完一只后优先换"另一只最近的敌人"（只有一只时仍回到它继续连踩）。</summary>
        private static readonly bool PreferDifferentTarget = false;

        // ================= 运行时状态 =================

        private int _targetUid;
        private int _justHitUid;
        private bool _flying;
        private CollisionMode _savedCollisionMode;
        private KingScepter _weapon;

        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _airborneStart;
        private double _hangUntil;
        private double _lastStepTime = -1.0;
        private int _stepCount;
        private int _chainHits;
        private int _spinMissingFrames;

        private int _contactUid;
        private double _contactStart;

        /// <summary>最近一次踩到目标的时刻（用于判断"这只够不到"）。</summary>
        private double _lastContactTime;

        /// <summary>本次起跳期间放弃的敌人（盯太久没踩到 = 够不到），清空时机 = 从地面重新起跳。</summary>
        private readonly HashSet<int> _skippedUids = new HashSet<int>();

        private int _swingSuppressed;
        private int _critBoosted;
        private int _shieldAffixApplied;

        /// <summary>已放大过的 AttackData（同一份攻击可能被结算到多个目标上，只放大一次）。</summary>
        private readonly AttackData[] _amplifiedRing = new AttackData[8];
        private int _amplifiedIdx;

        /// <summary>权杖 onExecute 后的短窗口（秒）：近战那一下的结算可能早于 dashingF 赋值。</summary>
        private double _kingAttackArmedUntil;

        // 驱动存活探测
        private bool _heroHookAlive;
        private bool _loggedHeroHook;
        private bool _loggedWeaponHook;
        private bool _loggedHeroUpdate;

        private bool _errorLogged;

        public KingScepterAimMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();

            try
            {
                Hook_KingScepter.jumpAndStartSpin += OnJumpAndStartSpin;
                Logger.Information("[KingScepterAim] Hook 成功: KingScepter.jumpAndStartSpin");
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] Hook jumpAndStartSpin 失败"); }

            try
            {
                Hook_KingScepter.stopSpin += OnStopSpin;
                Logger.Information("[KingScepterAim] Hook 成功: KingScepter.stopSpin");
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] Hook stopSpin 失败"); }

            try
            {
                Hook_Hero.onMobDeath += OnHeroMobDeath;
                Logger.Information("[KingScepterAim] Hook 成功: Hero.onMobDeath");
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] Hook onMobDeath 失败"); }

            // 取消挥砍伤害：伤害结算的总入口
            try
            {
                Hook_Entity.applyAttackResult += OnApplyAttackResult;
                Logger.Information("[KingScepterAim] Hook 成功: Entity.applyAttackResult（取消挥砍伤害）");
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] Hook applyAttackResult 失败（挥砍伤害不会取消）"); }

            // 挥砍窗口：权杖出手时开一个短窗口，覆盖"近战结算早于 dashingF 赋值"的情况
            try
            {
                Hook_KingScepter.onExecute += OnKingScepterExecute;
                Logger.Information("[KingScepterAim] Hook 成功: KingScepter.onExecute（挥砍窗口）");
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] Hook onExecute 失败（用 dashingF 判定兜底）"); }

            // 首选驱动
            try
            {
                Hook_Hero.fixedUpdate += OnHeroFixedUpdate;
                Logger.Information("[KingScepterAim] Hook 成功: Hero.fixedUpdate（首选驱动）");
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] Hook Hero.fixedUpdate 失败（用备用驱动）"); }

            // 备用驱动
            try
            {
                Hook_KingScepter.fixedUpdate += OnWeaponFixedUpdate;
                Logger.Information("[KingScepterAim] Hook 成功: KingScepter.fixedUpdate（备用驱动）");
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] Hook KingScepter.fixedUpdate 失败（用备用驱动）"); }

            Logger.Information("[KingScepterAim] 已加载：取消挥砍伤害 + 加大范围索敌 + 短促起跳 + 无限连跳踩头暴击");
        }

        // ==================================================================
        // 1) 取消挥砍伤害
        // ==================================================================

        /// <summary>
        /// 伤害结算总入口。
        ///   · 权杖"挥砍/首次命中"那一下（isFirstSpin==true）→ 直接不结算（取消挥砍伤害）；
        ///   · 权杖暴击段（isFirstSpin==false，即踩到敌人那一下）→ finalDmg × CritDamageMul；
        ///   · 其它任何来源的伤害原样放行。
        /// 注意 Entity.applyAttackResult 是用 a.finalDmg 扣血的（见 Entity.cs:7537），
        /// 所以在调 orig 之前改 finalDmg 就是改最终伤害。
        /// </summary>
        private void OnApplyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            try
            {
                if (attack != null && TryClassifyKingScepterHit(attack, out KingScepter ks, out bool firstSpin))
                {
                    if (firstSpin)
                    {
                        // 挥砍那一下：不结算伤害
                        if (CancelSwingDamage)
                        {
                            _swingSuppressed++;
                            if (_swingSuppressed <= 5 || _swingSuppressed % 50 == 0)
                            {
                                Logger.Information($"[KingScepterAim] 已取消权杖挥砍伤害（第 {_swingSuppressed} 次）");
                            }
                            return;
                        }
                    }
                    else if (CritDamageMul != 1.0 && !IsAlreadyAmplified(attack))
                    {
                        // 暴击那一下：最终伤害 ×CritDamageMul
                        MarkAmplified(attack);
                        int before = attack.finalDmg;
                        int after = (int)System.Math.Round(before * CritDamageMul);
                        attack.finalDmg = after;

                        _critBoosted++;
                        if (_critBoosted <= 5 || _critBoosted % 25 == 0)
                        {
                            Logger.Information($"[KingScepterAim] 暴击伤害 ×{CritDamageMul:0.#}：{before} → {after}（第 {_critBoosted} 次）");
                        }
                    }
                }
            }
            catch { }

            orig(self, attack);
        }

        /// <summary>权杖出手：开一个 0.25s 的"挥砍窗口"（近战 hitFrame=0，结算很早）。</summary>
        private bool OnKingScepterExecute(Hook_KingScepter.orig_onExecute orig, KingScepter self)
        {
            bool result = orig(self);
            try { _kingAttackArmedUntil = _clock.Elapsed.TotalSeconds + SwingWindowSec; }
            catch { }
            return result;
        }

        /// <summary>
        /// 判断这次攻击是否来自国王权杖，并区分"挥砍首段"与"暴击段"。
        /// 优先用 AttackData.sourceWeapon（最准），拿不到再退回扫英雄武器表 + 出手窗口。
        /// </summary>
        private bool TryClassifyKingScepterHit(AttackData attack, out KingScepter ks, out bool firstSpin)
        {
            ks = null;
            firstSpin = true;

            Hero hero;
            try { hero = attack.source as Hero; } catch { hero = null; }
            if (hero == null || hero.destroyed) return false;

            bool armed;
            try { armed = _clock.Elapsed.TotalSeconds < _kingAttackArmedUntil; }
            catch { armed = false; }

            try { ks = attack.sourceWeapon as KingScepter; } catch { ks = null; }
            if (ks == null || ks.destroyed) ks = FindActiveKingScepter(hero, armed);
            if (ks == null || ks.destroyed) return false;

            // 必须是权杖在出手状态（冲刺/旋转），或刚出手的窗口内
            bool active;
            try { active = ks.spinning || ks.dashingF > 0.0; } catch { active = false; }
            if (!active && !armed) return false;

            try { firstSpin = ks.isFirstSpin; } catch { firstSpin = true; }
            return true;
        }

        private static KingScepter FindActiveKingScepter(Hero hero, bool armed)
        {
            try
            {
                var wm = hero.weaponsManager;
                if (wm == null) return null;
                return FindActiveKingScepterIn(wm.mainWeapons, armed) ?? FindActiveKingScepterIn(wm.backpackWeapons, armed);
            }
            catch { return null; }
        }

        private static KingScepter FindActiveKingScepterIn(ArrayObj weapons, bool armed)
        {
            if (weapons == null) return null;
            for (int i = 0; i < weapons.length; i++)
            {
                if (weapons.getDyn(i) is not KingScepter ks || ks.destroyed) continue;

                bool active;
                try { active = armed || ks.spinning || ks.dashingF > 0.0; }
                catch { active = armed; }
                if (active) return ks;
            }
            return null;
        }

        /// <summary>同一份 AttackData 可能被结算到多个目标上，只放大一次。</summary>
        private bool IsAlreadyAmplified(AttackData a)
        {
            for (int i = 0; i < _amplifiedRing.Length; i++)
            {
                if (ReferenceEquals(_amplifiedRing[i], a)) return true;
            }
            return false;
        }

        private void MarkAmplified(AttackData a)
        {
            _amplifiedRing[_amplifiedIdx] = a;
            _amplifiedIdx = (_amplifiedIdx + 1) % _amplifiedRing.Length;
        }

        // ==================================================================
        // 1b) 附带 IgnoreGlobalShield 词缀
        // ==================================================================

        /// <summary>
        /// 给权杖 item 补上原版词缀 IgnoreGlobalShield（"攻击无视 GlobalShield"）。
        ///
        /// 原理（都在原版代码里核对过）：
        ///   · dc.tool.atk._AttackUtils.createFromHeroWeapon 里会执行
        ///     attackData.useItemAffixes(w.item) —— 攻击对象继承武器 item 的全部词缀；
        ///   · dc.tool.atk.AttackTargetImpl 判定命中时：
        ///       if (!atk.hasTag(8) &amp;&amp; !atk.hasAffix("IgnoreGlobalShield")) { ...目标 affect 28/29/86 → Block }
        ///     也就是说带了这个词缀的攻击不会因为对方的护盾类 affect 而被 Block。
        ///   · 该词缀在 affix 表里 props 为空、allowStacking=false、keepOnReroll=true，
        ///     是个纯标记词缀，不会改数值、也不会被洗掉。
        /// 所以只要给它加一次，之后权杖打出的每一次攻击（包括我们的三倍暴击）都无视护盾。
        /// </summary>
        private void EnsureIgnoreGlobalShieldAffix(KingScepter ks)
        {
            if (!AddIgnoreGlobalShield) return;
            try
            {
                if (ks == null || ks.destroyed) return;

                dc.tool.InventItem item = ks.item;
                if (item == null) return;

                dc.String id = ToHaxeString(IgnoreGlobalShieldAffix);
                if (item.hasAffix(id)) return;      // 已经有了（同一件只会加一次）

                bool ignoreChecks = true;
                item.addAffix(id, ref ignoreChecks);

                _shieldAffixApplied++;
                Logger.Information($"[KingScepterAim] 已为权杖附加词缀 {IgnoreGlobalShieldAffix}：攻击无视 GlobalShield（第 {_shieldAffixApplied} 件武器）");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[KingScepterAim] 附加 {IgnoreGlobalShieldAffix} 失败: {ex.Message}");
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        // ==================================================================
        // 2) 起跳瞬间：续期旋转 + 大范围锁敌
        // ==================================================================

        private void OnJumpAndStartSpin(Hook_KingScepter.orig_jumpAndStartSpin orig, KingScepter self, Entity e)
        {
            orig(self, e);

            try
            {
                if (self == null || self.owner == null || self.owner.destroyed) return;

                _weapon = self;

                // 附带 IgnoreGlobalShield 词缀（攻击无视对方 GlobalShield）
                EnsureIgnoreGlobalShieldAffix(self);

                // 续期：原版 spinCount = effectCharge(2)，打完 2 次就 stopSpin → 顶高它才能"一直跳"
                try { self.spinCount = ChainSpinCount; } catch { }

                Mob hit = e as Mob;
                int justHitUid = IsEnemyAlive(self.owner, hit) ? hit.__uid : 0;

                Mob target = PickTarget(self.owner, justHitUid);
                int uid = target == null ? 0 : target.__uid;

                BeginFlight(self.owner, justHitUid, uid);

                if (uid != 0)
                {
                    double d = DistanceTiles(self.owner, target);
                    Logger.Information($"[KingScepterAim] 起跳：锁敌 uid={uid} 距离={d:0.0}格（刚打中={justHitUid}，索敌半径={SearchRadiusTiles:0}格）");
                }
                else
                {
                    LogNoTarget(self.owner, "起跳");
                }
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] 起跳锁敌失败"); }
        }

        private void OnStopSpin(Hook_KingScepter.orig_stopSpin orig, KingScepter self)
        {
            orig(self);
            try { EndFlight(self == null ? null : self.owner, "旋转结束(stopSpin)"); }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] 结束飞行失败"); }
        }

        private void OnHeroMobDeath(Hook_Hero.orig_onMobDeath orig, Hero self, Mob m)
        {
            orig(self, m);
            try
            {
                if (!_flying || m == null || m.__uid != _targetUid) return;

                Mob next = PickTarget(self, m.__uid);
                _targetUid = next == null ? 0 : next.__uid;
                Logger.Information($"[KingScepterAim] 目标死亡，改锁 uid={_targetUid}");
            }
            catch (Exception ex) { Logger.Error(ex, "[KingScepterAim] 目标死亡重新索敌失败"); }
        }

        // ==================================================================
        // 3) 三个每帧驱动（谁先证明活着用谁）
        // ==================================================================

        private void OnHeroFixedUpdate(Hook_Hero.orig_fixedUpdate orig, Hero self)
        {
            orig(self);

            if (!_loggedHeroHook)
            {
                _loggedHeroHook = true;
                _heroHookAlive = true;
                Logger.Information("[KingScepterAim] 驱动激活: Hook_Hero.fixedUpdate（首选）");
            }
            SteerFlight(self);
        }

        private void OnWeaponFixedUpdate(Hook_KingScepter.orig_fixedUpdate orig, KingScepter self)
        {
            orig(self);

            if (!_loggedWeaponHook)
            {
                _loggedWeaponHook = true;
                if (!_heroHookAlive) Logger.Information("[KingScepterAim] 驱动激活: Hook_KingScepter.fixedUpdate（备用）");
            }

            if (_heroHookAlive) return;
            SteerFlight(self == null ? null : self.owner);
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (!_loggedHeroUpdate)
            {
                _loggedHeroUpdate = true;
                if (!_heroHookAlive) Logger.Information("[KingScepterAim] 驱动激活: IOnHeroUpdate（保底）");
            }

            if (_heroHookAlive) return;
            SteerFlight(ModCore.Modules.Game.Instance.HeroInstance);
        }

        // ==================================================================
        // 4) 接管位移
        // ==================================================================

        private void SteerFlight(Hero hero)
        {
            if (!_flying) return;

            double now = _clock.Elapsed.TotalSeconds;
            double dt = (_lastStepTime < 0.0) ? (1.0 / 60.0) : (now - _lastStepTime);
            if (dt < SameFrameEpsSec) return;
            if (dt > 0.1) dt = 0.1;
            _lastStepTime = now;

            try
            {
                if (hero == null || hero.destroyed || hero._level == null) { EndFlight(hero, "英雄无效"); return; }
                if (_weapon == null || _weapon.destroyed) { EndFlight(hero, "权杖已销毁"); return; }
                if (now - _airborneStart > MaxAirborneSec) { EndFlight(hero, $"腾空超时({MaxAirborneSec}s)"); return; }

                if (IsSpinning(hero, _weapon)) _spinMissingFrames = 0;
                else if (++_spinMissingFrames > SpinMissingGraceFrames)
                {
                    EndFlight(hero, "旋转已结束(spinning=false)");
                    return;
                }

                // 够不到就跳过：盯着一只很久都没踩到（比如在封闭房间里），换下一只，避免永远卡在它身上
                if (_targetUid != 0 && now - _lastContactTime > TargetGiveUpSec)
                {
                    Logger.Information($"[KingScepterAim] uid={_targetUid} 已 {TargetGiveUpSec}s 没踩到（够不到）→ 跳过它");
                    _skippedUids.Add(_targetUid);
                    _targetUid = 0;
                }

                // 目标：每帧按活体敌人重新取坐标（敌人会动）
                Mob tm = FindMobByUid(hero, _targetUid);
                if (tm == null)
                {
                    tm = PickTarget(hero, _justHitUid);
                    if (tm == null)
                    {
                        LogNoTarget(hero, "飞行中");
                        EndFlight(hero, _skippedUids.Count > 0 ? "剩余敌人够不到" : "全部敌人已清空");
                        return;
                    }
                    _targetUid = tm.__uid;
                    _lastContactTime = now;
                    Logger.Information($"[KingScepterAim] 改锁下一只 → uid={_targetUid} 距离={DistanceTiles(hero, tm):0.0}格");
                }

                double mobCenterX = (tm.cx + tm.xr) * 24.0;
                double mobCenterY = (tm.cy + tm.yr) * 24.0;
                double contactPx = hero.radius + tm.radius;                    // 原版命中阈值（像素）

                // 落点 = 敌人头顶，且保证不会高到脱离命中阈值
                double aimX = mobCenterX;
                double aimY = mobCenterY - System.Math.Min(tm.hei * 0.5, contactPx * 0.85);

                double heroX = (hero.cx + hero.xr) * 24.0;
                double heroY = (hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;

                double dx = aimX - heroX;
                double dy = aimY - heroY;
                double dist = System.Math.Sqrt(dx * dx + dy * dy);
                bool inContact = dist <= contactPx;

                _stepCount++;

                // ---- 踩到目标：等暴击生效 → 继续起跳（留在这只身上打到死）----
                if (inContact)
                {
                    _lastContactTime = now;

                    if (_contactUid != tm.__uid)
                    {
                        _contactUid = tm.__uid;
                        _contactStart = now;
                        Logger.Information($"[KingScepterAim] 踩到 uid={tm.__uid}（距离={dist:0} ≤ 阈值={contactPx:0}），等暴击");
                    }
                    else if (now - _contactStart >= ContactHoldSec)
                    {
                        _chainHits++;
                        _justHitUid = tm.__uid;
                        _contactUid = 0;

                        // 继续起跳：重置悬停计时（每次起跳都重新给一次"起跳时间"）
                        _hangUntil = now + HangSec;
                        _lastContactTime = now;

                        // 不换目标：就留在这只身上继续踩，直到把它打死。
                        // 它死之后 FindMobByUid 找不到 → 自动改锁全图最近的下一只 → "一一打败所有敌人"。
                        Logger.Information($"[KingScepterAim] 第 {_chainHits} 次踩击 uid={tm.__uid} → 继续起跳（不换目标，打死为止）");
                    }
                }

                if (_stepCount <= 3 || _stepCount % 20 == 0)
                {
                    Logger.Information($"[KingScepterAim] 突进 step={_stepCount} 英雄=({heroX:0},{heroY:0}) 落点=({aimX:0},{aimY:0}) 距离={dist:0} 阈值={contactPx:0} 接触={inContact} 悬停={now < _hangUntil} 速度dy={hero.dy:0.000}/{hero.bdy:0.000}");
                }

                LimitFall(hero, now);

                if (dist <= MinStepDistPx) return;

                double step = FallSpeedPxPerSec * dt;
                double nx = heroX + dx / dist * step;
                double ny = heroY + dy / dist * step;

                EnsureIgnoreWalls(hero);
                hero.setPosPixel(nx, ny);

                try
                {
                    int want = dx >= 0.0 ? 1 : -1;
                    if (hero.dir != want) hero.dir = want;
                }
                catch { }
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    Logger.Error(ex, "[KingScepterAim] 突进失败（后续异常不再打印）");
                }
            }
        }

        /// <summary>
        /// 起跳时间控制：
        ///   · 起跳后 HangSec 秒内把向下速度清零（向上的起跳保留）→ 冲高 + 悬停；
        ///   · 之后把向下速度限制在 MaxFallCasesPerFrame（原版 spinning 会 dy*=1.1 复利暴跌）。
        /// 这样"起跳时间"短促可控，而不是原版一掉到底、也不是无限悬停。
        /// </summary>
        private void LimitFall(Hero hero, double now)
        {
            try
            {
                double vy = hero.dy + hero.bdy;
                if (vy <= 0.0) return;                                   // 正在上升 → 不动

                double limit = (now < _hangUntil) ? 0.0 : MaxFallCasesPerFrame;
                if (vy <= limit) return;

                double k = limit / vy;
                hero.dy *= k;
                hero.bdy *= k;
            }
            catch { }
        }

        // ==================================================================
        // 5) 飞行状态
        // ==================================================================

        private void BeginFlight(Hero hero, int justHitUid, int targetUid)
        {
            double now = _clock.Elapsed.TotalSeconds;

            if (!_flying)
            {
                // 真正从地面起跳（不是空中换目标）才重置总时长、连踩计数与"够不到"名单
                _airborneStart = now;
                _chainHits = 0;
                _skippedUids.Clear();

                try { _savedCollisionMode = hero != null ? hero.collisionMode : null; }
                catch { _savedCollisionMode = null; }
            }

            _flying = true;
            _justHitUid = justHitUid;
            _targetUid = targetUid;
            _errorLogged = false;
            _stepCount = 0;
            _contactUid = 0;
            _contactStart = 0.0;
            _spinMissingFrames = 0;
            _lastStepTime = -1.0;
            _lastContactTime = now;
            _hangUntil = now + HangSec;      // 每次起跳都给一次"起跳时间"
        }

        private void EndFlight(Hero hero, string reason)
        {
            if (!_flying) return;
            _flying = false;
            _targetUid = 0;
            _contactUid = 0;

            Logger.Information($"[KingScepterAim] 腾空结束：{reason}（步数 {_stepCount}，连踩 {_chainHits} 只）");

            _weapon = null;
            if (hero != null && !hero.destroyed)
            {
                try { hero.collisionMode = _savedCollisionMode ?? new CollisionMode.Normal(); }
                catch { }
            }
            _savedCollisionMode = null;
        }

        private void EnsureIgnoreWalls(Hero hero)
        {
            try
            {
                if (hero.collisionMode is CollisionMode.IgnoreWalls) return;
                hero.collisionMode = new CollisionMode.IgnoreWalls();
            }
            catch { }
        }

        // ==================================================================
        // 6) 武器 / 目标判定
        // ==================================================================

        private static bool IsSpinning(Hero hero, KingScepter known)
        {
            try
            {
                if (known != null && !known.destroyed && known.spinning) return true;
            }
            catch { }

            try
            {
                var wm = hero.weaponsManager;
                if (wm == null) return false;
                if (CheckSpinning(wm.mainWeapons)) return true;
                if (CheckSpinning(wm.backpackWeapons)) return true;
            }
            catch { }
            return false;
        }

        private static bool CheckSpinning(ArrayObj weapons)
        {
            if (weapons == null) return false;
            for (int i = 0; i < weapons.length; i++)
            {
                if (weapons.getDyn(i) is KingScepter ks && !ks.destroyed && ks.spinning) return true;
            }
            return false;
        }

        private static Mob FindMobByUid(Hero hero, int uid)
        {
            if (uid == 0) return null;
            try
            {
                var mobs = hero._level?.entitiesByClass?.get(MobClid) as ArrayObj;
                if (mobs == null) return null;
                for (int i = 0; i < mobs.length; i++)
                {
                    if (mobs.getDyn(i) is Mob m && m.__uid == uid && IsEnemyAlive(hero, m)) return m;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 最低限度判定：活着 + 敌对。
        /// 注意：这里**不**要求 _targetable / canBeDetected / canBeHitBy ——
        /// 敌人被击中后有一小段状态会让这些判定失败，周围只剩它一只时就会"索不到敌"。
        /// 严格条件只用于优先挑选（见 FindNearestMob 的两轮搜索）。
        /// </summary>
        private static bool IsEnemyAlive(Hero hero, Mob m)
        {
            if (m == null || m.destroyed || m.life <= 0) return false;
            if (m._team != null && hero != null && m._team == hero._team) return false;
            return true;
        }

        private static bool IsHittableNow(Hero hero, Mob m)
        {
            try
            {
                if (!m._targetable) return false;
                if (!m.canBeDetected()) return false;
                if (hero != null && !m.canBeHitBy(hero)) return false;
            }
            catch { return false; }
            return true;
        }

        /// <summary>最近的敌人。两轮：先只找"当前能打的"，一只都没有才退回"活着的敌人"。</summary>
        private Mob FindNearestMob(Hero hero, int excludeUid)
        {
            return FindNearestMobPass(hero, excludeUid, true) ?? FindNearestMobPass(hero, excludeUid, false);
        }

        private Mob FindNearestMobPass(Hero hero, int excludeUid, bool requireHittable)
        {
            if (hero == null || hero.destroyed || hero._level == null) return null;
            try
            {
                var mobs = hero._level.entitiesByClass?.get(MobClid) as ArrayObj;
                if (mobs == null || mobs.length == 0) return null;

                double hx = hero.cx + hero.xr;
                double hy = hero.cy + hero.yr;

                Mob best = null;
                double bestD = double.MaxValue;

                for (int i = 0; i < mobs.length; i++)
                {
                    if (mobs.getDyn(i) is not Mob m) continue;
                    if (excludeUid != 0 && m.__uid == excludeUid) continue;
                    if (_skippedUids.Contains(m.__uid)) continue;     // 够不到的已拉黑
                    if (!IsEnemyAlive(hero, m)) continue;
                    if (requireHittable && !IsHittableNow(hero, m)) continue;

                    double dx = hx - (m.cx + m.xr);
                    double dy = hy - (m.cy + m.yr);
                    double dist = System.Math.Sqrt(dx * dx + dy * dy);
                    if (SearchRadiusTiles > 0.0 && dist > SearchRadiusTiles) continue;
                    if (dist >= bestD) continue;
                    bestD = dist;
                    best = m;
                }
                return best;
            }
            catch { return null; }
        }

        private static double DistanceTiles(Hero hero, Mob m)
        {
            if (hero == null || m == null) return -1.0;
            double dx = (hero.cx + hero.xr) - (m.cx + m.xr);
            double dy = (hero.cy + hero.yr) - (m.cy + m.yr);
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>优先"另一只最近的敌人"（PreferDifferentTarget）；否则取最近的敌人。</summary>
        private Mob PickTarget(Hero hero, int justHitUid)
        {
            if (PreferDifferentTarget && justHitUid != 0)
            {
                Mob other = FindNearestMob(hero, justHitUid);
                if (other != null) return other;
            }
            return FindNearestMob(hero, 0);
        }

        /// <summary>索敌失败时打诊断：怪物表里有几只、为什么被排除。</summary>
        private void LogNoTarget(Hero hero, string where)
        {
            try
            {
                var mobs = hero?._level?.entitiesByClass?.get(MobClid) as ArrayObj;
                if (mobs == null)
                {
                    Logger.Information($"[KingScepterAim] {where}索敌失败：entitiesByClass[{MobClid}] 为空");
                    return;
                }

                int alive = 0, mine = 0, dead = 0, hittable = 0, tooFar = 0;
                double nearest = double.MaxValue;
                for (int i = 0; i < mobs.length; i++)
                {
                    if (mobs.getDyn(i) is not Mob m) continue;
                    if (m.destroyed || m.life <= 0) { dead++; continue; }
                    if (m._team != null && hero != null && m._team == hero._team) { mine++; continue; }
                    alive++;
                    if (IsHittableNow(hero, m)) hittable++;

                    double d = DistanceTiles(hero, m);
                    if (d >= 0.0 && d < nearest) nearest = d;
                    if (SearchRadiusTiles > 0.0 && d > SearchRadiusTiles) tooFar++;
                }
                Logger.Information($"[KingScepterAim] {where}索敌失败：总数={mobs.length} 活敌={alive} 可打={hittable} 同队={mine} 已死={dead} 超范围={tooFar} 最近={nearest:0.0}格");
            }
            catch (Exception ex)
            {
                Logger.Information($"[KingScepterAim] {where}索敌失败（诊断本身出错）：{ex.Message}");
            }
        }

        // ==================================================================
        // 7) 退出
        // ==================================================================

        void IOnGameExit.OnGameExit()
        {
            _flying = false;
            _targetUid = 0;
            _weapon = null;

            try { Hook_KingScepter.jumpAndStartSpin -= OnJumpAndStartSpin; } catch { }
            try { Hook_KingScepter.stopSpin -= OnStopSpin; } catch { }
            try { Hook_KingScepter.onExecute -= OnKingScepterExecute; } catch { }
            try { Hook_KingScepter.fixedUpdate -= OnWeaponFixedUpdate; } catch { }
            try { Hook_Hero.onMobDeath -= OnHeroMobDeath; } catch { }
            try { Hook_Hero.fixedUpdate -= OnHeroFixedUpdate; } catch { }
            try { Hook_Entity.applyAttackResult -= OnApplyAttackResult; } catch { }

            Logger.Information("[KingScepterAim] 游戏退出，模组已卸载");
        }
    }
}
