using dc;
using dc.en;
using dc.hl.types;
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
using System.Runtime.CompilerServices;

namespace InfiniteBowOverhaul;

/// <summary>
/// 无尽弓（Arc et carquois sans fond / InfiniteBow）→ 环绕箭
/// =================================================================
///
/// 【原版机制】（GamePseudocode/dc.tool.weap.bow/InfiniteBow.cs + res/data.cdb）
///   · item InfiniteBow：group 5，commonProps 里没有 ammo（天生无限弹药），
///     gameplayDesc = "Munitions illimitées. Le dernier tir inflige un coup critique."
///   · strikeChain 三段：canCrit 分别是 false / false / **true**
///     → 只有第三段（最后一射）能暴击。
///   · shoot()：先 base.shoot(bulletsOut)（BaseBow 那条通用造箭流程），
///     再自己造一支特殊箭，maxDist = 11 * 24 = 264px，命中后消失。
///   · Bullet 自带 ignoreWalls 字段（fixedUpdate 里 `if (!ignoreWalls)` 才查墙）。
///
/// 【本模组】
///   1) 箭矢绕英雄转圈 —— Hook_Bullet.fixedUpdate 里接管位置：
///        角度 = 每支箭自己的初相 + 角速度 × 时间（都由 uid 打散，看起来随机）
///        半径 = 固定（默认 10 格 = 原版 DamageAura 的 distance 5 格 × 2）
///        位置 = 英雄中心 + (cos,sin) × 半径，每帧跟随英雄
///      速度清零、射程拉满 → 不打到敌人就一直绕着，打中就按原版消失。
///   2) 攻速 ×3       —— Hook_Weapon.get_attackSpeed
///   3) 任意箭可暴击  —— Hook__AttackUtils.hit 打暴击 tag，并设 critMul
///   4) 无视墙体      —— bullet.ignoreWalls = true
///
///   只对 `bullet.atk.sourceWeapon is InfiniteBow` 的箭生效，别的弓不受影响。
/// </summary>
public class InfiniteBowOverhaulMain : ModBase, IOnGameExit, IModMenu
{
    // ================================================================
    // ★ 常量
    // ================================================================

    /// <summary>AttackData 里"暴击"的 tag 下标（= Trident.hitFromWeapon 用的那个）。</summary>
    private const int CritTag = 2;

    /// <summary>一格多少像素。</summary>
    private const double CellSize = 24.0;

    /// <summary>把 limit 设成这个"射程"，等于永不过期。</summary>
    private const double HugeMaxDist = 1_000_000.0;

    // ================================================================
    // 运行时状态
    // ================================================================

    private bool _disposed;

    /// <summary>正在环绕的箭（用于上限控制 + 清理）。</summary>
    private readonly List<Bullet> _orbiting = new();

    /// <summary>正在环绕的箭的 hash 集合（O(1) 判定）。</summary>
    private readonly HashSet<int> _orbitingIds = new();

    /// <summary>上次清理死亡箭矢的帧号。</summary>
    private int _tick;
    private int _lastPruneTick;

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public InfiniteBowOverhaulMain(ModInfo info) : base(info) { }

    // ================================================================
    // 生命周期
    // ================================================================

    public override void Initialize()
    {
        base.Initialize();
        _disposed = false;

        HookSafe("InfiniteBow.shoot", () => Hook_InfiniteBow.shoot += OnInfiniteBowShoot);
        HookSafe("Bullet.fixedUpdate", () => Hook_Bullet.fixedUpdate += OnBulletFixedUpdate);
        HookSafe("Weapon.get_attackSpeed", () => Hook_Weapon.get_attackSpeed += OnWeaponGetAttackSpeed);
        HookSafe("_AttackUtils.hit", () => Hook__AttackUtils.hit += OnAttackUtilsHit);

        Log("[InfiniteBowOverhaul] 已加载：无尽弓 = 每次 10 箭，各自环绕 + 攻速 ×3 + 任意箭暴击 + 无视墙体");
    }

    void IOnGameExit.OnGameExit()
    {
        _disposed = true;
        try { Hook_InfiniteBow.shoot -= OnInfiniteBowShoot; } catch { }
        try { Hook_Bullet.fixedUpdate -= OnBulletFixedUpdate; } catch { }
        try { Hook_Weapon.get_attackSpeed -= OnWeaponGetAttackSpeed; } catch { }
        try { Hook__AttackUtils.hit -= OnAttackUtilsHit; } catch { }

        _orbiting.Clear();
        _orbitingIds.Clear();
        Log("[InfiniteBowOverhaul] 游戏退出，已卸载");
    }

