#nullable disable

using dc;
using dc.en;
using dc.hl.types;
using dc.hxd;
using dc.hxd.res;
using dc.level;
using dc.pr;
using dc.tool;
using dc.tool.atk;
using Hashlink.Proxy;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Storage;
using ModCore.Utilities;
using System;
using System.Collections.Generic;

namespace SeismicStompSlam
{
    /// <summary>
    /// 震地冲击（Telluric Shock / 内部 ID <c>SeismicStomp</c>）——「每移动一格自动下砸」模组
    /// =====================================================================================
    /// 【先说清楚技能到底是哪一个】
    ///   这个模组的对象是**主动技能 震地冲击**：
    ///     · 内部 ID        : SeismicStomp      （CDB：item 表 id=SeismicStomp，group=3 → Power 类主动技能）
    ///     · 代码类         : dc.pow.SeismicStomp（继承 dc.Power，由 BeheadedActiveSkillsManager 创建）
    ///     · 英文名 / 中文名 : Telluric Shock / 震地冲击（res/lang/main.en.mo、main.zh.mo）
    ///     · 技能描述        : "跳到空中然后猛然落地，对周围敌人造成 ::+power:: 伤害。"
    ///   注意别和 **破坏球 Wrecking Ball**（内部 ID WreckingBall，流星锤武器）搞混 ——
    ///   那个是武器，不会跃起、也没有石块特效，本模组与它无关。
    ///
    /// 【原版机制（GamePseudocode/dc.pow/SeismicStomp.cs + _SeismicStomp.cs）】
    ///   使用技能时按顺序发生：
    ///     _SeismicStomp.__inst_construct__
    ///       1) o.bump(...) + 播 sfx/hero/jump2.wav + 喷 fxAirDash  → **向上跃起**（前摇）
    ///       2) new ForcedDiveAttack(...)                          → 进入强制下砸状态
    ///       3) 监听 hero.heroSignals.diveAttackLandSignal
    ///    落地时 → SeismicStomp.onOwnerDiveAttackLand()（计算左右两格的地面）
    ///           → SeismicStomp.stompHit(left, -1) / stompHit(right, +1)   ← 真正的「下砸」
    ///   而 stompHit 里做的才是玩家看到的那个效果：
    ///     · 石块/尘土/冲击特效 : fx.khStomp(x, y, fxRc, fxC, fxBigRockTile, fxSmallRockTile)
    ///     · 音效              : sfx/active/stomp_char3.wav
    ///     · 震屏              : viewport.shakeS(0.0, 0.3, 1.0)
    ///     · 范围伤害          : 水平 ±48px（2 格）且 cy∈[scy-1, scy] 的敌人
    ///                           _AttackUtils.createFromHeroItem(hero, item, props.power) + _AttackUtils.hit
    ///
    /// 【本模组的做法 —— 为什么不直接 new SeismicStomp(...)】
    ///   「不用触发跃起」在引擎里其实有官方后门：_SeismicStomp.__inst_construct__ 里
    ///      if (noJump) { arg1.onOwnerDiveAttackLand(); goto IL_0276; }
    ///   也就是 noJump=true 时可以跳过 bump（GoldDigger 铲子砸地就是这么调用的）。
    ///   但即使 noJump=true，构造函数**仍然**会：设置 affect 13/22、new ForcedDiveAttack、
    ///   往 heroSignals.diveCanceledSignal / diveAttackLandSignal 上挂两个监听，
    ///   最后被丢进英雄状态机。本模组是「每移动一格砸一次」（跑动时约每 2~4 帧一次），
    ///   如果每次 new 一个 Power，几秒内就会堆出上百个带信号监听的 Power 实例，
    ///   之后英雄每次落地都会把它们全部触发一遍 —— 必然崩 / 必然雪崩。
    ///
    ///   所以这里**不实例化任何 Power**，而是只复刻 stompHit 那一段「纯粹的落地下砸」：
    ///     特效 + 音效 + 震屏 + 范围伤害，全部调用原版同一条 API。
    ///   于是天然满足需求：
    ///     · 不用触发跃起 → 完全没有 bump / ForcedDiveAttack / jump2 音效
    ///     · 下砸无前后摇 → 完全没有技能前摇(charge)、没有 ForcedDiveAttack 的状态锁、
    ///                      没有 setDurationS(4.0) 的持续期、没有 onEnd 的收尾
    ///     · 每移动一格   → 只在英雄的格子坐标 cx 变化时触发（水平方向）
    ///
    /// 【数值分工】（与 DamageAuraBoost 一致）
    ///   Assets/data.cdb_/item/SeismicStomp.json（res.pak 数据补丁，patch_seismicstomp_cdb.py 生成）
    ///     · effectCD 1.5 → 0.05  ：同一敌人的重复命中间隔（原版用来防止一次砸地打两下；
    ///                              本模组每格都砸，1.5 秒会让绝大多数下砸「只见特效不掉血」）
    ///     · power / bump / distance 等一律保持原版，本代码直接读取，方便随时在数据侧调
    ///   代码（本文件）
    ///     · 触发时机、无跃起/无前摇的落地下砸、范围判定
    ///
    /// 参考：DamageAuraBoost（目录格式 / Assets + cs 搭配 / res.pak 手动挂载）、
    ///       BloodNova（viewport.shakeS）、FlashTeleport（Res.Class.get_loader().loadCache 播音效）。
    /// </summary>
    public class SeismicStompSlamMain : ModBase, IOnGameExit, IOnHeroUpdate, IOnAfterLoadingAssets, IModMenu
    {
        /// <summary>震地冲击的 CDB id（item 表）。</summary>
        private const string SkillId = "SeismicStomp";

