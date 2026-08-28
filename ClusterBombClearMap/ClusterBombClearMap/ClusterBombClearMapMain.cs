using dc;
using dc.en;
using dc.en.gr;
using dc.hl.types;
using dc.tool.atk;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using System;
using System.Collections.Generic;

namespace ClusterBombClearMap
{
    /// <summary>
    /// ClusterBomb 强化模组：
    /// - 释放 ClusterBomb（扔出大手雷、撞击爆炸、分裂成小手雷）的任意阶段，
    ///   都会直接清空当前关卡全部敌人；
    /// - 清图方式参考 IndulgenceWallPierce：遍历关卡实体，找出所有合法敌人，
    ///   以“无限大”爆炸伤害（AttackUtils 攻击管线 + 巨大基础伤害）逐个命中，
    ///   因此击杀计数、细胞、掉落、死亡流程等全部走原版管线。
    /// </summary>
    public class ClusterBombClearMapMain : ModBase, IOnGameExit
    {
        // “无限大”爆炸伤害：1e9 基础伤害，远超任何敌人血量；
        // 再关闭按等级缩放（sourceTier=1 / useHeroScaling=false），
        // 避免 computeFinalDmg 里 (int) 强转溢出成负数。
        private const double INF_DMG = 1000000000.0;

        public ClusterBombClearMapMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            // 大手雷：释放（创建）与撞击爆炸两个时机都清图
            Hook_ClusterBomb.init += OnClusterBombInit;
            Hook_ClusterBomb.onTrigger += OnClusterBombTrigger;
            // 小手雷：爆炸/触发时再次清图（此时敌人通常已死，仅作兜底）
            Hook_ClusterBombSub.onTrigger += OnClusterBombSubTrigger;
            Hook_ClusterBombSub.onExplode += OnClusterBombSubExplode;
            System.Console.WriteLine("[ClusterBombClearMap] ClusterBomb 已强化：释放后直接清图（爆炸伤害无限大）");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_ClusterBomb.init -= OnClusterBombInit;
            Hook_ClusterBomb.onTrigger -= OnClusterBombTrigger;
            Hook_ClusterBombSub.onTrigger -= OnClusterBombSubTrigger;
            Hook_ClusterBombSub.onExplode -= OnClusterBombSubExplode;
            System.Console.WriteLine("[ClusterBombClearMap] 模组已卸载");
        }

        // ------------------------------------------------------------------
        // Hook 处理器
        // ------------------------------------------------------------------

        private void OnClusterBombInit(Hook_ClusterBomb.orig_init orig, ClusterBomb self)
        {
            orig(self);
            ClearMap(self);
        }

        private void OnClusterBombTrigger(Hook_ClusterBomb.orig_onTrigger orig, ClusterBomb self)
        {
            orig(self);
            ClearMap(self);
        }

        private void OnClusterBombSubTrigger(Hook_ClusterBombSub.orig_onTrigger orig, ClusterBombSub self)
        {
            orig(self);
            ClearMap(self);
        }

        private void OnClusterBombSubExplode(Hook_ClusterBombSub.orig_onExplode orig, ClusterBombSub self)
        {
            orig(self);
            ClearMap(self);
        }

        // ------------------------------------------------------------------
        // 清图核心：全图索敌（穿墙、无视距离） + 无限伤害命中
        // ------------------------------------------------------------------

        private static void ClearMap(GrenadeSkill grenade)
        {
            try
            {
                if (grenade == null || grenade.destroyed)
                {
                    return;
                }
                // 找到释放者（主角英雄），用于阵营判断与关卡定位
                Entity owner = grenade.parent;
                if (owner == null || owner._level == null || owner._level.entities == null)
                {
                    return;
                }

                ArrayObj entities = owner._level.entities;
                int len = entities.length;
                int killed = 0;
                for (int i = 0; i < len; i++)
                {
                    object item;
                    try { item = entities.getDyn(i); }
                    catch { break; } // 数组被替换等极端情况：安全退出

                    if (item is not Mob mob)
                    {
                        continue;
                    }
                    if (!IsValidTarget(owner, mob))
                    {
                        continue;
                    }

                    // 构造“无限大”伤害攻击并命中（完全走原版攻击管线）
                    try
                    {
                        AttackData atk = AttackUtils.Class.createFromHero.Invoke(owner, (dynamic)INF_DMG, null);
                        atk.useHeroScaling = false;
                        atk.sourceTier = 1;
                        AttackUtils.Class.hit.Invoke(atk, mob);
                        killed++;
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine("[ClusterBombClearMap] 命中异常: " + ex.Message);
                    }
                }

                if (killed > 0)
                {
                    System.Console.WriteLine($"[ClusterBombClearMap] 清图完成，命中敌人 {killed} 个");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ClusterBombClearMap] 清图异常: " + ex.Message);
            }
        }

        /// <summary>合法敌人过滤（与 IndulgenceWallPierce 一致：存活、可标记、敌对、可探测、可命中）。</summary>
        private static bool IsValidTarget(Entity owner, Mob mob)
        {
            if (mob == null || mob.destroyed || mob.life <= 0)
            {
                return false;
            }
            if (!mob._targetable)
            {
                return false;
            }
            if (!owner.isOpponent(mob))
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
            return true;
        }
    }
}
