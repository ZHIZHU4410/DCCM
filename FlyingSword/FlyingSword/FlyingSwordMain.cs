using dc;
using dc.en;
using dc.en.pet;
using dc.tool;
using dc.tool.skill;
using dc.tool.weap;
using Hashlink.Virtuals;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using HaxeProxy.Runtime.Internals.Cache;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;

namespace FlyingSword
{
    /// <summary>
    /// 飞剑增强 — 攻击相关全部 ×100：
    /// - 攻击蓄力/冷却强制接近 0（chargeMaxF=0.01、cooldownF=0）→ 攻击频率和冲刺 ×100
    /// - 攻击动画 genSpeed = 100；AI 永不锁定（连续攻击）
    /// - 近战形态武器 NotFlyingSword 攻速 ×100
    /// - 索敌范围 ×100；无视墙体索敌
    /// - 替换武器（overrideEquipedWeapon）时中断飞剑打击
    /// 待机飘行速度保持原版（不动 move.speed）。
    /// </summary>
    public class FlyingSwordMain : ModBase, IOnGameExit, IOnHeroUpdate, IOnGameInit
    {
        private const double MULT = 100.0;
        private const string ITEM_ID = "FlyingSword";
        private const double BASE_RANGE = 12.0;   // 原版索敌范围

        private bool _applied = false;

        public FlyingSwordMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_FlyingSword.updateAttack += OnUpdateAttack;
            Hook_FlyingSword.initSkill += OnInitSkill;
            Hook_FlyingSword.aiLocked += OnAiLocked;
            Hook_FlyingSword.fixedUpdate += OnFlyingSwordFixedUpdate;
            Hook_FlyingSword.initTarget += OnInitTarget;
            Hook_FlyingSword.overrideEquipedWeapon += OnOverrideEquipedWeapon;
            System.Console.WriteLine("[FlyingSword] 飞剑增强已加载 — 索敌范围×100，攻击速度×100（待机速度不变）");
        }