    // ================================================================
    // 0) 一次射 10 支箭
    // ================================================================
    //
    // InfiniteBow.shoot() 里只造了 1 支箭（BaseBow.shoot 只处理可破坏物和弹药，
    // 并不造箭），所以剩下的由我们按同样的方式补齐：
    //   createFromHeroWeapon → new Bullet → init → enableHeroFalling → shootFromWeapon
    // 每支用自己的 AttackData，互不干扰；朝向只是初始值，下一帧就会被环绕逻辑接管。

    private void OnInfiniteBowShoot(Hook_InfiniteBow.orig_shoot orig, InfiniteBow self, ArrayObj bulletsOut)
    {
        var cfg = InfiniteBowOverhaulKeys.Config.Value;

        int want = 1;
        if (!_disposed && self != null && cfg.EnableMod)
        {
            want = System.Math.Max(1, cfg.ArrowCount);
        }

        // 直接连调 N 次原版 shoot：
        // 每次都会完整走一遍「造箭 → init → enableHeroFalling → shootFromWeapon → push(bulletsOut)」，
        // 所以箭是真的被关卡收下的。手工 new Bullet 在代理层造出来的箭不会进入关卡实体表，
        // 表现就是"射了但只有一支"。
        for (int i = 0; i < want; i++)
        {
            try
            {
                orig(self!, bulletsOut);
            }
            catch (Exception ex)
            {
                Log($"[InfiniteBowOverhaul] 第 {i + 1}/{want} 次造箭失败: {ex.Message}");
                break;
            }
        }
    }

    // ================================================================
    // 1) 箭矢环绕
    // ================================================================

    private void OnBulletFixedUpdate(Hook_Bullet.orig_fixedUpdate orig, Bullet self)
    {
        if (_disposed || self == null)
        {
            orig(self!);
            return;
        }

        var cfg = InfiniteBowOverhaulKeys.Config.Value;
        bool ours = cfg.EnableMod && IsInfiniteBowBullet(self);

        if (ours)
        {
            if (++_tick % 30 == 0) PruneOrbiting();

            // 必须在 orig 之前：orig 第一件事就是 `if (!ignoreWalls) { 查墙 }`
            if (cfg.IgnoreWalls)
            {
                try { self.ignoreWalls = true; } catch { }
            }

            // 过场动画里 _GameCinematic 会遍历所有子弹，对 destroyOnCinematic 为真的直接 destroy()。
            // 环绕箭要跨过场存活，所以三件事一起做：
            //   destroyOnCinematic = false（不被清场）
            //   hasGravity = false / endGravity = 0（就算某几帧没人接管位置也不会掉下去）
            if (cfg.SurviveCinematic)
            {
                try { if (self.destroyOnCinematic) self.destroyOnCinematic = false; } catch { }
                try { if (self.hasGravity) self.hasGravity = false; } catch { }
                try { if (self.endGravity != 0.0) self.endGravity = 0.0; } catch { }
            }
        }

        orig(self);

        if (!ours) return;

        try { UpdateOrbit(self, cfg); } catch { }
    }

    private static bool IsInfiniteBowBullet(Bullet b)
    {
        try { return b.atk?.sourceWeapon is InfiniteBow; }
        catch { return false; }
    }

