#nullable disable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using dc;
using dc.en;
using dc.h2d;
using dc.hl.types;
using dc.hxd;
using dc.pr;
using dc.tool;
using dc.ui;
using dc.ui.hud;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;

namespace ForgeQualityCells
{
    /// <summary>
    /// 把「用细胞提升品质」这一行塞进铁匠学徒的锻造面板（<see cref="dc.ui.ForgeUnderground"/>）。
    ///
    /// ## 挂钩点为什么是 addItem
    ///
    /// 面板里每一件装备的两行（刷新词条 / 提升品质）都是
    /// <c>ForgeUnderground.addItem(InventItem i, Flow parent)</c> **现场造**出来的：
    ///
    /// ```
    /// Flow flow = new Flow(choicesFlow);          // 这一件装备的整块
    ///   Skill skill = new Skill(null, flow, ...); //   物品图标
    ///   Flow flow2 = new Flow(flow);              //   竖排容器
    ///     FlowBox 刷新词条行 = createBoxMain(flow2, 6, 7, 0);
    ///     FlowBox 提升品质行 = createBoxMain(flow2, 6, 7, 0);
    ///     registerChoice(每一行, canBeUsed, cb, onSelect)
    /// ```
    ///
    /// 所以在这里收尾（orig 跑完之后）往 `flow2` 里再 add 一个同款 FlowBox，
    /// 就能得到"原版两行 + 我们的第三行"，而且**布局/光标/翻页/鼠标事件全是原版那套**。
    ///
    /// ## 品质档位 = 0 / + / ++ / S / L
    ///
    /// 游戏自己把档位定义在 <c>_Lang.getRawItemUpgradeSuffix(up, legend)</c>：
    ///
    /// ```
    /// legend == true      -> "L"          ← L 是"传奇"，不是第 4 层 QualityUp
    /// up: 0 ""  1 "+"  2 "++"  3 "S"
    /// up >= 4             -> "??"         ← 压根没有第 4 层
    /// ```
    ///
    /// 旁证还有三处：`ItemMetaManager.f_getMaxUpgradeLevel() == 3`（金币通道封顶 S）、
    /// `FreeWeaponSelector.maxQuality == 3`（训练场面板的滑动条只到 S）、
    /// `gameElements.atlas` 里 `itemUpgrade` 只有 0/1/2 三帧（没有 L 的角标）。
    ///
    /// 所以「最高提升到 L 品质」= 让细胞通道把物品**做成传奇**（<see cref="Step.Legendary"/>）：
    /// 打上 `Legendary` 词条 + 挂物品自己的传奇词条 + 清掉 QualityUp
    /// （传奇的 `getAdjustedItemLevel()` 固定 +6，本来就**忽略** QualityUp 层数）。
    ///
    /// ## 三个回调分别是什么（照抄原版语义）
    ///
    ///   · <c>canBeUsed</c> —— 光标的红/白框判定 + 确认时的可用性判定
    ///                        （返回 false → 红色光标 + menu_error2 音效 + 抖一下）；
    ///   · <c>cb</c>        —— 按确认键时执行（真正买东西的地方，见 <see cref="Row.Buy"/>）；
    ///   · <c>onSelect</c>  —— 光标移到这一行（原版用它把右侧物品说明换成这一件）。
    ///
    /// `isDisabled` 只表示"这一行彻底没意义"（不可锻造 / 已经到 L），
    /// 原版就是这么区分的：**买不起**只是 alpha 0.5 + canBeUsed=false，行本身还在。
    /// </summary>
    internal static class ForgeQualityForge
    {
        // ==================================================================== 调参

        /// <summary>data.cdb `truelle` 表里放价格的那一行（patch_forge_quality_cdb.py 生成）。</summary>
        internal const string CdbRowId = "ForgeQualityCellCost";

        /// <summary>面板上这一行的文案前缀（后面会自动接 " -> +" / " -> S" / " -> L"）。</summary>
        internal const string LabelText = "提升品质";

        /// <summary>
        /// 细胞通道的品质上限（下标 = 玩家看到的档位）：
        ///
        ///   0 = 无印  1 = +  2 = ++  3 = S  4 = **L（传奇）**   ← 默认
        ///
        /// ⚠️ 4 走的是"传奇"那条路（`addAffix("Legendary")`），不是再叠一层 QualityUp ——
        ///    游戏里 up>=4 连角标art都没有，名字后缀会变成 "??"。
        ///    想让细胞通道只到 S（= 原版金币通道的上限），把它改成 3 即可。
        /// </summary>
        internal const int MaxCellTier = 4;

