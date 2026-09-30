#nullable disable

using dc;
using dc.en;
using dc.en.bu;
using dc.en.loot;
using dc.tool.atk;
using dc.tool.weap;
using Hashlink.Proxy;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using System;

namespace WreckingBallOverhaul
{
    /// <summary>
    /// WreckingBall（流星锤 / 破坏球）强化模组
    /// =====================================================================
    /// 需求：
    ///   1. 删除前 2a（两段平平无奇的平A），直接从 3a 起手；
    ///   2. 3a 丢出的流星锤一直留在场上（不自动消失、不回收，可反复丢）；
    ///   3. 丢出可暴击（原版丢出段 canCrit=false，数据补丁改为 true）；
    ///   4. 丢出 模型 ×5；
    ///   5. 丢出 判定范围 ×5（命中范围跟着放大后的模型走）；
    ///   6. 丢出 伤害 ×10（+1000%）；
    ///   7. 3a 前摇减少；
    ///   8. 丢出的流星锤无视墙体；
    ///   9. 丢出距离 40 格，飞到最远点时停下。
    ///
    /// 【原版机制（GamePseudocode/dc.tool.weap/WreckingBall.cs）】
    ///   strikeChain 是 4 段连击，weapon._cycle 直接就是段位下标：
    ///     cycle 0 / 1  -> AtkWreckingballA / B   两段平A（前 2a）
    ///     cycle 2      -> AtkWreckingballC       丢出流星锤（onExecute 里硬编码 `if (cycle == 2)`）
    ///     cycle 3      -> AtkWreckingballD       收回流星锤（本模组已取消）
    ///   「删掉 1a/2a」不能去删 CDB 里的 strikeChain 条目 ——
    ///   _Weapon.__inst_construct__ 会用 skills.length 去索引 strikeChain 建 areas，
    ///   砍短数组会越界崩游戏（详见 patch_wreckingball_cdb.py 的说明）。
    ///   正确做法是把段位钉在 2：效果等价，且完整复用原版丢球逻辑。
    ///
    /// 【本模组踩过的坑，全部已修】
    ///   坑1 不能靠 weapon.chainedEntity 判断「球还在不在」
    ///       原版 Weapon.cancelChain() / WreckingBall.dispose() 都会把它置空。
    ///       本模组用自有字段 _ball 追踪。
    ///   坑2 不能用 ReferenceEquals 比较 Hashlink 代理对象
    ///       HaxeProxy 每次字段访问可能给出不同的托管包装实例
    ///       （HashlinkObj 没重写 Equals/GetHashCode），会假失败导致
    ///       放大/钉住/清除判定全部失灵。本模组先比 HashlinkObj 包装器、
    ///       再退回比 HashlinkPointer（HL 对象真实地址）。
    ///   坑3 球的命中伤害不在 WreckingBall.hitFromWeapon 上
    ///       流星锤打中敌人走的是 Bullet.onTouchValidTarget → _AttackUtils.hit(球的
    ///       attackData, 敌人)，那条路径不经过 hitFromWeapon（那是近战挥击用的）。
    ///       所以伤害必须在 _AttackUtils.hit 上放大；改 strikeChain.power / CDB 都无效，
    ///       因为球的 AttackData 在丢出时就已按 skillInf.power 建好快照。
    ///   坑4 原版 maxDist 只有 11 格，球飞过去会走 Bullet.reachMaxDist() → vanish()，
    ///       表现就是「丢出去飞不远」。本模组把距离调到 40 格，并把终点行为改成「停下」。
    ///
    /// 【本模组的实现分工】
    ///   数据（res.pak 数据补丁，patch_wreckingball_cdb.py 生成）：
    ///     · 3a 前摇：charge 0.7 -> 0.25，animSpd 1.0 -> 1.6
    ///     · 3a 可暴击：canCrit false -> true（原版只有收回段能暴击）
    ///   代码（本文件）：
    ///     · WreckingBall.{onExecute,incrementCycle,set_cycle} —— 段位永远 2
    ///     · WreckingBallHero.{createAmmoDrop,hasAmmoToRetrieve} —— 不掉弹药、不消失
    ///     · Hook_Bullet.reachMaxDist —— 到最远点时令球停下（原版是 vanish() 销毁）
    ///     · Entity.fixedUpdate —— 每帧维持模型 ×5、判定半径 ×5、无视墙体
    ///     · _AttackUtils.hit —— 流星锤命中时把 dmgMultiplier ×10（+1000%）
    ///
    /// 参考：DamageAuraBoost（目录格式 / Assets + cs 搭配）、
    ///       CrossX10（sprScaleX/sprScaleY 改模型大小、collisionMode 改碰撞）、
    ///       ThrowingCardsOverhaul（Hook_XXX 事件式 Hook 用法）。
    /// </summary>
    public class WreckingBallOverhaulMain : ModBase, IOnGameExit, IOnAfterLoadingAssets
    {
        /// <summary>
        /// 丢球段（原版第3a）。本模组把连段永远钉在这一段：
        /// 不回收、不进入平A，按攻击键就是再丢一颗。
        /// </summary>
        private const int CYCLE_THROW = 2;