        // ==================================================================
        // 选项菜单滑条：hero 模型大小（参考 ZoomVision 的 addSliderWidget 写法）
        // ==================================================================

        /// <summary>英雄模型缩放的可选范围 / 步长。</summary>
        private const double HeroScaleMin = 0.25;
        private const double HeroScaleMax = 5.0;
        private const double HeroScaleStep = 0.05;

        /// <summary>选项菜单持久化配置（存到 mod 自己的配置文件里）。</summary>
        public static Config<Configs> config { get; } = new Config<Configs>("SeismicStompSlam");

        /// <summary>当前要应用的 hero 模型缩放（1.0 = 原版大小）。</summary>
        private static double HeroScale => config.Value.heroScale;

        /// <summary>
        /// 落地下砸的默认配色，取自 _SeismicStomp.__inst_construct__ 里 fxRc / fxC 的默认值。
        /// </summary>
        private const int FxRc = 5840463;
        private const int FxC = 16204843;

        /// <summary>原版 stompHit 的伤害中心：FX 摆在格子中心 (scx+0.5)*24。</summary>
        private const double PixelsPerCase = 24.0;

        /// <summary>原版 stompHit 的水平伤害半径：24px/格 → 48px = 2 格。</summary>
        private const double StompHitRangePx = 48.0;

        /// <summary>
        /// true  = 只有站在地面上时移动才会触发（跳跃/坠落中不触发，石块一定砸在地上）；
        /// false = 只要 cx 变化就触发（空中也会在半空炸出石块）。
        /// 判定方式与原版 _SeismicStomp 一致：检查脚下一格 (cx, cy+1) 是否有实心碰撞。
        /// </summary>
        private const bool RequireGrounded = true;

        /// <summary>
        /// 同一敌人的命中记录上限。跑动久了会累积，超过就整表清空（记录本身只有 1 个 double）。
        /// </summary>
        private const int MaxTrackedEnemies = 1024;

        /// <summary>
        /// 两次下砸之间至少间隔多少帧（0 = 关闭，严格「每移动一格砸一次」）。
        /// 如果觉得跑动时石块特效/音效太密、掉帧，把这个值调大即可（例如 6 ≈ 0.1 秒一次）。
        /// </summary>
        private const int MinFramesBetweenSlams = 0;

