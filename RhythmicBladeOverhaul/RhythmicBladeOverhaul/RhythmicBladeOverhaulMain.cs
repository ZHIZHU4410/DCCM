using dc;
using dc.en;
using dc.en.active;
using dc.en.inter;
using dc.hl.types;
using dc.pr;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Storage;
using ModCore.Utilities;
using System.Runtime.CompilerServices;

namespace RhythmicBladeOverhaul;

/// <summary>
/// 节奏之刃（Oud / RhythmicBlade）改造
/// =================================================================
///
/// 【原版机制】
///   3 段 strikeChain（data.cdb → weapon@strikeChain）：
///     cycle 0 = atkOudeA   power 55
///     cycle 1 = atkOudeB   power 80
///     cycle 2 = atkOudeC   power 130、critMul 1.5、animSpd 1.33
///   prepare() / onAnticipate() 判断有没有踩准节拍 → shouldCrit。
///   onExecute() 末尾：shouldCrit 且 cycle==2 时 set_cycle(1)，于是最后一下无限循环。
///
/// 【命中判定链，改之前必须看清】
///   RhythmicBlade.onExecute 的循环是：
///        realCurArea = getRealCurArea();
///        if (canHit(entity, realCurArea)) hitFromWeapon(entity, ...);
///   · 判定框来自 getRealCurArea()：
///       cycle 1 → new Area(144, 120)          矩形 6×5 格
///       cycle 2 → new Area(144, null)         ★ 圆，半径 4.5 格，圆心就是主角
///     （canHit 里圆形分支的判定是
///       dist²(圆心, 目标中心) ≤ (widPx*0.5 + e.radius)²  → 一个标准圆）
///   · canHit 在几何判定通过之后，还有一段**沿主角→目标做 Bresenham 直线、
///     逐格检查地图碰撞位**的墙体检测；被墙挡住就返回 false。
///     → 这就是"特效碰到了却打不到"的原因：特效不查墙，判定查墙。
///   · canHit 还会检查"目标自己是否站在实心格上"。
///
/// 【本模组的做法】
///   1) 取消节拍判定：prepare / onAnticipate 之后强制 shouldCrit = true
///      （顺带把节拍灯刷绿），再也不会有"这次没踩上"。
///   2) 恒为节奏打击：get_cycle 对节奏之刃恒返回 2。
///   3) 只保留节奏打击：initSkill 把每段 strikeChain 的 animId 统一成 atkOudeC。
///   4) 命中判定改成"以主角为圆心的 360° 圆"：
///      在 Hook_Weapon.canHit 里对节奏之刃直接做距离判定，
///      保留原版的存活 / canBeHit / 敌对阵营过滤，
///      但**完全跳过墙体与朝向**——圆就该打一圈。
///      半径 = 判定框 widPx * 0.5 + 目标 radius，单位都换算成格。
///   5) 每杀 1 个敌人范围 +10%：getRealCurArea 的判定框 ×(1.1)^击杀数（只乘一次）。
///   6) 伤害跟着范围涨：hitFromWeapon 里把 AttackData.dmgMultiplier × 当前范围倍率。
///   7) 特效跟着范围涨：dispShockWave 的半径改成"当前判定半径"，与判定范围一致
///      （可用 EffectRadiusScale 微调）。
/// </summary>
public class RhythmicBladeOverhaulMain
    : ModBase, IOnGameExit, IModMenu
{
    // ================================================================
    // ★ 常量
    // ================================================================

    /// <summary>节奏打击所在的 strikeChain 下标。</summary>
    private const int RhythmCycle = 2;

    /// <summary>节奏打击使用的角色动画组名（CSV: weapon@strikeChain[2].animId）。</summary>
    private const string RhythmAnimId = "atkOudeC";

    /// <summary>一格的像素数（游戏里到处是 24.0）。</summary>
    private const double CellSize = 24.0;

    // ================================================================
    // 运行时状态
    // ================================================================

    private bool _disposed;

    /// <summary>当前正在结算 onExecute 的武器；360° 判定只在此时接管。</summary>
    private RhythmicBlade? _executing;

    /// <summary>当前正在结算 hitFromWeapon 的武器；用于给这次伤害加倍率。</summary>
    private RhythmicBlade? _hitting;

    public RhythmicBladeOverhaulMain(ModInfo info) : base(info) { }

    // ================================================================
    // 生命周期
    // ================================================================

    public override void Initialize()
    {
        base.Initialize();
        _disposed = false;

        // 这些方法声明在 dc.tool.Weapon / dc.Fx 上，所以钩子挂基类再按类型过滤
        //（Hook_RhythmicBlade 里只有它自己重写的成员）。
        HookSafe("Weapon.get_cycle", () => Hook_Weapon.get_cycle += OnWeaponGetCycle);
        HookSafe("Weapon.initSkill", () => Hook_Weapon.initSkill += OnWeaponInitSkill);
        HookSafe("Weapon.canHit", () => Hook_Weapon.canHit += OnWeaponCanHit);
        HookSafe("RhythmicBlade.prepare", () => Hook_RhythmicBlade.prepare += OnRhythmicBladePrepare);
        HookSafe("RhythmicBlade.onAnticipate", () => Hook_RhythmicBlade.onAnticipate += OnRhythmicBladeAnticipate);
        HookSafe("RhythmicBlade.onExecute", () => Hook_RhythmicBlade.onExecute += OnRhythmicBladeExecute);
        HookSafe("RhythmicBlade.getRealCurArea", () => Hook_RhythmicBlade.getRealCurArea += OnRhythmicBladeGetRealCurArea);
        HookSafe("RhythmicBlade.hitFromWeapon", () => Hook_RhythmicBlade.hitFromWeapon += OnRhythmicBladeHit);
        HookSafe("RhythmicBlade.dynOnFxFrame", () => Hook_RhythmicBlade.dynOnFxFrame += OnRhythmicBladeFxFrame);
        HookSafe("_AttackUtils.hit", () => Hook__AttackUtils.hit += OnAttackUtilsHit);
        HookSafe("Fx.dispShockWave", () => Hook_Fx.dispShockWave += OnDispShockWave);

        Log($"[RhythmicBladeOverhaul] 已加载：必定节奏打击 + 360°圆形打击(无视墙体) + 范围/伤害/特效 ×{RhythmicBladeKeys.Config.Value.Multiplier:0.##}");
    }

    void IOnGameExit.OnGameExit()
    {
        _disposed = true;
        try { Hook_Weapon.get_cycle -= OnWeaponGetCycle; } catch { }
        try { Hook_Weapon.initSkill -= OnWeaponInitSkill; } catch { }
        try { Hook_Weapon.canHit -= OnWeaponCanHit; } catch { }
        try { Hook_RhythmicBlade.prepare -= OnRhythmicBladePrepare; } catch { }
        try { Hook_RhythmicBlade.onAnticipate -= OnRhythmicBladeAnticipate; } catch { }
        try { Hook_RhythmicBlade.onExecute -= OnRhythmicBladeExecute; } catch { }
        try { Hook_RhythmicBlade.getRealCurArea -= OnRhythmicBladeGetRealCurArea; } catch { }
        try { Hook_RhythmicBlade.hitFromWeapon -= OnRhythmicBladeHit; } catch { }
        try { Hook_RhythmicBlade.dynOnFxFrame -= OnRhythmicBladeFxFrame; } catch { }
        try { Hook__AttackUtils.hit -= OnAttackUtilsHit; } catch { }
        try { Hook_Fx.dispShockWave -= OnDispShockWave; } catch { }

        _executing = null;
        _hitting = null;
        Log("[RhythmicBladeOverhaul] 游戏退出，已卸载");
    }

    // ================================================================
    // 1) 取消判定：每次都是节奏打击
    // ================================================================

    private void OnRhythmicBladePrepare(Hook_RhythmicBlade.orig_prepare orig, RhythmicBlade self, double attackSpeed)
    {
        orig(self, attackSpeed);
        ForceBeat(self);
    }

    private void OnRhythmicBladeAnticipate(Hook_RhythmicBlade.orig_onAnticipate orig, RhythmicBlade self)
    {
        orig(self);
        ForceBeat(self);
    }

    private void ForceBeat(RhythmicBlade? self)
    {
        if (_disposed || self == null) return;
        if (!RhythmicBladeKeys.Config.Value.EnableMod) return;

        try
        {
            self.shouldCrit = true;
            self.shouldCritNext = true;
            if (!self.beatFxActivated) self.updateBeatFx(correct: true);
        }
        catch (Exception ex)
        {
            Log($"[RhythmicBladeOverhaul] 强制节拍失败: {ex.Message}");
        }
    }

    // ================================================================
    // 2) 恒为节奏打击
    // ================================================================

    private int OnWeaponGetCycle(Hook_Weapon.orig_get_cycle orig, Weapon self)
    {
        if (!_disposed && self is RhythmicBlade && RhythmicBladeKeys.Config.Value.EnableMod)
        {
            return RhythmCycle;
        }
        return orig(self);
    }

    /// <summary>把每段 strikeChain 的攻击动画统一成 atkOudeC，前两段挥砍不再出现。</summary>
    private void OnWeaponInitSkill(
        Hook_Weapon.orig_initSkill orig,
        Weapon self,
        int idx,
        virtual_animId_animSpd_area_breachBonus_canCrit_charge_coolDown_critMul_dynamicCharge_earlyCombo_fxId_fxProps_glowColor_hitFrame_lockCtrlAfter_onionSkinFrame_onionSkinOffX_power_props_sfxCharge_sfxHit_sfxProps_sfxRelease_ chainInf,
        WeaponSkill s)
    {
        if (!_disposed && self is RhythmicBlade && chainInf != null && RhythmicBladeKeys.Config.Value.EnableMod)
        {
            try { chainInf.animId = RhythmAnimId.AsHaxeString(); }
            catch (Exception ex) { Log($"[RhythmicBladeOverhaul] 替换 strikeChain 动画失败(idx={idx}): {ex.Message}"); }
        }

        orig(self, idx, chainInf, s);
    }

    /// <summary>标记"这一刀正在结算"——360° 判定只在此时接管。</summary>
    private bool OnRhythmicBladeExecute(Hook_RhythmicBlade.orig_onExecute orig, RhythmicBlade self)
    {
        if (_disposed || self == null) return orig(self!);

        var prev = _executing;
        _executing = self;
        try
        {
            return orig(self);
        }
        finally
        {
            _executing = prev;
        }
    }

    // ================================================================
    // 3) 360° 圆形打击 + 无视墙体
    // ================================================================
    //
    // 原版圆形判定本身是"以主角为圆心、半径 = widPx*0.5 + e.radius"的圆，
    // 但后面还要过一遍 Bresenham 直线墙体检测 + "目标站在实心格上"检测，
    // 所以墙后的敌人打不到、贴墙站的敌人也打不到 —— 特效却能盖过去。
    //
    // 这里对节奏之刃直接接管：只保留
    //   · 武器/主角活着
    //   · 目标活着、可被命中
    //   · 敌对阵营
    //   · 圆心到目标中心距离 ≤ 半径
    // 不做墙体检测、不做朝向判定 —— 圆就该打一圈。

    private bool OnWeaponCanHit(Hook_Weapon.orig_canHit orig, Weapon self, Entity e, Area area)
    {
        if (_disposed || self is not RhythmicBlade)
        {
            return orig(self, e, area);
        }

        var cfg = RhythmicBladeKeys.Config.Value;
        if (!cfg.EnableMod)
        {
            return orig(self, e, area);
        }

        // 只在这一刀正在结算时接管；其它时刻（例如别的系统借用 canHit）交回原版
        if (!ReferenceEquals(_executing, self) || !cfg.EnableWallPierce)
        {
            return orig(self, e, area);
        }

        try
        {
            return HitsWithinCircle((RhythmicBlade)self, e, area);
        }
        catch (Exception ex)
        {
            Log($"[RhythmicBladeOverhaul] 360° 判定异常，回退原版: {ex.Message}");
            return orig(self, e, area);
        }
    }

    private bool HitsWithinCircle(RhythmicBlade self, Entity e, Area? area)
    {
        Hero hero = self.owner;
        if (hero == null || hero.destroyed || hero.life <= 0) return false;
        if (e == null || e.destroyed || e.life <= 0) return false;

        // 目标必须可被命中（原版 canHit 也用这个）
        try { if (!e.canBeHit()) return false; }
        catch { return false; }

        // 敌对阵营（原版靠调用方的 opponentsIterator 保证，这里显式再确认一次）
        try
        {
            if (!hero.isOpponent(e)) return false;
        }
        catch { /* 拿不到阵营信息就放行 */ }

        if (e is Door || e is EtheralDoorSocle) return false;

        double radiusCells = RadiusCells(area);
        double dx = (e.cx + e.xr) - (hero.cx + hero.xr);
        double dy = (e.cy + e.yr) - (hero.cy + hero.yr);
        double reach = radiusCells + e.radius;

        return dx * dx + dy * dy <= reach * reach;
    }

    /// <summary>
    /// 当前判定半径，单位：格。
    /// 原版圆形分支的条件是 distPx² ≤ (widPx*0.5 + e.radius)²，
    /// 这里把 widPx*0.5 从像素换成格。
    /// </summary>
    private static double RadiusCells(Area? area)
    {
        if (area == null) return 0.0;
        return area.widPx * 0.5 / CellSize;
    }

    // ================================================================
    // 4) 击杀叠加范围：放大 getRealCurArea() 返回的判定框（只放一次）
    // ================================================================
    //
    // ⚠ 这里放过之后不要在 canHit 里对同一个 Area 乘第二遍，
    //   否则会变成平方级放大，判定框盖满全屏、穿墙效果被淹没。
    //   canHit 现在只读 widPx 当半径用，不改它。

    private Area? OnRhythmicBladeGetRealCurArea(Hook_RhythmicBlade.orig_getRealCurArea orig, RhythmicBlade self)
    {
        Area? area = orig(self);

        if (_disposed || self == null || area == null) return area;

        var cfg = RhythmicBladeKeys.Config.Value;
        if (!cfg.EnableMod) return area;

        double mul = Multiplier(cfg);
        if (System.Math.Abs(mul - 1.0) > 0.0000001)
        {
            try
            {
                area.widPx *= mul;
                area.heiPx *= mul;
            }
            catch (Exception ex)
            {
                Log($"[RhythmicBladeOverhaul] 放大判定框失败: {ex.Message}");
            }
        }

        return area;
    }

    // ================================================================
    // 5) 伤害跟着范围涨
    // ================================================================
    //
    // RhythmicBlade.hitFromWeapon 内部 new 出 AttackData 后立刻交给
    // _AttackUtils.hit(atk, target)，伤害在 hit → updateDamages 里算完。
    // 所以：
    //   · Hook_RhythmicBlade.hitFromWeapon 只用来标记"这一下是节奏之刃打的"
    //   · Hook__AttackUtils.hit 在伤害计算之前把 dmgMultiplier 乘上范围倍率
    // 这样暴击倍率、词条、伤害上限都还是走原版逻辑，只是基数变大。

    private void OnRhythmicBladeHit(
        Hook_RhythmicBlade.orig_hitFromWeapon orig, RhythmicBlade self, Entity e, Ref<int> _cycle)
    {
        if (_disposed || self == null)
        {
            orig(self!, e, _cycle);
            return;
        }

        var prev = _hitting;
        _hitting = self;
        try
        {
            orig(self, e, _cycle);
        }
        finally
        {
            _hitting = prev;
        }
    }

    private void OnAttackUtilsHit(Hook__AttackUtils.orig_hit orig, AttackData atk, Entity target)
    {
        var cfg = RhythmicBladeKeys.Config.Value;
        if (!_disposed && atk != null && cfg.EnableMod && cfg.ScaleDamage)
        {
            try
            {
                var blade = _hitting ?? (atk.sourceWeapon as RhythmicBlade);
                if (blade != null)
                {
                    double mul = Multiplier(cfg);
                    if (mul > 1.0000001) atk.dmgMultiplier *= mul;
                }
            }
            catch (Exception ex)
            {
                Log($"[RhythmicBladeOverhaul] 伤害加成失败: {ex.Message}");
            }
        }

        orig(atk, target);
    }

    // ================================================================
    // 固定倍率
    // ================================================================

    /// <summary>固定倍率（判定范围 / 伤害 / 特效共用），默认 3.0。</summary>
    private static double Multiplier(RhythmicBladeConfig cfg)
    {
        return cfg.Multiplier > 0.0 ? cfg.Multiplier : 1.0;
    }

    // ================================================================
    // 6) 特效跟着范围涨：冲击波半径 = 当前判定半径
    // ================================================================
    //
    // ================================================================
    // 6) 特效跟着范围涨 —— 用引擎自己的 fxProps.fxScale
    // ================================================================
    //
    // `weapon@strikeChain@fxProps` 在 CDB 里的字段是：
    //     fxStartFrame / fxOffset / fxScale / fxInnerColor / fxOuterColor
    // 其中 **fxScale 就是管特效大小的**（ArrayDyn，[scaleX, scaleY]），
    // `RhythmicBlade.dynOnFxFrame` 里就是这么用的：
    //     if (cinf.fxProps.fxScale != null) {
    //         hSprite.scaleX = cinf.fxProps.fxScale[0];
    //         hSprite.scaleY = cinf.fxProps.fxScale[1];
    //     }
    // 而 RhythmicBlade 三段 strikeChain 的 fxProps 里**只有 fxOffset**，
    // 根本没写 fxScale —— 所以特效一直是最原始的尺寸。
    //
    // 做法：在 orig 之前把 fxScale 设成 [f, f]，orig 里就会照它缩放特效；
    // orig 读的是同一个对象、读取是同步的，所以读完立刻还原，避免污染 CDB 数据。

    private void OnRhythmicBladeFxFrame(
        Hook_RhythmicBlade.orig_dynOnFxFrame orig,
        RhythmicBlade self,
        WeaponSkill s,
        virtual_animId_animSpd_area_breachBonus_canCrit_charge_coolDown_critMul_dynamicCharge_earlyCombo_fxId_fxProps_glowColor_hitFrame_lockCtrlAfter_onionSkinFrame_onionSkinOffX_power_props_sfxCharge_sfxHit_sfxProps_sfxRelease_ cinf)
    {
        if (_disposed || self == null || cinf?.fxProps == null)
        {
            orig(self!, s, cinf!);
            return;
        }

        var cfg = RhythmicBladeKeys.Config.Value;
        if (!cfg.EnableMod || !cfg.ScaleEffect)
        {
            orig(self, s, cinf);
            return;
        }

        var props = cinf.fxProps;
        var saved = props.fxScale;

        try
        {
            double mul = Multiplier(cfg);
            if (mul > 1.0000001)
            {
                double tweak = cfg.EffectScaleTweak > 0.0 ? cfg.EffectScaleTweak : 1.0;
                // 原版没写 fxScale 时按 1.0 起算；写了就按原值乘
                double baseX = 1.0, baseY = 1.0;
                if (saved is ArrayDyn old)
                {
                    try
                    {
                        baseX = System.Convert.ToDouble(old.getDyn(0));
                        baseY = System.Convert.ToDouble(old.getDyn(1));
                    }
                    catch { baseX = 1.0; baseY = 1.0; }
                }

                props.fxScale = MakePair(baseX * mul * tweak, baseY * mul * tweak);
            }
        }
        catch (Exception ex)
        {
            Log($"[RhythmicBladeOverhaul] 设置 fxScale 失败: {ex.Message}");
        }

        try
        {
            orig(self, s, cinf);
        }
        finally
        {
            // fxProps 是 CDB 里的共享对象，必须还原，否则倍率会越乘越大
            try { props.fxScale = saved; } catch { }
        }
    }

    /// <summary>构造一个 [x, y] 的 ArrayDyn（fxProps.fxScale 的类型）。</summary>
    private static ArrayDyn MakePair(double x, double y)
    {
        var arr = ArrayUtils.CreateDyn();
        arr.push(x);
        arr.push(y);
        return arr;
    }

    private void OnDispShockWave(
        Hook_Fx.orig_dispShockWave orig, Fx self, double x, double y, double radius,
        double? alpha, double? newLifeS)
    {
        var cfg = RhythmicBladeKeys.Config.Value;
        if (!_disposed && cfg.EnableMod && cfg.ScaleEffect && _executing != null)
        {
            try
            {
                double mul = Multiplier(cfg);
                if (mul > 1.0000001)
                {
                    // 原版这里传的是固定 0.2，把它按同一倍率放大
                    radius = radius * mul;
                }
            }
            catch { }
        }

        orig(self, x, y, radius, alpha, newLifeS);
    }

    // ================================================================
    // 选项菜单（IModMenu）
    // ================================================================

    public string GetName() => "节奏之刃·节奏打击";

    public void BuildMenu(dc.ui.Options options)
    {
        try
        {
            var b = (dc.ui.OptionsBase)options;
            var cfg = RhythmicBladeKeys.Config;

            ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("RHYTHMIC BLADE 设置"));
            b.createScroller(0.0);

            AddToggle(b, cfg, "启用改造", "必定节奏打击 + 360°圆形打击 + 范围/伤害/特效 ×3",
                () => cfg.Value.EnableMod, v => cfg.Value.EnableMod = v);

            AddToggle(b, cfg, "无视墙体", "只算圆心到目标的距离，不做墙体与朝向判定",
                () => cfg.Value.EnableWallPierce, v => cfg.Value.EnableWallPierce = v);

            AddToggle(b, cfg, "伤害 ×倍率", "最终伤害 × Multiplier",
                () => cfg.Value.ScaleDamage, v => cfg.Value.ScaleDamage = v);

            AddToggle(b, cfg, "特效 ×倍率", "把 fxProps.fxScale 设成 Multiplier，特效与打击范围一致",
                () => cfg.Value.ScaleEffect, v => cfg.Value.ScaleEffect = v);

            AddSlider(b, cfg, "固定倍率", "判定范围 / 伤害 / 特效 共用的倍率，默认 3.0",
                () => cfg.Value.Multiplier, v => cfg.Value.Multiplier = v);

            b.updateScroller();
        }
        catch (Exception ex)
        {
            Log($"[RhythmicBladeOverhaul] 建立选项菜单失败: {ex.Message}");
        }
    }

    /// <summary>滑条（0.5 ~ 6.0，步进 0.1），用来调固定倍率。</summary>
    private static void AddSlider(
        dc.ui.OptionsBase b, Config<RhythmicBladeConfig> cfg, string label, string hint,
        Func<double> get, Action<double> set)
    {
        double value = get();
        double step = 0.1;
        double min = 0.5;
        double max = 6.0;
        bool showPercent = false;
        bool showRawValue = true;
        int paddingLeft = 0;

        double __step = step;
        bool __percent = showPercent;
        bool __raw = showRawValue;
        double __min = min;
        double __max = max;
        int __pad = paddingLeft;

        b.addSliderWidget(
            StringUtils.AsHaxeString(label),
            (HlAction<double>)delegate (double v)
            {
                set(v);
                cfg.Save();
            },
            value,
            ref __step, b.scrollerFlow,
            ref __percent, ref __raw, ref __min, ref __max,
            StringUtils.AsHaxeString(hint),
            ref __pad);
    }

    private static void AddToggle(
        dc.ui.OptionsBase b, Config<RhythmicBladeConfig> cfg, string label, string hint,
        Func<bool> get, Action<bool> set)
    {
        bool current = get();
        b.addToggleWidget(
            StringUtils.AsHaxeString(label),
            StringUtils.AsHaxeString(hint),
            (HlFunc<bool>)delegate
            {
                bool next = !get();
                set(next);
                cfg.Save();
                return next;
            },
            new Ref<bool>(ref current),
            b.scrollerFlow);
    }

    // ================================================================
    // 工具
    // ================================================================

    private void HookSafe(string what, Action act)
    {
        try { act(); }
        catch (Exception ex) { Log($"[RhythmicBladeOverhaul] 挂载失败: {what} —— {ex.Message}"); }
    }

    private void Log(string msg)
    {
        System.Console.WriteLine(msg);
        try { Logger.Information(msg); } catch { }
    }
}
