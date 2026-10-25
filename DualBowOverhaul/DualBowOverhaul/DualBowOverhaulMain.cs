using dc;
using dc.en;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using dc.tool.weap.bow;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Storage;
using ModCore.Utilities;

namespace DualBowOverhaul;

/// <summary>
/// 多槽弓（DualBow）强化
/// =================================================================
///
/// 【原版机制】（GamePseudocode/dc.tool.weap.bow/DualBow.cs + res/data.cdb）
///   · CDB item DualBow：props.count = 3，commonProps.ammo = 21，
///     tags 里带 **NoCritical**（所以这把弓天生不能暴击）
///   · DualBow.shoot() 里：
///       int num2 = item._itemData.props.count;
///       if (item.hasAffix("DoubleBullets")) num2 *= 2;      // 词条翻倍
///       AttackData atk = createFromHeroWeapon(this, null);
///       atk.overrideBaseDamage(power / num2);              // 总伤害被摊到每支箭
///       for (i in 0..num2) { consumeAmmo(); new Bullet(...); bullet.shootFromWeapon(...); }
///   · Bullet 自带 **ignoreWalls** 字段（fixedUpdate 里 `if (!ignoreWalls)` 才查墙），
///     所以"无视墙体"不用改碰撞，把字段设成 true 就行。
///
/// 【本模组】
///   1) 每次 10 箭  —— Hook_DualBow.shoot：射之前把 item 的 props.count 临时改成
///      目标值（带 DoubleBullets 时减半，保证最终恰好 10），射完还原。
///   2) 可以暴击    —— Hook__AttackUtils.hit：命中时给这个 AttackData 打上暴击 tag(2)
///      并把 critMul 设成配置值（同一发箭共享一份 AttackData，只设一次，不会叠乘）。
///   3) 无视墙体    —— Hook_Bullet.fixedUpdate：把 bullet.ignoreWalls = true。
///   4) 追踪敌人    —— 同一个钩子里，找最近的活着的敌对目标，把箭的速度向量
///      (dx, dy) 改指向它，速度保持 props 里的 ~1.0 格/帧。
///   5) 无限弹药    —— Hook_Weapon.consumeAmmo：多槽弓不消耗弹药（可关）。
/// </summary>
public class DualBowOverhaulMain : ModBase, IOnGameExit, IModMenu
{
    // ================================================================
    // ★ 常量
    // ================================================================

    /// <summary>AttackData 里"暴击"的 tag 下标（Trident.hitFromWeapon 用的也是 2）。</summary>
    private const int CritTag = 2;

    /// <summary>原版多槽弓的 item id。</summary>
    private const string DualBowId = "DualBow";

    /// <summary>会翻倍箭数的词条。</summary>
    private const string DoubleBulletsAffix = "DoubleBullets";

    /// <summary>一格多少像素（引擎里到处是 24.0）。</summary>
    private const double CellSize = 24.0;

    // ================================================================
    // 运行时状态
    // ================================================================

    private bool _disposed;

    /// <summary>避免"数据还没加载"的日志刷屏。</summary>
    private bool _loggedNoItemData;

    /// <summary>启动日志只打一次。</summary>
    private bool _loggedReady;

    public DualBowOverhaulMain(ModInfo info) : base(info) { }

    // ================================================================
    // 生命周期
    // ================================================================

    public override void Initialize()
    {
        base.Initialize();
        _disposed = false;

        HookSafe("DualBow.shoot", () => Hook_DualBow.shoot += OnDualBowShoot);
        HookSafe("Weapon.consumeAmmo", () => Hook_Weapon.consumeAmmo += OnWeaponConsumeAmmo);
        HookSafe("Weapon.get_attackSpeed", () => Hook_Weapon.get_attackSpeed += OnWeaponGetAttackSpeed);
        HookSafe("Bullet.fixedUpdate", () => Hook_Bullet.fixedUpdate += OnBulletFixedUpdate);
        HookSafe("_AttackUtils.hit", () => Hook__AttackUtils.hit += OnAttackUtilsHit);

        Log("[DualBowOverhaul] 已加载：多槽弓 = 每次 10 箭 / 可暴击 / 无视墙体 / 追踪敌人 / 攻速 ×3");
    }

