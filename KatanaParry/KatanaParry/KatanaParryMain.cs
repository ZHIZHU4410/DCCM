#nullable disable

using System;
using dc;
using dc.en;
using dc.en.inter;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using dc.hl.types;

namespace KatanaParry
{
    /// <summary>
    /// 武士刀（Katana）前两刀带盾反
    /// ============================
    /// 需求：武士刀的**前两段挥砍**加上"盾反"效果（挥砍期间被敌人打 = 盾反）。
    ///
    /// 【原版盾反是怎么运作的】（GamePseudocode/dc.tool.weap/BaseShield.cs）
    ///   · 举盾 → <c>startParry()</c>：播 <c>fx.shieldParryStart(hero, 4480637)</c>，
    ///     给英雄挂 <c>affect 96</c>（盾反窗口），并 <c>hero.lastParryShield = this</c>；
    ///   · 敌人攻击命中 → 引擎把 <c>AttackData.hitResult</c> 算成 <c>HitResult.Block()</c>
    ///     → 调 <c>BaseShield.onOwnerAttackResultReceived(a)</c>；
    ///   · 那里判断 <c>hero.lastParryShield == this &amp;&amp; a.hitResult == Block</c> 成立就
    ///     <c>triggerParryFeedbacks()</c>（"Parade !" 文字 + 全屏闪 + 盾击光 + 放射线 + 音效）
    ///     和 <c>applyStunAndBumpFromParry(a)</c>（来源速度减半 + 反向小击退 + <c>affect 8</c> 眩晕）。
    ///
    /// 【本模组的做法】没有盾就没法走 hitResult=Block 那条路（那要靠盾的格挡区域），
    ///   所以直接在**攻击结算**这一层做，等价于"伤害被吃掉了 + 播盾反反馈"：
    ///   1) <c>Hook_Weapon.prepare</c>：武士刀第 0/1 段挥砍开始时打开一个 <c>_window</c>（默认 0.45s），
    ///      同时播 <c>shieldParryStart</c> 光环 —— 相当于"举刀即举盾"；
    ///   2) <c>Hook_Hero.applyAttackResult</c> / <c>Hook_Entity.applyAttackResult</c>：
    ///      窗口内且来源是敌对 → <b>不调用 orig</b>（= 不掉血、不进受击硬直），
    ///      给 <c>AttackData</c> 打上 <c>tag 7</c>（原版"已被盾反"标记，防重复处理），
    ///      再播反馈 + 眩晕击退（弹幕则 <c>bullet.block(true)</c> 打掉）。
    ///
    ///   注意 <c>Hero.applyAttackResult</c> 是**完整重写**（开头就 <c>activeSkillsManager.onTryApplyAttackResult</c>，
    ///   不调 base），所以英雄的伤害只走 Hook_Hero；Hook_Entity 那份是给其它路径兜底，靠 tag 7 去重。
    ///
    /// 【"前两刀"怎么认】<c>Weapon.get_cycle()</c>：连击序号在上一刀结束时
    /// <c>incrementCycle()</c> 递增（最后一段回到 0），所以在 <c>prepare</c> 这个时机读到的
    /// cycle 就是**这一刀**的段号。Katana 的 strikeChain 共 4 段（0/1/2/3，第 4 段是蓄力居合）。
    /// </summary>
    public class KatanaParryMain : ModBase, IOnGameExit, IOnGameInit, IOnFrameUpdate, IOnHeroUpdate, IModMenu
    {
        /// <summary>"前两刀"的下标上限（0 和 1）。</summary>
        private const int LastParryCycle = 1;

        /// <summary>
        /// 攻击 tag 7 = **不可格挡**（不是"已处理"）。
        /// GamePseudocode/dc.tool.atk/AttackTargetImpl.cs:191-193：
        /// <c>if (!atk.hasTag(7)) { if (isFrontBlockingAttack(atk)) hitResult = new HitResult.Block(); ... }</c>
        /// 也就是引擎自己就是用 tag 7 把"不可格挡攻击"排除在格挡判定之外的
        /// （<c>createRetaliation</c> / 环境伤害也都打这个 tag）。
        /// 所以本模组同样按它来：带 tag 7 的攻击**不盾反**，老老实实吃伤害 —— 与原版盾一致。
        /// </summary>
        private const int TagUnblockable = 7;

