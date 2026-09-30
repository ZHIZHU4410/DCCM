#nullable disable

using dc;
using dc.hl.types;
using dc.tool;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using System;

namespace LightningWhipBoost
{
    /// <summary>
    /// LightningWhip（闪电鞭 / Fouet électrique）强化模组
    /// =================================================
    /// 目标：敌人之间的连锁电击 —— 无视墙体、范围 ×10、伤害 ×10。
    ///
    /// 【数值部分】由 res.pak 数据补丁实现（patch_lightningwhip_cdb.py 生成）：
    ///     item/LightningWhip.props.range   2   -> 20    连锁索敌范围 ×10
    ///     item/LightningWhip.props.prct    0.5 -> 5.0   连锁命中伤害 ×10
    ///   依据 GamePseudocode/dc.tool.weap/LightningWhip.cs：
    ///     · 连锁每一跳的搜索范围 = base.itemInf.props.range
    ///       （onExecute: getNextTarget(arrayObj, base.itemInf.props.range)）
    ///     · 连锁伤害 = get_curSkillInf().power × base.itemInf.props.prct
    ///       （onExecute: attackData2.overrideBaseDamage(power * prct)）
    ///   本代码不再重复修改数值，避免与数据补丁叠加。
    ///
    /// 【穿墙部分】本代码实现：
    ///   原版 getNextTarget 里有一句
    ///       targetHelper.filterBySight(entity, ref null, 30);
    ///   （entity = 上一跳的目标），它会沿 Bresenham 线做视线/墙体检测，
    ///   被墙挡住就断链。这里在「敌人之间连锁」这一跳索敌期间挂起 filterBySight，
    ///   使连锁无视墙体；从主角找第一个目标时仍走原版视线判定，不改变鞭子本身手感。
    /// </summary>
    public class LightningWhipBoostMain : ModBase, IOnGameExit, IOnAfterLoadingAssets
    {
        /// <summary>连锁索敌中标记：为 true 时挂起 TargetHelper.filterBySight（穿墙）。</summary>
        private bool _chainPiercing;

        /// <summary>数据结构版本号（写日志用，便于确认数据补丁已生效）。</summary>
        private const string PakName = "res.pak";

        public LightningWhipBoostMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();

            try
            {
                Hook_LightningWhip.getNextTarget += OnGetNextTarget;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LightningWhipBoost] Hook_LightningWhip.getNextTarget 挂载失败");
            }

            try
            {
                Hook_TargetHelper.filterBySight += OnFilterBySight;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LightningWhipBoost] Hook_TargetHelper.filterBySight 挂载失败");
            }

            Logger.Information("[LightningWhipBoost] 已加载: 数值=res.pak(连锁范围×10/连锁伤害×10), 连锁穿墙=代码钩子");
        }

        /// <summary>
        /// 资源加载完成：手动加载 mod 自带的 res.pak（LightningWhip 数值补丁）。
        /// 与 DamageAuraBoost 一致，pak 里是 Assets/data.cdb_/item/LightningWhip.json。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(LightningWhipBoostMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, PakName);
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[LightningWhipBoost] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[LightningWhipBoost] 未找到 res.pak: {pakPath}（连锁范围/伤害仍为原版）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[LightningWhipBoost] res.pak 加载失败");
            }
        }

        /// <summary>
        /// getNextTarget：区分「主角 -> 第一个目标」与「敌人 -> 敌人」两种索敌。
        /// 只有后者（prevTargets 非空 = 连锁跳）才开启穿墙。
        /// </summary>
        private Entity OnGetNextTarget(Hook_LightningWhip.orig_getNextTarget orig,
                                       LightningWhip self, ArrayObj prevTargets, double rangeCase)
        {
            bool isChainJump = prevTargets != null && prevTargets.length > 0;
            if (!isChainJump)
            {
                // 首次索敌：保持原版行为（含视线判定）
                return orig(self, prevTargets, rangeCase);
            }

            bool prev = _chainPiercing;
            _chainPiercing = true;
            try
            {
                return orig(self, prevTargets, rangeCase);
            }
            finally
            {
                _chainPiercing = prev;
            }
        }

        /// <summary>
        /// filterBySight：连锁索敌期间直接返回（不做视线/墙体检测）=> 连锁电击穿墙。
        /// 其余任何武器/技能的视线判定不受影响。
        /// </summary>
        private void OnFilterBySight(Hook_TargetHelper.orig_filterBySight orig,
                                     TargetHelper self, Entity otherSource,
                                     Ref<bool> ignoreOneWay, int? ignoreSpotType)
        {
            if (_chainPiercing)
            {
                return;
            }
            orig(self, otherSource, ignoreOneWay, ignoreSpotType);
        }

        private static dc.String ToHaxeString(string s)
        {
            return new Hashlink.Proxy.Objects.HashlinkString(s).AsHaxe<dc.String>();
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_LightningWhip.getNextTarget -= OnGetNextTarget;
            Hook_TargetHelper.filterBySight -= OnFilterBySight;
            Logger.Information("[LightningWhipBoost] 游戏退出，模组已卸载");
        }
    }
}
