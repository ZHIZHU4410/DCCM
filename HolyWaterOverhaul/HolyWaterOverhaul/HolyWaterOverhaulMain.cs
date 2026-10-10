#nullable disable

using System;
using System.IO;
using dc.en;
using dc.en.gr;
using dc.tool;
using HaxeProxy.Runtime;
using ModCore;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;

// dc 命名空间里也有 Math / Path / File 这些同名类 → 显式别名到 BCL
using Math = System.Math;
using Path = System.IO.Path;
using File = System.IO.File;

namespace HolyWaterOverhaul
{
    /// <summary>
    /// HolyWaterOverhaul —— 把 HolyWater（圣水，DLC「Purple」）改成"满地火 + 全图烧"
    /// ==================================================================================
    /// 需求：
    ///   · 普通：**散射丢出 15 个火堆**
    ///   · 传奇（`ItemCrash` 词缀）：**灼烧整个地图的所有敌人**
    ///   · 持续时间**无上限**
    ///   · **CD 改为 0**
    ///
    /// ## 原版的两条路
    ///
    /// `dc.en.gr.HolyWater.onTrigger()`（`GamePseudocode/dc.en.gr/HolyWater.cs:1369`）一进来就分叉：
    ///
    ///     if (item.hasAffix("ItemCrash"))
    ///     {
    ///         new HolyRain(base.parent, base.item, base._level).init();   // ← 传奇：下雨
    ///         return;
    ///     }
    ///     // 普通：TargetHelper 找落点附近（commonProps.explosionRange = 1 格）的目标，
    ///     //       对每个目标：
    ///     //         double duration  = props.duration;                      // 5
    ///     //         double duration2 = props.duration2;                     // 0.7
    ///     //         double value     = _Const.scaleHeroValueToTier(props.dps, getRelevantTierFor(item));
    ///     //         level.addAreaAffectS(x, y, duration, 88, duration2, value, affixes);
    ///
    /// `dc.pr.Level.addAreaAffectS(cx, cy, aoeDurationS, a, aDurationS, aValue, affixes)`
    /// 就是"在某格生成一片带持续时间的区域效果" —— `a = 88` 是灼烧，`aoeDurationS` 是它活多久，
    /// `aValue` 是每秒伤害。
    ///
    /// 传奇那条走 `dc.en.gr.HolyRain`（`HolyRain.cs`）：
    ///
    ///     init()        tickRate = props.tick * ItemCrash.props.multiplier;
    ///                   level.addAreaAffectS(x, y, props.duration, 88, props.duration, 0.0, null);  // 雨柱
    ///     fixedUpdate() fx.holyRain(...);
    ///                   TargetHelper → filterByArea(area) → **filterBySight(...)**   // ← 只烧"视野内"
    ///                   对每个目标：
    ///                     double doT = hero.getDoTValue(item, props.dps, entity, null);
    ///                     entity.setAffectS(88, props.duration, ref doT, null);       // ← 直接挂灼烧
    ///
    /// 所以"视野内"就是那个 `filterBySight`；要烧全图，就不走它的 TargetHelper，自己遍历。
    ///
    /// ## 本模组怎么改
    ///
    /// 1. **CD 0 / 持续时间无上限** → 数据补丁（`patch_holywater_cdb.py`）：
    ///    `item/HolyWater.castCD` 12 → 0、`props.duration` 5 → 99999。
    ///    `InventItem.cs:8578` 就是 `double castCD = _itemData.castCD;`，所以改这里就够了。
    ///
    /// 2. **普通 15 个火堆** → Hook `HolyWater.onTrigger`，不调 orig，
    ///    自己以落点为中心把 15 个 `addAreaAffectS` 铺开（每个都吸附到该列地面）。
    ///
    /// 3. **传奇烧全图** → Hook `HolyWater.onTrigger` 认出 `ItemCrash` 时，
    ///    照常调 orig（保留原版下雨的表现），另外记下 item / dps / duration，
    ///    之后每 `LegendTick` 秒遍历**整张地图**的敌人，对每个都
    ///    `entity.setAffectS(88, props.duration, doT)` —— 用的是原版同一套 affect 88，
    ///    但去掉了 `filterByArea` / `filterBySight` 的限制。
    /// </summary>
    public class HolyWaterOverhaulMain : ModBase, IOnAfterLoadingAssets, IOnHeroUpdate, IOnHeroInit, IOnGameExit
    {
        /// <summary>原版用的灼烧 affect 编号。</summary>
        private const int BurnAffect = 88;

        /// <summary>传奇词缀。</summary>
        private const string LegendAffix = "ItemCrash";

