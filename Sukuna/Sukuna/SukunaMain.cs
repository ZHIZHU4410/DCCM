#nullable disable

using dc;
using dc.en;
using dc.libs.heaps;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;

namespace Sukuna
{
    /// <summary>
    /// 宿傩（Sukuna）模组
    /// ====================
    /// 「以原版女王细剑的斩击机制为骨架」实现咒术回战里宿傩的三招：
    ///
    ///   · 解（Dismantle）—— 通常斩击
    ///       女王细剑命中目标后，在目标周围随机位置追加 N 段斩击（默认 8 段 / 0.12 秒一段）。
    ///       对应原版 queenStrikeTrigger 的"斩断现实"连锁，只是把连锁点改成了随机分布。
    ///
    ///   · 捌（Cleave）—— 针对强者的贯穿斩
    ///       目标若是精英 / 体型大 / 血厚，就在它身上补一条过其中心、方向随机的**无限长斩线**。
    ///       判定复刻原版 Queen.doCutLineAttack（点到直线距离 &lt; 半径 + width×24×0.5），
    ///       线上每个受击目标都用**同一个角度**放原版斩击，fxQueenRapierCut 于是连成一条贯穿斩。
    ///
    ///   · 伏魔御厨子（Malevolent Shrine）—— 领域展开
    ///       按释放键（默认 V）展开领域：
    ///         · 视觉 —— 调用原版「死亡球」(dc.en.bu.Orb 用的 dc.Fx.orb) 把球画在英雄身上，
    ///                   颜色改成红色，半径随领域进度从 DomainStartRadius 慢慢长到 DomainRadius，
    ///                   同时叠一层红色屏幕染色 + 闪光（原版 Fx.customMask / multiFlashBangS）。
    ///         · 伤害 —— 领域的**伤害半径与死亡球半径同步增长**，每 DomainInterval 秒
    ///                   对范围内全体敌人各放一发原版斩击（命中自带原版斩击特效）。
    ///
    /// 【数据部分】由 res.pak 数据补丁实现（patch_sukuna_data.py 生成）：
    ///     item/QueenRapier   ：distance 50→240（斩击线半长）、width 0.5→1.4（斩击线厚度）、
    ///                          duration 0.2→0.1（命中→斩击的延迟）
    ///     weapon/QueenRapier ：strikeChain[*].props.power2 35→58（斩击本体伤害）
    ///
    /// 【代码部分】本文件 + SukunaSlash.cs：全部伤害都调**原版** queenStrike，
    /// 所以伤害/tag/DamageType/震动/音效/fxQueenRapierCut 都是原版链路。
    ///
    /// 【开关】仿 DamageAuraBoost：
    ///     状态存在 coremod/config/Sukuna.json，游戏内「选项 → 模组 → 宿傩·斩击」可切换，
    ///     也支持配置热键（默认只给"领域释放"绑了 V）。
    /// </summary>
    public class SukunaMain : ModBase, IOnAfterLoadingAssets, IOnGameInit, IOnGameExit, IOnHeroUpdate, IModMenu
    {
        /// <summary>女王细剑的 item id（原版 data.cdb item 表里的 id）。</summary>
        public const string ItemId = "QueenRapier";

        /// <summary>模组单例（供 SukunaSlash 写日志）。</summary>
        public static SukunaMain Instance { get; private set; }

        /// <summary>斩击引擎。</summary>
        private readonly SukunaSlash _slash = new();

        /// <summary>本模组的 res.pak 是否已经挂进 FsPak（只挂一次）。</summary>
        private bool _pakLoaded;

        /// <summary>是否已经打印过"数据补丁自检"日志（只打一次）。</summary>
        private bool _patchVerified;

        /// <summary>上一帧的英雄实例；换关/换角色时用来重置状态。</summary>
        private Hero _lastHero;

        /// <summary>上一帧英雄所在的关卡；换关时用来清掉残留的斩击队列。</summary>
        private dc.pr.Level _lastLevel;

        public SukunaMain(ModInfo info) : base(info) { }

