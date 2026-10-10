#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using dc;
using dc.en;
using dc.hl.types;
using dc.tool.weap.bow;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Storage;
using ModCore.Utilities;

namespace HeavyBowOverhaul
{
    /// <summary>
    /// HeavyBowOverhaul —— 镀金和弓（HeavyBow / Gilded Yumi）强化
    /// ==========================================================
    /// 三件事：
    ///   ① 射出的箭**模型放大 20 倍**（运行时改 `Bullet.spr.scaleX/scaleY`）
    ///   ② 箭的**伤害 ×5**（数据补丁：`weapon/HeavyBow` 的 `strikeChain[*].power`）
    ///   ③ 箭**无视墙体**（运行时把 `Bullet.ignoreWalls = true`）
    ///
    /// ## 原版这支箭是怎么造出来的
    ///
    /// `dc.tool.weap.bow.HeavyBow.shoot(bulletsOut)`：
    ///
    ///     AttackData a = _AttackUtils.createFromHeroWeapon(this, null);   // baseDmg = curSkillInf.power
    ///     Bullet bullet = new Bullet(hero, a, ang, speed, "smallArrow");
    ///     bullet.init();                          // → Entity.init() → initGfx()
    ///     bullet.shootFromWeapon(this, ...);
    ///     bullet.maxDist = itemInf.props.range * 24;
    ///     ...
    ///     spr.groupName = "heavyArrow";           // 换成大箭的贴图
    ///     spr.scaleX *= 0.5;  spr.scaleY *= 0.5;  // 原版自己又缩了一半
    ///
    /// 所以：
    ///   · **伤害**在 `power` 里 → 走 res.pak 数据补丁（顺带物品卡的每秒伤害也跟着 ×5）；
    ///   · **模型大小**只能在运行时改 sprite，而且要等 `shoot()` 整个跑完
    ///     （原版那句 `*= 0.5` 在后面），所以 hook 是"包住 orig，返回后再改"；
    ///   · **无视墙体**就是原版 `Bullet.ignoreWalls`（`dc.en.Bullet.fixedUpdate` 里
    ///     `if (!ignoreWalls) { ...地图碰撞... }` 一整个跳过），
    ///     潜行者的 Homing / Javelin / Saw 这些本来就是这么做的。
    ///
    /// ## 怎么拿到那支箭
    ///
    /// `HeavyBow.shoot` 不返回 bullet，`bulletsOut` 在单发时还是 null。
    /// 但 `bullet.init()` 一定会走 `Entity.init() -> initGfx()`，
    /// 所以在 `HeavyBow.shoot` 期间挂一个标记，让 `Hook_Bullet.initGfx` 把 bullet 记下来。
    /// </summary>
    public class HeavyBowOverhaulMain : ModBase, IOnAfterLoadingAssets, IOnGameExit
    {
        /// <summary>配置（coremod/config/HeavyBowOverhaul.json）。</summary>
        public static Config<HeavyBowOverhaulConfig> Config { get; } = new("HeavyBowOverhaul");

        /// <summary>是否正在 `HeavyBow.shoot` 里（用来认出这支箭）。</summary>
        private bool _inHeavyBowShoot;

        /// <summary>本次 `shoot()` 里造出来的箭。</summary>
        private readonly List<dc.en.Bullet> _captured = new();

        /// <summary>已经被强化过的箭（每帧给它们补命中扫描）。</summary>
        private readonly List<dc.en.Bullet> _arrows = new();

        public HeavyBowOverhaulMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();

            // 包住射箭：进去前开标记，出来后再改那支箭
            try { Hook_HeavyBow.shoot += OnHeavyBowShoot; }
            catch (Exception ex) { Logger.Error(ex, "[HeavyBowOverhaul] Hook_HeavyBow.shoot 挂载失败"); }

            // 造箭时把 bullet 记下来（shoot 不返回它）
            try { Hook_Bullet.initGfx += OnBulletInitGfx; }
            catch (Exception ex) { Logger.Error(ex, "[HeavyBowOverhaul] Hook_Bullet.initGfx 挂载失败"); }

            // 每帧给强化过的箭补一次"按半径"的命中扫描
            try { Hook_Bullet.fixedUpdate += OnBulletFixedUpdate; }
            catch (Exception ex) { Logger.Error(ex, "[HeavyBowOverhaul] Hook_Bullet.fixedUpdate 挂载失败"); }

            Logger.Information("[HeavyBowOverhaul] 已加载：箭模型 x20 / 判定 x20 / 射程 x3 / 伤害 x5（数据补丁）/ 无视墙体");
        }

        // ---------------------------------------------------------------- Hook

        private void OnHeavyBowShoot(Hook_HeavyBow.orig_shoot orig,
                                     HeavyBow self, ArrayObj bulletsOut)
        {
            _captured.Clear();
            _inHeavyBowShoot = true;
            try { orig(self, bulletsOut); }
            finally { _inHeavyBowShoot = false; }

            foreach (var bullet in _captured)
            {
                try { ApplyOverhaul(bullet); }
                catch (Exception ex) { Logger.Error(ex, "[HeavyBowOverhaul] 强化箭失败"); }
            }
            _captured.Clear();
        }

