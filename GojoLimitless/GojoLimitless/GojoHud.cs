#nullable disable
using System;
using dc;
using dc.en;
using dc.h2d;
using dc.hl.types;
using dc.pr;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;

using SysMath = System.Math;

namespace GojoLimitless
{
    /// <summary>
    /// 内置 HUD：左上角几行文字 + 一排字符进度条。
    ///
    /// ============================ 定位方式（v5 最终方案） ============================
    /// 20 行 × 2 个 <c>dc.h2d.Text</c>（1px 阴影 + 正文）**各自直接挂到 <c>level.root</c>
    /// 的 UI 层**，每行用**绝对坐标**定位 —— 和 <c>KillSwapWeapon</c> / <c>CameraMod</c>
    /// 一样，是仓库里验证过能正常渲染的写法。
    ///
    /// 期间试过"整块 HUD 挂一个 <c>dc.h2d.Object</c> 父容器、只缩放容器"的方案，
    /// **实机证明在这套 hashlink 代理下走不通**（详见 <see cref="ApplyLayout"/> 的注释）：
    /// 子节点的绝对变换不会被重算，20 行会全叠在同一处。所以回退到这个方案。
    ///
    /// 缩放问题的处理（原需求"放大后不要叠层"）：
    ///   · 每行文字自己 <c>scaleX/scaleY = scale</c>；
    ///   · **行距同时乘以 scale**，所以字号变大时行距按同比例变大，行与行不会互相压住；
    ///   · 阴影偏移固定 1.0 屏幕像素（`shadow = 正文 + 1`），不乘 scale，
    ///     放大后阴影仍是细边，不会糊成一片。
    ///
    /// ⚠️ Text 对象只在**关卡变化**时建一次，之后永不 removeChild
    /// （只清空文字 + 逐行 visible），所以"拖动缩放滑条时重建出一堆残影"那条老路也彻底断了。
    ///
    /// ⚠️ 只在**文本内容变了**的时候才 set_text —— 每帧重设会触发排版重算。
    /// </summary>
    public static class GojoHud
    {
        private const int Lines = 8;
        private const int LineTitle = 0;
        private const int LineInfinity = 1;
        private const int LineAbility0 = 2;      // 苍 / 赤 / 茈 / 领域 依次占 2..5
        private const int LineStatus = 6;
        private const int LineCombo = 7;

        /// <summary>HUD 自己的固定行 + 可选的日志行。</summary>
        private static int TotalLines => Lines + LogLineCount;

        private static int LogLineCount
        {
            get
            {
                try
                {
                    UiCfg ui = Cfg.V.Ui;
                    if (ui == null || !ui.HudShowLog) return 0;
                    int n = ui.HudLogLines;
                    if (n < 0) n = 0;
                    if (n > 12) n = 12;
                    return n;
                }
                catch { return 0; }
            }
        }

        // 配色（0xRRGGBB）
        private const int ColShadow = 0x000000;
        private const int ColTitle = 0xAEF4FF;
        private const int ColText = 0xE8F6FF;
        private const int ColDim = 0x8FA6B4;
        private const int ColReady = 0x6CFF8A;
        private const int ColCooling = 0xFFC24A;
        private const int ColDanger = 0xFF5A3C;
        private const int ColBlue = 0x4FA8FF;
        private const int ColRed = 0xFF6A4A;
        private const int ColPurple = 0xC48CFF;
        private const int ColVoid = 0x9A7CFF;
        private const int ColDisabled = 0x6B6B6B;

        private const int BarCells = 12;

        /// <summary>
        /// 字号基准：原版 <c>font12</c> 的行高。配置里的 <c>HudLineHeight</c> 就是按它算的
        /// （默认 16 → 行距系数 1.0，和 v4 的观感一致）。
        /// </summary>
        private const double BaseLineHeight = 16.0;

        /// <summary>阴影的局部偏移（固定 1px，不再随整体缩放变粗）。</summary>
        private const double ShadowOffset = 1.0;

        /// <summary>HUD 能容纳的最大行数（预分配池大小，不随配置变化而重建）。</summary>
        private const int MaxTotalLines = Lines + 12;

        // ---- 文字池 ----
        // ⚠️ 刻意**不用**父容器（dc.h2d.Object）：实机诊断证明在 hashlink 代理下
        //    子节点的绝对变换不会被重算（局部 y 分开了，absY 全是 0），20 行会叠在一起。
        //    现在每个 Text 直接挂 level.root 的 UI 层，用绝对坐标定位（同 KillSwapWeapon）。
        private static Text[] _texts;                // [i*2] = 阴影, [i*2+1] = 正文
        private static Level _level;

