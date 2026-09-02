using dc;
using dc.en;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.Runtime.CompilerServices;

namespace DeathRevive
{
    /// <summary>
    /// 死亡复活 + 复活强化模组
    ///
    /// 1. 死亡后满血复活（原地复活，不出死亡画面）：
    ///    - 正常死亡（生命归零）→ tryToPreventDeath 判定"必死" → 满血复活；
    ///    - 诅咒致死（被诅咒状态下受到攻击）→ 同样走 tryToPreventDeath（诅咒时跳过原版死亡保护）→ 满血复活；
    ///    - 致命坠落 → Fall 伤害 → tryToPreventDeath → 满血复活；
    ///    - 诅咒武器（EvilSword）命中 / 恶魔活力到期 / 死神 90% 斩杀 → 直接调用 kill() → 满血复活。
    ///    - 关键：原版"辅助模式-无限续关"（continueEnabled）会在 tryToPreventDeath 里走
    ///      checkContinueMode 续关流程（弹出续关界面并判定为"已处理"，返回 true），
    ///      导致复活钩子被跳过 —— 因此本模组额外 Hook checkContinueMode 恒返回 false，
    ///      把"续关"替换成本模组的原地满血复活。
    ///    保留原版 tryToPreventDeath 的剧情分支（女王抓取、训练场/BossRush、一击保护、YOLO 等）。
    ///
    /// 2. 每次复活触发武器强化（在上一基础上翻一倍，初始为原版 ×1）：
    ///    - 第 1 次复活 → 攻速 ×2、范围 ×2；
    ///    - 第 2 次复活 → ×4；第 3 次 → ×8 …… 上限 MAX_BUFF_MULT（默认 ×64，可改）。
    ///    攻速：Hook Weapon.prepare，把基础攻速乘以当前倍率（不会破坏武器自身攻速加成）。
    ///    范围：Hook HeroWeaponsManager.onWeaponUse，把攻击 Area 的位置与尺寸乘以当前倍率
    ///    （记录每个 Area 的原始值，避免重复缩放；武器销毁后自动释放）。
    ///    倍率跨关卡保留，新游戏（新建 Hero）时重置回 ×1。
    ///
    /// 3. 每次复活游戏内 HUD 播报"复活了 X 次！攻速/范围 ×N"（与 YOLO"Mort évitée !"同款通道）。
    ///
    /// 4. 角色体型 +1%：每帧把 sprScaleX/sprScaleY 保持为 1.01（跨关卡不丢失）。
    /// </summary>
    public class DeathReviveMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        private const float SIZE_MULT = 1.01f;        // 角色体型倍率（+1%）

        /// <summary>复活后保护罩（无敌）持续时间（秒）。</summary>
        private const double SHIELD_SECONDS = 3.0;

        /// <summary>攻速/范围叠加的安全上限（防止复活次数过多导致倍率溢出、动画崩溃）。想无限翻倍改大或删掉判断。</summary>
        private const double MAX_BUFF_MULT = 64.0;

        /// <summary>本局累计复活次数（新游戏/新建 Hero 时重置，跨关卡保留）。</summary>
        private int _reviveCount;

        /// <summary>当前武器攻速/范围倍率，初始 ×1（原版），每次复活翻一倍。</summary>
        private double _buffMult = 1.0;

        // ===== 攻击范围缩放 =====
        // 记录每个 Area 的原始位置/尺寸（弱引用表，武器销毁后自动释放），
        // 每次挥动前按当前倍率重算，避免对已缩放的值重复放大。
        private readonly ConditionalWeakTable<object, AreaBase> _areaBase = new();

        private sealed class AreaBase
        {
            public readonly double X, Y, WidPx, HeiPx;

            public AreaBase(double x, double y, double widPx, double heiPx)
            {
                X = x; Y = y; WidPx = widPx; HeiPx = heiPx;
            }
        }

        public DeathReviveMain(ModInfo info) : base(info) { }

        #region 生命周期

        public override void Initialize()
        {
            base.Initialize();
            // 死亡复活
            Hook_Hero.init += OnHeroInit;
            Hook_Hero.checkContinueMode += OnCheckContinueMode;
            Hook_Hero.tryToPreventDeath += OnTryToPreventDeath;
            Hook_Hero.kill += OnHeroKill;
            // 复活强化：武器攻速/范围随复活次数翻倍
            Hook_Weapon.prepare += OnWeaponPrepare;
            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUseHook;
            Logger.Information("[DeathRevive] 已加载：死亡满血复活 / 每次复活攻速范围翻倍（初始×1）/ 体型+1%");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.init -= OnHeroInit;
            Hook_Hero.checkContinueMode -= OnCheckContinueMode;
            Hook_Hero.tryToPreventDeath -= OnTryToPreventDeath;
            Hook_Hero.kill -= OnHeroKill;
            Hook_Weapon.prepare -= OnWeaponPrepare;
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUseHook;
            Logger.Information("[DeathRevive] 已卸载");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null) return;