        /// <summary>affect 8 = 眩晕（stun）。</summary>
        private const int AffectStun = 8;

        /// <summary>盾反窗口剩余秒数。</summary>
        private double _window;

        /// <summary>反馈节流：一次挥砍里连吃多发攻击时，避免特效/音效刷屏（伤害照样全免）。</summary>
        private double _fxCooldown;

        /// <summary>酒桶/炸弹扫描节流（窗口内每 0.05s 扫一次，别每帧扫全场实体）。</summary>
        private double _bounceCd;

        /// <summary>酒桶打回的自检日志计数（只打前几次）。</summary>
        private int _bounceLogCount;

        /// <summary>上一帧 dt（识别"同一帧被回调两次"）。</summary>
        private double _lastDt;
        private bool _hasDt;

        /// <summary>本局统计（日志用）。</summary>
        private int _parryCount;

        /// <summary>首帧诊断日志只打一条。</summary>
        private bool _diagLogged;

        /// <summary>异常日志限流。</summary>
        private int _errorCount;

        public KatanaParryMain(ModInfo info) : base(info) { }

        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        // ---------------------------------------------------------------- 生命周期

        public override void Initialize()
        {
            base.Initialize();

            try { Hook_Weapon.prepare += OnWeaponPrepare; }
            catch (Exception ex) { Logger.Error(ex, "[KatanaParry] Hook_Weapon.prepare 挂载失败"); }

            try { Hook_Hero.applyAttackResult += OnHeroAttackResult; }
            catch (Exception ex) { Logger.Error(ex, "[KatanaParry] Hook_Hero.applyAttackResult 挂载失败"); }

            try { Hook_Entity.applyAttackResult += OnEntityAttackResult; }
            catch (Exception ex) { Logger.Error(ex, "[KatanaParry] Hook_Entity.applyAttackResult 挂载失败"); }

            Write("[KatanaParry] 已加载：武士刀前两刀盾反（挥砍期间被打 = 免伤 + 盾反反馈 + 眩晕来源）");
        }

