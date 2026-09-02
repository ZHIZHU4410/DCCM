#nullable disable

using dc;
using dc.en;
using dc.en.bu;
using dc.hl.types;
using dc.pr;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero;
using dc.tool.weap.bow;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.IO;

namespace MagicBowBoost
{
    /// <summary>
    /// MagicBow（魔法弓）强化模组
    /// ==============================
    /// 【数值部分】由 res.pak 数据补丁实现（patch_magicbow_cdb.py 生成）：
    ///   count 24（每发箭数）、ang 0.06（散射）、speed 3.0（弹速）、range 12（追踪范围）、
    ///   distance 1（提前开始追踪）、limit 0.4（转向更猛）、offsetY 1.5（垂直聚拢）、
    ///   buff 1.10（同目标连击增伤）、ammo 9999、weapon coolDown 0.15（射速 x4）。
    ///
    /// 【代码部分】本代码实现 ——
    ///   1) 无视墙体：子弹标记 ignoreWalls（穿透墙壁不再被地形挡住）；
    ///   2) 无视墙体追踪：重写 chooseTarget，去掉视线/墙体检测，改为在追踪范围内
    ///      直接锁定最近的可侦测敌人（隔墙索敌）；
    ///   3) 远程：把箭的最大飞行距离从 11 格拉长到 15 格（360px），箭速 3.0 让箭快速
    ///      离场，避免同屏箭数过多撑爆碰撞缓冲；
    ///   4) 高射速：配合 cdb 冷却 0.075s（射速 x8），每次攻击一轮箭幕（24 支）；
    ///   5) 碰撞缓冲：Level 的圆形碰撞缓冲上限从 1024 提到 2048，防止大量箭（每支箭
    ///      都是参与碰撞的实体）触发 "Exceed circular collision entities buffer" 而跳过命中。
    /// </summary>
    public class MagicBowBoostMain : ModBase, IOnGameExit, IOnAfterLoadingAssets
    {
        /// <summary>每次攻击的箭幕轮数（1 = 每击一轮 24 支；冷却 0.075s 下已足够密集）。</summary>
        private const int BurstVolleys = 1;

        /// <summary>箭的最大飞行距离（像素）：15 格 × 24px（略大于追踪范围 12 格）。</summary>
        private const double ExtendedMaxDistPx = 15.0 * 24.0;

        /// <summary>Level 圆形碰撞缓冲上限（原版 1024，超过会跳过碰撞检查并打警告）。</summary>
        private const int CirColBufferMaxCount = 2048;

        public MagicBowBoostMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            // 扩大 Level 圆形碰撞缓冲上限（原版 1024）：箭太多时避免跳过碰撞/命中检查
            try
            {
                int oldCap = Level.Class.cirColBufferMaxCount;
                Level.Class.cirColBufferMaxCount = CirColBufferMaxCount;
                Logger.Information($"[MagicBowBoost] 碰撞缓冲上限 {oldCap} -> {CirColBufferMaxCount}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[MagicBowBoost] 调整碰撞缓冲上限失败");
            }
            try { Hook_MagicBowArrow.initOrigin += OnArrowInitOrigin; }
            catch (Exception ex) { Logger.Error(ex, "[MagicBowBoost] Hook_MagicBowArrow.initOrigin 挂载失败"); }
            try { Hook_MagicBowArrow.fixedUpdate += OnArrowFixedUpdate; }
            catch (Exception ex) { Logger.Error(ex, "[MagicBowBoost] Hook_MagicBowArrow.fixedUpdate 挂载失败"); }
            try { Hook_MagicBowArrow.chooseTarget += OnArrowChooseTarget; }
            catch (Exception ex) { Logger.Error(ex, "[MagicBowBoost] Hook_MagicBowArrow.chooseTarget 挂载失败"); }
            try { Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUse; }
            catch (Exception ex) { Logger.Error(ex, "[MagicBowBoost] Hook_HeroWeaponsManager.onWeaponUse 挂载失败"); }
            Logger.Information("[MagicBowBoost] 已加载: 数值=res.pak 数据补丁, 子弹无视墙体+隔墙追踪+射速x8");
        }

        /// <summary>资源加载完成：手动加载 mod 自带的 res.pak（数据补丁）。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(MagicBowBoostMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[MagicBowBoost] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[MagicBowBoost] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[MagicBowBoost] res.pak 加载失败");
            }
        }

        // ---------- 1) 箭出生时：无视墙体 ----------
        private void OnArrowInitOrigin(Hook_MagicBowArrow.orig_initOrigin orig, MagicBowArrow self, double x, double y)
        {
            orig(self, x, y);
            try
            {
                if (!self.destroyed)
                {
                    self.ignoreWalls = true;
                }
            }
            catch { }
        }

        // ---------- 3) 每帧：确保穿墙 + 拉长飞行距离 ----------
        private void OnArrowFixedUpdate(Hook_MagicBowArrow.orig_fixedUpdate orig, MagicBowArrow self)
        {
            orig(self);
            try
            {
                if (self.destroyed)
                {
                    return;
                }
                self.ignoreWalls = true;
                if (self.maxDist < ExtendedMaxDistPx)
                {
                    self.maxDist = ExtendedMaxDistPx;
                }
            }
            catch { }
        }

        // ---------- 2) 隔墙追踪：无视墙体/视线直接选最近目标 ----------
        /// <summary>
        /// 原版 chooseTarget 用 sightCheckCase 做视线（墙体）检测，隔墙敌人不会被锁定。
        /// 这里整体替换：在 homingRange 内选距离最近的、存活且可侦测的敌人，完全无视墙体。
        /// </summary>
        private Entity OnArrowChooseTarget(Hook_MagicBowArrow.orig_chooseTarget orig, MagicBowArrow self, ArrayObj candidates)
        {
            if (candidates == null || candidates.length == 0)
            {
                return null;
            }
            double bestDist = self.homingRange;
            Entity best = null;
            for (int i = 0; i < candidates.length; i++)
            {
                Entity e = candidates.getDyn(i) as Entity;
                if (e == null || e.destroyed || e.life <= 0)
                {
                    continue;
                }
                bool detectable = false;
                try { detectable = e.canBeDetected(); }
                catch { continue; }
                if (!detectable)
                {
                    continue;
                }
                // 与 vanilla 相同的距离公式（格子单位）
                double dx = (self.cx + self.xr) - (e.cx + e.xr);
                double dy = (self.cy + self.yr - self.hei / 48.0) - (e.cy + e.yr - e.hei / 48.0);
                double dist = System.Math.Sqrt(dx * dx + dy * dy);
                if (dist >= bestDist)
                {
                    continue;
                }
                bestDist = dist;
                best = e;
            }
            return best;
        }

        // ---------- 4) 高射速连发：每次攻击多射几轮箭幕 ----------
        private void OnWeaponUse(Hook_HeroWeaponsManager.orig_onWeaponUse orig, HeroWeaponsManager self, Weapon w, int slot)
        {
            orig(self, w, slot);
            if (w is not MagicBow mb || mb.destroyed)
            {
                return;
            }
            try
            {
                for (int i = 1; i < BurstVolleys; i++)
                {
                    mb.shoot(null);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[MagicBowBoost] 连发失败");
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_MagicBowArrow.initOrigin -= OnArrowInitOrigin;
            Hook_MagicBowArrow.fixedUpdate -= OnArrowFixedUpdate;
            Hook_MagicBowArrow.chooseTarget -= OnArrowChooseTarget;
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUse;
            Logger.Information("[MagicBowBoost] 游戏退出，模组已卸载");
        }
    }
}