        private static HolyWaterOverhaulMain _self;

        // ---- 传奇全图灼烧的持久状态 ----
        private bool _legendActive;
        private InventItem _legendItem;
        private object _legendDps;
        private double _legendDuration = 99999.0;
        private double _legendTick;
        private int _legendBurns;

        /// <summary>施放时所在的关卡。关卡一变（进下一关 / 重开一局）就把状态清掉。</summary>
        private dc.pr.Level _boundLevel;

        // ---- 日志计数 ----
        private int _poolCasts;

        public HolyWaterOverhaulMain(ModInfo info) : base(info) { }

        internal static void Write(string msg)
        {
            global::System.Console.WriteLine(msg);
            try { _self?.Logger?.Information(msg); } catch { }
        }

        public override void Initialize()
        {
            base.Initialize();
            _self = this;

            try { Hook_HolyWater.onTrigger += OnTrigger; }
            catch (Exception ex) { Logger.Error(ex, "[HolyWaterOverhaul] Hook_HolyWater.onTrigger 挂载失败"); }

            // 雨的自毁冷却也是在 init 里按 props.duration 注册的 → 在 orig 之前把时长钉死
            try { Hook_HolyRain.init += OnRainInit; }
            catch (Exception ex) { Logger.Error(ex, "[HolyWaterOverhaul] Hook_HolyRain.init 挂载失败"); }

            var c = HolyWaterKeys.Config.Value;
            Logger.Information($"[HolyWaterOverhaul] 已加载：普通散射 {c.PoolCount} 个火堆（宽 {c.PoolSpreadTiles} 格）/" +
                               $"传奇全图灼烧={c.LegendBurnWholeMap}（每 {c.LegendTick}s）/ CD 与持续时间走数据补丁");
            try { HolyWaterKeys.Config.Save(); } catch { }
        }

        // ---------------------------------------------------------------- 触发

        private void OnTrigger(Hook_HolyWater.orig_onTrigger orig, HolyWater self)
        {
            var cfg = HolyWaterKeys.Config.Value;
            if (cfg == null || !cfg.EnableMod || self == null)
            {
                orig(self);
                return;
            }

            bool legend = false;
            try { legend = self.item != null && self.item.hasAffix(StringUtils.AsHaxeString(LegendAffix)); }
            catch { }

            if (legend)
            {
                // 保留原版的下雨表现（HolyRain），另外把"全图灼烧"打开
                try { orig(self); } catch (Exception ex) { Logger.Error(ex, "[HolyWaterOverhaul] 原版 HolyRain 生成失败"); }
                ArmLegend(self, cfg);
                return;
            }

            try
            {
                SpawnPools(self, cfg);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[HolyWaterOverhaul] 铺火堆失败，回退原版");
                try { orig(self); } catch { }
            }
        }

        /// <summary>普通：以落点为中心，把 <see cref="HolyWaterOverhaulConfig.PoolCount"/> 个火堆散射铺开。</summary>
        private void SpawnPools(HolyWater self, HolyWaterOverhaulConfig cfg)
        {
            var level = self._level;
            if (level == null) return;

            var itemData = self.item?._itemData;
            if (itemData == null) return;

            // 地面火堆的存活时间：**故意不用 99999**，见 Config.PoolDurationSeconds 的注释
            double duration = cfg.PoolDurationSeconds > 0 ? cfg.PoolDurationSeconds : 15.0;
            double duration2 = ToDouble(itemData.props.duration2, 0.7);

            int tier = 0;
            try { tier = self.getRelevantTierFor(self.item); } catch { }

            double value = duration;   // 占位，下面覆盖
            try
            {
                value = dc.Const.Class.scaleHeroValueToTier.Invoke(itemData.props.dps, tier);
            }
            catch (Exception ex)
            {
                if (_poolCasts < 3) Logger.Warning($"[HolyWaterOverhaul] dps 缩放失败，改用原值: {ex.Message}");
                value = ToDouble(itemData.props.dps, 30.0);
            }

            int count = Math.Max(1, Math.Min(200, cfg.PoolCount));
            double half = (count - 1) / 2.0;
            double perTile = count > 1 ? cfg.PoolSpreadTiles / (count - 1.0) : 0.0;

            int baseCx = self.cx;
            int baseCy = self.cy;
            int made = 0;

            for (int i = 0; i < count; i++)
            {
                int cx = baseCx + (int)Math.Round((i - half) * perTile);
                int cy = baseCy;

                if (cfg.PoolSnapToGround)
                {
                    try { cy = level.map.getGroundY(cx, baseCy); } catch { }
                }

                try
                {
                    level.addAreaAffectS(cx, cy, duration, BurnAffect, duration2, value, null);
                    made++;
                }
                catch (Exception ex)
                {
                    if (_poolCasts < 3) Logger.Warning($"[HolyWaterOverhaul] addAreaAffectS({cx},{cy}) 失败: {ex.Message}");
                }
            }

            _poolCasts++;
            Write($"[HolyWaterOverhaul] 普通：落点 ({baseCx},{baseCy}) 散射铺了 {made}/{count} 个火堆、" +
                  $"每格 {perTile:0.##}、火堆存活 {duration:0}s、每段 {value:0.##}（affect {BurnAffect}）");
        }