        /// <summary>同时写控制台与模组日志文件（coremod/logs/log_latest.log）。</summary>
        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        /// <summary>给 SukunaSlash 用的静态日志入口。</summary>
        public static void Log(string msg)
        {
            try { Instance?.Write(msg); } catch { }
        }

        public override void Initialize()
        {
            base.Initialize();
            Instance = this;

            // Hook_QueenRapier 是框架为 dc.tool.weap.QueenRapier 自动生成的包装类，
            // 把 queenStrike 暴露成 C# 事件；订阅即生效，无需 CreateHook/Enable。
            try { Hook_QueenRapier.queenStrike += OnQueenStrike; }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] Hook_QueenRapier.queenStrike 挂载失败"); }

            // Hook_Fx.allocMultiBatch：原版 queenStrike 就是用它生成 fxQueenRapierCut 斩痕粒子的。
            // 在这里把那颗粒子染成黑红（见 SukunaSlash.TryTintSlashFx）。
            try { Hook_Fx.allocMultiBatch += OnAllocMultiBatch; }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] Hook_Fx.allocMultiBatch 挂载失败"); }

            // Hook_QueenRapier.hitFromWeapon：标记"正在结算挥砍"，供下面吞掉挥砍伤害用。
            try { Hook_QueenRapier.hitFromWeapon += OnSwingHitFromWeapon; }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] Hook_QueenRapier.hitFromWeapon 挂载失败"); }

            // Hook__AttackUtils.hit：挥砍的那次普通命中在这里被吞掉（只保留 queenStrike 的斩击）。
            try { Hook__AttackUtils.hit += OnAttackUtilsHit; }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] Hook__AttackUtils.hit 挂载失败"); }

            // Hook_Weapon.canHit：原版会做地图碰撞 / 视线检测（map.collisions + sightCheckCase），
            // 墙后的敌人砍不到。这里给女王细剑补一条"只看距离"的穿墙兜底。
            try { Hook_Weapon.canHit += OnCanHit; }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] Hook_Weapon.canHit 挂载失败"); }

            // Hook__Assets.getItem：把女王细剑的图标换成 Assets/sunuo.png。
            try { Hook__Assets.getItem += OnGetItem; }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] Hook__Assets.getItem 挂载失败"); }

            // Hook_QueenRapier.getStrikeAngle：原版这里返回的是连击表里写死的角度
            // （1a 斜下 / 2a 斜上 / 3a 水平），改成每一刀随机。
            try { Hook_QueenRapier.getStrikeAngle += OnGetStrikeAngle; }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] Hook_QueenRapier.getStrikeAngle 挂载失败"); }

            Logger.Information("[Sukuna] 已加载：解 / 捌 / 伏魔御厨子（全部走原版 queenStrike 结算）");
        }

        /// <summary>
        /// 资源加载完成 —— 把本模组自己的 res.pak 挂进游戏文件系统。
        ///
        /// 这一步是**数据补丁能不能生效的关键**：MDK 只是把 res.pak 复制到
        /// coremod/mods/Sukuna/，DCCM 并不会自动挂载它。必须在这里手动 loadPak，
        /// 之后 DCCM 的 CDBManager.GetAlteredCDB() 才能从 FsPak 里读到
        /// data.cdb_/item/QueenRapier.json 与 data.cdb_/weapon/QueenRapier.json，
        /// 把改动合进 data.cdb。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            LoadOwnResPak();
        }

        /// <summary>把模组自带的 res.pak 挂进 FsPak（幂等）。</summary>
        private void LoadOwnResPak()
        {
            if (_pakLoaded) return;
            try
            {
                // 官方推荐的取法：Info.ModRoot.GetFilePath("res.pak")
                string pakPath = null;
                try { pakPath = Info?.ModRoot?.GetFilePath("res.pak"); } catch { }

                // 兜底：dll 所在目录（AutoInstallMod 会把 res.pak 和 dll 放一起）
                if (string.IsNullOrEmpty(pakPath))
                {
                    string dir = System.IO.Path.GetDirectoryName(typeof(SukunaMain).Assembly.Location) ?? "";
                    pakPath = System.IO.Path.Combine(dir, "res.pak");
                }

                if (!System.IO.File.Exists(pakPath))
                {
                    Logger.Warning($"[Sukuna] 未找到 res.pak: {pakPath} —— 数据补丁（动作/特效/距离）不会生效！");
                    return;
                }

                // FsPak.FileSystem 是 DCCM 在游戏第一次 loadPak 时抓到的实例；
                // IOnAfterLoadingAssets 触发时它一定已经就绪（游戏自己的 res.pak 已经先挂上了）。
                var fs = FsPak.Instance?.FileSystem;
                if (fs == null)
                {
                    Logger.Warning("[Sukuna] FsPak.FileSystem 还没就绪，稍后重试挂载 res.pak");
                    return;
                }

                fs.loadPak(StringUtils.AsHaxeString(pakPath));
                _pakLoaded = true;
                Logger.Information($"[Sukuna] res.pak 已挂载，数据补丁生效: {pakPath}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Sukuna] res.pak 加载失败 —— 数据补丁（动作/特效/距离）不会生效");
            }
        }

        /// <summary>开局自检：写一份默认配置到 coremod/config/Sukuna.json（第一次运行会生成）。</summary>
        void IOnGameInit.OnGameInit()
        {
            // 兜底重试：如果 IOnAfterLoadingAssets 那一次没挂上（例如取路径失败），这里再试一次。
            // 幂等 —— 已经挂上就直接返回。
            LoadOwnResPak();

            try
            {
                var c = SukunaKeys.Config.Value;
                Logger.Information(
                    "[Sukuna] 开关状态 —— " +
                    $"总开关={c.EnableMod} 解={c.EnableDismantle} 捌={c.EnableCleave} 御厨子={c.EnableDomain}");
                Logger.Information(
                    $"[Sukuna] 领域释放键={c.KeyCastDomain}（默认 V，可改；留空 = 不绑）");
                Logger.Information(
                    $"[Sukuna] 配置文件: {SukunaKeys.Config.ConfigPath}" +
                    "（也可在游戏的 选项 → 模组 → 宿傩·斩击 菜单里直接改）");

                try { SukunaKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[Sukuna] 写配置失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- Hook

        /// <summary>
        /// 原版 queenStrike 的 Hook：先让原版把这一刀打完（伤害 + fxQueenRapierCut + 音效 + 震动），
        /// 再把"追加斩击"排进队列 —— 真正的追加发生在后续帧，所以不会和原版这一刀抢冷却。
        /// </summary>
        private void OnQueenStrike(Hook_QueenRapier.orig_queenStrike orig,
                                   dc.tool.weap.QueenRapier self,
                                   Entity target, double x, double y, double angle)
        {
            // 原版 queenStrike 内部会用**完全相同的 (x,y)** 去 fx.allocMultiBatch 生成
            // fxQueenRapierCut 斩痕粒子 —— 先把坐标记下来，让 Fx 那边的 Hook 认出它并染成黑红。
            bool tint = SukunaFeatures.IsOn(SukunaFeature.Mod);
            if (tint) _slash.BeginVanillaSlashFx(x, y);
            try
            {
                orig(self, target, x, y, angle);
            }
            finally
            {
                if (tint) _slash.EndVanillaSlashFx();
            }

            if (!SukunaFeatures.IsOn(SukunaFeature.Mod)) return;

            // 第一次真正挥出女王细剑时，把"实际生效的数值"打进日志，
            // 方便一眼确认 res.pak 数据补丁到底有没有合进 CDB。
            LogPatchSelfCheck(self);

            try { _slash.OnVanillaStrike(self, target, x, y, angle); }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] 追加斩击排队失败"); }
        }

        /// <summary>
        /// Hook dc.Fx.allocMultiBatch —— 原版 queenStrike 生成斩痕粒子的唯一入口。
        /// 只有在"女王细剑那一刀"的调用里、且坐标对得上时才染色（见 SukunaSlash.TryTintSlashFx），
        /// 因此不会影响其它任何特效。
        /// </summary>
        private HParticle OnAllocMultiBatch(Hook_Fx.orig_allocMultiBatch orig,
                                            dc.Fx self,
                                            SpriteBatchGroup batchGroup,
                                            FxTile t,
                                            double x, double y,
                                            Ref<bool> bDontKillEarly,
                                            virtual___alloc_ p,
                                            Ref<bool> ignoreParticleLimit)
        {
            HParticle hp = orig(self, batchGroup, t, x, y, bDontKillEarly, p, ignoreParticleLimit);
            try { _slash.TryTintSlashFx(hp, x, y); } catch { }
            return hp;
        }

        // ---------------------------------------------------------------- 只保留斩击伤害

        /// <summary>是否正在原版 <c>hitFromWeapon</c> 里（那一次普通命中就是"挥砍伤害"）。</summary>
        private bool _inSwingHit;

        /// <summary>
        /// Hook QueenRapier.hitFromWeapon：原版这里做了两件事 ——
        ///   ① <c>_AttackUtils.hit(createFromHeroWeapon(this,null), e)</c> ← **挥砍伤害**
        ///   ② <c>delayer.addS(..., queenStrike, itemInf.props.duration)</c> ← 延迟斩击
        /// 我们要留②删①，所以打个标记，让 OnAttackUtilsHit 把这一窗口内的普通命中吞掉。
        /// （queenStrike 是延迟回调，跑在这个窗口之外，所以斩击伤害不受影响。）
        /// </summary>
        private void OnSwingHitFromWeapon(Hook_QueenRapier.orig_hitFromWeapon orig,
                                          dc.tool.weap.QueenRapier self,
                                          Entity e, Ref<int> _cycle)
        {
            bool prev = _inSwingHit;
            _inSwingHit = true;
            try { orig(self, e, _cycle); }
            finally { _inSwingHit = prev; }
        }

        /// <summary>
        /// Hook _AttackUtils.hit：处于"挥砍窗口"、且攻击来源就是女王细剑时，直接吞掉 ——
        /// 这就是"删除挥砍伤害，只保留斩击伤害"。
        /// queenStrike 造的攻击（dmgType = ExtraDamage）走的是另一条时间线，不会被吞。
        /// </summary>
        private void OnAttackUtilsHit(Hook__AttackUtils.orig_hit orig, AttackData atk, Entity target)
        {
            if (_inSwingHit && SukunaFeatures.IsOn(SukunaFeature.SlashOnly))
            {
                try
                {
                    if (atk != null && atk.sourceWeapon is dc.tool.weap.QueenRapier)
                    {
                        _swingHitsEaten++;
                        if (_swingHitsEaten % 40 == 1)
                        {
                            Write($"[Sukuna] 已吞掉挥砍伤害 #{_swingHitsEaten}（只保留斩击伤害）");
                        }
                        return;
                    }
                }
                catch { }
            }
            orig(atk, target);
        }

        /// <summary>被吞掉的挥砍命中次数（日志采样用）。</summary>
        private int _swingHitsEaten;

        // ---------------------------------------------------------------- 无视墙体

        /// <summary>
        /// Hook Weapon.canHit：原版会做地图碰撞 / 视线检测，墙后的敌人砍不到。
        /// 女王细剑额外走一遍 <see cref="SukunaSlash.InWallIgnoreRange"/>（只看距离和朝向），
        /// 于是挥砍能"打穿"墙、把斩击送到墙后。
        /// </summary>
        private bool OnCanHit(Hook_Weapon.orig_canHit orig, dc.tool.Weapon self, Entity e, dc.tool.Area area)
        {
            if (orig(self, e, area)) return true;

            if (!SukunaFeatures.IsOn(SukunaFeature.WallIgnore)) return false;
            if (!(self is dc.tool.weap.QueenRapier)) return false;

            try { return _slash.InWallIgnoreRange(self.owner, e); }
            catch { return false; }
        }

        // ---------------------------------------------------------------- 随机斩击角度

        /// <summary>
        /// Hook QueenRapier.getStrikeAngle —— 原版返回的是连击表里写死的角度：
        ///
        ///     angle = (dir &lt; 0 ? π : 0) + dir * strikeChain[cycle].props.angle
        ///
        /// 我们数据补丁里的 props.angle 是 0.5 / -0.4 / 0（1a 斜下 / 2a 斜上 / 3a 水平），
        /// 所以三段连击的斩击方向永远一样。这里改成每一刀都在配置范围内随机。
        ///
        /// 这个角度同时决定：① queenStrike 里 fxQueenRapierCut 的朝向；
        /// ② queenStrikeTrigger 那条"斩断现实"连锁线的方向。所以改了之后两者一起变。
        /// </summary>
        private double OnGetStrikeAngle(Hook_QueenRapier.orig_getStrikeAngle orig,
                                        dc.tool.weap.QueenRapier self, int c)
        {
            double baseAngle = orig(self, c);

            if (!SukunaFeatures.IsOn(SukunaFeature.RandomAngle)) return baseAngle;

            try { return _slash.RandomizeStrikeAngle(baseAngle); }
            catch { return baseAngle; }
        }

        // ---------------------------------------------------------------- 自定义图标

        /// <summary>自定义图标（Assets/sunuo.png 打进的 res.pak 里）。null = 还没成功加载。</summary>
        private static dc.h2d.Tile _customIcon;

        /// <summary>自定义图标的基础规格：宽/高（原版物品图标 = data.cdb 里 icon.size = 24）。</summary>
        private static int _customIconW = 24;
        private static int _customIconH = 24;

        /// <summary>加载尝试次数（加载器可能还没热起来，允许重试几次）。</summary>
        private static int _customIconTries;

        /// <summary>
        /// Hook Assets.getItem：把女王细剑的图标换成 res.pak 里的 sunuo.png。
        /// 加载失败就退回原版图标（orig），不会影响其它物品。
        ///
        /// 注意这里每次都返回**新建**的 tile —— 原版 <c>Assets.getItem</c> 也是每次
        /// <c>itemIcons.sub(...)</c> 新建一个，避免某个 UI 改了 dx/dy 之后影响所有调用点。
        /// </summary>
        private dc.h2d.Tile OnGetItem(Hook__Assets.orig_getItem orig, dc.String i)
        {
            try
            {
                if (i != null && i.ToString() == ItemId)
                {
                    var baseTile = GetCustomIcon();
                    if (baseTile != null)
                    {
                        // 原版：itemIcons.sub(x, y, size, size, null, null)
                        // sub 会把 dx/dy 归零 —— 这一点很关键，见 GetCustomIcon 的注释。
                        return baseTile.sub(0, 0, _customIconW, _customIconH,
                                            Ref<int>.In(0), Ref<int>.In(0));
                    }
                }
            }
            catch { }
            return orig(i);
        }

        /// <summary>
        /// 从 res.pak 里加载 sunuo.png 并转成 Tile（懒加载 + 有限重试）。
        /// 用的就是原版 <c>_Assets.init()</c> 加载 cardIcons.png 的同一套 API：
        /// <c>Res.Class.get_loader().loadCache(path, Image.Class).toTile()</c>。
        ///
        /// ⚠️ 关键坑：<c>hxd.res.Image.toTile()</c> 拿到的 tile 带的是 **居中轴心**
        /// （<c>dx = -width/2, dy = -height/2</c>），而原版物品图标
        /// （<c>itemIcons.sub(x, y, size, size, null, null)</c>）的 <c>dx = dy = 0</c>。
        /// 如果直接把 toTile() 的结果交给 UI，图标就会整体**左上方偏移半个图标**，
        /// 表现就是"选框左上角正好对着图标中心"。
        ///
        /// 所以这里照抄游戏自己的做法（见 <c>dc._Assets</c> 里
        /// <c>_Res.load(id).toTile().sub(0, 0, w, h, null, null)</c>）：
        /// 用 <c>sub(..., 0, 0)</c> 把 dx/dy 归零。
        /// </summary>
        private dc.h2d.Tile GetCustomIcon()
        {
            if (_customIcon != null) return _customIcon;
            if (_customIconTries >= 30) return null;
            _customIconTries++;

            try
            {
                var resClass = dc.hxd.Res.Class;
                // 注意：代理里 get_loader 是一个属性（名字本身带 get_ 前缀），不是方法
                var loaderFunc = resClass?.get_loader;
                var loader = loaderFunc?.Invoke();
                if (loader == null) return null;

                var res = loader.loadCache(StringUtils.AsHaxeString(IconPath), dc.hxd.res.Image.Class);
                var img = res as dc.hxd.res.Image;
                if (img == null)
                {
                    Logger.Warning($"[Sukuna] 图标 {IconPath} 加载失败（不是图片资源？），退回原版图标");
                    _customIconTries = 999;
                    return null;
                }

                var raw = img.toTile();
                if (raw == null || raw.width <= 0 || raw.height <= 0)
                {
                    Logger.Warning($"[Sukuna] 图标 {IconPath} toTile() 异常，退回原版图标");
                    return null;
                }

                int w = raw.width;
                int h = raw.height;
                int rawDx = raw.dx;
                int rawDy = raw.dy;

                // sub(0,0,w,h, 0,0) 同时把 dx/dy 归零 —— 对齐原版 itemIcons.sub(...) 的规格
                var tile = raw.sub(0, 0, w, h, Ref<int>.In(0), Ref<int>.In(0));
                tile.dx = 0;
                tile.dy = 0;

                _customIconW = w;
                _customIconH = h;
                _customIcon = tile;

                Write($"[Sukuna] 图标已替换：{IconPath} {w}x{h}" +
                      $"（原始 dx={rawDx} dy={rawDy} → 已归零，对齐原版 itemIcons.sub 的轴心）");
                return _customIcon;
            }
            catch (Exception ex)
            {
                if (_customIconTries >= 30)
                {
                    Logger.Warning($"[Sukuna] 图标 {IconPath} 加载失败，退回原版图标: {ex.Message}");
                }
                return null;
            }
        }

        /// <summary>res.pak 里图标资源的路径（= Assets/sunuo.png，RootInPak 为空）。</summary>
        private const string IconPath = "sunuo.png";

        /// <summary>
        /// 一次性自检：直接读**当前这把武器**的运行时数据（CDB 合并后的结果），
        /// 打印动作 / 特效 / 距离，和补丁期望值对照。
        /// </summary>
        private void LogPatchSelfCheck(dc.tool.weap.QueenRapier self)
        {
            if (_patchVerified) return;
            _patchVerified = true;
            try
            {
                string dist = "?", width = "?", duration = "?";
                try
                {
                    dynamic props = self.itemInf?.props;
                    if (props != null)
                    {
                        dist = ((double)props.distance).ToString("0.###");
                        width = ((double)props.width).ToString("0.###");
                        duration = ((double)props.duration).ToString("0.###");
                    }
                }
                catch { }

                string anim = "?", fx = "?";
                try
                {
                    var chain = self.wInfos?.strikeChain;
                    if (chain != null && chain.length > 0)
                    {
                        var si = (virtual_animId_animSpd_area_breachBonus_canCrit_charge_coolDown_critMul_dynamicCharge_earlyCombo_fxId_fxProps_glowColor_hitFrame_lockCtrlAfter_onionSkinFrame_onionSkinOffX_power_props_sfxCharge_sfxHit_sfxProps_sfxRelease_)chain.getDyn(0);
                        if (si != null)
                        {
                            anim = si.animId?.ToString() ?? "null";
                            fx = si.fxId?.ToString() ?? "null";
                        }
                    }
                }
                catch { }

                Write("[Sukuna] 数据补丁自检（实际生效值）：" +
                      $"item.props.distance={dist} width={width} duration={duration}；" +
                      $"strikeChain[0].animId={anim} fxId={fx}");
                Write("[Sukuna] 期望值：distance=420 width=1.8 duration=0.1；" +
                      "animId=atkPunchA fxId=fxAtkPunchA（若不一致说明 res.pak 没挂上）");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[Sukuna] 数据补丁自检失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 每帧

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero hero = null;
            try { hero = Module<Game>.Instance.HeroInstance; } catch { }
            if (hero == null)
            {
                _lastHero = null;
                _lastLevel = null;
                return;
            }

            // 换英雄实例（重开 / 换角色）或换关卡 → 清掉上一关的残留状态
            bool heroChanged = !ReferenceEquals(hero, _lastHero);
            bool levelChanged = !ReferenceEquals(hero._level, _lastLevel);
            if (heroChanged || levelChanged)
            {
                _lastHero = hero;
                _lastLevel = hero._level;
                _slash.ResetForNewLevel();
                SukunaFeatures.ResetHotkeyState();
            }

            // 热键：各功能开关 + 「伏魔御厨子」释放键
            try { SukunaFeatures.PollHotkeys(Write, () => TryCastDomain(hero)); } catch { }

            try { _slash.Update(dt, hero); }
            catch (Exception ex) { Logger.Error(ex, "[Sukuna] 斩击引擎更新失败"); }
        }

        /// <summary>按下释放键：展开伏魔御厨子。</summary>
        private void TryCastDomain(Hero hero)
        {
            try
            {
                if (!SukunaFeatures.IsOn(SukunaFeature.Domain))
                {
                    Write("[Sukuna] 伏魔御厨子 已关闭（选项 → 模组 → 宿傩·斩击）");
                    return;
                }

                if (_slash.CastDomain(hero))
                {
                    var c = SukunaKeys.Config.Value;
                    Write($"[Sukuna] 领域展开·伏魔御厨子：{c.DomainStartRadius}px → {c.DomainRadius}px" +
                          $"（{c.DomainGrowTime}s 长满），持续 {c.DomainDuration}s，每 {c.DomainInterval}s 结算一次，" +
                          $"死亡球颜色 #{c.DomainOrbColor}");
                }
                else
                {
                    Write("[Sukuna] 领域展开失败：需要手持女王细剑（Queen's Rapier）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[Sukuna] 领域展开异常");
            }
        }

        // ---------------------------------------------------------------- 选项菜单（IModMenu）
        //
        // 与 DamageAuraBoost 一致：这一页不是自动生成的，而是在 BuildMenu() 里
        // 自己 addToggleWidget 建出来（开关值写回配置并落盘）。

        /// <summary>这一页在「选项 → 模组」里的名字。</summary>
        public string GetName() => "宿傩·斩击";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("SUKUNA 设置"));
                b.createScroller(0.0);

                foreach (var f in SukunaFeatures.All)
                {
                    var feature = f;                       // 闭包捕获：别直接用循环变量
                    bool on = SukunaFeatures.RawGet(feature);
                    string key = SukunaFeatures.KeyName(feature);
                    string hint = SukunaFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";
                    if (feature == SukunaFeature.Domain)
                    {
                        string cast = SukunaKeys.Config.Value?.KeyCastDomain ?? "";
                        hint += string.IsNullOrWhiteSpace(cast) ? "（未绑释放键）" : $"（释放键 {cast}）";
                    }

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(SukunaFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !SukunaFeatures.RawGet(feature);
                            SukunaFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                // ---- 数值滑条（写法照抄 ZoomVision 的 addSliderWidget）----
                //
                // addSliderWidget(标题, onUpdate, 当前值, 步进, 父Flow,
                //                 showPercent, showRawValue, 最小值, 最大值, 按钮, paddingLeft)
                // 拖动即写回配置并落盘；领域相关的数值都是**下一次/当次展开立刻生效**。
                var cfg = SukunaKeys.Config.Value;

                AddSlider(b, "领域持续时间 (秒)", cfg.DomainDuration, 0.5, 1.0, 20.0,
                          v => cfg.DomainDuration = v,
                          "伏魔御厨子持续多久；下次展开生效");

                AddSlider(b, "领域最终半径 (px)", cfg.DomainRadius, 10.0, 100.0, 900.0,
                          v => cfg.DomainRadius = v,
                          "领域长满后的半径（= 伤害半径）；即时生效");

                AddSlider(b, "领域展开时长 (秒)", cfg.DomainGrowTime, 0.1, 0.2, 10.0,
                          v => cfg.DomainGrowTime = v,
                          "从初始半径缓入长满所需时间；越小张得越快");

                AddSlider(b, "领域缓入指数", cfg.DomainGrowEase, 0.5, 1.0, 4.0,
                          v => cfg.DomainGrowEase = v,
                          "1=匀速，2=先慢后快（默认），越大起步越慢、后段越猛");

                AddSlider(b, "领域初始半径 (px)", cfg.DomainStartRadius, 10.0, 0.0, 300.0,
                          v => cfg.DomainStartRadius = v,
                          "刚展开那一瞬间的半径");

                AddSlider(b, "领域结算间隔 (秒)", cfg.DomainInterval, 0.05, 0.1, 2.0,
                          v => cfg.DomainInterval = v,
                          "每隔多久对范围内敌人斩一次");

                AddSlider(b, "解·追斩段数", cfg.DismantleSlashes, 1.0, 0.0, 32.0,
                          v => cfg.DismantleSlashes = (int)System.Math.Round(v),
                          "女王细剑命中后在目标周围追加几段斩击");

                AddSlider(b, "无视墙体·身前距离 (格)", cfg.WallIgnoreRange, 0.5, 1.0, 30.0,
                          v => cfg.WallIgnoreRange = v,
                          "穿墙能打到多远（24px = 1 格）");

                AddSlider(b, "无视墙体·高度差 (格)", cfg.WallIgnoreHeight, 0.5, 1.0, 20.0,
                          v => cfg.WallIgnoreHeight = v,
                          "穿墙判定允许的上下高度差");

                AddSlider(b, "斩击角度·随机范围 (度)", cfg.StrikeAngleRangeDeg, 10.0, 0.0, 360.0,
                          v => cfg.StrikeAngleRangeDeg = v,
                          "360=完全随机，90=正向±45°，0=原版固定角度");

                b.updateScroller();
                Write($"[Sukuna] 选项菜单已建立：{SukunaFeatures.All.Length} 个功能开关 + 10 个滑条");
            }
            catch (Exception ex)
            {
                Write($"[Sukuna] 建立选项菜单失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 加一条数值滑条（照抄 ZoomVision 的调用形态）。
        /// 拖动时会调用 apply() 把值写回配置并落盘。
        /// </summary>
        private void AddSlider(dc.ui.OptionsBase b, string label, double value,
                               double step, double min, double max,
                               Action<double> apply, string hint = null)
        {
            try
            {
                string text = string.IsNullOrEmpty(hint) ? label : $"{label} — {hint}";

                var onUpdate = (HlAction<double>)delegate (double v)
                {
                    try
                    {
                        apply(v);
                        SukunaKeys.Config.Save();
                    }
                    catch { }
                    Write($"[Sukuna] {label} = {v:0.##}");
                };

                b.addSliderWidget(
                    StringUtils.AsHaxeString(text),
                    onUpdate,
                    value,
                    Ref<double>.In(step),
                    b.scrollerFlow,
                    Ref<bool>.In(false),     // showPercent
                    Ref<bool>.In(true),      // showRawValue
                    Ref<double>.In(min),
                    Ref<double>.In(max),
                    null,                    // button
                    Ref<int>.In(0));         // paddingLeft
            }
            catch (Exception ex)
            {
                Write($"[Sukuna] 建立滑条失败（{label}）: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_QueenRapier.queenStrike -= OnQueenStrike; } catch { }
            try { Hook_QueenRapier.hitFromWeapon -= OnSwingHitFromWeapon; } catch { }
            try { Hook_QueenRapier.getStrikeAngle -= OnGetStrikeAngle; } catch { }
            try { Hook_Fx.allocMultiBatch -= OnAllocMultiBatch; } catch { }
            try { Hook__AttackUtils.hit -= OnAttackUtilsHit; } catch { }
            try { Hook_Weapon.canHit -= OnCanHit; } catch { }
            try { Hook__Assets.getItem -= OnGetItem; } catch { }            _slash.ResetForNewLevel();
            SukunaFeatures.ResetHotkeyState();
            Logger.Information("[Sukuna] 游戏退出，模组已卸载");
            Instance = null;
        }
    }
}