        /// <summary>原版金币通道的上限：S = 3（`ItemMetaManager.f_getMaxUpgradeLevel()`）。</summary>
        internal const int VanillaMaxQuality = 3;

        /// <summary>
        /// 删掉原版那条「用**金币**提升品质」的行（`Augmenter la puissance` / `getForgeRefineCost`）。
        ///
        /// true（默认）= 品质只能靠细胞升；面板上每件装备就剩两行：
        ///   · `Reforger les modificateurs`（金币刷新词条，原版）
        ///   · `提升品质 -> +/++/S/L`（细胞，本模组）
        ///
        /// 实现见 <see cref="StripGoldQualityRow"/>。想恢复原版行为改成 false 即可。
        /// </summary>
        internal static readonly bool RemoveVanillaGoldQualityRow = true;

        /// <summary>
        /// 顺手把面板顶部那句说明改掉。
        ///
        /// 原版写的是「Reforgez un objet équipé … ou pour augmenter sa puissance.」
        /// （……**或者提升它的力量**），指的就是已经被我们删掉的那条金币通道，
        /// 留着会误导。改成面板现状。
        /// </summary>
        internal static readonly bool RewritePanelSubtitle = true;

        /// <summary><see cref="RewritePanelSubtitle"/> 用的文案。</summary>
        internal const string SubtitleText = "刷新词条：花金币；提升品质：花细胞，最高到 L（传奇）。";

        /// <summary>读不到 CDB 价格表时的兜底（下标 = **当前**档位，[3] 就是 S→L 那一笔 = 400）。</summary>
        internal static readonly int[] FallbackCosts = { 50, 100, 200, 400, 800 };

        private static bool _loggedOnce;

        // ==================================================================== 钩子

        /// <summary>
        /// Hook_ForgeUnderground.addItem：
        ///
        ///   1) 让原版把这一件装备的两行造好；
        ///   2) **删掉它那条「用金币提升品质」**（见 <see cref="StripGoldQualityRow"/>）；
        ///   3) 补上我们的「用细胞提升品质」。
        ///
        /// 返回值原样透传（面板拿到的是同一个 Flow）。
        /// </summary>
        internal static Flow OnAddItem(Hook_ForgeUnderground.orig_addItem orig,
                                       ForgeUnderground self, InventItem i, Flow parent)
        {
            Flow flow = orig(self, i, parent);
            try
            {
                // ⚠️ 必须判 flow != null：
                //    addItem 对"不可锻造的物品"会在**注册任何可选项之前**就 return null
                //    （`if (i == null) return null; if (!i.canBeForged()) return null;`），
                //    那种情况下 choices 里最后一条是**上一件装备**的金币行 —— 删错了就出事。
                if (flow != null && i != null)
                {
                    if (RemoveVanillaGoldQualityRow) StripGoldQualityRow(self);
                    Attach(self, i, flow);
                }
            }
            catch (Exception ex)
            {
                Log($"[ForgeQualityCells] 处理「{LabelText}」行失败: {ex}");
            }
            return flow;
        }

        /// <summary>
        /// 删掉原版「用金币提升品质」那一行。
        ///
        /// 做法：把它的 **可选项**（`choices` 里那一条）摘掉，再把它的 FlowBox 从
        /// `flow2` 上摘下来。这样光标再也走不到它、它也不再出现在面板上；
        /// 而原版 `flow2.onBeforeReflow` 里对它调的 `reflow()` 只是空转，无害。
        ///
        /// 为什么可以只删"最后一条"：原版 `addItem` 的注册顺序是固定的 ——
        /// 先 `registerChoice(刷新词条行)`（`FlowBox 1`），后 `registerChoice(金币行)`
        /// （`FlowBox 2`）。我们这个钩子是原版**跑完之后**才进来的，
        /// 所以 `choices` 的末条一定就是这一件装备的金币行。
        ///
        /// 为什么不用"找 FlowBox 再比对引用"：`getDyn()` / 字段读出来的代理包装器
        /// 不一定两次都是同一个 C# 对象，跨包装器做 `ReferenceEquals` 很脆。
        /// 这里只把**从数组里读出来的同一个对象**原样传回 `remove`，
        /// 以及在**同一个** choice 上取 `f` 再传回 `removeChild`，
        /// 命中的是 Haxe 那一侧的对象，稳。
        /// </summary>
        private static void StripGoldQualityRow(ForgeUnderground forge)
        {
            try
            {
                ArrayObj choices = forge?.choices;
                if (choices == null || choices.length == 0) return;

                object last = choices.getDyn(choices.length - 1);
                var choice = last as virtual_canBeUsed_cb_f_isDisabled_onSelect_;
                if (choice == null) return;

                FlowBox box = choice.f;

                // 1) 先摘可选项（之后 currentIdx / 光标 / onAfterReflow 都不会再遇到它）
                bool removed = choices.remove(last);

                // 2) 再把行本体摘掉：它的 Interactive 是它的子节点，会一起离开场景树，
                //    所以不会再吃到鼠标事件；原版 reflow 里对它调 reflow() 只是空转。
                if (box != null && box.parent != null)
                {
                    box.parent.removeChild(box);
                }

                if (removed)
                {
                    Log("[ForgeQualityCells] 已删除原版「用金币提升品质」那一行");
                }
                else
                {
                    Log("[ForgeQualityCells] 金币提升品质行没能从 choices 里摘掉（行本体已移除）");
                }
            }
            catch (Exception ex)
            {
                Log($"[ForgeQualityCells] 删除金币提升品质行失败: {ex}");
            }
        }

