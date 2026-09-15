using System;
using Hashlink;
using Hashlink.Proxy.Objects;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using dc.en;
using dc.en.inter;
using dc.libs.heaps.slib;
using dc.tool;

namespace ChronoBlade
{
    /// <summary>
    /// 两个选择面板共用的日志、hashlink 字符串工具与收尾兜底。
    /// </summary>
    internal static class ChronoPanelLog
    {
        private static Serilog.ILogger? _logger;
        public static void Attach(Serilog.ILogger logger) => _logger = logger;

        public static void Write(string msg)
        {
            string line = $"[ChronoBlade] {msg}";
            System.Console.WriteLine(line);
            try { _logger?.Information(line); } catch { }
        }

        /// <summary>
        /// hashlink 字符串 → 纯文本。
        /// CDB 里的 id 序列化后是 "id=ChronoBlade" 这种带键名前缀的形式，直接比较会永远不相等。
        /// （ChronoWeaponFactory.NormalizeId 是同一个原因。）
        /// </summary>
        public static string Plain(dc.String? s)
        {
            string t;
            try { t = s?.ToString() ?? ""; } catch { return ""; }
            int eq = t.IndexOf('=');
            if (eq >= 0) t = t.Substring(eq + 1);
            return t.Trim().Trim('"', '\'', ' ');
        }

        public static dc.String Hx(string s) => new HashlinkString(s).AsHaxe<dc.String>();

        /// <summary>
        /// 把条目网格**钉在选择框的顶部**。
        ///
        /// 原版 `updateScrollingBox()` 在"内容比框矮"时会把 `wrapperItem` 往下推：
        ///
        /// ```
        /// num6  = mask.height - 内容高 - 5*px        // 框比内容高的"富余量"
        /// num10 = (cy2 &lt; num6) ? num6 : ...           // 富余量更大 → 直接推到 num6
        /// ```
        ///
        /// 也就是**"框有富余高度就把内容压到底部"**。我们为了让框装得下说明文字把框撑高了，
        /// 富余量很大 → 条目整块被推到框底、上面空一大片（"对齐到最下面"就是这个）。
        ///
        /// 为什么不重写 `updateSelection` 去拦：GameProxy 里
        /// `updateSelection(ref bool)` 是**普通方法**，只有 `updateSelection(Ref&lt;bool&gt;)`
        /// 才是 virtual —— 重写哪个都不能保证拦得住。
        /// `postUpdate()` 是 virtual、每帧在 update 之后 / 渲染之前执行，在这里钉位置最稳。
        ///
        /// 注意这里用 `(int)(pixelScale*5)`：原版 `onResize` 就是这样截断的，
        /// 不跟着截断会每帧差不到 1px、白白一直置 `posChanged`。
        /// </summary>
        public static void PinGridToTop(dc.ui.sel.GridSelector panel)
        {
            try
            {
                var wrapper = panel?.wrapperItem;
                if (wrapper == null) return;

                double top = 5.0;
                try { top = (int)(panel!.get_pixelScale.Invoke() * 5.0); } catch { }

                if (wrapper.y != top)
                {
                    wrapper.posChanged = true;
                    wrapper.y = top;
                }
            }
            catch { }
        }

        /// <summary>
        /// 解暂停 + 显示 HUD + 关掉残留的半成品面板。
        ///
        /// 用在"基类构造函数已经调过 pauseGame()，之后又抛异常"这条路径上：
        /// 那时游戏停在暂停里，而半成品进程已经挂到 Main 上了，两边都要收。
        /// 只在构造失败时调用，所以不会误伤原版界面。
        /// </summary>
        public static void EmergencyCleanup()
        {
            try { dc.pr.Game.Class.ME?.resume(); } catch { }
            try { dc.ui.HUD.Class.ME?.show(null); } catch { }

            try
            {
                var kids = dc.Main.Class.ME?.children;
                if (kids == null) return;

                for (int i = kids.length - 1; i >= 0; i--)
                {
                    if (kids.getDyn(i) is not dc.ui.sel.GridSelector gs) continue;
                    ChronoPanelLog.Write("清理残留的半成品选择面板进程");
                    try { gs.close(); } catch { }
                }
            }
            catch { }
        }
    }

    // ============================================================================================
    //  选择武器面板（热键 P）
    // ============================================================================================

    /// <summary>
    /// 选择武器面板 —— 只列本模组新增的两把武器（时之刃 / Zaphkiel）。
    ///
    /// ## 为什么直接继承原版的 FreeWeaponSelector
    ///
    /// 原版训练场那三个按钮（<see cref="TrainingWeaponSpawner"/>）按下时做的就是：
    ///
    /// ```csharp
    /// // Haxe 原型：new FreeWeaponSelector(spawnItem, tier, ref weaponLevel, ref quality,
    /// //                                   ref colorless, ref legendary, this);
    /// // 在 GameProxy 里那几个 ref 参数是 Ref<T>（不是 C# 的 ref）：
    /// new FreeWeaponSelector(spawnItem, tier,
    ///                        new Ref<int>(ref weaponLevel), new Ref<int>(ref quality),
    ///                        new Ref<bool>(ref colorless), new Ref<bool>(ref legendary),
    ///                        this);
    /// ```
    ///
    /// `FreeWeaponSelector` 基类是 `TieredItemSelector → ItemSelector → GridSelector`，
    /// 而 `GridSelector` 的构造函数里（`_GridSelector.__inst_construct__`）依次做了：
    ///
    /// ```
    /// _Process.__inst_construct__(arg1, Main.Class.ME);   // ← 挂到 Main 进程栈（不是 Game 底下！）
    /// arg1.pauseGame();                                    // ← HUD.hide() + Game.modalPause() = 真暂停
    /// arg1.createRootInLayers(parent.root, ROOT_DP_MENU);
    /// arg1.setControlLabel();
    /// arg1.initRightFlow();
    /// arg1.initGrid();                                     // ← 虚方法，会被下面重写
    /// arg1.onResize();
    /// ```
    ///
    /// 关键点：**面板是 Main 的子进程，而暂停的是 Game 这个兄弟进程。**
    /// `Process.updateAll` 每帧从 ROOTS（Main）开始递归，遇到 `paused == true` 的节点就直接跳过 ——
    /// 所以 Game（连同它底下的整个 Level：英雄、怪物、弹幕、粒子、动画）全停在那一帧，
    /// 而我们的面板照常 update，输入/关闭全由游戏主循环负责。
    ///
    /// 关闭时 `GridSelector.close()` 自己会 `Game.Class.ME.resume()`，`onDispose()` 会把 HUD 显示回来。
    /// **解除暂停的代码不在"它自己要关掉的那扇门后面"** —— 这正是上一版 `Game.paused` 手写切换
    /// 永远恢复不了的原因（paused=true 之后 Game.update 钩子再也不被调用）。
    ///
    /// ## 只列两把武器怎么做到
    ///
    /// `ItemSelector.initGrid()` 会遍历 `Data.item.all`，用 `itemIsFiltered(item)` 过滤后
    /// 把 id 塞进 `items`。这里只重写 `itemIsFiltered`，让它仅放行本模组的两个 id ——
    /// 不碰数组、不碰布局，等级/品质/无色/传奇那一整套原版控件全部原样继承。
    /// </summary>
    public sealed class ChronoWeaponPanel : dc.ui.sel.FreeWeaponSelector
    {
        /// <summary>面板标题。</summary>
        public const string Title = "选择武器";

