using dc;
using dc.en;
using dc.tool;
using dc.tool.mainSkills;
using dc.tool.weap;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Storage;
using ModCore.Utilities;

namespace TridentDash;

/// <summary>
/// 深渊三叉戟（Abyssal Trident / Trident）→ 常驻自动冲锋
/// =================================================================
///
/// 【哪一击才是"一直冲刺"】
///   strikeChain（res/data.cdb）三段：
///     cycle 0  AtkTridentB  普通挥击
///     cycle 1  AtkTridentC  ★蓄力冲锋 —— 位移就在这一段
///     cycle 2  AtkTridentD  收尾突刺（只有 0.1 的小 bump）
///
///   cycle 1 的 doChargeAttack() 里逐帧：
///     没刺中：owner.dx = dir * props.speed(=0.5) * 跑速加成   （单位：格/帧）
///     刺中时：owner.dx = 目标速度
///     另外按 props.tick(0.05s) 对身前敌人反复结算伤害（暴击时钉人）
///
/// 【本模组】
///   1) 只打冲锋这一击：Hook_Weapon.get_cycle 对三叉戟恒返回 1
///   2) 攻击一次 = 无限冲刺：Hook_Trident.onExecute 开始冲刺，之后每帧把
///      hero.bdx（bump 速度通道）顶成 dir × props.speed，直到被打断
///   3) 翻滚打断：Hook_DodgeRoll.onStart 里停掉冲刺（并把 bdx 清零）
///   4) 随时转向：hook 掉 allowTurnDuringCharge + 每帧跟随 hero.dir，
///      并把 canTurnNbLeft 顶满（原版这个计数用完就不许转向）
///   5) 伤害仍按原版 props.tick 节奏调 doChargeAttack(1.0) 结算
///
///   位移必须走 bdx 而不是 dx：Hero 的走路代码每帧会用输入重算 dx
///   （Hero.cs: dx = runSpd * accelFactor * dir * frameWalkSpd * …），
///   而 Entity 物理里总移动 = dx + bdx，bdx 只被 bump()/cancelVelocities() 改动。
/// </summary>
public class TridentDashMain : ModBase, IOnGameExit, IModMenu
{
    // ================================================================
    // ★ 常量
    // ================================================================

    /// <summary>"一直冲刺"的那一击在 strikeChain 里的下标（AtkTridentC 蓄力冲锋）。</summary>
    private const int ChargeCycle = 1;

    /// <summary>读不到数据时的兜底：冲刺速度（格/帧）。</summary>
    private const double FallbackDashSpeed = 0.5;

    /// <summary>读不到数据时的兜底：伤害结算间隔（秒）。</summary>
    private const double FallbackTick = 0.05;

    /// <summary>满蓄力比例（原版 &gt;0.52 即判暴击）。</summary>
    private const double FullCharge = 1.0;

    /// <summary>一格多少像素（引擎里到处是 24.0）。</summary>
    private const double CellSize = 24.0;

    /// <summary>转向次数顶到这个值，等于"随便转"。</summary>
    private const int UnlimitedTurns = 9999;

    /// <summary>原版各怪物冲刺拖尾用的默认白（7628951）。</summary>
    private const int DefaultTrailColor = 7628951;

    /// <summary>Hero 的无敌 affect 下标 —— toggleFullInvincibility 用的就是 5。</summary>
    private const int InvincibleAffect = 5;

    // ================================================================
    // 运行时状态
    // ================================================================

    private bool _disposed;

    /// <summary>正在冲锋的武器实例。</summary>
    private Trident? _dashOwner;

    /// <summary>当前冲刺方向（每帧跟随 hero.dir）。</summary>
    private int _dashDir = 1;

    /// <summary>本次冲刺的巡航速度（格/帧）。</summary>
    private double _dashSpeed;

    /// <summary>起步爆发速度（格/帧）。</summary>
    private double _launchSpeed;

    /// <summary>已冲刺的帧数，用于速度曲线与拖尾节流。</summary>
    private int _dashFrames;

    /// <summary>拖尾颜色（取武器 strikeChain 的 glowColor）。</summary>
    private int _trailColor = DefaultTrailColor;