        // ==================================================================== 造一行

        private static void Attach(ForgeUnderground forge, InventItem item, Flow itemFlow)
        {
            // ---- 0) 面板顶部的说明（原版那句"…或者提升它的力量"已经过时了），每个面板只改一次 ----
            EnsurePanelSubtitleOnce(forge);

            // ---- 1) 找到原版放那两个 FlowBox 的竖排容器（flow2） ----
            Flow boxParent = FindBoxParent(itemFlow);
            if (boxParent == null)
            {
                Log("[ForgeQualityCells] 找不到物品块的竖排容器，跳过这一件");
                return;
            }

            // ---- 2) 造一个和原版同款的 FlowBox ----
            FlowBox box = dc.ui.FlowBox.Class.createBoxMain.Invoke(boxParent, 6, 7, (int?)0);
            if (box == null) return;

            try { box.set_verticalAlign(new FlowAlign.Middle()); } catch { }
            try { if (box.box != null) box.box.alpha = 0.2; } catch { }

            dc.ui.Text label = dc.Assets.Class.makeText(Hx(LabelText), (int?)null, (bool?)true, box);
            dc.ui.Text costText = dc.Assets.Class.makeText(null, (int?)null, (bool?)true, box);
            if (costText != null)
            {
                try { box.getProperties(costText).horizontalAlign = new FlowAlign.Right(); } catch { }
            }
            if (label != null)
            {
                try { label.set_textAlign(new dc.h2d.Align.Left()); } catch { }
            }

            var row = new Row(forge, item, box, label, costText, boxParent);

            // ---- 3) 注册成面板的一个可选项（原版 registerChoice 的用法） ----
            row.Choice = forge.registerChoice(box, row.CanUse, row.Buy, row.OnSelected);

            // ---- 4) 鼠标 / 手柄都能点 ----
            try
            {
                box.set_enableInteractive(true);
                if (box.interactive != null)
                {
                    box.interactive.set_cursor(new Cursor.Button());
                    box.interactive.propagateEvents = true;
                    box.interactive.onClick = row.OnClick;
                    box.interactive.onMove = row.OnMove;
                }
            }
            catch (Exception ex)
            {
                Log($"[ForgeQualityCells] 绑定鼠标事件失败（不影响手柄操作）: {ex.Message}");
            }

            // ---- 5) 宽度对齐原版那两行，高度至少和物品图标一样高 ----
            try
            {
                int fbW = forge.fb?.minWidth ?? 0;
                int w = (int)(fbW * 0.45);
                if (w > 0)
                {
                    box.set_maxWidth(w);
                    box.set_minWidth(w);
                }
            }
            catch { }
            try
            {
                double scale = forge.get_pixelScale.Invoke();
                int minH = (int)(scale * (Skill.Class.ICONBG_SIZE + 12));
                if (minH > 0) box.set_minHeight(minH);
            }
            catch { }

            // ---- 6) 把刷新挂到 flow2 的 onBeforeReflow 上 ----
            //
            // 原版那两个 FlowBox 的文案/置灰就是在**它们自己的** onBeforeReflow 里算的，
            // 而 flow2 每次 reflow 时会显式 reflow 这两个子盒。
            // 我们的盒子不是原版造的，所以：包一层 flow2.onBeforeReflow ——
            // 先跑原版，再刷新我们这一行，最后 reflow 我们的盒子，
            // 这样 flow2 排版时拿到的就是最新的尺寸。
            row.ChainReflow();

            try { boxParent.reflow(); } catch { }
            try { row.Refresh(); } catch { }

            // ---- 7) 面板右上角补一个"细胞余额"，不然 HUD 被隐藏时看不到自己有多少细胞 ----
            EnsureCellsCount(forge);

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Log("[ForgeQualityCells] 已生效：锻造面板每件装备多出「" + LabelText + "」行" +
                    $"（细胞通道上限 = {(MaxCellTier > VanillaMaxQuality ? "L（传奇）" : "S")}）");
            }
        }

