#nullable disable

using dc;
using dc.en;
using dc.pow;
using dc.tool;
using dc.tool.atk;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using SysMath = System.Math;

namespace DashOverhaul
{
    /// <summary>
    /// Dash（突击 Assault）强化模组
    /// ==================================================================
    /// 原版行为（读的是 GamePseudocode/dc.pow.Dash.cs + dc.pow._Dash.cs）：
    ///   · 使用 Dash   -> new Dash(hero, item, isBack:false)，沿 dir(±1) 水平冲刺 range 格；
    ///   · 结束时 Dash.onEnd() 里 hero.transformInventoryItem(item,null)，
    ///     读 item._itemData.commonProps.item 当变身目标 -> 变成 BackDash；
    ///   · 再用就是 new Dash(hero, item, isBack:true)，朝反方向冲回来。
    ///   原版 fixedUpdate 里有十几处硬编码的「沿 X 轴」判定（剩余距离按 |startX-curX| 算、
    ///   没位移就 didntMoveLastFrame -> endDash），所以原版天然做不出 8 方向。
    ///
    /// 本模组实现（需求）：
    ///   1) 只能释放 Dash   —— 数据补丁把 Dash.commonProps.item 改回 "Dash"（不再变 BackDash），
    ///                          代码里再兜一层：BackDash 若真的出现，照样按本模组的 8 方向冲刺跑。
    ///   2) 八方向冲刺      —— Hook_Dash.fixedUpdate 完全接管原版逐帧逻辑（不调用 orig），
    ///                          起手瞬间采样方向键/WASD 得到单位向量，整段冲刺锁死这个方向。
    ///   3) 伤害 +500%      —— 数据补丁 props.power [100] -> [600]（createFromHeroItem 直接吃这个值）。
    ///   4) 无视墙体        —— 冲刺期间 hero.collisionMode = CollisionMode.IgnoreWalls、
    ///                          hasGravity = false，并且位移由本模组直接写 cx/xr/cy/yr，不经过碰撞求解。
    ///   5) 拖尾改淡蓝色    —— Hook_Dash.postUpdate 不调用 orig（原版是橙色 0xEE9B11 / 0xFFFF33），
    ///                          改画 0x99E5FF 的 OnionSkin 残影。
    ///   6) castCD = 0      —— 数据补丁。
    ///
    /// 单位约定（KingScepterAim / dc.en.Hero 里踩过的坑）：
    ///   · Entity.dx / dy / bdx / bdy 是【格/帧】；
    ///   · Entity.cx/cy 是格，xr/yr 是格内 0..1 小数；世界像素 = (cx+xr)*24；
    ///   · Entity.radius / hei 是【像素】。
    ///   本模组全部用【格】做位移与命中判定，只有画特效时才 *24 转像素。
    ///
    /// 编译期 API 说明：mod 编译的是 MDK 的 GameProxy.dll，
    ///   · haxe 静态方法要通过类对象调用：AttackUtils.Class.createFromHeroItem.Invoke(...)；
    ///   · 可选参数用 HaxeProxy.Runtime.Ref&lt;T&gt;；
    ///   · dc 命名空间里有个 dc.Math，所以 System.Math 必须用别名 SysMath。
    /// </summary>
    public class DashOverhaulMain : ModBase, IOnGameExit, IOnAfterLoadingAssets
    {
        // ================= 可调参数 =================

        /// <summary>冲刺拖尾 / 残影颜色：淡蓝色（原版是橙色 0xEE9B11，残影 0xFFFF33）。</summary>
        private const int TrailColor = 0x99E5FF;

        /// <summary>路径命中判定的额外宽容（格）。</summary>
        private const double HitPadCases = 0.25;

        /// <summary>数据读不到时的兜底（= 原版 Dash 的 props）。</summary>
        private const double DefaultSpeedCases = 5.0;     // 格/帧
        private const double DefaultRangeCases = 13.0;    // 格

        /// <summary>极端情况下的兜底：冲刺帧数上限，防止任何原因导致冲刺永不完结。</summary>
        private const int MaxDashFrames = 240;