        /// <summary>面板里**只**列这两把（CDB item id）。</summary>
        public static readonly string[] OnlyIds = { "ChronoBlade", TimeBullet.name };

        /// <summary>
        /// 选择框的上下内边距。原版是 5，这里调大一点把框撑高。
        ///
        /// 为什么需要：`GridSelector.onResize()` 的高度公式是
        ///
        /// ```
        /// 内容高   = max( (entry.cy - entry.sectionIdx) * (条目高 + pixelScale*10) )
        /// 选择框高 = pixelScale * (内容高 + 行数 * padV*2 + 22)
        /// ```
        ///
        /// 两把武器在 6 列网格里只占**一行**（cy 全是 0）→ 内容高算成 0
        /// → 框高只剩 `pixelScale*32`，比一个武器卡片图标还矮一点，底边被裁
        /// （现象就是"高度只能显示 80% 个武器"）。
        ///
        /// ⚠️ padV **只**进高度公式；宽度用的是 padH
        ///    （`宽 = wid * (entryWid + padH*2)`），所以调它不会改变宽度 ——
        ///    宽度仍然是严格的原版尺寸。12 大约让框从 32 单位高变成 46 单位。
        /// </summary>
        private const double BoxPadV = 12.0;

        /// <summary>当前打开的面板（同一时刻只允许一个）。</summary>
        public static ChronoWeaponPanel? Current { get; private set; }

        public static bool IsOpen => Current != null && !Current.destroyed;

        public ChronoWeaponPanel(HlAction<dc.String> validateCb, dc.String tier,
                                 Ref<int> level, Ref<int> quality,
                                 Ref<bool> colorless, Ref<bool> legendary)
            : base(validateCb, tier, level, quality, colorless, legendary, null)
        {
            // ⚠ weaponSpawner 传 null 是**故意**的：
            //   FreeWeaponSelector.onValidate() 里对 null 有专门的短路分支
            //   （`if (weaponSpawner == null) { base.onValidate(); return; }`），
            //   而 base（ItemSelector.onValidate）会照常 invoke 我们的 validateCb 再关闭面板。
            //   于是我们拿到了"等级/品质控件 + 确认回调"，又不需要训练场那个实体。
            Current = this;
        }

