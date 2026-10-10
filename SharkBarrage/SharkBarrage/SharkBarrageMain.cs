#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using dc.en;
using dc.hl.types;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;

// dc 命名空间里也有 Math / Path / File 这些同名类 → 显式别名到 BCL，避免二义性
using Math = System.Math;
using Path = System.IO.Path;
using File = System.IO.File;

namespace SharkBarrage
{
    /// <summary>
    /// SharkBarrage —— 把 Shark（Maw of the Deep / 深渊之口）改成"每次攻击都泼一屏鲨鱼"
    /// ==================================================================================
    /// 需求：
    ///   · 删掉前两下平a —— 每次按攻击都直接丢鲨鱼
    ///   · 普通丢 **20** 只、传奇词条丢 **40** 只（原版 1 / 3）
    ///   · 飞行距离：普通 **×2**、传奇 **×5**（原版 maxDist = 11 格 × 24 = 264px）
    ///   · 全部**无视墙体**
    ///
    /// ## 原版是怎么走的
    ///
    /// `dc.tool.weap.Shark.onExecute()`（`GamePseudocode/dc.tool.weap/Shark.cs:53`）：
    ///
    ///     bool flag = base.onExecute();          // Weapon.onExecute()：词缀 / onion skin / onWeaponExecute
    ///     viewport.bumpDir(dir, 0.5);
    ///     int c = get_cycle();
    ///     if (c == 1)      { ...震屏 + breakBreakableGround + groundStones... }
    ///     else if (c >= 2) { owner.spr.get_anim().playCustomSequence("AtkSharkC_NOSHARK", 16, 32, null);
    ///                        throwShark();  return true; }
    ///     // c == 0 落下来走普通的"遍历敌方队伍 canHit → hitFromWeapon"挥砍
    ///
    /// `throwShark()`（同文件 412）：
    ///
    ///     double aim = getTargetAng(11 * 0.4, 0.2, null);      // 瞄准最近的敌人
    ///     AttackData atk = _AttackUtils.createFromHeroWeapon(this, null);
    ///     int n = 1;
    ///     if (item.hasAffix("TripleBullets")) n += 2;          // ← 传奇 = 3
    ///     double step = owner.dir != 1 ? 0.15 : -0.15;
    ///     for (int i = 0; i < n; i++)
    ///     {
    ///         if (!consumeAmmo()) break;                       // Shark 的 commonProps 是 {} → 没有 ammo 字段
    ///                                                          // → Weapon.consumeAmmo() 第 896~907 行直接 return true
    ///         var shark = new dc.en.bu.Shark(hero, atk, aim + i * step, 1.1 + rnd * 0.1);
    ///         shark.init();
    ///         shark.tail = new BulletTail.Line(glowColor, 0.7, null);
    ///         shark.shootFromWeapon(this, ..., ..., ...);
    ///         fx.shoot(get_shootX(), get_shootY(), aim, 15297046);
    ///     }
    ///
    /// 飞行距离与判定半径来自基类构造 `dc.en._Bullet.__inst_construct__`：
    ///
    ///     arg1.maxDist = 11.0 * 24.0;                        // = 264px（11 格）
    ///     arg1.radius  = 24.0 * (isHero ? 0.66 : 0.4);       // 英雄子弹 ≈ 15.84px
    ///     arg1.ignoreWalls = false;                          // ← 默认撞墙
    ///
    /// Shark 自己**完全不碰** maxDist / ignoreWalls，所以这两个值就是我们能直接改的开关。
    ///
    /// ## 本模组怎么改
    ///
    /// 1. **删掉前两下** —— Hook `Weapon.get_cycle`，对 Shark 恒返回 2。
    ///    这样 `Shark.onExecute()` 里 `if (c == 1)` / `c == 0` 的普通挥砍分支永远不会走，
    ///    每次攻击都直接落到 `c >= 2` 那条 → 播 AtkSharkC_NOSHARK + `throwShark()`。
    ///    （用改 cycle 而不是"连按两次空挥"，是因为 cycle 还决定 `get_curSkillInf()`
    ///      取哪一段 strikeChain —— 恒定 2 正好让蓄力/伤害/动画全用第三下那套。）
    ///
    /// 2. **20 / 40 只 + 距离 ×2 / ×5 + 穿墙** —— Hook `Shark.throwShark`，整段重写，
    ///    不再调 orig：按配置的数量铺扇形、每只都 `maxDist *= 倍数`、`ignoreWalls = true`。
    ///
    /// 写法参考：`ThrowableStuff`（`shootFromWeapon(self, Ref&lt;bool&gt;.Null, ...)`）、
    /// `ChronoBlade`（`new dc.en.bu.Shark(hero, atk, ang, spd)` 那类）。
    ///
    /// ## 攻速 ×3 走的是数据补丁
    ///
    /// "攻速"由 strikeChain 里三个字段决定（`res/data.cdb` 的 `weapon` 表）：
    ///
    ///     charge         出招前摇（秒）—— 按下去到命中要等多久
    ///     lockCtrlAfter  命中后锁控制时长 —— 也决定能不能马上接下一段
    ///     animSpd        动画播放倍速（原版 [0]/[1] = 1.3，[2] 没写 = 1.0）
    ///
    /// 由 `patch_shark_cdb.py` 改成 `charge/3`、`lockCtrlAfter/3`、`animSpd*3`，
    /// 打进 res.pak 后由下面的 `IOnAfterLoadingAssets` 挂载 —— 别的模组只"复制" res.pak
    /// 到 mods 目录，DCCM 不会自动挂载，不挂的话这份数据补丁根本不会生效。
    /// </summary>
    public class SharkBarrageMain : ModBase, IOnGameExit, IOnAfterLoadingAssets
    {
        /// <summary>原版 throwShark() 瞄准用的初值：11 * 0.4。</summary>
        private const double VanillaAimRcase = 4.4;