    void IOnGameExit.OnGameExit()
    {
        _disposed = true;
        try { Hook_DualBow.shoot -= OnDualBowShoot; } catch { }
        try { Hook_Weapon.consumeAmmo -= OnWeaponConsumeAmmo; } catch { }
        try { Hook_Weapon.get_attackSpeed -= OnWeaponGetAttackSpeed; } catch { }
        try { Hook_Bullet.fixedUpdate -= OnBulletFixedUpdate; } catch { }
        try { Hook__AttackUtils.hit -= OnAttackUtilsHit; } catch { }

        Log("[DualBowOverhaul] 游戏退出，已卸载");
    }

    // ================================================================
    // 0) 攻速 ×N
    // ================================================================
    //
    // 引擎里攻速的唯一乘数就是 Weapon.get_attackSpeed()：
    //   WeaponSkill.getCastSpeed() = base.getCastSpeed() * weapon.get_attackSpeed()
    // 所以在这里放大，蓄力与动画会一起变快。

    private double OnWeaponGetAttackSpeed(Hook_Weapon.orig_get_attackSpeed orig, Weapon self)
    {
        double baseSpeed = orig(self);

        if (_disposed || self is not DualBow) return baseSpeed;

        var cfg = DualBowOverhaulKeys.Config.Value;
        if (!cfg.EnableMod) return baseSpeed;

        double mul = cfg.AttackSpeedMul > 0.0 ? cfg.AttackSpeedMul : 1.0;
        return baseSpeed * mul;
    }

    // ================================================================
    // 1) 每次射击 N 箭
    // ================================================================

    private void OnDualBowShoot(Hook_DualBow.orig_shoot orig, DualBow self, dc.hl.types.ArrayObj bulletsOut)
    {
        if (_disposed || self == null)
        {
            orig(self!, bulletsOut);
            return;
        }

        var cfg = DualBowOverhaulKeys.Config.Value;
        if (!cfg.EnableMod || cfg.ArrowCount <= 0)
        {
            orig(self, bulletsOut);
            return;
        }

        int? previous = null;
        bool patched = false;

        try
        {
            var data = self.item?._itemData;
            if (data?.props != null)
            {
                // 带 DoubleBullets 词条时原版会 ×2，所以基础值减半，保证最终恰好是 ArrowCount
                bool doubled = HasDoubleBullets(self);
                int target = doubled
                    ? (int)System.Math.Ceiling(cfg.ArrowCount / 2.0)
                    : cfg.ArrowCount;

                previous = data.props.count;
                data.props.count = target;
                patched = true;

                if (!_loggedReady)
                {
                    _loggedReady = true;
                    Log($"[DualBowOverhaul] 箭数：{cfg.ArrowCount}（基础值 {target}，DoubleBullets={doubled}）");
                }
            }
            else if (!_loggedNoItemData)
            {
                _loggedNoItemData = true;
                Log("[DualBowOverhaul] item 数据尚未加载，这一发先按原版；之后会正常生效");
            }
        }
        catch (Exception ex)
        {
            Log($"[DualBowOverhaul] 修改箭数失败: {ex.Message}");
        }

        try
        {
            orig(self, bulletsOut);
        }
        finally
        {
            if (patched)
            {
                try
                {
                    var data = self.item?._itemData;
                    if (data?.props != null) data.props.count = previous;
                }
                catch { }
            }
        }
    }

    private static bool HasDoubleBullets(DualBow self)
    {
        try { return self.item != null && self.item.hasAffix(StringUtils.AsHaxeString(DoubleBulletsAffix)); }
        catch { return false; }
    }

    // ================================================================
    // 2) 无限弹药
    // ================================================================

