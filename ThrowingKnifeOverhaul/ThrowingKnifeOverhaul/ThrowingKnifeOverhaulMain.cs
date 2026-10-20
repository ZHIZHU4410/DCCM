#nullable disable

using dc;
using dc.en;
using dc.hl.types;
using dc.tool;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ThrowingKnifeOverhaul
{
    /// <summary>
    /// 飞刀（ThrowingKnife / Couteau de lancer）强化模组
    /// =================================================
    /// 需求对应实现：
    ///   1) **飞刀无视墙体**
    ///      → <c>Hook_Bullet.fixedUpdate</c>（pre-orig）给我们的飞刀置
    ///        <c>ignoreWalls = true</c> / <c>ignoreOneWays = true</c>；
    ///        再把 <c>Hook_Bullet.blockOnCollision</c> / <c>onHitWall</c> 对飞刀短路掉。
    ///        两者缺一不可：<c>ignoreWalls</c> 只让 fixedUpdate 的格子判定跳过，
    ///        而 <c>onTouchGround()</c> 是**无条件**调 <c>blockOnCollision()</c> 的
    ///        （GamePseudocode/dc.en/Bullet.cs:3952），落墙/落地照样会被打掉。
    ///        命中敌人不受影响 —— 走的是 <c>onTouchValidTarget</c> 另一条路。
    ///
    ///   2) **攻速翻三倍**
    ///      → 两处都要调，只调一处会打不满：
    ///        a. <c>Hook_Weapon.prepare(attackSpeed)</c> 乘 3 —— 压的是蓄力/出招动画
    ///           （原版 charge 0.1s，走 `chargeF -= tmod * getCastSpeed()`）；
    ///        b. 武器技能的 <c>cooldownSpeedMul</c> 乘 3 —— 原版冷却 0.25s 走的是
    ///           `coolDownF -= tmod * getCooldownSpeed()`（GamePseudocode/dc.tool.skill/OldSkill.cs:1149），
    ///           而 <c>getCooldownSpeed()</c> **不含** attackSpeed（只乘 cooldownSpeedMul），
    ///           不单独放大它的话射速会被 0.25s 卡死在 4 发/秒。
    ///
    ///   3) **锁定追击敌人**
    ///      → <c>KnifeHoming.Steer</c>：每帧用游戏的 <c>TargetHelper</c> 找最近的怪
    ///        （刻意不调 <c>filterBySight</c>，隔墙也能锁），再把速度矢量 <c>dx/dy</c>
    ///        朝它限速转向、速率不变（与 <c>HomingSoul.fixedUpdate</c> 同思路）。
    ///        另外原版 <c>maxDist</c> 只有 5.5 格，追不到人，所以配一个"追击射程 ×2.5"。
    ///
    ///   ＋ **无限飞刀**：原版只有 8 发弹药（commonProps.ammo = 8 + LimitedAmmo 标签），
    ///      配 ×3 攻速会一瞬打空，所以每次投掷前把弹药补满（可选开关）。
    ///
    /// 全部走运行时改写，不需要 CDB 数据补丁 / res.pak。
    /// </summary>
    public class ThrowingKnifeOverhaulMain : ModBase, IOnGameExit, IOnGameInit, IOnHeroUpdate, IModMenu
    {
        /// <summary>索敌半径（格）。飞刀射程约 13.75 格，索敌给宽一点。</summary>
        private const double HomingSearchCells = 16.0;

        /// <summary>每枚飞刀的运行时状态（只放"只需做一次"的标记）。</summary>
        private sealed class KnifeStat
        {
            /// <summary>本枚飞刀的 maxDist 是否已经乘过射程倍率（防止每帧越乘越大）。</summary>
            public bool RangeBoosted;
        }

        /// <summary>本模组认领的飞刀（key = 对象身份哈希）。子弹销毁时移除。</summary>
        private readonly Dictionary<int, KnifeStat> _knives = new();

        /// <summary>异常日志限流。</summary>
        private int _errorCount;

        /// <summary>首帧诊断日志只打一条。</summary>
        private bool _diagLogged;

        public ThrowingKnifeOverhaulMain(ModInfo info) : base(info) { }

        /// <summary>同时写控制台与模组日志文件（logs\log_latest.log）。</summary>
        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        // ---------------------------------------------------------------- 生命周期

        public override void Initialize()
        {
            base.Initialize();

            try { Hook_Weapon.prepare += OnWeaponPrepare; }
            catch (Exception ex) { Logger.Error(ex, "[ThrowingKnifeOverhaul] Hook_Weapon.prepare 挂载失败"); }

            try { Hook_Bullet.fixedUpdate += OnBulletFixedUpdate; }
            catch (Exception ex) { Logger.Error(ex, "[ThrowingKnifeOverhaul] Hook_Bullet.fixedUpdate 挂载失败"); }

            try { Hook_Bullet.blockOnCollision += OnBulletBlockOnCollision; }
            catch (Exception ex) { Logger.Error(ex, "[ThrowingKnifeOverhaul] Hook_Bullet.blockOnCollision 挂载失败"); }

            try { Hook_Bullet.onHitWall += OnBulletHitWall; }
            catch (Exception ex) { Logger.Error(ex, "[ThrowingKnifeOverhaul] Hook_Bullet.onHitWall 挂载失败"); }

            try { Hook_Bullet.dispose += OnBulletDispose; }
            catch (Exception ex) { Logger.Error(ex, "[ThrowingKnifeOverhaul] Hook_Bullet.dispose 挂载失败"); }

            Write("[ThrowingKnifeOverhaul] 已加载：飞刀无视墙体 + 攻速×3 + 锁定追击 + 射程×2.5 + 无限飞刀");
        }

        void IOnGameInit.OnGameInit()
        {
            try
            {
                var cfg = KnifeKeys.Config.Value;
                Write("[ThrowingKnifeOverhaul] 开关状态 —— " +
                      $"总开关={cfg.EnableMod} 无视墙体={cfg.EnableWallPierce} 攻速×{cfg.AttackSpeedMult:0.##}={cfg.EnableAttackSpeed} " +
                      $"锁定追击={cfg.EnableHoming}（{cfg.HomingTurnDegPerFrame:0.#}°/帧）" +
                      $"射程×{cfg.RangeMult:0.##}={cfg.EnableHomingRange} 无限飞刀={cfg.EnableInfiniteAmmo}");
                Write($"[ThrowingKnifeOverhaul] 配置文件: {KnifeKeys.Config.ConfigPath}" +
                      "（也可在游戏里 选项 → 模组 → 飞刀强化 直接改）");

                try { if (KnifeKeys.Config.Value != null) KnifeKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ThrowingKnifeOverhaul] 写配置失败: {ex.Message}");
            }
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 热键切换（默认一个都没绑；只在"刚按下"那一帧动作）
            try { KnifeFeatures.PollHotkeys(Write); } catch { }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Weapon.prepare -= OnWeaponPrepare; } catch { }
            try { Hook_Bullet.fixedUpdate -= OnBulletFixedUpdate; } catch { }
            try { Hook_Bullet.blockOnCollision -= OnBulletBlockOnCollision; } catch { }
            try { Hook_Bullet.onHitWall -= OnBulletHitWall; } catch { }
            try { Hook_Bullet.dispose -= OnBulletDispose; } catch { }

            KnifeFeatures.ResetHotkeyState();
            _knives.Clear();
            _diagLogged = false;
            Write("[ThrowingKnifeOverhaul] 游戏退出，模组已卸载");
        }

        // ---------------------------------------------------------------- ② 攻速 / 弹药（每次投掷前）

        /// <summary>
        /// 每次投掷前：
        ///   1) 无限弹药的补弹**必须放在 orig 之前** —— 原版 prepare 里
        ///      `if (ammo &lt;= 0 &amp;&amp; !hasTag("CanPrepareWithoutAmmo")) { onOutOfAmmoTry(); return; }`
        ///      会先把这次投掷掐掉，补弹放到之后就永远补不上；
        ///   2) 攻速倍率同时乘到 prepare 的 attackSpeed 和技能的 cooldownSpeedMul 上（见类注释）。
        /// </summary>
        private void OnWeaponPrepare(Hook_Weapon.orig_prepare orig, Weapon self, double attackSpeed)
        {
            if (self is ThrowingKnife knife)
            {
                try
                {
                    var cfg = KnifeKeys.Config.Value;

                    if (KnifeFeatures.IsOn(KnifeFeature.InfiniteAmmo)) RefillAmmo(knife);

                    bool fast = KnifeFeatures.IsOn(KnifeFeature.AttackSpeed);
                    double mult = fast ? Clamp(cfg.AttackSpeedMult, 1.0, 20.0) : 1.0;

                    ApplyCooldownSpeed(knife, mult);

                    if (mult != 1.0) attackSpeed *= mult;
                }
                catch (Exception ex) { Fail("prepare", ex); }
            }

            orig(self, attackSpeed);
        }

        /// <summary>把弹药补到上限（原版 8 发）。</summary>
        private static void RefillAmmo(ThrowingKnife knife)
        {
            InventItem item = knife.item;
            if (item == null) return;

            int max = item.getMaxAmmo();
            if (max > 0 && item.ammo != max) item.ammo = max;
        }

        /// <summary>
        /// 把武器技能的 <c>cooldownSpeedMul</c> 写成给定倍率。
        /// 这个字段是冷却条（`coolDownF -= tmod * getCooldownSpeed()`）的速度系数，
        /// **原版不含 attackSpeed**，所以攻速想真的翻倍就靠它。
        /// 恢复原版时写回 1.0，所以开关可以来回切。
        /// </summary>
        private static void ApplyCooldownSpeed(Weapon w, double mult)
        {
            ArrayObj skills = w.skills;
            if (skills == null) return;

            for (int i = 0; i < skills.length; i++)
            {
                WeaponSkill sk = skills.getDyn(i) as WeaponSkill;
                if (sk != null) sk.cooldownSpeedMul = mult;
            }
        }

        // ---------------------------------------------------------------- ①③ 子弹逐帧

        /// <summary>
        /// 挂在 Bullet 基类的 fixedUpdate 上（飞刀就是原生 <c>new Bullet(...)</c>，没有子类）。
        /// 放在 orig 之前：本帧的 ignoreWalls / 航向立刻生效（原版 fixedUpdate 会读它们）。
        /// </summary>
        private void OnBulletFixedUpdate(Hook_Bullet.orig_fixedUpdate orig, Bullet self)
        {
            if (self != null && !self.destroyed && IsKnife(self))
            {
                try { ApplyPerFrame(self); }
                catch (Exception ex) { Fail("bullet fixedUpdate", ex); }
            }

            orig(self);
        }

        /// <summary>每帧给飞刀应用：① 穿墙、③ 射程、③ 索敌转向。</summary>
        private void ApplyPerFrame(Bullet b)
        {
            int key = RuntimeHelpers.GetHashCode(b);
            if (!_knives.TryGetValue(key, out KnifeStat st))
            {
                st = new KnifeStat();
                _knives[key] = st;
            }

            var cfg = KnifeKeys.Config.Value;

            // ---- ① 穿墙：fixedUpdate 的格子判定 + ignoreOneWays 一起打开 ----
            if (KnifeFeatures.IsOn(KnifeFeature.WallPierce))
            {
                if (!b.ignoreWalls) b.ignoreWalls = true;
                if (!b.ignoreOneWays) b.ignoreOneWays = true;
            }

            // ---- ③ 射程：用状态记着"已经乘过"，避免每帧越乘越大 ----
            bool wantRange = KnifeFeatures.IsOn(KnifeFeature.HomingRange);
            if (st.RangeBoosted != wantRange && b.maxDist > 0.0)
            {
                double rangeMult = Clamp(cfg.RangeMult, 1.0, 20.0);
                b.maxDist = wantRange ? b.maxDist * rangeMult : b.maxDist / rangeMult;
                st.RangeBoosted = wantRange;
            }

            // ---- ③ 索敌转向 ----
            if (KnifeFeatures.IsOn(KnifeFeature.Homing))
            {
                KnifeHoming.Steer(b, Clamp(cfg.HomingTurnDegPerFrame, 1.0, 90.0), HomingSearchCells);
            }

            if (!_diagLogged) LogDiagOnce(b, cfg);
        }

        /// <summary>
        /// 首帧诊断日志（一局一条）：把认领到的飞刀的关键字段打出来，
        /// 便于确认"是不是真的接管了这枚子弹、穿墙/射程有没有生效"。
        /// </summary>
        private void LogDiagOnce(Bullet b, KnifeConfig cfg)
        {
            if (_diagLogged) return;
            _diagLogged = true;

            try
            {
                Write("[ThrowingKnifeOverhaul] 已接管飞刀 —— " +
                      $"ignoreWalls={b.ignoreWalls} ignoreOneWays={b.ignoreOneWays} " +
                      $"maxDist={b.maxDist:0.##}px 速度=({b.dx:0.##},{b.dy:0.##}) " +
                      $"攻速×{cfg.AttackSpeedMult:0.##} 转向={cfg.HomingTurnDegPerFrame:0.#}°/帧 射程×{cfg.RangeMult:0.##}");
            }
            catch { }
        }

        /// <summary>
        /// 认领飞刀：第一帧靠 <c>atk.sourceWeapon is ThrowingKnife</c> 判断，
        /// 认到之后就记进字典、以后只查字典。
        ///
        /// 为什么不每帧都查 atk：<c>AttackData</c> 是对象池（<c>AttackData.POOL</c>）里的，
        /// 被回收复用时会 `sourceWeapon = null`（GamePseudocode/dc.tool.atk/AttackData.cs:126），
        /// 飞在半路的子弹就可能"失去身份"。认一次、记下来最稳。
        /// </summary>
        private bool IsKnife(Bullet b)
        {
            if (b == null) return false;

            int key = RuntimeHelpers.GetHashCode(b);
            if (_knives.ContainsKey(key)) return true;

            try
            {
                if (b.atk != null && b.atk.sourceWeapon is ThrowingKnife)
                {
                    // 兜底：万一 dispose 没被调到，别让字典无限长
                    if (_knives.Count > 4096) _knives.Clear();
                    _knives[key] = new KnifeStat();
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// ① 穿墙的第二条路径：<c>Bullet.onTouchGround()</c> 是**无条件**调
        /// <c>blockOnCollision()</c> 的（GamePseudocode/dc.en/Bullet.cs:3952-3956），
        /// 而 blockOnCollision → onHitWall → block(false) → destroy()，
        /// 所以光设 ignoreWalls 不够 —— 飞刀一落到墙/地面上照样被打掉。
        /// 命中敌人走的是另一条 <c>onTouchValidTarget</c>，不受影响。
        /// </summary>
        private void OnBulletBlockOnCollision(Hook_Bullet.orig_blockOnCollision orig, Bullet self)
        {
            if (self != null && IsKnife(self) && KnifeFeatures.IsOn(KnifeFeature.WallPierce)) return;
            orig(self);
        }

        /// <summary>① 兜底：某些路径直接调 <c>onHitWall()</c>，同样对飞刀跳过。</summary>
        private void OnBulletHitWall(Hook_Bullet.orig_onHitWall orig, Bullet self)
        {
            if (self != null && IsKnife(self) && KnifeFeatures.IsOn(KnifeFeature.WallPierce)) return;
            orig(self);
        }

        /// <summary>飞刀销毁 → 清掉追踪记录（字典不会无限增长）。</summary>
        private void OnBulletDispose(Hook_Bullet.orig_dispose orig, Bullet self)
        {
            try
            {
                if (self != null) _knives.Remove(RuntimeHelpers.GetHashCode(self));
            }
            catch { }

            orig(self);
        }

        // ---------------------------------------------------------------- 选项菜单（IModMenu）

        /// <summary>这一页在「选项 → 模组」里的名字。</summary>
        public string GetName() => "飞刀强化";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("THROWING KNIFE OVERHAUL 设置"));
                b.createScroller(0.0);

                // 每个功能一个复选框（总开关排最前）
                foreach (var f in KnifeFeatures.All)
                {
                    var feature = f;                       // 闭包捕获：别直接用循环变量
                    bool on = KnifeFeatures.RawGet(feature);
                    string key = KnifeFeatures.KeyName(feature);
                    string hint = KnifeFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(KnifeFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !KnifeFeatures.RawGet(feature);
                            KnifeFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                // ---- 三条数值滑条（写法照抄 ZoomVision / AdeleScytheOverhaul 的 addSliderWidget）----
                var cfg = KnifeKeys.Config.Value;

                b.addSliderWidget(
                    StringUtils.AsHaxeString("攻速倍率"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.AttackSpeedMult = v;
                        try { KnifeKeys.Config.Save(); } catch { }
                    },
                    cfg.AttackSpeedMult,
                    Ref<double>.In(0.5),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(1.0),
                    Ref<double>.In(20.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("追击转向(°/帧)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.HomingTurnDegPerFrame = v;
                        try { KnifeKeys.Config.Save(); } catch { }
                    },
                    cfg.HomingTurnDegPerFrame,
                    Ref<double>.In(5.0),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(5.0),
                    Ref<double>.In(90.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("追击射程倍率"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.RangeMult = v;
                        try { KnifeKeys.Config.Save(); } catch { }
                    },
                    cfg.RangeMult,
                    Ref<double>.In(0.5),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(1.0),
                    Ref<double>.In(10.0),
                    null,
                    Ref<int>.In(0));

                b.updateScroller();
                Write($"[ThrowingKnifeOverhaul] 选项菜单已建立：{KnifeFeatures.All.Length} 个功能开关 + 3 条滑条");
            }
            catch (Exception ex)
            {
                Write($"[ThrowingKnifeOverhaul] 建立选项菜单失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>异常日志限流：最多刷 5 条，避免每帧刷屏。</summary>
        private void Fail(string where, Exception ex)
        {
            if (_errorCount >= 5) return;
            _errorCount++;
            Write($"[ThrowingKnifeOverhaul] {where} 出错（第 {_errorCount} 次，最多记 5 次）: {ex.Message}");
        }

        /// <summary>把滑条值夹到合法区间（防止手改配置写成负数 / 天文数字）。</summary>
        private static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }
    }
}