        private static readonly string[] Last = new string[MaxTotalLines];
        private static readonly int[] _lastColor = new int[MaxTotalLines];

        private static bool _createFailed;

        /// <summary>是否已打过一次布局诊断（避免刷屏）。</summary>
        private static bool _layoutDiagLogged;

        /// <summary>
        /// 每帧刷新（由主类调用）。
        ///
        /// ⚠️ 本类的核心原则：**Text 对象只在关卡变化时建一次，之后永不 removeChild。**
        /// 老路子在配置变化时 Cleanup() + Create()，拖动缩放滑条时每帧重建，
        /// 只要有一次没被摘干净，旧的一层就会永远留在屏幕上叠着 —— 这才是"叠层"的来源。
        /// 现在隐藏 = 清空文字 + 逐行 visible = false，对象全程复用。
        /// </summary>
        public static void Update(Hero hero)
        {
            if (hero == null) return;

            UiCfg ui = Cfg.V.Ui;
            bool want = ui == null || ui.HudEnabled;

            // 该显示的条件全不满足 → 只隐藏（不销毁）
            if (!want || GojoMenu.IsOpen || IsOptionsOrPauseScreen())
            {
                Hide();
                return;
            }

            Level level = null;
            try { level = hero._level; }
            catch { }
            if (level == null) { Hide(); return; }

            // 只有"关卡变了"才重建（关卡一变 level.root 就没了，必须重建）
            if (_texts == null || !ReferenceEquals(_level, level))
            {
                DestroyPool();
                _level = level;
                Create(level, ui);
                if (_texts == null) return;   // 字体没就绪，下一帧再试
            }

            // 键名直接来自配置，不需要再读任何游戏状态
            SetVisible(true);
            ApplyLayout(ui);

            int total = TotalLines;
            string[] rows = BuildRows(ui, total);

            for (int i = 0; i < MaxTotalLines && i * 2 + 1 < _texts.Length; i++)
            {
                bool used = i < total;

                // 多余的行直接清空（对象留着，下次要用直接写回去）
                if (!used)
                {
                    if (Last[i] != null)
                    {
                        Last[i] = null;
                        try { _texts[i * 2]?.set_text(GojoUtil.Hs("")); } catch { }
                        try { _texts[i * 2 + 1]?.set_text(GojoUtil.Hs("")); } catch { }
                    }
                    continue;
                }

                int col = ColorFor(i, total);
                bool colorChanged = _lastColor[i] != col;
                if (colorChanged) _lastColor[i] = col;

                string row = rows[i] ?? "";
                if (row == Last[i] && !colorChanged) continue;
                Last[i] = row;

                dc.String s;
                // dc.h2d.HtmlText.set_text 会把文本当 XML 解析 —— 尖括号要先清掉
                try { s = GojoUtil.Hs(GojoMenu.Sanitize(row)); }
                catch { continue; }

                try { _texts[i * 2]?.set_text(s); } catch { }
                try { _texts[i * 2 + 1]?.set_text(s); } catch { }
                try { _texts[i * 2 + 1].textColor = col; } catch { }
            }
        }