    private bool OnWeaponConsumeAmmo(Hook_Weapon.orig_consumeAmmo orig, Weapon self)
    {
        if (!_disposed && self is DualBow && DualBowOverhaulKeys.Config.Value.EnableMod
            && DualBowOverhaulKeys.Config.Value.InfiniteAmmo)
        {
            return true;   // 不扣弹药，射击照常进行
        }
        return orig(self);
    }

    // ================================================================
    // 3) + 4) 箭矢：无视墙体 + 追踪敌人
    // ================================================================

    private void OnBulletFixedUpdate(Hook_Bullet.orig_fixedUpdate orig, Bullet self)
    {
        if (_disposed || self == null)
        {
            orig(self!);
            return;
        }

        var cfg = DualBowOverhaulKeys.Config.Value;

        if (cfg.EnableMod && IsDualBowBullet(self))
        {
            // 必须在 orig 之前：orig 里第一件事就是 `if (!ignoreWalls) { 查墙 }`
            if (cfg.IgnoreWalls)
            {
                try { self.ignoreWalls = true; } catch { }
            }

            if (cfg.Homing)
            {
                try { Steer(self, cfg); } catch { }
            }
        }

        orig(self);
    }

    /// <summary>这支箭是不是多槽弓射出来的。</summary>
    private static bool IsDualBowBullet(Bullet b)
    {
        try { return b.atk?.sourceWeapon is DualBow; }
        catch { return false; }
    }

    /// <summary>把箭的速度向量指向最近的敌对目标。</summary>
    private void Steer(Bullet b, DualBowOverhaulConfig cfg)
    {
        Team? team = b._team;
        if (team == null) return;

        double bx = b.cx + b.xr;
        double by = b.cy + b.yr;

        Entity? best = null;
        double bestDist2 = double.MaxValue;

        TeamIterator it = team.opponentsIterator.reset(team);
        int guard = 0;
        while (it.hasNext() && guard++ < 512)
        {
            Entity e = it.next();
            if (e == null || e.destroyed || e.life <= 0) continue;

            try { if (!e.canBeHit()) continue; } catch { continue; }

            double dx = CenterX(e, cfg) - bx;
            double dy = CenterY(e, cfg) - by;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                best = e;
            }
        }

        if (best == null) return;

        double ang = System.Math.Atan2(
            CenterY(best, cfg) - by,
            CenterX(best, cfg) - bx);