        /// <summary>连续多少帧位置没变就认为被卡住（正常 IgnoreWalls 不会发生）。</summary>
        private const int MaxStillFrames = 4;

        // ================= 按键（方向键 + WASD） =================

        private const int VK_LEFT = 0x25;
        private const int VK_UP = 0x26;
        private const int VK_RIGHT = 0x27;
        private const int VK_DOWN = 0x28;
        private const int VK_A = 0x41;
        private const int VK_D = 0x44;
        private const int VK_W = 0x57;
        private const int VK_S = 0x53;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vKey);

        // ================= 运行时状态 =================

        /// <summary>一次冲刺的全部状态（key = Hero.__uid）。</summary>
        private sealed class DashState
        {
            public int HeroUid;
            public int DashUid;
            public Hero Hero;
            public Dash Dash;

            /// <summary>props.power（数据补丁后是 [600]），命中时直接交给 createFromHeroItem。</summary>
            public object Power;

            public double Vx;                          // 单位方向向量
            public double Vy;
            public double Speed = DefaultSpeedCases;   // 格/帧
            public double MaxDist = DefaultRangeCases; // 格
            public double Traveled;                    // 已冲距离（格）
            public double PrevX;                       // 上一帧位置（格）
            public double PrevY;
            public double StartX;                      // 起手位置（格），画拖尾用
            public double StartY;

            public bool GravityWasOn = true;
            public CollisionMode PrevCollision;

            public int Frames;
            public int StillFrames;
            public int TrailFrames;

            /// <summary>本次冲刺已经结算过的敌人（同一个敌人一次冲刺只吃一次伤害）。</summary>
            public readonly HashSet<int> HitUids = new HashSet<int>();
        }

        private readonly Dictionary<int, DashState> _states = new Dictionary<int, DashState>();

        /// <summary>没有被本模组接管的 Dash（理论上不会出现）—— 计次后强制结束，避免卡死。</summary>
        private readonly Dictionary<int, int> _orphanTicks = new Dictionary<int, int>();

        private bool _loggedFirstDash;
        private bool _loggedMissingAttackUtils;
        private bool _loggedBackDashFallback;
        private bool _loggedDashData;

        public DashOverhaulMain(ModInfo info) : base(info) { }

        // ==================================================================
        // 生命周期
        // ==================================================================

        public override void Initialize()
        {
            base.Initialize();

            try { Hook_Dash.fixedUpdate += OnDashFixedUpdate; }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] Hook_Dash.fixedUpdate 挂载失败"); }

