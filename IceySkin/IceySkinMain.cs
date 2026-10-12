using System;
using System.Runtime.InteropServices;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using dc.en;
using dc.hl.types;
using dc.tool.atk;
using dc.hxd.fs;
using dc.libs.heaps.slib;
using dc.libs.heaps.slib._AnimManager;
using dc.tool;
using dc.tool.hero;
using dc.tool.mainSkills;
using dc.tool.mainSkills.blackboards;

namespace IceySkin;

/// <summary>
/// ICEY -> Dead Cells: ICEY as a new playable character skin.
///
/// This mod ships a self-contained hero atlas ("beheadedIcey") rendered offline
/// from ICEY's Spine 2.1.27 animations, plus two new data.cdb rows
/// (skin/Icey, item/Icey), so the character is selectable without overwriting
/// any vanilla asset.
///
/// Pipeline exercised here:
///   1. res.pak mounted by FsPak
///   2. new skin/item rows merged by CDBManager
///   3. hero.applySkin rebuilds the sprite + state machine from the new atlas
///   4. the hero atlas SpriteLib is re-created for the new model
///
/// F7 = diagnose + toggle ICEY. The skin is also applied automatically once,
/// shortly after the hero first exists, so the character is visible right away.
/// F8 / F9 = shrink / grow the character via dc.Entity.sprScaleX / sprScaleY
/// (the game's own sprite scale; it re-applies it every frame as
/// spr.scaleX = sprScaleX * dir, so turning keeps working).
///
/// v0.5.0 = ICEY 战斗层（全部跑在 IOnHeroUpdate 的自建状态机里，ModCore 没有
/// applyAttackResult / fixedUpdate 钩子）：
///   * J = 轻攻击 JJJJ（atkPunchA/B + comboKickA + atkPunchC），I = 重攻击，
///     不拿任何武器也能打出：伤害走
///     AttackUtils.Class.createFromHero.Invoke(hero, power, tier) + hit
///     （DashOverhaul / CocoonPassive 同一套）+ atk.addTag(7)。
///   * 使用 ICEY 时删除武器与技能：hero.weaponsManager.addWeaponLock("IceySkin")
///     + activeSkillsManager.addSkillLock("IceySkin")（原版 bankChallenge 房间同款，
///     顺带把 HUD 武器/技能栏淡出），并把 act1..5 的绑定全部解绑，
///     J/I/O 改成 mod 自己轮询 Win32 按键。
///   * O = ICEY 满级冲刺：八方向（方向键/WASD 采样）、无视墙体、全程无敌帧、
///     每帧直接写 hero.cx/xr/cy/yr；命中敌人 -> 冲刺冷却清零（以攻击换冷却），
///     冲刺起手 0.16s 内撞到人 = 一闪（追加伤害 + 回血，回血 3s CD）；
///     冲刺可以打断攻击后摇，冲刺中按 J/I = 冲刺斩。
///
/// Ribbon uses DC's own scarf physics, weapon + glow are baked into the atlas:
///   * ribbon - ICEY's "Idle/Tail" mesh is hidden at bake time
///     (icey_repack.HIDE_SLOT_SUBSTR = ("Tail",)) and replaced by 4 additive
///     `scarfGlow` layers in the skin row (cdb `scarfs` / `scarfBlendMode = 1`).
///     They hang off the head bone + attachOffY (dc.tool.ScarfSegment.cs:956-971
///     / 1122-1124) with a real physics swing. Whole ribbon length is roughly
///     count * maxLength (:454-478) - vanilla skins sit at 20..60 px; the first
///     attempt used 70..126 px and got stretched into one long straight beam.
///   * weapon - ICEY keeps its gun in a second Spine skin ("WeaponOn") whose
///     attachments the animations reference by name; spine21_render.load_rig
///     merges that skin into the default one so the gun shows up at all.
///   * glow   - the cyan parts pulse like a breathing lamp ("呼吸灯"); baked by
///     icey_repack.glow_pass() with k = MIN + (MAX-MIN) * (0.5+0.5 sin(2*pi*f*t)).
/// v0.6.11 (this round):
///   * swing FX now comes from ICEY's own art, baked into atlas/iceyFx.atlas by
///     tools/icey_fx_bake.py (DaoEffect crescents / qishen slash / dianguang
///     flash / Atk32xx thrust beam); fxWeapon stays only as the fallback.
///   * air-attack hover is ON by default and spans the whole air combo
///     including the 0.30s combo window; F10 still toggles it off.
///   * ground combos now root the hero (vanilla Hero.lockControlsS, same lock
///     bosses use) while swinging and during the combo window; dash cancels it.
/// </summary>
public class IceySkinMain : ModBase, IOnHeroUpdate, IOnAfterLoadingAssets
{
    public static IceySkinMain? inst { get; private set; }

    /// <summary>Skin id to apply (must match a row id in the cdb skin/item sheets).</summary>
    private const string SkinId = "Icey";

    /// <summary>Toggle key F7 (0x76). ModCore has no input API, so mods poll Win32 like the others do.</summary>
    private const int VkToggle = 0x76;

    /// <summary>
    /// F8 / F9 = 实时缩小 / 放大角色（每按一次 x1.02 / ÷1.02，范围 = 默认值 /2 .. 默认值 x2），
    /// 改完会在控制台打印当前值。调大小时注意：tracks（披风/武器挂点）是按
    /// DefaultHeroScale 烘焙的，改大了挂点会跟着偏，所以微调之后要告诉我，
    /// 我会用 `icey_repack.py --art-scale K` + 这个常量重新对齐。
    /// </summary>
    private const int VkScaleDown = 0x77;   // F8
    private const int VkScaleUp = 0x78;     // F9

    /// <summary>
    /// F10 = 切换「空中攻击悬停」。
    ///
    /// ICEY 原版空中攻击第 0 帧就有 AirPhysic(0.0) 事件
    /// （AirAtk1.json / AirAtk2.json / AirAtk6.json：gravityScale=0 + SetSpeed(zero)
    /// -> 角色整个出招期间悬停在空中，轻攻击完全静止、重击 gravityScale=0.01 极缓下漂）。
    /// 也就是说「在空中攻击会停滞、停止攻击才开始下落」本来就是 ICEY 的原版行为。
    ///
    /// DC 侧默认**开启悬停**（v0.6.11 起：整条空中连段都挂着，连段窗口结束才下落，
    /// 与 ICEY 原版一致）；按 F10 可关掉换成 DC 的自然下坠手感。
    /// 无论哪种模式，重力都会在连段结束/落地/冲刺时立刻还原，不会把角色永久挂在空中。
    /// </summary>
    private const int VkHoverToggle = 0x79;   // F10

    /// <summary>
    /// 角色缩放比例（就是 DC 自己的 sprite scale）。
    /// dc.Entity.postUpdate 每帧都会重写
    ///     spr.scaleX = sprScaleX * dir;  spr.scaleY = sprScaleY;
    /// （Entity.cs:13391-13399），我们只要给 entity 写 sprScaleX/sprScaleY 就够了，
    /// 精灵 scale()、翻转、方向都由游戏自己保持一致。
    ///
    /// 取 1/2 是因为图集按 ART_SCALE = 2 烘焙（icey_repack.py 文件头的 K），
    /// 而本机运行时的 pixelScale = 2（日志实测 render: pixelScale=2）。
    /// K = pixelScale 时屏幕像素 : 图集像素 = 1:1，最清晰；K 取大了会被
    /// 最近邻缩样丢像素（发糊）——上一版 K=4 就是这么糊掉的。
    /// Dead Cells 的像素倍率见 Main.onResize (dc/Main.cs:1952-1975)
    ///     pixelScale = max(1, floor(3 * min(winW/1920, winH/1080) * 4) / 4)
    /// 1280x720 -> 2、1920x1080 -> 3、2560x1600(全屏满分辨率) -> 4。
    /// 如果换了显示器/分辨率，就按新 pixelScale 改这个值并重跑
    /// `python tools/icey_repack.py --art-scale K`（图集尺寸 = K x 原始尺寸，
    /// tracks 同样补偿 T = R/2 + hero_scale*K*(track - V/2)，两边必须成对改）。
    /// </summary>
    private const double DefaultHeroScale = 1.0 / 2.0;

    // ------------------------------------------------------------------
    // ICEY 跳跃（需求：用 ICEY 的二段跳覆盖原版 DC 的跳跃，只用 ICEY 同款跳跃）
    //
    // 原版跳跃的三条腿（都在 cdb 的 hero/Beheaded 行里，运行时从
    // hero.mainSkillsManager 拿到 skillInfos 后直接改 props）：
    //   * JumpBeheaded      props.power = 0.36, count = 3, buff = 0.1
    //       normalJump(): hero.dy = -power，之后每帧若仍按住跳跃键且 count>0 就
    //       dy -= buff（JumpBeheaded.cs:35-260 / 436-470）——这就是 DC 的
    //       「按住跳更高」：轻点 0.36（≈1.0 格），按住最高 0.36+3*0.1 = 0.66（≈3.35 格）。
    //   * AirJumpBeheaded   props.power = 0.65，二段跳位置 dy = -power
    //       （AirJumpBeheaded.cs:31-90；额外还有 0.6 倍率，只对第 3 段以后的跳生效）。
    //   * 英雄重力 gravity = 0.065 * gravityFactor（_Hero.cs:230-237），
    //     顶点高度 = power^2 / (2*0.065) 格。
    //
    // ICEY 的跳是**固定高度**的一段 + 同高度的二段（二段跳 = 空中再来一次同样的跳），
    // 所以这里：
    //   * 一段跳：power = 0.66、count = 0 —— 去掉「按住更高」，轻点即到原版满蓄高度
    //     （0.66^2/0.13 = 3.35 格，与原版按住跳的最高点完全一致，关卡通过性不变）；
    //   * 二段跳：power = 0.66 —— 与一段跳等高（原版二段 0.65 本来就接近满蓄，
    //     改成同名值后两段完全对称）；
    //   * 空中跳时强制播放 ICEY 的**二段跳动画**（见 IceyAirJumpAnim*）。
    //
    // 只改这个英雄实例的 skillInfos（也是 cdb 行对象），F7 切回原版时还原。
    // ------------------------------------------------------------------
    private const double IceyJumpPower = 0.66;

    /// <summary>
    /// ICEY 二段跳动画的锁定时间。
    ///
    /// ★ 只用 0.18s：原版 gravity = 0.065/帧^2、power = 0.66 -> 上升到顶点只用
    ///   0.66/0.065 = 10.2 帧 ≈ 0.17s。以前锁 0.467s（= 整个 Jump2 源动画时长），
    ///   结果上升结束后人物还在空中翻跟头 —— 用户反馈的「空中多动症」就是它。
    /// </summary>
    private const double IceyAirJumpAnimTime = 0.18;

    /// <summary>空中跳动画的优先级：要盖过 jumpDown(11)/jumpUp(10)，但低于 rollEnd(15)/atkSpin(16)。</summary>
    private const int IceyAirJumpAnimPriority = 14;

    /// <summary>
    /// 播放 ICEY 二段跳用的组名。
    ///
    /// 这个名字**原版没有**：icey_repack.py 单独烘了一组 iceyAirJump =
    /// ICEY Jump2 的腾空段（不含起跳前的下蹲）+ ICEY 自己的空中跳特效环
    /// （NewPlayerAirJumpEffect/AirJump，压在脚下），所以二段跳一眼能认出来。
    /// 5 帧 x speed 1.0 = 0.167s ≈ 上升时间。
    /// </summary>
    private const string IceyAirJumpAnimGroup = "iceyAirJump";

    /// <summary>iceyAirJump 组的播放速度（5 帧 / 30fps = 0.167s）。</summary>
    private const double IceyAirJumpAnimSpeed = 1.0;

    // ==================================================================
    // ICEY 战斗：J=轻攻击 I=重击 O=八向冲刺（v0.6.0：伤害/范围/特效全部照搬 ICEY 原版）
    // ------------------------------------------------------------------
    // 数据来源（ICEY 安装目录解包的原始配置表）：
    //   * 连段链  extracted_icey/config/NewCombo.txt
    //       地面轻 J：Atk1 -> Atk2 -> Atk5 -> Atk15
    //       空中轻 J：AirAtk1 -> AirAtk2 -> AirAtk6
    //       地面重 I：AtkHv1 -> AtkHv2 -> AtkHv3（TripleAttack）
    //       空中重 I：AirAtkHv1 -> AirAtkHv2 -> AirAtkHv5
    //   * 数值    extracted_icey/config/AtkParameters.txt
    //       damagePercent / shakeClip（震屏帧）/ shakeOffset（震幅）/ frozenClip（定格帧）
    //   * 伤害换算（Assembly-CSharp PlayerDamageCalculate）：
    //       ICEY 最终伤害 = baseAtk(40) x damagePercent x 1.5^(atkLevel-1)
    //       DC 侧把 baseAtk x damagePercent 交给 AttackUtils.createFromHero，
    //       成长交给 DC 自己的 sourceTier（三维卷轴）缩放，等价于 ICEY 的 atkLevel。
    //   * 判定几何：在新图集上逐帧量「帧的右缘相对画布中心」（格，24px/格）：
    //       Reach = 该段最大伸距；HitAt = 动作摆出去的那一刻（首帧 >= 85% 最大伸距）。
    //   * 特效：ICEY 用 shakeClip/frozenClip 帧数表达 -> DC 侧换成
    //       viewport.shakeS(x,y,秒) 震屏 + Boot.slowMo(秒,倍率) 定格（重击第 2 段 15 帧）。
    //
    // 播放用 AnimManager.play()：push 一个非 state 动画（isStateAnim=false），
    // applyStateAnims 遇到它直接 return（AnimManager.cs:1514-1541），所以出招期间
    // 不会被 run/idle 状态动画抢掉，播完自动回到状态机。
    // 动画速度按「源动画时长」反算：DC 帧率 = 30fps x AnimInstance.speed，
    // 因此 speed = 帧数 / (30 x 时长)；新烘的 60fps 组恰好是 2.0。
    // ==================================================================

    /// <summary>武器/技能锁的名字（原版 addWeaponLock/addSkillLock 的字符串钥匙）。</summary>
    private const string CombatLockTag = "IceySkin";