        void IOnGameInit.OnGameInit()
        {
            try
            {
                var cfg = ParryKeys.Config.Value;
                Write("[KatanaParry] 开关状态 —— " +
                      $"总开关={cfg.EnableMod} 前两刀盾反={cfg.EnableParry} " +
                      $"反击眩晕={cfg.EnableStun} 盾反特效={cfg.EnableFx} " +
                      $"窗口={cfg.ParryWindowSec:0.##}s 眩晕={cfg.StunSec:0.##}s");
                Write($"[KatanaParry] 配置文件: {ParryKeys.Config.ConfigPath}" +
                      "（也可在游戏里 选项 → 模组 → 武士刀盾反 直接改）");

                try { if (ParryKeys.Config.Value != null) ParryKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[KatanaParry] 写配置失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Weapon.prepare -= OnWeaponPrepare; } catch { }
            try { Hook_Hero.applyAttackResult -= OnHeroAttackResult; } catch { }
            try { Hook_Entity.applyAttackResult -= OnEntityAttackResult; } catch { }

            ParryFeatures.ResetHotkeyState();
            _window = 0.0;
            _fxCooldown = 0.0;
            _lastDt = 0.0;
            _hasDt = false;
            _parryCount = 0;
            _diagLogged = false;

            Write("[KatanaParry] 游戏退出，模组已卸载");
        }

        // ---------------------------------------------------------------- 每帧

        void IOnFrameUpdate.OnFrameUpdate(double dt) => Tick(dt);

        void IOnHeroUpdate.OnHeroUpdate(double dt) => Tick(dt);

        private void Tick(double dt)
        {
            try { ParryFeatures.PollHotkeys(Write); } catch { }

            try
            {
                bool sameFrame = _hasDt && dt == _lastDt;
                _lastDt = dt;
                _hasDt = true;

                if (dt > 0.0 && !sameFrame)
                {
                    if (_window > 0.0)
                    {
                        _window -= dt;
                        if (_window < 0.0) _window = 0.0;
                    }
                    if (_fxCooldown > 0.0)
                    {
                        _fxCooldown -= dt;
                        if (_fxCooldown < 0.0) _fxCooldown = 0.0;
                    }
                    if (_bounceCd > 0.0)
                    {
                        _bounceCd -= dt;
                        if (_bounceCd < 0.0) _bounceCd = 0.0;
                    }

                    // 窗口内顺便照原版盾的 parryInArea 那样，把附近的敌方炸弹/酒桶打回去
                    if (_window > 0.0) TickBounce();
                }
            }
            catch (Exception ex) { Fail("tick", ex); }
        }

        // ---------------------------------------------------------------- ① 开窗口：前两刀

        /// <summary>
        /// 每次挥砍开始时调用（<c>HeroWeaponsManager</c> 里 <c>onWeaponUse → weapon.prepare()</c>）。
        /// 只有"武士刀 + 段号 0/1"才开盾反窗口。
        /// </summary>
        private void OnWeaponPrepare(Hook_Weapon.orig_prepare orig, Weapon self, double attackSpeed)
        {
            // 放在 orig 之前：窗口在本帧就生效（原版 prepare 只是初始化技能，不改窗口状态）
            if (self != null && !self.destroyed)
            {
                try
                {
                    if (self is Katana && ParryFeatures.IsOn(ParryFeature.Parry))
                    {
                        int cycle = self.get_cycle();
                        if (cycle >= 0 && cycle <= LastParryCycle)
                        {
                            var cfg = ParryKeys.Config.Value;
                            double sec = cfg.ParryWindowSec;
                            if (double.IsNaN(sec) || sec < 0.05) sec = 0.05;
                            if (sec > 3.0) sec = 3.0;

                            _window = sec;

                            // 开窗口的"盾反起手"光环（原版 startParry 的第一步）
                            if (ParryFeatures.IsOn(ParryFeature.Fx))
                            {
                                try { ParryFx.PlayParryStart(self.owner); } catch { }
                            }

                            DiagOnce(self, cycle, sec);
                        }
                    }
                }
                catch (Exception ex) { Fail("prepare", ex); }
            }

            orig(self, attackSpeed);
        }

        private void DiagOnce(Weapon w, int cycle, double sec)
        {
            if (_diagLogged) return;
            _diagLogged = true;

            try
            {
                Write($"[KatanaParry] 首次开盾反窗口 —— 段号={cycle}（前两刀 0/1） 窗口={sec:0.##}s " +
                      $"武器={w?.item?._itemData?.id}");
            }
            catch { }
        }

        // ---------------------------------------------------------------- ② 拦伤害 + 盾反

        /// <summary>英雄受伤路径（<c>Hero.applyAttackResult</c> 是完整重写，英雄伤害只走这里）。</summary>
        private void OnHeroAttackResult(Hook_Hero.orig_applyAttackResult orig, Hero self, AttackData a)
        {
            if (TryParry(self, a)) return;      // 盾反成功 = 不调 orig（不掉血、不进硬直）

            orig(self, a);
        }

        /// <summary>兜底：有些路径会静态派发到 <c>Entity.applyAttackResult</c>（和 Hero 那条互斥）。</summary>
        private void OnEntityAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData a)
        {
            if (self is Hero hero && TryParry(hero, a)) return;

            orig(self, a);
        }

