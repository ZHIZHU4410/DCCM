#nullable disable
using System;
using dc;
using dc.en;
using dc.h2d;

using SysMath = System.Math;

namespace GojoLimitless
{
    /// <summary>
    /// 领域展开「无量空处」的**氛围层** —— 领域持续期间一直存在，直到领域结束才消失。
    ///
    /// ============================ 它做了什么 ============================
    ///
    /// 1) **开场**：全屏白 → 紫色闪光（约 0.45 秒），把"展开"这一下打出来。
    ///
    /// 2) **环境改变**（持续到领域结束）：
    ///    · 全屏压一层紫色（<c>Multiply</c> 混合）—— 整个场景的色调被拉进领域里；
    ///    · 四周一圈**暗紫晕影**（中心透明、边缘最重）—— 视野被"收进"领域；
    ///    · 全屏紫色轻微**脉动**，像领域在呼吸。
    ///
    /// 3) **炫酷细节**（叠加在环境层上面）：
    ///    · 以英雄为中心、不断向外扩散的**虚空环**；
    ///    · 缓慢旋转的**空间裂纹**（6 条 + 一层反向 12 条）；
    ///    · 英雄脚下旋转的**光环**；
    ///    · 偶尔掠过全屏的一条**扫过光带**（每 1.6 秒一次）。
    ///
    /// ============================ 怎么画的 ============================
    ///
    /// 照搬仓库里 <c>EchoVision</c> 那套（它是目前唯一在实机上稳定画全屏叠加层的模组）：
    ///   * 在 <c>level.scroller</c> 上挂一个 <c>dc.h2d.Object</c> 当根节点，
    ///     放在 <c>DP_CTX_UI</c> 层 —— 压在场景与已有特效之上、UI 之下；
    ///   * 每帧 <c>clear()</c> 后按**摄像机可视矩形**（<c>level.viewport.realX/realY/wid/hei</c>）
    ///     重画，所以角色怎么跑，覆盖都在正确位置；
    ///   * 顶点颜色 / alpha 直接走 <c>Graphics.addVertex</c>，才能做出渐变。
    ///
    /// ⚠️ 全程 try/catch：氛围层画不出来也绝不影响领域本身的定身 / 伤害 / 无敌。
    /// </summary>
    public static class GojoDomainFx
    {
        private const double TilePx = 24.0;

        /// <summary>开场闪光时长（秒）。</summary>
        private const double OpeningFlash = 0.45;

        /// <summary>虚空环每波间隔（秒）。</summary>
        private const double RingInterval = 0.55;

        /// <summary>扫过光带间隔（秒）。</summary>
        private const double SweepInterval = 1.6;

        /// <summary>同时存在的虚空环数量（循环复用）。</summary>
        private const int RingCount = 4;

        // ---------------------------------------------------------------- 运行时状态

        private static dc.h2d.Object _root;
        private static Graphics _g;
        private static object _levelKey;

        private static bool _active;
        private static double _time;          // 领域已经持续了多久
        private static double _duration;      // 领域总时长

        // ---------------------------------------------------------------- 对外接口

        /// <summary>领域是否正在（或即将）驱动氛围层。</summary>
        public static bool IsActive => _active;

        /// <summary>
        /// 领域开始时调一次。<paramref name="duration"/> 是总时长（用于开场闪光的配比）。
        /// </summary>
        public static void Begin(Hero hero, double duration)
        {
            _active = true;
            _time = 0.0;
            _duration = SysMath.Max(0.5, duration);
            try { EnsureLayer(hero); } catch { }
        }