    /// <summary>击杀叠加的冲刺动量（0 起步，逐帧衰减）。</summary>
    private double _momentum;

    /// <summary>当前关卡的 FX 句柄（冲刺期间缓存）。</summary>
    private dc.Fx? _fx;

    /// <summary>冲刺中的英雄（用于还原碰撞模式）。</summary>
    private Hero? _dashHero;

    /// <summary>冲刺前的碰撞模式，冲刺结束时还原。</summary>
    private CollisionMode? _prevCollision;

    /// <summary>有限时长模式下剩余帧数；无限模式忽略。</summary>
    private int _framesLeft;

    /// <summary>当前是否处于冲刺状态。</summary>
    private bool _dashing;

    /// <summary>距离上次伤害结算过了多少秒。</summary>
    private double _tickAccum;

    /// <summary>伤害结算间隔（秒），取自原版 props.tick。</summary>
    private double _tickInterval = FallbackTick;

    /// <summary>启动日志只打一次，避免刷屏。</summary>
    private bool _loggedStart;

    public TridentDashMain(ModInfo info) : base(info) { }

    // ================================================================
    // 生命周期
    // ================================================================

    public override void Initialize()
    {
        base.Initialize();
        _disposed = false;

        HookSafe("Weapon.get_cycle", () => Hook_Weapon.get_cycle += OnWeaponGetCycle);
        HookSafe("Trident.isReady", () => Hook_Trident.isReady += OnTridentIsReady);
        HookSafe("Trident.canStartChargeEarly", () => Hook_Trident.canStartChargeEarly += OnTridentCanStartChargeEarly);
        HookSafe("Trident.allowTurnDuringCharge", () => Hook_Trident.allowTurnDuringCharge += OnTridentAllowTurn);
        HookSafe("Trident.onOwnerTurned", () => Hook_Trident.onOwnerTurned += OnTridentOwnerTurned);
        HookSafe("Trident.onExecute", () => Hook_Trident.onExecute += OnTridentExecute);
        HookSafe("Trident.fixedUpdate", () => Hook_Trident.fixedUpdate += OnTridentFixedUpdate);
        HookSafe("DodgeRoll.onStart", () => Hook_DodgeRoll.onStart += OnDodgeRollStart);
        HookSafe("Entity.onDie", () => Hook_Entity.onDie += OnEntityDie);

        Log("[TridentDash] 已加载：三叉戟攻击一次 = 无限冲刺（可随时转向，翻滚打断）");
    }

    void IOnGameExit.OnGameExit()
    {
        _disposed = true;
        try { Hook_Weapon.get_cycle -= OnWeaponGetCycle; } catch { }
        try { Hook_Trident.isReady -= OnTridentIsReady; } catch { }
        try { Hook_Trident.canStartChargeEarly -= OnTridentCanStartChargeEarly; } catch { }
        try { Hook_Trident.allowTurnDuringCharge -= OnTridentAllowTurn; } catch { }
        try { Hook_Trident.onOwnerTurned -= OnTridentOwnerTurned; } catch { }
        try { Hook_Trident.onExecute -= OnTridentExecute; } catch { }
        try { Hook_Trident.fixedUpdate -= OnTridentFixedUpdate; } catch { }
        try { Hook_DodgeRoll.onStart -= OnDodgeRollStart; } catch { }
        try { Hook_Entity.onDie -= OnEntityDie; } catch { }

        StopDash();
        Log("[TridentDash] 游戏退出，已卸载");
    }

    // ================================================================
    // 0) 翻滚打断
    // ================================================================
    //
    // 翻滚 = dc.tool.mainSkills.DodgeRoll（继承 Dodge → HeroMainSkill）。
    // 它自己重写了 onStart，Hook_DodgeRoll.onStart 能稳定拿到这个时机。

    private void OnDodgeRollStart(Hook_DodgeRoll.orig_onStart orig, DodgeRoll self)
    {
        orig(self);

        if (_disposed) return;

        var cfg = TridentDashKeys.Config.Value;
        if (!cfg.EnableMod || !cfg.InterruptOnRoll) return;
        if (!_dashing) return;

        try
        {
            // 顺手把冲刺速度清掉，免得翻滚被残留的 bdx 顶歪
            var hero = _dashOwner?.owner;
            if (hero != null && !hero.destroyed) hero.bdx = 0.0;
        }
        catch { }

        StopDash();
        Log("[TridentDash] 翻滚 → 冲刺中断");
    }