        double spd = cfg.HomingSpeed > 0.0 ? cfg.HomingSpeed : 1.0;
        b.dx = System.Math.Cos(ang) * spd;
        b.dy = System.Math.Sin(ang) * spd;
    }

    // ================================================================
    // 目标点：敌人"身体中心"，不是脚底
    // ================================================================
    //
    // Entity 的 (cx+xr, cy+yr) 是**脚底中心**：
    //   原版 Fx.dash() 里取中心用的就是 `(cy+yr)*24 - hei*0.5`
    //   （Trident.doChargeAttack 的命中判定同款写法）。
    // 所以 X 已经是中心，Y 要减去半个身高。

    /// <summary>Entity 的横向锚点本身就是中心，直接用 (cx + xr)。</summary>
    private static double CenterX(Entity e, DualBowOverhaulConfig cfg)
    {
        return e.cx + e.xr;
    }

    private static double CenterY(Entity e, DualBowOverhaulConfig cfg)
    {
        try
        {
            double y = e.cy + e.yr;
            if (cfg.AimAtCenter)
            {
                double h = e.hei;
                if (h > 0.0) y -= h / (2.0 * CellSize);
            }
            return y;
        }
        catch { return e.cy + e.yr; }
    }

    // ================================================================
    // 5) 可以暴击
    // ================================================================
    //
    // 原版这把弓带 NoCritical 标签，攻击数据里没有 critMul。
    // 同一发箭的所有箭头共享一份 AttackData，所以这里只设一次
    //（hasTag 判断），不会因为多次命中而把倍率乘爆。

    private void OnAttackUtilsHit(Hook__AttackUtils.orig_hit orig, AttackData atk, Entity target)
    {
        var cfg = DualBowOverhaulKeys.Config.Value;

        if (!_disposed && atk != null && cfg.EnableMod && cfg.CanCrit)
        {
            try
            {
                if (atk.sourceWeapon is DualBow && !atk.hasTag(CritTag))
                {
                    atk.setTag(CritTag, true);
                    double mul = cfg.CritMul > 1.0 ? cfg.CritMul : 1.0;
                    if (atk.critMul < mul) atk.critMul = mul;
                }
            }
            catch (Exception ex)
            {
                Log($"[DualBowOverhaul] 设置暴击失败: {ex.Message}");
            }
        }

        orig(atk, target);
    }

    // ================================================================
    // 选项菜单（IModMenu）
    // ================================================================

    public string GetName() => "多槽弓·十箭追踪";

    public void BuildMenu(dc.ui.Options options)
    {
        try
        {
            var b = (dc.ui.OptionsBase)options;
            var cfg = DualBowOverhaulKeys.Config;

            ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("DUALBOW 设置"));
            b.createScroller(0.0);

            AddToggle(b, cfg, "启用改造", "每次 10 箭 + 暴击 + 穿墙 + 追踪",
                () => cfg.Value.EnableMod, v => cfg.Value.EnableMod = v);

            AddToggle(b, cfg, "无限弹药", "不消耗箭矢",
                () => cfg.Value.InfiniteAmmo, v => cfg.Value.InfiniteAmmo = v);

            AddToggle(b, cfg, "可以暴击", "去掉原版 NoCritical 的限制",
                () => cfg.Value.CanCrit, v => cfg.Value.CanCrit = v);

            AddToggle(b, cfg, "无视墙体", "箭矢穿过墙壁",
                () => cfg.Value.IgnoreWalls, v => cfg.Value.IgnoreWalls = v);

            AddToggle(b, cfg, "追踪敌人", "箭矢自动转向最近的敌人",
                () => cfg.Value.Homing, v => cfg.Value.Homing = v);

            AddToggle(b, cfg, "瞄准身体中心", "关掉 = 瞄准脚底（原版锚点）",
                () => cfg.Value.AimAtCenter, v => cfg.Value.AimAtCenter = v);

            AddSlider(b, cfg, "每次箭数", "带 DoubleBullets 词条时会自动折半保证总数",
                () => cfg.Value.ArrowCount, v => cfg.Value.ArrowCount = (int)System.Math.Round(v), 1.0, 30.0, 1.0);

            AddSlider(b, cfg, "暴击倍率", "原版 1.0 = 不额外加成",
                () => cfg.Value.CritMul, v => cfg.Value.CritMul = v, 1.0, 4.0, 0.05);

            AddSlider(b, cfg, "追踪速度", "格/帧，原版箭速约 1.0",
                () => cfg.Value.HomingSpeed, v => cfg.Value.HomingSpeed = v, 0.3, 3.0, 0.05);

            AddSlider(b, cfg, "攻速倍率", "1.0 = 原版；3.0 = 攻速翻三倍",
                () => cfg.Value.AttackSpeedMul, v => cfg.Value.AttackSpeedMul = v, 0.5, 6.0, 0.25);

            b.updateScroller();
        }
        catch (Exception ex)
        {
            Log($"[DualBowOverhaul] 建立选项菜单失败: {ex.Message}");
        }
    }

    private static void AddToggle(
        dc.ui.OptionsBase b, Config<DualBowOverhaulConfig> cfg, string label, string hint,
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
        dc.ui.OptionsBase b, Config<DualBowOverhaulConfig> cfg, string label, string hint,
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
        catch (Exception ex) { Log($"[DualBowOverhaul] 挂载失败: {what} —— {ex.Message}"); }
    }

    private void Log(string msg)
    {
        System.Console.WriteLine(msg);
        try { Logger.Information(msg); } catch { }
    }
}