        void IOnGameInit.OnGameInit()
        {
            Apply();
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (!_applied) Apply();

            // 近战形态（NotFlyingSword）攻速也 ×100
            try
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                var wm = hero?.weaponsManager;
                if (wm?.mainWeapons != null)
                {
                    for (int i = 0; i < wm.mainWeapons.length; i++)
                    {
                        object? raw = wm.mainWeapons.array[i];
                        if (raw == null) continue;
                        try
                        {
                            if (raw is dc.tool.weap.NotFlyingSword nfs)
                            {
                                nfs._attackSpeed = MULT;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void Apply()
        {
            if (_applied) return;

            try
            {
                dynamic itemData = Data.Class.item.byId.get(ToHaxeString(ITEM_ID));
                if (itemData?.props == null)
                {
                    System.Console.WriteLine($"[FlyingSword] 未找到物品 {ITEM_ID}，稍后重试");
                    return;
                }

                itemData.props.range = BASE_RANGE * MULT;

                _applied = true;
                System.Console.WriteLine($"[FlyingSword] 已应用：索敌范围 {itemData.props.range}（×{MULT}）");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[FlyingSword] 应用失败: {ex.Message}");
            }
        }

        private bool OnAiLocked(Hook_FlyingSword.orig_aiLocked orig, dc.en.pet.FlyingSword self)
        {
            return false;
        }

        private void OnOverrideEquipedWeapon(Hook_FlyingSword.orig_overrideEquipedWeapon orig, dc.en.pet.FlyingSword self, bool withFeedbacks)
        {
            orig(self, withFeedbacks);

            try
            {
                InterruptAttacks(self);
            }
            catch { }
        }

        private void OnInitTarget(Hook_FlyingSword.orig_initTarget orig, dc.en.pet.FlyingSword self)
        {
            orig(self);

            if (self.target != null && !self.target.destroyed && self.target.life > 0) return;

            try
            {
                Entity? parent = self.parent;
                if (parent == null || parent._team == null) return;

                double rangeSq = (BASE_RANGE * MULT) * (BASE_RANGE * MULT);
                Entity? best = null;
                double bestDist = rangeSq + 1.0;

                var iter = parent._team.opponentsIterator.reset(parent._team);
                while (iter.hasNext())
                {
                    Entity e = iter.next();
                    if (e == null || e.destroyed || e.life <= 0 || !e.canBeHit()) continue;

                    double dx = ((double)parent.cx + parent.xr) - ((double)e.cx + e.xr);
                    double dy = ((double)parent.cy + parent.yr) - ((double)e.cy + e.yr);
                    double dist = dx * dx + dy * dy;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = e;
                    }
                }

                if (best != null)
                {
                    self.target = best;
                }
            }
            catch { }
        }

        private void OnInitSkill(
            Hook_FlyingSword.orig_initSkill orig,
            dc.en.pet.FlyingSword self,
            virtual_animId_animSpd_area_breachBonus_canCrit_charge_coolDown_critMul_dynamicCharge_earlyCombo_fxId_fxProps_glowColor_hitFrame_lockCtrlAfter_onionSkinFrame_onionSkinOffX_power_props_sfxCharge_sfxHit_sfxProps_sfxRelease_ indexStrike,
            int attack)
        {
            orig(self, indexStrike, attack);
            try { SpeedUpAttacks(self); } catch { }
        }

        private void OnUpdateAttack(Hook_FlyingSword.orig_updateAttack orig, dc.en.pet.FlyingSword self)
        {
            orig(self);

            try
            {
                if (self.spr != null && self.spr._animManager != null)
                {
                    self.spr._animManager.genSpeed = MULT;
                }

                // 每帧强制攻击蓄力/冷却接近 0
                SpeedUpAttacks(self);
            }
            catch { }
        }

        private void OnFlyingSwordFixedUpdate(Hook_FlyingSword.orig_fixedUpdate orig, dc.en.pet.FlyingSword self)
        {
            orig(self);

            try
            {
                if (self.item != null && self.item._itemData != null)
                {
                    dynamic itemData = self.item._itemData;
                    itemData.props.range = BASE_RANGE * MULT;
                }
            }
            catch { }
        }

        /// <summary>
        /// 把飞剑攻击技能全部设为：蓄力上限 0.01、冷却 0 → 攻击每帧触发、冲刺瞬间完成。
        /// </summary>
        private static void SpeedUpAttacks(dc.en.pet.FlyingSword self)
        {
            if (self.attackList == null) return;

            int len = self.attackList.length;
            for (int i = 0; i < len; i++)
            {
                object? raw = self.attackList.array[i];
                if (raw == null) continue;

                OldSkill skill;
                try { skill = (OldSkill)raw; }
                catch { continue; }
                if (skill == null) continue;

                skill.chargeMaxF = 0.01;
                skill.coolDownF = 0.0;
                skill.coolDownMaxF = 0.0;
            }
        }

        private static void InterruptAttacks(dc.en.pet.FlyingSword self)
        {
            if (self.attackList == null) return;

            int len = self.attackList.length;
            for (int i = 0; i < len; i++)
            {
                object? raw = self.attackList.array[i];
                if (raw == null) continue;
                try
                {
                    ((OldSkill)raw).interrupt();
                }
                catch { }
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_FlyingSword.updateAttack -= OnUpdateAttack;
            Hook_FlyingSword.initSkill -= OnInitSkill;
            Hook_FlyingSword.aiLocked -= OnAiLocked;
            Hook_FlyingSword.fixedUpdate -= OnFlyingSwordFixedUpdate;
            Hook_FlyingSword.initTarget -= OnInitTarget;
            Hook_FlyingSword.overrideEquipedWeapon -= OnOverrideEquipedWeapon;
            System.Console.WriteLine("[FlyingSword] 游戏退出，模组已卸载");
        }
    }
}
