#nullable disable

using dc;
using dc.en;
using dc.en.bu;
using dc.hl.types;
using dc.tool;              // Weapon / WeaponSkill（WeaponSkill : OldSkill）
using dc.tool.skill;        // Hook_OldSkill / OldSkill（dc.tool.skill.Hook_OldSkill）
using dc.tool.weap;         // BaseBow / Hook_BaseBow（都不在 .bow 子命名空间里）
using dc.tool.weap.bow;     // SonicCrossbow / Hook_SonicCrossbow
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SonicCrossbowOverhaul
{
    /// <summary>
    /// SonicCrossbow（音波弩 / Carabine sonique）改造
    /// =====================================================================
    /// 需求：
    ///   1) 子弹随机角度散射
    ///   2) 射程 +20%
    ///   3) 子弹穿墙
    ///
    /// 该武器本身已是机关枪（weapon.props.tick = 0.13，BaseBow.autoFireTickS 驱动连射），
    /// 所以不改射速 / 弹量 / 连射机制，只改「子弹效果」。
    ///
    /// =====================================================================
    /// 【为什么挂在「子弹」上，而不是 SonicCrossbow.shoot 上】
    ///
    /// 第一版把逻辑挂在 Hook_SonicCrossbow.shoot 上，实测**一次都没触发**：
    /// 该局日志里 ChronoBlade 的 Hook_Weapon.onExecute 对 SonicCrossbow 记到 20 次开火，
    /// 而本模组 `游戏退出（射击 0 次，改写 0 发）`。
    /// 即：Weapon.onExecute 正常，但 SonicCrossbow.shoot 的 Hook 没进 —— 说明这条
    /// 「基类 virtual shoot → 子类 override」的调用链在 Hashlink 层不是走 SonicCrossbow.shoot
    /// 那个函数槽，挂在子类方法上不可靠（MDK 的 Hook 是拦截 Hashlink 函数入口，
    /// 子类 override 的函数槽与实际调用点不一定是同一个）。
    ///
    /// 所以子弹部分改成挂**子弹自己的生命周期**，与武器怎么发射完全解耦；
    /// 攻速部分挂**技能的速度读取点**：
    ///
    ///   Hook_Entity.init         —— 实体初始化（Bullet / SonicBolt 共用 Entity 这一个 init 槽）
    ///                                · 认出 SonicBolt（其它实体直接跳过）
    ///                                · ignoreWalls = true      → 穿墙
    ///                                · 登记进 _tracked
    ///   Hook_Entity.initGfx      —— 图形初始化，**orig 之前**改 color → 每发随机颜色
    ///   Hook_Bullet.fixedUpdate  —— 第一帧做两件事，之后就不再管
    ///                                · 旋转速度矢量 (dx,dy)    → 散射
    ///                                · maxDist ×= 4.8          → 距离翻倍
    ///   Hook_OldSkill.getCastSpeed     —— 起手 / 动画速度 ×6
    ///   Hook_OldSkill.getCooldownSpeed —— 冷却速度 ×6（射速的真瓶颈）
    ///
    /// ⚠ 为什么是 Hook_Entity.init 而不是 Hook_SonicBolt.init：
    ///   反查 GameProxy 的生成包装类，`dc.Hook_Entity` 才有 `init` 事件
    ///   （delegate `orig_init(Entity self)`）；`dc.en.Hook_Bullet` 里**没有** init，
    ///   子类生成物 `dc.en.bu.Hook_SonicBolt` 里也**没有** init。
    ///   也就是说 Bullet / SonicBolt 的 init 全部走 Entity 那一个函数槽 ——
    ///   挂在子类生成物上的事实就是「订阅成功但永不触发」（和第一版
    ///   Hook_SonicCrossbow.shoot 同样的坑）。
    ///
    /// 关键：散射与射程都放在 fixedUpdate 第一帧，原因是原版赋值顺序
    ///
    ///     new SonicBolt(hero, atk, angle, 1.5, color, hasLight: true);
    ///         └─ 构造函数内部 init()                    ★ 此时 dx/dy 已按 angle/spd 写好
    ///     bolt.init();
    ///     bolt.radius = 19.2;
    ///     bolt.shootFromWeapon(this, ...);        // 只读 dx/dy 定方向，不改它
    ///     bolt.offsetOrigin(0, 3 * sin(shootIdx * 2.1));
    ///     bolt.maxDist = props.range * 24;        // ★ maxDist 在这里才写入（264px）
    ///     bolt.pierceCount += 999;
    ///     bulletsOut.push(bolt);
    ///
    ///   → init 时 maxDist 还没赋值（放 init 会被原版覆盖），
    ///     但到了 fixedUpdate 第一帧：dx/dy 已是最终值、maxDist 已是基线 264。
    ///     两件事一起在第一帧做，一次到位，既不会被覆盖也不会重复叠乘。
    ///
    /// ⚠ 随机颜色为什么必须挂在 initGfx 的 **orig 之前**：
    ///   Entity.init() 内部会调用 initGfx()，而 SonicBolt.initGfx() 里
    ///       hSprite.color = ...（由 color 算出的染色）、if (hasLight) createLight(color, ...)
    ///   都是**读** color 的。等 init 跑完（即 orig 之后）再改，精灵与光照已经染好了，
    ///   改了也看不见 —— 必须在 orig 之前把 color 换掉。
    ///
    /// ⚠ 攻速为什么必须同时挂 getCastSpeed **和** getCooldownSpeed：
    ///   OldSkill.fixedUpdate 里两条计时器走的是**不同**公式：
    ///       chargeF   -= tmod * getCastSpeed()        // 起手（WeaponSkill 里 ×get_attackSpeed()）
    ///       coolDownF -= tmod * getCooldownSpeed()    // 冷却（只等于 cooldownSpeedMul，与攻速无关！）
    ///   只放大 get_attackSpeed / getCastSpeed 会变成「起手快了，但每发之间照样等原来那么久」，
    ///   对机关枪这种「冷却即射速」的武器表现就是**射速没变** —— 这正是上一版 ×3 无效的原因。
    ///
    ///   ⚠ 但**不要**再挂 Hook_Weapon.get_attackSpeed：它已经被
    ///   WeaponSkill.getCastSpeed() 乘进 castSpeed 了，如果那里也 ×6，
    ///   castSpeed 就会变成 6×6=36 倍（双重叠乘）。只挂 OldSkill.getCastSpeed 即可，
    ///   它同时覆盖「起手」与「动画」（Weapon.cs:9261 `castSpeed = get_curSkill().getCastSpeed()`）。
    ///
    /// 【射程为什么只用 maxDist】
    ///   Bullet.getCoveredDistSqr() = |(cx+xr, cy+yr-hei/2) - (ox, oy)|²，ox/oy 是出生点；
    ///   fixedUpdate() 末尾：
    ///       if (!(getCoveredDistSqr() <= maxDist * maxDist)) { onReachMaxDist(); reachMaxDist(); }
    ///   所以 maxDist 就是「真实飞行距离上限」，乘 1.2 即精确 +20%。
    ///   不需要自管射程 / 逐帧记账 / 手动 destroy（第一版那套是多余的，且容易出 bug）。
    /// =====================================================================
    /// </summary>
    public class SonicCrossbowOverhaulMain : ModBase, IOnGameExit
    {
        // ============================ 可调参数 ============================

        /// <summary>
        /// 散射角最大偏移（弧度）。
        /// 每发在 [-SPREAD_MAX, +SPREAD_MAX] 之间独立随机，形成扇形。
        ///
        /// 0.88 rad ≈ **50.4°**（再翻一倍：0.44 → 0.88）。总张角 = 2 × SPREAD_MAX ≈ **100.8°**。
        /// 原版只有 ±0.01 rad（≈0.57°），基本等于不散。
        /// </summary>
        private const double SPREAD_MAX = 0.88;

        /// <summary>
        /// 射程倍率（再翻一倍：2.40 → 4.80）。
        /// 原版 maxDist = props.range(11) × 24 = 264px（约 11 格）
        /// → ×4.8 = **1267.2px（约 52.8 格）**。
        /// </summary>
        private const double RANGE_MULT = 4.80;

        /// <summary>
        /// 攻速倍率（再翻一倍：3.0 → 6.0，即快 6 倍）。
        ///
        /// ⚠ 这里必须同时挂**两个**读取点，只挂 get_attackSpeed 是不够的 ——
        /// 见下方 OnGetSkillCastSpeed / OnGetSkillCooldownSpeed 的说明：
        ///   · chargeF  由 getCastSpeed()    驱动   ← WeaponSkill 里有 ×get_attackSpeed()
        ///   · coolDownF 由 getCooldownSpeed() 驱动  ← **只等于 cooldownSpeedMul，与攻速无关**
        /// 只放大 get_attackSpeed 只缩短起手、不缩短冷却，
        /// 对机关枪而言冷却才是瓶颈，所以表现为「射速没变」。
        /// </summary>
        private const double ATTACK_SPEED_MULT = 6.0;

        /// <summary>
        /// 按住不放时的**自动连射间隔**（秒）—— 这才是音波弩「机关枪射速」的真瓶颈。
        ///
        /// 来源：`_BaseBow.__inst_construct__` 里不管条件如何都会
        ///       `arg1.autoFireTickS = 0.1;`
        /// 而 `_BaseBow` 的连射回调 `ArrowFunctionEntry_36220(sr)` 里：
        ///       if (!(sr &lt;= 1.0)) {                    // 蓄满后持续按住
        ///           double num3 = autoFireTickS * owner.cd.baseFps;
        ///           IntMap fastCheck = cd.fastCheck;
        ///           int length = 1277165568;            // ★ 专用冷却 key
        ///           if (fastCheck.exists(1277165568)) flag2 = true;   // 冷却中 → 这一帧不开火
        ///           else { ...; cdInst2 = new CdInst(1277165568, num5); fastCheck.set(...); }
        ///       }
        ///   flag2 为真才会真正走 execute/开火。也就是说**每发之间固定间隔 0.1 秒**，
        ///   由一个**独立的冷却 key** 控制，跟 chargeF / coolDownF 完全是两套东西。
        ///
        /// 所以我之前挂的 `OldSkill.getCastSpeed` / `getCooldownSpeed`（×6）动不了它 ——
        /// 那两条只影响「蓄力推进」和「技能冷却」，而机关枪的射速被这个 tick 死死卡住。
        /// 把 autoFireTickS 改小才是真正提高射速。
        /// </summary>
        private const double DEFAULT_AUTO_FIRE_TICK = 0.1;

        /// <summary>是否让弹丸穿墙（需求 3）。</summary>
        private const bool WALL_PIERCE = true;

        /// <summary>是否让每一发音波弹随机颜色。</summary>
        private const bool RANDOM_COLOR = true;

        /// <summary>
        /// 随机颜色模式：
        ///   true  —— 全色相随机（R/G/B 各 0~255，颜色鲜艳但不保证亮度）；
        ///   false —— 只随机色相、亮度固定（更好看、更像能量弹）。
        /// </summary>
        private const bool WILD_COLOR = true;

        /// <summary>日志采样上限，避免刷屏。</summary>
        private const int LOG_SAMPLES = 12;

        private const bool DEBUG_LOG = true;

        // ============================ 运行状态 ============================

        private static readonly Random _random = new Random();

        /// <summary>本模组处理过的音波弹：是否已加散射 / 是否已加射程。</summary>
        private sealed class Tracked
        {
            public bool Spread;   // 散射已应用
            public bool Range;    // 射程已应用
        }

        private readonly Dictionary<int, Tracked> _tracked = new Dictionary<int, Tracked>();

        private int _spawned;      // 见过的音波弹
        private int _spread;       // 已加散射
        private int _ranged;       // 已加射程
        private int _recolored;    // 已随机颜色
        private int _tickRewrites; // 已压低连射间隔的帧数
        private int _failures;     // 异常

        private string _lastDebug;

        public SonicCrossbowOverhaulMain(ModInfo info) : base(info) { }

        // ==================================================================
        //  初始化 / 卸载
        // ==================================================================

        public override void Initialize()
        {
            base.Initialize();

            // 出生登记：Entity.init 是所有实体（含 Bullet）唯一的 init 函数槽
            // —— SonicBolt / Bullet 都没有自己的 init hook 槽（只有 dc.Hook_Entity.init），
            //    这正是上一版 Hook_SonicBolt.init 挂不上、静默失效的原因。
            Hook_Entity.init += OnEntityInit;

            // 随机颜色必须赶在 initGfx 之前改 color（initGfx 里用 color 算 sprite 染色）
            Hook_Entity.initGfx += OnEntityInitGfx;

            // 全部子弹改写都在 fixedUpdate 第一帧做（此时 dx/dy 与 maxDist 都已是最终基线）
            Hook_Bullet.fixedUpdate += OnBulletFixedUpdate;

            // ★ 攻速的真瓶颈：BaseBow 的自动连射间隔 autoFireTickS（默认 0.1s）
            Hook_BaseBow.fixedUpdate += OnBowFixedUpdate;

            // 辅助：起手 / 动画（getCastSpeed）与技能冷却（getCooldownSpeed）
            // 这两条不是机关枪的瓶颈，但一起放大可以让蓄力与动画同步跟上，观感更连贯。
            Hook_OldSkill.getCastSpeed += OnGetSkillCastSpeed;
            Hook_OldSkill.getCooldownSpeed += OnGetSkillCooldownSpeed;

            // 兜底：万一某条路径真的走了 SonicCrossbow.shoot，这里也能立刻改（带去重）
            Hook_SonicCrossbow.shoot += OnShoot;

            Logger.Information("[SonicCrossbowOverhaul] 已加载：散射 ±" + Deg(SPREAD_MAX)
                + "° / 射程 ×" + RANGE_MULT.ToString("0.##")
                + "（" + (264.0 * RANGE_MULT).ToString("0") + "px ≈ "
                + (11 * RANGE_MULT).ToString("0.#") + " 格）/ 攻速 ×"
                + ATTACK_SPEED_MULT.ToString("0.##")
                + "（连射间隔 " + DEFAULT_AUTO_FIRE_TICK.ToString("0.###") + "s → "
                + (DEFAULT_AUTO_FIRE_TICK / ATTACK_SPEED_MULT).ToString("0.####") + "s）"
                + " / 穿墙=" + WALL_PIERCE
                + " / 随机颜色=" + RANDOM_COLOR
                + " | hooks: Entity.init + Entity.initGfx + Bullet.fixedUpdate"
                + " + BaseBow.fixedUpdate + OldSkill.getCastSpeed + OldSkill.getCooldownSpeed"
                + " (+SonicCrossbow.shoot 兜底)");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Entity.init -= OnEntityInit;
            Hook_Entity.initGfx -= OnEntityInitGfx;
            Hook_Bullet.fixedUpdate -= OnBulletFixedUpdate;
            Hook_BaseBow.fixedUpdate -= OnBowFixedUpdate;
            Hook_OldSkill.getCastSpeed -= OnGetSkillCastSpeed;
            Hook_OldSkill.getCooldownSpeed -= OnGetSkillCooldownSpeed;
            Hook_SonicCrossbow.shoot -= OnShoot;
            _tracked.Clear();
            Logger.Information("[SonicCrossbowOverhaul] 游戏退出（生成 " + _spawned
                + " 发 / 散射 " + _spread + " / 射程 " + _ranged
                + " / 随机颜色 " + _recolored
                + " / 攻速改写 " + _tickRewrites
                + " / 异常 " + _failures + "）");
        }

        // ==================================================================
        //  Hook 0：BaseBow.fixedUpdate —— 连射间隔 autoFireTickS（射速真瓶颈）
        // ==================================================================

        /// <summary>
        /// 把音波弩的自动连射间隔压到 `DEFAULT_AUTO_FIRE_TICK / ATTACK_SPEED_MULT`。
        ///
        /// 为什么每帧都要设：`BaseBow.onBowCharging()` 系列回调每帧都会把
        /// `autoFireTickS` 重新写回 cdb 的 props.tick（SonicCrossbow 是 0.1），
        /// 所以一次性设置会被下一帧覆盖，必须每帧压住。
        ///
        /// 时序说明：在 `orig(self)` **之前**写。原版 `BaseBow.fixedUpdate()` 只推
        /// `bowChargeF` 和处理满蓄力打断，**不读** autoFireTickS；
        /// 真正读它的是连射回调 `ArrowFunctionEntry_36220`（由 dynOnCharging 触发）。
        /// 而它每帧都会把值重置回 baseline，所以「原版之前压低」无论回调在本帧
        /// 之前还是之后执行都成立：早于回调 → 本帧立即生效；晚于回调 → 本帧用的是
        /// 上一帧我们压好的值（同样是压低后的）。两种情况都不会漏。
        /// </summary>
        private void OnBowFixedUpdate(Hook_BaseBow.orig_fixedUpdate orig, BaseBow self)
        {
            if (self is SonicCrossbow bolt)
            {
                try
                {
                    double want = DEFAULT_AUTO_FIRE_TICK / ATTACK_SPEED_MULT;
                    if (bolt.autoFireTickS > want)
                    {
                        bolt.autoFireTickS = want;
                        _tickRewrites++;
                        if (DEBUG_LOG && _tickRewrites <= LOG_SAMPLES)
                        {
                            Debug("[SonicCrossbowOverhaul] 连射间隔 "
                                + DEFAULT_AUTO_FIRE_TICK.ToString("0.###") + "s → "
                                + want.ToString("0.####") + "s（攻速 ×"
                                + ATTACK_SPEED_MULT.ToString("0.##") + "）");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fail("改写 autoFireTickS 失败", ex);
                }
            }

            orig(self);
        }

        // ==================================================================
        //  Hook 0：攻速 —— 起手（castSpeed）+ 冷却（cooldownSpeed）
        // ==================================================================

        /// <summary>
        /// 把 SonicCrossbow 的**起手速度**放大 ×6。
        ///
        /// 链路：WeaponSkill.getCastSpeed() = OldSkill.getCastSpeed() × weapon.get_attackSpeed()
        /// OldSkill.fixedUpdate 里 `chargeF -= tmod * getCastSpeed()`，攒到 hitFrame 才出手；
        /// 同时它也是动画速度（Weapon.dynOnAttackAnim 里 animInstance.speed = animSpd × castSpeed）。
        ///
        /// 挂在 OldSkill 而不是 Weapon 上，是为了能通过 (self as WeaponSkill).weapon
        /// 精确判断「这是不是音波弩的技能」，比 hook get_attackSpeed 更直接、更不容易波及别人。
        /// </summary>
        private double OnGetSkillCastSpeed(Hook_OldSkill.orig_getCastSpeed orig, OldSkill self)
        {
            double castSpeed = orig(self);
            if (IsSonicCrossbowSkill(self))
            {
                return castSpeed * ATTACK_SPEED_MULT;
            }
            return castSpeed;
        }

        /// <summary>
        /// 把 SonicCrossbow 的**冷却速度**放大 ×6 —— 这才是「射速」的真瓶颈。
        ///
        /// OldSkill.fixedUpdate 里冷却走的是**另一条**公式：
        ///     coolDownF -= tmod * getCooldownSpeed()          // ← 不是 getCastSpeed！
        /// 而 OldSkill.getCooldownSpeed() 只返回 `cooldownSpeedMul`（Taunt 时再乘个常数），
        /// **完全不含 weapon.get_attackSpeed()**。
        /// 所以只放大 get_attackSpeed 会出现：起手快了，但每发之间仍要等原来那么久的冷却，
        /// 对机关枪这种「冷却即射速」的武器来说，表现就是「速度没变」。
        /// 这里补上冷却速度，才是真正的攻速翻倍。
        /// </summary>
        private double OnGetSkillCooldownSpeed(Hook_OldSkill.orig_getCooldownSpeed orig, OldSkill self)
        {
            double cdSpeed = orig(self);
            if (IsSonicCrossbowSkill(self))
            {
                return cdSpeed * ATTACK_SPEED_MULT;
            }
            return cdSpeed;
        }

        /// <summary>判断某个技能是不是音波弩自己的技能（WeaponSkill.weapon is SonicCrossbow）。</summary>
        private static bool IsSonicCrossbowSkill(OldSkill self)
        {
            WeaponSkill ws = self as WeaponSkill;
            return ws != null && ws.weapon is SonicCrossbow;
        }

        // ==================================================================
        //  Hook 1：Entity.init —— 认出音波弹 + 登记 + 穿墙
        // ==================================================================

        private void OnEntityInit(Hook_Entity.orig_init orig, Entity self)
        {
            orig(self);

            // 必须是音波弹；其余实体（包括其它子弹）一律不碰
            SonicBolt bolt = self as SonicBolt;
            if (bolt == null || bolt.destroyed) return;

            try
            {
                _spawned++;
                GetTracked(bolt);

                // 需求 3：穿墙（Bullet.fixedUpdate 的墙体分支直接跳过）
                if (WALL_PIERCE) bolt.ignoreWalls = true;

                if (DEBUG_LOG && _spawned <= LOG_SAMPLES)
                {
                    Debug("[SonicCrossbowOverhaul] 音波弹 #" + _spawned + " 出生：dx="
                        + bolt.dx.ToString("0.###") + " dy=" + bolt.dy.ToString("0.###")
                        + " maxDist=" + bolt.maxDist.ToString("0.#") + " ignoreWalls=" + bolt.ignoreWalls);
                }
            }
            catch (Exception ex)
            {
                Fail("Entity.init 认出音波弹失败", ex);
            }
        }

        // ==================================================================
        //  Hook 1b：Entity.initGfx —— 随机颜色（必须早于 orig）
        // ==================================================================

        /// <summary>
        /// 给每一发音波弹随机一个颜色。
        ///
        /// ⚠ 必须在 **orig(self) 之前** 改 `color`：
        ///   Entity.init() 内部就会调用 initGfx()，而 SonicBolt.initGfx() 里
        ///       num = color; ... hSprite.color = ...   // 精灵染色
        ///       if (hasLight) createLight(color, ...)  // 光照颜色
        ///   都是**读** color 来算的。等 orig 跑完再改，精灵和光照已经染好了，改了也看不见。
        ///   所以放在 orig 前，让原版用我们随机出来的颜色去初始化图形。
        ///
        /// 另外 SonicBolt.doTail() 每次开尾迹时读的是当前 color，所以也会跟着变。
        /// </summary>
        private void OnEntityInitGfx(Hook_Entity.orig_initGfx orig, Entity self)
        {
            if (RANDOM_COLOR && self is SonicBolt bolt && !bolt.destroyed)
            {
                try
                {
                    bolt.color = NextRandomColor();
                    _recolored++;
                    if (DEBUG_LOG && _recolored <= LOG_SAMPLES)
                    {
                        Debug("[SonicCrossbowOverhaul] 音波弹 #" + _recolored
                            + " 随机颜色 = #" + bolt.color.ToString("X6"));
                    }
                }
                catch (Exception ex)
                {
                    Fail("随机颜色失败", ex);
                }
            }

            orig(self);
        }

        /// <summary>
        /// 生成一个随机 0xRRGGBB 颜色。
        ///   WILD_COLOR = true  → 三通道各 40~255，颜色跨度最大（默认）；
        ///   WILD_COLOR = false → 只随机色相、亮度固定在较亮的区间，观感更像能量弹。
        /// </summary>
        private static int NextRandomColor()
        {
            int r = _random.Next(40, 256);
            int g = _random.Next(40, 256);
            int b = _random.Next(40, 256);

            if (IsWildColor())
            {
                return (r << 16) | (g << 8) | b;
            }

            // 非 wild：只保留「最亮的一个通道满亮、其余压暗」，保证亮度稳定、色相随机
            if (r >= g && r >= b) return (255 << 16) | (b << 8) | (g >> 2);
            if (g >= r && g >= b) return (b << 16) | (255 << 8) | (r >> 2);
            return (g << 16) | (r << 8) | 255;
        }

        /// <summary>
        /// 用方法包一层，避免 WILD_COLOR 作为 const 参与编译期分支裁剪
        /// 而产生 CS0162「无法访问的代码」警告。
        /// </summary>
        private static bool IsWildColor()
        {
            return WILD_COLOR;
        }

        // ==================================================================
        //  Hook 2：Bullet.fixedUpdate —— 散射 + 射程 ×1.2（各只做一次）
        // ==================================================================

        private void OnBulletFixedUpdate(Hook_Bullet.orig_fixedUpdate orig, Bullet self)
        {
            SonicBolt bolt = self as SonicBolt;
            if (bolt != null && !bolt.destroyed)
            {
                int key = RuntimeHelpers.GetHashCode(bolt);
                Tracked t;
                if (_tracked.TryGetValue(key, out t))
                {
                    // ---- 需求 1：随机角度散射 ----
                    // 放在第一帧而不是 init：init 时 dx/dy 可能还没写完，而到这一帧
                    // 原版 shootFromWeapon 已经读过方向、位置也定好了，dx/dy 就是最终值。
                    if (!t.Spread)
                    {
                        t.Spread = true;
                        try { if (ApplySpread(bolt)) _spread++; }
                        catch (Exception ex) { Fail("散射失败", ex); }
                    }

                    // ---- 需求 2：射程 ×1.2 ----
                    // 原版在 init 之后才写 maxDist = props.range * 24（264px），
                    // 到这一帧它已是最终基线值，乘一次即精确 +20%（不会重复叠乘）。
                    if (!t.Range)
                    {
                        t.Range = true;
                        try
                        {
                            double baseDist = bolt.maxDist;
                            if (baseDist > 0.0)
                            {
                                bolt.maxDist = baseDist * RANGE_MULT;
                                _ranged++;
                                if (DEBUG_LOG && _ranged <= LOG_SAMPLES)
                                {
                                    Debug("[SonicCrossbowOverhaul] 射程 " + baseDist.ToString("0.#")
                                        + " → " + bolt.maxDist.ToString("0.#") + "px（×"
                                        + RANGE_MULT.ToString("0.##") + "）");
                                }
                            }
                        }
                        catch (Exception ex) { Fail("射程 ×1.2 失败", ex); }
                    }

                    // 两件事都做完了就删记录，表不会无限增长
                    if (t.Spread && t.Range) _tracked.Remove(key);
                }
            }

            orig(self);
        }

        // ==================================================================
        //  Hook 3（兜底）：SonicCrossbow.shoot —— 原版跑完后立刻改
        // ==================================================================

        private void OnShoot(Hook_SonicCrossbow.orig_shoot orig, SonicCrossbow self, ArrayObj bulletsOut)
        {
            orig(self, bulletsOut);

            if (bulletsOut == null || bulletsOut.length <= 0) return;

            try
            {
                for (int i = 0; i < bulletsOut.length; i++)
                {
                    SonicBolt bolt = bulletsOut.array[i] as SonicBolt;
                    if (bolt == null || bolt.destroyed) continue;

                    Tracked t = GetTracked(bolt);

                    if (!t.Spread && ApplySpread(bolt)) _spread++;
                    if (WALL_PIERCE) bolt.ignoreWalls = true;
                    if (!t.Range && bolt.maxDist > 0.0)
                    {
                        bolt.maxDist *= RANGE_MULT;
                        t.Range = true;
                        _ranged++;
                    }
                    t.Spread = true;

                    if (t.Spread && t.Range) _tracked.Remove(RuntimeHelpers.GetHashCode(bolt));
                }
            }
            catch (Exception ex)
            {
                Fail("SonicCrossbow.shoot 兜底改写失败", ex);
            }
        }

        // ==================================================================
        //  具体改写
        // ==================================================================

        /// <summary>
        /// 把速度矢量 (dx, dy) 绕原点随机旋转 [-SPREAD_MAX, +SPREAD_MAX] 弧度。
        /// 模长不变 → 速度不变，只改方向；这就是「随机角度散射」。
        /// 返回是否真的改了。
        /// </summary>
        private bool ApplySpread(Bullet bolt)
        {
            double dx = bolt.dx;
            double dy = bolt.dy;
            double speed = System.Math.Sqrt(dx * dx + dy * dy);
            if (!(speed > 0.0)) return false;

            double spread = (_random.NextDouble() * 2.0 - 1.0) * SPREAD_MAX;
            double cos = System.Math.Cos(spread);
            double sin = System.Math.Sin(spread);

            bolt.dx = dx * cos - dy * sin;
            bolt.dy = dx * sin + dy * cos;

            // 同步朝向（只影响 sprite 左右翻转，不影响轨迹），避免表现与飞行方向不一致
            try { bolt.updateDir(); } catch { }

            if (DEBUG_LOG && _spread < LOG_SAMPLES)
            {
                Debug("[SonicCrossbowOverhaul] 散射=" + spread.ToString("+0.000;-0.000")
                    + " rad（" + Deg(spread) + "°）");
            }
            return true;
        }

        private Tracked GetTracked(Bullet bolt)
        {
            int key = RuntimeHelpers.GetHashCode(bolt);
            Tracked t;
            if (!_tracked.TryGetValue(key, out t))
            {
                t = new Tracked();
                _tracked[key] = t;
            }
            return t;
        }

        // ==================================================================
        //  工具
        // ==================================================================

        /// <summary>弧度 → 角度字符串（1 rad = 180/π 度）。</summary>
        private static string Deg(double rad)
        {
            return (rad * 180.0 / System.Math.PI).ToString("0.0");
        }

        /// <summary>
        /// 带毫秒时间戳的日志（相邻重复消息去重）。
        ///
        /// 特意加毫秒：游戏默认日志只到秒，无法验证「射速」到底有没有变。
        /// 有了毫秒就能直接从日志里量出连射间隔：
        ///     搜索「音波弹 #」的时间戳，相邻两条之差 = 实际每发间隔。
        ///     期望值 ≈ DEFAULT_AUTO_FIRE_TICK / ATTACK_SPEED_MULT。
        /// </summary>
        private void Debug(string message)
        {
            if (message == _lastDebug) return;
            _lastDebug = message;
            Logger.Information("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message);
        }

        private void Fail(string what, Exception ex)
        {
            _failures++;
            if (_failures <= 5) Logger.Warning("[SonicCrossbowOverhaul] " + what + ": " + ex);
        }
    }
}