    private void UpdateOrbit(Bullet b, InfiniteBowOverhaulConfig cfg)
    {
        if (b.destroyed) return;

        Hero? hero = null;
        try { hero = b.atk?.source as Hero ?? FindHeroFor(b); } catch { }
        if (hero == null || hero.destroyed || hero.life <= 0) return;

        // 数量上限：超出的箭不接管（按原版飞出去、自然消失）
        int id = RuntimeHelpers.GetHashCode(b);
        if (!_orbitingIds.Contains(id))
        {
            if (_orbiting.Count >= cfg.MaxOrbitingBullets) return;
            _orbiting.Add(b);
            _orbitingIds.Add(id);
        }

        // 不打到敌人就一直留着
        if (cfg.KeepAlive)
        {
            try { if (b.maxDist < HugeMaxDist) b.maxDist = HugeMaxDist; } catch { }
        }

        // ---- 每支箭用自己的 uid 打散出一条"自己的轨道" ----
        //
        // 要的不是"所有箭都贴在最外圈匀速转"，而是"在范围内无规则运动 / 像电子云"。
        // 做法：主轨道半径每支箭不同（30%~100% 最大半径），再叠一个不同频率的小圆，
        //      两个频率不成整数比 → 轨迹不闭合，看起来一直在范围里乱窜；
        //      最后把位移向量夹到最大半径内，保证不越界。
        uint seed = unchecked((uint)b.__uid);
        double u1 = Mix(seed + 0x9E3779B9u) / 4294967296.0;
        double u2 = Mix(seed + 0x7F4A7C15u) / 4294967296.0;
        double u3 = Mix(seed + 0x94D049BBu) / 4294967296.0;
        double u4 = Mix(seed + 0xD6E8FEB8u) / 4294967296.0;
        double u5 = Mix(seed + 0x2545F491u) / 4294967296.0;

        double rMax = (cfg.OrbitRadiusCells > 0.0 ? cfg.OrbitRadiusCells : 10.0) * CellSize;

        double inner = cfg.InnerRadiusFrac;
        if (inner < 0.05) inner = 0.05;
        if (inner > 0.95) inner = 0.95;
        double rBase = rMax * (inner + (1.0 - inner) * u1);      // 每支箭自己的轨道半径

        double lo = cfg.SpinMin > 0.0 ? cfg.SpinMin : 0.5;
        double hi = cfg.SpinMax > lo ? cfg.SpinMax : lo + 0.5;
        double spin = lo + u2 * (hi - lo);
        if ((Mix(seed + 0x94D049BBu) & 1u) != 0u) spin = -spin;   // 一半正转一半反转

        double baseAng = u3 * System.Math.PI * 2.0;

        // 二级小圆：频率取主轨道的非整数倍 → 轨道不闭合，像电子云
        double wobble = rMax * (cfg.WobbleFrac > 0.0 ? cfg.WobbleFrac : 0.0) * (0.6 + 0.4 * u4);
        double spin2 = spin * (2.0 + 1.5 * u5);
        double phase2 = u4 * System.Math.PI * 2.0;

        double now = _clock.Elapsed.TotalSeconds;
        double a1 = baseAng + spin * now;
        double a2 = phase2 + spin2 * now;

        double ox = rBase * System.Math.Cos(a1) + wobble * System.Math.Cos(a2);
        double oy = rBase * System.Math.Sin(a1) + wobble * System.Math.Sin(a2);

        // 夹到最大半径内（保证"范围固定"）
        double len = System.Math.Sqrt(ox * ox + oy * oy);
        if (len > rMax && len > 1e-6)
        {
            double k = rMax / len;
            ox *= k;
            oy *= k;
        }

        double hx = (hero.cx + hero.xr) * CellSize;
        double hy = (hero.cy + hero.yr) * CellSize - hero.hei * 0.5;

        double px = hx + ox;
        double py = hy + oy;

        // 速度清零：位置完全由我们决定，否则原版积分会把它又推走
        try
        {
            b.dx = 0.0;
            b.dy = 0.0;
            b.bdx = 0.0;
            b.bdy = 0.0;
        }
        catch { }

        b.setPosPixel(px, py);
    }

