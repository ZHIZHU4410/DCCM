#nullable disable
using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.h2d;
using dc.pr;
using GojoLimitless.Abilities;
using HaxeProxy.Runtime;

using SysMath = System.Math;

namespace GojoLimitless
{
    /// <summary>
    /// 模组菜单（默认 F10）。
    ///
    /// ============================ 操作 ============================
    ///   Up / Down        选择条目（按住会连续移动）
    ///   Left / Right     数值 -/+（按住 Left Ctrl 是 10 倍步长）
    ///   Enter            开关项取反 / 按键项进入"按键捕获"
    ///   捕获中按任意键    把当前按键名写进配置（Backspace 取消捕获）
    ///   Tab / Shift+Tab  上一页 / 下一页
    ///   Esc              保存到磁盘并关闭（顺便重载一次配置）
    ///
    /// ============================ 实现 ============================
    ///   · 菜单打开时用反射调原版 <c>Game.modalPause</c> 真暂停
    ///     —— 暂停期间 <c>IOnHeroUpdate</c> 不会回调，所以菜单由
    ///     <c>IOnFrameUpdate</c>（模组框架直接回调，不受暂停影响）驱动。
    ///   · 渲染和 HUD 同一套：<c>dc.Assets.font12</c> + <c>level.root</c> 的 UI 层。
    ///   · 所有值都直接用委托直读写 <see cref="Cfg"/> 的对象，改完立刻 <c>Save()</c>。
    /// </summary>
    public static class GojoMenu
    {
        // ---------------------------------------------------------------- 条目

        private sealed class Item
        {
            public string Label;
            public Func<string> Get;
            public Action<string> Set;
            public double Step;
            public double Min;
            public double Max;
            public bool IsBool;

            /// <summary>按键项（Enter / → 进入捕获）。</summary>
            public bool IsKey;

            /// <summary>Enter 直接执行一次的动作（如"恢复默认值"）。</summary>
            public bool IsAction;

            /// <summary>数值项 = 不是开关、不是按键、也不是"按一下就执行"的动作。</summary>
            public bool IsNumeric => !IsBool && !IsKey && !IsAction;
        }

        private sealed class Page
        {
            public string Title;
            public readonly List<Item> Items = new();
        }

        private static readonly List<Page> Pages = new();

        private static bool _open;
        private static int _page;
        private static int _index;
        private static int _scroll;
        private static string _flash = "";
        private static double _flashTimer;

        /// <summary>按键捕获模式：下一个按下的（非修饰）键就是新绑定。</summary>
        private static bool _capturingKey;

        /// <summary>配置被菜单改过（Esc 时统一写盘，避免每按一次方向键就全量写文件）。</summary>
        private static bool _dirty;

        /// <summary>
        /// 暂停游戏是否**真的成功**了。
        ///
        /// 反射可能因为找不到方法 / 签名不符而失败，那时游戏其实在跑（玩家可能在菜单里被打死），
        /// 标题栏会显示警告。**只有这里为 true 才允许调 resume()** ——
        /// 否则会把原版暂停菜单的暂停状态一起取消掉。
        /// </summary>
        private static bool _pauseSucceeded;

        private static Text[] _texts;
        private static Level _level;
        private static readonly string[] Last = new string[TextLines];
        private static bool _createFailed;

        private const int TextLines = 22;
        private const int TitleLine = 0;
        private const int HelpLine1 = TextLines - 2;
        private const int HelpLine2 = TextLines - 1;
        private const int ListStart = 2;
        private const int MaxVisible = HelpLine1 - ListStart;

        private const int ColShadow = 0x000000;
        private const int ColTitle = 0xAEF4FF;
        private const int ColText = 0xE8F6FF;
        private const int ColDim = 0x93A7B4;
        private const int ColSelected = 0xFFE08A;
        private const int ColValue = 0x8CFFC0;
        private const int ColWarn = 0xFF6A4A;

        public static bool IsOpen => _open;

        // ---------------------------------------------------------------- 开关

        public static void Toggle(Hero hero)
        {
            if (_open) Close();
            else Open(hero);
        }

        public static void Open(Hero hero)
        {
            if (_open) return;
            try
            {
                Cfg.Reload();
                Rebuild();
                _page = 0;
                _index = 0;
                _scroll = 0;
                _capturingKey = false;
                _dirty = false;
                _open = true;
                _flash = "Esc = 保存并关闭；PageUp/PageDown 翻页";
                _flashTimer = 3.0;

                // 先把菜单文本建出来再暂停，避免"暂停了但还没渲染"的一帧空白
                try { if (EnsureText(hero)) ShowText(); } catch { }

                // ⚠️ 只有真的停住了才记 _pauseSucceeded，否则关菜单时绝不能调 resume()
                _pauseSucceeded = SetGamePaused(true);
                if (!_pauseSucceeded)
                {
                    Log.Warn("菜单已打开，但暂停失败 —— 游戏仍在运行，请小心");
                }
                // 正常情况的开/关日志统一由主循环打（这里不再打，避免一次切换出现两条）
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "打开菜单失败");
                _open = false;
                _pauseSucceeded = false;
            }
        }