        /// <summary>
        /// 每次下砸命中额外附加的固定伤害 —— 碰到敌人就 +9999999。
        ///
        /// 注入点说明（踩过的坑）：
        ///   · AttackData.dmgScaledAdd 看起来像「纯加算」（computeFinalDmg 里是
        ///     scaleHeroValueToTier(baseDmg, tier) + atk.dmgScaledAdd），
        ///     但它在 _AttackUtils.hitInit 里会被清零、紧接着在 updateDamages 里
        ///     被 `atk.dmgScaledAdd = num9`（词缀加成）重新赋值 —— 从外面设进去必然被覆盖，
        ///     所以这条路走不通。
        ///   · 正确位置是 Hook_Entity.applyAttackResult：调用链是
        ///       _AttackUtils.hit → applyHitResult → attackTarget.applyHit(atk)
        ///         → parent.applyAttackResult(atk) → Entity.applyAttackResult 读 a.finalDmg 扣血
        ///     在这里 orig 之前把 finalDmg 加上固定值，正好卡在「扣血前的最后一步」，
        ///     而且顺带绕过了 Boss 的 dmgSoftCap（上限在更早的 computeFinalDmg 里就算完了）。
        ///
        /// 只对自己人（本次下砸刚创建的那个 AttackData）生效，见 _pendingFlatBonus。
        /// 设为 0 即可关闭，只保留震地冲击原本的伤害。
        /// </summary>
        private const double FlatDamageBonus = 9999999.0;

        // ---------------- 状态 ----------------
        /// <summary>上一帧英雄的格子 X（int.MinValue = 尚未初始化）。</summary>
        private int _lastCx = int.MinValue;

        /// <summary>上一帧所在关卡的 HL 地址，用于识别换关并重置状态。</summary>
        private IntPtr _lastLevelPtr = IntPtr.Zero;

        /// <summary>模组自己的时钟（秒），用于 effectCD 判定。</summary>
        private double _clock;

        /// <summary>距离上一次下砸经过了多少次 OnHeroUpdate（供 MinFramesBetweenSlams 限流）。</summary>
        private int _framesSinceSlam;

        /// <summary>缓存的 SeismicStomp CDB 行（dynamic = HaxeProxy 代理对象）。</summary>
        private dynamic _itemData;

        /// <summary>缓存的 props.effectCD（秒）。</summary>
        private double _effectCd = 0.05;

        /// <summary>缓存的 props.bump（击退力度）。</summary>
        private double _bump = 1.0;

        /// <summary>缓存的 props.power（伤害基数，CDB 里是浮点数组）。</summary>
        private object _power;

        /// <summary>缓存的落地下砸音效。</summary>
        private Sound _stompSfx;

        /// <summary>音效加载失败后不再重试（技能没有声音也照常生效）。</summary>
        private bool _sfxUnavailable;

        /// <summary>每个敌人上一次吃到下砸伤害的时间（key = HL 对象地址）。</summary>
        private readonly Dictionary<IntPtr, double> _lastHitAt = new Dictionary<IntPtr, double>();

        /// <summary>累计命中次数（只用于首帧日志）。</summary>
        private int _hitCount;

        /// <summary>
        /// 本次 hit 调用期间需要额外 +9999999 的 AttackData（key = HL 对象真实地址）。
        /// 只在 AttackUtils.hit 调用前后短暂存在，用来把「我们的下砸」和游戏里其它攻击区分开。
        /// </summary>
        private readonly HashSet<IntPtr> _pendingFlatBonus = new HashSet<IntPtr>();

        public SeismicStompSlamMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();

            // 需求「碰到敌人 +9999999 伤害」：挂在扣血前的最后一步
            if (FlatDamageBonus != 0.0)
            {
                Hook_Entity.applyAttackResult += OnEntityApplyAttackResult;
            }

            Logger.Information("[SeismicStompSlam] 已加载：震地冲击(SeismicStomp) —— 水平每移动一格自动下砸；" +
                               "无跃起、无前摇后摇；" +
                               $"命中额外 +{FlatDamageBonus} 固定伤害（Hook_Entity.applyAttackResult）；" +
                               $"选项菜单滑条「Hero size」= hero 模型大小（当前 {HeroScale:0.00}x，" +
                               $"{HeroScaleMin}~{HeroScaleMax}）");
        }

        // ==================================================================
        // 选项菜单：hero 模型大小滑条（形式参考 ZoomVision）
        // ==================================================================

        public string GetName() => "SeismicStompSlam";