        /// <summary>每帧推进 + 重画。领域结束后不再调用。</summary>
        public static void Update(Hero hero, double dt)
        {
            if (!_active) return;

            try
            {
                _time += dt;

                if (!EnsureLayer(hero)) return;

                Graphics g = _g;
                if (g == null) return;

                // 摄像机可视矩形（世界坐标，像素）
                ViewRect(hero, out double vx, out double vy, out double vw, out double vh);

                g.clear();

                double t = _time;
                double k = DrawScale(hero);      // 世界单位 → 屏幕观感的倍率

                // ---- 2) 环境改变：全屏紫色 + 四周晕影 + 脉动 ----
                DrawEnvironment(g, vx, vy, vw, vh, t);

                // ---- 3) 炫酷细节（画在环境层之上）----
                // ⚠️ 中心一律用 GojoUtil.CenterPx（实机验证过的实体中心换算）
                HeroCenter(hero, out double hx, out double hy);

                DrawRadialGlow(g, hx, hy, k);
                DrawRotatingCracks(g, hx, hy, t, k);
                DrawExpandingRings(g, hx, hy, t, k);
                DrawZoneBoundaries(g, hx, hy, k);
                DrawHeroHalo(g, hx, hy, t, k);
                DrawSweep(g, vx, vy, vw, vh, t);

                // ---- 1) 开场闪光（最上层）----
                DrawOpeningFlash(g, vx, vy, vw, vh, t);
            }
            catch
            {
                // 氛围层出错就静默放弃，不影响玩法
            }
        }

        /// <summary>领域结束时调一次：立刻清掉所有氛围，恢复原样。</summary>
        public static void End()
        {
            _active = false;
            _time = 0.0;
            try
            {
                if (_g != null) _g.clear();
            }
            catch { }
        }

        /// <summary>换关 / 换英雄 / 卸载时调：连图层一起丢掉，下次重建。</summary>
        public static void Reset()
        {
            _active = false;
            _time = 0.0;

            try { if (_g != null) _g.clear(); } catch { }

            try
            {
                if (_root != null)
                {
                    if (_root.parent != null) _root.parent.removeChild(_root);
                    else _root.remove();
                }
            }
            catch { }

            _root = null;
            _g = null;
            _levelKey = null;
        }

        // ---------------------------------------------------------------- 图层管理