    // ================================================================
    // 0b) 击杀 = 冲刺动量（越杀越冲，爽点）
    // ================================================================

    private void OnEntityDie(Hook_Entity.orig_onDie orig, Entity self)
    {
        orig(self);

        if (_disposed || !_dashing) return;

        var cfg = TridentDashKeys.Config.Value;
        if (!cfg.EnableMod || cfg.KillSpeedBoost <= 0.0) return;

        try
        {
            if (self is not Mob mob) return;          // 只算怪物
            if (mob.lastHitHero == null) return;      // 必须是玩家这边打死的

            _momentum = System.Math.Min(cfg.KillBoostMax, _momentum + cfg.KillSpeedBoost);
            SpawnBurstAt((mob.cx + mob.xr) * CellSize, (mob.cy + mob.yr) * CellSize, 0.18, 0.7, 0.10);
        }
        catch { }
    }

    // ================================================================
    // 1) 只保留冲锋那一击
    // ================================================================

    private int OnWeaponGetCycle(Hook_Weapon.orig_get_cycle orig, Weapon self)
    {
        if (!_disposed && self is Trident && TryGetConfig(out var cfg) && cfg.OnlyChargeAttack)
        {
            return ChargeCycle;
        }
        return orig(self);
    }

    private static bool TryGetConfig(out TridentDashConfig cfg)
    {
        cfg = TridentDashKeys.Config.Value;
        return cfg.EnableMod;
    }

    // ================================================================
    // 2) 常驻为被动：随时可用 + 随时转向
    // ================================================================

    private bool OnTridentIsReady(Hook_Trident.orig_isReady orig, Trident self)
    {
        if (!_disposed && TryGetConfig(out var cfg) && cfg.AlwaysReady) return true;
        return orig(self);
    }

    private bool OnTridentCanStartChargeEarly(Hook_Trident.orig_canStartChargeEarly orig, Trident self)
    {
        if (!_disposed && TryGetConfig(out var cfg) && cfg.AlwaysReady) return true;
        return orig(self);
    }

    /// <summary>
    /// 原版：cycle 1 只在"蓄力中"(CD 1411383296 存在) 或 canTurnNbLeft &gt; 0 时才允许转向。
    /// 这里直接放行，实现"随时转向"。
    /// </summary>
    private bool OnTridentAllowTurn(Hook_Trident.orig_allowTurnDuringCharge orig, Trident self)
    {
        if (!_disposed && TryGetConfig(out var cfg) && cfg.TurnAnyTime) return true;
        return orig(self);
    }

    /// <summary>原版转向会消耗 canTurnNbLeft，这里在每次转向后把它顶满，等于无限次转向。</summary>
    private void OnTridentOwnerTurned(Hook_Trident.orig_onOwnerTurned orig, Trident self)
    {
        orig(self);

        if (_disposed) return;
        var cfg = TridentDashKeys.Config.Value;
        if (!cfg.EnableMod || !cfg.TurnAnyTime) return;

        try { self.canTurnNbLeft = UnlimitedTurns; } catch { }
    }

    // ================================================================
    // 3) 出手也照样进入冲刺（没有自动冲刺时作为手动触发）
    // ================================================================

    private bool OnTridentExecute(Hook_Trident.orig_onExecute orig, Trident self)
    {
        if (_disposed || self == null) return orig(self!);

        bool result = orig(self);

        var cfg = TridentDashKeys.Config.Value;
        if (!cfg.EnableMod) return result;

        try
        {
            if (!_dashing) BeginDash(self, cfg, resetState: true);
        }
        catch (Exception ex)
        {
            Log($"[TridentDash] 启动冲刺失败: {ex.Message}");
        }

        return result;
    }

    // ================================================================
    // 4) 每帧：自动冲刺 + 跟随转向 + 按 tick 结算伤害
    // ================================================================

