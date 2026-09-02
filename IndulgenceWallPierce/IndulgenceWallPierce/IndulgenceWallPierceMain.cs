using dc;
using dc.en;
using dc.hl.types;
using dc.pow;
using dc.pr;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using System;
using System.Collections.Generic;

namespace IndulgenceWallPierce
{
    /// <summary>
    /// Indulgence（宽恕 / 赎罪）强化模组：
    /// - 全图索敌：无视屏幕视口限制，当前关卡任意位置都能成为目标；
    /// - 穿墙打击：无视墙体、视线、单向板等阻挡（不调用原版视线检测）；
    /// - 由近及远依次打击：每次命中后自动选择剩余敌人中最近的一个，
    ///   把命中次数动态扩展到剩余敌人数量，依次打完全图所有敌人。
    /// 参考 chuanqiang（穿墙）模组的 Hook 思路实现。
    /// </summary>
    public class IndulgenceWallPierceMain : ModBase, IOnGameExit
    {
        // 命中后施加到目标身上的冷却 key（原版机制：防止同一敌人被重复选中）
        private const int HitAgainCdKey = -1962934272;

        public IndulgenceWallPierceMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_Indulgence.getTarget += OnGetTarget;
            Hook_Indulgence.hitTarget += OnHitTarget;
            System.Console.WriteLine("[IndulgenceWallPierce] Indulgence 已强化：全图索敌 + 穿墙 + 由近及远依次打击");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Indulgence.getTarget -= OnGetTarget;
            Hook_Indulgence.hitTarget -= OnHitTarget;
            System.Console.WriteLine("[IndulgenceWallPierce] 模组已卸载");
        }

        // ------------------------------------------------------------------
        // getTarget 钩子：完全替换原版目标选择逻辑
        //   1) 全图：不再检查视口/屏幕范围
        //   2) 穿墙：不再做视线/墙体（fastSpots、Bresenham 等）检测
        //   3) 由近及远：返回距离最近的合法敌人
        // ------------------------------------------------------------------
        private dc.en.Mob OnGetTarget(Hook_Indulgence.orig_getTarget orig, Indulgence self)
        {
            try
            {
                dc.en.Mob result = FindNearestTarget(self);
                if (result == null)
                {
                    LogTargetStats(self);
                }
                return result;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[IndulgenceWallPierce] getTarget 异常，回退原版: " + ex.Message);
            }
            return orig(self);
        }