        /// <summary>
        /// 找物品块里的竖排容器 flow2。
        ///
        /// 判断条件不用"类型"（Skill / FlowBox 都可能间接继承 Flow，靠类型排除很脆），
        /// 而是**看内容**：那个"是 Flow、自己不是 FlowBox、而且装着 FlowBox"的孩子，
        /// 就是原版放那两个按钮的竖排容器。
        /// </summary>
        private static Flow FindBoxParent(Flow itemFlow)
        {
            try
            {
                ArrayObj kids = itemFlow.children;
                if (kids == null) return null;

                Flow fallback = null;
                for (int k = 0; k < kids.length; k++)
                {
                    object child = kids.getDyn(k);
                    if (!(child is Flow f)) continue;
                    if (child is FlowBox) continue;          // 原版那两行自己
                    if (fallback == null) fallback = f;
                    if (ContainsFlowBox(f)) return f;        // 装按钮的那个 = flow2
                }
                return fallback;
            }
            catch { }
            return null;
        }

        private static bool ContainsFlowBox(Flow f)
        {
            try
            {
                ArrayObj kids = f.children;
                if (kids == null) return false;
                for (int k = 0; k < kids.length; k++)
                {
                    if (kids.getDyn(k) is FlowBox) return true;
                }
            }
            catch { }
            return false;
        }

        // ==================================================================== 价格

        private static int[] _costs;

        /// <summary>
        /// 价格表：优先读 data.cdb `truelle` 表的 <see cref="CdbRowId"/>（value0 是浮点数组），
        /// 读不到就用 <see cref="FallbackCosts"/>。第一次读之后缓存。
        /// </summary>
        internal static int[] CostTable()
        {
            if (_costs != null) return _costs;

            try
            {
                dynamic row = dc.Data.Class.truelle?.byId?.get(Hx(CdbRowId));
                if (row != null)
                {
                    object v0 = row.value0;
                    var list = new List<int>();

                    if (v0 is ArrayBytes_Float floats)
                    {
                        for (int i = 0; i < floats.length; i++)
                            list.Add((int)System.Math.Round(ReadFloat(floats, i)));
                    }
                    else if (v0 is ArrayObj arr)
                    {
                        for (int i = 0; i < arr.length; i++)
                            list.Add((int)System.Math.Round(Convert.ToDouble(arr.getDyn(i))));
                    }
                    else if (v0 != null)
                    {
                        list.Add((int)System.Math.Round(Convert.ToDouble(v0)));
                    }

                    if (list.Count > 0)
                    {
                        _costs = list.ToArray();
                        Log("[ForgeQualityCells] 价格表来自 data.cdb: [" + string.Join(",", _costs) + "]");
                        return _costs;
                    }
                }
                Log("[ForgeQualityCells] data.cdb 里没有价格表，用 C# 兜底价格");
            }
            catch (Exception ex)
            {
                Log($"[ForgeQualityCells] 读价格表失败（用 C# 兜底价格）: {ex.Message}");
            }

            _costs = FallbackCosts;
            return _costs;
        }