        /// <summary>
        /// 模型 / 判定范围 倍率（丢出 ×5）。
        /// 注意：伤害倍率是独立的 DAMAGE_MUL，不要用这个。
        /// </summary>
        private const double SCALE_MUL = 5.0;

        /// <summary>
        /// 伤害倍率（丢出 ×10，即 +1000%）。
        /// 单独一个常量，方便和模型/判定范围分开调。
        /// </summary>
        private const double DAMAGE_MUL = 10.0;

        /// <summary>
        /// 流星锤的基准碰撞半径（像素）。
        /// Entity.radius 是「圆形碰撞」用的判定半径，
        /// dc.pr.Level.resolveCircularCollisions() 里判定条件是
        ///     entity.radius + entity2.radius  >=  两中心距离
        /// 所以把球的 radius 乘 5，命中范围就跟着放大 5 倍，与放大后的模型匹配。
        /// </summary>
        private const double BASE_BALL_RADIUS = 18.0;

        /// <summary>
        /// 丢出距离（格）。原版 WreckingBallHero.setGoingBackToHero(false) 里固定
        /// maxDist = 11 * 24（即 11 格），到点就 vanish()。
        /// 这里调大到 40 格，让球飞得更远，然后停在最远点。
        /// maxDist 的单位是像素，1 格 = 24px。
        /// </summary>
        private const double THROW_DISTANCE_TILES = 40.0;

        /// <summary>
        /// 给流星锤的攻击数据打的记号（AttackData tag）。
        /// 用 tag 而不是「看 sourceWeapon 是不是 WreckingBall」来判定：
        /// 命中结算时手上只有 AttackData，用 tag 最直接可靠。
        /// 取值避开原版已用的 1/2/7/8/14/17/22/23/26/27/28 等。
        /// </summary>
        private const int TAG_WRECKING_BALL = 3001;

        /// <summary>
        /// 最新丢出的那颗流星锤。
        /// 刻意不用 weapon.chainedEntity —— 那个字段会被 cancelChain()/dispose() 置空。
        /// </summary>
        private static WreckingBallHero _ball;

        /// <summary>已乘过倍率的球与它乘之前的基准缩放，便于精确还原。</summary>
        private static WreckingBallHero _scaledBall;
        private static double _scaleBaseX = 1.0;
        private static double _scaleBaseY = 1.0;

        /// <summary>当前这颗球的出生计时（帧）。</summary>
        private static int _ballAge;

        /// <summary>防重入：cancelChain/interrupt 可能二次触发同一个 Hook。</summary>
        private static bool _inExecute;

        /// <summary>日志采样。</summary>
        private bool _loggedStop;

        public WreckingBallOverhaulMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();

            // ---- 需求1/2：段位永远钉在 3a（删前2a、取消收回）----
            Hook_WreckingBall.onExecute += OnBallExecute;
            Hook_WreckingBall.incrementCycle += OnIncrementCycle;
            Hook_WreckingBall.set_cycle += OnSetCycle;

            // ---- 需求2：流星锤留在场上、不吐弹药、不消失 ----
            Hook_WreckingBallHero.onTouchGround += OnBallTouchGround;
            Hook_WreckingBallHero.onHitWall += OnBallHitWall;
            Hook_WreckingBallHero.createAmmoDrop += OnBallCreateAmmoDrop;
            Hook_WreckingBallHero.hasAmmoToRetrieve += OnBallHasAmmoToRetrieve;
            Hook_WreckingBallHero.setGoingBackToHero += OnBallSetGoingBack;