    /// <summary>ICEY 基础攻击力（PlayerDamageCalculate 里的 baseAtk；本轮按用户要求大幅上调）。</summary>
    private const double IceyBaseAtk = 110.0;

    /// <summary>ICEY 伤害成长：英雄每 1 点属性（红/绿/紫）额外 +10% 伤害（对标原版卷轴）。</summary>
    private const double IceyStatDmgPerPoint = 0.10;
    /// <summary>属性成长倍率上限（防止极端属性把伤害拉爆）。</summary>
    private const double IceyStatDmgMax = 6.0;

    /// <summary>一段攻击的全部参数（一段 = 一个 ICEY 源动画）。</summary>
    private readonly struct AtkStep
    {
        public readonly string Anim;    // DC 图集组名
        public readonly string Src;     // ICEY 源动画名（= 数值表的键，日志用）
        public readonly double Time;    // 状态时长 = 源动画时长（秒）
        public readonly double HitAt;   // 判定时刻（秒）
        public readonly double Dmg;     // 伤害 = 40 x damagePercent
        public readonly double Reach;   // 判定前伸（格）
        public readonly double ShakeX;  // 震屏 x 强度（0..1，0 = 不震）
        public readonly double ShakeY;
        public readonly double ShakeD;  // 震屏时长（秒）
        public readonly double Freeze;  // 命中定格时长（秒，0 = 无）
        public readonly string Fx;      // 挥砍特效（Assets.fxWeapon 图集组名，每段一个）

        public AtkStep(string anim, string src, double time, double hitAt, double dmg,
                       double reach, double shakeX, double shakeY, double shakeD, double freeze,
                       string fx)
        {
            Anim = anim; Src = src; Time = time; HitAt = hitAt; Dmg = dmg;
            Reach = reach; ShakeX = shakeX; ShakeY = shakeY; ShakeD = shakeD; Freeze = freeze;
            Fx = fx;
        }
    }

    private const double LightHalfH = 1.90;   // 轻攻击判定上下半高（格；本轮 1.60 -> 1.90）
    private const double HeavyHalfH = 2.15;   // 重击判定上下半高（格；本轮 1.85 -> 2.15）
    private const double AtkRangeBoost = 1.55; // 打击距离放大（本轮 1.25 -> 1.55，用户要求加大范围）
    private const double ComboWindowS = 0.30; // 一段打完后再按同键续招的窗口（秒）
    private const double FreezeSlowSpeed = 0.15;  // 定格时的时间倍率（slowMo 的 spd）

    // ---- 挥砍特效（自建 iceyFx 图集 = ICEY 原版青色月牙刀光）----
    //   * 每一段攻击在「挥出去」的那一刻（= 判定时刻 HitAt）播一次，
    //     **与是否命中敌人无关**（用户要求：不是打击命中的特效，是挥砍出来的特效）。
    //   * 图集 atlas/iceyFx.atlas（tools/icey_fx_bake.py 用 ICEY 原始素材烘焙：
    //     DaoEffect 月牙 + qishen 大斩 + dianguang 闪光 + Atk32xx 突刺光束），2x 密度，
    //     组名逐个写在 AtkStep.Fx 上；图集/组缺失时自动回退原版 fxWeapon 流程。
    //   * 播法照抄 MobBuffCombo.PlayWeaponFx（手搓 HSprite + killAfterPlay）。
    private const string IceyFxAtlas = "atlas/iceyFx.atlas";
    private const double FxScaleIcey = 0.5;   // iceyFx 是 2x 密度 -> 0.5 缩放 = 与英雄同世界尺寸
    private const double FxSpeedIcey = 0.55;  // 4 帧 ≈ 0.12s 停留（ICEY 刀光一闪的节奏）
    private const double FxScale = 0.75;   // 回退 fxWeapon 时的相对缩放（英雄本体是 0.5）
    private const double FxSpeed = 0.8;    // 回退 fxWeapon 时的 AnimManager.genSpeed

    // ---- 地面轻攻击（J）：Atk1(0.8) -> Atk2(0.9) -> Atk5(1.0) -> Atk15(1.1) ----
    private static readonly AtkStep[] GroundLight =
    {
        new("atkPunchA",  "Atk1",  0.467, 0.13, IceyBaseAtk * 0.80, 1.77, 0.06, 0.06, 8.0 / 60.0, 0.0, "iceyFxSwing1"),
        new("atkPunchB",  "Atk2",  0.433, 0.07, IceyBaseAtk * 0.90, 2.23, 0.06, 0.06, 8.0 / 60.0, 0.0, "iceyFxSwing2"),
        new("atkPunchC",  "Atk5",  0.533, 0.10, IceyBaseAtk * 1.00, 3.96, 0.06, 0.06, 8.0 / 60.0, 0.0, "iceyFxSwing3"),
        new("comboKickC", "Atk15", 1.000, 0.10, IceyBaseAtk * 1.10, 1.54, 0.06, 0.06, 8.0 / 60.0, 0.0, "iceyFxFinisher"),
    };

    // ---- 空中轻攻击（J）：AirAtk1(0.8) -> AirAtk2(0.8) -> AirAtk6(0.9) ----
    private static readonly AtkStep[] AirLight =
    {
        new("iceyAirAtk1", "AirAtk1", 0.367, 0.10, IceyBaseAtk * 0.80, 3.31, 0.06, 0.06, 8.0 / 60.0, 0.0, "iceyFxSwing1"),
        new("iceyAirAtk2", "AirAtk2", 0.367, 0.09, IceyBaseAtk * 0.80, 3.15, 0.06, 0.06, 8.0 / 60.0, 0.0, "iceyFxSwing2"),
        new("iceyAirAtk6", "AirAtk6", 0.533, 0.27, IceyBaseAtk * 0.90, 1.48, 0.06, 0.06, 8.0 / 60.0, 0.0, "iceyFxFinisher"),
    };

    // ---- 地面重击（I）：AtkHv1(1.6) -> AtkHv2(1.8) -> AtkHv3(0.7) ----
    private static readonly AtkStep[] GroundHeavy =
    {
        new("iceyAtkHv1", "AtkHv1", 0.633, 0.25, IceyBaseAtk * 1.60, 3.92, 0.20, 0.20, 5.0 / 60.0, 0.0, "iceyFxHeavy1"),
        new("iceyAtkHv2", "AtkHv2", 1.000, 0.50, IceyBaseAtk * 1.80, 3.12, 0.80, 0.80, 20.0 / 60.0, 15.0 / 60.0, "iceyFxHeavy2"),
        new("iceyAtkHv3", "AtkHv3", 1.400, 0.58, IceyBaseAtk * 0.70, 2.67, 0.00, 0.00, 0.0, 0.0, "iceyFxHeavy3"),
    };

    // ---- 空中重击（I）：AirAtkHv1(1.3) -> AirAtkHv2(1.4) -> AirAtkHv5(0.5) ----
    private static readonly AtkStep[] AirHeavy =
    {
        new("iceyAirAtkHv1", "AirAtkHv1", 0.633, 0.25, IceyBaseAtk * 1.30, 3.38, 0.20, 0.20, 5.0 / 60.0, 0.0, "iceyFxHeavy1"),
        new("iceyAirAtkHv2", "AirAtkHv2", 1.000, 0.55, IceyBaseAtk * 1.40, 3.52, 0.60, 0.60, 16.0 / 60.0, 15.0 / 60.0, "iceyFxHeavy2"),
        new("iceyAirAtkHv5", "AirAtkHv5", 0.767, 0.51, IceyBaseAtk * 0.50, 1.73, 0.00, 0.00, 0.0, 0.0, "iceyFxThrust"),
    };

    /// <summary>空中判定：离地 &gt; 0.35 格（distToGroundCase 单位 = 格）或正在上升。</summary>
    private const double AirborneDist = 0.35;

    // ---- 八向冲刺（O）：距离翻倍、零伤害（纯机动 + 无敌帧）----
    // ★ hero.dx / hero.dy 的单位 = 「格 / 引擎物理步」：引擎 Level 的固定步进是 2 个 tmod
    //   （tmod = dt*60）-> 物理步 ≈ 30Hz，且与渲染帧率无关。绝不能乘渲染帧 dt（高刷下会慢成爬）。
    private const double DashTargetSpeed = 27.0;       // 冲刺目标速度（格/秒）：7.2 格 ≈ 0.27s
    private const double DashStepPerFrame = DashTargetSpeed / 30.0;  // 物理步≈30Hz -> 每次写 dx 的基准值
    private const double DashSpeedScaleMin = 0.30;     // 速度自校准系数下限
    private const double DashSpeedScaleMax = 3.00;     // 速度自校准系数上限
    private const double DashAdaptWindowS = 0.07;      // 自校准采样窗口（秒）
    private const double DashMaxDist = 7.2;           // 单次冲刺距离（格）≈ 173px（本轮 x2）
    private const double DashCdS = 0.12;              // 冲刺后的自然冷却（秒）——很短，冲刺基本连续可用
    private const double DashRechargePerHitS = 0.5;   // 每次攻击命中敌人回充的冷却（秒）（以攻击换冷却）
    private const double DashChainFrac = 0.25;        // 冲刺进行到 25% 后即可再按 O 续一段
    private const double DashCliffInset = 0.30;       // 地面冲刺：脚底最多推进到「最后一格地板」内 0.3 格处就收手
    private const double DashCliffStopEps = 0.02;     // 可推进距离 ≤0.02 格 = 已经贴边，直接收手
    private const int DashCliffScanMax = 12;          // 悬崖前探列数上限（掉帧一帧也不可能越界）
    private const int DashCrouchAffectId = 3;         // 原版翻滚小体型 affect（DodgeRoll.extendDodgeRoll 同款）
    private const double DashCrouchRefreshS = 0.25;   // 小体型 affect 每帧续上（爬行通道比一段冲刺长，过期就钻不过去）
    private const double DashHardCapS = 1.6;          // 冲刺硬上限（钻矮洞会被引擎限速拖慢，但绝不能无限冲）
    private const double DashEngineProbeS = 0.08;     // 起手这段时间内跨帧位移仍≈0 -> 判定引擎 dx 不生效
    private const double DashStillTimeMax = 0.22;     // 位移≈0 累计满这么久判定撞墙（时间制，与帧率无关）
    private const double DashLockS = DashMaxDist / DashTargetSpeed * 1.25 + 0.05;  // 冲刺期间的输入锁时长（略长于冲刺本身）
    private const int DashTrailColor = 0x99E5FF;      // ICEY 冲刺残影：淡蓝

    // ---- 一闪（冲刺无敌帧反击）：只回血、无伤害（用户要求删除冲刺伤害）----
    private const double FlashWindowS = 0.10;         // 一闪窗口（起手 0.1s 内穿过敌人）
    private const double FlashHealPct = 0.025;        // 回血 = 最大生命 2.5%
    private const double FlashHealMin = 10.0;
    private const double FlashHealMax = 40.0;
    private const double FlashHealCdS = 3.0;
    private const double DashHitPad = 0.25;           // 一闪路径判定宽容（格）

    // ---- 方向采样（八向冲刺：方向键 + WASD）----
    private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;
    private const int VkA = 0x41, VkW = 0x57, VkD = 0x44, VkS = 0x53;

    private bool _applied;
    private bool _keyWasDown;

    private double _heroScale = DefaultHeroScale;
    private bool _scaleDownWas;
    private bool _scaleUpWas;

    /// <summary>Auto-apply the skin once, ~0.5 s after the hero first exists.</summary>
    private int _autoFrames;
    private bool _autoDone;

    // ---- ICEY 跳跃 / 动画节奏 相关的运行时状态 ----
    private Hero? _boundHero;
    private HeroMainSkill? _jumpSkill;
    private HeroMainSkill? _airJumpSkill;
    private AirJump? _airJump;
    private JumpBlackBoard? _jumpBoard;
    private double? _savedJumpPower;
    private int? _savedJumpCount;
    private double? _savedAirJumpPower;
    private int _lastAirJumps;
    private double _airJumpAnimLeft;
    private bool _airJumpAnimRegistered;
    private bool _animSpeedWarned;
    private readonly HashSet<string> _speedLogged = new();

    // ---- ICEY 按键系统（只改内存里的 controller 绑定，不落盘）----
    private dc.tool.BindingProfiles? _keysProfiles;
    private readonly List<int> _keysSavedActs = new();
    private readonly List<int[]> _keysSavedSlots = new();
    private bool _keysApplied;

    // ---- ICEY 战斗运行时状态（全部由 OnHeroUpdate 驱动）----
    private bool _lightWas, _heavyWas, _dashWas;      // 上一帧按键状态（边沿检测）
    private bool _atkActive, _atkAir, _atkHitDone;
    private int _atkKind;            // 当前连段：1 = 轻攻击（J），2 = 重击（I）
    private int _atkQueuedKind;      // 预约的下一段（0 = 无）
    private int _atkStep;
    private double _atkTimeLeft, _atkHitLeft, _comboLeft;
    private bool _dashing;
    private Hero? _dashHero;
    private double _dashVx, _dashVy, _dashTraveled, _dashElapsed;
    private bool _dashHitEnemy, _flashDoneThisDash;
    private bool _dashQueued;     // 冲刺中按过 O 的输入缓冲（冲刺结束自动接下一段 = 连贯冲刺）
    private bool _dashBodyLogged; // 每段冲刺只打一次「小体型是否生效」诊断日志
    private double _dashGroundCy, _dashGroundYr;   // 最后一个「脚底有地板」的位置（越界回拉时用）
    private bool _dashHadTileGround;               // 起手时脚下有瓦片地板（实体平台/电梯上不做瓦片悬崖保护）
    private bool _dashCrawlLogged;                 // 矮洞爬行日志限频（每段一次）
    private readonly HashSet<int> _dashHitUids = new();   // 一次冲刺内每个敌人只结算一次（DashOverhaul 同款）
    private double _dashCdLeft, _flashHealCdLeft;
    private bool _invincible;
    private bool _savedGravity = true;
    private dc.CollisionMode? _savedCollision;
    private HeroWeaponsManager? _weaponLockTarget;
    private HeroActiveSkillsManager? _skillLockTarget;
    private bool _ctrlLockOwn;                 // 地面出招定身 lockControlsS 由本 mod 持有（才负责解锁）
    private bool _combatWarned;
    private bool _combatTickWarned;
    private long _inputBlockedLogAt;   // 「按键被输入门挡住」的限频上报