    private void OnTridentFixedUpdate(Hook_Trident.orig_fixedUpdate orig, Trident self)
    {
        orig(self);

        if (_disposed || self == null) return;

        var cfg = TridentDashKeys.Config.Value;
        if (!cfg.EnableMod)
        {
            StopDash();
            return;
        }

        try
        {
            // 没装备在手上就不接管（避免背包里的副武器也把人推着走）
            if (!IsEquipped(self))
            {
                StopDash();
                return;
            }

            Hero hero = self.owner;
            if (hero == null || hero.destroyed || hero.life <= 0)
            {
                StopDash();
                return;
            }

            // 过场动画期间不推人（引擎的 hasButtonPressedForWeapon 也是这么判断的）
            if (cfg.PauseInCinematic && IsCinematic()) return;

            // 随时转向：把转向次数顶满，方向每帧跟随当前朝向
            if (cfg.TurnAnyTime)
            {
                try { self.canTurnNbLeft = UnlimitedTurns; } catch { }
                _dashDir = hero.dir >= 0 ? 1 : -1;
            }

            // 只在冲刺状态下推进；冲刺由"攻击一次"开启
            if (!_dashing) return;
            if (!ReferenceEquals(_dashOwner, self)) return;

            // (a) 位移：写 bump 速度通道 bdx。
            //
            //     不能写 hero.dx —— 引擎每帧会用输入重算它
            //     （Hero.cs 走路那段：dx = runSpd * accelFactor * dir * frameWalkSpd ...），
            //     只有武器动作锁住控制时（原版蓄力）dx 才留得住。
            //
            //     Entity 的物理里总移动 = dx + bdx，而 bdx 只被 bump()/cancelVelocities()
            //     改动，输入完全不管它，所以每帧写 bdx 就能得到持续位移。
            _dashFrames++;
            double speed = CurrentSpeed(cfg, _dashFrames) * (1.0 + _momentum);
            hero.bdx = _dashDir * speed;

            // 击杀动量逐帧衰减，冲得越久越稳
            if (_momentum > 0.0)
            {
                _momentum *= cfg.KillBoostDecay;
                if (_momentum < 0.002) _momentum = 0.0;
            }

            // (a2) 视觉：引擎自带的冲刺拖尾（原版怪物冲刺用的就是它）
            if (cfg.UseTrail && cfg.TrailInterval > 0 && (_dashFrames % cfg.TrailInterval) == 0)
            {
                SpawnTrail(hero);
            }

            // (a3) 护盾：每帧刷新无敌 affect（toggleFullInvincibility 用的也是 5 号）
            if (cfg.ShieldWhileDashing)
            {
                RefreshShield(hero, cfg);
            }

            // (b) 伤害：按原版 props.tick 节奏结算（范围 / 暴击 / 钉人 / 推人）
            if (cfg.AutoDamage)
            {
                _tickAccum += FrameSeconds(hero);
                if (_tickAccum >= _tickInterval)
                {
                    _tickAccum -= _tickInterval;
                    self.doChargeAttack(FullCharge);
                }
            }

            // (c) 时长：无限模式永不结束
            if (!cfg.InfiniteDash)
            {
                _framesLeft--;
                if (_framesLeft <= 0) StopDash();
            }
        }
        catch (Exception ex)
        {
            Log($"[TridentDash] 冲刺推进失败: {ex.Message}");
            StopDash();
        }
    }