            // ---- 需求9：飞到最远点停下（而不是原版的直接销毁）----
            // Bullet.fixedUpdate 的距离判定：
            //     num = maxDist;
            //     if (!(getCoveredDistSqr() <= num*num)) { onReachMaxDist(); reachMaxDist(); }
            // 原版 reachMaxDist() 会 vanish() 把球销毁；这里改成「清速度 → 停在最远点」。
            Hook_Bullet.reachMaxDist += OnBallReachMaxDist;

            // ---- 需求4/5/6：模型 ×5、判定 ×5、伤害 ×10 ----
            Hook_WreckingBallHero.initGfx += OnBallInitGfx;
            Hook_Entity.fixedUpdate += OnAnyEntityFixedUpdate;

            // 需求6（伤害）：流星锤打中敌人走的**不是** WreckingBall.hitFromWeapon，
            // 而是 Bullet.onTouchValidTarget → _AttackUtils.hit(球的 attackData, 敌人)。
            // 所以必须在 _AttackUtils.hit 上做放大，否则改 power 完全没用。
            Hook__AttackUtils.hit += OnAttackHit;

            Logger.Information("[WreckingBallOverhaul] 已加载：删前2a / 取消收回(可反复丢) / " +
                               "丢出可暴击 / 模型×5 判定×5 伤害×10 / 无视墙体 / 40格停 / 前摇减少");
        }

        // ==================================================================
        // HL 对象身份：不能用 ReferenceEquals（见类注释 坑2）
        // ==================================================================

        /// <summary>
        /// 两个 Haxe 代理是否指向同一个 HL 对象。
        /// 先比 HashlinkObj（包装器本身，HaxeProxy 会缓存，通常就是同一个实例），
        /// 再退回比 HashlinkPointer（底层 HL 对象地址）——
        /// 单用 ReferenceEquals 在包装器被重建时会假失败。
        /// </summary>
        private static bool SameHlObj(HaxeObject a, HaxeObject b)
        {
            if (ReferenceEquals(a, null) || ReferenceEquals(b, null)) return false;
            if (ReferenceEquals(a, b)) return true;
            try
            {
                var oa = a.HashlinkObj;
                var ob = b.HashlinkObj;
                if (oa != null && ob != null && ReferenceEquals(oa, ob)) return true;
            }
            catch { /* 继续退回指针比较 */ }
            try
            {
                IntPtr pa = a.HashlinkPointer;
                IntPtr pb = b.HashlinkPointer;
                if (pa == IntPtr.Zero || pb == IntPtr.Zero) return false;
                return pa == pb;
            }
            catch
            {
                return false;
            }
        }

        // ==================================================================
        // 需求1/2：段位永远钉在 3a —— 删前2a、取消收回
        // ==================================================================

        /// <summary>
        /// 每次出手前把段位强制成 2（丢球段）。
        ///   · cycle 0/1 —— 原版前两段平A，属于「要删除」的内容；
        ///   · cycle 3   —— 原版收回段，本模组已取消；
        /// 两者都改成 2，于是每次攻击都是「丢出一颗新的流星锤」。
        /// 必须在 orig 之前改 —— WreckingBall.onExecute 是按 get_cycle() 分支的；
        /// _executeImpl 在调用 onExecute 之前已取好 areas[旧段位]，
        /// 而 0/1/2/3 都是同一条 strikeChain 的合法 Area，改段位不会越界。
        /// </summary>
        private bool OnBallExecute(Hook_WreckingBall.orig_onExecute orig, WreckingBall self)
        {
            if (_inExecute)
            {
                // 重入（cancelChain/interrupt 引发的二次调用）：直接放行原版
                return orig(self);
            }

            _inExecute = true;
            try
            {
                try
                {
                    int cycle = self.get_cycle();
                    if (cycle != CYCLE_THROW)
                    {
                        self.set_cycle(CYCLE_THROW);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[WreckingBallOverhaul] onExecute 段位纠正失败");
                }

                bool result = orig(self);

                try
                {
                    // 只有一个分支：这次就是「丢出一颗新球」
                    TrackNewBall(self);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[WreckingBallOverhaul] 丢球后处理失败");
                }

                return result;
            }
            finally
            {
                _inExecute = false;
            }
        }

        /// <summary>
        /// 连段推进：本模组不需要推进，永远留在丢球段。
        /// 原版会 2 → 3（收回段）；这里直接钉回 2，
        /// 于是下一次攻击又是「丢出一颗新球」，永不进入 1a/2a/收回。
        /// 刻意不调用原版 —— 原版会跑到 isLastCycle 分支并设暴击反馈。
        /// </summary>
        private void OnIncrementCycle(Hook_WreckingBall.orig_incrementCycle orig, WreckingBall self)
        {
            try
            {
                self.set_cycle(CYCLE_THROW);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] incrementCycle 拦截失败");
                orig(self);
            }
        }