        /// <summary>只放行本模组的两把武器；原版的组别/tier 过滤整个绕开。</summary>
        public override bool itemIsFiltered(
            virtual_ambiantDesc_castCD_cellCost_commonProps_dlc_droppable_gameplayDesc_group_icon_id_legendAffixes_moneyCost_name_props_synergy_tags_tier1_tier2_ item)
        {
            if (item == null) return false;

            string id = ChronoPanelLog.Plain(item.id);
            if (id.Length == 0) return false;

            foreach (string want in OnlyIds)
            {
                if (string.Equals(id, want, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public override dc.String getTitleText() => ChronoPanelLog.Hx(Title);

        /// <summary>
        /// 永远可选。
        /// 这是"自选召唤"面板而不是图鉴解锁判定 —— 就算默认解锁那一步没跑成
        /// （ChronoBladeMod.UnlockDefaultItems），也不该让玩家点不动。
        /// </summary>
        public override bool isEntryLocked(int i) => false;

        /// <summary>
        /// 严格使用原版尺寸 —— 这里**不重写任何尺寸相关的方法**。
        ///
        /// `ItemSelector` 给的就是原版训练场那套：`get_wid() = 6` 列、格子 24×24，
        /// 于是 `GridSelector.onResize()` 算出来的选择框就是原版大小
        /// （宽 = pixelScale*(6*(24+10)+22)，高 = pixelScale*32）。
        ///
        /// ⚠️ 别照着别处的"想把框变高"去改 `get_wid()`：把它收成 1 列确实会让两把武器
        ///    分成两行、框高变成约 3 倍，但那已经不是原版尺寸了。
        ///    另外**绝对不能**直接把 `hei` 调大 —— `moveSelection()` 拿 `hei` 当上下移动边界，
        ///    调大之后能移动到不存在的行，`getEntryAt()` 返回 null → 当场崩。
        /// </summary>
        /// <summary>
        /// 建网格（原版 `ItemSelector.initGrid` 按 `itemIsFiltered` 组装 items / entries），
        /// 完了把 `fbItems.padV` 调大，补上"单行高度退化"缺的那截。
        ///
        /// 这里必须用 initGrid 当钩子：`_GridSelector.__inst_construct__` 的顺序是
        /// 建 fbItems → initRightFlow → **initGrid** → **onResize**，
        /// 而 onResize 是在运行时读 `fbItems.padV` 的 —— 只有在这之前改才有效。
        /// </summary>
        public override void initGrid()
        {
            base.initGrid();
            try { fbItems.padV = BoxPadV; } catch { }
        }

        /// <summary>
        /// 每帧把条目网格钉回框顶 —— 详细原因见 `ChronoPanelLog.PinGridToTop`。
        ///
        /// ⚠️ **顺序不能反**：原版 `GridSelector.postUpdate()` 就是在这里算选中框的
        ///    （`localToGlobal(entry)` → `selectionSG.x/y`），而它算的时候要用到 `wrapperItem.y`。
        ///    如果先 `base.postUpdate()` 再钉，选中框会按"还没钉住"的位置算一遍 ——
        ///    光标一动会触发滚动补间，选中框就先往下跑、下一帧才回到正确位置
        ///    （现象就是"先往下、再往左、然后往上对齐"）。
        ///    先把网格钉住，再让原版去算，选中框一次就算对。
        /// </summary>
        public override void postUpdate()
        {
            ChronoPanelLog.PinGridToTop(this);
            base.postUpdate();

            // 刻刻帝那一格的动态图标：手动逐帧推进（见 getIconBmp 的注释）
            AdvanceIconAnim();
        }

        // ------------------------------------------------------------------ 刻刻帝的动态图标
        //
        // 需求：Zaphkiel（刻刻帝）的图标用 `atlas/TIMEZHANJI.atlas` 做**动态**图标。
        //
        // 为什么只能在面板里做：物品图标本身是 CDB 里一条
        // `icon = { x, y, file, size }` 的**静态**子矩形（`_Icon.createItemIcon`
        // 就是 `_Assets.getItem()` 取一块 Tile 画成 Bitmap），没有"帧"的概念。
        // 面板这一格是模组自己画的，才有机会逐帧播。
        // HUD / 背包里那一份仍然是 CDB 的静态图标（要改得换 CDB 的图标资源）。
        //
        // 画法和弹药面板画罗马数字完全一致，那几个坑这里同样成立：
        //   · SpriteLib **每次现取**（换关后旧实例会失效）；
        //   · HSprite 的锚点必须是**左上角 (0,0)** —— GridSelector 是按 Bitmap 的
        //     [0,w]×[0,h] 包围盒摆格子的，用居中锚点整张图会偏半个身位；
        //   · 缩放加在 sprite 自己身上、外面**必须套一层 holder** ——
        //     `addEntryAt()` 的 onBeforeReflow 会把返回对象的 scaleX/Y 强制写成 pixelScale，
        //     直接返回 sprite 的话缩放下一帧就被覆盖；
        //   · 要 `pauseCurrentAnim()` + 自己 `setFrame()`，否则 HSprite 自带的
        //     AnimManager 也在推帧，两边打架（表现为画面乱跳或停在第 0 帧）。

        /// <summary>
        /// TIMEZHANJI 帧的原始格子尺寸（atlas 里 `orig: 298, 298`）。
        /// 用它来缩放，保证整个 cell 正好落在 24×24 的格子里、绝不溢出到隔壁格
        /// （帧的可见内容是 206×204，比 cell 小，所以会留一点边距）。
        /// 觉得图标偏小就把这个值调小一点。
        /// </summary>
        private const double IconArtCell = 298.0;

        /// <summary>格子逻辑尺寸（`GridSelector.get_entryWid()/get_entryHei()` 都是 24）。</summary>
        private const double IconBox = 24.0;

        /// <summary>动态图标的循环帧率（TIMEZHANJI 有 46 帧，15fps 约 3 秒一轮）。</summary>
        private const double IconFps = 15.0;

        /// <summary>本面板里正在逐帧播的图标 sprite。</summary>
        private readonly List<HSprite> _iconAnims = new();

        /// <summary>
        /// 只把**刻刻帝**那一格换成 TIMEZHANJI 的动态图标；时之刃仍然用 CDB 的静态图标。
        /// </summary>
        public override dc.h2d.Object getIconBmp(int i, dc.h2d.Object p)
        {
            try
            {
                if (!IsZaphkielEntry(i)) return base.getIconBmp(i, p);

                var lib = ChronoFx.GetCastLib();
                if (lib == null) return base.getIconBmp(i, p);

                var holder = new dc.h2d.Object(p);

                int startFrame = 0;
                var spr = new HSprite(lib, ChronoPanelLog.Hx(ChronoFx.CastGroupName),
                                      Ref<int>.From(ref startFrame), holder);
                if (spr == null) return base.getIconBmp(i, p);

                // 左上角锚点（理由见上面注释）
                var pivot = spr.pivot;
                pivot.centerFactorX = 0.0;
                pivot.centerFactorY = 0.0;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                double sc = IconBox / IconArtCell;
                spr.scaleX = sc;
                spr.scaleY = sc;
                spr.posChanged = true;

                // 交给我们自己推帧，别让 HSprite 自带的 AnimManager 也推
                try { spr.get_anim().pauseCurrentAnim(); } catch { }
                try { spr.setFrame(0); } catch { }

                _iconAnims.Add(spr);

                return holder;
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"刻刻帝动态图标创建失败，退回原图标: {ex.Message}");
                return base.getIconBmp(i, p);
            }
        }

        /// <summary>下标 i 是不是刻刻帝那一格（拿 ItemSelector.items 里的 item id 判断）。</summary>
        private bool IsZaphkielEntry(int i)
        {
            try
            {
                var arr = items;
                if (arr == null || i < 0 || i >= arr.length) return false;
                string id = arr.getDyn(i)?.ToString() ?? "";
                return string.Equals(id, TimeBullet.name, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        /// <summary>按时间推进图标帧（用墙钟，postUpdate 不给 dt）。</summary>
        private void AdvanceIconAnim()
        {
            if (_iconAnims.Count == 0) return;

            try
            {
                double t = Environment.TickCount64 / 1000.0;
                for (int k = 0; k < _iconAnims.Count; k++)
                {
                    var spr = _iconAnims[k];
                    if (spr == null) continue;

                    int frames = 1;
                    try { frames = spr.totalFrames(); } catch { }
                    if (frames <= 1) continue;

                    int f = (int)(t * IconFps) % frames;
                    if (f < 0) f = 0;
                    try { spr.setFrame(f); } catch { }
                }
            }
            catch { }
        }

        public override void onDispose()
        {
            base.onDispose();
            if (ReferenceEquals(Current, this)) Current = null;
        }

        // ------------------------------------------------------------------ 打开 / 关闭

        /// <summary>
        /// 打开面板。成功返回 true（此时游戏已经真暂停，由游戏自己负责恢复）。
        /// </summary>
        public static bool Open()
        {
            if (IsOpen) return false;

            var game = dc.pr.Game.Class.ME;
            if (game == null || game.destroyed) return false;

            // 别人（暂停菜单 / 其它选择界面）已经暂停了就别插队
            bool alreadyPaused = false;
            try { alreadyPaused = game.paused; } catch { }
            if (alreadyPaused)
            {
                ChronoPanelLog.Write("选择武器面板：游戏已处于暂停状态，不重复打开");
                return false;
            }

            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero.destroyed || hero._level == null)
            {
                ChronoPanelLog.Write("选择武器面板：英雄/关卡未就绪，不打开");
                return false;
            }

            // ★ 开面板前先确认 CDB 里真的有这两把武器。
            //   如果一行都查不到，网格会是空的 —— 而空网格会让基类构造函数在
            //   pauseGame() **之后**抛异常（updateRightFlow 里对空 entries 解引用），
            //   那是最难收拾的情况。所以宁可现在就不开。
            var missing = new System.Collections.Generic.List<string>();
            foreach (string id in OnlyIds)
            {
                if (!ItemExists(id)) missing.Add(id);
            }
            if (missing.Count > 0)
            {
                ChronoPanelLog.Write(
                    $"选择武器面板：CDB 里找不到 {string.Join(" / ", missing)}，面板不打开" +
                    "（检查 res.pak / data.cdb 补丁有没有生效）");
                return false;
            }

            // 面板的初始等级 / 品质：跟原版训练场一样默认 Lv1、品质 0（普通）
            int level = 1;
            int quality = 0;
            bool colorless = false;
            bool legendary = false;

            try
            {
                var cb = new HlAction<dc.String>(OnChosen);

                // 原版签名要的是 Ref<T>（Haxe 的按引用传参），不是 C# 的 ref 参数。
                // 面板会把值拷进自己的 level / quality / colorless / legendary 字段，
                // 所以这几个局部变量出了作用域也没关系。
                var panel = new ChronoWeaponPanel(cb, ChronoPanelLog.Hx(""),
                                                  new Ref<int>(ref level), new Ref<int>(ref quality),
                                                  new Ref<bool>(ref colorless), new Ref<bool>(ref legendary));

                // 自检：真的装进网格几项？（过滤逻辑万一改错，看这一行就知道）
                // 正常打开时**不打日志**（以前每次开面板都刷一行），只在网格为空时报警。
                int loaded = 0;
                try { loaded = panel.items?.length ?? 0; } catch { }

                if (loaded == 0)
                {
                    ChronoPanelLog.Write("选择武器面板：网格是空的，立即关闭（否则空网格会拖垮后续 UI）");
                    try { panel.close(); } catch { }
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // ⚠ 兜底：pauseGame() 在基类构造函数里就执行了，如果之后（建 UI 时）抛异常，
                //   游戏会停在暂停里再也出不来。这里必须手动把暂停解掉。
                ChronoPanelLog.Write($"打开选择武器面板失败，已强制恢复: {ex}");
                Current = null;
                ChronoPanelLog.EmergencyCleanup();
                return false;
            }
        }

        /// <summary>物品 id 是否真的进了 CDB（和 ChronoCdbProbe 用的是同一个入口）。</summary>
        private static bool ItemExists(string id)
        {
            try { return dc.Data.Class.item?.byId?.get(ChronoPanelLog.Hx(id)) != null; }
            catch { return false; }
        }

        /// <summary>面板确认（Enter）时的回调。此刻游戏仍然暂停、面板还没销毁。</summary>
        private static void OnChosen(dc.String chosen)
        {
            int level = 1;
            int quality = 0;
            bool colorless = false;
            bool legendary = false;

            var panel = Current;
            try
            {
                if (panel != null && !panel.destroyed)
                {
                    level = panel.level;
                    quality = panel.quality;
                    colorless = panel.colorless;
                    legendary = panel.isLegendary();
                }
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"读取面板等级/品质失败（用默认值）: {ex.Message}");
            }

            string id = ChronoPanelLog.Plain(chosen);
            if (id.Length == 0)
            {
                ChronoPanelLog.Write("面板确认回调拿到空 id，取消召唤");
                return;
            }

            SpawnWeaponDrop(id, LabelOf(id), level, quality, colorless, legendary);
        }

        private static string LabelOf(string id)
        {
            if (string.Equals(id, "ChronoBlade", StringComparison.Ordinal)) return "时之刃";
            if (string.Equals(id, TimeBullet.name, StringComparison.Ordinal)) return TimeBullet.DisplayName;
            return id;
        }

        // ------------------------------------------------------------------ 召唤

        /// <summary>
        /// 在英雄**上方**放一个武器掉落物（和原版训练场一样：选完从上方掉下来）。
        ///
        /// 走原版掉落流程（`ItemDrop` + `onDropAsLoot()`），把拾取判定、HUD 刷新、
        /// 技能初始化、武器替换 UI 全部交回给游戏本体 —— 这也是本模组一直以来的做法，
        /// 能绕开 `Inventory.add()` 的"武器格已满"异常和手工赋 `_itemData` 的强转坑。
        /// </summary>
        private static void SpawnWeaponDrop(string weaponId, string label,
                                            int level, int quality, bool colorless, bool legendary)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero.destroyed || hero._level == null)
            {
                ChronoPanelLog.Write($"召唤{label}失败：英雄/关卡未就绪");
                return;
            }

            try
            {
                var item = MakeItem(weaponId, level, quality, colorless, legendary);

                // 和原版 TrainingWeaponSpawner.spawnItem 一致：构造 → init() → onDropAsLoot()。
                // ⚠️ 落点是**英雄当前所在格**（不是上方 N 格）—— 需求要"在 hero 坐标生成"。
                bool inArmory = false;
                var drop = new ItemDrop(hero._level, hero.cx, hero.cy,
                                        item, true, new Ref<bool>(ref inArmory));
                drop.init();                 // 必须调用，否则崩
                drop.onDropAsLoot();         // 交给原版掉落 / 拾取流程

                try
                {
                    // 用 setPosCase 而不是 setPosPixel：保持落在合法格子上，
                    // 掉落物的落地/碰撞判定才不会错位。xr/yr 直接用英雄的，
                    // 这样它就落在英雄脚下同一格、同一位置。
                    drop.setPosCase(drop.cx, drop.cy, hero.xr, hero.yr);
                }
                catch (Exception ex)
                {
                    ChronoPanelLog.Write($"掉落位置调整失败（不影响掉落）: {ex.Message}");
                }

                // 召唤成功不再打日志（每次召唤一行，正常玩是噪音）；失败仍会报。
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"召唤{label}失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// 按面板里选的等级 / 品质造物品。
        ///
        /// ⚠️ 关键教训：**不能依赖 `TrainingWeaponSpawner.Class.lootGen`**。
        ///   那个 LootGen 只在 `_TrainingWeaponSpawner.__inst_construct__`（也就是训练场里
        ///   真的摆了一个武器生成器实体）时才被创建；普通关卡里它是 **null**。
        ///   原版训练场选武器能出传奇，是因为它一定在训练场里；我们在任意关卡按 P，
        ///   以前走到这里 gen 就是 null → 直接跳过 → **物品根本没有 "Legendary" 词条**，
        ///   于是"选传奇出来的是普通货、也不显示传奇词条"。
        ///
        /// 所以现在：
        ///   · LootGen 拿得到就照原版走（顺带处理等级/基础数值）；
        ///   · 拿不到就自己补齐 —— **显式补上 "Legendary" 词条** + 本模组的传奇词条。
        /// 任何一步失败都退回"裸物品"，绝不让等级/品质把召唤本身搞挂。
        /// </summary>
        private static InventItem MakeItem(string weaponId, int level, int quality,
                                           bool colorless, bool legendary)
        {
            var item = new InventItem(new InventItemKind.Weapon(ChronoPanelLog.Hx(weaponId)));

            bool finalized = false;
            try
            {
                var gen = TrainingWeaponSpawner.Class.lootGen;
                if (gen != null)
                {
                    bool overrideBaseLevel = true;
                    item = legendary
                        ? gen.finalizeLegendaryItem(item, level, ref overrideBaseLevel, null, null)
                        : gen.finalizeItem(item, level, ref overrideBaseLevel, null, null);
                    finalized = true;
                }
                else
                {
                    // LootGen 在普通关卡里一定是 null（它只在训练场的武器生成器实体里创建），
                    // 这是**正常**情况：等级/传奇词条由下面自己补。以前这里每次都打一行，
                    // 现在静默 —— 这里本来就不是异常路径。
                }
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"LootGen 处理等级失败（改用裸物品）: {ex.Message}");
            }

            if (legendary)
            {
                // ★ 先让物品**真的成为传奇**：原版是靠 finalizeLegendaryItem 里
                //   `addAffix("Legendary")` 做到的。少了这一步，物品既没有传奇外观，
                //   也不会把 legendAffixes 里的词条算进说明。
                if (!finalized || !HasAffix(item, "Legendary"))
                {
                    EnsureAffix(item, "Legendary");
                }

                // 再把本模组的传奇词条挂上（池子里只有一条，正常一定 Roll 得到）
                string affixId = LegendAffixFor(weaponId);
                if (affixId.Length > 0) EnsureAffix(item, affixId);

                // 传奇的"品质"由 Legendary 词条表达，**不叠 QualityUp** ——
                // 原版 spawnItem 在传奇分支里 set_weaponQuality(0)，那条 while 循环加 0 次。
            }
            else
            {
                for (int i = 0; i < quality; i++) EnsureAffix(item, "QualityUp");
            }

            if (colorless) EnsureAffix(item, "Colorless");

            // ---- 等级：无论上面走哪条路，最后都**强制**写成面板选的那个值 ----
            //
            // ⚠️ 这就是"按 P 只能召唤一级武器"的原因：
            //   普通关卡里 `lootGen` 是 null（它只在训练场的武器生成器实体构造时才创建），
            //   而等级**只有** `finalizeItem(...)` 那条路会写 —— 于是 fallback 分支里
            //   等级从头到尾没人设过，物品就一直是默认的 1 级。
            //
            //   等级存在 `InventItem._itemLevel`（`getRawItemLevel()` / `setItemLevel()`），
            //   `getAdjustedItemLevel()` 会在此基础上加"升级次数 ×2"、传奇再 +6，
            //   武器的伤害/需求属性都走那个值。所以写在这里就同时修好了显示与数值。
            //
            //   放在最后而不是只在 fallback 里写：面板的"等级"是玩家的明确选择，
            //   而训练场原版也是把面板值当**基准等级**用的（`overrideBaseLevel = true`），
            //   两条路都对齐到它，行为才可预期。
            try
            {
                item.setItemLevel(level);
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"写入物品等级失败（物品仍可召唤）: {ex.Message}");
            }

            return item;
        }

        /// <summary>已经带着这个词条就跳过，不会重复叠加。</summary>
        private static void EnsureAffix(InventItem item, string affixId)
        {
            if (HasAffix(item, affixId)) return;
            try
            {
                item.addAffix(ChronoPanelLog.Hx(affixId), Ref<bool>.Null);
                ChronoPanelLog.Write($"已附加词条: {affixId}");
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"附加词条 {affixId} 失败: {ex.Message}");
            }
        }

        /// <summary>这把武器对应的传奇词条 id（和 patch_chronoblade_cdb.py 里的一致）。</summary>
        private static string LegendAffixFor(string weaponId)
        {
            if (string.Equals(weaponId, "ChronoBlade", StringComparison.Ordinal)) return "IgnoreGlobalShield";
            if (string.Equals(weaponId, TimeBullet.name, StringComparison.Ordinal)) return ChronoBullets.LegendAffixId;
            return "";
        }

        private static bool HasAffix(InventItem item, string affixId)
        {
            try { return item.hasAffix(ChronoPanelLog.Hx(affixId)); }
            catch { return false; }
        }
    }

    // ============================================================================================
    //  选择弹药面板（热键 X）
    // ============================================================================================

    /// <summary>
    /// 选择弹药面板 —— Zaphkiel 的十二之弹（罗马数字 I…XII）。
    ///
    /// 和武器面板同一套机制：继承原版 `GridSelector`。它的构造函数同样把自己挂到
    /// `Main.Class.ME` 上并调 `pauseGame()`，所以打开时是**真暂停**：
    /// 英雄、怪物、弹幕、粒子、动画全部停在那一帧，而面板自己的 update 照常跑。
    ///
    /// 上一版这里是"冻结战场"（把怪物锁 AI + 移速归零），只停了怪物，
    /// 英雄/弹幕/动画照旧；现在换成和原版选择界面完全一致的做法，那个 hack 已经删掉。
    /// </summary>
    public sealed class ChronoAmmoPanel : dc.ui.sel.GridSelector
    {
        /// <summary>面板标题。</summary>
        public const string Title = "选择弹药";

        // ------------------------------------------------------------------ HUD 图标跟着弹药走
        //
        // 需求：切换装填哪一发，主手上刻刻帝的图标就变成那一发的图标。
        //
        // 做法（**不需要任何钩子**）：
        //   · 12 个罗马数字已经由 `make_icon_sheet.py` 的 numerals 批次嫁接进
        //     `cardIcons.png` 的空格里（坐标表见 `_icon_cells.txt`）；
        //   · 原版 `dc.ui.HUD.updateIcon(InventItem i, Tile t)` 正好就是干这个的 ——
        //     它会遍历 HUD 的 skillWeapons / skillPowers，把 `ii == i` 那一格的图标
        //     换成传进去的 Tile。所以只要自己从图标表里切出对应那一格交给它即可。
        //
        // 为什么不用钩子/不用改 CDB：CDB 的 `icon` 是**静态一格**（而且 `file` 是死数据，
        // 见 make_icon_sheet.py 的注释），改不动"随装填变化"；`updateIcon` 是原版给
        // 状态变化用的正规入口，比 hook 图标创建稳得多。

        /// <summary>
        /// 第 i 发子弹（0 基）对应的 cardIcons.png 格子。
        ///
        /// 这批数字是**用户自己画好放进 `Assets/cardIcons.png` 的**，
        /// 位置是像素区 (0, 576) → (143, 623)：
        ///   · 第一行 y=576（= 第 24 行格子）→ 一…六；
        ///   · 第二行 y=600（= 第 25 行格子）→ 七…十二；
        ///   每格 24×24，x 从 0 开始每列 +24（= 格 0…5）。
        /// 所以子弹 i → (x = i % 6, y = 24 + i / 6)。
        ///
        /// ⚠️ 这批**不是** `make_icon_sheet.py` 嫁接的（早期版本嫁接的是 TIMEKASAN 的
        ///    12 帧，颜色很淡，已弃用）。改这里的坐标前先确认图上的实际位置。
        /// </summary>
        private static readonly (int X, int Y)[] BulletIconCells =
        {
            (0, 24), (1, 24), (2, 24), (3, 24), (4, 24), (5, 24),   // I   … VI
            (0, 25), (1, 25), (2, 25), (3, 25), (4, 25), (5, 25),   // VII … XII
        };

        /// <summary>图标格子边长（CDB 里 icon.size，也是 GridSelector 的条目尺寸）。</summary>
        private const int IconCellSize = 24;

        /// <summary>上一次真正写进 HUD 的是第几发（-1 = 还没写过 / 手里没有刻刻帝）。</summary>
        private static int _lastHudIconBullet = -1;

        /// <summary>
        /// 把主手刻刻帝的 HUD 图标同步成"当前装填的那一发"的数字。每帧调一次，
        /// 只有"装填的那一发真的变了"才会去动 HUD（切弹药、刚捡起来、传奇与否都不额外开销）。
        /// </summary>
        public static void SyncHudIcon(TimeBullet? gun)
        {
            try
            {
                if (gun == null)
                {
                    _lastHudIconBullet = -1;      // 手放下了，下次拿起来要重写一遍
                    return;
                }

                int idx = gun.BulletIndex;
                if (idx < 0) idx = 0;
                if (idx >= BulletIconCells.Length) idx = BulletIconCells.Length - 1;
                if (idx == _lastHudIconBullet) return;

                var item = gun.item;              // ⚠️ Weapon.item（InventItem），不是 wInfos.item
                if (item == null) return;

                var sheet = dc.Assets.Class.itemIcons;
                if (sheet == null) return;

                var (cx, cy) = BulletIconCells[idx];
                var tile = sheet.sub(cx * IconCellSize, cy * IconCellSize,
                                     IconCellSize, IconCellSize, Ref<int>.Null, Ref<int>.Null);
                if (tile == null) return;

                dc.ui.HUD.Class.ME?.updateIcon(item, tile);
                _lastHudIconBullet = idx;
                // 同步成功不打日志（每次换弹一行，正常玩是噪音）；失败仍会报。
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"同步 HUD 弹药图标失败: {ex.Message}");
            }
        }

        /// <summary>格子尺寸：要放得下 "XII" 这种三字符罗马数字，比原版 24 宽一些。</summary>
        private const int EntryWid = 36;
        private const int EntryHei = 32;

        /// <summary>说明文字距选择框内左边的距离（取不到条目左边距时的兜底值）。</summary>
        private const double DescLeftPad = 12.0;

        /// <summary>说明文字离选择框内底边的距离（贴底基准）。</summary>
        private const double DescBottomPad = 8.0;

        /// <summary>
        /// 说明文字在贴底的基础上**再往上抬几个"单位"**。
        ///
        /// 需求原文：「子弹介绍可以看见了，但是只能看见一半，再往上移动一个单位」——
        /// 所以从 1 个单位调到 2 个。"一个单位" = **一行文字的高度**（`text.font.lineHeight`）。
        ///
        /// 只看见一半的真正原因见 <see cref="LayoutDescText"/>：文字高度量不到（=0）时，
        /// 贴底公式算出的 y 会偏大一整个文字高度，下半截正好被框底切掉。
        /// 抬行只能缓解，所以那边同时加了"量不到就按行数估"的兜底 —— 两个一起才治本。
        /// </summary>
        private const int DescRaiseLines = 2;

        /// <summary>
        /// 说明文字上缘最多贴到框内顶部留这么多。
        /// 兜底用：万一框太矮、文字又太高，`maskH - h` 会算出负数把整段文字顶到框外
        /// （那才是真的"完全看不到介绍"），夹一下至少保证内容在可视区里。
        /// </summary>
        private const double DescTopPad = 4.0;

        /// <summary>行高量不到时的兜底（单位：UI 单位）。</summary>
        private const double LineHeightFallback = 12.0;

        /// <summary>最近一次写进说明文字的行数（`textHeight` 量不到时用它估高度）。</summary>
        private int _descLineCount = 1;

        private static bool _descGeomLogged;

        /// <summary>
        /// 选择框的上下内边距（原版 5）。调大是为了在**框内底部**腾出放弹药说明的高度。
        /// padV 只进高度公式（宽度用 padH），所以不会把框撑宽。
        /// </summary>
        private const double BoxPadV = 14.0;

        /// <summary>
        /// TIMEKASAN 的帧约 480×430（世界空间素材），缩到**正好铺满格子**：
        /// 32/430 → 约 35.7×32，塞进 36×32 的格子。
        /// 铺满之后"图集右下角"自然就落在"选择框右下角"上（框比格子每边大 5 单位）。
        /// 缩放加在 sprite 自己身上是**没用**的：GridSelector 会覆盖它 —— 见 getIconBmp。
        /// </summary>
        private const double NumeralScale = (double)EntryHei / 430.0;

        /// <summary>
        /// 初始光标。
        /// ⚠ 必须在 `new` 之前写好：`initGrid()` 是在**基类构造函数里**被调用的，
        ///   那时候本类的实例字段还没赋值，拿不到构造参数。
        /// </summary>
        private static int _pendingStart;

        public static ChronoAmmoPanel? Current { get; private set; }

        public static bool IsOpen => Current != null && !Current.destroyed;

        private readonly TimeBullet? _gun;

        /// <summary>子弹作用说明（挂在 GridSelector 的 rightFlow 里，跟在选择框下方）。</summary>
        private dc.ui.Text? _descText;

        public ChronoAmmoPanel(TimeBullet? gun)
        {
            // 注意：走到这里时基类已经挂好进程栈、暂停了游戏、并且调过 initGrid() 了。
            _gun = gun;
            Current = this;

            // 基类里第一次 updateRightFlow()（selectEntryAt → updateSelection）跑的时候
            // 本类的字段还没赋值，拿不到 _gun（也就不知道是不是传奇）；
            // 这里补一次，让说明文案第一帧就是对的。
            try { updateRightFlow(); } catch { }
        }

        // ------------------------------------------------------------------ 网格内容

        public override void initGrid()
        {
            int n = ChronoBullets.All.Length;
            if (n <= 0)
            {
                ChronoPanelLog.Write("选择弹药面板：子弹表为空，面板立即关闭");
                close();
                return;
            }

            initEntries(n);

            // 给"框内底部的弹药说明"腾高度。onResize 还在后面，所以这里改还来得及。
            try { fbItems.padV = BoxPadV; } catch { }

            // 光标落在当前装填的那一发上
            int start = _pendingStart;
            _pendingStart = 0;
            if (start < 0) start = 0;
            if (start >= n) start = n - 1;

            int wid = get_wid();
            if (wid <= 0) wid = 1;

            bool scroll = false;
            selectEntryAt(start % wid, start / wid, ref scroll);
        }

        public override dc.String getTitleText() => ChronoPanelLog.Hx(Title);

        /// <summary>自选换弹，永远可选。</summary>
        public override bool isEntryLocked(int i) => false;

        // ------------------------------------------------------------------ 当前弹药说明
        //
        // GridSelector 的 initRightFlow / updateRightFlow 默认都是空实现（框架留的钩子），
        // ItemSelector 就是用它画右侧"物品说明"的。弹药不是 CDB 物品，没有 NewItemDesc 可用，
        // 所以这里自己放一个 Text 到 rightFlow 里，光标一动就刷新。

        public override void initRightFlow()
        {
            base.initRightFlow();      // GridSelector 里是空的，留个位置而已

            try
            {
                // 说明文字要出现在**选择框内部**，所以挂在 `mask` 上 ——
                // mask 就是框内的可视区（随框一起被裁），而且它是绝对定位的，
                // 不影响任何 Flow 布局。
                //
                // ⚠️ 千万别挂到 mainFlow 上：那样文字高度一变，mainFlow 就会重新居中，
                //    光标换到下一行时整块面板跟着上下跳 ——
                //    这正是"到下一行集体往下移"的原因。
                var text = new dc.ui.Text(mask, null, null, Ref<double>.Null, null, null);
                text.canHaveBackground = false;
                // 左对齐（多行说明也要每行都从左边开始）
                try { text.set_textAlign(new dc.h2d.Align.Left()); } catch { }
                _descText = text;
            }
            catch (Exception ex)
            {
                _descText = null;
                ChronoPanelLog.Write($"弹药说明文字创建失败（面板仍可用）: {ex.Message}");
            }
        }

        /// <summary>
        /// 每帧把条目网格钉回框顶 —— 详细原因见 `ChronoPanelLog.PinGridToTop`。
        /// 注意必须**先钉再调 base**（原版在 postUpdate 里算选中框，见武器面板的同名注释）。
        /// </summary>
        public override void postUpdate()
        {
            ChronoPanelLog.PinGridToTop(this);
            base.postUpdate();
        }

        /// <summary>原版 onResize 会把 mask.width / mask.height 算好，之后才能贴底部。</summary>
        public override void onResize()
        {
            base.onResize();
            LayoutDescText();
        }

        /// <summary>
        /// 把说明文字贴到"选择框内部"的**左下角**（左对齐 + 贴底 + 再往上抬 <see cref="DescRaiseLines"/> 行）。
        ///
        /// ⚠️ 这里的 `h` 必须真的等于文字高度，否则会"只看见一半"：
        ///    贴底公式是 `y = 框高 - h - 贴底间距 - 抬行`，如果 `text.textHeight` 量出来是 0
        ///    （文字刚 set_text、还没走完布局那一步），y 就会偏大一整个文字高度 ——
        ///    文字的下半截正好落在框外被裁掉。所以量不到时按 `行数 × 行高` 估一个。
        /// </summary>
        private void LayoutDescText()
        {
            var text = _descText;
            if (text == null) return;

            try
            {
                double lineH = 0;
                try { lineH = text.font?.lineHeight ?? 0; } catch { }
                if (lineH <= 0) lineH = LineHeightFallback;

                double maskH = 0;
                try { maskH = mask.height; } catch { }

                double h = 0;
                try { h = text.textHeight; } catch { }

                // 量不到（或明显不合理）就按行数估
                double est = System.Math.Max(1, _descLineCount) * lineH;
                if (h <= 0 || h > maskH + est) h = est;

                // 左边对齐到条目网格的左边缘（取不到就用固定内边距）
                double left = 0;
                try { left = wrapperItem.x; } catch { }
                if (left <= 0) left = DescLeftPad;

                double raise = lineH * DescRaiseLines;
                double y = maskH - h - DescBottomPad - raise;

                // 兜底：框太矮时别把文字顶出可视区（否则就是"看不到介绍"）
                if (y < DescTopPad) y = DescTopPad;

                if (!_descGeomLogged)
                {
                    _descGeomLogged = true;
                    ChronoPanelLog.Write($"弹药说明排版：框高={maskH:0.#} 行高={lineH:0.#} " +
                                         $"行数={_descLineCount} 文字高={h:0.#} 上抬={raise:0.#} 最终y={y:0.#}");
                }

                text.posChanged = true;
                text.x = left;
                text.posChanged = true;
                text.y = y;
            }
            catch { }
        }

        public override void updateRightFlow()
        {
            UpdateDescText();
        }

        /// <summary>把光标所在那一发的作用写进说明文字。</summary>
        private void UpdateDescText()
        {
            var text = _descText;
            if (text == null) return;

            try
            {
                int index = -1;
                var entry = getEntryAt(curX, curY);
                if (entry != null) index = entry.i;
                if (index < 0 || index >= ChronoBullets.All.Length) return;

                bool boost = false;
                try { boost = _gun?.IsLegendaryDouble ?? false; } catch { }

                var def = ChronoBullets.Get(index);
                var sb = new System.Text.StringBuilder();
                // ⚠️ 分隔符只用 ASCII 和汉字：`·`(U+00B7) 这类符号游戏字体可能没有字形，会变成方块
                sb.Append("第 ").Append(index + 1).Append(" / ").Append(ChronoBullets.All.Length)
                  .Append(" 发  ").Append(def.Name).Append('\n');
                sb.Append(ChronoBullets.DescriptionFor(index, boost));
                if (boost && ChronoBullets.IsBoostedByLegendary(index))
                    sb.Append("\n【传奇：效果翻倍】");

                text.set_text(ChronoPanelLog.Hx(sb.ToString()));
                try { text.set_textColor(def.Color); } catch { }

                // 记下实际行数 —— textHeight 还没算出来时靠它估高度（见 LayoutDescText）
                int lines = 1;
                for (int i = 0; i < sb.Length; i++)
                {
                    if (sb[i] == '\n') lines++;
                }
                _descLineCount = lines;

                // 文案换了高度可能变 → 重新贴一次底部
                LayoutDescText();
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"刷新弹药说明失败: {ex.Message}");
            }
        }

