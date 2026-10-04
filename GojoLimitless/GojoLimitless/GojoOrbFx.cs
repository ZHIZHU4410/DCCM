#nullable disable
using System;
using dc;
using dc.en;
using dc.en.bu;
using dc.tool;

namespace GojoLimitless
{
    /// <summary>
    /// 「虚式·茈」额外叠的一层表现：召唤原版**死亡之球**（<c>dc.en.bu.Orb</c>，也就是 SlowOrb），
    /// 但**不往前飞、也不从小长到大** —— 一出场就是 5 倍大小，显示一小段时间后消失。
    ///
    /// ============================ 关键：大小由 radius 决定，不是 spr ============================
    /// 一开始我按 <c>spr.scaleX/scaleY</c> 去放大，实机**完全没反应**。查反编译才找到真正的原因：
    ///
    /// <code>
    /// // GamePseudocode/dc.en.bu/Orb.cs:467  （postUpdate 里每帧画球）
    /// fx.orb(x, y, dx * 24.0, base.radius, color);
    ///
    /// // GamePseudocode/dc/Fx.cs:153160
    /// public unsafe void orb(double x, double y, double dx, double radius, int c)
    /// </code>
    ///
    /// 也就是说：**球的视觉半径就是 <c>base.radius</c>**（第 4 个参数），
    /// 而 <c>spr</c> 根本没参与绘制（<c>Orb.initGfx</c> 里那个 <c>SpriteLib</c> 字面量就是 null）。
    /// 仓库里 <c>SlowOrbBlackHole</c> 放大黑洞用的也正是 <c>orb.radius</c>
    /// （<c>SlowOrbBlackHoleMain.cs:338  orb.radius = st.BaseRadius * scale;</c>）。
    ///
    /// 所以现在改成：**一生成就把 radius 设成基础的 5 倍**，不再做任何插值生长。
    ///
    /// 其余保持：
    ///   · <c>speed = 0 / dx = 0</c> —— 原地不动；
    ///   · <see cref="LifeSeconds"/> 秒后 <c>destroy()</c> 收掉。
    /// </summary>
    public static class GojoOrbFx
    {
        /// <summary>原版死亡之球的物品 id（和 SlowOrbBlackHole 用的同一个）。</summary>
        private const string OrbItemId = "SlowOrb";

        /// <summary>显示时长（秒）。</summary>
        private const double LifeSeconds = 0.3;

        /// <summary>放大倍数 —— 一出场就是这个大小。</summary>
        private const double ScaleFactor = 5.0;

        /// <summary>
        /// 万一读不到原始 radius，用这个兜底（原版 SlowOrb 的 radius = props.size(2) × 24 = 48）。
        /// </summary>
        private const double FallbackBaseRadius = 48.0;

        // ---------------------------------------------------------------- 运行时状态

        private static Orb _orb;
        private static double _age;
        private static bool _active;

        /// <summary>原始（未放大）的半径，用来算 5 倍。</summary>
        private static double _baseRadius;

        private static bool _warned;
        private static string _warnedReason;

        /// <summary>是否已经打过一次诊断。</summary>
        private static bool _diagDone;

        /// <summary>当前有没有正在显示的死亡之球。</summary>
        public static bool IsActive => _active && _orb != null && !_orb.destroyed;

        // ---------------------------------------------------------------- 对外接口

        /// <summary>
        /// 在英雄当前位置召唤一个"五倍大小、原地不动"的死亡之球。
        /// 重复调用会先把上一个收掉（不叠加）。
        /// </summary>
        public static void Spawn(Hero hero)
        {
            if (hero == null || hero.destroyed) return;

            Kill();   // 先收掉上一个

            try
            {
                // 1) 造临时物品条目：Orb 的构造函数要读 item._itemData
                InventItem item = MakeOrbItem();
                if (item == null)
                {
                    WarnOnce("造不出 SlowOrb 的临时 InventItem（数据表里没有这个 id？）");
                    return;
                }

                // 2) 照抄 dc.pow._SlowOrb 的生成方式：new Orb(owner, item).init()
                Orb orb = new Orb(hero, item);
                if (orb == null) { WarnOnce("new Orb(...) 返回 null"); return; }

                orb.init();

                // 3) 记下原始半径（init 之后才有效），然后**直接**放到 5 倍
                double baseRadius = FallbackBaseRadius;
                try
                {
                    double r = orb.radius;
                    if (r > 0.0) baseRadius = r;
                }
                catch { }
                _baseRadius = baseRadius;

                try { orb.radius = baseRadius * ScaleFactor; } catch { }

                // 4) 原地不动
                try { orb.speed = 0.0; } catch { }
                try { orb.dx = 0.0; } catch { }
                try { orb.dy = 0.0; } catch { }
                try { orb.maxDist = 0.0; } catch { }

                // 5) 放到英雄身上（实体坐标就是格 cx/cy）
                try
                {
                    orb.cx = hero.cx;
                    orb.cy = hero.cy;
                }
                catch { }

                _orb = orb;
                _age = 0.0;
                _active = true;

                DiagOnce(orb, baseRadius);
            }
            catch (Exception ex)
            {
                WarnOnce("召唤死亡之球失败: " + ex.Message);
                _active = false;
                _orb = null;
            }
        }