    private void BeginDash(Trident self, TridentDashConfig cfg, bool resetState)
    {
        Hero hero = self.owner;
        if (hero == null || hero.destroyed || hero.life <= 0) return;

        if (!cfg.DashInAir)
        {
            bool airborne;
            try { airborne = hero.get_pf() == null; } catch { airborne = false; }
            if (airborne) return;
        }

        _dashOwner = self;
        _dashHero = hero;
        _dashDir = hero.dir >= 0 ? 1 : -1;
        try { _fx = hero._level?.fx; } catch { _fx = null; }

        // 无视墙体：把碰撞模式切成 IgnoreWalls（引擎枚举里的第 7 项）
        if (cfg.IgnoreWalls)
        {
            try
            {
                _prevCollision = hero.collisionMode;
                hero.collisionMode = new CollisionMode.IgnoreWalls();
            }
            catch (Exception ex)
            {
                Log($"[TridentDash] 切换 IgnoreWalls 失败: {ex.Message}");
            }
        }
        double mul = cfg.DashSpeedMul > 0.0 ? cfg.DashSpeedMul : 1.0;
        _dashSpeed = BaseDashSpeed(self) * mul;
        _launchSpeed = _dashSpeed * (cfg.LaunchMul > 1.0 ? cfg.LaunchMul : 1.0);
        _trailColor = GetGlowColor(self);
        _dashFrames = 0;
        _tickInterval = BaseTick(self);
        _framesLeft = System.Math.Max(1, cfg.DashFrames);
        _dashing = true;

        if (resetState)
        {
            _tickAccum = _tickInterval;   // 进入冲刺立刻结算一次
            try
            {
                self.isDoingCharge = false;
                self.hasBegunCharge = false;
            }
            catch { }
        }

        // 起步冲击波：给"发射感"
        if (cfg.StartBurst) SpawnBurst(hero);

        if (!_loggedStart)
        {
            _loggedStart = true;
            Log($"[TridentDash] 冲刺已启动：speed={_dashSpeed:F3} 格/帧, tick={_tickInterval:F3}s, " +
                $"infinite={cfg.InfiniteDash}, turn={cfg.TurnAnyTime}, 翻滚打断={cfg.InterruptOnRoll}");
        }
    }

    private void StopDash()
    {
        _dashing = false;
        _dashOwner = null;
        _framesLeft = 0;
        _tickAccum = 0.0;
        _dashFrames = 0;
        _momentum = 0.0;
        _fx = null;
        RestoreCollision();
    }

    /// <summary>还原冲刺前的碰撞模式（否则会一直穿墙）。</summary>
    private void RestoreCollision()
    {
        try
        {
            if (_dashHero != null && !_dashHero.destroyed && _prevCollision is not null)
            {
                _dashHero.collisionMode = _prevCollision;
            }
        }
        catch { }

        _prevCollision = null;
        _dashHero = null;
    }

    /// <summary>刷新无敌护盾（affect 5），持续到冲刺结束后自然过期。</summary>
    private void RefreshShield(Hero hero, TridentDashConfig cfg)
    {
        try
        {
            double v = 1.0;
            hero.setAffectS(InvincibleAffect, cfg.ShieldSeconds, ref v, null);
        }
        catch { }
    }

    // ================================================================
    // 手感：起步爆发 → 缓出到巡航速度
    // ================================================================

    private double CurrentSpeed(TridentDashConfig cfg, int frames)
    {
        if (cfg.LaunchFrames <= 0 || cfg.LaunchMul <= 1.0) return _dashSpeed;

        double t = frames / (double)cfg.LaunchFrames;
        if (t >= 1.0) return _dashSpeed;

        // 缓出曲线：前几帧掉得快、后面平缓，读起来像"弹射出去"
        double e = 1.0 - System.Math.Pow(1.0 - t, 2.0);
        return _launchSpeed + (_dashSpeed - _launchSpeed) * e;
    }

    // ================================================================
    // 视觉：拖尾 + 起步冲击波
    // ================================================================

    /// <summary>引擎自带的冲刺拖尾（原版怪物冲刺用的同一个），颜色用武器 glowColor。</summary>
    private void SpawnTrail(Hero hero)
    {
        try
        {
            if (_fx == null) return;
            int dxOff = 0;
            double scaleMul = 1.0;
            _fx.dash(hero, _dashDir, _trailColor, ref dxOff, ref scaleMul);
        }
        catch { }
    }

    /// <summary>起步冲击波，给一个"发射"的顿感。</summary>
    private void SpawnBurst(Hero hero)
    {
        SpawnBurstAt((hero.cx + hero.xr) * CellSize, (hero.cy + hero.yr) * CellSize, 0.12, 0.55, 0.12);
    }

    private void SpawnBurstAt(double x, double y, double radius, double alpha, double lifeS)
    {
        try { _fx?.dispShockWave(x, y, radius, alpha, lifeS); } catch { }
    }

    /// <summary>拖尾颜色：三叉戟 strikeChain[1] 的 glowColor（青色系），读不到用原版白。</summary>
    private int GetGlowColor(Trident self)
    {
        try
        {
            var inf = GetChargeInf(self);
            if (inf?.glowColor != null) return (int)inf.glowColor;
        }
        catch { }
        return DefaultTrailColor;
    }