        /// <summary>原版第三下的自定义动画序列。</summary>
        private const string ThrowAnim = "AtkSharkC_NOSHARK";

        /// <summary>传奇词缀。</summary>
        private const string LegendAffix = "TripleBullets";

        /// <summary>原版枪口特效颜色。</summary>
        private const int MuzzleColor = 15297046;

        private static SharkBarrageMain _self;

        private readonly Random _rng = new();

        /// <summary>已丢出多少只（日志用）。</summary>
        private int _spawned;

        public SharkBarrageMain(ModInfo info) : base(info) { }

        internal static void Write(string msg)
        {
            global::System.Console.WriteLine(msg);
            try { _self?.Logger?.Information(msg); } catch { }
        }

        public override void Initialize()
        {
            base.Initialize();
            _self = this;

            // ① 删掉前两下：把 Shark 的 get_cycle() 恒定为 2
            try { Hook_Weapon.get_cycle += OnGetCycle; }
            catch (Exception ex) { Logger.Error(ex, "[SharkBarrage] Hook_Weapon.get_cycle 挂载失败"); }

            // ② 重写丢鲨鱼：数量 / 距离 / 穿墙
            try { Hook_Shark.throwShark += OnThrowShark; }
            catch (Exception ex) { Logger.Error(ex, "[SharkBarrage] Hook_Shark.throwShark 挂载失败"); }

            var c = SharkKeys.Config.Value;
            Logger.Information($"[SharkBarrage] 已加载：每次攻击直接丢鲨鱼（删前两下）=" +
                               $"{c.SharkCount} 只 / 传奇 {c.LegendSharkCount} 只、距离 ×{c.RangeMult} / 传奇 ×{c.LegendRangeMult}、" +
                               $"无视墙体={c.IgnoreWalls}、扇角 {c.FanAngleDeg}°");
            try { SharkKeys.Config.Save(); } catch { }
        }

        // ---------------------------------------------------------------- ① 恒定第三下

        /// <summary>
        /// 对所有武器都会触发，所以这里只处理 <see cref="Shark"/>，其余原样返回。
        /// 返回 2 的效果见类注释：Shark.onExecute() 永远走"丢鲨鱼"那条分支。
        /// </summary>
        private int OnGetCycle(Hook_Weapon.orig_get_cycle orig, dc.tool.Weapon self)
        {
            int cycle = orig(self);

            var cfg = SharkKeys.Config.Value;
            if (cfg == null || !cfg.EnableMod || !cfg.AlwaysThrow) return cycle;
            if (self == null || self.destroyed || self.owner == null) return cycle;

            // ⚠ dc.tool.weap.Shark 和 dc.en.bu.Shark 同名，这里要的是"武器"那个
            if (self is Shark) return 2;

            return cycle;
        }

        // ---------------------------------------------------------------- ② 丢鲨鱼

        private void OnThrowShark(Hook_Shark.orig_throwShark orig, Shark self)
        {
            var cfg = SharkKeys.Config.Value;
            if (cfg == null || !cfg.EnableMod)
            {
                orig(self);
                return;
            }

            try
            {
                ThrowBarrage(self, cfg);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SharkBarrage] 丢鲨鱼失败，回退原版");
                try { orig(self); } catch { }
            }
        }