    // ---- 挥砍特效（fxWeapon）/ 空中攻击悬停 / 临时物理诊断 ----
    private int _fxLog;                      // 挥砍特效日志限频
    private bool _fxWarned;                  // 特效图集缺名/播放失败只提醒一次
    private SpriteLib? _iceyFxLib;           // atlas/iceyFx.atlas（ICEY 原版月牙刀光，懒加载）
    private bool _iceyFxWarned;              // iceyFx 图集加载失败只上报一次
    private bool _hoverAtkEnabled = true;    // F10：空中攻击滞空（默认开 = ICEY 原版；关 = DC 自然下坠）
    private bool _hoverKeyWas;               // F10 边沿检测
    private bool _hoverActive;               // 当前是否挂着悬停
    private bool _hoverSavedGravity = true;  // 悬停前的 hasGravity（还原用）
    private bool _dashGrounded;              // 本段冲刺是否地面起手（地面段 = 引擎物理 + 悬崖保护）
    private double _dashStillTime;           // 冲刺位移≈0 的累计时长（时间制撞墙判定）
    private double _dashLastX, _dashLastY;   // 上一帧末位置（位移必须「跨帧」统计，同帧比较恒为 0）
    private bool _dashEngineBroken;          // 引擎 dx/dy 位移不生效（每段重新探测；撞墙/爬行不误判）
    private double _dashWinTime, _dashWinDist;    // 速度自校准采样窗口
    private static double _dashSpeedScale = 1.0;  // 冲刺速度自校准系数（跨段保留，第一段内收敛）
    private int _airStallFrames;             // 空中攻击 dy≈0 的连续帧数（诊断）
    private int _airPhysLog;                 // 诊断日志限频

    public IceySkinMain(ModInfo info) : base(info)
    {
        inst = this;
        info.Version = "0.6.11";
    }

    public override void Initialize()
    {
        Logger.Information("[IceySkin] initialized (ICEY character skin)");
        Console.WriteLine("[IceySkin] loaded: F7 = diagnose + toggle ICEY skin");
    }

    private static dc.String Hx(string s) => new HashlinkString(s).AsHaxe<dc.String>();

    /// <summary>
    /// Console.WriteLine 不会进 coremod/logs（游戏是窗口程序），排查只能靠
    /// ModCore 的 Logger；两边都写一份，用户重开游戏后我能直接从日志里读状态。
    /// </summary>
    private static void Log(string msg)
    {
        Console.WriteLine("[IceySkin] " + msg);
        try { inst?.Logger.Information("[IceySkin] " + msg); } catch { }
    }