        public static void Close()
        {
            if (!_open) return;
            _open = false;
            _capturingKey = false;

            // 只在真的改过值时才写盘
            if (_dirty)
            {
                try { Cfg.Save(); } catch { }
                _dirty = false;
            }

            try { Cfg.Reload(); } catch { }
            try { GojoHub.ReloadKeys(); } catch { }
            try { InfinityAbility.InvalidateReserve(); } catch { }

            // ⚠️ 只有我们自己成功暂停过才解暂停 —— 否则会把原版暂停菜单一起取消
            if (_pauseSucceeded)
            {
                SetGamePaused(false);
                _pauseSucceeded = false;
            }

            HideText();
            Input.Reset();
            // 开/关的日志统一由主循环打（这里不再打，避免一次切换出现两条）
        }

        // ---------------------------------------------------------------- 每帧

        /// <summary>只重画，不消费输入（同一帧的重复回调走这里）。</summary>
        public static void RenderOnly(Hero hero)
        {
            if (!_open) return;
            try { Render(hero); }
            catch { }
        }

        /// <summary>由 <c>IOnFrameUpdate</c> 每帧调用（暂停时也会跑）。</summary>
        public static void Update(Hero hero, double dt)
        {
            if (!_open) return;
            if (dt <= 0.0 || double.IsNaN(dt) || dt > 1.0) dt = 1.0 / 60.0;

            try
            {
                // ⚠️ 这里**绝对不能再调 Input.Poll()**。
                //    主循环 TickAll 每帧已经 Poll 过一次，Poll 会重建 JustPressed；
                //    再调一次会把本帧的"刚按下"清空 —— F10 于是在同一帧里被判定两次：
                //    第一次（TickAll）打开菜单，第二次（这里）又把它关掉，
                //    日志就出现"已打开 / 已关闭"刷屏、人根本关不掉菜单。
                HandleInput(dt);

                // ⚠️ HandleInput 里 Esc 会调 Close()（把 _texts 置 null 并解暂停）。
                //    这里必须重新判一次 _open，否则 Render 会立刻把文本重新建出来，
                //    而下一帧 Update 因为 _open==false 直接返回 —— 菜单就永远留在屏幕上了。
                if (!_open) return;

                Render(hero);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "菜单每帧更新失败");
            }
        }

        private static void HandleInput(double dt)
        {
            if (_flashTimer > 0.0) _flashTimer -= dt;

            // 导航键也走**原版动作**（上/下/左/右/确认/取消/上一个/下一个），
            // 所以在原版「选项 → 控制」里改了方向键，这里也跟着变。
            // 取不到时退回经典 VK 码，保证菜单永远能用。
            // 导航键固定用经典码（菜单打开时游戏是暂停的，不会和玩法抢输入）
            int up = 0x26, down = 0x28, left = 0x25, right = 0x27;
            int enter = 0x0D, esc = 0x1B, back = 0x08;
            int prev = 0x21, next = 0x22;    // PageUp / PageDown
            int ctrl = 0x11;                 // Ctrl = "×10 粗调"修饰键

            // 原版手柄的 UI_PREV/UI_NEXT 可能是同一组键，去重避免翻页翻两次
            if (next == prev) next = 0;

            // ---- 按键捕获模式：下一个按下的键就是新绑定 ----
            if (_capturingKey)
            {
                if (Input.Pressed(back) || Input.Pressed(esc))
                {
                    _capturingKey = false;
                    Flash("已取消按键捕获");
                    return;
                }

                int captured = Input.FirstPressed();
                if (captured != 0)
                {
                    Item it = Current();
                    if (it != null && it.IsKey)
                    {
                        string name = GojoKeys.Name(captured);
                        it.Set(name);
                        // 新键要进跟踪集，否则第一次按会被吃掉
                        GojoHub.ReloadKeys();
                        Flash($"{it.Label} = {name}");
                        Log.Info($"菜单：{it.Label} 绑定为 {name}");
                    }
                    _capturingKey = false;
                }
                return;
            }

            // ---- Escape：保存并关闭 ----
            if (Input.Pressed(esc))
            {
                Close();
                return;
            }

            // ---- 翻页：原版的"上一个/下一个"动作 ----
            if (Input.Pressed(prev))
            {
                _page = (_page - 1 + Pages.Count) % Pages.Count;
                _index = 0;
                _scroll = 0;
                return;
            }
            if (Input.Pressed(next))
            {
                _page = (_page + 1) % Pages.Count;
                _index = 0;
                _scroll = 0;
                return;
            }

            Item cur = Current();

            // ---- 上下选择 ----
            if (Input.PressedRepeat(up, dt)) Move(-1);
            if (Input.PressedRepeat(down, dt)) Move(1);

            // ---- 左右调值 ----
            double mult = Input.Held(ctrl) ? 10.0 : 1.0;
            if (Input.PressedRepeat(left, dt)) Adjust(cur, -1.0 * mult);
            if (Input.PressedRepeat(right, dt)) Adjust(cur, 1.0 * mult);

            // ---- 回车：动作 / 开关 / 按键捕获 ----
            if (Input.Pressed(enter))
            {
                if (cur == null) return;
                if (cur.IsAction)
                {
                    cur.Set(cur.Get());
                }
                else if (cur.IsBool)
                {
                    bool now = cur.Get() == "True";
                    cur.Set((!now).ToString());
                    Flash($"{cur.Label} = {(!now ? "开" : "关")}");
                }
                else if (cur.IsKey)
                {
                    _capturingKey = true;
                    Flash("按下要绑定的键（Backspace / Esc 取消）");
                }
                else
                {
                    Flash(cur.Label + " = " + cur.Get());
                }
            }
        }

        private static void Move(int delta)
        {
            Page p = CurrentPage();
            if (p == null || p.Items.Count == 0) return;

            _index += delta;
            if (_index < 0) _index = p.Items.Count - 1;
            if (_index >= p.Items.Count) _index = 0;

            if (_index < _scroll) _scroll = _index;
            if (_index >= _scroll + MaxVisible) _scroll = _index - MaxVisible + 1;
        }

        private static void Adjust(Item it, double dir)
        {
            if (it == null) return;

            if (it.IsAction)
            {
                if (dir > 0.0) it.Set(it.Get());
                return;
            }

            if (it.IsBool)
            {
                bool now = it.Get() == "True";
                it.Set((!now).ToString());
                Flash($"{it.Label} = {(!now ? "开" : "关")}");
                return;
            }

            // 按键项：→ 进入捕获
            if (it.IsKey)
            {
                if (dir > 0.0) { _capturingKey = true; Flash("按下要绑定的键（Backspace / Esc 取消）"); }
                return;
            }

            if (!double.TryParse(it.Get(), System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double v))
            {
                v = 0.0;
            }

            v += dir * it.Step;
            if (v < it.Min) v = it.Min;
            if (v > it.Max) v = it.Max;

            // 步长 < 1 的保留两位小数，否则取整
            string s = it.Step < 1.0
                ? v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                : SysMath.Round(v).ToString(System.Globalization.CultureInfo.InvariantCulture);

            it.Set(s);
            Flash($"{it.Label} = {s}");
        }

        private static void Flash(string msg)
        {
            _flash = msg;
            _flashTimer = 2.5;
        }

        private static Page CurrentPage()
        {
            if (Pages.Count == 0) return null;
            if (_page < 0) _page = 0;
            if (_page >= Pages.Count) _page = Pages.Count - 1;
            return Pages[_page];
        }

        private static Item Current()
        {
            Page p = CurrentPage();
            if (p == null || p.Items.Count == 0) return null;
            if (_index < 0) _index = 0;
            if (_index >= p.Items.Count) _index = p.Items.Count - 1;
            return p.Items[_index];
        }

        // ---------------------------------------------------------------- 渲染

        private static void Render(Hero hero)
        {
            if (!EnsureText(hero)) return;
            ShowText();

            var rows = new string[TextLines];
            Page p = CurrentPage();

            int total = Pages.Count;
            rows[TitleLine] = $"GOJO LIMITLESS  -  {p?.Title ?? "?"}  [{_page + 1}/{total}]";

            int line = ListStart;
            if (p != null)
            {
                for (int i = _scroll; i < p.Items.Count && line < HelpLine1; i++, line++)
                {
                    Item it = p.Items[i];
                    bool sel = i == _index;

                    string marker = sel ? "> " : "  ";
                    string label = Pad(it.Label, 24);
                    string val = Pad(it.Get(), 10);

                    string suffix = "";
                    if (it.IsNumeric) suffix = $"step {it.Step:0.###}  [{it.Min:0.##}..{it.Max:0.##}]";

                    rows[line] = marker + label + val + suffix;
                }
            }

            rows[HelpLine1] = _capturingKey
                ? "按下要绑定的键（Backspace / Esc 取消）"
                : (_flashTimer > 0.0
                    ? _flash
                    : "Up/Down select   Left/Right adjust   Enter toggle/rebind   PgUp/PgDn page   Esc save+close");
            rows[HelpLine2] = "按键项按 Enter 进捕获；推荐 J/K/L/U/I/O/H；Ctrl+Left/Right = x10";

            for (int i = 0; i < TextLines; i++)
            {
                // ⚠️ dc.h2d.HtmlText.set_text 会走 _Xml_.parse(text) —— **文本被当成 XML**，
                //    出现 < 就会解析失败（"Bad node type"）。所有进 set_text 的字符串
                //    统一在这里把尖括号替换掉，避免以后再加文案时踩同一个坑。
                string s = Sanitize(rows[i] ?? "");
                if (s != Last[i])
                {
                    Last[i] = s;
                    dc.String hs;
                    try { hs = GojoUtil.Hs(s); }
                    catch { continue; }
                    try { _texts[i * 2]?.set_text(hs); } catch { }
                    try { _texts[i * 2 + 1]?.set_text(hs); } catch { }
                }

                int col = ColorFor(i);
                try { _texts[i * 2 + 1].textColor = col; } catch { }
            }
        }

        /// <summary>把会破坏 XML 解析的字符换成等价的普通字符。</summary>
        internal static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            if (s.IndexOf('<') < 0 && s.IndexOf('>') < 0 && s.IndexOf('&') < 0) return s;
            return s.Replace('<', '(').Replace('>', ')').Replace("&", "+");
        }

        private static int ColorFor(int line)
        {
            if (line == TitleLine) return ColTitle;
            if (line == HelpLine1) return _flashTimer > 0.0 ? ColWarn : ColDim;
            if (line == HelpLine2) return ColDim;

            int idx = _scroll + (line - ListStart);
            if (line < ListStart || line >= HelpLine1) return ColDim;
            return idx == _index ? ColSelected : ColText;
        }

        private static string Pad(string s, int width)
        {
            if (string.IsNullOrEmpty(s)) return new string(' ', width);
            if (s.Length >= width) return s.Substring(0, width);
            return s + new string(' ', width - s.Length);
        }

        // ---------------------------------------------------------------- 文本对象
        //
        // ⚠️ 和 HUD 一样的原则：**Text 只在关卡变化时建一次，之后只切可见性 + 改文字**，
        //    绝不 removeChild。原版那套 removeChild 在这个层级上并不可靠，
        //    而"关一次菜单就重建一层"会让旧文字留在屏幕上叠起来
        //    （用户看到的"放大后有叠层"就是这类残留之一）。

        private static bool EnsureText(Hero hero)
        {
            Level level = null;
            try { level = hero?._level; } catch { }
            if (level == null) return false;

            // 关卡变了 → 旧 root 已经随关卡销毁，重建是对的
            if (_texts != null && ReferenceEquals(_level, level)) return true;
            if (_createFailed) return false;

            DestroyTextPool();
            _level = level;

            try
            {
                Font font = dc.Assets.Class.font12;
                if (font == null) return false;

                int layer = dc.Const.Class.ROOT_DP_CTX_UI;

                // ⚠️ 不用父容器：实机证明 hashlink 代理下子节点绝对变换不会被重算
                //    （局部 y 分开、absY 全 0），22 行会叠在一起。
                //    改为每个 Text 直接挂 UI 层 + 绝对坐标（同 GojoHud / KillSwapWeapon）。
                var texts = new Text[TextLines * 2];
                for (int i = 0; i < TextLines; i++)
                {
                    texts[i * 2] = MakeText(font, level, ColShadow);
                    texts[i * 2 + 1] = MakeText(font, level, ColText);
                }

                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] == null) continue;
                    try { level.root.addChildAt(texts[i], layer); } catch { }
                }

                for (int i = 0; i < TextLines; i++) Last[i] = null;
                _texts = texts;
                ApplyMenuLayout();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("菜单文本创建失败: " + ex.Message);
                _createFailed = true;
                DestroyTextPool();
                return false;
            }
        }

        /// <summary>
        /// 把位置 / 行距 / 缩放套到每一行文字上（绝对坐标，不用父容器）。
        ///
        /// 缩放同时作用在**字号**和**行距**上，所以菜单放大后行与行仍按比例分开、
        /// 不会互相压住；阴影偏移固定 1px 屏幕像素。
        /// </summary>
        private static void ApplyMenuLayout()
        {
            if (_texts == null) return;

            UiCfg ui = Cfg.V.Ui;
            double x = ui != null ? ui.HudX : 12.0;
            double y = 64.0;
            double lh = ui != null ? ui.HudLineHeight : 16.0;
            double scale = ui != null ? ui.HudScale : 1.0;
            double spacing = ui != null ? ui.HudRowSpacing : 1.35;
            if (scale < 0.2) scale = 0.2;
            if (scale > 4.0) scale = 4.0;
            if (lh < 4.0) lh = 4.0;
            if (spacing < 0.5) spacing = 0.5;
            if (spacing > 3.0) spacing = 3.0;

            // 行距 = 行高 × 缩放 × 额外倍率（同 GojoHud.ApplyLayout）
            double pitch = lh * scale * spacing;

            for (int i = 0; i < TextLines && i * 2 + 1 < _texts.Length; i++)
            {
                double rowY = y + i * pitch;
                double rowX = x + 1.0;      // 阴影整体右移 1px；正文在 x

                try
                {
                    if (_texts[i * 2] != null)
                    {
                        _texts[i * 2].x = rowX;
                        _texts[i * 2].y = rowY + 1.0;
                        _texts[i * 2].scaleX = scale;
                        _texts[i * 2].scaleY = scale;
                        _texts[i * 2].posChanged = true;
                    }
                    if (_texts[i * 2 + 1] != null)
                    {
                        _texts[i * 2 + 1].x = x;
                        _texts[i * 2 + 1].y = rowY;
                        _texts[i * 2 + 1].scaleX = scale;
                        _texts[i * 2 + 1].scaleY = scale;
                        _texts[i * 2 + 1].posChanged = true;
                    }
                }
                catch { }
            }
        }

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
                t.visible = false;
                return t;
            }
            catch { return null; }
        }

        /// <summary>隐藏 = 清文字 + 逐行不可见，**对象留着**（没有父容器可以一把关掉）。</summary>
        private static void HideText()
        {
            if (_texts == null) return;

            for (int i = 0; i < _texts.Length; i++)
            {
                try { _texts[i]?.set_text(GojoUtil.Hs("")); } catch { }
                try { if (_texts[i] != null) _texts[i].visible = false; } catch { }
            }

            for (int i = 0; i < TextLines; i++) Last[i] = null;
        }

        private static void ShowText()
        {
            if (_texts == null) return;

            for (int i = 0; i < _texts.Length; i++)
            {
                try { if (_texts[i] != null) _texts[i].visible = true; } catch { }
            }

            ApplyMenuLayout();
        }

        /// <summary>真的销毁（只在关卡切换 / 卸载 / 创建失败时走）：逐个 Text 从 UI 层摘掉。</summary>
        private static void DestroyTextPool()
        {
            Text[] texts = _texts;
            _texts = null;
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

            for (int i = 0; i < TextLines; i++) Last[i] = null;
        }

        /// <summary>换关 / 卸载时清理（<c>level.root</c> 会随关卡销毁）。</summary>
        public static void Reset()
        {
            // 只在我们自己成功暂停过时才解暂停 —— 否则会把原版暂停一起取消
            if (_pauseSucceeded)
            {
                SetGamePaused(false);
                _pauseSucceeded = false;
            }
            if (_dirty)
            {
                try { Cfg.Save(); } catch { }
                _dirty = false;
            }
            _open = false;
            _createFailed = false;
            DestroyTextPool();
        }

        // ---------------------------------------------------------------- 暂停

        /// <summary>
        /// 菜单打开时把游戏暂停（原版 <c>Game.modalPause</c>）；返回是否**真的**停住了。
        ///
        /// ⚠️ 两个坑（都是实机踩出来的）：
        ///
        /// 1) <c>dc.pr.Game</c> 上有**两个** <c>modalPause</c> 重载：
        ///      <c>modalPause(HaxeProxy.Runtime.Ref&lt;bool&gt;)</c>
        ///      <c>modalPause(ref bool)</c>
        ///    所以 <c>GetMethod("modalPause")</c>（不传参数类型）会抛
        ///    <c>AmbiguousMatchException</c> —— 异常被 catch 吞掉，菜单却已经在跑，
        ///    结果是"说暂停了其实没暂停"。必须显式指定参数类型。
        ///
        /// 2) 失败时**不能**在 Close() 里调 <c>resume()</c>：<c>Game.resume()</c> 不是空操作
        ///    （它会 base.resume()、清 <c>_pauseAfterFrames</c>、还原鼠标模式、unblur、
        ///    恢复 voteWinMan），会把玩家自己打开的原版暂停菜单一起取消掉。
        ///    所以这里返回 bool，调用点只在 true 时才反过来调 resume()。
        ///
        /// 返回 false 时菜单照常可用（<c>IOnFrameUpdate</c> 不受暂停影响），
        /// 只是游戏还在跑 —— 标题栏会显示警告。
        /// </summary>
        private static bool SetGamePaused(bool paused)
        {
            try
            {
                Game game = Game.Class.ME;
                if (game == null) return false;
                System.Type t = game.GetType();

                if (!paused)
                {
                    // resume() 只有一个无参重载，不会歧义
                    System.Reflection.MethodInfo mres = t.GetMethod("resume", System.Type.EmptyTypes);
                    mres?.Invoke(game, null);
                    return false;
                }

                // 优先精确匹配 ref bool（GameProxy 里的真实签名）
                System.Reflection.MethodInfo mi =
                    t.GetMethod("modalPause", new[] { typeof(bool).MakeByRefType() })
                    ?? t.GetMethod("modalPause", System.Type.EmptyTypes);

                if (mi == null)
                {
                    // 退化：手动遍历所有同名方法，挑参数最少且能塞进 null 的那个
                    foreach (System.Reflection.MethodInfo cand in t.GetMethods())
                    {
                        if (cand.Name != "modalPause") continue;
                        System.Reflection.ParameterInfo[] cps = cand.GetParameters();
                        bool ok = true;
                        for (int i = 0; i < cps.Length; i++)
                        {
                            System.Type pt = cps[i].ParameterType;
                            if (pt.IsByRef && pt.GetElementType() == typeof(bool)) continue;
                            ok = false;
                            break;
                        }
                        if (ok) { mi = cand; break; }
                    }
                }

                if (mi == null)
                {
                    Log.Warn("找不到 Game.modalPause —— 菜单不会暂停游戏（游戏仍在运行）");
                    return false;
                }

                // ref bool 传 null 等价于 false（= 不播 audioEffect 的淡出），
                // 与 modalPause 自己的 `audioEffect == false` 分支一致。
                System.Reflection.ParameterInfo[] ps = mi.GetParameters();
                object[] args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    System.Type pt = ps[i].ParameterType;
                    if (pt.IsByRef && pt.GetElementType() == typeof(bool)) args[i] = null;
                    else if (pt == typeof(bool)) args[i] = false;
                    else args[i] = null;
                }

                mi.Invoke(game, args);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("暂停游戏失败（菜单仍可用，游戏继续运行）: " + ex.Message);
                return false;
            }
        }

        // ---------------------------------------------------------------- 条目表

        /// <summary>把所有配置项铺成菜单（打开菜单时重建一次，保证和当前值同步）。</summary>
        private static void Rebuild()
        {
            Pages.Clear();

            // ============================================================ 苍
            var blue = new Page { Title = "术式顺转·苍 (Blue)" };
            AddAbility(blue, () => Cfg.V.Blue, () => Cfg.V.Blue.Key,
                       0.5, 60.0, 1.0, 40.0, 0.0, 100000.0, 0.0, 8.0);
            Pages.Add(blue);

            // ============================================================ 赤
            var red = new Page { Title = "术式反转·赤 (Red)" };
            AddAbility(red, () => Cfg.V.Red, () => Cfg.V.Red.Key,
                       0.5, 60.0, 1.0, 40.0, 0.0, 100000.0, 0.0, 8.0);
            Pages.Add(red);

            // ============================================================ 茈
            var purple = new Page { Title = "虚式·茈 (Hollow Purple)" };
            AddAbility(purple, () => Cfg.V.Purple, () => Cfg.V.Purple.Key,
                       0.5, 60.0, 2.0, 80.0, 0.0, 1000000.0, 0.0, 8.0);
            Pages.Add(purple);

            // ============================================================ 领域展开
            var domain = new Page { Title = "领域展开·无量空处 (Domain)" };
            AddAbility(domain, () => Cfg.V.Domain, () => Cfg.V.Domain.Key,
                       0.5, 120.0, 2.0, 60.0, 0.0, 100000.0, 0.0, 8.0);
            AddNum(domain, "内圈半径(格)", () => Cfg.V.Domain.InnerRadius,
                   s => Cfg.V.Domain.InnerRadius = s, 0.5, 1.0, 60.0);
            AddNum(domain, "外圈吸附倍率", () => Cfg.V.Domain.OuterPullMultiplier,
                   s => Cfg.V.Domain.OuterPullMultiplier = s, 0.1, 1.0, 10.0);
            AddNum(domain, "领域移速倍率", () => Cfg.V.Domain.SlowMultiplier,
                   s => Cfg.V.Domain.SlowMultiplier = s, 0.05, 0.02, 1.0);
            AddNum(domain, "伤害跳间隔(s)", () => Cfg.V.Domain.TickInterval,
                   s => Cfg.V.Domain.TickInterval = s, 0.05, 0.05, 5.0);
            AddBool(domain, "领域内无敌", () => Cfg.V.Domain.Invincible,
                    b => Cfg.V.Domain.Invincible = b);
            AddBool(domain, "领域氛围特效", () => Cfg.V.Domain.DomainFx,
                    b => Cfg.V.Domain.DomainFx = b);
            Pages.Add(domain);

            // ============================================================ 无下限
            var inf = new Page { Title = "无下限 (Infinity)" };
            AddBool(inf, "启用无下限", () => Cfg.V.Infinity.Enabled, b => Cfg.V.Infinity.Enabled = b);
            AddNum(inf, "屏障半径(格)", () => Cfg.V.Infinity.Radius, s => Cfg.V.Infinity.Radius = s, 0.5, 0.5, 40.0);
            AddNum(inf, "咒力储量上限", () => Cfg.V.Infinity.Reserve, s => Cfg.V.Infinity.Reserve = s, 20.0, 1.0, 100000.0);
            AddNum(inf, "储量/属性等级", () => Cfg.V.Infinity.ReservePerLevel, s => Cfg.V.Infinity.ReservePerLevel = s, 2.0, 0.0, 10000.0);
            AddNum(inf, "储量回充/秒", () => Cfg.V.Infinity.RegenPerSecond, s => Cfg.V.Infinity.RegenPerSecond = s, 10.0, 0.0, 100000.0);
            AddNum(inf, "单次吸收上限(%)", () => Cfg.V.Infinity.MaxDrainPerHit, s => Cfg.V.Infinity.MaxDrainPerHit = s, 0.05, 0.05, 1.0);
            AddNum(inf, "破裂停顿(s)", () => Cfg.V.Infinity.BreakPenalty, s => Cfg.V.Infinity.BreakPenalty = s, 0.1, 0.0, 10.0);
            AddNum(inf, "破裂期回充倍率", () => Cfg.V.Infinity.BreakRegenFactor, s => Cfg.V.Infinity.BreakRegenFactor = s, 0.05, 0.0, 1.0);
            AddNum(inf, "迟滞最低移速倍率", () => Cfg.V.Infinity.SlowMinMultiplier, s => Cfg.V.Infinity.SlowMinMultiplier = s, 0.05, 0.02, 1.0);
            AddNum(inf, "迟滞尾巴(s)", () => Cfg.V.Infinity.SlowLinger, s => Cfg.V.Infinity.SlowLinger = s, 0.05, 0.0, 5.0);
            AddBool(inf, "湮灭敌方子弹", () => Cfg.V.Infinity.EraseBullets, b => Cfg.V.Infinity.EraseBullets = b);
            AddBool(inf, "显示呼吸光圈", () => Cfg.V.Infinity.AuraFx, b => Cfg.V.Infinity.AuraFx = b);
            AddNum(inf, "光圈呼吸幅度", () => Cfg.V.Infinity.AuraBreath, s => Cfg.V.Infinity.AuraBreath = s, 0.1, 0.0, 3.0);
            AddNum(inf, "低咒力变红阈值", () => Cfg.V.Infinity.AuraLowReserveRatio, s => Cfg.V.Infinity.AuraLowReserveRatio = s, 0.05, 0.0, 1.0);
            Pages.Add(inf);

            // ============================================================ 战斗 / 全局
            var misc = new Page { Title = "战斗与全局" };
            AddNum(misc, "Boss牵引/击退系数", () => Cfg.V.BossPullPushFactor, s => Cfg.V.BossPullPushFactor = s, 0.05, 0.0, 1.0);
            AddNum(misc, "连招窗口(s)", () => Cfg.V.ComboWindow, s => Cfg.V.ComboWindow = s, 0.25, 0.0, 10.0);
            Pages.Add(misc);

            // ============================================================ 系统
            var sys = new Page { Title = "界面与系统" };
            AddBool(sys, "模组总开关", () => Cfg.V.Ui.ModEnabled, b => Cfg.V.Ui.ModEnabled = b);
            AddBool(sys, "显示 HUD", () => Cfg.V.Ui.HudEnabled, b => Cfg.V.Ui.HudEnabled = b);
            AddNum(sys, "HUD X", () => Cfg.V.Ui.HudX, s => Cfg.V.Ui.HudX = s, 4.0, -200.0, 2000.0);
            AddNum(sys, "HUD Y", () => Cfg.V.Ui.HudY, s => Cfg.V.Ui.HudY = s, 4.0, -200.0, 2000.0);
            AddNum(sys, "HUD 行高", () => Cfg.V.Ui.HudLineHeight, s => Cfg.V.Ui.HudLineHeight = s, 1.0, 8.0, 40.0);
            AddNum(sys, "HUD 行距倍率", () => Cfg.V.Ui.HudRowSpacing, s => Cfg.V.Ui.HudRowSpacing = s, 0.05, 0.5, 3.0);
            AddKey(sys, "覆盖层菜单键", () => Cfg.V.Ui.MenuKey, s => Cfg.V.Ui.MenuKey = s);
            AddKey(sys, "强制开启键", () => Cfg.V.Ui.ForceEnableKey, s => Cfg.V.Ui.ForceEnableKey = s);
            AddNum(sys, "强制开启时长(s)", () => Cfg.V.Ui.ForceEnableSeconds, s => Cfg.V.Ui.ForceEnableSeconds = s, 30.0, 1.0, 3600.0);
            AddBool(sys, "详细日志", () => Cfg.V.Ui.VerboseLog, b => Cfg.V.Ui.VerboseLog = b);
            AddAction(sys, "重新载入配置", () =>
            {
                Cfg.Reload();
                GojoHub.ReloadKeys();
                InfinityAbility.InvalidateReserve();
                Rebuild();
                _index = 0;
                _scroll = 0;
                Flash("已重新载入");
            });
            AddAction(sys, "恢复全部默认值", () =>
            {
                Cfg.ResetToDefault();
                GojoHub.ReloadKeys();
                InfinityAbility.InvalidateReserve();
                Rebuild();
                _index = 0;
                _scroll = 0;
                Flash("已恢复默认值");
            });
            Pages.Add(sys);
        }

        private static void AddAbility(Page p, Func<AbilityCfg> cfg, Func<string> actionName,
                                       double cdStep, double cdMax,
                                       double radStep, double radMax,
                                       double dmgStep, double dmgMax,
                                       double powStep, double powMax)
        {
            AddBool(p, "启用", () => cfg().Enabled, b => cfg().Enabled = b);
            AddKey(p, "触发键", () => cfg().Key, s => cfg().Key = s);
            AddNum(p, "冷却(s)", () => cfg().Cooldown, s => cfg().Cooldown = s, cdStep, 0.05, cdMax);
            AddNum(p, "伤害", () => cfg().Damage, s => cfg().Damage = s, dmgStep, 0.0, dmgMax);
            AddBool(p, "伤害吃卷轴加成", () => cfg().UseHeroScaling, b => cfg().UseHeroScaling = b);
            AddNum(p, "半径/射程(格)", () => cfg().Radius, s => cfg().Radius = s, radStep, 1.0, radMax);
            AddNum(p, "力度", () => cfg().Power, s => cfg().Power = s, powStep, 0.0, powMax);
            AddNum(p, "持续(s)", () => cfg().Duration, s => cfg().Duration = s, 0.5, 0.0, 60.0);
        }

        /// <summary>
        /// 按键项：Enter（或 →）进入**按键捕获**，下一个按下的键就是新绑定。
        ///
        /// v7 起按键回到"模组自己存键名"，所以这里可以真正改键。
        /// 推荐用死亡细胞里默认空着的 J / K / L / U / I / O / H 或 F1~F12。
        /// </summary>
        private static void AddKey(Page p, string label, Func<string> get, Action<string> set)
        {
            p.Items.Add(new Item
            {
                Label = label,
                Get = () => get() ?? "",
                Set = s =>
                {
                    set(s);
                    Cfg.Save();
                },
                IsKey = true,
            });
        }

        /// <summary>只读的"按键"显示行（用于不该被改的项）。</summary>
        private static void AddKeyReadOnly(Page p, string label, Func<string> get)
        {
            p.Items.Add(new Item
            {
                Label = label,
                Get = () => get() ?? "",
                Set = _ => { },
                IsKey = true,
            });
        }

        private static void AddBool(Page p, string label, Func<bool> get, Action<bool> set)
        {
            p.Items.Add(new Item
            {
                Label = label,
                Get = () => get() ? "True" : "False",
                Set = s => set(string.Equals(s, "True", StringComparison.OrdinalIgnoreCase)),
                IsBool = true,
            });
        }

        private static void AddNum(Page p, string label, Func<double> get, Action<double> set,
                                   double step, double min, double max)
        {
            p.Items.Add(new Item
            {
                Label = label,
                Get = () => get().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                Set = s =>
                {
                    if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out double v))
                    {
                        set(v);
                        Cfg.Save();
                    }
                },
                Step = step,
                Min = min,
                Max = max,
            });
        }

        private static void AddAction(Page p, string label, Action act)
        {
            p.Items.Add(new Item
            {
                Label = label,
                Get = () => "<Enter>",
                Set = _ => { try { act(); } catch (Exception ex) { Log.Exception(ex, "菜单动作失败"); } },
                IsAction = true,
            });
        }
    }
}