        /// <summary>
        /// 被动的 set_cycle(0)：原版 cancelChain() / 连段被打断 / 冷却结束 / 换武器时都会调。
        /// 放任它归 0，下一次攻击就又要靠 onExecute 兜回来；这里直接钉成 2，
        /// 让状态始终自洽。
        /// </summary>
        private int OnSetCycle(Hook_WreckingBall.orig_set_cycle orig, WreckingBall self, int v)
        {
            try
            {
                if (v != CYCLE_THROW)
                {
                    return orig(self, CYCLE_THROW);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] set_cycle 拦截失败");
            }
            return orig(self, v);
        }

        // ==================================================================
        // 球的生命周期追踪
        // ==================================================================

        /// <summary>丢球结束后，从武器身上抓出刚生成的球记下来，并把各项强化打上去。</summary>
        private void TrackNewBall(WreckingBall self)
        {
            if (self == null || self.destroyed) return;

            // 原版 onExecute 在 cycle 2 里会把新球挂到 chainedEntity，
            // 这是唯一可靠拿到「刚丢出那颗」的时机（之后会被 cancelChain 置空）。
            WreckingBallHero ball = AsBall(self.chainedEntity);
            if (ball == null) return;

            // 换球：把上一颗的缩放还原，避免旧球一直顶着一个过期的 ×5
            if (!ReferenceEquals(_scaledBall, null) && !SameHlObj(_scaledBall, ball))
            {
                RestoreScale();
            }

            _ball = ball;
            _scaledBall = ball;
            _scaleBaseX = ball.sprScaleX;
            _scaleBaseY = ball.sprScaleY;
            _ballAge = 0;
            ApplyScaleTo(ball);
            ApplyHitRadius(ball);     // 需求5：判定范围 ×5
            ApplyIgnoreWalls(ball);   // 需求8：丢出即穿墙（不能等落地才设）

            // 需求9：把飞行距离调大（原版 11 格），到最远点时由 reachMaxDist Hook 令其停下
            try
            {
                ball.maxDist = THROW_DISTANCE_TILES * 24.0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] 设置丢出距离失败");
            }

            // 需求6：给这颗球的攻击数据打记号，_AttackUtils.hit 里据此放大伤害
            try
            {
                var atk = ball.atk;
                if (atk != null)
                {
                    atk.addTag(TAG_WRECKING_BALL);
                }
                else
                {
                    Logger.Warning("[WreckingBallOverhaul] 球的 attackData 为空，伤害放大可能不生效");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] 给攻击数据打记号失败");
            }

