#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GojoLimitless
{
    /// <summary>
    /// 键盘输入。
    ///
    /// DCCM 没有"注册模组热键"的公开 API，仓库里所有模组（Automation / create_mod.py 模板 /
    /// KingScepterAim …）统一走 <c>user32!GetAsyncKeyState</c>。这里封装成"边沿触发"：
    /// <see cref="Poll"/> 每帧调一次，<see cref="Pressed"/> 只在按下的那一帧返回 true。
    ///
    /// ============================ 跟踪的键集 ============================
    /// 基础集（<see cref="BaseKeys"/>）覆盖了字母 / 数字 / F 键 / 常用功能键，
    /// 再加上 <see cref="EnsureKey"/> 动态补进来的键 —— 每次解析配置里的键名时都会调它，
    /// 所以玩家把按键改成任何不常见的值也能用。
    ///
    /// 为什么必须每帧把**所有**关心的键都 Poll 一遍（包括这一帧没在用的）：
    /// 否则重新绑定后旧键的状态会停在"按下"，新键的第一次按会被吃掉。
    /// </summary>
    public static class Input
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        private const int KEY_DOWN_BIT = 0x8000;

        /// <summary>基础跟踪集（字母/数字/F 键/常用功能键/标点）。</summary>
        private static readonly int[] BaseKeys =
        {
            0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x4B, 0x4C, 0x4D,
            0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A,
            0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B, // F1-F12
            0x20, 0x09, 0x0D, 0x1B, 0x10, 0x11, 0x12, 0x08,
            0x25, 0x26, 0x27, 0x28,
            0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22,
            0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0xBD, 0xBB, 0xDB, 0xDD, 0xDC, 0xBA, 0xDE, 0xBC, 0xBE, 0xBF, 0xC0,
        };

        private static readonly HashSet<int> Tracked = new(BaseKeys);
        private static readonly HashSet<int> Down = new();
        private static readonly HashSet<int> PrevDown = new();
        private static readonly HashSet<int> JustPressed = new();

        /// <summary>
        /// 原版按键绑定里当前用到的键集（由 <see cref="SetTrackedKeys"/> 写入）。
        /// null = 还没读到过绑定，先退回基础集。
        /// </summary>
        private static HashSet<int> _nativeKeys;

        /// <summary>菜单用：按键重复计时。</summary>
        private static readonly Dictionary<int, double> RepeatTimer = new();

        private const double RepeatDelay = 0.40;
        private const double RepeatRate = 0.075;

        /// <summary>
        /// 每成功刷新一次按键状态就 +1。
        ///
        /// 主循环用**它有没有变**来判断"本帧输入是否已经处理过"：
        /// DCCM 一帧内可能多次回调（Hook_Boot_update 会广播 IOnFrameUpdate，
        /// 同时转发 IOnHeroUpdate），第二次进来时 <c>JustPressed</c> 还是同一份内容、
        /// 没有被消费 —— 同一个 F10 就会被判两次，菜单疯狂开关。
        /// </summary>
        public static long Generation { get; private set; }

        /// <summary>
        /// 把一个键加进跟踪集（单点补充用；主力是 <see cref="SetTrackedKeys"/>）。
        /// </summary>
        public static void EnsureKey(int vk)
        {
            if (vk > 0) Tracked.Add(vk);
        }

        /// <summary>
        /// 用一个键集**替换**跟踪集（少用；平时靠 <see cref="EnsureKey"/> 累加）。
        /// 被替换掉的旧键会顺手从 Down / PrevDown / JustPressed 里摘掉 ——
        /// 否则旧键如果当时正按着，会永远留在 Down 里，新键的第一次按被判成"重复"而吃掉。
        /// </summary>
        public static void SetTrackedKeys(IEnumerable<int> keys)
        {
            if (keys == null) return;

            _nativeKeys ??= new HashSet<int>();
            _nativeKeys.Clear();

            foreach (int k in keys)
            {
                if (k > 0) _nativeKeys.Add(k);
            }

            Tracked.Clear();
            foreach (int k in BaseKeys) Tracked.Add(k);
            foreach (int k in _nativeKeys) Tracked.Add(k);

            Down.RemoveWhere(k => !Tracked.Contains(k));
            PrevDown.RemoveWhere(k => !Tracked.Contains(k));
            JustPressed.RemoveWhere(k => !Tracked.Contains(k));
        }

        /// <summary>
        /// 问一次"这个键现在按住没有"。
        ///
        /// ============================ 为什么优先问游戏 ============================
        /// 原来只用 <c>GetAsyncKeyState</c>。实机诊断（日志 `触发诊断`）显示：
        /// 总开关正常、跟踪集 87、键码也对（J=74），但**所有键恒为"松开"** ——
        /// 于是招式永远不触发。同一时刻菜单里的"按键捕获"却是好的
        /// （靠捕获 GUI 的 <c>GetAsyncKeyState</c> 路径），说明这条 API 在
        /// 游戏主循环里并不可靠（大概率与窗口激活状态 / 焦点有关）。
        ///
        /// 所以改成**优先读游戏自己的按键状态表** <c>dc.hxd.Key.isDown</c>：
        /// 那是游戏每帧在处理的同一份数据（原版 <c>Beheaded</c> 就读它），
        /// 不依赖窗口焦点、也不依赖任何阻塞式 API。
        /// 读不到（主菜单 / 还没初始化）再退回 <c>GetAsyncKeyState</c>。
        /// </summary>
        private static bool IsDownRaw(int vk)
        {
            if (vk <= 0) return false;
            return IsDownViaGame(vk) || IsDownViaWin32(vk);
        }

        /// <summary>
        /// 游戏自己的按键状态表：<c>dc.hxd.Key.isDown(code)</c>。
        /// 原版 <c>Beheaded</c> 就是读它判断方向键的（<c>Beheaded.cs:280-330</c>），
        /// 所以这是"游戏认为这个键按着没有"的权威答案。
        /// </summary>
        public static bool IsDownViaGame(int vk)
        {
            if (vk <= 0) return false;
            try
            {
                dc.hxd._Key k = dc.hxd.Key.Class;
                if (k == null) return false;
                HaxeProxy.Runtime.HlFunc<bool, int> f = k.isDown;
                return f != null && f.Invoke(vk);
            }
            catch { return false; }
        }

        /// <summary>Win32 <c>GetAsyncKeyState</c>（依赖窗口焦点，只作兜底 / 捕获用）。</summary>
        public static bool IsDownViaWin32(int vk)
        {
            if (vk <= 0) return false;
            try { return (GetAsyncKeyState(vk) & KEY_DOWN_BIT) != 0; }
            catch { return false; }
        }

        /// <summary>每帧调一次：刷新所有被跟踪键的状态。</summary>
        public static void Poll()
        {
            PrevDown.Clear();
            foreach (int k in Down) PrevDown.Add(k);
            Down.Clear();
            JustPressed.Clear();

            foreach (int k in Tracked)
            {
                if (IsDownRaw(k)) Down.Add(k);
            }

            foreach (int k in Down)
            {
                if (!PrevDown.Contains(k)) JustPressed.Add(k);
            }

            Generation++;

            // 诊断：有键被按住时把"轮询这一路看到了什么"报出来（节流 2 秒）。
            // 有了它就能区分"键没读到"（Tracked=0）和"读到了但边沿没算出来"（JustPressed=0）。
            if (Down.Count > 0)
            {
                long now = System.Environment.TickCount64;
                if (now - _pollDiagAt > 2000)
                {
                    _pollDiagAt = now;
                    var sb = new System.Text.StringBuilder();
                    foreach (int k in Down) { if (sb.Length > 0) sb.Append(','); sb.Append(k); }
                    Log.Info($"[轮询] Down={Down.Count} JustPressed={JustPressed.Count} "
                             + $"Tracked={Tracked.Count} 按住的键=[{sb}]");
                }
            }
        }

        private static long _pollDiagAt;

        /// <summary>这一帧刚按下（边沿触发）。</summary>
        public static bool Pressed(int vk) => vk != 0 && JustPressed.Contains(vk);

        /// <summary>
        /// 这个键**此刻**是否按住（不经过任何跟踪集 / 边沿状态）。
        ///
        /// 给招式触发用的入口 —— 照抄 <c>ZoomVision.IsKeyDown</c>：直接读键，
        /// 调用方自己记一个 bool 做边沿。
        /// （原来走 <see cref="Poll"/> 的跟踪集 + <see cref="Pressed"/>，
        ///   实机证明按下会在那一层被丢掉。）
        /// </summary>
        public static bool IsDownNow(int vk) => IsDownRaw(vk);

        /// <summary>当前一直按住（走跟踪集；菜单等 UI 用）。</summary>
        public static bool Held(int vk) => vk != 0 && Down.Contains(vk);

        /// <summary>当前有几个被跟踪的键是按住状态（0 = 全松开了）。</summary>
        public static int HeldCount => Down.Count;

        /// <summary>跟踪集大小（诊断用）。</summary>
        public static int TrackedCount => Tracked.Count;

        /// <summary>
        /// 这一帧刚按下的**任意**跟踪键（复用于"按键捕获"界面）。
        /// 0 = 没有。
        ///
        /// ⚠️ 会跳过方向键 / 空格 / 回车 / Esc / Tab / Shift —— 那些键归原版
        /// 菜单和移动用，捕获界面误吞了会让玩家走不动路。
        /// </summary>
        public static int FirstPressed()
        {
            foreach (int k in JustPressed)
            {
                if (!IsGameBoundKey(k)) return k;
            }
            return 0;
        }

        /// <summary>是不是"游戏本来就在用"的键（移动 / UI 导航）。</summary>
        public static bool IsGameBoundKey(int vk) => vk switch
        {
            0x25 or 0x26 or 0x27 or 0x28 => true,   // 方向键
            0x20 or 0x0D or 0x1B or 0x09 or 0x08 => true,   // 空格 / 回车 / Esc / Tab / Backspace
            0x10 or 0x11 or 0x12 => true,           // Shift / Ctrl / Alt
            _ => false,
        };

        /// <summary>这一帧新按下的键有几个（0 = 完全没输入）。</summary>
        public static int JustPressedCount => JustPressed.Count;

        /// <summary>所有被跟踪的键（按键捕获界面列给玩家看）。</summary>
        public static IEnumerable<int> TrackedKeys => Tracked;

        /// <summary>
        /// 菜单用按键：带自动重复 —— 刚按下立刻返回一次，
        /// 之后按住 0.4 秒开始每 0.075 秒一次。方向键调数值全靠它。
        /// </summary>
        public static bool PressedRepeat(int vk, double dt)
        {
            if (vk == 0) return false;

            if (JustPressed.Contains(vk))
            {
                RepeatTimer[vk] = -RepeatDelay;   // 负数 = 还在首次延迟里
                return true;
            }

            if (!Down.Contains(vk))
            {
                RepeatTimer.Remove(vk);
                return false;
            }

            if (!RepeatTimer.TryGetValue(vk, out double t))
            {
                RepeatTimer[vk] = -RepeatDelay;
                return false;
            }

            t += dt;
            if (t < 0.0)
            {
                RepeatTimer[vk] = t;
                return false;
            }

            if (t >= RepeatRate)
            {
                RepeatTimer[vk] = 0.0;
                return true;
            }

            RepeatTimer[vk] = t;
            return false;
        }

        /// <summary>清空状态（换关 / 重开时调用，避免残留的"按下"误触发）。</summary>
        public static void Reset()
        {
            Down.Clear();
            PrevDown.Clear();
            JustPressed.Clear();
            RepeatTimer.Clear();
        }
    }
}