        /// <summary>传奇：记下 item / dps / duration，打开全图灼烧。</summary>
        private void ArmLegend(HolyWater self, HolyWaterOverhaulConfig cfg)
        {
            var itemData = self.item?._itemData;
            if (itemData == null) return;

            _legendItem = self.item;
            _legendDps = itemData.props.dps;
            _legendDuration = cfg.BurnSeconds > 0 ? cfg.BurnSeconds : 99999.0;   // 敌人身上的灼烧 = 无上限
            _legendActive = true;
            _legendTick = 0.0;
            _boundLevel = self._level;      // 记住施放时在哪一关，切关就重置

            Write($"[HolyWaterOverhaul] 传奇：已开启全图灼烧（每 {cfg.LegendTick}s 刷一次、" +
                  $"灼烧 {_legendDuration:0}s = 无上限、仅限本关）");
        }

        /// <summary>
        /// 传奇下雨：`_HolyRain.__inst_construct__` 里雨的存活时长是
        /// `props.duration * cd.baseFps` 帧，雨柱数量则来自
        /// `_Bresenham.iterateDisc(cx, cy, (int)(props.distance * 7.2), cb)`。
        ///
        /// 所以在 orig 之前把这两个值改掉：
        ///   · `props.duration`  → 地面雨柱的存活时间（有限值，别填满那个 512 格池）
        ///   · `props.distance`  → 圆盘半径（原版 14.4 格 / ≈615 根雨柱 → 默认 5 格 / ≈81 根）
        ///
        /// 全图灼烧由本模组自己那段 `setAffectS` 负责，不依赖这些雨柱。
        /// </summary>
        private void OnRainInit(Hook_HolyRain.orig_init orig, HolyRain self)
        {
            var cfg = HolyWaterKeys.Config.Value;
            double discCells = 0.0;
            double lifeSeconds = 0.0;

            if (cfg != null && cfg.EnableMod)
            {
                try
                {
                    var props = self?.item?._itemData?.props;
                    if (props != null)
                    {
                        lifeSeconds = cfg.PoolDurationSeconds > 0 ? cfg.PoolDurationSeconds : 15.0;
                        props.duration = (double?)lifeSeconds;

                        discCells = cfg.RainDiscCells > 0 ? cfg.RainDiscCells : 5.0;
                        props.distance = (double?)(discCells / 7.2);
                    }
                }
                catch (Exception ex) { Logger.Warning($"[HolyWaterOverhaul] 改雨参数失败: {ex.Message}"); }
            }

            orig(self);

            if (cfg != null && cfg.EnableMod && self != null)
            {
                double r = discCells > 0 ? discCells : 14.4;
                Write($"[HolyWaterOverhaul] 传奇雨已生成：圆盘半径 {r:0.#} 格（约 {Math.PI * r * r:0} 根雨柱）、" +
                      $"雨柱存活 {lifeSeconds:0}s（地面池只有 512 格，故意不设永久）");
            }
        }

        // ---------------------------------------------------------------- 每帧：全图灼烧

        /// <summary>
        /// 进入新关卡 / 重开一局时英雄会重建，这里把本模组的持久状态清干净
        /// （全图灼烧关掉、绑定的关卡和计数清零）。
        /// 上一关的火堆和雨本来就是关卡实体，跟着关卡一起没了。
        /// </summary>
        void IOnHeroInit.OnHeroInit()
        {
            ResetState("进入新关卡 / 新的一局");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (!_legendActive) return;

            var cfg = HolyWaterKeys.Config.Value;
            if (cfg == null || !cfg.EnableMod || !cfg.LegendBurnWholeMap) return;

            Hero hero = null;
            try { hero = Module<Game>.Instance.HeroInstance; } catch { }
            if (hero == null || hero.destroyed || hero.life <= 0 || hero._level == null) return;

            // 兜底：IOnHeroInit 万一没触发，靠关卡引用变化也能发现切关了
            if (_boundLevel != null && hero._level != _boundLevel)
            {
                ResetState("检测到关卡切换");
                return;
            }

            _legendTick -= dt;
            if (_legendTick > 0.0) return;
            _legendTick = Math.Max(0.05, cfg.LegendTick);

            int burned = BurnWholeMap(hero);
            _legendBurns += burned;

            if (burned > 0)
            {
                Write($"[HolyWaterOverhaul] 全图灼烧：本次点燃 {burned} 个敌人（本关累计 {_legendBurns}）");
            }
        }

