#nullable disable

using System;
using System.IO;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;

namespace ForgeQualityCells
{
    /// <summary>
    /// ForgeQualityCells —— 铁匠学徒（休息房「金币刷新词条」那个 NPC）新增「用细胞提升品质」
    /// ====================================================================================
    ///
    /// ## 目标 NPC / 面板
    ///
    /// 休息房里的 <c>dc.en.inter.npc.SmallBlacksmith</c>（"Reforger"，法语 `FORGE MINEURE`）
    /// 激活时开的是 <c>dc.ui.ForgeUnderground</c> 面板：
    ///   每个已装备物品两行 —— 「Reforger les modificateurs」（花**金币**刷新词条）
    ///                      和「Augmenter la puissance」（花**金币** +1 品质 = QualityUp）。
    ///
    /// 原版这两行都在 <c>ForgeUnderground.addItem()</c> 里现场造出来。
    /// 本模组挂钩这个方法，在**每一件装备**下面再补第三行：
    ///
    ///     提升品质  N{iconCell@img}
    ///
    /// 花 **N 个细胞**给该物品 +1 品质（等价于原版 refine：`reforge(item, 1)`，
    /// 也就是叠一层 `QualityUp` 词条 +1 力量等级，并补上随品质增长的新词条）。
    ///
    /// ## 为什么值这个功能
    ///
    /// 原版给品质只能花**金币**（`getForgeRefineCost`），而休息房里金币往往同时被
    /// 刷新词条抢走；细胞却经常剩一大堆。多一条细胞通道，等于把"金币不够但细胞有余"
    /// 的局救回来。价格、上限、能否超过原版上限全在
    /// <see cref="ForgeQualityForge"/> 里，可一处调参。
    ///
    /// ## 数值来源（Assets 数据补丁）
    ///
    /// 价格表放在 **data.cdb 的 `truelle` 表**（原版放全局调参的那张表：
    /// `ForgeRerollCost` / `ForgeRefineCost` / `ForgeCellCosts` 都在里面），
    /// 由 <c>patch_forge_quality_cdb.py</c> 生成：
    ///
    ///     ForgeQualityCellCost.value0 = [1,2,3,4,5,6,8,10]   ← 下标 = 当前品质档位
    ///
    /// csproj 里 BuildResPak 目标把它 diff + unpack 成
    /// `Assets/data.cdb_/truelle/ForgeQualityCellCost.json`，再打进 res.pak。
    /// 运行时读不到这张表也不会坏 —— C# 侧有内置兜底价格表。
    ///
    /// ## 代码分工
    ///
    ///   · 本文件      —— ModBase / 钩子挂载 / res.pak 加载；
    ///   · ForgeQualityForge.cs —— 面板那一行的创建、刷新、扣细胞与 +1 品质；
    ///   · patch_forge_quality_cdb.py —— 数据侧（truelle 表那一行）。
    /// </summary>
    public class ForgeQualityCellsMain : ModBase, IOnGameExit, IOnAfterLoadingAssets
    {
        public ForgeQualityCellsMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();

            try
            {
                dc.ui.Hook_ForgeUnderground.addItem += ForgeQualityForge.OnAddItem;
                Logger.Information("[ForgeQualityCells] Hook_ForgeUnderground.addItem 已挂载");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ForgeQualityCells] Hook_ForgeUnderground.addItem 挂载失败");
            }

            Logger.Information("[ForgeQualityCells] 已加载：锻造面板新增「用细胞提升品质」");
        }

        /// <summary>
        /// 资源加载完成后手动加载本模组自带的 res.pak（里面的 data.cdb_ 补丁 = 价格表）。
        /// 和 DamageAuraBoost 的做法一致。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(ForgeQualityCellsMain).Assembly.Location) ?? "";
                string pakPath = Path.Combine(dir, "res.pak");
                if (File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[ForgeQualityCells] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[ForgeQualityCells] 未找到 res.pak: {pakPath}（价格表将走 C# 兜底值）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ForgeQualityCells] res.pak 加载失败（价格表将走 C# 兜底值）");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { dc.ui.Hook_ForgeUnderground.addItem -= ForgeQualityForge.OnAddItem; }
            catch { }
            Logger.Information("[ForgeQualityCells] 游戏退出，模组已卸载");
        }

        internal static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