            // 体型 +1%：每帧从持久化倍率恢复，跨关卡不丢失
            if (System.Math.Abs(hero.sprScaleX - SIZE_MULT) > 0.0001f ||
                System.Math.Abs(hero.sprScaleY - SIZE_MULT) > 0.0001f)
            {
                hero.sprScaleX = SIZE_MULT;
                hero.sprScaleY = SIZE_MULT;
            }
        }

        #endregion

        #region 死亡 → 满血复活

        /// <summary>新游戏（新建 Hero）时重置复活计数与强化倍率；过关复用 Hero 不会触发这里。</summary>
        private void OnHeroInit(Hook_Hero.orig_init orig, Hero self)
        {
            orig(self);
            _reviveCount = 0;
            _buffMult = 1.0;
            Logger.Information("[DeathRevive] 新游戏开始，复活计数与强化倍率已重置（攻速/范围 ×1）");
        }

        /// <summary>
        /// 禁用原版"辅助模式续关"流程（checkLifeRemaining/续关界面）。
        /// 原版续关流程会在 tryToPreventDeath 中把死亡标记为"已处理"（返回 true），
        /// 使复活钩子被跳过；这里恒返回 false，让所有死亡统一落到本模组的满血复活。
        /// </summary>
        private bool OnCheckContinueMode(Hook_Hero.orig_checkContinueMode orig, Hero self, AttackData a)
        {
            return false;
        }

        /// <summary>
        /// 致命伤害钩子：正常死亡、诅咒致死、致命坠落都会在生命归零时调用本方法。
        /// 先调用原版逻辑（保留女王抓取剧情、训练场/BossRush 原地复活、一击保护、YOLO 等分支），
        /// 仅当原版判定为"必死"（返回 false）或英雄生命值已归零时，直接满血复活并返回 true 阻止死亡。
        /// </summary>
        private bool OnTryToPreventDeath(Hook_Hero.orig_tryToPreventDeath orig, Hero self, AttackData a, double prevLife)
        {
            bool prevented = orig(self, a, prevLife);
            if (!prevented || self.life <= 0)
            {
                Revive(self);
                return true;
            }
            return prevented;
        }

        /// <summary>
        /// 直接调用 kill() 的死亡路径（诅咒武器 EvilSword 命中、恶魔活力到期、死神 90% 斩杀等）：
        /// 直接满血复活，不调用 orig，跳过真正的死亡流程。
        /// </summary>
        private void OnHeroKill(Hook_Hero.orig_kill orig, Hero self)
        {
            Revive(self);
        }

        /// <summary>
        /// 满血复活：生命回满，清理死亡标记，累计复活次数并翻倍强化倍率，
        /// 套上 3 秒保护罩（affect 28 = 游戏自带的 Global Shield：攻击被格挡为 0 伤害 + 可见护盾气泡），
        /// 最后游戏内播报。
        /// </summary>
        private void Revive(Hero self)
        {
            self.fullHeal();
            self.onDieDone = false;
            _reviveCount++;

            // 攻速/范围在上一基础上翻一倍（×1 → ×2 → ×4 → ×8 …，到上限后封顶）
            if (_buffMult < MAX_BUFF_MULT)
            {
                _buffMult = System.Math.Min(MAX_BUFF_MULT, _buffMult * 2.0);
            }

            // 3 秒保护罩（无敌）：affect 28 = Global Shield，攻击全部 Block（0 伤害），自带护盾气泡特效
            try
            {
                double shieldVal = 0.0;
                self.setAffectS(28, SHIELD_SECONDS, ref shieldVal, null);
            }
            catch (Exception ex)
            {
                Logger.Information($"[DeathRevive] 保护罩施加失败: {ex.Message}");
            }

            // 游戏内播报（HUD 日志，与 YOLO"Mort évitée !"同款通道）
            try
            {
                if (self._level?.game?.log != null)
                {
                    string msg = "复活了 " + _reviveCount + " 次！攻速/范围 ×" + (int)_buffMult + "，获得 " + (int)SHIELD_SECONDS + " 秒保护罩";
                    self._level.game.log.text(msg.AsHaxeString(), null, null, null);
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[DeathRevive] 播报失败: {ex.Message}");
            }

            Logger.Information($"[DeathRevive] 死亡被阻止，已满血复活（第 {_reviveCount} 次，攻速/范围 ×{(int)_buffMult}，保护罩 {(int)SHIELD_SECONDS} 秒）");
        }

        #endregion

        #region 复活强化：武器攻速/范围翻倍

        /// <summary>每次攻击准备时把基础攻速乘以当前倍率（未复活时为 ×1，即原版）。</summary>
        private void OnWeaponPrepare(Hook_Weapon.orig_prepare orig, Weapon self, double attackSpeed)
        {
            orig(self, attackSpeed * _buffMult);
        }

        /// <summary>每次挥动武器前，把该武器的攻击 Area 按当前倍率放大（未复活时为 ×1，即原版）。</summary>
        private void OnWeaponUseHook(Hook_HeroWeaponsManager.orig_onWeaponUse orig, HeroWeaponsManager self, Weapon w, int slot)
        {
            try
            {
                ScaleWeaponAreas(w, _buffMult);
            }
            catch (Exception ex)
            {
                Logger.Information($"[DeathRevive] 攻击范围缩放失败: {ex.Message}");
            }

            orig(self, w, slot);
        }

        private void ScaleWeaponAreas(Weapon? w, double mult)
        {
            if (w == null) return;

            dynamic areas = w.areas;
            if (areas == null) return;

            int len = (int)areas.length;
            for (int i = 0; i < len; i++)
            {
                dynamic a = areas.getDyn(i);
                if (a == null) continue;

                object key = (object)a;
                if (!_areaBase.TryGetValue(key, out AreaBase? baseArea))
                {
                    baseArea = new AreaBase((double)a.x, (double)a.y, (double)a.widPx, (double)a.heiPx);
                    _areaBase.Add(key, baseArea);
                }

                a.x = baseArea.X * mult;
                a.y = baseArea.Y * mult;
                a.widPx = baseArea.WidPx * mult;
                a.heiPx = baseArea.HeiPx * mult;
            }
        }

        #endregion
    }
}