        public override int get_entryWid() => EntryWid;

        public override int get_entryHei() => EntryHei;

        /// <summary>
        /// 每个格子里画一个罗马数字 —— 直接用 `TIMEKASAN.atlas` 的 idle_0000…idle_0011
        /// （idle_0000 = 罗马数字 I，往后递增），帧映射复用 `ChronoFx.FrameIndexForRoman`，
        /// 和斩击刻印 / 开火蹦字走的是同一张表，改图集只需要改那一处。
        ///
        /// ⚠ 为什么要套一层 holder：
        ///   `GridSelector.addEntryAt()` 会给这里返回的对象挂一个 onBeforeReflow，
        ///   里面**强制**把 scaleX / scaleY 设成 pixelScale。所以缩放不能加在 sprite 身上
        ///   （下一帧就被覆盖成原尺寸，数字会撑爆整个格子），
        ///   只能把 sprite 放进外层容器，让容器的 pixelScale 缩放和 sprite 自己的比例相乘。
        /// </summary>
        public override dc.h2d.Object getIconBmp(int i, dc.h2d.Object p)
        {
            try
            {
                var lib = ChronoFx.GetNumeralLib();
                if (lib == null) return base.getIconBmp(i, p);

                var holder = new dc.h2d.Object(p);

                int frame = ChronoFx.FrameIndexForBullet(i);
                int startFrame = frame;
                var spr = new HSprite(lib, ChronoPanelLog.Hx(ChronoFx.NumeralGroup),
                                      Ref<int>.From(ref startFrame), holder);
                if (spr == null) return base.getIconBmp(i, p);

                // 图集帧数保护：帧不够时退回最后一帧（和 ChronoFx.ShowNumeralScaled 一致）
                int frames = 1;
                try { frames = spr.totalFrames(); } catch { }
                int shown = frame;
                if (frames > 0 && shown >= frames) shown = frames - 1;

                // 只 setFrame 不够 —— sprite 的 AnimManager 仍会继续推进帧，把这一格盖掉
                try { spr.setFrame(shown); } catch { }
                try { spr.get_anim().pauseCurrentAnim(); } catch { }

                // ⚠ 锚点必须是**左上角 (0,0)**，绝对不能用居中 (0.5,0.5)。
                //
                //   原版默认的格子图标是 dc.h2d.Bitmap（Icon : Bitmap），它的包围盒是
                //   [0,w]×[0,h] —— 也就是**左上角对齐格子**。GridSelector 就是按这个约定
                //   摆放格子的。
                //   而 HSprite 一旦用居中锚点，包围盒变成 [-w/2,w/2]×[-h/2,h/2]，
                //   整张图会往左上偏半个身位 —— 表现出来正好是
                //   "选择框的中心对上了图集的右下角"，差半张图。
                var pivot = spr.pivot;
                pivot.centerFactorX = 0.0;
                pivot.centerFactorY = 0.0;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                spr.scaleX = NumeralScale;
                spr.scaleY = NumeralScale;
                spr.posChanged = true;

                return holder;
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"弹药格子 {i + 1} 画罗马数字失败，退回占位方块: {ex.Message}");
                return base.getIconBmp(i, p);
            }
        }