        /// <summary>确保叠加层挂在当前关卡的 scroller 上（换关会自动重建）。</summary>
        private static bool EnsureLayer(Hero hero)
        {
            try
            {
                if (hero == null || hero._level == null || hero._level.scroller == null) return false;

                object key = hero._level;
                if (_root != null && ReferenceEquals(_levelKey, key)) return true;

                Reset();
                _levelKey = key;

                _root = new dc.h2d.Object(hero._level.scroller);

                // ⚠️ 图层：**DP_ROOM_MAIN_BACK** —— "英雄/怪物身后、房间背景之前"。
                //
                // 参考 ChronoBlade（刻刻帝身后背景）的结论，ChronoFx.cs:904-907：
                //   ★ 关键在图层：DP_ROOM_MAIN_BACK 比实体用的 DP_ROOM_MAIN **低一层**，
                //     也就是"英雄/怪物身后、房间背景之前"——原版宠物 Owl 也挂在这一层。
                //     挂在 DP_ROOM_MAIN 会和英雄同层，谁在前取决于添加顺序，不可靠。
                //
                // 层级顺序（UnnamedFunctions.cs:31563+ 按 obj15._uniq 递增分配，
                // 声明顺序即前后顺序）：
                //
                //   0 DP_BACKGROUND      最底：纯背景色
                //   1 DP_ROOM_WALLS_BG   ← 我上一版误挂这里：会被 2~7 层全部盖掉，等于白画
                //   2 DP_ROOM_WALLS
                //   3 DP_ROOM_WALLS_FX
                //   4 DP_ROOM_BACK_DECO
                //   5 DP_ROOM_BACK
                //   6 DP_ROOM_BACK_FX
                //   7 DP_ROOM_MAIN_BACK  ← ✅ 这里：一切背景之后、英雄/怪物之前
                //   8 DP_ROOM_MAIN
                //   9 DP_ROOM_MAIN_HERO      英雄 / 怪物
                //  ...
                //  17 DP_CTX_UI          ← 最初误挂这里：盖住了所有人
                //
                // 所以这一层同时满足两件事：**看得见** + **不遮挡英雄/怪物**。
                int layer = 7;
                try { layer = dc.Const.Class.DP_ROOM_MAIN_BACK; }
                catch
                {
                    try { layer = dc.Const.Class.DP_ROOM_BACK_FX; } catch { }
                }

                // ⚠️ 绝对不能传负数给 addChildAt：
                //    h2d.Layers.addChildAt 里用的是**无符号**比较
                //    （Layers.cs:73  `if ((uint)layer >= (uint)num)`），
                //    -1 会被当成 4294967295 走进异常分支。
                if (layer < 0) layer = 0;

                try { hero._level.scroller.addChildAt(_root, layer); } catch { }

                _root.x = 0.0;
                _root.y = 0.0;

                _g = new Graphics(_root);
                return _g != null;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- 几何 / 颜色工具

        /// <summary>
        /// 摄像机当前可视矩形（世界坐标 + 像素）。
        ///
        /// ⚠️ <c>viewport.realX/realY</c> 是**摄像机中心**，不是左上角。
        /// 依据（<c>GamePseudocode/dc.pr/Level.cs:13131-13143</c>）：
        /// <code>
        /// scroller.x = (0 - (viewport.realX - viewport.wid * 0.5)) * scroller.scaleX;
        /// scroller.y = (0 - (viewport.realY - viewport.hei * 0.5)) * scroller.scaleY;
        /// </code>
        /// 展开就是"屏幕中心 = 世界 realX/realY"。所以可视矩形要**以它为中心**往两边摊开。
        /// （一开始我当成左上角用了，导致整个覆盖区域偏移半屏。）
        /// </summary>
        private static void ViewRect(Hero hero, out double cx, out double cy, out double w, out double h)
        {
            cx = hero != null ? (hero.cx + hero.xr) * TilePx : 0.0;
            cy = hero != null ? (hero.cy + hero.yr) * TilePx : 0.0;
            w = 480.0;
            h = 270.0;

            try
            {
                if (hero?._level?.viewport != null)
                {
                    var vp = hero._level.viewport;
                    cx = vp.realX;
                    cy = vp.realY;
                    w = vp.wid;
                    h = vp.hei;
                    if (w <= 32.0) w = 480.0;
                    if (h <= 32.0) h = 270.0;
                }
            }
            catch { }

            // 外扩一圈，避免边缘露白
            double padX = w * 0.30;
            double padY = h * 0.30;

            // 以摄像机中心为准，摊成"左上角 + 宽高"
            w += padX * 2.0;
            h += padY * 2.0;
            cx -= w * 0.5;
            cy -= h * 0.5;
        }

        /// <summary>英雄中心（像素）。直接复用已经在实机验证过的 <see cref="GojoUtil.CenterPx"/>。</summary>
        private static void HeroCenter(Hero hero, out double x, out double y)
        {
            if (!GojoUtil.CenterPx(hero, out x, out y))
            {
                x = 0.0;
                y = 0.0;
            }
        }

        /// <summary>
        /// 世界单位 → 绘制单位的缩放。
        ///
        /// <c>scroller.scaleX = pixelScale * viewport.zoom</c>（<c>Level.cs:13121-13124</c>），
        /// 也就是 h2d 场景把世界坐标放大到屏幕像素的那个倍率。
        /// 我们在 scroller 里画东西，**顶点坐标是"世界单位"**，但尺寸/alpha 之类按视觉来给的
        /// 数值要乘这个倍率，才和实际屏幕观感一致（否则高分辨率下所有光圈都偏小一半）。
        /// </summary>
        private static double DrawScale(Hero hero)
        {
            try
            {
                double s = hero._level.scroller.scaleX;
                if (s > 0.05 && s < 8.0) return s;
            }
            catch { }
            return 1.0;
        }

        private static void Rgb(int color, out double r, out double g, out double b)
        {
            r = ((color >> 16) & 255) / 255.0;
            g = ((color >> 8) & 255) / 255.0;
            b = (color & 255) / 255.0;
        }

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        /// <summary>开一个填充块（颜色由每个顶点自带，这里只负责进入填充状态）。</summary>
        private static void Fill(Graphics g)
        {
            int c = 0xFFFFFF;
            double a = 1.0;
            g.beginFill(ref c, ref a);
        }

        private static void Vert(Graphics g, double x, double y, double r, double gr, double b, double a)
        {
            double u = 0.0, v = 0.0;
            g.addVertex(x, y, r, gr, b, a, ref u, ref v);
        }

        /// <summary>一个填充四边形（颜色 + 每顶点 alpha 自由度最高，渐变都靠它）。</summary>
        private static void Quad(Graphics g,
                                 double x1, double y1, double a1,
                                 double x2, double y2, double a2,
                                 double x3, double y3, double a3,
                                 double x4, double y4, double a4,
                                 int color)
        {
            Rgb(color, out double r, out double gr, out double bb);

            // 两个三角形拼成四边形
            Vert(g, x1, y1, r, gr, bb, Clamp01(a1));
            Vert(g, x2, y2, r, gr, bb, Clamp01(a2));
            Vert(g, x3, y3, r, gr, bb, Clamp01(a3));

            Vert(g, x1, y1, r, gr, bb, Clamp01(a1));
            Vert(g, x3, y3, r, gr, bb, Clamp01(a3));
            Vert(g, x4, y4, r, gr, bb, Clamp01(a4));
        }

        /// <summary>实心矩形（单一 alpha）。</summary>
        private static void Rect(Graphics g, double x, double y, double w, double h, int color, double alpha)
        {
            Fill(g);
            Quad(g,
                 x, y, alpha,
                 x + w, y, alpha,
                 x + w, y + h, alpha,
                 x, y + h, alpha,
                 color);
            g.endFill();
        }

        /// <summary>带内外 alpha 的一圈环形（用 <paramref name="segments"/> 段四边形逼近）。</summary>
        private static void Ring(Graphics g, double cx, double cy, double r0, double r1,
                                 int color, double a0, double a1, int segments)
        {
            if (r1 <= 1.0 || (a0 <= 0.001 && a1 <= 0.001)) return;

            if (r0 < 1.0) r0 = 1.0;
            if (r1 <= r0) return;

            Fill(g);
            for (int i = 0; i < segments; i++)
            {
                double t0 = i * 6.283185307179586 / segments;
                double t1 = (i + 1) * 6.283185307179586 / segments;

                double c0 = SysMath.Cos(t0), s0 = SysMath.Sin(t0);
                double c1 = SysMath.Cos(t1), s1 = SysMath.Sin(t1);

                Quad(g,
                     cx + c0 * r0, cy + s0 * r0, a0,
                     cx + c1 * r0, cy + s1 * r0, a0,
                     cx + c1 * r1, cy + s1 * r1, a1,
                     cx + c0 * r1, cy + s0 * r1, a1,
                     color);
            }
            g.endFill();
        }

        // ---------------------------------------------------------------- 各个视觉层

        /// <summary>环境改变：全屏紫色压色 + 四周暗紫晕影 + 呼吸脉动。</summary>
        private static void DrawEnvironment(Graphics g, double vx, double vy, double vw, double vh, double t)
        {
            // 呼吸：0.65 ~ 0.90 之间缓慢起伏
            double breathe = 0.775 + 0.125 * SysMath.Sin(t * 1.7);

            // (a) 全屏紫色压色 —— 整个场景的色调被领域拉走
            Rect(g, vx, vy, vw, vh, GojoPalette.Void, 0.20 * breathe + 0.06);

            // (b) 四周晕影：中心透明 → 边缘最重，把视野"收进"领域
            double inset = SysMath.Min(vw, vh) * 0.16;
            double depth = 0.52 * breathe;

            // 上
            Fill(g);
            Quad(g, vx, vy, depth, vx + vw, vy, depth, vx + vw, vy + inset, 0.0, vx, vy + inset, 0.0, 0x120826);
            g.endFill();
            // 下
            Fill(g);
            Quad(g, vx, vy + vh - inset, 0.0, vx + vw, vy + vh - inset, 0.0,
                 vx + vw, vy + vh, depth, vx, vy + vh, depth, 0x120826);
            g.endFill();
            // 左
            Fill(g);
            Quad(g, vx, vy, depth, vx + inset, vy, 0.0, vx + inset, vy + vh, 0.0, vx, vy + vh, depth, 0x120826);
            g.endFill();
            // 右
            Fill(g);
            Quad(g, vx + vw - inset, vy, 0.0, vx + vw, vy, depth, vx + vw, vy + vh, depth,
                 vx + vw - inset, vy + vh, 0.0, 0x120826);
            g.endFill();
        }

        /// <summary>缓慢旋转的空间裂纹（正反两层）。</summary>
        private static void DrawRotatingCracks(Graphics g, double hx, double hy, double t, double k)
        {
            double baseR = 7.0 * TilePx * k;
            double breathe = 0.75 + 0.25 * SysMath.Sin(t * 2.1);

            DrawSpokes(g, hx, hy, baseR * 0.45, baseR * 1.75, 6, t * 0.35, GojoPalette.Void,
                       0.30 * breathe, 20.0 * k);
            DrawSpokes(g, hx, hy, baseR * 0.90, baseR * 2.30, 12, -t * 0.22 + 0.26, GojoPalette.Purple,
                       0.18 * breathe, 12.0 * k);
        }

        /// <summary>从中心向外辐射的若干条细光带。</summary>
        private static void DrawSpokes(Graphics g, double cx, double cy, double r0, double r1,
                                       int count, double rot, int color, double alpha, double halfWidth)
        {
            if (alpha <= 0.005) return;

            Fill(g);
            for (int i = 0; i < count; i++)
            {
                double a = rot + i * (6.283185307179586 / count);
                double ca = SysMath.Cos(a), sa = SysMath.Sin(a);

                // 垂直于射线方向的偏移，把"线"做成细长四边形
                double nx = -sa * halfWidth;
                double ny = ca * halfWidth;

                Quad(g,
                     cx + ca * r0 + nx, cy + sa * r0 + ny, 0.0,
                     cx + ca * r1 + nx, cy + sa * r1 + ny, alpha,
                     cx + ca * r1 - nx, cy + sa * r1 - ny, alpha,
                     cx + ca * r0 - nx, cy + sa * r0 - ny, 0.0,
                     color);
            }
            g.endFill();
        }

        /// <summary>以英雄为中心不断向外扩散的虚空环。</summary>
        private static void DrawExpandingRings(Graphics g, double hx, double hy, double t, double k)
        {
            double maxR = 9.0 * TilePx * k;

            for (int i = 0; i < RingCount; i++)
            {
                // 每一波错开 RingInterval
                double age = (t + i * RingInterval) % (RingInterval * RingCount);
                double f = age / (RingInterval * RingCount);      // 0 → 1

                double r = maxR * f;
                if (r < 8.0 * k) continue;

                // 越扩越淡
                double a = 0.38 * (1.0 - f);
                if (a <= 0.01) continue;

                double thick = (6.0 + 26.0 * f) * k;
                Ring(g, hx, hy, r, r + thick, GojoPalette.SixEyes, a, 0.0, 40);
            }
        }

        /// <summary>
        /// **内外圈分界线** —— 让"外圈不伤害 / 内圈伤害"看得见。
        ///
        /// 外圈：<c>Radius</c>（默认 22 格），画一圈很淡的边界 + 慢慢转的刻度；
        /// 内圈：<c>InnerRadius</c>（默认 7 格），画一圈明显更亮、脉动的边界，
        ///       再加一层很淡的填充，让"伤害区"一眼可辨。
        /// </summary>
        private static void DrawZoneBoundaries(Graphics g, double hx, double hy, double k)
        {
            Configs cfg = Cfg.V;
            if (cfg?.Domain == null) return;

            double outerTiles = cfg.Domain.Radius;
            double innerTiles = cfg.Domain.InnerRadius;

            if (double.IsNaN(innerTiles) || innerTiles <= 0.0) innerTiles = 7.0;
            if (innerTiles >= outerTiles) innerTiles = SysMath.Max(1.0, outerTiles * 0.9);

            double outerR = outerTiles * TilePx * k;
            double innerR = innerTiles * TilePx * k;

            // (a) 内圈：淡淡的填充，标记"会掉血的区域"
            Ring(g, hx, hy, 1.0, innerR, GojoPalette.Void, 0.10, 0.035, 48);

            // (b) 内圈边界：亮、并且脉动
            double pulse = 0.55 + 0.45 * SysMath.Sin(_time * 4.2);
            Ring(g, hx, hy, innerR - 3.0 * k, innerR + 3.0 * k,
                 GojoPalette.SixEyes, 0.55 * pulse, 0.55 * pulse, 48);

            // (c) 外圈边界：很淡，只做"领域范围"的提示
            Ring(g, hx, hy, outerR - 2.0 * k, outerR + 2.0 * k,
                 GojoPalette.Purple, 0.20, 0.20, 56);
        }

        /// <summary>英雄脚下的旋转光环（多层反向）。</summary>
        private static void DrawHeroHalo(Graphics g, double hx, double hy, double t, double k)
        {
            double breathe = 0.7 + 0.3 * SysMath.Sin(t * 3.0);

            Ring(g, hx, hy, 52.0 * k, (52.0 + 7.0) * k, GojoPalette.Infinity, 0.0, 0.42 * breathe, 36);
            Ring(g, hx, hy, 74.0 * k, (74.0 + 4.0) * k, GojoPalette.Void, 0.30 * breathe, 0.0, 36);
            Ring(g, hx, hy, 96.0 * k, (96.0 + 3.0) * k, GojoPalette.Purple, 0.0, 0.24 * breathe, 36);
        }

        /// <summary>
        /// 以英雄为中心的**径向辉光**：一圈圈由内到外递减的圆环，叠出平滑的光晕。
        ///
        /// 图层已经挪到最底层，所以这层辉光是"从背景里透出来"的效果 ——
        /// 墙壁、怪物、人物、物品都会正常盖在它上面。
        /// </summary>
        private static void DrawRadialGlow(Graphics g, double hx, double hy, double k)
        {
            double breathe = 0.8 + 0.2 * SysMath.Sin(_time * 1.9);

            const int steps = 14;
            double maxR = 10.0 * TilePx * k;
            double inner = 1.5 * TilePx * k;

            for (int i = 0; i < steps; i++)
            {
                double f0 = (double)i / steps;
                double f1 = (double)(i + 1) / steps;

                double r0 = inner + (maxR - inner) * f0;
                double r1 = inner + (maxR - inner) * f1;

                // 中心亮、往外迅速衰减
                double a = 0.075 * breathe * (1.0 - f1) * (1.0 - f1);
                if (a <= 0.002) continue;

                Ring(g, hx, hy, r0, r1, GojoPalette.Void, a, a, 28);
            }
        }

        /// <summary>每 1.6 秒从一侧扫过全屏的一条光带。</summary>
        private static void DrawSweep(Graphics g, double vx, double vy, double vw, double vh, double t)
        {
            double phase = (t % SweepInterval) / SweepInterval;      // 0 → 1
            if (phase > 0.55) return;                                 // 后半段留白

            double f = phase / 0.55;                                  // 0 → 1
            double x = vx - vw * 0.25 + vw * 1.5 * f;

            double bandW = vw * 0.16;
            double a = 0.30 * SysMath.Sin(f * 3.141592653589793);

            Fill(g);
            Quad(g,
                 x, vy, 0.0,
                 x + bandW, vy, a,
                 x + bandW, vy + vh, a,
                 x, vy + vh, 0.0,
                 GojoPalette.SixEyes);
            g.endFill();
        }

        /// <summary>开场：全屏白闪 → 紫闪，0.45 秒内衰减到 0。</summary>
        private static void DrawOpeningFlash(Graphics g, double vx, double vy, double vw, double vh, double t)
        {
            if (t >= OpeningFlash) return;

            double k = t / OpeningFlash;                 // 0 → 1
            double a = SysMath.Pow(1.0 - k, 2.2);        // 快速衰减

            // 前 40% 走白，之后走紫
            int color = k < 0.4 ? 0xFFFFFF : GojoPalette.Void;
            double alpha = color == 0xFFFFFF ? a * 0.75 : a * 0.55;

            Rect(g, vx, vy, vw, vh, color, alpha);
        }
    }
}