        /// <summary>
        /// 在 DCCM 的模组选项页里画一个滑条，用来实时改 hero 的模型大小。
        /// 结构与 ZoomVisionMain.BuildMenu 完全一致：
        ///   改标题 → createScroller → addSliderWidget → updateScroller
        /// 滑条的值通过 ModCore.Storage.Config 持久化，下次进游戏还记得。
        /// </summary>
        public void BuildMenu(dc.ui.Options options)
        {
            ((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(
                StringUtils.AsHaxeString("SEISMIC STOMP SLAM"));

            ((dc.ui.OptionsBase)options).createScroller(0.0);

            ((dc.ui.OptionsBase)options).addSliderWidget(
                StringUtils.AsHaxeString("Hero size"),
                (HlAction<double>)delegate (double v)
                {
                    config.Value.heroScale = v;
                    config.Save();

                    // 菜单里拖动时立刻生效（不用等回到游戏）
                    TryApplyHeroScaleNow();
                },
                HeroScale,
                Ref<double>.In(HeroScaleStep),
                ((dc.ui.OptionsBase)options).scrollerFlow,
                Ref<bool>.In(false),                 // showPercent
                Ref<bool>.In(true),                  // showRawValue（显示 1.00 / 2.50 这种）
                Ref<double>.In(HeroScaleMin),
                Ref<double>.In(HeroScaleMax),
                null,
                Ref<int>.In(0));

            ((dc.ui.OptionsBase)options).updateScroller();
        }

        /// <summary>
        /// hero 模型大小 —— 参考 ShrinkOnKillMain：直接钉 sprScaleX / sprScaleY。
        /// 这两个是「精灵缩放源值」，游戏每帧会用它们重算 sprite.scale，
        /// 所以每帧维持一次最稳（跨关卡、换头、复活都不会丢）。
        /// </summary>
        private static void ApplyHeroScale(Hero hero)
        {
            try
            {
                double s = HeroScale;
                if (s <= 0.0) return;
                if (System.Math.Abs(hero.sprScaleX - s) > 0.0005 ||
                    System.Math.Abs(hero.sprScaleY - s) > 0.0005)
                {
                    hero.sprScaleX = s;
                    hero.sprScaleY = s;
                }
            }
            catch { /* 体型设置失败不影响下砸逻辑 */ }
        }

        /// <summary>在选项菜单里拖滑条时，立即把新尺寸贴到当前 hero 上。</summary>
        private static void TryApplyHeroScaleNow()
        {
            try
            {
                Hero hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero != null && !hero.destroyed) ApplyHeroScale(hero);
            }
            catch { /* 菜单里没有 hero 时忽略 */ }
        }

        /// <summary>资源加载完成：手动挂载 mod 自带的 res.pak（effectCD 数据补丁）。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(SeismicStompSlamMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[SeismicStompSlam] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[SeismicStompSlam] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SeismicStompSlam] res.pak 加载失败");
            }
        }

        // ==================================================================
        // 触发：每水平移动一格
        // ==================================================================

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            _clock += dt;

            try
            {
                Hero hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero == null || hero.destroyed)
                {
                    _lastCx = int.MinValue;
                    return;
                }

                // 体型：每帧维持一次（选项菜单滑条的值），跨关卡不丢
                ApplyHeroScale(hero);

                if (hero._level == null || hero.life <= 0)
                {
                    _lastCx = int.MinValue;
                    return;
                }

                // 换关：重置，避免拿上一关的 cx 做比较
                IntPtr levelPtr = hero._level.HashlinkPointer;
                if (levelPtr != _lastLevelPtr)
                {
                    _lastLevelPtr = levelPtr;
                    _lastCx = hero.cx;
                    _lastHitAt.Clear();
                    return;
                }

                if (_lastCx == int.MinValue)
                {
                    _lastCx = hero.cx;
                    return;
                }

                int cx = hero.cx;
                if (cx == _lastCx) return;

                int delta = cx - _lastCx;
                _lastCx = cx;

                // |Δcx| > 1 不是「走一格」，而是传送/关卡重定位，不触发
                if (delta > 1 || delta < -1) return;

                if (RequireGrounded && !IsStandingOnGround(hero)) return;

                _framesSinceSlam++;
                if (MinFramesBetweenSlams > 0 && _framesSinceSlam < MinFramesBetweenSlams) return;

                _framesSinceSlam = 0;
                Slam(hero);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SeismicStompSlam] 下砸触发失败");
            }
        }

        // ==================================================================
        // 落地下砸：特效 + 音效 + 震屏 + 范围伤害（复刻 SeismicStomp.stompHit）
        // ==================================================================

        private void Slam(Hero hero)
        {
            dynamic itemData = GetItemData();
            if (itemData == null) return;

            Level level = hero._level;
            if (level == null || level.fx == null) return;

            // 与原版一致：冲击点落在英雄所在格的中心 / 该格底部
            double centerPx = (hero.cx + 0.5) * PixelsPerCase;
            int scy = hero.cy;
            double groundPy = (scy + 1) * PixelsPerCase;

            // ① 特效：石块 + 尘土 + 冲击波
            //    与原版 stompHit 完全同一条调用；后两个参数传 null 时
            //    khStomp 内部会自己从 Assets.fxTile._fxBigRock / _fxDirt 随机取石块图块。
            level.fx.khStomp(centerPx, groundPy, (int?)FxRc, (int?)FxC, null, null);

            // ② 音效（sfx/active/stomp_char3.wav，与技能落地同一音源）
            PlayStompSfx(level, centerPx, scy * PixelsPerCase);

            // ③ 震屏
            level.viewport.shakeS(0.0, 0.3, 1.0);

            // ④ 范围伤害
            ApplySlamDamage(hero, centerPx, scy);
        }

        /// <summary>
        /// 复刻 SeismicStomp.stompHit 的伤害段：
        ///   水平 |enemy.px - 中心| &lt; 48px（2 格）且 enemy.cy ∈ [scy-1, scy]，
        ///   命中后按方向击退。
        /// 原版是左右两格各调一次 stompHit；本模组把冲击点放在英雄自己脚下，
        /// 因此等价于「以英雄为中心、半径 2 格」的范围。
        /// </summary>
        private void ApplySlamDamage(Hero hero, double centerPx, int scy)
        {
            if (_power == null) return;

            Team team = hero._team;
            if (team == null) return;

            TeamIterator it = team.opponentsIterator.reset(team);
            if (it == null) return;

            if (_lastHitAt.Count > MaxTrackedEnemies) _lastHitAt.Clear();

            int? tier = RelevantTier(hero);

            while (it.hasNext())
            {
                Entity enemy = it.next();
                if (enemy == null || enemy.destroyed || enemy.life <= 0) continue;
                if (!enemy.canBeHitBy(hero)) continue;

                // 原版：只砸「和英雄同一格 + 上一格」高度的敌人
                if (enemy.cy < scy - 1 || enemy.cy > scy) continue;

                double enemyPx = ((double)enemy.cx + enemy.xr) * PixelsPerCase;
                double dx = enemyPx - centerPx;
                double dist = dx >= 0.0 ? dx : -dx;
                if (dist >= StompHitRangePx) continue;

                // 原版用 props.effectCD 做「同一敌人重复命中间隔」（数据补丁降到 0.05s）
                if (IsOnHitCooldown(enemy)) continue;

                AttackData atk = AttackUtils.Class.createFromHero.Invoke(hero, _power, tier);
                if (atk == null) continue;

                // 与原版 stompHit 完全一致的 3 个 tag
                atk.addTag(7);    // 无视护盾/招架类判定
                atk.addTag(9);
                atk.addTag(14);   // 远程标记（狂怒/连击等变异据此区分）

                // 需求：碰到敌人 +9999999 伤害
                // 这里只做「标记」，真正的加算在 OnEntityApplyAttackResult（扣血前一步）里做，
                // 因为 hit 内部的 updateDamages 会覆盖 dmgScaledAdd。
                IntPtr atkKey = FlatDamageBonus != 0.0 ? SafePointer(atk) : IntPtr.Zero;
                if (atkKey != IntPtr.Zero) _pendingFlatBonus.Add(atkKey);
                try
                {
                    AttackUtils.Class.hit.Invoke(atk, enemy);
                }
                finally
                {
                    if (atkKey != IntPtr.Zero) _pendingFlatBonus.Remove(atkKey);
                }

                if (!atk.isSuccess()) continue;

                MarkHit(enemy);

                double dir = dx >= 0.0 ? 1.0 : -1.0;
                try { enemy.bump(dir * _bump, 0.3, null); } catch { /* 击退失败不影响伤害 */ }

                _hitCount++;
                if (_hitCount == 1)
                {
                    try
                    {
                        Logger.Information($"[SeismicStompSlam] 首次命中确认：finalDmg={atk.finalDmg}, " +
                                           $"rawFinalDmg={atk.rawFinalDmg}, dmgScaledAdd={atk.dmgScaledAdd}");
                    }
                    catch { /* 日志失败不影响战斗 */ }
                }
            }
        }

        // ==================================================================
        // 需求：碰到敌人 +9999999 伤害
        // ==================================================================

        /// <summary>
        /// 扣血前最后一步：Entity.applyAttackResult(a) 会读 a.finalDmg 扣血。
        /// 这里在 orig 之前把固定加算补上，只对 _pendingFlatBonus 里登记过的
        /// （也就是本模组下砸刚创建的）AttackData 生效 —— 武器/怪物/原版技能的伤害不受影响。
        ///
        /// 调用链：_AttackUtils.hit → applyHitResult
        ///         → attackTarget.applyHit(atk) → parent.applyAttackResult(atk) → 本 Hook
        /// </summary>
        private void OnEntityApplyAttackResult(Hook_Entity.orig_applyAttackResult orig,
                                               Entity self, AttackData attack)
        {
            try
            {
                if (_pendingFlatBonus.Count > 0 && attack != null)
                {
                    IntPtr key = SafePointer(attack);
                    if (key != IntPtr.Zero && _pendingFlatBonus.Contains(key))
                    {
                        attack.finalDmg = attack.finalDmg + (int)FlatDamageBonus;
                    }
                }
            }
            catch { /* 加算失败就按原伤害结算，不影响游戏 */ }

            orig(self, attack);
        }

        // ==================================================================
        // 数据 / 工具
        // ==================================================================

        /// <summary>读取并缓存 SeismicStomp 的 CDB 行与用到的几个 props。</summary>
        private dynamic GetItemData()
        {
            if (_itemData != null) return _itemData;

            try
            {
                dynamic row = Data.Class.item.byId.get(ToHaxeString(SkillId));
                if (row == null)
                {
                    Logger.Error($"[SeismicStompSlam] CDB 里找不到 item/{SkillId}");
                    return null;
                }

                _itemData = row;
                try { _effectCd = (double)row.props.effectCD; } catch { _effectCd = 0.05; }
                try { _bump = (double)row.props.bump; } catch { _bump = 1.0; }
                try { _power = (object)row.props.power; } catch { _power = null; }

                Logger.Information($"[SeismicStompSlam] 已读取 {SkillId} 数据：" +
                                   $"power={DescribePower(_power)}, bump={_bump}, effectCD={_effectCd}s");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SeismicStompSlam] 读取 SeismicStomp 数据失败");
            }

            return _itemData;
        }

        private static string DescribePower(object power)
        {
            try
            {
                if (power is ArrayBytes_Float arr) return $"[{arr.length} 项]";
                return power?.ToString() ?? "null";
            }
            catch { return "?"; }
        }

        /// <summary>
        /// 英雄的相关 tier：SeismicStomp 的 tier1=Brutality、tier2=Survival，
        /// 与原版 Hero.getRelevantTierFor(item) 的取法一致（取两者较高的那个）。
        /// </summary>
        private static int? RelevantTier(Hero hero)
        {
            try
            {
                int a = hero.brutalityTier;
                int b = hero.survivalTier;
                return a >= b ? a : b;
            }
            catch
            {
                return null;
            }
        }

        private bool IsOnHitCooldown(Entity enemy)
        {
            if (_effectCd <= 0.0) return false;
            IntPtr key = SafePointer(enemy);
            if (key == IntPtr.Zero) return false;
            return _lastHitAt.TryGetValue(key, out double t) && (_clock - t) < _effectCd;
        }

        private void MarkHit(Entity enemy)
        {
            IntPtr key = SafePointer(enemy);
            if (key != IntPtr.Zero) _lastHitAt[key] = _clock;
        }

        private static IntPtr SafePointer(HaxeObject o)
        {
            try { return o == null ? IntPtr.Zero : o.HashlinkPointer; }
            catch { return IntPtr.Zero; }
        }

        /// <summary>碰撞位：0x200 = 斜坡（原版 _SeismicStomp 判断「站在斜坡上」用的就是这一位）。</summary>
        private const int CollisionSlope = 0x200;

        /// <summary>
        /// 英雄是否踩在地面上（脚下有落脚点）。
        /// 判定思路与原版 _SeismicStomp.__inst_construct__ 的「能否原地起跳」一致：
        ///   · 脚下那一格 (cx, cy+1) 有任何落脚碰撞（实心 / 平台 / 斜坡）→ 站在地上；
        ///   · 否则再看自己所在格是否带斜坡位。
        /// 纯滞空（脚下是空气）时返回 false，从而不会在半空炸出石块。
        /// LevelMap.collisions 是一维数组，下标 = cy * wid + cx（单位 4 字节）。
        /// </summary>
        private static unsafe bool IsStandingOnGround(Hero hero)
        {
            LevelMap map = hero._level?.map;
            if (map == null) return false;

            int cx = hero.cx;
            int cy = hero.cy;

            int below = TryReadCollision(map, cx, cy + 1);
            if (below == NoCollision) return false;   // 脚下越界 → 认为悬空
            if (below != 0) return true;              // 脚下有落脚点

            int here = TryReadCollision(map, cx, cy);
            return here != NoCollision && (here & CollisionSlope) != 0;
        }

        /// <summary>读取失败（越界 / 无可读内存）时的哨兵值。</summary>
        private const int NoCollision = int.MinValue;

        private static unsafe int TryReadCollision(LevelMap map, int cx, int cy)
        {
            int wid = map.wid;
            int hei = map.hei;
            if (wid <= 0 || hei <= 0) return NoCollision;
            if (cx < 0 || cy < 0 || cx >= wid || cy >= hei) return NoCollision;

            ArrayBytes_Int collisions = map.collisions;
            if (collisions == null) return NoCollision;

            int index = cy * wid + cx;
            if (index < 0 || index >= collisions.length) return NoCollision;

            IntPtr bytes = collisions.bytes;
            if (bytes == IntPtr.Zero) return NoCollision;

            return *(int*)((byte*)bytes.ToPointer() + (index << 2));
        }

        /// <summary>播放落地下砸音效（与原版 stompHit 同一个 wav）。</summary>
        private void PlayStompSfx(Level level, double x, double y)
        {
            if (_sfxUnavailable || level.lAudio == null) return;

            try
            {
                if (_stompSfx == null)
                {
                    _stompSfx = (Sound)Res.Class.get_loader()
                        .loadCache(ToHaxeString("sfx/active/stomp_char3.wav"), Sound.Class);
                }
                if (_stompSfx == null)
                {
                    _sfxUnavailable = true;
                    return;
                }
                level.lAudio.playEventAt(_stompSfx, x, y, null, null, null);
            }
            catch (Exception ex)
            {
                _sfxUnavailable = true;
                Logger.Warning($"[SeismicStompSlam] 音效加载/播放失败，后续静音：{ex.Message}");
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        void IOnGameExit.OnGameExit()
        {
            if (FlatDamageBonus != 0.0)
            {
                Hook_Entity.applyAttackResult -= OnEntityApplyAttackResult;
            }

            _lastCx = int.MinValue;
            _lastLevelPtr = IntPtr.Zero;
            _itemData = null;
            _power = null;
            _stompSfx = null;
            _lastHitAt.Clear();
            _pendingFlatBonus.Clear();
            Logger.Information("[SeismicStompSlam] 游戏退出，模组已卸载");
        }
    }

    /// <summary>
    /// 选项菜单持久化配置（参考 ZoomVision 的 Configs）。
    /// 用的是 public 字段 —— ModCore.Storage.Config&lt;T&gt; 按字段名读写。
    /// </summary>
    public class Configs
    {
        /// <summary>hero 模型大小倍率（1.0 = 原版；选项菜单滑条可调）。</summary>
        public double heroScale = 1.0;
    }
}