        /// <summary>
        /// 把位置 / 行距 / 缩放套到每一行文字上（不重建任何对象）。
        ///
        /// ============================ 为什么不用父容器（踩坑记录） ============================
        /// 原本设想是"整块 HUD 挂一个 dc.h2d.Object 容器，只缩放容器"。实机诊断（日志
        /// `HUD 布局诊断`）证明**在这套 hashlink 代理下走不通**：
        ///
        ///   容器 x=28 y=116 scaleX=0.85 posChanged=True
        ///   行0 y=0     absY=0
        ///   行1 y=0.938 absY=0      ← 局部 y 分开了，但引擎算出的 absY 全是 0
        ///   行5 y=4.688 absY=0
        ///
        /// 也就是说子节点的绝对变换**从来没被重算过**（`posChanged` 置在容器上也不管用），
        /// 结果 20 行全叠在同一处。所以这里改成**直接给每行设绝对坐标** ——
        /// 这正是改动之前能正常渲染的写法（仓库里 KillSwapWeapon / CameraMod 也是这么做的）。
        ///
        /// 缩放的处理：
        ///   · 每行文字自己 `scaleX/scaleY = scale`（字号跟着变）；
        ///   · **行距也乘以 scale**，保证放大后行与行依然按比例分开、不会互相压住
        ///     （这正是"放大后叠层"的根因：原来行距写死 16px，而字号 ×3 变成 36px 高）；
        ///   · 阴影偏移固定 1.0px 屏幕像素（`shadow = 正文 + 1`），不再乘 scale，
        ///     所以放大后阴影仍是细边而不是糊成一片。
        /// </summary>
        private static void ApplyLayout(UiCfg ui)
        {
            if (_texts == null) return;

            double x = ui != null ? ui.HudX : 12.0;
            double y = ui != null ? ui.HudY : 276.0;
            double lh = ui != null ? ui.HudLineHeight : BaseLineHeight;
            double scale = ui != null ? ui.HudScale : 1.0;
            double spacing = ui != null ? ui.HudRowSpacing : 1.35;

            if (scale < 0.2) scale = 0.2;
            if (scale > 4.0) scale = 4.0;
            if (lh < 4.0) lh = 4.0;
            if (spacing < 0.5) spacing = 0.5;
            if (spacing > 3.0) spacing = 3.0;

            // 行距 = 行高 × 缩放 × 额外倍率。
            //   · 乘 scale：字号放大时行距同比例放大，行与行不会互相压住
            //     （只乘 lh 不乘 scale 的话，scale=3 时字号 36px 而行距才 16px → 叠在一起）；
            //   · 再乘 spacing：给玩家一个"把行距拉开"的旋钮（默认 1.35，留出呼吸空间）。
            double pitch = lh * scale * spacing;

            for (int i = 0; i < MaxTotalLines && i * 2 + 1 < _texts.Length; i++)
            {
                Text shadow = _texts[i * 2];
                Text body = _texts[i * 2 + 1];

                double rowY = y + i * pitch;
                double rowX = x + ShadowOffset;   // 阴影整体右移 1px；正文在 x

                try
                {
                    if (shadow != null)
                    {
                        shadow.x = rowX;
                        shadow.y = rowY + ShadowOffset;
                        shadow.scaleX = scale;
                        shadow.scaleY = scale;
                        shadow.posChanged = true;
                    }
                    if (body != null)
                    {
                        body.x = x;
                        body.y = rowY;
                        body.scaleX = scale;
                        body.scaleY = scale;
                        body.posChanged = true;
                    }
                }
                catch { }
            }
        }