    /// <summary>32 位散列（把一个种子的低位熵摊开，避免相邻 uid 转出相邻角度）。</summary>
    private static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }

    /// <summary>箭矢的 atk.source 有时不是英雄，兜底从关卡里拿英雄。</summary>
    private static Hero? FindHeroFor(Bullet b)
    {
        try { return b._level?.game?.hero; } catch { return null; }
    }

    /// <summary>定期把已经销毁的箭从登记表里剔掉。</summary>
    private void PruneOrbiting()
    {
        if (_tick - _lastPruneTick < 30) return;
        _lastPruneTick = _tick;

        for (int i = _orbiting.Count - 1; i >= 0; i--)
        {
            Bullet b = _orbiting[i];
            bool dead = b == null || b.destroyed;
            if (!dead) continue;

            _orbitingIds.Remove(RuntimeHelpers.GetHashCode(b));
            _orbiting.RemoveAt(i);
        }
    }

    // ================================================================
    // 2) 攻速 ×3
    // ================================================================

    private double OnWeaponGetAttackSpeed(Hook_Weapon.orig_get_attackSpeed orig, Weapon self)
    {
        double baseSpeed = orig(self);

        if (_disposed || self is not InfiniteBow) return baseSpeed;

        var cfg = InfiniteBowOverhaulKeys.Config.Value;
        if (!cfg.EnableMod) return baseSpeed;

        double mul = cfg.AttackSpeedMul > 0.0 ? cfg.AttackSpeedMul : 1.0;
        return baseSpeed * mul;
    }

    // ================================================================
    // 3) 任意箭矢可暴击
    // ================================================================
    //
    // 原版只有 cycle 2 的最后一射 canCrit=true。
    // 所有箭共享同一份 AttackData，所以用 hasTag(2) 做一次性判断，避免倍率叠乘。

    private void OnAttackUtilsHit(Hook__AttackUtils.orig_hit orig, AttackData atk, Entity target)
    {
        var cfg = InfiniteBowOverhaulKeys.Config.Value;

        if (!_disposed && atk != null && cfg.EnableMod && cfg.CanCrit)
        {
            try
            {
                if (atk.sourceWeapon is InfiniteBow && !atk.hasTag(CritTag))
                {
                    atk.setTag(CritTag, true);
                    double mul = cfg.CritMul > 1.0 ? cfg.CritMul : 1.0;
                    if (atk.critMul < mul) atk.critMul = mul;
                }
            }
            catch { }
        }

        orig(atk, target);
    }

    // ================================================================
    // 选项菜单（IModMenu）
    // ================================================================

    public string GetName() => "无尽弓·环绕箭";

    public void BuildMenu(dc.ui.Options options)
    {
        try
        {
            var b = (dc.ui.OptionsBase)options;
            var cfg = InfiniteBowOverhaulKeys.Config;

            ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("INFINITE BOW 设置"));
            b.createScroller(0.0);

            AddToggle(b, cfg, "启用改造", "箭矢环绕 + 攻速×3 + 暴击 + 穿墙",
                () => cfg.Value.EnableMod, v => cfg.Value.EnableMod = v);

            AddToggle(b, cfg, "箭矢环绕", "打不到敌人就一直绕着英雄乱窜",
                () => cfg.Value.KeepAlive, v => cfg.Value.KeepAlive = v);

            AddSlider(b, cfg, "每次箭数", "原版只射 1 支，其余由模组补齐（各自独立轨迹）",
                () => cfg.Value.ArrowCount, v => cfg.Value.ArrowCount = (int)System.Math.Round(v), 1.0, 30.0, 1.0);

            AddToggle(b, cfg, "无视墙体", "箭矢穿过墙壁",
                () => cfg.Value.IgnoreWalls, v => cfg.Value.IgnoreWalls = v);

            AddToggle(b, cfg, "跨过场保留", "过场动画里箭矢不被清掉、不掉落",
                () => cfg.Value.SurviveCinematic, v => cfg.Value.SurviveCinematic = v);

            AddToggle(b, cfg, "可以暴击", "所有箭矢都能暴击",
                () => cfg.Value.CanCrit, v => cfg.Value.CanCrit = v);

            AddSlider(b, cfg, "最大半径（格）", "默认 10 格 = 原版 DamageAura 范围(5) 的两倍",
                () => cfg.Value.OrbitRadiusCells, v => cfg.Value.OrbitRadiusCells = v, 2.0, 20.0, 0.5);

            AddSlider(b, cfg, "内侧占比", "0.3 = 各箭轨道半径在 30%~100% 之间随机（不会全挤在外圈）",
                () => cfg.Value.InnerRadiusFrac, v => cfg.Value.InnerRadiusFrac = v, 0.0, 0.9, 0.05);

            AddSlider(b, cfg, "乱窜幅度", "0 = 纯圆轨道；0.18 左右像电子云",
                () => cfg.Value.WobbleFrac, v => cfg.Value.WobbleFrac = v, 0.0, 0.8, 0.02);

            AddSlider(b, cfg, "环绕速度占比", "上限角速度（弧度/秒）",
                () => cfg.Value.SpinMax, v => cfg.Value.SpinMax = v, 0.5, 8.0, 0.1);

            AddSlider(b, cfg, "攻速倍率", "1.0 = 原版",
                () => cfg.Value.AttackSpeedMul, v => cfg.Value.AttackSpeedMul = v, 1.0, 6.0, 0.25);

            AddSlider(b, cfg, "暴击倍率", "1.0 = 不加伤",
                () => cfg.Value.CritMul, v => cfg.Value.CritMul = v, 1.0, 4.0, 0.05);

            AddSlider(b, cfg, "环绕箭上限", "太多会掉帧",
                () => cfg.Value.MaxOrbitingBullets, v => cfg.Value.MaxOrbitingBullets = (int)System.Math.Round(v), 10.0, 300.0, 5.0);

            b.updateScroller();
        }
        catch (Exception ex)
        {
            Log($"[InfiniteBowOverhaul] 建立选项菜单失败: {ex.Message}");
        }
    }

    private static void AddToggle(
        dc.ui.OptionsBase b, Config<InfiniteBowOverhaulConfig> cfg, string label, string hint,
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
        dc.ui.OptionsBase b, Config<InfiniteBowOverhaulConfig> cfg, string label, string hint,
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
        catch (Exception ex) { Log($"[InfiniteBowOverhaul] 挂载失败: {what} —— {ex.Message}"); }
    }

    private void Log(string msg)
    {
        System.Console.WriteLine(msg);
        try { Logger.Information(msg); } catch { }
    }
}