        /// <summary>
        /// 每帧推进。因为"一出场就是 5 倍"，这里只负责
        /// **按住它别跑**（原版每帧会推它）和**到点收掉**。
        /// </summary>
        public static void Update(double dt)
        {
            if (!_active) return;

            try
            {
                if (_orb == null || _orb.destroyed)
                {
                    _active = false;
                    _orb = null;
                    return;
                }

                _age += dt;

                // 原版每帧都在动 / 在缩，这里持续按住
                try { _orb.speed = 0.0; } catch { }
                try { _orb.dx = 0.0; } catch { }
                // 防止有任何逻辑把它缩回去
                try
                {
                    double want = _baseRadius * ScaleFactor;
                    if (_orb.radius < want) _orb.radius = want;
                }
                catch { }

                if (_age >= LifeSeconds) Kill();
            }
            catch (Exception ex)
            {
                WarnOnce("推进死亡之球失败: " + ex.Message);
                Kill();
            }
        }

        /// <summary>立刻收掉（换关 / 卸载 / 重新召唤时调）。</summary>
        public static void Kill()
        {
            _active = false;
            _age = 0.0;

            Orb orb = _orb;
            _orb = null;
            if (orb == null) return;

            try { if (!orb.destroyed) orb.destroy(); } catch { }
        }

        /// <summary>换关 / 卸载时复位。</summary>
        public static void Reset()
        {
            Kill();
            _warned = false;
            _warnedReason = null;
        }

        // ---------------------------------------------------------------- 内部

        /// <summary>
        /// 造一个 id = <c>SlowOrb</c> 的临时物品条目（只用来喂 <c>Orb</c> 的构造函数，
        /// 决定配色 / 伤害等；不进背包）。
        /// </summary>
        private static InventItem MakeOrbItem()
        {
            try
            {
                var kind = new InventItemKind.Active(GojoUtil.Hs(OrbItemId));
                return new InventItem(kind);
            }
            catch (Exception ex)
            {
                Log.Warn("构造 InventItem 失败: " + ex.Message);
                return null;
            }
        }

        /// <summary>第一次召唤时把关键数值摊开（排查用，只会打一条）。</summary>
        private static void DiagOnce(Orb orb, double baseRadius)
        {
            if (_diagDone) return;
            _diagDone = true;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("基础半径=").Append(baseRadius.ToString("0.#"))
                  .Append(" → 已设为 ").Append((baseRadius * ScaleFactor).ToString("0.#"));

                try { sb.Append(" | 实际 radius=").Append(orb.radius.ToString("0.#")); } catch { }
                try { sb.Append(" | destroyed=").Append(orb.destroyed); } catch { }
                try { sb.Append(" | cx=").Append(orb.cx).Append(" cy=").Append(orb.cy); } catch { }
                try { sb.Append(" | speed=").Append(orb.speed.ToString("0.##")); } catch { }
                try { sb.Append(" | item=").Append(orb.item == null ? "null" : "有"); } catch { }
                try { sb.Append(" | spr=").Append(orb.spr == null ? "null(正常，绘制走 fx.orb)" : "有"); } catch { }

                Log.Info("[茈·死亡之球] 诊断: " + sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Warn("[茈·死亡之球] 诊断失败: " + ex.Message);
            }
        }

        private static void WarnOnce(string reason)
        {
            if (_warned && _warnedReason == reason) return;
            _warned = true;
            _warnedReason = reason;
            Log.Warn("[茈·死亡之球] " + reason);
        }
    }
}