    /// <summary>这把武器是否握在手上（mainWeapons 里）。</summary>
    private static bool IsEquipped(Trident self)
    {
        try
        {
            var wm = self.owner?.weaponsManager;
            if (wm?.mainWeapons == null) return false;

            int n = wm.mainWeapons.length;
            for (int i = 0; i < n; i++)
            {
                if (ReferenceEquals(wm.mainWeapons.getDyn(i), self)) return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsCinematic()
    {
        try { return dc.pr.Game.Class.ME != null && dc.pr.Game.Class.ME.hasCinematic(); }
        catch { return false; }
    }

    /// <summary>一帧多少秒。</summary>
    private static double FrameSeconds(Hero hero)
    {
        try
        {
            double fps = hero.cd.baseFps;
            if (fps > 1.0) return 1.0 / fps;
        }
        catch { }
        return 1.0 / 60.0;
    }

    // ================================================================
    // 读原版数据（自校准，不写死数值）
    // ================================================================

    private static
        virtual_animId_animSpd_area_breachBonus_canCrit_charge_coolDown_critMul_dynamicCharge_earlyCombo_fxId_fxProps_glowColor_hitFrame_lockCtrlAfter_onionSkinFrame_onionSkinOffX_power_props_sfxCharge_sfxHit_sfxProps_sfxRelease_?
        GetChargeInf(Trident self)
    {
        try
        {
            var chain = self.wInfos?.strikeChain;
            if (chain == null || chain.length <= ChargeCycle) return null;
            return chain.getDyn(ChargeCycle)
                as virtual_animId_animSpd_area_breachBonus_canCrit_charge_coolDown_critMul_dynamicCharge_earlyCombo_fxId_fxProps_glowColor_hitFrame_lockCtrlAfter_onionSkinFrame_onionSkinOffX_power_props_sfxCharge_sfxHit_sfxProps_sfxRelease_;
        }
        catch { return null; }
    }

    /// <summary>冲刺速度：原版 cycle 1 的 props.speed（默认 0.5 格/帧）。</summary>
    private double BaseDashSpeed(Trident self)
    {
        try
        {
            var inf = GetChargeInf(self);
            if (inf?.props?.speed != null)
            {
                double s = (double)inf.props.speed;
                if (s > 0.0) return s;
            }
        }
        catch (Exception ex)
        {
            Log($"[TridentDash] 读取冲刺速度失败，用兜底值: {ex.Message}");
        }
        return FallbackDashSpeed;
    }

    /// <summary>伤害结算间隔：原版 cycle 1 的 props.tick（默认 0.05 秒）。</summary>
    private double BaseTick(Trident self)
    {
        try
        {
            var inf = GetChargeInf(self);
            if (inf?.props?.tick != null)
            {
                double t = (double)inf.props.tick;
                if (t > 0.001) return t;
            }
        }
        catch { }
        return FallbackTick;
    }

    // ================================================================
    // 选项菜单（IModMenu）
    // ================================================================

    public string GetName() => "深渊三叉戟·常驻冲锋";

    public void BuildMenu(dc.ui.Options options)
    {
        try
        {
            var b = (dc.ui.OptionsBase)options;
            var cfg = TridentDashKeys.Config;

            ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("TRIDENT DASH 设置"));
            b.createScroller(0.0);

            AddToggle(b, cfg, "启用改造", "三叉戟：攻击一次 = 无限冲刺",
                () => cfg.Value.EnableMod, v => cfg.Value.EnableMod = v);

            AddToggle(b, cfg, "只打冲锋", "关掉 = 恢复原版三段连招",
                () => cfg.Value.OnlyChargeAttack, v => cfg.Value.OnlyChargeAttack = v);

            AddToggle(b, cfg, "无限冲刺", "攻击一次之后一直冲，不会因时长结束",
                () => cfg.Value.InfiniteDash, v => cfg.Value.InfiniteDash = v);

            AddToggle(b, cfg, "翻滚打断", "翻滚时中断冲刺",
                () => cfg.Value.InterruptOnRoll, v => cfg.Value.InterruptOnRoll = v);

            AddToggle(b, cfg, "随时转向", "冲锋中可随时改变方向",
                () => cfg.Value.TurnAnyTime, v => cfg.Value.TurnAnyTime = v);

            AddToggle(b, cfg, "冲刺造成伤害", "冲刺期间按原版 tick 结算伤害",
                () => cfg.Value.AutoDamage, v => cfg.Value.AutoDamage = v);

            AddToggle(b, cfg, "空中也能冲", "允许空中保持冲刺",
                () => cfg.Value.DashInAir, v => cfg.Value.DashInAir = v);

            AddToggle(b, cfg, "过场暂停", "过场动画期间不推进冲刺",
                () => cfg.Value.PauseInCinematic, v => cfg.Value.PauseInCinematic = v);

            AddToggle(b, cfg, "冲刺拖尾", "引擎自带的冲刺残影特效",
                () => cfg.Value.UseTrail, v => cfg.Value.UseTrail = v);

            AddToggle(b, cfg, "起步冲击波", "出发瞬间的爆冲特效",
                () => cfg.Value.StartBurst, v => cfg.Value.StartBurst = v);

            AddToggle(b, cfg, "冲刺无视墙体", "冲刺期间直接穿过墙（结束后还原）",
                () => cfg.Value.IgnoreWalls, v => cfg.Value.IgnoreWalls = v);

            AddToggle(b, cfg, "冲刺获得护盾", "冲刺期间无敌",
                () => cfg.Value.ShieldWhileDashing, v => cfg.Value.ShieldWhileDashing = v);

            AddSlider(b, cfg, "冲刺速度倍率", "基准 = 原版冲锋速度 props.speed（0.5 格/帧）",
                () => cfg.Value.DashSpeedMul, v => cfg.Value.DashSpeedMul = v, 0.2, 4.0, 0.05);

            AddSlider(b, cfg, "起步爆发倍率", "刚出发那几帧的额外提速，调大手感更冲",
                () => cfg.Value.LaunchMul, v => cfg.Value.LaunchMul = v, 1.0, 5.0, 0.1);

            AddSlider(b, cfg, "拖尾间隔帧", "越小拖尾越密（越吃粒子）",
                () => cfg.Value.TrailInterval, v => cfg.Value.TrailInterval = (int)System.Math.Round(v), 1.0, 12.0, 1.0);

            AddSlider(b, cfg, "击杀加速", "每杀一个怪增加的冲刺速度（0 = 关闭）",
                () => cfg.Value.KillSpeedBoost, v => cfg.Value.KillSpeedBoost = v, 0.0, 0.5, 0.01);

            AddSlider(b, cfg, "击杀加速上限", "最多比巡航速度快多少",
                () => cfg.Value.KillBoostMax, v => cfg.Value.KillBoostMax = v, 0.0, 2.0, 0.05);

            AddSlider(b, cfg, "冲刺时长帧数", "仅「无限冲刺」关闭时生效",
                () => cfg.Value.DashFrames, v => cfg.Value.DashFrames = (int)System.Math.Round(v), 1.0, 120.0, 1.0);

            b.updateScroller();
        }
        catch (Exception ex)
        {
            Log($"[TridentDash] 建立选项菜单失败: {ex.Message}");
        }
    }

    private static void AddToggle(
        dc.ui.OptionsBase b, Config<TridentDashConfig> cfg, string label, string hint,
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

    private static void AddSlider(
        dc.ui.OptionsBase b, Config<TridentDashConfig> cfg, string label, string hint,
        Func<double> get, Action<double> set, double min, double max, double step)
    {
        double value = get();
        double __step = step;
        bool __percent = false;
        bool __raw = true;
        double __min = min;
        double __max = max;
        int __pad = 0;

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

    // ================================================================
    // 工具
    // ================================================================

    private void HookSafe(string what, Action act)
    {
        try { act(); }
        catch (Exception ex) { Log($"[TridentDash] 挂载失败: {what} —— {ex.Message}"); }
    }

    private void Log(string msg)
    {
        System.Console.WriteLine(msg);
        try { Logger.Information(msg); } catch { }
    }
}