        /// <summary>选项界面 / 暂停菜单开着（此时不要画 HUD）。</summary>
        private static bool IsOptionsOrPauseScreen()
        {
            try
            {
                dc.pr.Game game = dc.pr.Game.Class.ME;
                return game != null && game.paused;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ 内容

        private static string[] BuildRows(UiCfg ui, int total)
        {
            var rows = new string[total];

            bool active = Abilities.GojoHub.AbilitiesActive;
            bool force = Abilities.GojoHub.ForceActive;

            rows[LineTitle] = active
                ? (force ? "GOJO  LIMITLESS  [FORCED]" : "GOJO  LIMITLESS")
                : "GOJO  LIMITLESS  [OFF]";

            // ---- 无下限：咒力条 + 储量 ----
            double reserve = Abilities.InfinityAbility.Reserve;
            double max = Abilities.InfinityAbility.MaxReserve;
            double ratio = max > 0.0 ? reserve / max : 0.0;

            rows[LineInfinity] = "INF  " + Bar(ratio) + (max > 0.0
                ? $"  {reserve:0}/{max:0}"
                : "  --");

            // ---- 四个主动招式 ----
            var list = Abilities.GojoHub.Abilities;
            for (int i = 0; i < 4; i++)
            {
                int row = LineAbility0 + i;
                Abilities.GojoAbilityBase ab = FindActive(list, i);
                if (ab == null) { rows[row] = ""; continue; }

                // 键名直接来自配置（玩家在菜单里改完，这里立刻跟着变）
                string key = Pad(GojoKeys.Name(ab.TriggerKey), 4);
                string name = Pad(ab.DisplayName, 14);

                if (!ab.Enabled)
                {
                    rows[row] = key + name + "[off]";
                }
                else if (ab.Ready)
                {
                    rows[row] = key + name + "[READY]";
                }
                else
                {
                    rows[row] = key + name + Bar(ab.CooldownRatio) + $" {ab.CooldownLeft:0.0}s";
                }
            }

            // ---- 状态行 ----
            string status;
            if (Abilities.UnlimitedVoidAbility.IsActive)
            {
                status = $"DOMAIN ACTIVE  {Abilities.UnlimitedVoidAbility.Remaining:0.0}s";
            }
            else
            {
                int slowed = Abilities.SlowAura.ActiveCount;
                status = slowed > 0
                    ? $"SLOWED FOES {slowed}"
                    : (active ? "ALL SYSTEMS NOMINAL" : "MOD DISABLED");
            }
            rows[LineStatus] = status;

            // ---- 连招提示 ----
            string hint = Abilities.GojoHub.ComboHint;
            rows[LineCombo] = string.IsNullOrEmpty(hint) ? "" : "COMBO: " + hint;

            // ---- 可选：最近日志 ----
            int logCount = total - Lines;
            if (logCount > 0)
            {
                string[] recent = Log.Recent(logCount);
                for (int i = 0; i < logCount; i++)
                {
                    int row = Lines + i;
                    if (row >= total) break;
                    rows[row] = i < recent.Length ? recent[i] : "";
                }
            }

            return rows;
        }

        private static Abilities.GojoAbilityBase FindActive(
            System.Collections.Generic.IReadOnlyList<Abilities.GojoAbilityBase> list, int index)
        {
            // list[0] 是无下限被动，跳过它
            int n = index + 1;
            if (list == null || n >= list.Count) return null;
            return list[n];
        }

        private static int ColorFor(int line, int total)
        {
            // 日志行统一用暗色
            if (line >= Lines) return ColDim;

            switch (line)
            {
                case LineTitle:
                    return Abilities.GojoHub.AbilitiesActive ? ColTitle : ColDisabled;

                case LineInfinity:
                {
                    double max = Abilities.InfinityAbility.MaxReserve;
                    double ratio = max > 0.0 ? Abilities.InfinityAbility.Reserve / max : 0.0;
                    if (ratio <= 0.30) return ColDanger;
                    if (ratio <= 0.60) return ColCooling;
                    return ColTitle;
                }

                case LineStatus:
                    if (Abilities.UnlimitedVoidAbility.IsActive) return ColVoid;
                    return Abilities.GojoHub.AbilitiesActive ? ColDim : ColDanger;

                case LineCombo:
                    return string.IsNullOrEmpty(Abilities.GojoHub.ComboHint) ? ColDim : ColPurple;

                default:
                {
                    var list = Abilities.GojoHub.Abilities;
                    int i = line - LineAbility0;
                    Abilities.GojoAbilityBase ab = FindActive(list, i);
                    if (ab == null) return ColDim;
                    if (!ab.Enabled) return ColDisabled;
                    if (ab.Ready) return i switch
                    {
                        0 => ColBlue,
                        1 => ColRed,
                        2 => ColPurple,
                        _ => ColVoid,
                    };
                    return ColCooling;
                }
            }
        }

        /// <summary>字符进度条：剩余冷却用 █ 表示"还没好"。</summary>
        private static string Bar(double ratio01)
        {
            if (double.IsNaN(ratio01)) ratio01 = 0.0;
            if (ratio01 < 0.0) ratio01 = 0.0;
            if (ratio01 > 1.0) ratio01 = 1.0;

            int filled = (int)SysMath.Round(ratio01 * BarCells);
            if (filled < 0) filled = 0;
            if (filled > BarCells) filled = BarCells;

            var sb = new System.Text.StringBuilder(BarCells + 2);
            sb.Append('[');
            for (int i = 0; i < BarCells; i++) sb.Append(i < filled ? '#' : '.');
            sb.Append(']');
            return sb.ToString();
        }

        private static string Pad(string s, int width)
        {
            if (string.IsNullOrEmpty(s)) return new string(' ', width);
            if (s.Length >= width) return s.Substring(0, width) + " ";
            return s + new string(' ', width - s.Length);
        }

        // ------------------------------------------------------------ 创建 / 销毁

        /// <summary>
        /// 一次性把整个池建出来（<see cref="MaxTotalLines"/> 行 × 2），每个 Text
        /// **直接挂在 <c>level.root</c> 的 UI 层**上（不用父容器，理由见 <see cref="ApplyLayout"/>）。
        ///
        /// ⚠️ 之后**再也不重建**：行数 / 缩放 / 位置全部通过改 Text 属性生效。
        /// </summary>
        private static void Create(Level level, UiCfg ui)
        {
            if (_createFailed) return;

            try
            {
                Font font = dc.Assets.Class.font12;
                if (font == null) return;      // 字体还没加载好，下一帧再试

                int layer = dc.Const.Class.ROOT_DP_CTX_UI;

                var texts = new Text[MaxTotalLines * 2];
                for (int i = 0; i < MaxTotalLines; i++)
                {
                    texts[i * 2] = MakeText(font, level, ColShadow);
                    texts[i * 2 + 1] = MakeText(font, level, ColText);
                }

                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] == null) continue;
                    try { level.root.addChildAt(texts[i], layer); } catch { }
                }

                for (int i = 0; i < MaxTotalLines; i++) { Last[i] = null; _lastColor[i] = int.MinValue; }

                _texts = texts;
                ApplyLayout(ui);
                Log.Info($"HUD 已创建（池 {MaxTotalLines} 行 × 2，逐行绝对定位，字体 font12）");

                // 头一次创建后把"我设进去的值"和"引擎算出来的 absY"一起摊开 ——
                // 以后再出现"文字叠在一起"可以直接看这条，不用猜。
                LogLayoutDiag();
            }
            catch (Exception ex)
            {
                Log.Warn("HUD 创建失败（本局不再尝试）: " + ex.Message);
                _createFailed = true;
                Cleanup();
            }
        }