        // ------------------------------------------------------------------
        // hitTarget 钩子：命中后把 nbExecute 动态扩展为“剩余合法敌人数量”，
        // 使技能链一直延续到打完全图敌人（原版上限 5 次 / 受 PureHeart 重置）。
        // ------------------------------------------------------------------
        private void OnHitTarget(Hook_Indulgence.orig_hitTarget orig, Indulgence self)
        {
            try
            {
                int remaining = CountValidTargets(self);
                if (remaining > self.nbExecute)
                {
                    self.nbExecute = remaining;
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[IndulgenceWallPierce] hitTarget 前置扩展失败: " + ex.Message);
            }

            orig(self);

            try
            {
                if (!self.destroyed && self.nbExecute > 0)
                {
                    int remaining = CountValidTargets(self);
                    if (remaining > self.nbExecute)
                    {
                        self.nbExecute = remaining;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[IndulgenceWallPierce] hitTarget 后置扩展失败: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // 工具方法
        // ------------------------------------------------------------------

        /// <summary>遍历当前关卡所有合法目标（未命中过、存活、可被攻击的敌人）。</summary>
        private static IEnumerable<Mob> EnumerateValidTargets(Indulgence self)
        {
            Entity owner = self.owner;
            if (owner == null || owner._level == null || owner._level.entities == null)
            {
                yield break;
            }

            ArrayObj entities = owner._level.entities;
            int len = entities.length;
            for (int i = 0; i < len; i++)
            {
                object item;
                try { item = entities.getDyn(i); }
                catch { break; } // 数组被替换等极端情况：安全退出
                if (item is Mob mob && IsValidTarget(self, mob))
                {
                    yield return mob;
                }
            }
        }

        /// <summary>与“敌人”相关的合法性过滤（保留原版过滤，去掉视口与墙体限制）。</summary>
        private static bool IsValidTarget(Indulgence self, Mob mob)
        {
            if (mob == null || mob.destroyed || mob.life <= 0)
            {
                return false;
            }
            if (!mob._targetable)
            {
                return false;
            }
            Entity owner = self.owner;
            if (owner == null || !owner.isOpponent(mob))
            {
                return false;
            }
            if (!mob.canBeDetected())
            {
                return false;
            }
            if (!mob.canBeHitBy(owner))
            {
                return false;
            }
            // 本轮技能已经命中过的敌人不再选（原版 hitAgain 机制）
            if (mob.cd != null && mob.cd.fastCheck != null && mob.cd.fastCheck.exists(HitAgainCdKey))
            {
                return false;
            }
            return true;
        }

        /// <summary>返回距离主角最近的一个合法目标（全图、穿墙）。</summary>
        private static Mob FindNearestTarget(Indulgence self)
        {
            Entity owner = self.owner;
            if (owner == null)
            {
                return null;
            }

            double sub = 1.0 / 48.0;
            double heroX = owner.cx + owner.xr;
            double heroY = owner.cy + owner.yr - owner.hei * sub;

            Mob best = null;
            double bestDistSq = double.MaxValue;
            foreach (Mob mob in EnumerateValidTargets(self))
            {
                double dx = (mob.cx + mob.xr) - heroX;
                double dy = (mob.cy + mob.yr - mob.hei * sub) - heroY;
                double distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = mob;
                }
            }

            if (best != null)
            {
                double dist = System.Math.Sqrt(bestDistSq);
                System.Console.WriteLine($"[IndulgenceWallPierce] 锁定目标 cx={best.cx},cy={best.cy} 距离={dist:F1}格");
            }
            return best;
        }

        /// <summary>统计当前合法目标数量。</summary>
        private static int CountValidTargets(Indulgence self)
        {
            int count = 0;
            foreach (Mob _ in EnumerateValidTargets(self))
            {
                count++;
            }
            return count;
        }

        /// <summary>找不到目标时输出各过滤环节的计数，便于排查。</summary>
        private static void LogTargetStats(Indulgence self)
        {
            try
            {
                Entity owner = self.owner;
                if (owner == null || owner._level == null || owner._level.entities == null)
                {
                    System.Console.WriteLine("[IndulgenceWallPierce] 诊断: owner/_level/entities 为空");
                    return;
                }
                ArrayObj entities = owner._level.entities;
                int total = entities.length;
                int mobs = 0, alive = 0, targetable = 0, opponent = 0, detected = 0, hittable = 0, notHit = 0;
                for (int i = 0; i < total; i++)
                {
                    object item;
                    try { item = entities.getDyn(i); }
                    catch { break; }
                    if (item is not Mob mob) continue;
                    mobs++;
                    if (mob.destroyed || mob.life <= 0) continue;
                    alive++;
                    if (!mob._targetable) continue;
                    targetable++;
                    if (!owner.isOpponent(mob)) continue;
                    opponent++;
                    if (!mob.canBeDetected()) continue;
                    detected++;
                    if (!mob.canBeHitBy(owner)) continue;
                    hittable++;
                    if (mob.cd != null && mob.cd.fastCheck != null && mob.cd.fastCheck.exists(HitAgainCdKey)) continue;
                    notHit++;
                }
                System.Console.WriteLine(
                    $"[IndulgenceWallPierce] 诊断: 实体={total} Mob={mobs} 存活={alive} 可标记={targetable} " +
                    $"敌对={opponent} 可探测={detected} 可命中={hittable} 未命中过={notHit}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[IndulgenceWallPierce] 诊断异常: " + ex.Message);
            }
        }
    }
}
