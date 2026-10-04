using dc;
using dc.en;
using dc.hxd.fs;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero;
using dc.tool.weap;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;

namespace KatanaBoost
{
    /// <summary>
    /// Katana（武士刀）强化模组 —— 平砍 / 斩击时无敌 + 伤害 ×2
    /// ======================================================
    /// 【代码部分】= 上次提交到 git 的「只有平砍和斩击时无敌」版本（commit 9524948 的 KatanaMain.cs），
    ///   原样保留其全部逻辑，不做任何居合化改写：
    ///     · Hook_HeroWeaponsManager.onWeaponUse —— Katana 每次出手（平砍三段 + 蓄力冲刺斩）时
    ///       激活 0.5s 无敌帧：_invincibleTimer = 0.5 且 hero.setAffectS(48, 0.5, ...)（48 = 无敌）
    ///     · Hook_Entity.applyAttackResult / Hook_Hero.applyAttackResult —— 无敌帧期间直接 return，
    ///       不把伤害结算交给原版（兜底）
    ///     · IOnHeroUpdate —— 无敌帧倒计时，并 removeAllAffects(8) 清除眩晕（8 = stun）
    ///
    /// 【数值部分】由 res.pak 数据补丁实现（patch_katana_data.py 生成），
    ///   目标是一级武器介绍页显示「187 (287) 伤害/每秒」：
    ///     power 45/55/65/88 -> 63/77/91/124，第 4 段（唯一 canCrit 段）critMul 1 -> 1.27
    ///   其余字段（charge / coolDown / lockCtrlAfter / canCrit / range / 特效）全部保持原版。
    ///
    ///   介绍页那一行的公式（反推自 InventItem.getBaseWeaponDPS，第 32930 行）：
    ///     圆括号前 = round( Σ power / Σ(charge + lockCtrlAfter) )
    ///     圆括号内 = round( Σ power × (canCrit ? 2×critMul : 1) / Σ(charge + lockCtrlAfter) )
    ///   该公式已用原版武器对日文wiki实测值交叉验证（GiantKiller 127(465)、Rapier 156(356) 等全部吻合）。
    ///
    /// 【Assets 与 cs 代码的配合方式】仿 DamageAuraBoost：
    ///   patch_katana_data.py -> data.cdb（csproj 同级）-> csproj 的 BuildResPak 目标用
    ///   DCCMTool cdb diff + pak unpack 生成 Assets/data.cdb_/weapon/Katana.json
    ///   -> PackAssets 打包成 res.pak -> dotnet build 自动安装到 coremod/mods/KatanaBoost/。
    ///   运行期由本类的 IOnAfterLoadingAssets 把 res.pak 挂进 FsPak，CDBManager 合并数据补丁。
    /// </summary>
    public class KatanaBoostMain : ModBase, IOnHeroUpdate, IOnGameExit, IOnAfterLoadingAssets
    {
        public KatanaBoostMain(ModInfo info) : base(info) { }

        // ---------- 无敌帧相关 ----------
        private double _invincibleTimer = 0.0;
        // 无敌帧持续时间：覆盖 Katana 攻击动画
        private const double INVINCIBLE_DURATION = 0.5;

        /// <summary>同时写控制台与模组日志文件（logs\log_latest.log）。</summary>
        internal void Write(string msg)
        {
            global::System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        public override void Initialize()
        {
            base.Initialize();

            // 钩子：检测 Katana 武器使用
            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUseHook;
            // 钩子：阻止无敌帧期间的伤害
            Hook_Entity.applyAttackResult += Hook_Entity_applyAttackResult;
            Hook_Hero.applyAttackResult += Hook_Hero_applyAttackResult;

            Write("[KatanaBoost] 已加载: 平砍/斩击无敌帧(0.5s) + 武士刀伤害 x2(res.pak 数据补丁)");
        }

        /// <summary>
        /// 武器使用时触发。检测是否为 Katana 攻击（平砍三段 + 蓄力冲刺斩均触发），
        /// 激活短暂无敌帧。
        /// </summary>
        private void OnWeaponUseHook(Hook_HeroWeaponsManager.orig_onWeaponUse orig, HeroWeaponsManager self, Weapon w, int slot)
        {
            orig(self, w, slot);

            if (self.hero == null) return;

            // 通过类型判断是否为 Katana 武器
            bool isKatana = w is dc.tool.weap.Katana;
            // 备选：通过物品 ID 判断
            if (!isKatana && w?.item?._itemData?.id != null)
            {
                isKatana = w.item._itemData.id.ToString() == "Katana";
            }

            if (!isKatana) return;

            Hero hero = self.hero;

            // 平砍 + 蓄力冲刺均激活无敌帧
            _invincibleTimer = INVINCIBLE_DURATION;

            double ignore = 0;
            var ignoreRef = new Ref<double>(ref ignore);
            // affectS id 48 = 无敌
            hero.setAffectS(48, INVINCIBLE_DURATION, ignoreRef, null);
        }

        /// <summary>
        /// 实体受到攻击结果时触发。若玩家处于无敌帧中，阻止伤害应用。
        /// </summary>
        private void Hook_Entity_applyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            // 判断受击者是否为玩家英雄
            Hero? targetHero = self as Hero;
            if (targetHero == null && attack?.lastHitTarget is Hero hitHero)
                targetHero = hitHero;

            if (targetHero != null && _invincibleTimer > 0)
            {
                // 无敌帧中，不应用伤害
                return;
            }

            orig(self, attack);
        }

        /// <summary>
        /// 英雄受到攻击结果时触发。若处于无敌帧中，阻止伤害应用。
        /// </summary>
        private void Hook_Hero_applyAttackResult(Hook_Hero.orig_applyAttackResult orig, Hero self, AttackData attack)
        {
            if (self != null && _invincibleTimer > 0)
                return;
            orig(self, attack);
        }

        // ---------- 资源加载 ----------
        /// <summary>
        /// 资源加载完成后：把本模组 res.pak（含 data.cdb_ 补丁：weapon/Katana 伤害 ×2）挂载进 FsPak，
        /// 游戏的 CDBManager 会在首次关卡生成/重载资源时合并 data.cdb_ 补丁。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(KatanaBoostMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Write($"[KatanaBoost] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Write($"[KatanaBoost] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Write($"[KatanaBoost] res.pak 加载失败: {ex.Message}");
            }
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 无敌帧倒计时
            if (_invincibleTimer > 0)
            {
                _invincibleTimer -= dt;
                if (_invincibleTimer < 0) _invincibleTimer = 0;

                // 免疫眩晕：清除 stun affect（ID 8）
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero != null && hero.life > 0)
                {
                    hero.removeAllAffects(8);
                }
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUseHook;
            Hook_Entity.applyAttackResult -= Hook_Entity_applyAttackResult;
            Hook_Hero.applyAttackResult -= Hook_Hero_applyAttackResult;
            Write("[KatanaBoost] 游戏退出，资源清理完成");
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
