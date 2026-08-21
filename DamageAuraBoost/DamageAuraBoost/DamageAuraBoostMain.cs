#nullable disable

using dc;
using dc.en;
using dc.pow;
using dc.tool.atk;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.IO;

namespace DamageAuraBoost
{
    /// <summary>
    /// DamageAura（撕裂光环）强化模组
    /// ==============================
    /// 【数值部分】由 res.pak 数据补丁实现（patch_aura_data.py 生成）：
    ///   范围 distance ×2、攻速 tick ÷4（dps ×4 抵消，单次伤害不变）、持续时间 ×4。
    ///   本代码不再修改任何数值，避免与数据补丁叠加。
    ///
    /// 【连击部分】本代码实现 —— 参考 CollectorSpin 的触发方式：
    ///   原版 P_DmgKill（连击）变异只在 Mob.onDirectHitFromHero（英雄直接命中）时触发，
    ///   而光环攻击带"远程"标记 tag 14 会被排除，所以原版光环击杀/命中不触发连击。
    ///   修复：光环命中 Mob 时移除 tag 14，使其成为"直接命中"：
    ///     - 装备了连击变异 → 走原版 onDirectHitFromHero 链路（UI + 叠层 + 伤害加成全原版）
    ///     - 未装备变异     → 复刻 Mob.cs 的 P_DmgKill 处理（叠 affect 132 + 右上角连击 UI）
    /// </summary>
    public class DamageAuraBoostMain : ModBase, IOnGameExit, IOnAfterLoadingAssets
    {
        /// <summary>P_DmgKill 变异使用的 affect 编号（连击层数）。</summary>
        private const int ComboAffectId = 132;

        /// <summary>光环攻击结算中标记（精确判定命中来自光环）。</summary>
        private bool _auraTicking;

        /// <summary>连击触发次数（日志采样用，避免刷屏）。</summary>
        private int _comboHits;

        public DamageAuraBoostMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            try { Hook_DamageAura.fixedUpdate += OnAuraFixedUpdate; }
            catch (Exception ex) { Logger.Error(ex, "[DamageAuraBoost] Hook_DamageAura.fixedUpdate 挂载失败"); }
            try { Hook_Entity.applyAttackResult += OnApplyAttackResult; }
            catch (Exception ex) { Logger.Error(ex, "[DamageAuraBoost] Hook_Entity.applyAttackResult 挂载失败"); }
            Logger.Information("[DamageAuraBoost] 已加载: 数值=res.pak 数据补丁, 光环命中触发连击(P_DmgKill)");
        }

        /// <summary>资源加载完成：手动加载 mod 自带的 res.pak（数据补丁）。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(DamageAuraBoostMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[DamageAuraBoost] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[DamageAuraBoost] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[DamageAuraBoost] res.pak 加载失败");
            }
        }

        /// <summary>光环结算期间置标记。</summary>
        private void OnAuraFixedUpdate(Hook_DamageAura.orig_fixedUpdate orig, DamageAura self)
        {
            bool prev = _auraTicking;
            _auraTicking = true;
            try
            {
                orig(self);
            }
            finally
            {
                _auraTicking = prev;
            }
        }

        /// <summary>
        /// 光环命中 Mob：
        /// 1) 移除远程标记 tag 14 → 攻击变为"英雄直接命中" → 原版 Mob.onDirectHitFromHero
        ///    被调用（与 CollectorSpin 一致），装备连击变异时原版链路完整生效（UI/叠层/伤害）。
        /// 2) 未装备变异时，复刻 Mob.cs 的 P_DmgKill 处理：叠层 + 刷新窗口 + 右上角连击 UI。
        /// </summary>
        private void OnApplyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            bool isAuraHit = _auraTicking
                             && self is Mob
                             && !self.destroyed
                             && self._level != null
                             && self._team != null
                             && self._team == self._level.teamMob;
            if (isAuraHit)
            {
                try { attack.setTag(14, false); } catch { }
            }
            orig(self, attack);

            if (isAuraHit && attack.source is Hero hero
                && hero.life > 0 && !hero.destroyed
                && !hero.inventory.hasItem(ToHaxeString("P_DmgKill")))
            {
                try
                {
                    TriggerDmgKillCombo(hero);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[DamageAuraBoost] 连击模拟失败");
                }
            }
        }

        /// <summary>复刻 Mob.cs onDirectHitFromHero 中 P_DmgKill 的处理：每次直接命中叠一层。</summary>
        private void TriggerDmgKillCombo(Hero hero)
        {
            dynamic itemData = Data.Class.item.byId.get(ToHaxeString("P_DmgKill"));
            if (itemData == null) return;

            double duration = (double)itemData.props.duration;   // 2.5s
            double one = 1.0;
            hero.setAffectS(ComboAffectId, duration, Ref<double>.From(ref one), null);
            hero.resetAllAffectToTime(ComboAffectId, duration);

            int count = hero.countAffect(ComboAffectId);
            double prct = (double)itemData.props.prct;
            double scaling = (double)itemData.commonProps.customScaling;
            double bonus = (prct + scaling * (double)hero.getRelevantPerkTier(ToHaxeString("P_DmgKill"))) * count;

            dynamic hud = hero._level?.game?.hud;
            if (hud != null && hud.comboCount != null)
            {
                if (count == 0)
                {
                    hud.comboCount.reset();
                }
                else
                {
                    hud.comboCount.setValue(count, 1.0 + bonus);
                }
            }

            _comboHits++;
            if (_comboHits % 10 == 1)
            {
                Logger.Information($"[DamageAuraBoost] 连击层数={count}, 加成={bonus:P1}, hud存在={hud != null}, comboCount存在={(hud != null && hud.comboCount != null)}");
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_DamageAura.fixedUpdate -= OnAuraFixedUpdate;
            Hook_Entity.applyAttackResult -= OnApplyAttackResult;
            Logger.Information("[DamageAuraBoost] 游戏退出，模组已卸载");
        }
    }
}