        /// <summary>重写版的 throwShark()。</summary>
        private void ThrowBarrage(Shark self, SharkBarrageConfig cfg)
        {
            Hero hero = self.owner;
            if (hero == null || hero.destroyed || hero._level == null) return;

            // ---- 瞄准（和原版一样：getTargetAng(11 * 0.4, 0.2, null)）
            double aim = VanillaAimRcase;
            try { aim = self.getTargetAng(VanillaAimRcase, (double?)0.2, null); }
            catch { }

            // ---- 传奇词缀
            bool legend = false;
            try { legend = self.item != null && self.item.hasAffix(StringUtils.AsHaxeString(LegendAffix)); }
            catch { }

            int count = legend ? cfg.LegendSharkCount : cfg.SharkCount;
            if (count < 1) count = 1;
            if (count > 400) count = 400;

            double rangeMult = legend ? cfg.LegendRangeMult : cfg.RangeMult;
            if (rangeMult <= 0.0) rangeMult = 1.0;

            // ---- 攻击数据（武器那份，带等级/词缀倍率）
            AttackData atk = AttackUtils.Class.createFromHeroWeapon.Invoke(self, null);
            if (atk == null) return;

            int glow = 0;
            try { glow = (int)self.get_curSkillInf().glowColor; } catch { }

            // ---- 扇形铺开：以瞄准角为中心对称
            double step = 0.0;
            if (count > 1) step = (cfg.FanAngleDeg * Math.PI / 180.0) / (count - 1);
            double start = -step * (count - 1) / 2.0;

            int made = 0;
            for (int i = 0; i < count; i++)
            {
                // 原版这里会 break（Shark 没有 commonProps.ammo → 恒 true，实际上不会断）
                try { if (!self.consumeAmmo()) break; } catch { }

                double ang = aim + start + step * i;
                double spd = 1.1 + _rng.NextDouble() * 0.1;

                dc.en.bu.Shark shark = new dc.en.bu.Shark(hero, atk, ang, spd);
                shark.init();

                try { shark.tail = new BulletTail.Line(glow, 0.7, null); } catch { }

                try { shark.shootFromWeapon(self, Ref<bool>.Null, Ref<double>.Null, Ref<double>.Null); }
                catch { }

                // 范围 ×N（基类默认 11 格 × 24 = 264px）
                if (rangeMult != 1.0) shark.maxDist *= rangeMult;

                // 无视墙体
                if (cfg.IgnoreWalls)
                {
                    shark.ignoreWalls = true;
                    shark.ignoreOneWays = true;
                }

                made++;
            }

            // ---- 枪口特效（原版每只都放，这里只放一次）
            if (cfg.PlayMuzzleFx && made > 0)
            {
                try { hero._level.fx.shoot(self.get_shootX(), self.get_shootY(), aim, MuzzleColor); }
                catch { }
            }

            _spawned += made;
            Write($"[SharkBarrage] 丢出 {made} 只鲨鱼（{(legend ? "传奇" : "普通")}）：瞄准角 {aim:0.###}、" +
                  $"扇角 {cfg.FanAngleDeg:0.#}°、每只间隔 {step:0.####}rad、" +
                  $"距离 ×{rangeMult:0.##}（单只 {264.0 * rangeMult:0}px）、无视墙体={cfg.IgnoreWalls}");
        }

        // ---------------------------------------------------------------- 生命周期

        /// <summary>
        /// 资源加载完成后手动挂载本模组自带的 res.pak。
        /// ⚠ 不挂的话 DCCM 的 CDBManager 读不到 data.cdb_ 补丁，攻速就不会 ×3。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string pakPath = null;
                try { pakPath = Info?.ModRoot?.GetFilePath("res.pak"); } catch { }
                if (string.IsNullOrEmpty(pakPath))
                {
                    string dir = Path.GetDirectoryName(typeof(SharkBarrageMain).Assembly.Location) ?? "";
                    pakPath = Path.Combine(dir, "res.pak");
                }

                if (File.Exists(pakPath))
                {
                    var fs = FsPak.Instance?.FileSystem;
                    if (fs == null) { Logger.Warning("[SharkBarrage] FsPak 还没就绪，攻速补丁未挂载"); return; }
                    fs.loadPak(StringUtils.AsHaxeString(pakPath));
                    Logger.Information($"[SharkBarrage] res.pak 已挂载，攻速 x3 补丁生效: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[SharkBarrage] 未找到 res.pak: {pakPath}（攻速不会 ×3，其余改动照常）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[SharkBarrage] res.pak 加载失败（攻速不会 ×3）");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Weapon.get_cycle -= OnGetCycle; } catch { }
            try { Hook_Shark.throwShark -= OnThrowShark; } catch { }
            Logger.Information($"[SharkBarrage] 游戏退出，模组已卸载（本局共丢出 {_spawned} 只鲨鱼）");
            _self = null;
        }
    }
}