        /// <summary>把本模组的持久状态清空。本来就没状态时不刷日志。</summary>
        private void ResetState(string reason)
        {
            bool hadState = _legendActive || _legendItem != null || _boundLevel != null;
            if (!hadState) return;

            _legendActive = false;
            _legendItem = null;
            _legendDps = null;
            _legendDuration = 99999.0;
            _legendTick = 0.0;
            _boundLevel = null;
            _poolCasts = 0;
            _legendBurns = 0;

            Write($"[HolyWaterOverhaul] {reason}：圣水状态已重置（全图灼烧关闭、绑定关卡与计数清零）");
        }

        /// <summary>
        /// 遍历**整张地图**的敌人，逐个挂 affect 88（原版 HolyRain 用的同一套），
        /// 但去掉了 `filterByArea` + `filterBySight` —— 所以墙后、屏幕外也照烧。
        /// </summary>
        private int BurnWholeMap(Hero hero)
        {
            int n = 0;
            try
            {
                var team = hero._team;
                if (team == null) return 0;

                // 先收集：一边遍历 team 一边让游戏改它不安全
                var targets = new System.Collections.Generic.List<dc.Entity>();
                var it = team.opponentsIterator.reset(team);
                if (it == null) return 0;

                while (it.hasNext())
                {
                    var e = it.next();
                    if (e == null || e.destroyed || e.life <= 0) continue;
                    if (e._level == null || e._level != hero._level) continue;
                    targets.Add(e);
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    var e = targets[i];
                    try
                    {
                        double doT;
                        try { doT = hero.getDoTValue(_legendItem, _legendDps, e, null); }
                        catch { doT = 0.0; }

                        e.setAffectS(BurnAffect, _legendDuration, Ref<double>.In(doT), null);
                        n++;
                    }
                    catch (Exception ex)
                    {
                        if (_legendBurns < 3) Logger.Warning($"[HolyWaterOverhaul] setAffectS 失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[HolyWaterOverhaul] 全图灼烧遍历失败");
            }
            return n;
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>props 里的数值是 dynamic，安全地取成 double。</summary>
        private static double ToDouble(object v, double fallback)
        {
            try
            {
                if (v == null) return fallback;
                if (v is double d) return d;
                if (v is float f) return f;
                if (v is int i) return i;
                return Convert.ToDouble(v.ToString());
            }
            catch { return fallback; }
        }

        // ---------------------------------------------------------------- 生命周期

        /// <summary>
        /// 挂载本模组自带的 res.pak。
        /// ⚠ 不挂的话 DCCM 的 CDBManager 读不到 data.cdb_ 补丁，CD 和持续时间都不会变。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string pakPath = null;
                try { pakPath = Info?.ModRoot?.GetFilePath("res.pak"); } catch { }
                if (string.IsNullOrEmpty(pakPath))
                {
                    string dir = Path.GetDirectoryName(typeof(HolyWaterOverhaulMain).Assembly.Location) ?? "";
                    pakPath = Path.Combine(dir, "res.pak");
                }

                if (File.Exists(pakPath))
                {
                    var fs = FsPak.Instance?.FileSystem;
                    if (fs == null) { Logger.Warning("[HolyWaterOverhaul] FsPak 还没就绪，数据补丁未挂载"); return; }
                    fs.loadPak(StringUtils.AsHaxeString(pakPath));
                    Logger.Information($"[HolyWaterOverhaul] res.pak 已挂载，CD=0 / 持续时间无上限 生效: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[HolyWaterOverhaul] 未找到 res.pak: {pakPath}（CD 和持续时间不会变）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[HolyWaterOverhaul] res.pak 加载失败");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_HolyWater.onTrigger -= OnTrigger; } catch { }
            try { Hook_HolyRain.init -= OnRainInit; } catch { }
            _legendActive = false;
            _legendItem = null;
            _legendDps = null;
            _boundLevel = null;
            Logger.Information($"[HolyWaterOverhaul] 游戏退出，模组已卸载（本次运行铺了 {_poolCasts} 次火堆、全图点燃 {_legendBurns} 次）");
            _self = null;
        }
    }
}