        private void OnBulletInitGfx(Hook_Bullet.orig_initGfx orig, dc.en.Bullet self)
        {
            orig(self);
            if (_inHeavyBowShoot && self != null) _captured.Add(self);
        }

        /// <summary>
        /// Hook Bullet.fixedUpdate —— 给强化过的箭补一次"按半径"的命中扫描。
        /// 放在 orig 之后：这时这一帧的移动已经做完，扫描用的就是箭当前的（也是画面上看到的）位置。
        /// </summary>
        private void OnBulletFixedUpdate(Hook_Bullet.orig_fixedUpdate orig, dc.en.Bullet self)
        {
            orig(self);

            if (_arrows.Count == 0) return;
            if (self == null || self.destroyed) { PruneArrows(); return; }
            if (!ContainsArrow(self)) return;

            try { SweepHits(self); }
            catch { }
        }

        /// <summary>
        /// 对刚射出的那支箭施加改造。
        /// 注意这是在 `shoot()` 跑完之后调的，所以是在原版 `spr.scale *= 0.5` 的基础上再乘。
        /// </summary>
        private void ApplyOverhaul(dc.en.Bullet bullet)
        {
            if (bullet == null || bullet.destroyed) return;

            var cfg = Config.Value;
            if (cfg == null || !cfg.EnableMod) return;

            // ① 模型放大
            //
            // ⚠️ 这里有个坑：只改 `spr.scaleX/scaleY` **只会生效一帧**。
            //    `Entity.postUpdate()`（GamePseudocode/dc/Entity.cs:13391）每帧都会重算：
            //
            //        hSprite.scaleX = sprScaleX * dir;
            //        hSprite.scaleY = sprScaleY;
            //
            //    也就是说 sprite 上的 scale 只是"结果"，真正说了算的是 **sprScaleX / sprScaleY**。
            //    只改 spr.scaleX 的表现就是：出膛那一瞬间是大的，下一帧就被刷回原版大小。
            //    所以要改的是那两个"母"字段，顺手把 sprite 上的值也同步一次（让当帧就是大的）。
            double scale = cfg.ArrowScale > 0 ? cfg.ArrowScale : 1.0;
            if (scale != 1.0)
            {
                bullet.sprScaleX *= scale;
                bullet.sprScaleY *= scale;
                SyncSpriteScale(bullet);
            }

            // ② 撞击识别放大
            //
            // 原版子弹的命中判定是"箭所在的那**一格**里的敌人"：
            //   Bullet.onStep()（GamePseudocode/dc.en/Bullet.cs:3419）
            //     entity = level.listCurrentQuadElements[i]
            //     if (entity.cx == bullet.cx && entity.cy == bullet.cy) onTouchValidTarget(entity);
            // 模型放大 20 倍后，这个"一格"判定就显得完全对不上了。
            // 处理：把实体自身的 radius 也放大（其它系统读到的半径一起变大），
            // 并在每帧的 fixedUpdate 里补一次**按半径**的命中扫描（见 SweepHits）。
            double hitMult = cfg.HitRadiusMult > 0 ? cfg.HitRadiusMult : 1.0;
            if (hitMult > 1.0) bullet.radius *= hitMult;

            // ③ 飞行距离：原版 maxDist = itemInf.props.range(25) * 24 = 600px
            double distMult = cfg.FlightDistanceMult > 0 ? cfg.FlightDistanceMult : 1.0;
            if (distMult != 1.0) bullet.maxDist *= distMult;

            // ④ 无视墙体（原版 Bullet.ignoreWalls）
            if (cfg.IgnoreWalls)
            {
                bullet.ignoreWalls = true;
                bullet.ignoreOneWays = true;
            }

            if (!ContainsArrow(bullet)) _arrows.Add(bullet);

            Logger.Information(
                $"[HeavyBowOverhaul] 箭已强化：sprScaleX/Y={bullet.sprScaleX:0.##}/{bullet.sprScaleY:0.##}" +
                $"（sprite={bullet.spr?.scaleX:0.##}）、判定半径={bullet.radius:0.##}、" +
                $"maxDist={bullet.maxDist:0.##}、ignoreWalls={bullet.ignoreWalls}");
        }

        /// <summary>按原版的算法把 sprScaleX/Y 同步到 sprite 上（scaleX 要乘朝向）。</summary>
        private static void SyncSpriteScale(dc.en.Bullet bullet)
        {
            var spr = bullet.spr;
            if (spr == null) return;
            int dir = bullet.dir;
            spr.scaleX = bullet.sprScaleX * (double)dir;
            spr.scaleY = bullet.sprScaleY;
        }

        // ---------------------------------------------------------------- 放大的命中扫描