            Logger.Information($"[WreckingBallOverhaul] 丢出流星锤：模型 ×{SCALE_MUL} " +
                               $"({_scaleBaseX} -> {_scaleBaseX * SCALE_MUL})，" +
                               $"判定半径 ×{SCALE_MUL}，伤害 ×{DAMAGE_MUL}，" +
                               $"无视墙体，最远 {THROW_DISTANCE_TILES} 格");
        }

        private static WreckingBallHero AsBall(Entity e)
        {
            if (e == null || e.destroyed) return null;
            return e as WreckingBallHero;
        }

        // ==================================================================
        // 需求2：丢出的流星锤留在场上
        // ==================================================================

        /// <summary>落地：保留原版震屏/音效（球现在飞到最远点就停，这条主要用于贴地情况的音效表现）。</summary>
        private void OnBallTouchGround(Hook_WreckingBallHero.orig_onTouchGround orig, WreckingBallHero self)
        {
            orig(self);
        }

        /// <summary>撞墙：保留原版震屏/音效（球已开启穿墙，正常不会再触发）。</summary>
        private void OnBallHitWall(Hook_WreckingBallHero.orig_onHitWall orig, WreckingBallHero self)
        {
            orig(self);
        }

        /// <summary>
        /// 掉落弹药：return null 完全阻止「球自行化作弹药被回收」。
        /// 取消回收后的语义是「永不吐弹药」，所以任何情况下都返回 null。
        /// </summary>
        private Ammo OnBallCreateAmmoDrop(Hook_WreckingBallHero.orig_createAmmoDrop orig, WreckingBallHero self)
        {
            try
            {
                if (IsTrackedBall(self)) return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] createAmmoDrop 拦截失败");
            }
            return orig(self);
        }

        /// <summary>
        /// 只要球是本模组追踪的那颗就报告「没有弹药可回收」。
        /// 这一个返回值同时决定：dispose() 不掉弹药、vanish() 走销毁分支、
        /// onHitWall() 不把弹药钉进墙里 —— 正是「常驻不消失」所需要的。
        /// </summary>
        private bool OnBallHasAmmoToRetrieve(Hook_WreckingBallHero.orig_hasAmmoToRetrieve orig,
                                             WreckingBallHero self)
        {
            try
            {
                if (IsTrackedBall(self)) return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] hasAmmoToRetrieve 拦截失败");
            }
            return orig(self);
        }

        /// <summary>
        /// 本模组不再需要「回手」状态；万一原版把球切过去，
        /// 就顺势解除追踪（球会被原版收回，不影响下一次丢球）。
        /// </summary>
        private void OnBallSetGoingBack(Hook_WreckingBallHero.orig_setGoingBackToHero orig,
                                        WreckingBallHero self, bool goingBack)
        {
            if (goingBack && SameHlObj(_ball, self))
            {
                Logger.Information("[WreckingBallOverhaul] 球进入回手状态，解除追踪");
                ClearBall();
            }
            orig(self, goingBack);
        }

        /// <summary>这颗球是否属于本模组追踪的那颗。</summary>
        private static bool IsTrackedBall(WreckingBallHero ball)
        {
            if (ball == null || ball.destroyed) return false;
            if (ball.fromWeapon == null || ball.fromWeapon.destroyed) return false;
            return SameHlObj(_ball, ball);
        }

        // ==================================================================
        // 需求8：丢出的流星锤无视墙体
        // ==================================================================

        /// <summary>
        /// 让球穿墙飞行。
        /// 原版 WreckingBallHero.shootAtAngle() 结尾有三句：
        ///     ignoreWalls = false; ignoreOneWays = false;
        /// 而 Bullet.fixedUpdate() 的移动循环就是读 ignoreWalls / ignoreOneWays
        /// 来决定要不要被墙/单向板挡住，所以必须在丢出之后把它们改回 true。
        /// 另外按 CrossX10 对投掷物的做法把 collisionMode 设成 IgnoreWalls，
        /// 覆盖其它按 collisionMode 判断的路径。
        ///
        /// 注意：不能借用原版 setGoingBackToHero(true) —— 那个函数虽然也会置
        /// ignoreWalls/ignoreOneWays，但它同时把 maxDist 改成 99999，
        /// 会让球在 fixedUpdate 里走「回手」分支，语义不对。
        /// </summary>
        private void ApplyIgnoreWalls(WreckingBallHero ball)
        {
            if (ball is null || ball.destroyed) return;
            try
            {
                ball.ignoreWalls = true;
                ball.ignoreOneWays = true;
                ball.collisionMode = new CollisionMode.IgnoreWalls();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] 设置无视墙体失败");
            }
        }

        // ==================================================================
        // 需求9：飞到最远点就停下（不是销毁）
        // ==================================================================

        /// <summary>
        /// 被追踪的球到达 maxDist 时：清掉速度、停住，而不是原版的 vanish()。
        /// 这就是「丢出去飞到最远点停下」——球会停在它飞出去的最远位置。
        /// 半空中停住是刻意的：需求要的就是「停在丢出去的位置」，
        /// 而不是掉到地上再停。
        /// </summary>
        private void OnBallReachMaxDist(Hook_Bullet.orig_reachMaxDist orig, Bullet self)
        {
            try
            {
                if (self is WreckingBallHero ball && IsTrackedBall(ball))
                {
                    StopBall(ball);
                    return;
                }
            }
            catch { /* 出错时退回原版 */ }
            orig(self);
        }

        /// <summary>把球停住：清速度、关掉重力强度（保留 hasGravity 以避免 vanish 分支）。</summary>
        private void StopBall(WreckingBallHero ball)
        {
            if (ball is null || ball.destroyed) return;
            try
            {
                if (ball.dx == 0.0 && ball.dy == 0.0 && ball.bdx == 0.0 && ball.bdy == 0.0) return;

                ball.dx = 0.0;
                ball.dy = 0.0;
                ball.bdx = 0.0;
                ball.bdy = 0.0;
                ball.enableDefaultGravity(0.0);
                ball.isHeavy = true;
                ApplyIgnoreWalls(ball);

                if (!_loggedStop)
                {
                    _loggedStop = true;
                    Logger.Information($"[WreckingBallOverhaul] 流星锤已飞到最远点并停下" +
                                       $"（maxDist={ball.maxDist}，飞行 {_ballAge} 帧）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] 停下流星锤失败");
            }
        }

        // ==================================================================
        // 需求4：模型 ×5
        // ==================================================================

        /// <summary>球第一次建好图形时，记下基准缩放（此时 TrackNewBall 可能还没跑）。</summary>
        private void OnBallInitGfx(Hook_WreckingBallHero.orig_initGfx orig, WreckingBallHero self)
        {
            orig(self);
            try
            {
                if (self == null || self.destroyed) return;
                if (!SameHlObj(_ball, self)) return;
                _scaleBaseX = self.sprScaleX;
                _scaleBaseY = self.sprScaleY;
                _scaledBall = self;
                ApplyScaleTo(self);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] initGfx 记录基准缩放失败");
            }
        }

        private static void ApplyScaleTo(WreckingBallHero ball)
        {
            if (ball is null || ball.destroyed) return;
            ball.sprScaleX = _scaleBaseX * SCALE_MUL;
            ball.sprScaleY = _scaleBaseY * SCALE_MUL;
        }

        /// <summary>保持当前球的 ×5（球换了就按新基准重算）。</summary>
        private static void MaintainScale(WreckingBallHero ball)
        {
            if (ball is null || ball.destroyed) return;
            ApplyScaleTo(ball);
        }

        private static void ResetScaleCache()
        {
            _scaledBall = null;
            _scaleBaseX = 1.0;
            _scaleBaseY = 1.0;
        }

        /// <summary>把球还原成基准大小（球被替换/销毁时）。</summary>
        private static void RestoreScale()
        {
            WreckingBallHero ball = _scaledBall;
            double bx = _scaleBaseX;
            double by = _scaleBaseY;
            ResetScaleCache();
            if (ball is null || ball.destroyed) return;
            try
            {
                ball.sprScaleX = bx;
                ball.sprScaleY = by;
            }
            catch { /* 球已销毁时忽略 */ }
        }

        // ==================================================================
        // 需求5：判定范围 ×5
        // ==================================================================

        /// <summary>
        /// 把球的命中判定半径乘上倍率（与放大后的模型匹配）。
        /// dc.pr.Level.resolveCircularCollisions() 的判定是
        ///     entity.radius + entity2.radius >= 两中心距离
        /// 所以改 radius 就等价于放大命中范围。
        /// </summary>
        private static void ApplyHitRadius(WreckingBallHero ball)
        {
            if (ball is null || ball.destroyed) return;
            try
            {
                ball.radius = BASE_BALL_RADIUS * SCALE_MUL;
            }
            catch { /* 写不进去时忽略，不影响其它功能 */ }
        }

        /// <summary>解除「球还在场上」的状态（保留缩放缓存以便还原）。</summary>
        private void ClearBall()
        {
            _ball = null;
            _ballAge = 0;
        }

        /// <summary>
        /// 每帧把当前球重新钉成 ×5 缩放、×5 判定半径、并保持穿墙。
        /// Entity.spriteUpdate() 每帧都用 sprScaleX/sprScaleY 重写 sprite.scale，
        /// 单次赋值可能被 hitDistort 等逻辑覆盖，放在 fixedUpdate 之后最稳。
        /// </summary>
        private void OnAnyEntityFixedUpdate(Hook_Entity.orig_fixedUpdate orig, Entity self)
        {
            orig(self);
            try
            {
                WreckingBallHero ball = self as WreckingBallHero;
                if (ball == null) return;

                bool isCurrent = SameHlObj(_ball, ball);
                bool isScaled = SameHlObj(_scaledBall, ball);
                if (!isCurrent && !isScaled) return;

                if (ball.destroyed)
                {
                    if (isCurrent) ClearBall();
                    if (isScaled) RestoreScale();
                    return;
                }

                if (isCurrent)
                {
                    _ballAge++;
                    ApplyHitRadius(ball);     // 需求5：每帧维持判定范围 ×5
                    ApplyIgnoreWalls(ball);   // 需求8：每帧维持，避免被别的逻辑改回去
                    MaintainScale(ball);
                }
                else if (isScaled)
                {
                    // _ball 已经换人：旧球不再是「当前球」，还原它的大小
                    RestoreScale();
                }
            }
            catch { /* 缩放/判定维护失败不影响游戏 */ }
        }

        // ==================================================================
        // 需求6：丢出命中 伤害 ×10（+1000%）
        // ==================================================================

        /// <summary>
        /// 需求6：流星锤命中伤害 ×10（+1000%）。
        ///
        /// 【为什么挂在 _AttackUtils.hit 上】
        ///   流星锤打中敌人走的是
        ///       Bullet.onTouchValidTarget(e) → _AttackUtils.hit(球的 attackData, e)
        ///   这条路径**不会**调用 WreckingBall.hitFromWeapon
        ///   （那是角色近战挥击用的）。所以之前改 strikeChain.power 完全没效果 ——
        ///   球的 AttackData 是在丢出时 _AttackUtils.createFromHeroWeapon 建好的快照，
        ///   之后再改 CDB/power 都不会回头影响它。
        ///
        /// 【为什么改 dmgMultiplier】
        ///   _AttackUtils.hit 第 460 行：num5 = atk.dmgMultiplier; num2 *= num5;
        ///   即最终伤害直接乘这个字段。它是普通 double（不是数组、不是代理字段），
        ///   改动最稳。放大后**必须还原** —— AttackData 来自对象池（_AttackData.POOL），
        ///   不还原会污染后续复用。
        /// </summary>
        private void OnAttackHit(Hook__AttackUtils.orig_hit orig, dc.tool.atk.AttackData atk, Entity target)
        {
            double backup = 0.0;
            bool modified = false;

            try
            {
                if (atk != null && atk.hasTag(TAG_WRECKING_BALL))
                {
                    backup = atk.dmgMultiplier;
                    atk.dmgMultiplier = backup * DAMAGE_MUL;
                    modified = true;

                    Logger.Information($"[WreckingBallOverhaul] 流星锤命中结算：dmgMultiplier " +
                                       $"{backup} -> {atk.dmgMultiplier}（×{DAMAGE_MUL}）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] 命中伤害放大失败");
                modified = false;
            }

            try
            {
                orig(atk, target);
            }
            finally
            {
                if (modified)
                {
                    try { atk.dmgMultiplier = backup; }
                    catch { /* 还原失败不影响已完成的这一次伤害 */ }
                }
            }
        }

        // ==================================================================
        // 资源：挂载 mod 自带的 res.pak（数据补丁：前摇 + 丢出可暴击）
        // ==================================================================
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(WreckingBallOverhaulMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[WreckingBallOverhaul] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[WreckingBallOverhaul] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WreckingBallOverhaul] res.pak 加载失败");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_WreckingBall.onExecute -= OnBallExecute;
            Hook_WreckingBall.incrementCycle -= OnIncrementCycle;
            Hook_WreckingBall.set_cycle -= OnSetCycle;

            Hook_WreckingBallHero.onTouchGround -= OnBallTouchGround;
            Hook_WreckingBallHero.onHitWall -= OnBallHitWall;
            Hook_WreckingBallHero.createAmmoDrop -= OnBallCreateAmmoDrop;
            Hook_WreckingBallHero.hasAmmoToRetrieve -= OnBallHasAmmoToRetrieve;
            Hook_WreckingBallHero.setGoingBackToHero -= OnBallSetGoingBack;

            Hook_Bullet.reachMaxDist -= OnBallReachMaxDist;

            Hook_WreckingBallHero.initGfx -= OnBallInitGfx;
            Hook__AttackUtils.hit -= OnAttackHit;
            Hook_Entity.fixedUpdate -= OnAnyEntityFixedUpdate;

            _ball = null;
            _ballAge = 0;
            ResetScaleCache();
            Logger.Information("[WreckingBallOverhaul] 游戏退出，模组已卸载");
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