        /// <summary>`ArrayBytes_Float` 里第 i 个元素（Haxe 的 f64 数组，每格 8 字节）。</summary>
        private static double ReadFloat(ArrayBytes_Float arr, int i)
        {
            if (arr.bytes == IntPtr.Zero) return 0.0;
            long bits = Marshal.ReadInt64(arr.bytes, i * 8);
            return BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>**当前**档位对应的细胞价格（下标 = getUpgradeLevel()，[3] 就是 S→L 那一笔）。</summary>
        internal static int QualityCost(InventItem item)
        {
            if (item == null) return 0;

            if (IsTraining()) return 0;      // 训练场和原版一样免费

            int[] table = CostTable();
            int lvl = SafeUpgradeLevel(item);
            if (lvl < 0) lvl = 0;
            if (lvl < table.Length) return table[lvl];

            // 超出表格（理论上不会：L 之后就隐藏这一行了）：最后一档 + 每多一档 +1
            return table[table.Length - 1] + (lvl - table.Length + 1);
        }

        internal static bool IsTraining()
        {
            try { return Game.Class.ME != null && Game.Class.ME.isTraining(); }
            catch { return false; }
        }

        internal static int SafeUpgradeLevel(InventItem item)
        {
            try { return item.getUpgradeLevel(); } catch { return 0; }
        }

        internal static bool HasAffix(InventItem item, string affixId)
        {
            try { return item.hasAffix(Hx(affixId)); }
            catch { return false; }
        }

        /// <summary>物品能不能被打上传奇标记（原版 ItemGen.generateStats 也先问这一句）。</summary>
        internal static bool CanBecomeLegendary(InventItem item)
        {
            try
            {
                if (item == null) return false;
                if (HasAffix(item, "Legendary")) return false;
                return item.canReceiveAffix(Hx("Legendary"));
            }
            catch { return false; }
        }

        /// <summary>
        /// 英雄现在**手上**有多少细胞。
        ///
        /// ⚠️ 一定要读 <c>Hero.cells</c>，不能读 <c>game.data.cells</c>！
        ///
        /// 这两者不对称（和金币正好相反）：
        ///
        ///   · 金币：`Hero.substractMoney` 里写了 `game.data.money -= v` 再 `hudSetMoney(...)`，
        ///           `game.data.money` 是**双向同步**的权威存储 → 原版金币数字是实时的；
        ///   · 细胞：`Hero.substractCells` / `addCells` 只改 `Hero.cells` + `hudSetCells(...)`，
        ///           **全工程没有任何一处写回 `game.data.cells`**；`game.data.cells` 只在
        ///           `_Hero.__inst_construct__` 里被读一次（`arg1.cells = game.data.cells`）
        ///           —— 也就是"进这一关时"的快照。
        ///
        /// 之前读 `game.data.cells` 的后果正是"扣细胞不实时"：
        /// 花完细胞后这个数不降，于是显示不变、行也不变灰，
        /// 而 `substractCells` 内部会把超额的部分夹掉（`v = min(v, cells)`）→ 等于白拿。
        /// </summary>
        internal static int HeroesCells()
        {
            try
            {
                Hero hero = Game.Class.ME?.hero;
                if (hero != null) return hero.cells;    // 运行中的实时值
                return Game.Class.ME.data.cells;        // 没英雄时（领奖界面之类）退回快照
            }
            catch { return 0; }
        }

        // ==================================================================== 档位

        /// <summary>这一次"提升品质"到底做什么。</summary>
        internal enum Step
        {
            /// <summary>到顶了（或者这件物品根本没法再提）。</summary>
            None = 0,
            /// <summary>再叠一层 QualityUp（0→+ / +→++ / ++→S）。</summary>
            QualityUp = 1,
            /// <summary>S→L：做成传奇。</summary>
            Legendary = 2,
        }

        /// <summary>
        /// 下一次购买会走到哪一档。
        ///
        /// 顺序推进：0 → + → ++ → S，到了 S 若 <see cref="MaxCellTier"/> 允许，就再走一步 L（传奇）。
        /// 已经是传奇 = 到顶（原版传奇物品本来也不给锻造）。
        /// </summary>
        internal static Step NextStep(InventItem item)
        {
            if (item == null) return Step.None;
            if (HasAffix(item, "Legendary")) return Step.None;

            int lvl = SafeUpgradeLevel(item);
            if (lvl >= VanillaMaxQuality)
            {
                // S 了：细胞通道还能再上一步 = L（传奇）
                if (MaxCellTier > VanillaMaxQuality && CanBecomeLegendary(item)) return Step.Legendary;
                return Step.None;
            }
            if (lvl >= MaxCellTier) return Step.None;
            return Step.QualityUp;
        }

        /// <summary>档位 → 名字后缀（和 <c>_Lang.getRawItemUpgradeSuffix</c> 的规则一致）。</summary>
        internal static string SuffixOf(int up)
        {
            switch (up)
            {
                case 0: return "";
                case 1: return "+";
                case 2: return "++";
                case 3: return "S";
                default: return "L";
            }
        }

        // ============================================================ 面板右上角的细胞余额

        private static readonly ConditionalWeakTable<ForgeUnderground, Count> _cellsCounts = new();

        private static void EnsureCellsCount(ForgeUnderground forge)
        {
            try
            {
                if (forge == null || forge.fCount == null) return;
                if (_cellsCounts.TryGetValue(forge, out _)) return;

                Tile tile = dc.Assets.Class.ui.getTile(Hx("iconCell"),
                                                       Ref<int>.Null, Ref<double>.Null, Ref<double>.Null, null);
                if (tile == null) return;

                var count = new Count(tile, forge.fCount);
                // 和 HUD 里那些 Count 一样，显式把 pixelScale 接上（原版锻造面板的
                // 金币 Count 没接也照跑，但接上更稳：文本缩放在 UI 缩放下才对得上）
                try { count.get_pixelScale = forge.get_pixelScale; } catch { }
                try { count.text.get_pixelScale = forge.get_pixelScale; } catch { }
                count.setValue(HeroesCells(), Ref<bool>.In(false));
                try { count.text.set_textColor(ColorOf("GO")); } catch { }
                _cellsCounts.Add(forge, count);
                try { count.onResize(); } catch { }

                try { forge.fCount.reflow(); } catch { }
            }
            catch (Exception ex)
            {
                Log($"[ForgeQualityCells] 加细胞余额显示失败（不影响功能）: {ex.Message}");
            }
        }

        internal static void SyncCellsCount(ForgeUnderground forge)
        {
            try
            {
                if (forge != null && _cellsCounts.TryGetValue(forge, out Count count) && count != null)
                    count.setValue(HeroesCells(), Ref<bool>.In(true));
            }
            catch { }
        }

        // ==================================================== 面板顶部说明（每个面板只改一次）

        private static readonly ConditionalWeakTable<ForgeUnderground, object> _subtitlesDone = new();

        /// <summary>
        /// 把面板顶部的说明换成 <see cref="SubtitleText"/>。
        ///
        /// 原版那句是「Reforgez un objet équipé … ou pour augmenter sa puissance.」，
        /// 后半句说的是"花金币提升力量"= 已经被删掉的那条通道，留着会误导玩家。
        /// 用 ConditionalWeakTable 记住改过的面板，同一次打开只改一次（不挡住原版 onResize）。
        /// </summary>
        private static void EnsurePanelSubtitleOnce(ForgeUnderground forge)
        {
            try
            {
                if (forge == null) return;
                if (_subtitlesDone.TryGetValue(forge, out _)) return;
                _subtitlesDone.Add(forge, new object());

                if (!RewritePanelSubtitle) return;

                dc.ui.Text sub = forge.subText;
                if (sub == null) return;

                sub.set_text(Hx(SubtitleText));
                sub.onResize();
            }
            catch (Exception ex)
            {
                Log($"[ForgeQualityCells] 改面板说明文字失败（不影响功能）: {ex.Message}");
            }
        }

        // ==================================================================== 小工具

        internal static dc.String Hx(string s) => ForgeQualityCellsMain.ToHaxeString(s);

        /// <summary>
        /// hashlink 字符串 → 纯文本。
        /// CDB 里有些字段序列化出来是 "affix=BleedOnHit" 这种带键名前缀的形式，
        /// 直接拿去 byId.get() 会永远查不到（ChronoBlade 踩过同一个坑）。
        /// </summary>
        internal static string Plain(dc.String s)
        {
            string t;
            try { t = s?.ToString() ?? ""; } catch { return ""; }
            int eq = t.IndexOf('=');
            if (eq >= 0) t = t.Substring(eq + 1);
            return t.Trim().Trim('"', '\'', ' ');
        }

        internal static int ColorOf(string key)
        {
            try { return (int)dc.ui.Text.Class.COLORS.get(Hx(key)); }
            catch { return 16777215; }
        }

        internal static void Log(string msg)
        {
            System.Console.WriteLine(msg);
        }

        internal static void PlayBuySfx()
        {
            try
            {
                var loader = dc.hxd.Res.Class.get_loader();
                var snd = (dc.hxd.res.Sound)loader.loadCache(Hx("sfx/inter/pick_buy.wav"),
                                                             dc.hxd.res.Sound.Class);
                dc.Audio.Class.ME.playUIEvent(snd, (double?)null);
            }
            catch { }
        }

        // ==================================================================== 一行

        /// <summary>面板里我们加的那一行（一件装备一份）。</summary>
        private sealed class Row
        {
            private readonly ForgeUnderground _forge;
            private readonly InventItem _item;
            private readonly FlowBox _box;
            private readonly dc.ui.Text _label;
            private readonly dc.ui.Text _costText;
            private readonly Flow _boxParent;

            private HlAction _originalReflow;
            private string _lastLabel;

            internal virtual_canBeUsed_cb_f_isDisabled_onSelect_ Choice;

            internal Row(ForgeUnderground forge, InventItem item, FlowBox box,
                         dc.ui.Text label, dc.ui.Text costText, Flow boxParent)
            {
                _forge = forge;
                _item = item;
                _box = box;
                _label = label;
                _costText = costText;
                _boxParent = boxParent;
            }

            internal int Cost() => QualityCost(_item);

            /// <summary>这一行到底能不能买（买不起 → 红光标 + 报错音，和原版一致）。</summary>
            internal bool CanUse()
            {
                try
                {
                    if (_forge == null || _forge.destroyed) return false;
                    if (NextStep(_item) == Step.None) return false;
                    return HeroesCells() >= Cost();
                }
                catch { return false; }
            }

            /// <summary>这一行是不是"彻底没意义"（原版用它决定 isDisabled + 隐藏）。</summary>
            private bool HardDisabled()
            {
                try
                {
                    if (_item == null) return true;
                    return NextStep(_item) == Step.None;
                }
                catch { return true; }
            }

            /// <summary>把原版 flow2 的 onBeforeReflow 包一层：原版 → 刷新我们 → reflow 我们。</summary>
            internal void ChainReflow()
            {
                try
                {
                    _originalReflow = _boxParent.onBeforeReflow;
                    _boxParent.onBeforeReflow = ChainReflowCallback;

                    // 自己的 onBeforeReflow 也接上：原版 select()/onResize() 会**单独**
                    // reflow "当前选中"的那个 FlowBox（ForgeUnderground.onResize 的
                    // `choice.f.reflow()`），那条路径不经过 flow2，靠这里兜住。
                    _box.onBeforeReflow = Refresh;
                }
                catch (Exception ex)
                {
                    Log($"[ForgeQualityCells] 挂 flow2 刷新回调失败: {ex.Message}");
                }
            }

            private void ChainReflowCallback()
            {
                try { _originalReflow?.Invoke(); } catch { }
                try { Refresh(); } catch { }
                try { _box.reflow(); } catch { }
            }

            /// <summary>刷新这一行的文案 / 置灰 / 可选项状态（对应原版那两个 ArrowFunction）。</summary>
            internal void Refresh()
            {
                Step step = NextStep(_item);
                int cost = Cost();
                bool hard = step == Step.None;
                bool affordable = HeroesCells() >= cost;
                bool looksDisabled = hard || !affordable;

                if (Choice != null)
                {
                    try { Choice.isDisabled = hard; } catch { }
                }

                try { _box.set_visible(!hard); } catch { }
                try { _box.alpha = looksDisabled ? 0.5 : 1.0; } catch { }

                if (_label != null)
                {
                    try
                    {
                        // "提升品质 -> +" / "-> ++" / "-> S" / "-> L"
                        // ⚠️ 只用 ASCII 箭头：游戏字体对 U+2192「→」这类符号不一定有字形
                        string txt = step == Step.None
                            ? LabelText
                            : LabelText + " -> " + SuffixOf(step == Step.Legendary
                                                            ? MaxCellTier
                                                            : SafeUpgradeLevel(_item) + 1);
                        if (txt != _lastLabel)
                        {
                            _lastLabel = txt;
                            _label.set_text(Hx(txt));
                            _label.onResize();
                        }
                        _label.set_textColor(16777215);
                    }
                    catch { }
                }

                if (_costText != null)
                {
                    try
                    {
                        // 原版这一格是 "1234{iconCoin@img}"，我们换成细胞图标。
                        // ⚠️ 这里**不能**用 C# 的内插字符串（$"..."）：{...@img} 里的花括号
                        //    会被当成格式占位符，编译期直接报 CS8086。
                        string txt = cost + "{iconCell@img}";
                        _costText.set_text(dc.Lang.Class.t.untranslated(Hx(txt)));
                        _costText.set_textColor(ColorOf(affordable ? "GO" : "LO"));
                        _costText.onResize();
                    }
                    catch { }
                }
            }

            // ---------------------------------------------------------- 回调

            internal void OnSelected()
            {
                // 光标移到这一行：和原版一样，把右侧说明换成这一件物品
                try { _forge.setItemDesc(_item); } catch { }
            }

            internal void OnClick(dc.hxd.Event e) => SelectMe();
            internal void OnMove(dc.hxd.Event e) => SelectMe();

            private void SelectMe()
            {
                try
                {
                    int idx = _forge.choices.indexOf(Choice, (int?)null);
                    if (idx < 0) return;
                    _forge.currentIdx = idx;
                    _forge.select(true, Ref<bool>.Null);
                }
                catch { }
            }

            /// <summary>
            /// 确认购买：
            ///   · 还没到 S   → 走**原版 refine 的同一条路**（`reforge(item, 1)` = 叠一层 QualityUp
            ///                  + 补上随品质增长的新词条），只是把金币换成细胞；
            ///   · 已经到 S   → <see cref="GrantLegendary"/>（L 档 = 传奇）。
            /// </summary>
            internal void Buy()
            {
                try
                {
                    if (!CanUse()) return;
                    int cost = Cost();
                    Step step = NextStep(_item);

                    if (step == Step.Legendary)
                    {
                        GrantLegendary();
                    }
                    else
                    {
                        // 1) +1 品质：原版 ForgeUnderground.reforge(i, 1) 的 else 分支
                        _forge.reforge(_item, 1);

                        // 2) 原版 refine 的收尾：去掉刷新标记、打上"已精炼"标记
                        try { _item.removeAllAffixes(Hx("Rerolled")); } catch { }
                        try { _item.addAffix(Hx("ForgeRefined"), Ref<bool>.Null); } catch { }
                    }

                    // 3) 音效
                    PlayBuySfx();

                    // 4) 扣细胞（Hero.hudSetCells 会顺带刷新 HUD 上的细胞数）
                    Hero hero = Game.Class.ME?.hero;
                    if (hero != null) hero.substractCells(cost, Ref<bool>.Null);

                    // 5) 收尾刷新
                    Refresh();
                    SyncCellsCount(_forge);
                    try { _forge.setItemDesc(_item); } catch { }
                    try { _forge.onResize(); } catch { }
                }
                catch (Exception ex)
                {
                    Log($"[ForgeQualityCells] 购买「{LabelText}」失败: {ex}");
                }
            }

            /// <summary>
            /// S → L：把物品做成**传奇**。步骤照抄原版 `ItemGen.generateStats` 的传奇分支：
            ///
            ///   1) <c>addAffix("Legendary")</c>        —— 原版就是这么打上"传奇"的
            ///      （`_Lang.getRawItemUpgradeSuffix` 见到这个 affix 才把后缀写成 "L"）；
            ///   2) 物品 CDB 的 <c>legendAffixes</c> 池逐条挂上 —— 原版同在 generateStats 里做，
            ///      这才是 L 真正的收益（传奇专属词条，比如撕裂光环的 BleedOnHit）；
            ///   3) <c>removeAllAffixes("QualityUp")</c> —— 传奇的 `getAdjustedItemLevel()`
            ///      是 `itemLevel + 6` **固定值、忽略 QualityUp 层数**，而 S 也是 +6
            ///      （3 层 × 2），所以清掉**不掉战力**；原版生成的传奇物品同样带 0 层
            ///      （spawn 的传奇分支会 set_weaponQuality(0)），清掉才和原版传奇长得一样。
            /// </summary>
            private void GrantLegendary()
            {
                if (!CanBecomeLegendary(_item)) return;

                // 1) 传奇标记
                _item.addAffix(Hx("Legendary"), Ref<bool>.In(true));

                // 2) 传奇词条池（item._itemData.legendAffixes）
                try
                {
                    ArrayObj pool = _item._itemData?.legendAffixes;
                    if (pool == null)
                    {
                        // 正常不会发生：面板刚把这件物品画过一遍，_itemData 早就解析好了。
                        // 真为 null 也只是少给一条传奇词条，不会坏档。
                        Log("[ForgeQualityCells] 拿不到物品的 legendAffixes 池（这一件只有传奇标记）");
                    }
                    else
                    {
                        for (int k = 0; k < pool.length; k++)
                        {
                            var entry = pool.getDyn(k) as virtual_affix_;
                            if (entry == null) continue;
                            string id = Plain(entry.affix);
                            if (id.Length == 0) continue;
                            if (HasAffix(_item, id)) continue;
                            // 原版也是 ignoreChecks = true（generateStats 里 flag6 = true）
                            _item.addAffix(Hx(id), Ref<bool>.In(true));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ForgeQualityCells] 挂传奇词条失败（物品已经变成 L）: {ex.Message}");
                }

                // 3) 清掉 QualityUp（见上面的说明：传奇忽略层数，清了不掉战力）
                try { _item.removeAllAffixes(Hx("QualityUp")); } catch { }

                // 4) 通知英雄：装备数值 / 外观（金色）刷新
                try
                {
                    Hero hero = Game.Class.ME?.hero;
                    if (hero != null)
                        hero.onEquipedItemsChange(Ref<bool>.In(true), Ref<bool>.Null, Ref<bool>.Null);
                }
                catch (Exception ex)
                {
                    Log($"[ForgeQualityCells] 刷新装备变化失败: {ex.Message}");
                }

                Log("[ForgeQualityCells] 已用细胞把物品提升到 L（传奇）：" +
                    Plain(SafeItemId(_item)));
            }

            private static dc.String SafeItemId(InventItem item)
            {
                try { return item._itemData?.id; } catch { return null; }
            }
        }
    }
}