            try { Hook_Dash.postUpdate += OnDashPostUpdate; }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] Hook_Dash.postUpdate 挂载失败"); }

            try { Hook_Dash.onEnd += OnDashOnEnd; }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] Hook_Dash.onEnd 挂载失败"); }

            Logger.Information("[DashOverhaul] 已加载：Dash 8 方向冲刺 / 伤害+500%(数据补丁) / 无视墙体 / 淡蓝拖尾 / castCD=0 / 永不变成 BackDash");
        }

        /// <summary>
        /// 资源加载完成：手动挂载本模组的 res.pak（item/Dash 数据补丁）。
        /// 【必须做】DCCM 不会自动把模组目录下的 res.pak 合进游戏资源，
        /// 少了这一步 castCD / props.power / commonProps.item 全都是原版值，
        /// 表现就是「用完 Dash 依然变成 BackDash」。
        /// 参考 DamageAuraBoost / WreckingBallOverhaul / BossNoDmgSoftCap 的同名实现。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(DashOverhaulMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[DashOverhaul] res.pak 已加载: {pakPath}");
                    LogDashDataOnce("res.pak 挂载后");
                }
                else
                {
                    Logger.Warning($"[DashOverhaul] 未找到 res.pak: {pakPath}（castCD/伤害/变身目标将退回原版，代码里有 BackDash 兜底）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[DashOverhaul] res.pak 加载失败（castCD/伤害/变身目标将退回原版，代码里有 BackDash 兜底）");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Dash.fixedUpdate -= OnDashFixedUpdate; } catch { }
            try { Hook_Dash.postUpdate -= OnDashPostUpdate; } catch { }
            try { Hook_Dash.onEnd -= OnDashOnEnd; } catch { }

            var leftover = new List<DashState>(_states.Values);
            _states.Clear();
            _orphanTicks.Clear();
            foreach (var st in leftover) RestoreHero(st);

            Logger.Information("[DashOverhaul] 游戏退出，模组已卸载");
        }

        // ==================================================================
        // 1) 冲刺逐帧：接管位移 + 路径伤害 + 结束判定
        //
        //    入口选在 Hook_Dash.fixedUpdate 而不是 Hook_Hero.fixedUpdate：
        //    原版 Dash 的移动本来就依赖 Power.update() -> fixedUpdate()，所以这个钩子必然会被调到；
        //    而位移是本模组直接写 cx/xr/cy/yr，也不需要每帧去"重申"。
        //    好处是同一帧只会 tick 一次，不会出现两个钩子各推一次导致双倍位移。
        // ==================================================================

        private void OnDashFixedUpdate(Hook_Dash.orig_fixedUpdate orig, Dash self)
        {
            // 刻意不调用 orig(self)：原版只会沿 X 轴冲刺，
            // 而且它用 |startX-curX| 判"有没有移动"，竖向冲刺会被它当场 endDash。
            try
            {
                var owner = self.owner as Hero;
                if (owner != null && !self.destroyed && self.isDashing && !owner.destroyed)
                {
                    _orphanTicks.Remove(self.__uid);
                    TickDash(owner);
                    return;
                }

                // Power 已经结束/owner 不是英雄：计次后兜底收尾，避免挂着一个永不结束的 Power
                _orphanTicks.TryGetValue(self.__uid, out int c);
                c++;
                _orphanTicks[self.__uid] = c;
                if (c >= 5)
                {
                    _orphanTicks.Remove(self.__uid);
                    Logger.Warning("[DashOverhaul] 检测到异常状态的 Dash，强制结束以免卡死");
                    try { if (!self.destroyed) self.endDash(); } catch { }
                }
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] fixedUpdate 接管异常"); }
        }

        private void TickDash(Hero hero)
        {
            _states.TryGetValue(hero.__uid, out DashState st);

            if (hero.destroyed || hero.life <= 0 || hero._level == null)
            {
                if (st != null) FinishDash(st, "英雄不可用");
                return;
            }

            Dash dash = FindActiveDash(hero);
            if (dash == null)
            {
                if (st != null) FinishDash(st, "冲刺 Power 已消失");
                return;
            }

            if (st == null || st.DashUid != dash.__uid)
            {
                if (st != null) FinishDash(st, "被新的冲刺替换");
                st = StartDash(hero, dash);
                if (st == null) return;
                _states[hero.__uid] = st;
            }
            else
            {
                st.Dash = dash;      // 刷新代理实例
            }

            // ---------- 位移：无视墙体，直接写 cx/xr/cy/yr ----------
            try
            {
                hero.hasGravity = false;
                if (!(hero.collisionMode is CollisionMode.IgnoreWalls))
                    hero.collisionMode = new CollisionMode.IgnoreWalls();
                // 清掉原版速度，避免 Entity.updatePositionXY 再叠加位移
                hero.dx = 0.0;
                hero.dy = 0.0;
                hero.bdx = 0.0;
                hero.bdy = 0.0;

                double remain = st.MaxDist - st.Traveled;
                if (remain < 0.0) remain = 0.0;
                double step = st.Speed < remain ? st.Speed : remain;
                MoveBy(hero, st.Vx * step, st.Vy * step);
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] 冲刺位移异常"); }

            // ---------- 统计位移 ----------
            double px = CaseX(hero);
            double py = CaseY(hero);
            double ddx = px - st.PrevX;
            double ddy = py - st.PrevY;
            st.Traveled += SysMath.Sqrt(ddx * ddx + ddy * ddy);
            st.StillFrames = (SysMath.Abs(ddx) + SysMath.Abs(ddy) < 0.001) ? st.StillFrames + 1 : 0;

            // ---------- 路径伤害（上一帧位置 -> 当前位置 的整条线段） ----------
            HitAlongPath(hero, st, st.PrevX, st.PrevY, px, py);

            st.PrevX = px;
            st.PrevY = py;
            st.Frames++;

            // ---------- 结束判定 ----------
            if (st.Traveled >= st.MaxDist - 0.001) { FinishDash(st, "冲满距离"); return; }
            if (hero.isUnconscious()) { FinishDash(st, "英雄失去意识"); return; }
            if (dash.item == null) { FinishDash(st, "技能道具丢失"); return; }
            if (st.Frames >= MaxDashFrames) { FinishDash(st, "超时兜底"); return; }
            if (st.StillFrames >= MaxStillFrames) { FinishDash(st, "被挡住"); return; }
        }

        /// <summary>在 level.powers 里找这个英雄当前正在进行的 Dash。</summary>
        private static Dash FindActiveDash(Hero hero)
        {
            try
            {
                var level = hero._level;
                if (level == null) return null;
                var powers = level.powers;
                if (powers == null) return null;

                int n = powers.length;
                for (int i = 0; i < n; i++)
                {
                    if (!(powers.getDyn(i) is Dash p)) continue;
                    if (p.destroyed || !p.isDashing) continue;
                    var owner = p.owner;
                    if (owner == null || owner.destroyed) continue;
                    if (owner.__uid == hero.__uid) return p;
                }
            }
            catch { }
            return null;
        }

        private DashState StartDash(Hero hero, Dash dash)
        {
            var st = new DashState
            {
                Hero = hero,
                HeroUid = hero.__uid,
                Dash = dash,
                DashUid = dash.__uid,
            };

            // ---- 方向采样：方向键 / WASD，八方向 ----
            double vx = 0.0, vy = 0.0;
            if (KeyDown(VK_RIGHT) || KeyDown(VK_D)) vx += 1.0;
            if (KeyDown(VK_LEFT) || KeyDown(VK_A)) vx -= 1.0;
            if (KeyDown(VK_DOWN) || KeyDown(VK_S)) vy += 1.0;
            if (KeyDown(VK_UP) || KeyDown(VK_W)) vy -= 1.0;   // cy 向下增长
            if (vx == 0.0 && vy == 0.0)
            {
                // 没按方向就是原版行为：朝面朝方向平冲
                vx = (dash.dir >= 0) ? 1.0 : -1.0;
                vy = 0.0;
            }
            double len = SysMath.Sqrt(vx * vx + vy * vy);
            st.Vx = vx / len;
            st.Vy = vy / len;

            // ---- 数值：来自数据补丁后的 Dash 行 ----
            ReadDashProps(dash, st);

            // ---- 保存并接管英雄状态 ----
            try { st.GravityWasOn = hero.hasGravity; } catch { st.GravityWasOn = true; }
            try { st.PrevCollision = hero.collisionMode; } catch { st.PrevCollision = null; }

            try
            {
                hero.hasGravity = false;
                hero.collisionMode = new CollisionMode.IgnoreWalls();
                if (vx != 0.0) hero.dir = (vx > 0.0) ? 1 : -1;
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] 接管英雄状态失败"); }

            st.PrevX = CaseX(hero);
            st.PrevY = CaseY(hero);
            st.StartX = st.PrevX;
            st.StartY = st.PrevY;

            if (!_loggedFirstDash)
            {
                _loggedFirstDash = true;
                Logger.Information($"[DashOverhaul] 首次冲刺：方向=({st.Vx:0.##},{st.Vy:0.##}) 速度={st.Speed:0.##}格/帧 距离上限={st.MaxDist:0.#}格 power={(st.Power == null ? "NULL" : "ok")}");
                LogDashDataOnce("首次冲刺时");
            }

            return st;
        }

        private void FinishDash(DashState st, string reason)
        {
            if (st == null) return;
            _states.Remove(st.HeroUid);
            _orphanTicks.Remove(st.DashUid);

            try
            {
                Logger.Information($"[DashOverhaul] 冲刺结束（{reason}）：位移 {st.Traveled:0.##}/{st.MaxDist:0.##} 格，{st.Frames} 帧，命中 {st.HitUids.Count} 个敌人");
            }
            catch { }

            // 先让原版收尾（它会 removeAllAffects(13)、播动画、走 transformInventoryItem、
            // 以及 apply slowMo），再还原我们改过的重力/碰撞模式，
            // 这样 endDash 里可能的 hasGravity=true（BackDash 分支）不会盖掉玩家的真实状态。
            try
            {
                var dash = st.Dash;
                if (dash != null && !dash.destroyed) dash.endDash();
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] endDash 异常"); }

            RestoreHero(st);
        }

        /// <summary>把冲刺期间被改掉的英雄状态还原（重力 / 碰撞模式 / 卡墙自救）。</summary>
        private static void RestoreHero(DashState st)
        {
            if (st == null) return;
            var hero = st.Hero;
            if (hero == null) return;
            try
            {
                if (hero.destroyed) return;
                hero.hasGravity = st.GravityWasOn;
                hero.collisionMode = st.PrevCollision ?? new CollisionMode.Normal();
                Unstick(hero);
            }
            catch { }
        }

        /// <summary>无视墙体冲刺有可能停在墙里，结束后往上/左右挪一小步脱困。</summary>
        private static void Unstick(Hero hero)
        {
            try
            {
                if (hero.isPositionEmpty(hero.cx, hero.cy)) return;
                for (int d = 1; d <= 8; d++)
                {
                    if (hero.isPositionEmpty(hero.cx, hero.cy - d))
                    {
                        hero.cy -= d;
                        hero.yr = 0.5;
                        return;
                    }
                }
                for (int d = 1; d <= 8; d++)
                {
                    if (hero.isPositionEmpty(hero.cx + d, hero.cy)) { hero.cx += d; return; }
                    if (hero.isPositionEmpty(hero.cx - d, hero.cy)) { hero.cx -= d; return; }
                }
            }
            catch { }
        }

        // ==================================================================
        // 2) 路径伤害
        // ==================================================================

        /// <summary>
        /// 沿这一帧走过的线段做命中判定，命中就按原版结算链打一次：
        ///   AttackUtils.Class.createFromHeroItem.Invoke(hero, item, props.power) -> AttackUtils.Class.hit
        /// 原版也会给这类攻击加 tag 14(Ranged) / 7，这里保持一致。
        /// 同一个敌人一次冲刺只结算一次（原版是给敌人挂一个 per-item 冷却，效果等价）。
        /// </summary>
        private void HitAlongPath(Hero hero, DashState st, double ax, double ay, double bx, double by)
        {
            if (st.Power == null || st.Dash == null || st.Dash.item == null) return;
            var team = hero._team;
            if (team == null) return;

            // haxe 静态方法在代理里是类对象上的 HlFunc 字段，先取出来并做空保护，
            // 避免某个时机类静态还没初始化时整段冲刺白跑还不报错。
            var createFn = AttackUtils.Class.createFromHeroItem;
            var hitFn = AttackUtils.Class.hit;
            if (createFn == null || hitFn == null)
            {
                if (!_loggedMissingAttackUtils)
                {
                    _loggedMissingAttackUtils = true;
                    Logger.Error($"[DashOverhaul] AttackUtils.Class.createFromHeroItem/hit 不可用（create={(createFn != null)} hit={(hitFn != null)}），冲刺不做伤害");
                }
                return;
            }

            double heroR = 0.5;
            try { heroR = hero.radius / 24.0; } catch { }

            try
            {
                var iter = team.opponentsIterator.reset(team);
                if (iter == null) return;

                while (iter.hasNext())
                {
                    Entity e = iter.next();
                    if (e == null || e.destroyed || e.life <= 0) continue;

                    int uid;
                    try { uid = e.__uid; } catch { continue; }
                    if (st.HitUids.Contains(uid)) continue;

                    try { if (!e.canBeHit()) continue; } catch { continue; }

                    double ex = e.cx + e.xr;
                    double ey = e.cy + e.yr;
                    double r;
                    try { r = heroR + e.radius / 24.0 + HitPadCases; } catch { r = heroR + 0.5 + HitPadCases; }

                    if (DistPointToSegment(ex, ey, ax, ay, bx, by) > r) continue;

                    st.HitUids.Add(uid);

                    AttackData atk = createFn.Invoke(hero, st.Dash.item, st.Power);
                    if (atk == null) continue;
                    atk.addTag(7);
                    atk.addTag(14);
                    hitFn.Invoke(atk, e);
                }
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] 路径伤害异常"); }
        }

        private static double DistPointToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double lenSq = dx * dx + dy * dy;
            if (lenSq <= 1e-9) return SysMath.Sqrt((px - ax) * (px - ax) + (py - ay) * (py - ay));
            double t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            double qx = ax + t * dx;
            double qy = ay + t * dy;
            return SysMath.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        }

        // ==================================================================
        // 4) 拖尾：淡蓝色（不执行原版的橙色拖尾）
        // ==================================================================

        private void OnDashPostUpdate(Hook_Dash.orig_postUpdate orig, Dash self)
        {
            // 同样刻意不调用 orig(self)：原版在这里画 0xEE9B11 的 tailLine /
            // 0xFFFF33 的 OnionSkin，而且只认水平方向。
            DashState st = FindStateByDash(self);
            if (st == null) return;

            var hero = st.Hero;
            if (hero == null || hero.destroyed || hero._level == null) return;

            try
            {
                st.TrailFrames++;

                double curX = CaseX(hero) * 24.0;
                double curY = CaseY(hero) * 24.0;

                var fx = hero._level.fx;
                if (fx != null)
                {
                    // ① 贴身的淡蓝色尾巴（原版这里是 0xEE9B11 橙色）
                    double offX = 0.0;
                    double offY = 0.0;
                    fx.tailLine(hero, TrailColor, 1.0, 4.0, 0.5, ref offX, ref offY);

                    // ② 起手点 -> 当前位置的淡蓝色光带（斜向/竖向都成立，
                    //    原版的 timeKeeperDash 只支持水平线，所以竖向冲刺这里补一条）
                    if (SysMath.Abs(st.Vy) > 1e-6)
                    {
                        fx.tailLineFree(st.StartX * 24.0, st.StartY * 24.0,
                                        curX, curY,
                                        TrailColor, 1.0, 4.0, 0.5);
                    }
                    else
                    {
                        fx.timeKeeperDash(st.StartX * 24.0, st.StartY * 24.0,
                                          st.MaxDist * 24.0, st.Vx >= 0.0 ? 1 : -1,
                                          1.0, TrailColor);
                    }
                }

                // ③ 残影：每帧一张淡蓝色，冲刺全程留下淡蓝色拖尾
                OnionSkin.Class.fromEntity(
                    hero,
                    null,
                    TrailColor,
                    Ref<double>.In(1.0),      // alpha
                    Ref<double>.In(0.3),      // sec（残影存活时间）
                    Ref<bool>.Null,           // fadeOutMovement
                    Ref<bool>.Null,           // useColorAdjust
                    Ref<double>.Null);        // colorAdjustAlpha
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] 拖尾异常"); }
        }

        private DashState FindStateByDash(Dash self)
        {
            try
            {
                foreach (var kv in _states)
                {
                    if (kv.Value.DashUid == self.__uid) return kv.Value;
                }
            }
            catch { }
            return null;
        }

        // ==================================================================
        // 5) 冲刺结束（无论谁结束的）：清状态 + 还原英雄
        // ==================================================================

        private void OnDashOnEnd(Hook_Dash.orig_onEnd orig, Dash self)
        {
            int permId = -1;
            try { permId = self.item?.permanentId ?? -1; } catch { permId = -1; }

            try
            {
                var st = FindStateByDash(self);
                if (st != null)
                {
                    _states.Remove(st.HeroUid);
                    _orphanTicks.Remove(st.DashUid);
                    RestoreHero(st);
                }
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] onEnd 清理异常"); }

            // 原版 onEnd 还要播动画 / 走 transformInventoryItem（数据补丁已把目标改成 Dash 自己）
            // 以及 slowMo，所以照旧调用。
            orig(self);

            // ---- 运行时兜底：绝不留下 BackDash ----
            // 正常情况下 res.pak 已把 commonProps.item 指回 Dash，这里查到的就是 Dash，什么都不做。
            // 万一 res.pak 没挂上（或其它模组覆盖了数据），原版会把技能变成 BackDash，
            // 这里再转一次 —— BackDash 自己的 commonProps.item 就是 "Dash"，所以一次即可回到 Dash。
            try
            {
                var hero = self.owner as Hero;
                if (hero != null && !hero.destroyed && hero._level != null && permId >= 0)
                {
                    var item = hero.inventory?.getByPermanentId(permId);
                    var data = item?._itemData;
                    if (data != null)
                    {
                        string id = data.id?.ToString();
                        if (id == "BackDash")
                        {
                            if (!_loggedBackDashFallback)
                            {
                                _loggedBackDashFallback = true;
                                Logger.Warning("[DashOverhaul] 冲刺结束后技能是 BackDash —— 说明 res.pak 数据补丁没生效，已用运行时兜底转回 Dash");
                            }
                            hero.transformInventoryItem(item, null);
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] BackDash 兜底失败"); }
        }

        // ==================================================================
        // 工具
        // ==================================================================

        /// <summary>读 Dash 行（已经过数据补丁）的 props：speed(格/帧) / range(格) / power。</summary>
        private void ReadDashProps(Dash dash, DashState st)
        {
            st.Speed = DefaultSpeedCases;
            st.MaxDist = DefaultRangeCases;
            st.Power = null;

            try
            {
                var data = dash.item?._itemData;
                if (data != null)
                {
                    var props = data.props;
                    if (props != null)
                    {
                        if (props.speed is double sp && sp > 0.0) st.Speed = sp;
                        if (props.range is double rg && rg > 0.0) st.MaxDist = rg;
                        st.Power = props.power;
                        if (st.Power != null) return;
                    }
                }
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] 读取 Dash props 失败"); }

            // 兜底：直接按 id 从数据表里取
            try
            {
                dynamic byId = Data.Class.item.byId;
                dynamic d = byId.get(ToHaxeString("Dash"));
                if (d != null)
                {
                    dynamic props = d.props;
                    if (props.speed != null)
                    {
                        double sp = (double)props.speed;
                        if (sp > 0.0) st.Speed = sp;
                    }
                    if (props.range != null)
                    {
                        double rg = (double)props.range;
                        if (rg > 0.0) st.MaxDist = rg;
                    }
                    st.Power = (object)props.power;
                }
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] 兜底读取 Dash props 失败"); }
        }

        /// <summary>
        /// 打一次 Dash 行的实际数值，用来判断数据补丁到底有没有生效
        /// （castCD 应为 0，power 应为 [600]，commonProps.item 应为 Dash）。
        /// </summary>
        private void LogDashDataOnce(string phase)
        {
            if (_loggedDashData) return;
            try
            {
                dynamic byId = Data.Class.item.byId;
                dynamic d = byId.get(ToHaxeString("Dash"));
                if (d == null)
                {
                    Logger.Warning($"[DashOverhaul] {phase}：Data.item.byId 里找不到 Dash");
                    return;
                }
                _loggedDashData = true;
                Logger.Information($"[DashOverhaul] {phase} 的 Dash 数据：castCD={d.castCD} " +
                                   $"power={d.props.power} commonProps.item={d.commonProps.item} " +
                                   $"speed={d.props.speed} range={d.props.range}");
            }
            catch (Exception ex) { Logger.Error(ex, "[DashOverhaul] 读取 Dash 数据失败"); }
        }

        private static double CaseX(Hero hero)
        {
            return hero.cx + hero.xr;
        }

        private static double CaseY(Hero hero)
        {
            return hero.cy + hero.yr;
        }

        /// <summary>直接按格位移（绕过碰撞求解 = 无视墙体），并保持 xr/yr 落在 [0,1)。</summary>
        private static void MoveBy(Hero hero, double ddx, double ddy)
        {
            double nx = hero.cx + hero.xr + ddx;
            int ncx = (int)SysMath.Floor(nx);
            hero.cx = ncx;
            hero.xr = nx - ncx;

            double ny = hero.cy + hero.yr + ddy;
            int ncy = (int)SysMath.Floor(ny);
            hero.cy = ncy;
            hero.yr = ny - ncy;
        }

        private static bool KeyDown(int vk)
        {
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