        /// <summary>
        /// 每帧补一次"按半径"的命中扫描，补上原版那种"只认一格"的判定。
        ///
        /// 用 <c>Bullet.onTouch(e)</c> 作为入口（不是直接 onTouchValidTarget），
        /// 因为它本身就是原版的完整命中管线：per-entity 冷却 → canHit 回调 →
        /// ignoreTrashMobs → 命中。所以重复调用同一个敌人不会重复结算。
        /// </summary>
        private void SweepHits(dc.en.Bullet bullet)
        {
            var cfg = Config.Value;
            if (cfg == null || !cfg.EnableMod) return;
            if (cfg.HitRadiusMult <= 1.0) return;

            double r = bullet.radius;
            if (r <= 0.0) return;

            var team = bullet._team;
            if (team == null || bullet._level == null) return;

            double bx = ((double)bullet.cx + bullet.xr) * 24.0;
            double by = ((double)bullet.cy + bullet.yr) * 24.0 - bullet.hei * 0.5;

            // 先收集再结算：一边遍历 team 一边让游戏改它不安全
            var targets = new List<Entity>();
            var it = team.opponentsIterator.reset(team);
            if (it == null) return;
            while (it.hasNext())
            {
                var e = it.next();
                if (e == null || e.destroyed || e.life <= 0) continue;
                if (e._level == null || e._level != bullet._level) continue;
                targets.Add(e);
            }

            for (int i = 0; i < targets.Count; i++)
            {
                var e = targets[i];
                double ex = ((double)e.cx + e.xr) * 24.0;
                double ey = ((double)e.cy + e.yr) * 24.0 - e.hei * 0.5;
                double dx = ex - bx;
                double dy = ey - by;
                double rr = r + e.radius;
                if (dx * dx + dy * dy <= rr * rr) bullet.onTouch(e);
            }
        }

        private bool ContainsArrow(dc.en.Bullet bullet)
        {
            for (int i = 0; i < _arrows.Count; i++)
                if (ReferenceEquals(_arrows[i], bullet)) return true;
            return false;
        }

        /// <summary>丢掉已经消失的箭，避免列表无限增长。</summary>
        private void PruneArrows()
        {
            for (int i = _arrows.Count - 1; i >= 0; i--)
            {
                var b = _arrows[i];
                if (b == null || b.destroyed) _arrows.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- 生命周期

        /// <summary>
        /// 资源加载完成后手动挂载本模组自带的 res.pak。
        /// ⚠️ 不挂的话 DCCM 的 CDBManager 读不到 data.cdb_ 补丁，伤害就不会 ×5。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string pakPath = null;
                try { pakPath = Info?.ModRoot?.GetFilePath("res.pak"); } catch { }
                if (string.IsNullOrEmpty(pakPath))
                {
                    string dir = Path.GetDirectoryName(typeof(HeavyBowOverhaulMain).Assembly.Location) ?? "";
                    pakPath = Path.Combine(dir, "res.pak");
                }

                if (File.Exists(pakPath))
                {
                    var fs = FsPak.Instance?.FileSystem;
                    if (fs == null) { Logger.Warning("[HeavyBowOverhaul] FsPak 还没就绪，稍后重试"); return; }
                    fs.loadPak(StringUtils.AsHaxeString(pakPath));
                    Logger.Information($"[HeavyBowOverhaul] res.pak 已挂载，伤害补丁生效: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[HeavyBowOverhaul] 未找到 res.pak: {pakPath}（伤害不会 ×5）");
                }

                var c = Config.Value;
                Logger.Information($"[HeavyBowOverhaul] 配置：总开关={c.EnableMod} 模型倍数={c.ArrowScale} " +
                                   $"判定倍数={c.HitRadiusMult} 射程倍数={c.FlightDistanceMult} 无视墙体={c.IgnoreWalls}");
                try { Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[HeavyBowOverhaul] res.pak 加载失败（伤害不会 ×5）");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_HeavyBow.shoot -= OnHeavyBowShoot; } catch { }
            try { Hook_Bullet.initGfx -= OnBulletInitGfx; } catch { }
            try { Hook_Bullet.fixedUpdate -= OnBulletFixedUpdate; } catch { }
            _captured.Clear();
            _arrows.Clear();
            Logger.Information("[HeavyBowOverhaul] 游戏退出，模组已卸载");
        }
    }

    /// <summary>模组配置（落到 coremod/config/HeavyBowOverhaul.json，可手改）。</summary>
    public class HeavyBowOverhaulConfig
    {
        /// <summary>总开关。</summary>
        public bool EnableMod = true;

        /// <summary>箭模型放大倍数（原版 1.0）。</summary>
        public double ArrowScale = 20.0;

        /// <summary>
        /// 撞击识别半径倍数。原版子弹的判定是"箭所在的那**一格**里的敌人"
        /// （见 <c>Bullet.onStep</c>：遍历 <c>level.listCurrentQuadElements</c> 后
        /// 要求 <c>entity.cx == bullet.cx &amp;&amp; entity.cy == bullet.cy</c>），
        /// 所以默认把这个半径也乘 20，跟模型对齐。&lt;= 1 就是不放大（纯原版判定）。
        /// </summary>
        public double HitRadiusMult = 20.0;

        /// <summary>飞行距离倍数（原版 maxDist = item.props.range 25 格 × 24 = 600px）。</summary>
        public double FlightDistanceMult = 3.0;

        /// <summary>箭是否无视墙体。</summary>
        public bool IgnoreWalls = true;
    }
}