    // ------------------------------------------------------------------
    // Diagnose: query the game's RUNTIME cdb to see whether the new rows
    // actually got merged. This is the only reliable way to tell apart
    // "pak not mounted" / "cdb not merged" / "group wrong".
    // ------------------------------------------------------------------
    private void Diagnose()
    {
        Console.WriteLine("[IceySkin] --- CDB DIAGNOSE ---");
        try
        {
            dynamic item = dc.Data.Class.item;
            var refRow = item.byId.get(Hx("PrisonerDefault"));
            Console.WriteLine("[IceySkin] item.byId[PrisonerDefault] = "
                              + (refRow == null ? "NULL (unexpected!)" : "present"));

            var mine = item.byId.get(Hx(SkinId));
            Console.WriteLine("[IceySkin] item.byId[" + SkinId + "] = "
                              + (mine == null ? "NULL -> row NOT merged" : "present"));
            if (mine != null)
            {
                Console.WriteLine("[IceySkin]   group = " + mine.group);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IceySkin] item diagnose failed: " + ex.Message);
        }

        try
        {
            dynamic skin = dc.Data.Class.skin;
            // dc.cdb.Index has no byId - only IndexId does. User.getHeroSkinInfos()
            // (User.cs:91-114) scans skin.all for (group == 1 && item == heroSkin),
            // so we do exactly the same thing here.
            dynamic all = skin.all;
            int n = all.length;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                dynamic r = all[i];
                if ((string)r.item.ToString() == SkinId)
                {
                    Console.WriteLine("[IceySkin] skin.all[" + i + "] item=" + r.item
                                      + " model=" + r.model + " group=" + r.group);
                    found = true;
                }
            }
            Console.WriteLine("[IceySkin] skin.all scanned (" + n + " rows), found "
                              + SkinId + " = " + found);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IceySkin] skin diagnose failed: " + ex.Message);
        }
        Console.WriteLine("[IceySkin] --- END DIAGNOSE ---");
    }

    /// <summary>
    /// Print what the hero is ACTUALLY wearing right now. This is the decisive
    /// check: if model is still "beheaded" the skin row was not picked up, and
    /// the unchanged visuals are explained by the model rather than by a
    /// failed atlas load.
    /// </summary>
    private void ReportCurrentSkin(Hero hero)
    {
        try
        {
            var info = hero.getSkinInfo();
            if (info == null)
            {
                Console.WriteLine("[IceySkin] getSkinInfo() = NULL");
                return;
            }
            Console.WriteLine("[IceySkin] current skin: item=" + info.item
                              + " model=" + info.model
                              + " colorMap=" + info.colorMap);
            var lib = hero.spr?.lib;
            Console.WriteLine("[IceySkin] spr.lib = " + (lib == null ? "NULL" : "ok"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IceySkin] ReportCurrentSkin failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // 1) Mount res.pak - must happen before any atlas is loaded.
    // ------------------------------------------------------------------
    void IOnAfterLoadingAssets.OnAfterLoadingAssets()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(typeof(IceySkinMain).Assembly.Location) ?? "";
            string pakPath = System.IO.Path.Combine(dir, "res.pak");
            if (!System.IO.File.Exists(pakPath))
            {
                Console.WriteLine("[IceySkin] res.pak not found: " + pakPath);
                return;
            }
            FsPak.Instance.FileSystem.loadPak(Hx(pakPath));
            Console.WriteLine("[IceySkin] res.pak mounted: " + pakPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IceySkin] res.pak mount failed: " + ex);
        }
    }

    // ------------------------------------------------------------------
    // 2) Apply / revert the skin.
    // ------------------------------------------------------------------
    private void TryApply(Hero hero)
    {
        if (_applied) return;
        try
        {
            hero.applySkin(Hx(SkinId));
            _applied = true;
            Console.WriteLine("[IceySkin] applySkin(" + SkinId + ") returned");
            ApplyHeroScale(hero);
            ApplyIceyKeys();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IceySkin] applySkin threw: " + ex.Message);
        }
    }

    private void TryRevert(Hero hero)
    {
        if (!_applied) return;
        try
        {
            UnbindIceyMovement();
            RestoreIceyKeys();
            hero.applySkin(Hx("PrisonerDefault"));
            _applied = false;
            ResetHeroScale(hero);
            Console.WriteLine("[IceySkin] reverted to PrisonerDefault");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IceySkin] revert failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // 3) Per frame: F7 = diagnose then toggle.
    // ------------------------------------------------------------------
    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        Hero? hero = Game.Instance.HeroInstance;
        if (hero == null || hero._level == null) return;

        if (!_autoDone)
        {
            if (++_autoFrames == 30)
            {
                _autoDone = true;
                TryApply(hero);
                ReportCurrentSkin(hero);
            }
        }

        bool down;
        try { down = (GetAsyncKeyState(VkToggle) & 0x8000) != 0; }
        catch { return; }

        if (down && !_keyWasDown)
        {
            Diagnose();
            if (_applied) TryRevert(hero);
            else TryApply(hero);
            ReportCurrentSkin(hero);
        }
        _keyWasDown = down;

        // ---- 角色缩放：每帧重申（有些流程会重置），F8/F9 实时微调 ----
        if (_applied) ApplyHeroScale(hero);
        HandleScaleKeys(hero);

        // ---- ICEY 跳跃 + 动画节奏（只在 ICEY 生效时改，切回原版立刻还原）----
        if (_applied) SyncIceyMovement(hero, dt);
        else if (_boundHero != null) UnbindIceyMovement();

        // ---- ICEY 战斗：J/I 无武器攻击 + O 八向冲刺（含武器/技能锁）----
        if (_applied)
        {
            try { SyncIceyCombat(hero, dt); }
            catch (Exception ex)
            {
                if (!_combatTickWarned)
                {
                    _combatTickWarned = true;
                    Log("combat: 主循环异常（已捕获，不再重复上报） " + ex);
                }
            }
        }
    }

    // ------------------------------------------------------------------
    // 4) sprite scale：dc.Entity.sprScaleX / sprScaleY
    //    游戏每帧用它们重算 spr.scaleX = sprScaleX * dir / spr.scaleY = sprScaleY，
    //    因此这就是官方的「整体缩放 + 自动镜像」入口，不需要碰 spr 本身。
    // ------------------------------------------------------------------
    private void ApplyHeroScale(Hero hero)
    {
        try
        {
            if (hero.sprScaleX != _heroScale || hero.sprScaleY != _heroScale)
            {
                hero.sprScaleX = _heroScale;
                hero.sprScaleY = _heroScale;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IceySkin] ApplyHeroScale failed: " + ex.Message);
        }
    }

    private void ResetHeroScale(Hero hero)
    {
        _heroScale = DefaultHeroScale;
        try
        {
            hero.sprScaleX = 1.0;
            hero.sprScaleY = 1.0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[IceySkin] ResetHeroScale failed: " + ex.Message);
        }
    }

    private void HandleScaleKeys(Hero hero)
    {
        bool dn, up, hv;
        try
        {
            dn = (GetAsyncKeyState(VkScaleDown) & 0x8000) != 0;
            up = (GetAsyncKeyState(VkScaleUp) & 0x8000) != 0;
            hv = (GetAsyncKeyState(VkHoverToggle) & 0x8000) != 0;
        }
        catch { return; }

        if ((dn && !_scaleDownWas) || (up && !_scaleUpWas))
        {
            double lo = DefaultHeroScale * 0.5, hi = DefaultHeroScale * 2.0;
            _heroScale = Math.Round(Math.Clamp(_heroScale * (up ? 1.02 : 1.0 / 1.02), lo, hi), 5);
            Console.WriteLine("[IceySkin] hero scale = " + _heroScale.ToString("0.00000")
                              + "  (F8 缩小 / F9 放大；默认 " + DefaultHeroScale.ToString("0.00000") + ")");
            if (_applied) ApplyHeroScale(hero);
        }
        _scaleDownWas = dn;
        _scaleUpWas = up;

        // ---- F10：空中攻击悬停开关（默认开 = ICEY 原版滞空；关 = DC 自然下坠）----
        if (hv && !_hoverKeyWas)
        {
            _hoverAtkEnabled = !_hoverAtkEnabled;
            if (!_hoverAtkEnabled) EndAirHover(hero);
            Log("hover: 空中攻击悬停 " + (_hoverAtkEnabled
                ? "开（ICEY 原版：整条空中连段滞空，窗口结束才下落）"
                : "关（DC 手感：空中出招也自然下坠）") + "  [F10]");
        }
        _hoverKeyWas = hv;
    }

    // ==================================================================
    // ICEY 按键系统（把原版动作绑定清空，J/I/O 由 mod 自己轮询）
    //
    //   act 1  DODGE        翻滚     -> 解绑（O 由 mod 接管 = ICEY 八向冲刺）
    //   act 2  MAIN_WEAPON  主武器   -> 解绑（J 由 mod 接管 = ICEY 轻攻击）
    //   act 3  SIDE_WEAPON  副武器   -> 解绑（I 由 mod 接管 = ICEY 重攻击）
    //   act 4  RIGHT_SKILL  第二技能 -> 解绑（ICEY 没有第二技能）
    //   act 5  LEFT_SKILL   技能     -> 解绑（ICEY 没有技能）
    //   act 7  HEAL         回血     -> L（裁决回血的当前实现）
    //
    //   ★ 使用 ICEY 时「删除武器与技能的使用」是两层保险：
    //     ① 这里把 act1..5 的 primary/secondary/third 全部写成 -1（键盘/手柄都失效）；
    //     ② SyncIceyCombat 里再对 weaponsManager / activeSkillsManager 调用
    //        addWeaponLock/addSkillLock（原版 bankChallenge 房间同款），
    //        让 canUseWeapon / canUseActiveSkill 直接返回 false，HUD 武器栏淡出。
    //     这样即使玩家在选项菜单里重新绑定，武器/技能也打不出来。
    //
    //   act 表来自 dc.Options.setKeyMapping(Options.cs:2285-2327)，别用别的编号。
    //   只写 controller.get_bindings() 返回的那份（内存），不碰 dc_options.json，
    //   所以不会污染玩家的键位存档；F7 关闭 ICEY 时按修改前的原值逐个还原。
    // ==================================================================
    private const int ActDodge = 1;
    private const int ActMain = 2;
    private const int ActSide = 3;
    private const int ActRightSkill = 4;
    private const int ActLeftSkill = 5;
    private const int ActHeal = 7;

    private const int KeyJ = 74;   // VK_J 轻攻击
    private const int KeyI = 73;   // VK_I 重攻击
    private const int KeyO = 79;   // VK_O 冲刺/翻滚
    private const int KeyL = 76;   // VK_L 终结/回血（当前实现 = 原版回血）

    private static int ReadSlot(ArrayBytes_Int? arr, int idx)
    {
        try
        {
            if (arr == null || idx < 0 || idx >= arr.length) return -1;
            return arr.getDyn(idx);
        }
        catch { return -1; }
    }

    private static void WriteSlot(ArrayBytes_Int? arr, int idx, int val)
    {
        if (arr == null || idx < 0) return;
        if (idx >= arr.length) arr.__expand(idx);
        arr.setDyn(idx, val);
    }

    /// <summary>
    /// ICEY 键位表：primary 写 key（-1 = 整行解绑），secondary/third 一律解绑。
    /// J/I/O 不再占用任何原版动作 —— 它们由 SyncIceyCombat 直接轮询按键。
    /// </summary>
    private static readonly (int act, int key)[] IceyKeys =
    {
        (ActDodge, -1),
        (ActMain, -1),
        (ActSide, -1),
        (ActRightSkill, -1),
        (ActLeftSkill, -1),
        (ActHeal, KeyL),
    };

    /// <summary>把 IceyKeys 写进指定 profile（不动快照）。</summary>
    private static void WriteIceyKeys(BindingProfiles prof)
    {
        foreach ((int act, int key) in IceyKeys)
        {
            WriteSlot(prof.primary, act, key);
            WriteSlot(prof.secondary, act, -1);
            WriteSlot(prof.third, act, -1);
        }
    }

    /// <summary>检测 6 个动作是否仍是 ICEY 键位（选项菜单/自定义模式会把原值灌回来）。</summary>
    private static bool IceyKeysIntact(BindingProfiles prof)
    {
        foreach ((int act, int key) in IceyKeys)
        {
            if (ReadSlot(prof.primary, act) != key) return false;
            if (ReadSlot(prof.secondary, act) != -1) return false;
            if (ReadSlot(prof.third, act) != -1) return false;
        }
        return true;
    }

    /// <summary>记下 act 的原绑定（还原用），然后再写 ICEY 键位。</summary>
    private void SaveActBindings(BindingProfiles prof, int act)
    {
        _keysSavedActs.Add(act);
        _keysSavedSlots.Add(new[]
        {
            ReadSlot(prof.primary, act),
            ReadSlot(prof.secondary, act),
            ReadSlot(prof.third, act),
        });
    }

    private void ApplyIceyKeys()
    {
        try
        {
            dc.tool.Controller? ctl = dc.Boot.Class?.ME?.controller;
            if (ctl == null) { Log("keys: controller = NULL"); return; }
            BindingProfiles? prof = ctl.get_bindings();
            if (prof == null) { Log("keys: get_bindings() = NULL"); return; }
            if (_keysApplied && ReferenceEquals(prof, _keysProfiles))
            {
                // 已经被别处（选项菜单 / 自定义模式）重新灌过 -> 直接再压一遍，快照保持不动
                if (!IceyKeysIntact(prof))
                {
                    WriteIceyKeys(prof);
                    Log("keys: 绑定被外部改写，已重新应用 ICEY 键位");
                }
                return;
            }
            if (_keysApplied) RestoreIceyKeys();
            if (prof.primary == null || prof.secondary == null || prof.third == null)
            {
                Log("keys: 绑定数组为 NULL，跳过");
                return;
            }

            _keysProfiles = prof;
            foreach ((int act, int _) in IceyKeys) SaveActBindings(prof, act);
            WriteIceyKeys(prof);
            _keysApplied = true;
            Log("keys: ICEY 键位生效 -> J=轻攻击 I=重攻击 O=八向冲刺 L=回血(终结占位)；"
                + "武器/技能/翻滚动作已全部解绑（J/I/O 由 mod 轮询）");
        }
        catch (Exception ex)
        {
            Log("keys apply failed: " + ex);
        }
    }

    private void RestoreIceyKeys()
    {
        try
        {
            if (_keysApplied && _keysProfiles != null)
            {
                for (int i = 0; i < _keysSavedActs.Count; i++)
                {
                    int act = _keysSavedActs[i];
                    int[] v = _keysSavedSlots[i];
                    WriteSlot(_keysProfiles.primary, act, v[0]);
                    WriteSlot(_keysProfiles.secondary, act, v[1]);
                    WriteSlot(_keysProfiles.third, act, v[2]);
                }
                Log("keys: 已还原原版键位（" + _keysSavedActs.Count + " 个动作）");
            }
        }
        catch (Exception ex)
        {
            Log("keys restore failed: " + ex.Message);
        }
        _keysSavedActs.Clear();
        _keysSavedSlots.Clear();
        _keysProfiles = null;
        _keysApplied = false;
    }

    // ==================================================================
    // ICEY 跳跃 / 动画节奏
    // ==================================================================

    private void SyncIceyMovement(Hero hero, double dt)
    {
        BindHeroForMovement(hero);
        ApplyIceyKeys();
        ApplyAnimSpeeds(hero);
        TickAirJumpAnim(hero, dt);
    }

    /// <summary>
    /// 绑定新英雄：一次性改跳跃 props、注册「空中跳 = ICEY 二段跳动画」的状态动画。
    /// （换局/换英雄时自动重绑。）
    /// </summary>
    private void BindHeroForMovement(Hero hero)
    {
        if (ReferenceEquals(_boundHero, hero)) return;
        UnbindIceyMovement();
        _boundHero = hero;
        _lastAirJumps = 0;
        _airJumpAnimLeft = 0.0;

        try
        {
            ApplyIceyKeys();

            HeroMainSkillsManager msm = hero.mainSkillsManager;
            if (msm == null)
            {
                Log("jump: mainSkillsManager = NULL");
                return;
            }

            // ---- 1) 一段跳 / 二段跳 = ICEY 固定高度、等高 ----
            _jumpSkill = msm.getMainSkill(JumpBeheaded.Class);
            var jProps = _jumpSkill?.skillInfos?.props;
            if (jProps != null)
            {
                _savedJumpPower = jProps.power;
                _savedJumpCount = jProps.count;
                jProps.power = IceyJumpPower;
                jProps.count = 0;
                Log("jump: JumpBeheaded power " + _savedJumpPower + " -> " + IceyJumpPower
                    + ", count " + _savedJumpCount + " -> 0");
            }
            else
            {
                Log("jump: JumpBeheaded skill/props not found");
            }

            _airJumpSkill = msm.getMainSkill(AirJumpBeheaded.Class);
            var aProps = _airJumpSkill?.skillInfos?.props;
            if (aProps != null)
            {
                _savedAirJumpPower = aProps.power;
                aProps.power = IceyJumpPower;
                Log("jump: AirJumpBeheaded power " + _savedAirJumpPower + " -> " + IceyJumpPower);
            }
            else
            {
                Log("jump: AirJumpBeheaded skill/props not found");
            }

            // ---- 2) 空中跳的检测：JumpBlackBoard.airJumps 每次空中跳 +1 ----
            _jumpBoard = msm.getBlackBoard(JumpBlackBoard.Class) as JumpBlackBoard;
            _airJump = _airJumpSkill as AirJump;
            Log("jump: blackboard=" + (_jumpBoard != null) + " airJumpSkill=" + (_airJump != null));

            // ---- 渲染清晰度自检：pixelScale 与 1/K 的比值 = 1 时 1 图集像素 = 1 屏幕像素 ----
            // （K = DefaultHeroScale 的倒数；窗口分辨率一变这个值就变，日志里能一眼看出来）
            try
            {
                double ps = dc.Main.Class.ME.pixelScale;
                double ratio = ps * DefaultHeroScale;
                Log("render: pixelScale=" + ps.ToString("0.###")
                    + "  artScale=" + (1.0 / DefaultHeroScale).ToString("0.###")
                    + "  screenpx-per-arttexel=" + ratio.ToString("0.###")
                    + (Math.Abs(ratio - 1.0) < 0.02 ? "  (1:1 最佳)" : "  (非 1:1，需要按这个 pixelScale 重新烘焙)"));
            }
            catch (Exception ex)
            {
                Log("render probe failed: " + ex.Message);
            }

            // ---- 3) 空中跳期间显示 ICEY 的二段跳动画 ----
            AnimManager anim = hero.spr?.get_anim();
            if (anim != null)
            {
                bool loop = false;
                anim.registerStateAnim(Hx(IceyAirJumpAnimGroup), IceyAirJumpAnimPriority,
                                       (double?)IceyAirJumpAnimSpeed,
                                       (HlFunc<bool>)IceyAirJumpAnimActive,
                                       new Ref<bool>(ref loop), (HlAction)null);
                _airJumpAnimRegistered = true;
                Log("jump: registered anim group '" + IceyAirJumpAnimGroup
                    + "' at priority " + IceyAirJumpAnimPriority);
            }
        }
        catch (Exception ex)
        {
            Log("jump bind failed: " + ex);
        }
    }

    private bool IceyAirJumpAnimActive()
    {
        try { return _applied && _airJumpAnimLeft > 0.0; }
        catch { return false; }
    }

    private int ReadAirJumps()
    {
        if (_jumpBoard != null) return _jumpBoard.airJumps;
        if (_airJump != null) return _airJump.get_airJumps();
        return 0;
    }

    /// <summary>
    /// 检测「刚才发生了空中跳」：airJumps 计数增加 -> 播放 ICEY 的二段跳动画。
    /// 计数着地时会被 AirJump.passivePreUpdate 清 0（AirJump.cs:244），所以
    /// 只需要比较相邻两帧的计数。
    /// </summary>
    private void TickAirJumpAnim(Hero hero, double dt)
    {
        try
        {
            int aj = ReadAirJumps();
            if (aj > _lastAirJumps)
            {
                _airJumpAnimLeft = IceyAirJumpAnimTime;
                Log("jump: air-jump #" + aj + " -> ICEY Jump2 anim for "
                    + IceyAirJumpAnimTime + "s");
            }
            _lastAirJumps = aj;
        }
        catch (Exception ex)
        {
            if (!_animSpeedWarned)
            {
                _animSpeedWarned = true;
                Log("air-jump probe failed: " + ex.Message);
            }
        }

        if (_airJumpAnimLeft > 0.0)
        {
            _airJumpAnimLeft -= dt;
            if (_airJumpAnimLeft < 0.0) _airJumpAnimLeft = 0.0;
        }
    }

    /// <summary>
    /// 动画节奏：把当前状态动画的播放速度设成 IceyAnimSpeeds.Table 里的值，
    /// 让每组的真实时长等于 ICEY 源动画的原生时长（详见 IceyAnimSpeeds.cs 头注释）。
    ///
    /// DC 的 AnimManager 只在**切换组**时写 animInstance.speed
    /// （applyStateAnims：group 相同就直接 return，AnimManager.cs:1514-1600），
    /// 所以这里每帧重申一次就足够，切换后 1 帧内生效。
    /// 只处理状态动画（isStateAnim），不碰武器攻击组（表里已排除 atk*）。
    /// </summary>
    private void ApplyAnimSpeeds(Hero hero)
    {
        try
        {
            AnimManager anim = hero.spr?.get_anim();
            if (anim == null || anim.destroyed) return;
            var stack = anim.stack;
            if (stack == null || stack.length == 0) return;
            if (stack.getDyn(0) is not AnimInstance inst) return;
            if (!inst.isStateAnim) return;

            string g = inst.group?.ToString() ?? "";
            if (g.Length == 0) return;
            if (!IceyAnimSpeeds.Table.TryGetValue(g, out double spd)) return;
            if (spd <= 0.0 || spd >= 1.0) return;
            if (Math.Abs(inst.speed - spd) > 0.0005)
            {
                inst.speed = spd;
                if (_speedLogged.Add(g)) Log("anim '" + g + "' speed -> " + spd.ToString("0.###"));
            }
        }
        catch (Exception ex)
        {
            if (!_animSpeedWarned)
            {
                _animSpeedWarned = true;
                Log("anim speed failed: " + ex.Message);
            }
        }
    }

    /// <summary>切回原版：还原跳跃 props、注销空中跳动画、解除武器/技能锁、收掉冲刺。</summary>
    private void UnbindIceyMovement()
    {
        Hero? hero = _boundHero;
        try
        {
            ReleaseCombatLocks(hero);
            var jProps = _jumpSkill?.skillInfos?.props;
            if (jProps != null)
            {
                if (_savedJumpPower != null) jProps.power = _savedJumpPower;
                if (_savedJumpCount != null) jProps.count = _savedJumpCount;
            }
            var aProps = _airJumpSkill?.skillInfos?.props;
            if (aProps != null && _savedAirJumpPower != null)
            {
                aProps.power = _savedAirJumpPower;
            }
            if (_airJumpAnimRegistered && hero != null)
            {
                hero.spr?.get_anim()?.removeStateAnim(Hx(IceyAirJumpAnimGroup), IceyAirJumpAnimPriority);
            }
        }
        catch (Exception ex)
        {
            Log("jump unbind failed: " + ex.Message);
        }

        _jumpSkill = null;
        _airJumpSkill = null;
        _airJump = null;
        _jumpBoard = null;
        _airJumpAnimRegistered = false;
        _airJumpAnimLeft = 0.0;
        _lastAirJumps = 0;
        _boundHero = null;

        ResetCombatState(hero);
    }


    // ==================================================================
    // ICEY 战斗主循环（只在 _applied 时由 OnHeroUpdate 调用）
    //
    // 一帧的顺序：锁武器/技能 -> 收键（J/I/O 边沿）-> 攻击/冲刺状态机。
    // 冲刺中按 J/I 会先把冲刺收掉（= ICEY 的「冲刺斩」）。
    // ==================================================================
    private void SyncIceyCombat(Hero hero, double dt)
    {
        if (_dashing && !ReferenceEquals(_dashHero, hero))
        {
            EndDash(_dashHero ?? hero, "换英雄");
        }

        ApplyCombatLocks(hero);

        if (_dashCdLeft > 0.0) _dashCdLeft = Math.Max(0.0, _dashCdLeft - dt);
        if (_flashHealCdLeft > 0.0) _flashHealCdLeft = Math.Max(0.0, _flashHealCdLeft - dt);
        if (_comboLeft > 0.0) _comboLeft = Math.Max(0.0, _comboLeft - dt);

        bool inputOk = IceyInputAllowed(hero);
        bool lightDown = false, heavyDown = false, dashDown = false;
        try
        {
            lightDown = KeyDown(KeyJ);
            heavyDown = KeyDown(KeyI);
            dashDown = KeyDown(KeyO);
        }
        catch { }

        bool lightPress = inputOk && lightDown && !_lightWas;
        bool heavyPress = inputOk && heavyDown && !_heavyWas;
        bool dashPress = inputOk && dashDown && !_dashWas;
        _lightWas = lightDown; _heavyWas = heavyDown; _dashWas = dashDown;

        if (!inputOk && (lightDown || heavyDown || dashDown)
            && Environment.TickCount64 - _inputBlockedLogAt > 2000)
        {
            _inputBlockedLogAt = Environment.TickCount64;
            Log("input: J/I/O 已按下但被输入门挡住（昏迷/过场/控制器锁），本帧忽略");
        }

        if (lightPress || heavyPress)
        {
            if (_dashing) { _dashQueued = false; EndDash(hero, "冲刺斩"); }   // 冲刺斩：放弃续冲缓冲
            if (lightPress) StartAttack(hero, 1);
            else StartAttack(hero, 2);
        }
        if (dashPress)
        {
            if (_dashing)
            {
                // 冲刺中再按 O：满足条件立刻续冲（八向连冲）；否则缓冲，
                // 冲刺结束时若已被「攻击命中」回充满 -> 自动接上下一段。
                if (!TryStartDash(hero)) _dashQueued = true;
            }
            else TryStartDash(hero);
        }

        TickAttack(hero, dt);
        TickDash(hero, dt);
    }

    /// <summary>原版输入门：昏迷/手动锁/控制器锁/过场时不接受 ICEY 按键。</summary>
    private static bool IceyInputAllowed(Hero hero)
    {
        try
        {
            if (hero.isUnconscious() || hero.life <= 0) return false;
            dc.tool.ControllerAccess? ctl = hero.controller;
            if (ctl == null) return true;
            if (ctl.manualLock) return false;
            dc.tool.Controller? parent = ctl.parent;
            if (parent != null)
            {
                if (parent.isLocked) return false;
                if (parent.exclusiveId != null
                    && !string.Equals(parent.exclusiveId.ToString(), ctl.id?.ToString()))
                {
                    return false;
                }
            }
            dc.pr.Level? level = hero._level;
            if (level != null && level.game != null && level.game.hasCinematic()) return false;
        }
        catch { }
        return true;
    }

    // ------------------------------------------------------------------
    // 武器 / 技能锁：使用 ICEY 时彻底禁用（原版 bankChallenge 房间同款）
    //   canUseWeapon   -> weaponControlLocks[slot].length != 0 -> false
    //   canUseActiveSkill -> skillControlLocks[id].length != 0 -> false
    //   顺带把 HUD 的武器/技能栏淡出（applyHudLockFade 每帧维护）
    // ------------------------------------------------------------------
    private void ApplyCombatLocks(Hero hero)
    {
        // addWeaponLock/addSkillLock 内部会写 HUD（setWeaponFade/setPowerFade），
        // HUD 还没建好时调用会踩空指针 —— 等它出现再锁。
        try { if (dc.ui.HUD.Class.ME == null) return; } catch { return; }

        try
        {
            HeroWeaponsManager? ln = hero.weaponsManager;
            if (ln != null && !ReferenceEquals(ln, _weaponLockTarget))
            {
                int all = -1;
                ln.addWeaponLock(Hx(CombatLockTag), ref all);
                _weaponLockTarget = ln;
                Log("lock: 武器已锁定（canUseWeapon=false，HUD 武器栏淡出）");
            }
        }
        catch (Exception ex)
        {
            if (!_combatWarned) { _combatWarned = true; Log("lock: 武器锁定失败 " + ex.Message); }
        }

        try
        {
            HeroActiveSkillsManager? sk = hero.activeSkillsManager;
            if (sk != null && !ReferenceEquals(sk, _skillLockTarget))
            {
                int all = -1;
                sk.addSkillLock(Hx(CombatLockTag), ref all);
                _skillLockTarget = sk;
                Log("lock: 技能已锁定（canUseActiveSkill=false，HUD 技能栏淡出）");
            }
        }
        catch (Exception ex)
        {
            if (!_combatWarned) { _combatWarned = true; Log("lock: 技能锁定失败 " + ex.Message); }
        }
    }

    private void ReleaseCombatLocks(Hero? hero)
    {
        try
        {
            if (hero != null && !hero.destroyed && hero.weaponsManager != null)
            {
                int all = -1;
                hero.weaponsManager.removeWeaponLock(Hx(CombatLockTag), ref all);
            }
        }
        catch (Exception ex) { Log("lock: 解除武器锁失败 " + ex.Message); }
        try
        {
            if (hero != null && !hero.destroyed && hero.activeSkillsManager != null)
            {
                int all = -1;
                hero.activeSkillsManager.removeSkillLock(Hx(CombatLockTag), ref all);
            }
        }
        catch (Exception ex) { Log("lock: 解除技能锁失败 " + ex.Message); }

        _weaponLockTarget = null;
        _skillLockTarget = null;
    }

    /// <summary>切回原版 / 换英雄时把战斗状态清干净（冲刺还原、无敌帧关闭、连招清零）。</summary>
    private void ResetCombatState(Hero? hero)
    {
        EndAirHover(hero);
        ReleaseAttackRoot(hero);
        if (_dashing)
        {
            EndDash(_dashHero ?? hero, "重置");
        }
        if (_invincible && hero != null)
        {
            try { hero.toggleFullInvincibility(false); } catch { }
            _invincible = false;
        }
        _atkActive = false; _atkQueuedKind = 0; _atkHitDone = true;
        _atkAir = false;
        _atkStep = 0; _atkTimeLeft = 0.0; _atkHitLeft = 0.0;
        _comboLeft = 0.0;
        _dashCdLeft = 0.0;
        _flashHealCdLeft = 0.0;
        _dashHitEnemy = false; _flashDoneThisDash = false; _dashQueued = false;
        _dashHitUids.Clear();
        _lightWas = false; _heavyWas = false; _dashWas = false;
    }

    // ------------------------------------------------------------------
    // 攻击（J = 轻攻击 / I = 重击）：地上 / 空中各一条连段链，全部照 ICEY 原版
    //   正在出招时：判定帧前按 = 预约下一段；判定帧后按 = 立刻取消后摇接下一段；
    //   换键（J <-> I）在后摇里可以直接切换。
    //   一条链打完后 ComboWindowS 内再按同键 = 从头再来一条链。
    // ------------------------------------------------------------------
    private static AtkStep[] ChainOf(int kind, bool air)
        => kind == 1 ? (air ? AirLight : GroundLight) : (air ? AirHeavy : GroundHeavy);

    /// <summary>空中判定：离地 &gt; 0.35 格或正在上升（distToGroundCase 单位 = 格）。</summary>
    private static bool IsAirborne(Hero hero)
    {
        try
        {
            if (hero.distToGroundCase() > AirborneDist) return true;
            try { if (hero.dy < -0.02) return true; } catch { }
        }
        catch { }
        return false;
    }

    private void StartAttack(Hero hero, int kind)
    {
        bool air = IsAirborne(hero);
        if (_atkActive)
        {
            if (!_atkHitDone) { _atkQueuedKind = kind; return; }   // 判定帧前 = 预约
            if (kind == _atkKind) AdvanceAttack(hero, kind, air);  // 后摇里 = 立刻接下一段
            else BeginAttack(hero, kind, air, 0);                  // 换键 = 直接起另一条链
            return;
        }
        if (_comboLeft > 0.0 && _atkKind == kind) AdvanceAttack(hero, kind, air);
        else BeginAttack(hero, kind, air, 0);
    }

    private void AdvanceAttack(Hero hero, int kind, bool air)
    {
        AtkStep[] chain = ChainOf(kind, air);
        int next = _atkStep + 1;
        if (next >= chain.Length) next = 0;      // 链尾 -> 重新起手
        BeginAttack(hero, kind, air, next);
    }

    private void BeginAttack(Hero hero, int kind, bool air, int step)
    {
        AtkStep[] chain = ChainOf(kind, air);
        AtkStep st = chain[step];
        _atkActive = true; _atkHitDone = false;
        _atkKind = kind; _atkAir = air; _atkStep = step; _atkQueuedKind = 0;
        _comboLeft = 0.0;
        _atkTimeLeft = st.Time;
        _atkHitLeft = st.HitAt;
        if (air && _hoverAtkEnabled) BeginAirHover(hero);
        else if (!air) RootForAttack(hero);   // 地面出招定身（ICEY 地面连招锁移动）
        PlayAnim(hero, st.Anim, st.Time);
        Log("atk: " + (kind == 1 ? "轻攻击" : "重击") + (air ? "(空)" : "(地)")
            + " " + (step + 1) + "/" + chain.Length + " -> " + st.Anim
            + " (" + st.Src + ", " + st.Dmg.ToString("0") + " x" + DmgScale(hero).ToString("0.##")
            + " = " + (st.Dmg * DmgScale(hero)).ToString("0") + " 伤, 伸距 "
            + (st.Reach * AtkRangeBoost).ToString("0.##") + " 格)");
    }

    /// <summary>
    /// 地面出招定身：用原版 Hero.lockControlsS（Boss 抓取同款）锁掉移动/跳跃输入 ——
    /// ICEY 的地面连招本来就是定身的。只锁地面段（空中段保持可操控）。
    /// 每帧续 0.12s；连段窗口结束 / 冲刺 / 切皮肤时 unlockControls() 释放
    /// （_ctrlLockOwn 保证只解自己的锁，不动别人的）。
    /// </summary>
    private void RootForAttack(Hero hero)
    {
        try
        {
            hero.lockControlsS(0.12);
            _ctrlLockOwn = true;
        }
        catch { }
    }

    private void ReleaseAttackRoot(Hero? hero)
    {
        if (!_ctrlLockOwn) return;
        _ctrlLockOwn = false;
        if (hero == null || hero.destroyed) return;
        try { hero.unlockControls(); } catch { }
    }

    private void TickAttack(Hero hero, double dt)
    {
        // 出招/定身中被昏迷或击杀打断 -> 立刻收招并释放定身（否则 root 会续到醒）
        bool stunned = false;
        try { stunned = hero.isUnconscious() || hero.life <= 0; } catch { }
        if (stunned && (_atkActive || _hoverActive || _ctrlLockOwn))
        {
            _atkActive = false; _atkQueuedKind = 0; _comboLeft = 0.0;
            EndAirHover(hero);
        }

        if (!_atkActive)
        {
            // 连段窗口期：地面继续定身 / 空中继续保持滞空（ICEY 原版整条连段挂着）
            if (_comboLeft <= 0.0)
            {
                ReleaseAttackRoot(hero);
                if (_hoverActive) EndAirHover(hero);
            }
            else
            {
                if (!_atkAir) RootForAttack(hero);
                else if (_hoverActive) TickAirHover(hero);
            }
            return;
        }

        if (!_atkAir) RootForAttack(hero);   // 地面出招：全程定身（每帧续 0.12s）
        TickAirHover(hero);
        ProbeAirStall(hero);

        _atkTimeLeft -= dt;
        if (!_atkHitDone)
        {
            _atkHitLeft -= dt;
            if (_atkHitLeft <= 0.0)
            {
                _atkHitDone = true;
                AtkStep st = ChainOf(_atkKind, _atkAir)[_atkStep];
                SwingFx(hero, st);   // 挥砍特效：命中/打空都播（这就是「挥出去」那一刻）
                double halfH = _atkKind == 1 ? LightHalfH : HeavyHalfH;
                int hits = HitArc(hero, st.Reach, halfH, st.Dmg);
                if (hits > 0) ApplyHitFeedback(hero, st);
            }
        }

        if (_atkTimeLeft > 0.0) return;
        _atkActive = false;
        if (_atkQueuedKind != 0)
        {
            int qk = _atkQueuedKind;
            _atkQueuedKind = 0;
            bool air = IsAirborne(hero);
            if (qk == _atkKind) AdvanceAttack(hero, qk, air);
            else BeginAttack(hero, qk, air, 0);
        }
        else
        {
            _comboLeft = ComboWindowS;
            // 空中：先保持滞空 —— 连段窗口内再按 J 还能接下一刀；窗口结束由
            // TickAttack 开头统一收掉（不会挂在天上）。
            if (!(_atkAir && _hoverActive)) EndAirHover(hero);
        }
    }

    /// <summary>命中反馈：ICEY 的 shakeClip（震屏）/ frozenClip（定格）换算到 DC。</summary>
    private void ApplyHitFeedback(Hero hero, AtkStep st)
    {
        if (st.ShakeD > 0.0 && (st.ShakeX > 0.0 || st.ShakeY > 0.0))
        {
            try
            {
                dc.Viewport? vp = hero._level?.viewport;
                if (vp != null) vp.shakeS(st.ShakeX, st.ShakeY, st.ShakeD);
            }
            catch { }
        }
        if (st.Freeze > 0.0)
        {
            try { dc.Boot.Class?.ME?.slowMo(st.Freeze, FreezeSlowSpeed, (bool?)null, (bool?)null); } catch { }
        }
    }

    /// <summary>
    /// AttackUtils 静态委托暂时读不到（HL 类惰性初始化等）——限频上报但绝不
    /// 永久放弃，因为下一帧类一初始化好，攻击伤害就该恢复。
    /// </summary>
    private void ReportAttackUtilsMissing(bool createOk, bool hitOk)
    {
        if (_combatWarned && _combatLastWarnAt > 0.0
            && Environment.TickCount64 - _combatLastWarnAt < 5000) return;
        _combatWarned = true;
        _combatLastWarnAt = Environment.TickCount64;
        if (createOk && hitOk) return;
        Log("atk: AttackUtils.Class.createFromHero/hit 暂不可用（create=" + createOk
            + " hit=" + hitOk + "），本帧攻击不结算伤害（会自动重试）");
    }

    private long _combatLastWarnAt;

    /// <summary>
    /// ICEY 伤害成长倍率：1 + 0.10 x 属性总和（红/绿/紫），上限 IceyStatDmgMax。
    /// 对标原版武器随卷轴/装备等级变强 —— 否则后期敌人几千血时 ICEY 伤害会显得「特别低」。
    /// </summary>
    private static double DmgScale(Hero hero)
    {
        try
        {
            double stats = hero.brutalityTier + hero.survivalTier + hero.tacticTier;
            double mul = 1.0 + IceyStatDmgPerPoint * stats;
            if (mul < 1.0) mul = 1.0;
            if (mul > IceyStatDmgMax) mul = IceyStatDmgMax;
            return mul;
        }
        catch { return 1.0; }
    }

    /// <summary>
    /// 朝向矩形判定 + 原版伤害结算（返回命中数）。
    /// 前伸 fwd = (敌 - 我)·dir ∈ [-0.35, reach]、上下 |dy| &lt;= halfH（格）。
    /// 数值走 AttackUtils.Class.createFromHero（DashOverhaul 同款空保护），
    /// tier 传 null -> 用英雄自己的卷轴档（ICEY atkLevel 的对应物）。
    /// </summary>
    private int HitArc(Hero hero, double reach, double halfH, double dmg)
    {
        HlFunc<AttackData, dc.Entity, dynamic, int?>? createFn = AttackUtils.Class.createFromHero;
        HlAction<AttackData, dc.Entity>? hitFn = AttackUtils.Class.hit;
        if (createFn == null || hitFn == null)
        {
            ReportAttackUtilsMissing(createFn != null, hitFn != null);
            return 0;
        }

        dc.tool.Team? team = hero._team;
        if (team == null) return 0;

        double hx = hero.cx + hero.xr;
        double hy = hero.cy + hero.yr;
        int dir = hero.dir >= 0 ? 1 : -1;
        int hits = 0;
        try
        {
            var iter = team.opponentsIterator.reset(team);
            if (iter == null) return 0;
            while (iter.hasNext())
            {
                dc.Entity e = iter.next();
                if (e == null || e.destroyed || e.life <= 0) continue;

                double ex = e.cx + e.xr;
                double ey = e.cy + e.yr;
                double fwd = (ex - hx) * dir;
                double eR = 0.6;
                try { eR = e.radius / 24.0; } catch { eR = 0.6; }
                // 打的是敌人的「身体」不是中心点 -> 把手感距离 + 敌人半径算进来
                double reachEff = reach * AtkRangeBoost + eR;
                if (fwd < -0.8 || fwd > reachEff) continue;
                if (Math.Abs(ey - hy) > halfH) continue;
                try { if (!e.canBeHit()) continue; } catch { continue; }

                double dmgEff = dmg * DmgScale(hero);   // 随英雄属性成长（用户要求提高伤害）
                AttackData? atk = createFn.Invoke(hero, (object)dmgEff, (int?)null);
                if (atk == null) continue;
                try { atk.addTag(7); } catch { }
                hitFn.Invoke(atk, e);
                hits++;
                HitSpark(hero, e, dir, dmgEff >= IceyBaseAtk * 1.2);
            }
        }
        catch (Exception ex) { Log("atk: 命中结算异常 " + ex.Message); }

        if (hits > 0) RechargeDash(hits);
        return hits;
    }

    /// <summary>命中火花（原版 fx.hitLines + impact），补 ICEY 的打击特效。</summary>
    private void HitSpark(Hero hero, dc.Entity e, int dir, bool heavy)
    {
        try
        {
            dc.Fx? fx = hero._level?.fx;
            if (fx == null) return;
            double px = (e.cx + e.xr) * 24.0;
            double hei = 24.0;
            try { hei = e.hei; } catch { hei = 24.0; }
            double py = (e.cy + e.yr) * 24.0 - hei * 0.5;
            int s1 = 0, s2 = 0;
            fx.hitLines(px, py, dir, heavy ? 0xA8E8FF : 0xFFFFFF, (bool?)null, ref s1, ref s2);
            if (heavy) fx.impact(px, py, 28, 0xA8E8FF, 0.9);
        }
        catch { }
    }

    // ------------------------------------------------------------------
    // 挥砍特效（用户需求：每次挥出去都要有，和是否命中无关 —— 类似 DC 的 fxId）
    //   * 主路径 TrySwingFxIcey：atlas/iceyFx.atlas（ICEY 原版月牙刀光，见 icey_fx_bake.py）
    //   * 兜底：Assets.fxWeapon（原版武器特效图集），图集/组缺失时自动回退
    //   * 播法照抄原版 Fx.playWeaponAnimFromObject / MobBuffCombo.PlayWeaponFx：
    //     手搓 HSprite 挂到 level.scroller(DP_ROOM_MAIN)，killAfterPlay 自动回收。
    //   * 时机：每段的判定时刻 HitAt = 动作「挥出去」的那一刻（命中/打空都播）。
    // ------------------------------------------------------------------
    private void SwingFx(Hero hero, AtkStep st)
    {
        if (string.IsNullOrEmpty(st.Fx)) return;
        if (TrySwingFxIcey(hero, st)) return;   // 首选：ICEY 原版月牙刀光（iceyFx 图集）
        try
        {
            SpriteLib? lib = null;
            try { lib = dc.Assets.Class?.fxWeapon; } catch { }
            if (lib == null || lib.groups == null)
            {
                if (!_fxWarned)
                {
                    _fxWarned = true;
                    Log("fx: Assets.fxWeapon 拿不到（图集未加载？），本次不播挥砍特效");
                }
                return;
            }

            string id = st.Fx;
            if (!lib.groups.exists(Hx(id)))
            {
                if (!_fxWarned)
                {
                    _fxWarned = true;
                    Log("fx: fxWeapon 图集里没有 '" + id + "'（检查 res.pak 的 atlas/fxWeapon.atlas）");
                }
                id = "fxAtkPunchA";
                if (!lib.groups.exists(Hx(id))) return;
            }

            int startFrame = 0;
            HSprite fx = new HSprite(lib, Hx(id), Ref<int>.From(ref startFrame), null);

            SpritePivot pivot = fx.pivot;
            pivot.centerFactorX = 0.5;
            pivot.centerFactorY = 0.5;
            pivot.usingFactor = true;
            pivot.isUndefined = false;

            int dir = hero.dir >= 0 ? 1 : -1;
            fx.scaleX = (double)dir * FxScale;
            fx.scaleY = FxScale;

            hero._level.scroller.addChildAt(fx, dc.Const.Class.DP_ROOM_MAIN);

            // 锚点：精灵中心 + 朝向前移（原版 get_shootX 同思路），让刀光落在
            // 挥砍的位置而不是人身上。
            fx.x = (hero.cx + hero.xr) * 24.0 + dir * 16.0;
            fx.y = (hero.cy + hero.yr) * 24.0 - 4.0;
            fx.posChanged = true;

            AnimManager anim = fx.get_anim();
            anim.play(Hx(id), (int?)null, (bool?)null);
            // 放慢到 0.8x。⚠ 不要走 stack[0] as AnimInstance 强转（运行时可能是裸
            // 对象，MobBuffCombo 实测必崩）：先改 AnimManager.genSpeed，失败再退回实例 speed。
            bool sped = false;
            try { anim.genSpeed = FxSpeed; sped = true; } catch { }
            if (!sped)
            {
                try
                {
                    var stack = anim.stack;
                    if (stack != null && stack.length > 0
                        && stack.getDyn(0) is AnimInstance inst) inst.speed = FxSpeed;
                }
                catch { }
            }
            anim.killAfterPlay();

            _fxLog++;
            if (_fxLog <= 10 || _fxLog % 40 == 0)
            {
                Log("fx: 挥砍特效 " + id + " (" + st.Src + ") @("
                    + fx.x.ToString("0") + "," + fx.y.ToString("0") + ") dir=" + dir);
            }
        }
        catch (Exception ex)
        {
            if (!_fxWarned)
            {
                _fxWarned = true;
                Log("fx: 挥砍特效播放失败 " + ex.Message);
            }
        }
    }

    /// <summary>
    /// ICEY 原版挥砍刀光：从自制图集 atlas/iceyFx.atlas 播放（组名 = AtkStep.Fx）。
    /// 命中/打空都播；返回 false = 图集/组不可用，调用方回退 fxWeapon 流程。
    /// </summary>
    private bool TrySwingFxIcey(Hero hero, AtkStep st)
    {
        SpriteLib? lib = _iceyFxLib;
        if (lib == null)
        {
            try { lib = dc.Assets.Class?.lib?.get(Hx(IceyFxAtlas)); } catch { lib = null; }
            _iceyFxLib = lib;
            if (lib == null && !_iceyFxWarned)
            {
                _iceyFxWarned = true;
                Log("fx: atlas/iceyFx.atlas 加载失败，回退原版 fxWeapon 特效");
            }
            if (lib == null) return false;
        }
        try
        {
            if (lib.groups == null || !lib.groups.exists(Hx(st.Fx))) return false;

            int startFrame = 0;
            HSprite fx = new HSprite(lib, Hx(st.Fx), Ref<int>.From(ref startFrame), null);

            SpritePivot pivot = fx.pivot;
            pivot.centerFactorX = 0.5;
            pivot.centerFactorY = 0.5;
            pivot.usingFactor = true;
            pivot.isUndefined = false;

            int dir = hero.dir >= 0 ? 1 : -1;
            fx.scaleX = (double)dir * FxScaleIcey;
            fx.scaleY = FxScaleIcey;

            hero._level.scroller.addChildAt(fx, dc.Const.Class.DP_ROOM_MAIN);
            fx.x = (hero.cx + hero.xr) * 24.0 + dir * 16.0;
            fx.y = (hero.cy + hero.yr) * 24.0 - 4.0;
            fx.posChanged = true;

            AnimManager anim = fx.get_anim();
            anim.play(Hx(st.Fx), (int?)null, (bool?)null);
            bool sped = false;
            try { anim.genSpeed = FxSpeedIcey; sped = true; } catch { }
            if (!sped)
            {
                try
                {
                    var stack = anim.stack;
                    if (stack != null && stack.length > 0
                        && stack.getDyn(0) is AnimInstance inst) inst.speed = FxSpeedIcey;
                }
                catch { }
            }
            anim.killAfterPlay();

            _fxLog++;
            if (_fxLog <= 10 || _fxLog % 40 == 0)
            {
                Log("fx: ICEY 刀光 " + st.Fx + " (" + st.Src + ") @("
                    + fx.x.ToString("0") + "," + fx.y.ToString("0") + ") dir=" + dir);
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!_fxWarned)
            {
                _fxWarned = true;
                Log("fx: ICEY 刀光播放失败 " + ex.Message);
            }
            return false;
        }
    }

    // ------------------------------------------------------------------
    // 空中攻击悬停（F10 开关，默认开启 = ICEY 原版滞空）
    //   ICEY 原版的空中连段本来就带滞空（AirAtk1/2/6 事件表 t=0 调 AirPhysic(0)
    //   挂起重力）：整条空中连段（含连段窗口）都挂在天上，窗口结束 / 落地 /
    //   冲刺 / 二段跳才落下。只挂重力，不改碰撞/水平速度。
    // ------------------------------------------------------------------
    private void BeginAirHover(Hero hero)
    {
        if (_hoverActive) return;
        try
        {
            _hoverSavedGravity = hero.hasGravity;
            hero.hasGravity = false;
            hero.dy = 0.0; hero.bdy = 0.0;
            _hoverActive = true;
        }
        catch { _hoverActive = false; }
    }

    private void TickAirHover(Hero hero)
    {
        if (!_hoverActive) return;
        bool gone = false, stunned = false;
        try { gone = hero.destroyed || hero.life <= 0; } catch { }
        try { stunned = hero.isUnconscious(); } catch { }
        if (gone || stunned || !IsAirborne(hero)) { EndAirHover(hero); return; }
        double dy = 0.0;
        try { dy = hero.dy; } catch { }
        // 悬停里玩家按了跳（dy 变负 = 二段跳）-> 收掉悬停让跳跃正常生效
        if (dy < -0.02) { EndAirHover(hero); return; }
        try { hero.dy = 0.0; hero.bdy = 0.0; } catch { }
    }

    private void EndAirHover(Hero? hero)
    {
        if (!_hoverActive) return;
        _hoverActive = false;
        if (hero == null) return;
        try { hero.hasGravity = _hoverSavedGravity; } catch { }
    }

    /// <summary>
    /// 空中攻击物理自检（临时诊断）：重力正常时 dy 每帧 +0.065，只会在顶点附近
    /// 一瞬间接近 0。若出招期间连续 24 帧 |dy| &lt; 0.02，说明重力被什么东西挂住了
    /// （用来定位「空中攻击会停滞」到底是不是还有别的来源）。
    /// </summary>
    private void ProbeAirStall(Hero hero)
    {
        if (!_atkActive || !_atkAir || !IsAirborne(hero)) { _airStallFrames = 0; return; }
        double dy = 0.0; bool grav = true;
        try { dy = hero.dy; } catch { return; }
        try { grav = hero.hasGravity; } catch { }
        if (!grav || Math.Abs(dy) >= 0.02) { _airStallFrames = 0; return; }
        _airStallFrames++;
        if (_airStallFrames == 24 && _airPhysLog < 8)
        {
            _airPhysLog++;
            string cm = "?";
            try { cm = hero.collisionMode?.ToString() ?? "null"; } catch { }
            Log("dbg: 空中攻击 dy≈0 连续 0.4s（hasGravity=" + grav + ", collision=" + cm
                + ", 悬停开关=" + _hoverAtkEnabled + "）");
        }
    }

    // ------------------------------------------------------------------
    // 八向冲刺（O）：纯机动 + 无敌帧，零伤害（用户要求删除冲刺伤害）
    //   * 方向：方向键 / WASD 采样（同 DashOverhaul），没按方向 -> 朝面朝方向
    //   * 位移：★参考原版翻滚（DodgeRoll）★ —— 每帧只写 hero.dx / hero.dy（格/帧），
    //     由引擎 updatePositionXY 自己解算墙体/地面碰撞；不再手写 cx/xr/cy/yr、不再
    //     IgnoreWalls（那会穿墙、平地也会掉虚空）。地面段只做水平位移、保持重力，
    //     悬崖保护＝格子级探测＋每帧位移夹紧（不依赖 get_pf，绝不会冲出平台掉虚空）；
    //     空中段才关重力走八向。
    //   * 无敌帧：hero.toggleFullInvincibility（原版 FlaskGoggles 同款完整无敌）
    //   * 输入锁定：hero.lockControlFromSkill，避免方向键额外推着人走
    //   * 连贯：单段 7.2 格（本轮 x2）；跑过 25% 后再按 O 立刻续段，冲刺中按过 O
    //     会缓冲到本段结束自动接上；冷却只有 0.12s，且攻击命中会回充
    //   * 一闪：起手 FlashWindowS 内穿过敌人只回血（3s CD），不结算任何伤害
    // ------------------------------------------------------------------
    private bool TryStartDash(Hero hero)
    {
        if (_dashing)
        {
            // 连贯冲刺：跑过 DashChainFrac 之后，任何时候再按 O 都能立刻续下一段
            //（不再要求命中敌人；方向按当前按键重新采样 -> 可空中变向连冲）
            if (_dashTraveled < DashMaxDist * DashChainFrac) return false;
        }
        else if (_dashCdLeft > 0.0)
        {
            return false;
        }
        StartDash(hero);
        return true;
    }

    private void StartDash(Hero hero)
    {
        EndAirHover(hero);
        ReleaseAttackRoot(hero);   // 冲刺 = 攻击定身的正当取消途径（ICEY 冲刺可打断一切）
        bool grounded = !IsAirborne(hero);   // 地面段/空中段一次判定（两者位移驱动方式不同）
        double vx = 0.0, vy = 0.0;
        if (KeyDown(VkRight) || KeyDown(VkD)) vx += 1.0;
        if (KeyDown(VkLeft) || KeyDown(VkA)) vx -= 1.0;
        if (KeyDown(VkDown) || KeyDown(VkS)) vy += 1.0;
        if (KeyDown(VkUp) || KeyDown(VkW)) vy -= 1.0;   // cy 向下增长
        if (vx == 0.0 && vy == 0.0) vx = (hero.dir >= 0) ? 1.0 : -1.0;
        // 站在地面上不做垂直冲（下冲第一步就撞地 = 0 格 + 白吃冷却，上冲没意义）
        // -> 一律朝面朝/输入方向水平冲；空中保持八向（向下冲落地即停）。
        if (grounded)
        {
            vy = 0.0;
            if (vx == 0.0) vx = (hero.dir >= 0) ? 1.0 : -1.0;
        }
        double len = Math.Sqrt(vx * vx + vy * vy);
        vx /= len; vy /= len;

        if (!_dashing)
        {
            try { _savedGravity = hero.hasGravity; } catch { _savedGravity = true; }
            try { _savedCollision = hero.collisionMode; } catch { _savedCollision = null; }
        }

        try
        {
            // ★ 参考翻滚：地面段保持原版重力与碰撞模式（千万别再 IgnoreWalls）；
            //   只有空中段关重力，竖直方向才冲得动。
            if (!grounded) hero.hasGravity = false;
            if (vx != 0.0) hero.dir = vx > 0.0 ? 1 : -1;
            hero.dx = 0.0; hero.dy = 0.0; hero.bdx = 0.0; hero.bdy = 0.0;
            hero.lockControlFromSkill(DashLockS);
        }
        catch (Exception ex) { Log("dash: 起手接管失败 " + ex.Message); }

        // ★ 小体型（原版翻滚的 affect 3）不再起手就套：只有检测到 0x40 矮洞爬行通道时才套
        //   （见 TickDash 的 crawlHere）—— 平时保持原体型，避免「蹲姿限速」之类的常驻副作用。

        if (!_invincible)
        {
            try { hero.toggleFullInvincibility(true); _invincible = true; } catch { }
        }
        PlayAnim(hero, "dash", DashMaxDist / DashTargetSpeed);   // dash 动画铺满整段位移（7.2 格 ≈ 0.27s）

        _atkActive = false; _atkQueuedKind = 0;      // 冲刺打断攻击后摇

        _dashing = true; _dashHero = hero;
        _dashVx = vx; _dashVy = vy;
        _dashGrounded = grounded;
        _dashTraveled = 0.0; _dashElapsed = 0.0;
        _dashStillTime = 0.0;
        _dashLastX = hero.cx + hero.xr;
        _dashLastY = hero.cy + hero.yr;
        _dashHitEnemy = false; _flashDoneThisDash = false; _dashQueued = false;
        _dashBodyLogged = false;
        _dashStillTime = 0.0; _dashWinTime = 0.0; _dashWinDist = 0.0;
        _dashEngineBroken = false;   // 每段重新探测：旧版一旦误判就永久兜底 -> 之后全部不能爬洞口
        _dashGroundCy = hero.cy; _dashGroundYr = hero.yr;
        _dashHadTileGround = GroundSupported(hero, hero.cx, FeetRow(hero));
        _dashCrawlLogged = false;
        _dashHitUids.Clear();
        _dashCdLeft = DashCdS;
        Log("dash: 冲刺 (" + vx.ToString("0.##") + "," + vy.ToString("0.##") + ") "
            + DashMaxDist.ToString("0.#") + " 格 · " + (grounded ? ("地面段(格子悬崖保护" + (_dashHadTileGround ? "" : "，脚下无瓦片地板") + ")") : "空中段")
            + "，无敌帧开（零伤害，小体型可钻矮洞）");
    }

    private void TickDash(Hero hero, double dt)
    {
        if (!_dashing) return;
        if (_dashHero != hero || hero.destroyed || hero._level == null || hero.life <= 0)
        {
            EndDash(hero, "英雄不可用");
            return;
        }

        // ★ 越界回拉：掉帧时引擎一帧可能把 dx 应用多次，位移前夹紧拦不住 ->
        //   只要「脚底当前格」已经失去地板支撑，立刻拉回最后一格地板边缘并收手（绝不掉虚空）。
        if (_dashGrounded && _dashVx != 0.0 && _dashHadTileGround)
        {
            int cdir2 = _dashVx >= 0.0 ? 1 : -1;
            int frNow = FeetRow(hero);
            if (!GroundSupported(hero, hero.cx, frNow))
            {
                double px = DashGroundPullBackX(hero, cdir2);
                double feetX2 = hero.cx + hero.xr;
                double delta2 = Math.Abs(px - feetX2);
                try
                {
                    int ncx2 = (int)Math.Floor(px);
                    hero.cx = ncx2; hero.xr = px - ncx2;
                    hero.cy = (int)_dashGroundCy; hero.yr = _dashGroundYr;   // 已经开始下坠也拉回台面
                    hero.dx = 0.0; hero.dy = 0.0;
                }
                catch { }
                if (delta2 > 0.05) Log("dash: 越界回拉 " + delta2.ToString("0.##") + " 格（脚底已离开地板）");
                EndDash(hero, "到平台边缘");
                return;
            }
            _dashGroundCy = hero.cy; _dashGroundYr = hero.yr;          // 记录最后的站台位置
        }

        // 距离封顶在位移前先判：引擎掉帧时一帧可能把 dx 应用多次，事后计程必然超标
        if (_dashTraveled >= DashMaxDist - 0.001) { EndDash(hero, "冲满"); return; }
        if (_dashElapsed > DashHardCapS) { EndDash(hero, "超时"); return; }

        double step = DashTargetSpeed * dt;    // 兜底路径：按真实时间推进（与渲染帧率无关）
        double remain = DashMaxDist - _dashTraveled;
        if (step > remain) step = remain;

        double ax = hero.cx + hero.xr;
        double ay = hero.cy + hero.yr;

        // ★ 矮洞爬行通道检测（0x40）：只有它才需要小体型（原版翻滚 affect 3）。
        //   canCrawlThrought 的第一条件就是 affects[3] 非空；爬行比一段冲刺慢，
        //   所以检测到就每帧续期（0.25s 滚动窗口），离开通道后自然过期。
        bool crawlHere = _dashGrounded && CrawlAheadOf(hero, _dashVx >= 0.0 ? 1 : -1);
        if (crawlHere)
        {
            try { hero.setAffectS(DashCrouchAffectId, DashCrouchRefreshS, Ref<double>.Null, null); } catch { }
            if (!_dashCrawlLogged)
            {
                _dashCrawlLogged = true;
                Log("dash: 检测到矮洞爬行通道(0x40)，套小体型并保持冲刺让引擎爬过去");
            }
        }

        // ★ 地面段：格子级悬崖保护 —— 每帧把本帧位移夹紧到「前方最后一格有支撑的地板」以内，
        //   即使掉帧一帧走得比保护余量还远，脚底也绝不可能越过地板边缘。旧实现依赖
        //   hero.get_pf()：人一离开平台它就变 null，保护直接失效 -> 悬空滑出去掉虚空。
        double cliffAllow = -1.0;
        if (_dashGrounded && _dashVx != 0.0 && _dashHadTileGround)
        {
            int cdir = _dashVx >= 0.0 ? 1 : -1;
            double limit = DashGroundLimitX(hero, cdir);
            double feetX = hero.cx + hero.xr;
            double allow = cdir > 0 ? (limit - feetX) : (feetX - limit);
            if (allow <= DashCliffStopEps)
            {
                EndDash(hero, "到平台边缘");
                return;
            }
            cliffAllow = allow;
            if (step > allow) step = allow;    // 兜底路径按 tick 计；引擎路径下面再按帧数夹一次
        }

        if (!_dashEngineBroken)
        {
            try
            {
                // ★ 参考翻滚：位移交给引擎 —— 每帧只写 hero.dx / hero.dy（单位 = 格/帧），
                //   updatePositionXY 自己按墙体/地面碰撞解算；地面段连重力都不动。
                hero.bdx = 0.0; hero.bdy = 0.0;
                // ★★ hero.dx/dy 的单位是「格/引擎物理步」，引擎每固定步消耗一次 —— 绝不能乘 dt！
                //    高刷（几百 fps）时 hook 的 dt 很小，乘 dt 等于把速度按刷新率稀释，
                //    冲刺就变成「很慢很慢的翻滚」。这里写常数 DashStepPerFrame×自校准系数：
                //    距离封顶用 remain 收，悬崖夹紧按「本 tick 预计经过几个物理步」折算。
                double frames = Math.Max(1.0, dt * 30.0);   // 本 tick 大约会跑几个引擎物理步（≈30Hz）
                double dvWrite = Math.Min(DashStepPerFrame * _dashSpeedScale, Math.Max(0.0, remain));
                if (cliffAllow >= 0.0) dvWrite = Math.Min(dvWrite, cliffAllow / frames);
                hero.dx = _dashVx * dvWrite;
                // ★ 地面段不写 dy：重力/贴地完全交给引擎（原版翻滚 applyDodgeMovement 也只写 dx）。
                if (!_dashGrounded)
                {
                    hero.hasGravity = false;
                    hero.dy = _dashVy * dvWrite;
                }
                hero.lockControlFromSkill(DashLockS);
            }
            catch (Exception ex) { Log("dash: 位移异常 " + ex.Message); }
        }
        else
        {
            // 兜底：引擎 dx/dy 被吃掉（例如控制锁期间）时，自己安全地按格推进
            try
            {
                hero.dx = 0.0; hero.dy = 0.0; hero.bdx = 0.0; hero.bdy = 0.0;
                if (!SafeStep(hero, _dashVx * step, _dashVy * step))
                {
                    EndDash(hero, _dashVy > 0.35 ? "落地" : "撞墙");
                    return;
                }
                if (!_dashGrounded) hero.hasGravity = false;
                hero.lockControlFromSkill(DashLockS);
            }
            catch (Exception ex) { Log("dash: 兜底位移异常 " + ex.Message); }
        }

        // 空中向下冲：贴到地面就立刻收（落地即停）
        if (!_dashGrounded && _dashVy > 0.35)
        {
            try
            {
                if (!IsAirborne(hero)) { EndDash(hero, "落地"); return; }
            }
            catch { }
        }

        double bx = hero.cx + hero.xr;
        double by = hero.cy + hero.yr;
        // ⚠ 位移必须「跨帧」统计：引擎物理在本帧钩子之外跑，同帧前后相减恒为 0
        //  （上一版就栽在这：误判成撞墙 -> 冲 0 格）
        double ddx = bx - _dashLastX, ddy = by - _dashLastY;
        double moved = Math.Sqrt(ddx * ddx + ddy * ddy);
        _dashLastX = bx; _dashLastY = by;
        // 地面段只按水平位移计程：贴地/上坡带来的纵向位移不计入里程，
        // 不然会虚增里程提前「冲满」（旧日志里 7.2 格的冲刺记成 11.14 格）
        _dashTraveled += _dashGrounded ? Math.Abs(ddx) : moved;
        _dashElapsed += dt;

        // ★ 速度自校准：不管引擎物理步到底是 30Hz 还是别的内部步长，用「实测均速 / 目标速度」
        //   反馈微调写入的 dx —— 第一段冲刺里每 0.07s 采样一次，两三次就收敛，之后全速。
        //   （只在本段是引擎路径、没钻矮洞、没被悬崖夹紧、还没接近冲满时采样。）
        if (!_dashEngineBroken && !crawlHere && cliffAllow < 0.0 && remain > 1.2)
        {
            _dashWinTime += dt; _dashWinDist += moved;
            if (_dashWinTime >= DashAdaptWindowS)
            {
                double avg = _dashWinDist / _dashWinTime;
                if (avg > 1.0)
                {
                    if (avg < DashTargetSpeed * 0.75)
                        _dashSpeedScale = Math.Min(DashSpeedScaleMax, _dashSpeedScale * 1.25);
                    else if (avg > DashTargetSpeed * 1.35)
                        _dashSpeedScale = Math.Max(DashSpeedScaleMin, _dashSpeedScale * 0.85);
                }
                _dashWinTime = 0.0; _dashWinDist = 0.0;
            }
        }

        if (!_dashBodyLogged && _dashElapsed > 0.08)
        {
            _dashBodyLogged = true;
            try { Log("dash: 小体型检查 radius=" + hero.radius.ToString("0.#") + " hei=" + hero.hei.ToString("0.#") + " smallHitbox=" + hero.smallHitbox); } catch { }
        }

        // 引擎 dx/dy 完全不生效（被控制锁等吃掉）-> 本段起改用兜底直接位移
        if (!_dashEngineBroken && _dashElapsed > DashEngineProbeS && _dashTraveled < 0.1
            && !crawlHere && !WallAheadOf(hero, _dashVx >= 0.0 ? 1 : -1))
        {
            _dashEngineBroken = true;
            Log("dash: 引擎 dx/dy 位移未生效 -> 切换兜底直接位移（仍会撞墙/悬崖收手）");
        }

        // 撞墙：连续多帧几乎没动就收手（刚起手那两三帧跳过，别误判）；爬行通道（上面已判）不算。
        if (!crawlHere && moved < 0.02 && _dashElapsed > 0.10)
        {
            _dashStillTime += dt;
            if (_dashStillTime >= DashStillTimeMax)
            {
                try
                {
                    int cdir3 = _dashVx >= 0.0 ? 1 : -1;
                    int frD = FeetRow(hero);
                    Log("dash: 撞墙诊断 脚底格=" + CellFlags(hero, hero.cx, frD).ToString("X")
                        + " 前方格=" + CellFlags(hero, hero.cx + cdir3, frD).ToString("X")
                        + "/" + CellFlags(hero, hero.cx + cdir3, frD - 1).ToString("X")
                        + " canCrawl=" + hero.canCrawlThrought(hero.cx + cdir3, frD));
                }
                catch { }
                EndDash(hero, "撞墙");
                return;
            }
        }
        else _dashStillTime = 0.0;

        DashAfterimage(hero);

        // 一闪：只有起手窗口内的「穿人」才记（只回血，不结算伤害）
        if (_dashElapsed <= FlashWindowS)
        {
            int seen = FlashDetect(hero, ax, ay, bx, by);
            if (seen > 0)
            {
                _dashHitEnemy = true;
                if (!_flashDoneThisDash)
                {
                    _flashDoneThisDash = true;
                    Log("flash: 一闪！无敌帧冲刺穿过 " + seen + " 个敌人（无伤害）");
                    TryFlashHeal(hero);
                }
            }
        }

        if (_dashTraveled >= DashMaxDist - 0.001) EndDash(hero, "冲满");
    }

    private void EndDash(Hero? hero, string reason)
    {
        if (!_dashing) return;
        _dashing = false;
        double traveled = _dashTraveled;

        if (hero != null)
        {
            // 每一步单独兜底：任何一步失败都不能让「重力/碰撞/无敌帧」留在冲刺状态
            try { hero.cancelSkillControlLock(); } catch { }
            try { hero.hasGravity = _savedGravity; } catch { }
            try { hero.collisionMode = _savedCollision ?? new dc.CollisionMode.Normal(); } catch { }
            // 收掉冲刺注入的速度（引擎本来也会摩擦衰减，这里直接清零更干脆）
            try { hero.dx = 0.0; hero.dy = 0.0; hero.bdx = 0.0; hero.bdy = 0.0; } catch { }
            try { Unstick(hero); } catch { }
            if (_invincible)
            {
                try { hero.toggleFullInvincibility(false); } catch { }
            }
        }
        _invincible = false;

        _dashHero = null;
        _dashGrounded = false;
        _dashStillTime = 0.0;
        if (!_dashHitEnemy) _dashCdLeft = DashCdS;   // 一闪穿过敌人就不罚冷却
        Log("dash: 结束(" + reason + ") 位移 " + traveled.ToString("0.##") + " 格，用时 "
            + _dashElapsed.ToString("0.00") + "s（均速 "
            + (_dashElapsed > 0.02 ? (traveled / _dashElapsed).ToString("0.#") : "-") + " 格/s，倍率×"
            + _dashSpeedScale.ToString("0.00") + "）");

        // 冲刺中按过 O 的缓冲：直接在冲刺结束接下一段（连贯冲刺，不再等冷却/命中）
        bool queued = _dashQueued;
        _dashQueued = false;
        if (queued && reason == "冲满"
            && hero != null && !hero.destroyed && hero._level != null && hero.life > 0)
        {
            StartDash(hero);
        }
    }

    /// <summary>一闪检测：冲刺路径上是否有敌人（只数命中数，不造成任何伤害）。</summary>
    private int FlashDetect(Hero hero, double ax, double ay, double bx, double by)
    {
        dc.tool.Team? team = hero._team;
        if (team == null) return 0;

        double heroR = 0.5;
        try { heroR = hero.radius / 24.0; } catch { }

        int hits = 0;
        try
        {
            var iter = team.opponentsIterator.reset(team);
            if (iter == null) return 0;
            while (iter.hasNext())
            {
                dc.Entity e = iter.next();
                if (e == null || e.destroyed || e.life <= 0) continue;

                int uid;
                try { uid = e.__uid; } catch { uid = -1; }
                if (uid >= 0 && _dashHitUids.Contains(uid)) continue;

                double ex = e.cx + e.xr;
                double ey = e.cy + e.yr;
                double r;
                try { r = heroR + e.radius / 24.0 + DashHitPad; } catch { r = heroR + 0.5 + DashHitPad; }
                if (DistPointToSegment(ex, ey, ax, ay, bx, by) > r) continue;
                if (uid >= 0) _dashHitUids.Add(uid);
                hits++;
            }
        }
        catch (Exception ex) { Log("dash: 一闪检测异常 " + ex.Message); }
        return hits;
    }

    private void DashAfterimage(Hero hero)
    {
        try
        {
            OnionSkin.Class.fromEntity(hero, null, DashTrailColor,
                Ref<double>.In(0.85), Ref<double>.In(0.25),
                Ref<bool>.Null, Ref<bool>.Null, Ref<double>.Null);
        }
        catch { }
    }

    /// <summary>攻击命中敌人 -> 冲刺冷却回充（ICEY「以攻击换冷却」）。</summary>
    private void RechargeDash(int hits)
    {
        if (hits <= 0 || _dashCdLeft <= 0.0) return;
        _dashCdLeft = Math.Max(0.0, _dashCdLeft - DashRechargePerHitS * hits);
        if (_dashCdLeft <= 0.0) Log("dash: 攻击命中 -> 冲刺已充能（可立刻再冲）");
    }

    /// <summary>一闪回血（独立 3s CD，防止无限回血）。</summary>
    private void TryFlashHeal(Hero hero)
    {
        if (_flashHealCdLeft > 0.0) return;
        int max = 0;
        try { max = hero.maxLife; } catch { }
        int heal = (int)Math.Clamp(max * FlashHealPct, FlashHealMin, FlashHealMax);
        if (heal <= 0) return;
        try
        {
            hero.life = hero.life + heal;
            if (max > 0 && hero.life > max) hero.life = max;
            hero.updateLifeBar();
            _flashHealCdLeft = FlashHealCdS;
            Log("flash: 一闪回血 +" + heal + "（life=" + hero.life + "/" + max + "）");
        }
        catch (Exception ex) { Log("flash: 回血失败 " + ex.Message); }
    }

    /// <summary>
    /// 播放一次性动画（非 state 动画，播完自动回到状态机），并把播放速度调成
    /// 「整段耗时 = srcDur 秒」：DC 帧率 = 30fps x AnimInstance.speed
    /// （genSpeed = 0.5、dt 按帧计），所以 speed = 帧数 / (30 x srcDur)。
    /// 新烘的攻击组按 60fps 采样 -> speed 恰好 2.0；旧组（帧数 = 原版同组帧数）
    /// 会自动拉长/压缩到 ICEY 源时长。
    /// </summary>
    private void PlayAnim(Hero hero, string group, double srcDur)
    {
        try
        {
            AnimManager? anim = hero.spr?.get_anim();
            if (anim == null) return;
            anim.play(Hx(group), (int?)null, (bool?)null);
            var stack = anim.stack;
            if (stack == null || stack.length == 0) return;
            if (stack.getDyn(0) is not AnimInstance inst) return;
            if (srcDur <= 0.0) return;
            int n = 0;
            try { n = inst.frames?.length ?? 0; } catch { n = 0; }
            if (n <= 0) return;
            double spd = n / (30.0 * srcDur);
            if (spd > 0.05 && spd < 20.0) inst.speed = spd;
        }
        catch (Exception ex) { Log("anim: 播放失败 " + group + " -> " + ex.Message); }
    }

    private static double DistPointToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double lenSq = dx * dx + dy * dy;
        if (lenSq <= 1e-9) return Math.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
        double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;
        double qx = ax + t * dx;
        double qy = ay + t * dy;
        return Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
    }

    /// <summary>
    /// 脚底支撑行的行号：DC 站立约定 yr≈1（脚下地板在 cy+1），也兼容 yr≈0（地板在 cy）。
    /// </summary>
    private static int FeetRow(Hero hero) => hero.yr > 0.5 ? hero.cy + 1 : hero.cy;

    /// <summary>读地图格子碰撞位；取不到地图 = -1（调用方一律「别拦」），地图外 = 0（当空）。</summary>
    private static int CellFlags(Hero hero, int col, int row)
    {
        try
        {
            dc.pr.Level? lvl = hero._level;
            if (lvl == null) return -1;
            dc.level.LevelMap? map = lvl.map;
            if (map == null) return -1;
            if (map.collisions == null) return -1;
            if (col < 0 || col >= map.wid || row < 0 || row >= map.hei) return 0;
            int idx = row * map.wid + col;
            if (idx < 0 || idx >= map.collisions.length) return 0;
            return map.collisions.getDyn(idx);
        }
        catch { return -1; }
    }

    private static bool HasBit(int flags, int mask) => flags > 0 && (flags & mask) != 0;

    /// <summary>
    /// 某列朝下 2 行内有没有可踩的地板：实心 1 / 单向平台 4 / 斜坡 0x200。
    /// （isPositionEmpty 把单向平台当「空」，不能拿来当地板判定。）
    /// 允许 1 格小台阶：滚下台阶接着冲，深坑才算悬崖。
    /// </summary>
    private static bool GroundSupported(Hero hero, int col, int row)
    {
        for (int r = row; r <= row + 1; r++)
        {
            int c = CellFlags(hero, col, r);
            if (c < 0) return true;                       // 取不到地图：不拦（别误停）
            if ((c & 1) != 0 || (c & 4) != 0 || (c & 512) != 0) return true;
        }
        return false;
    }

    /// <summary>
    /// 脚下/前方是不是 0x40「矮洞爬行通道」（原版翻滚 canCrawlThrought 认的标志位）。
    /// 冲刺钻进这种格子时引擎会限速爬行 —— 此时绝不能按「撞墙」收手。
    /// </summary>
    private static bool CrawlAheadOf(Hero hero, int dir)
    {
        int fr = FeetRow(hero);
        return HasBit(CellFlags(hero, hero.cx + dir, fr), 0x40)
            || HasBit(CellFlags(hero, hero.cx + dir, fr - 1), 0x40)
            || HasBit(CellFlags(hero, hero.cx, fr), 0x40)
            || HasBit(CellFlags(hero, hero.cx, fr - 1), 0x40);
    }

    /// <summary>冲刺方向紧邻的身体格是不是实心墙（用来区分「撞墙」和「引擎根本没在动」）。
    /// 撞墙时探测不该切兜底（旧版就是在这里误判 -> 永久兜底 -> 不能爬洞口）。</summary>
    private static bool WallAheadOf(Hero hero, int dir)
    {
        int fr = FeetRow(hero);
        return HasBit(CellFlags(hero, hero.cx + dir, fr - 1), 1)
            || HasBit(CellFlags(hero, hero.cx + dir, fr - 2), 1);
    }

    /// <summary>越界回拉目标：从当前位置往回找最近的有地板支撑列，返回该列内 DashCliffInset 处的 x。</summary>
    private static double DashGroundPullBackX(Hero hero, int dir)
    {
        int row = FeetRow(hero);
        int back = dir > 0 ? -1 : 1;
        for (int k = 0; k <= DashCliffScanMax; k++)
        {
            int c = hero.cx + back * k;
            if (GroundSupported(hero, c, row))
                return dir > 0 ? (c + 1) - DashCliffInset : c + DashCliffInset;
        }
        return hero.cx + hero.xr;    // 找不到：别乱动
    }

    /// <summary>
    /// 地面冲刺的推进上限（x 坐标）：沿冲刺方向数「前方还有多少格地板连着」，
    /// 允许脚底最多推进到最后一格支撑列内 DashCliffInset 格处；再往前就收手。
    /// ——纯格子探测，不吃 hero.get_pf()（人一离开平台 get_pf 立刻变 null，旧实现就栽在这：
    /// 一旦因掉帧一帧跨过边缘，保护就失效，人悬空滑出去然后掉进虚空）。
    /// </summary>
    private static double DashGroundLimitX(Hero hero, int dir)
    {
        int row = FeetRow(hero);
        int col = hero.cx;
        int run = 0;
        while (run < DashCliffScanMax && GroundSupported(hero, col + dir * (run + 1), row)) run++;
        return dir > 0 ? (col + run + 1) - DashCliffInset
                       : (col - run) + DashCliffInset;
    }

    /// <summary>
    /// 兜底直接位移（仅当引擎 dx/dy 不生效时启用）：按格推进前先探测目标格的
    /// 「身体」（脚底往上 1 格）是否为空 —— 撞墙就收手（绝不穿墙）；向下时脚底
    /// 格从空变实 = 到地面，收手。配合悬崖保护 -> 平地也不会掉进虚空。
    /// 0x40 矮洞爬行格例外：小体型（affects[3]）下原版本来就允许穿过去。
    /// </summary>
    private static bool SafeStep(Hero hero, double ddx, double ddy)
    {
        double nx = hero.cx + hero.xr + ddx;
        double ny = hero.cy + hero.yr + ddy;
        int ncx = (int)Math.Floor(nx);
        int ncy = (int)Math.Floor(ny);

        try
        {
            if (ddy > 0.0 && !hero.isPositionEmpty(ncx, ncy)) return false;   // 向下：踩到地面
            // 身体撞墙 —— 但 0x40 矮洞爬行格例外：翻滚态（affects[3]）下原版就允许穿过去
            if (!hero.isPositionEmpty(ncx, ncy - 1) && !hero.canCrawlThrought(ncx, ncy - 1)) return false;
        }
        catch { }

        hero.cx = ncx;
        hero.xr = nx - ncx;
        hero.cy = ncy;
        hero.yr = ny - ncy;
        return true;
    }

    /// <summary>
    /// 冲刺结束脱困：只有「身体」卡进实心格才需要挪。
    /// ⚠ 不能用脚底格 (cx, cy) 判断 —— 站在地面上时脚底点本来就落在脚下的实心
    /// 格子里（DC 的坐标约定），拿它判断会把「正常站在地上」误判成「卡墙」，
    /// 于是每次都把人往上挪一格（以前还有 yr = 0.5 的瞬间位移）= 用户看到的
    /// 「闪着闪着跳到另一个地方」。现在检查/挪动都用脚底往上 1 格的身体位。
    /// </summary>
    private static void Unstick(Hero hero)
    {
        try
        {
            if (hero.isPositionEmpty(hero.cx, hero.cy - 1)) return;   // 身体是空的 -> 没卡住
            for (int d = 1; d <= 3; d++)
            {
                if (hero.isPositionEmpty(hero.cx, hero.cy - 1 - d))
                {
                    hero.cy -= d;
                    return;
                }
            }
            for (int d = 1; d <= 4; d++)
            {
                if (hero.isPositionEmpty(hero.cx + d, hero.cy - 1)) { hero.cx += d; return; }
                if (hero.isPositionEmpty(hero.cx - d, hero.cy - 1)) { hero.cx -= d; return; }
            }
        }
        catch { }
    }

    private static bool KeyDown(int vk)
    {
        try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        catch { return false; }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