        /// <summary>
        /// 判定并执行一次盾反。返回 true = 已吃掉这次攻击（调用方不要调 orig）。
        /// </summary>
        private bool TryParry(Hero hero, AttackData a)
        {
            try
            {
                if (_window <= 0.0) return false;
                if (!ParryFeatures.IsOn(ParryFeature.Parry)) return false;
                if (hero == null || hero.destroyed || hero.life <= 0) return false;
                if (a == null) return false;

                // 不可格挡的攻击（tag 7）不盾反 —— 与原版盾的判定一致
                if (a.hasTag(TagUnblockable)) return false;

                // 只反"敌对来源"；环境伤害（source == null，比如陷阱/坠落）不盾反
                Entity src = a.source;
                if (src == null || src.destroyed) return false;
                if (!hero.isOpponent(src)) return false;

                // ---- 吃掉这次攻击：不调 orig（不结算伤害），并把 hitResult 标成 Block ----
                //
                // ★ 这一步是**修 Pitcher 那个"被盾反后英雄被瞬移/推走"的 bug 的关键**：
                //   怪物的命中回调里普遍写着 `if (atk.isSuccess()) { e.bump(...); e.setAffectS(8, ...); }`
                //   （例如 Pitcher 扔 Hardy 的那一发：GamePseudocode/dc.en.mob/Pitcher.cs:1846-1857，
                //    `bump = 1` 格 + `duration = 1s` 眩晕；扔石头 ThrownRock 是同一段回调）。
                //   原版盾反时 `AttackTargetImpl.updateHitResult` 早就把 hitResult 算成 Block 了，
                //   所以这些回调自然不生效；我们直接在 applyAttackResult 里跳过结算，
                //   hitResult 还停在"成功"，于是**英雄会被怪物的回调推走/眩晕** —— 看起来就像瞬移。
                //   改成 Block 之后 `isSuccess()` 为 false，这类副作用全部消失，语义也和原版盾反一致。
                try { a.hitResult = new dc.HitResult.Block(); } catch { }

                // 兜底：把那一下可能已经挂到英雄身上的击退速度清掉
                // （怪物回调里是 `e.bump(...)`，有些路径在 applyAttackResult 之前就会打到英雄）。
                try { hero.bdx = 0.0; hero.bdy = 0.0; } catch { }

                _parryCount++;

                // ---- 反馈（节流，避免一发挥砍里连吃多发时刷屏）----
                if (ParryFeatures.IsOn(ParryFeature.Fx) && _fxCooldown <= 0.0)
                {
                    _fxCooldown = 0.15;
                    try { ParryFx.PlayParrySuccess(hero); } catch { }
                }

                // ---- 弹幕：打回去（打不成才退回"打掉"）----
                bool isBullet = a.carrier is Bullet;
                if (isBullet)
                {
                    Bullet bullet = (Bullet)a.carrier;
                    bool reflected = false;

                    if (ParryFeatures.IsOn(ParryFeature.Reflect))
                    {
                        try { reflected = ReflectBullet(hero, a, bullet); }
                        catch (Exception ex) { Fail("reflect", ex); }
                    }

                    if (!reflected)
                    {
                        // 关掉「打回去」或打不回去时，至少别让它继续飞
                        try { if (!bullet.destroyed) bullet.block(true); } catch { }
                    }
                }

                // ---- 近战：眩晕 + 击退 + 反击伤害 ----
                // 炸弹/酒桶的爆炸伤害不走这里：它们的实体在爆炸时已经 destroy 了，敲它没意义；
                // 真正的"打回去"是在 TickBounce 里**提前**把它们英雄化/反射掉的。
                bool isExplosive = src is Grenade || src is ExplosiveBarrel;

                if (!isBullet && !isExplosive)
                {
                    if (ParryFeatures.IsOn(ParryFeature.Stun)) PunishSource(hero, a);

                    if (ParryFeatures.IsOn(ParryFeature.Counter))
                    {
                        double power = ParryDamageNow();
                        CounterDamage(hero, src, power);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Fail("parry", ex);
                return false;
            }
        }

        /// <summary>当前盾反伤害（配置值，clamp 一下防止手改配置写出负数）。</summary>
        private static double ParryDamageNow()
        {
            double p = ParryKeys.Config.Value.ParryDamage;
            if (double.IsNaN(p) || p <= 0.0) p = 15.0;
            if (p > 100000.0) p = 100000.0;
            return p;
        }

        /// <summary>
        /// 把弹丸**打回去** —— 直接复用原版盾的反射逻辑。
        ///
        /// 原版怎么做（GamePseudocode/dc.tool.weap/_BaseShield.cs:73-384 `createCounterBullet`）：
        /// <code>
        /// AttackData atk = _AttackUtils.createFromHeroItem(owner, item, power);  // 80 ±10%
        /// double ang = atan2(来源中心 - 弹丸中心)                                // 朝来源；无来源则原方向 + π
        /// double spd = cBullet.isHeavy ? 1.5 : 1.0;                             // ★ 速度倍率固定，不跟原弹丸
        /// var countered = new CounteredBullet(owner, cBullet, atk, ang, spd, color);
        /// countered.init();
        /// countered.ignoreWalls / ignoreOneWays / ignoreGround = 原弹丸的同名字段;
        /// countered.pierceCount = cBullet.pierceCount + 1;
        /// </code>
        ///
        /// 关键就是那个 **速度倍率 1.0 / 1.5**：原版反射弹的速度是"基础弹速 × 倍率"，
        /// 而上一版我们是**复用原弹丸**、保留它原来的绝对速度 —— 遇到高速弹丸（比如各种快速箭/石）
        /// 打回去之后会快到看不清，就是你说的"子弹太快"。
        /// 所以这一版改成**调用原版 createCounterBullet 造一发 CounteredBullet**，
        /// 速度/贴图/穿透/物理标记全部照原版；只把它的伤害换成本模组的盾反伤害 ×75%。
        ///
        /// 返回 false = 造不出来（原弹丸已销毁 / 拿不到武士刀物品等），调用方会退回"打掉"。
        /// </summary>
        private bool ReflectBullet(Hero hero, AttackData a, Bullet bullet)
        {
            if (hero == null || hero.destroyed) return false;
            if (bullet == null || bullet.destroyed) return false;

            Entity src = a.source;
            if (src == null || src.destroyed) return false;

            // createCounterBullet 里会用 item 做伤害/等级缩放（我们随后会把 atk 换掉），
            // 但 item 不能为 null，所以拿手上的武士刀物品顶上。
            Weapon katana = FindKatana(hero);
            InventItem item = katana?.item;
            if (item == null) return false;

            // ---- 1) 原版反射：造一发 CounteredBullet（速度 = 基础弹速 × 1.0 / 1.5）----
            Bullet reflected = null;
            try
            {
                reflected = dc.tool.weap.BaseShield.Class.createCounterBullet?.Invoke(hero, item, a, bullet);
            }
            catch (Exception ex) { Fail("reflect-create", ex); }

            if (reflected == null || reflected.destroyed) return false;

            // ---- 2) 伤害换成本模组的反击攻击（盾反伤害 × 75%）----
            try
            {
                double ratio = ParryKeys.Config.Value.ReflectDamageRatio;
                if (double.IsNaN(ratio) || ratio <= 0.0) ratio = 0.75;
                if (ratio > 5.0) ratio = 5.0;

                AttackData counter = MakeCounterAttack(hero, ParryDamageNow() * ratio);
                if (counter != null)
                {
                    counter.addTag(1);              // 原版 createCounterBullet 也给反射弹打 tag 1
                    counter.carrier = reflected;    // 标成"弹丸造成"，与普通弹丸一致
                    reflected.atk = counter;
                }
            }
            catch (Exception ex) { Fail("reflect-atk", ex); }

            // ---- 3) 原弹丸消掉（原版 areaCounterBullet 也是造完就 cBullet.destroy()）----
            try { if (!bullet.destroyed) bullet.destroy(); } catch { }

            return true;
        }

        // ---------------------------------------------------------------- 酒桶 / 炸弹（照原版 parryInArea）

        /// <summary>
        /// 窗口内每 0.05s 扫一次场景里附近的**敌方**炸弹/酒桶并打回去。
        ///
        /// 原版盾的做法是 <c>BaseShield.parryInArea(area, fullParry)</c>
        /// （GamePseudocode/dc.tool.weap/BaseShield.cs:1409-1445）：举盾期间从
        /// <c>level.entitiesByClass</c> 取 <c>Grenade</c>（clid 27931），逐个
        /// <c>counterGrenade(grenade)</c> → <c>Grenade.counter(item, tier, playSound)</c>。
        /// 我们没有盾的 area 组件，所以等价成"以英雄为中心、半径 BounceRadiusCells 的圆"。
        /// </summary>
        private void TickBounce()
        {
            if (!ParryFeatures.IsOn(ParryFeature.Bounce)) return;
            if (_bounceCd > 0.0) return;
            _bounceCd = 0.05;

            Hero hero = null;
            try { hero = dc.pr.Game.Class?.ME?.hero; } catch { }
            if (hero == null || hero.destroyed) return;

            dc.pr.Level level = hero._level;
            if (level == null) return;

            ArrayObj all = level.entities;
            if (all == null) return;

            var cfg = ParryKeys.Config.Value;
            double cells = cfg.BounceRadiusCells;
            if (double.IsNaN(cells) || cells <= 0.0) cells = 2.5;
            if (cells > 12.0) cells = 12.0;
            double radiusPx = cells * 24.0;

            double hx = ((double)hero.cx + hero.xr) * 24.0;
            double hy = ((double)hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;

            int n = all.length;
            for (int i = 0; i < n; i++)
            {
                Entity e;
                try { e = all.getDyn(i) as Entity; } catch { continue; }
                if (e == null || e.destroyed) continue;

                try
                {
                    if (e is Grenade grenade)
                    {
                        if (!NearPx(e, hx, hy, radiusPx)) continue;
                        if (!hero.isOpponent(e)) continue;          // 自己扔的炸弹不打回去
                        if (BounceGrenade(hero, grenade)) PlayBounceFeedback(hero);
                        continue;
                    }

                    if (e is ExplosiveBarrel barrel)
                    {
                        if (!NearPx(e, hx, hy, radiusPx)) continue;
                        // 不按阵营过滤：场景里的酒桶多半是中立阵营（teamNeutral），
                        // 用 isOpponent 会把它们全筛掉。是不是"自己扔的"看 activatedByHero（见 BounceBarrel）。
                        if (BounceBarrel(hero, barrel)) PlayBounceFeedback(hero);
                    }
                }
                catch (Exception ex) { Fail("bounce-scan", ex); }
            }
        }

        /// <summary>两个实体中心的像素距离是否在半径内。</summary>
        private static bool NearPx(Entity e, double hx, double hy, double radiusPx)
        {
            double ex = ((double)e.cx + e.xr) * 24.0;
            double ey = ((double)e.cy + e.yr) * 24.0 - e.hei * 0.5;
            double dx = ex - hx;
            double dy = ey - hy;
            return dx * dx + dy * dy <= radiusPx * radiusPx;
        }

        /// <summary>
        /// 炸弹打回去 —— 直接走原版 <c>Grenade.counter(item, tier, playSound)</c>
        /// （GamePseudocode/dc.en/Grenade.cs:5388）：
        /// 播 <c>sfx/env/arrow_block.wav</c>，造一发 <c>RepeledGrenade(hero, 这枚炸弹, item, tier)</c>
        /// （英雄阵营、朝敌人飞），然后把原炸弹 <c>destroy()</c>。
        ///
        /// 只跳过原版 <c>counterGrenade</c> 里"给反射弹套盾词条"的那一步
        /// （<c>addAllAffixesFrom(盾)</c>）—— 需求明确不加任何盾的词条。
        /// </summary>
        private bool BounceGrenade(Hero hero, Grenade grenade)
        {
            Weapon katana = FindKatana(hero);
            InventItem item = katana?.item;
            if (item == null) return false;

            int tier = 1;
            try { tier = hero.getRelevantTierFor(item); } catch { }
            if (tier < 1) tier = 1;

            bool playSound = true;
            try
            {
                grenade.counter(item, tier, Ref<bool>.From(ref playSound));
            }
            catch (Exception ex) { Fail("bounce-grenade", ex); return false; }

            return true;
        }

        /// <summary>
        /// 酒桶打回去。
        ///
        /// 归属那两步用官方语义（"这桶算英雄的"）：
        ///   · <c>explode()</c> 用 <c>_team.get_targetHelper()</c> 选目标
        ///     → <c>set_team(hero._team)</c> 之后爆炸才会打怪、不打英雄；
        ///   · <c>createAttack()</c> 里 <c>activatedByHero ? createFromHero : createFromMob</c>
        ///     → <c>setActivatedByHero(true)</c> 就是"英雄扔的"（半径 1.0→1.2 格、伤害走英雄版）。
        ///
        /// ⚠ 掉头必须改 **bdx / bdy**，不能改 dx / dy —— 这是踩过的坑：
        ///   酒桶是"被 bump 出去"的，位移走的是 bump 速度（<c>Entity.bdx/bdy</c>），
        ///   原版 <c>ExplosiveBarrel.bounce()</c>（GamePseudocode/dc.en.inter/ExplosiveBarrel.cs:2165）就是
        ///   <code>
        ///   bumpResistance = 0.0;
        ///   bdx = 0.0; bdy = 0.0;
        ///   bump(dir * forceX, -forceY, null);      // ← 真正的飞行速度
        ///   </code>
        ///   只改 <c>dx/dy</c> 的话它照原方向飞（就是"没被打回去"的成因）。
        /// </summary>
        private bool BounceBarrel(Hero hero, ExplosiveBarrel barrel)
        {
            if (barrel.haveExploded) return false;
            if (barrel.activatedByHero) return false;       // 已经是英雄的桶（自己扔的）不动

            try { barrel.set_team(hero._team); } catch { }
            try { barrel.setActivatedByHero(true); } catch { }

            // 没引爆的桶顺便点着，这样它飞回去之后会在怪堆里炸
            try { if (!barrel.activated) barrel.activate(); } catch { }

            try
            {
                double nbx = -barrel.bdx;
                double nby = -barrel.bdy;

                // 朝向也翻过来（贴图 + 后续撞墙反弹都用 dir）
                barrel.dir = -barrel.dir;

                if (System.Math.Abs(nbx) < 0.02 && System.Math.Abs(nby) < 0.02)
                {
                    // 已经落地/静止的桶：用原版那份冲量（forceX / forceY）反着踢出去
                    double fx = barrel.forceX;
                    double fy = barrel.forceY;
                    if (double.IsNaN(fx) || fx <= 0.0) fx = 0.8;
                    if (double.IsNaN(fy) || fy <= 0.0) fy = 0.8;

                    nbx = barrel.dir * fx;      // dir 上面已经翻过
                    nby = -fy;
                }

                barrel.bumpResistance = 0.0;    // 和原版 bounce() 一样先清抗性，别被抗性吃掉速度
                barrel.bdx = nbx;
                barrel.bdy = nby;
                barrel.dx = 0.0;
                barrel.dy = 0.0;
                barrel.bouncing = true;

                // 自检日志（只打前几次，方便确认速度到底改没改对）
                if (_bounceLogCount < 6)
                {
                    _bounceLogCount++;
                    Write($"[KatanaParry] 酒桶已打回 #{_bounceLogCount}: bdx/bdy 反转 → ({nbx:0.###}, {nby:0.###})，" +
                          $"dir={barrel.dir} activated={barrel.activated}");
                }
            }
            catch (Exception ex) { Fail("bounce-barrel-vel", ex); }

            return true;
        }

        /// <summary>打回去时的反馈（复用盾反那套，带节流）。</summary>
        private void PlayBounceFeedback(Hero hero)
        {
            if (!ParryFeatures.IsOn(ParryFeature.Fx)) return;
            if (_fxCooldown > 0.0) return;

            _fxCooldown = 0.15;
            try { ParryFx.PlayParrySuccess(hero); } catch { }
        }

        /// <summary>
        /// 造一次"来自英雄"的攻击数据，并打上原版盾反的三个 tag。
        ///
        /// ⚠ 访问方式（CocoonPassive 踩过同一个坑，见其 source 注释）：
        /// <c>_AttackUtils</c> 上的 <c>createFromHero</c> / <c>hit</c> 是 **Hashlink 静态对象上的
        /// 实例字段（HlFunc）**，正确写法是去掉下划线的 `AttackUtils.Class.xxx.Invoke(...)`；
        /// `_AttackUtils.createFromHero(...)` 编译不过。
        ///
        /// 伤害随卷轴成长：`_AttackData._create` 只记 <c>sourceTier</c>，
        /// 真正的缩放发生在 <c>hit → updateDamages</c>（按 tier 约 ×1.15/级）。
        ///
        /// **不加任何盾的词条**：这里只用 hero + 数值构造，不读任何盾的 InventItem / props /
        /// affix，也不调 <c>addAllAffixesFrom</c>。
        /// </summary>
        private AttackData MakeCounterAttack(Hero hero, double power)
        {
            if (hero == null || hero.destroyed) return null;

            int tier = 1;
            try
            {
                Weapon w = FindKatana(hero);
                if (w?.item != null) tier = hero.getRelevantTierFor(w.item);
            }
            catch { }
            if (tier < 1) tier = 1;

            AttackData counter = AttackUtils.Class.createFromHero.Invoke(hero, (dynamic)power, (int?)tier);
            if (counter == null) return null;

            // 13 = 反制攻击、25 = 完美招架（fullParry）、2 = 暴击（原版 shieldCounterAttack 同款）
            try
            {
                counter.addTag(13);
                counter.setTag(25, true);
                counter.setTag(2, true);
            }
            catch { }

            return counter;
        }

        /// <summary>盾反伤害结算：`AttackUtils.Class.hit.Invoke(atk, src)`。</summary>
        private void CounterDamage(Hero hero, Entity src, double power)
        {
            try
            {
                if (src == null || src.destroyed || src.life <= 0) return;
                if (src is Hero) return;

                AttackData counter = MakeCounterAttack(hero, power);
                if (counter == null) return;

                AttackUtils.Class.hit.Invoke(counter, src);
            }
            catch (Exception ex) { Fail("counter", ex); }
        }

        /// <summary>找英雄手上的武士刀（用来取 tier；找不到就按 1 级算）。</summary>
        private static Weapon FindKatana(Hero hero)
        {
            try
            {
                dc.tool.hero.HeroWeaponsManager wm = hero.weaponsManager;
                if (wm == null) return null;

                Weapon w = wm.findInMain(Katana.Class);
                if (w is Katana) return w;

                dc.hl.types.ArrayObj mains = wm.mainWeapons;
                if (mains != null)
                {
                    for (int i = 0; i < mains.length; i++)
                        if (mains.getDyn(i) is Katana k) return k;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 反击：把近战攻击来源速度减半 + 反向小击退 + 眩晕。
        /// 逐条对应 GamePseudocode/dc.tool.weap/BaseShield.cs:3229-3276。
        /// 注意原版只罚"近战来源"（<c>a.carrier != null</c> = 弹丸/手雷 → 直接 return，只打掉弹丸）。
        /// </summary>
        private void PunishSource(Hero hero, AttackData a)
        {
            try
            {
                if (a.carrier != null) return;            // 弹丸/手雷不眩晕（与原版一致）

                Entity src = a.source;
                if (src == null || src.destroyed || src.life <= 0) return;
                if (src is Hero) return;                  // 别把自己/别的英雄当怪罚

                // 速度减半
                src.dx *= 0.5;
                src.dy *= 0.5;

                // 朝英雄面朝方向的小击退
                src.bump(hero.dir * 0.05, 0.0, null);

                // 眩晕（affect 8）
                double sec = ParryKeys.Config.Value.StunSec;
                if (double.IsNaN(sec) || sec <= 0.0) sec = 1.0;
                if (sec > 10.0) sec = 10.0;

                double val = 0.0;
                src.setAffectS(AffectStun, sec, Ref<double>.From(ref val), null);
            }
            catch (Exception ex) { Fail("punish", ex); }
        }

        // ---------------------------------------------------------------- 选项菜单（IModMenu）

        /// <summary>这一页在「选项 → 模组」里的名字。</summary>
        public string GetName() => "武士刀盾反";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("KATANA PARRY 设置"));
                b.createScroller(0.0);

                foreach (var f in ParryFeatures.All)
                {
                    var feature = f;
                    bool on = ParryFeatures.RawGet(feature);
                    string key = ParryFeatures.KeyName(feature);
                    string hint = ParryFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(ParryFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !ParryFeatures.RawGet(feature);
                            ParryFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                var cfg = ParryKeys.Config.Value;

                b.addSliderWidget(
                    StringUtils.AsHaxeString("盾反窗口(秒)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.ParryWindowSec = v;
                        try { ParryKeys.Config.Save(); } catch { }
                    },
                    cfg.ParryWindowSec,
                    Ref<double>.In(0.05),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.05),
                    Ref<double>.In(1.5),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("反击眩晕(秒)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.StunSec = v;
                        try { ParryKeys.Config.Save(); } catch { }
                    },
                    cfg.StunSec,
                    Ref<double>.In(0.1),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.1),
                    Ref<double>.In(4.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("盾反伤害(一级)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.ParryDamage = v;
                        try { ParryKeys.Config.Save(); } catch { }
                    },
                    cfg.ParryDamage,
                    Ref<double>.In(1.0),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(1.0),
                    Ref<double>.In(500.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("弹幕伤害系数"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.ReflectDamageRatio = v;
                        try { ParryKeys.Config.Save(); } catch { }
                    },
                    cfg.ReflectDamageRatio,
                    Ref<double>.In(0.05),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.05),
                    Ref<double>.In(3.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("酒桶/炸弹范围(格)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.BounceRadiusCells = v;
                        try { ParryKeys.Config.Save(); } catch { }
                    },
                    cfg.BounceRadiusCells,
                    Ref<double>.In(0.5),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.5),
                    Ref<double>.In(8.0),
                    null,
                    Ref<int>.In(0));

                b.updateScroller();
                Write($"[KatanaParry] 选项菜单已建立：{ParryFeatures.All.Length} 个功能开关 + 5 条滑条");
            }
            catch (Exception ex)
            {
                Write($"[KatanaParry] 建立选项菜单失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 工具

        private void Fail(string where, Exception ex)
        {
            if (_errorCount >= 5) return;
            _errorCount++;
            Write($"[KatanaParry] {where} 出错（第 {_errorCount} 次，最多记 5 次）: {ex.Message}");
        }
    }
}