        /// <summary>
        /// 布局诊断：把"我设进去的值"和"引擎算出来的 absY"并排打出来。
        ///
        /// 判读方法：
        ///   · 每行 `y` 是递增的、`absY` 也是递增的 → 布局正常；
        ///   · `y` 递增但 `absY` 全是 0 → 引擎没重算绝对变换（父容器方案就是这么挂的）；
        ///   · `y` 本身就全一样 → <see cref="ApplyLayout"/> 没跑到或算错。
        /// </summary>
        private static void LogLayoutDiag()
        {
            if (_layoutDiagLogged) return;
            _layoutDiagLogged = true;

            try
            {
                var sb = new System.Text.StringBuilder();
                int[] probe = { 0, 1, 5, 8 };
                for (int i = 0; i < probe.Length; i++)
                {
                    int idx = probe[i] * 2 + 1;
                    if (_texts == null || idx >= _texts.Length || _texts[idx] == null) continue;
                    Text t = _texts[idx];
                    if (sb.Length > 0) sb.Append(" | ");
                    sb.Append("行").Append(probe[i])
                      .Append(" x=").Append(Num(t.x))
                      .Append(" y=").Append(Num(t.y))
                      .Append(" scale=").Append(Num(t.scaleY))
                      .Append(" absY=").Append(Num(t.absY))
                      .Append(" pc=").Append(t.posChanged);
                }

                Log.Info("HUD 布局诊断: " + sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Warn("HUD 布局诊断失败: " + ex.Message);
            }
        }

        private static string Num(double v)
            => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private static Text MakeText(Font font, Level level, int color)
        {
            try
            {
                var t = new Text(font, level.root);
                t.textColor = color;
                t.x = 0.0;
                t.y = 0.0;
                t.scaleX = 1.0;
                t.scaleY = 1.0;
                t.visible = true;
                return t;
            }
            catch { return null; }
        }

        /// <summary>把池里所有 Text 从父容器摘掉（只走这一条路径，避免漏摘）。</summary>
        private static void Cleanup()
        {
            DestroyPool();
        }

        /// <summary>
        /// 隐藏 = 清空文字 + 每行 <c>visible = false</c>。
        /// **对象留着复用**（绝不 removeChild）。
        /// </summary>
        private static void Hide()
        {
            if (_texts == null) return;

            for (int i = 0; i < _texts.Length; i++)
            {
                try { _texts[i]?.set_text(GojoUtil.Hs("")); } catch { }
                try { if (_texts[i] != null) _texts[i].visible = false; } catch { }
            }

            for (int i = 0; i < MaxTotalLines; i++) Last[i] = null;
        }

        /// <summary>整块显示/隐藏（逐行设，因为没有父容器可以一把关掉）。</summary>
        private static void SetVisible(bool v)
        {
            if (_texts == null) return;
            for (int i = 0; i < _texts.Length; i++)
            {
                try { if (_texts[i] != null) _texts[i].visible = v; } catch { }
            }
        }

        /// <summary>真的销毁（只用于关卡切换 / 卸载）：逐个 Text 从 <c>level.root</c> 摘掉。</summary>
        private static void DestroyPool()
        {
            Text[] texts = _texts;
            _texts = null;          // 先断引用，后面即使抛异常也不会再有人指向它们
            _level = null;

            if (texts != null)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    Text t = texts[i];
                    if (t == null) continue;
                    try { t.visible = false; } catch { }
                    try
                    {
                        dc.h2d.Object p = t.parent;
                        if (p != null) p.removeChild(t);
                    }
                    catch { }
                }
            }

            for (int i = 0; i < MaxTotalLines; i++) { Last[i] = null; _lastColor[i] = int.MinValue; }
        }

        /// <summary>模组卸载 / 换关强制重建。</summary>
        public static void Reset()
        {
            Cleanup();
            _createFailed = false;
        }
    }
}