        // ------------------------------------------------------------------ 确认 / 关闭

        /// <summary>
        /// Enter 确认：把光标那一发装填进武器，然后走原版的关闭流程
        /// （`base.onValidate()` → `close()` → `Game.resume()` + 销毁本进程）。
        /// </summary>
        public override void onValidate()
        {
            int index = -1;
            try
            {
                var entry = getEntryAt(curX, curY);
                if (entry != null) index = entry.i;
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"读取弹药光标失败: {ex.Message}");
            }

            // 先让原版收尾：播确认音、关面板、恢复游戏
            base.onValidate();

            if (index < 0)
            {
                ChronoPanelLog.Write("弹药选择取消（没读到光标）");
                return;
            }

            try
            {
                _gun?.SetBullet(index);
                ChronoPanelLog.Write($"弹药面板确认：第 {index + 1} 发 {ChronoBullets.Get(index).Name}");
            }
            catch (Exception ex)
            {
                ChronoPanelLog.Write($"装填失败: {ex.Message}");
            }
        }

        public override void onDispose()
        {
            base.onDispose();
            if (ReferenceEquals(Current, this)) Current = null;
        }

        // ------------------------------------------------------------------ 打开

        /// <summary>
        /// 打开面板。成功返回 true（此时游戏已经真暂停，由游戏自己负责恢复）。
        /// </summary>
        public static bool Open(TimeBullet? gun, int current)
        {
            if (IsOpen) return false;

            if (gun == null)
            {
                ChronoPanelLog.Write("手里没有 Zaphkiel，选择弹药面板不打开");
                return false;
            }

            var game = dc.pr.Game.Class.ME;
            if (game == null || game.destroyed) return false;

            bool alreadyPaused = false;
            try { alreadyPaused = game.paused; } catch { }
            if (alreadyPaused)
            {
                ChronoPanelLog.Write("选择弹药面板：游戏已处于暂停状态，不重复打开");
                return false;
            }

            int n = ChronoBullets.All.Length;
            _pendingStart = n > 0 ? (((current % n) + n) % n) : 0;

            try
            {
                _ = new ChronoAmmoPanel(gun);
                // 打开成功不打日志（每次开面板一行，正常玩是噪音）；失败仍会报。
                return true;
            }
            catch (Exception ex)
            {
                // 同武器面板：基类构造里已经 pauseGame()，抛异常必须手动解暂停
                ChronoPanelLog.Write($"打开选择弹药面板失败，已强制恢复: {ex}");
                Current = null;
                _pendingStart = 0;
                ChronoPanelLog.EmergencyCleanup();
                return false;
            }
        }
    }
}
