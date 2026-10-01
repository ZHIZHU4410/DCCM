#nullable disable

using dc;
using dc.en;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;

using SysMath = System.Math;

namespace EvilSwordYan
{
    /// <summary>
    /// 诅咒之刃 → 「YAN / 炎之呼吸」强化模组
    /// =====================================
    /// 1) 数据：item / weapon 数据由 patch_evilsword_data.py 从参考资源合并而来
    ///    （名称 YAN、4 段 HUOA~D 火焰特效、攻击框 8×6 / 8×6 / 8×4 / 13.5×12.5）；
    /// 2) 攻速 2 倍：Hook Weapon.prepare 把基础攻速乘 2（只影响本武器）；
    /// 3) 攻击无视墙体：Hook Weapon.canHit，原版几何判定失败时改用
    ///    "只用攻击框尺寸 + 朝向偏移"的宽松矩形判定（不做任何墙体/视线检查）；
    /// 4) 每一击附带术式顺转「苍」的全部功能（**除伤害外**）：
    ///    吸附范围内敌人（越远拉得越狠）+ 青色领域 / 贯穿光带 / 命中火花；
    /// 5) 每次死亡：满血原地复活 + 伤害永久 ×2（只增强伤害）+ 3 秒无敌 + 游戏内播报。
    ///
    /// 参考实现：GojoLimitless（苍的吸附与特效）、DeathRevive（死亡复活/播报/无敌）、
    ///           InfiniteRangeWallPierce（Weapon.canHit 挂点）。
    /// </summary>
    public class EvilSwordYanMain : ModBase, IOnGameExit, IOnAfterLoadingAssets, IOnHeroUpdate, IOnGameInit
    {
        /// <summary>本体武器 id。</summary>
        private const string WeaponId = "EvilSword";

        // ===== 数值 =====
        private const double SPEED_MUL = 2.0;              // 攻速倍率（3 倍基础上减慢 1 倍 → 2 倍）
        private const double PULL_RADIUS_TILES = 50.0;     // 「苍」吸附半径（格）：10 格增大 500% → ×5
        private const double PULL_FX_RADIUS_MAX = 25.0;    // 领域特效的显示半径上限（只影响观感，不影响吸附）
        private const double PULL_POWER = 0.42;            // 吸附力度（每击）
        private const double PULL_MIN_INTERVAL = 0.07;     // 两次吸附之间的最短间隔（秒）
        private const double DMG_MUL_PER_DEATH = 2.0;      // 每次死亡伤害 ×2
        private const double MAX_DMG_MUL = 64.0;           // 伤害倍率上限
        private const double SHIELD_SECONDS = 3.0;         // 死亡后无敌时长（秒）
        private const int SHIELD_AFFECT = 28;              // 28 = 原版 Global Shield（可见护盾气泡）

        // ===== 状态 =====
        private int _deathCount;                            // 本局死亡次数
        private double _dmgMult = 1.0;                      // 当前伤害倍率（初始 ×1）
        private double _pullCooldown;                       // 吸附节流计时

        public EvilSwordYanMain(ModInfo info) : base(info) { }

        #region 生命周期

        public override void Initialize()
        {
            base.Initialize();

            try { Hook_Weapon.prepare += OnWeaponPrepare; }
            catch (Exception ex) { Logger.Error(ex, "[EvilSwordYan] Hook_Weapon.prepare 挂载失败"); }

            try { Hook_Weapon.canHit += OnWeaponCanHit; }
            catch (Exception ex) { Logger.Error(ex, "[EvilSwordYan] Hook_Weapon.canHit 挂载失败"); }

            try { Hook_EvilSword.onExecute += OnEvilSwordExecute; }
            catch (Exception ex) { Logger.Error(ex, "[EvilSwordYan] Hook_EvilSword.onExecute 挂载失败"); }

            try { Hook_Hero.applyAttackResult += OnHeroApplyAttackResult; }
            catch (Exception ex) { Logger.Error(ex, "[EvilSwordYan] Hook_Hero.applyAttackResult 挂载失败"); }

            try
            {
                Hook_Hero.init += OnHeroInit;
                Hook_Hero.checkContinueMode += OnCheckContinueMode;
                Hook_Hero.tryToPreventDeath += OnTryToPreventDeath;
                Hook_Hero.kill += OnHeroKill;
            }
            catch (Exception ex) { Logger.Error(ex, "[EvilSwordYan] 死亡复活 Hook 挂载失败"); }

            Logger.Information("[EvilSwordYan] 已加载：攻速 2 倍 / 无视墙体 / 每击附带「苍」吸附 / 死亡复活+伤害翻倍+3秒无敌");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Weapon.prepare -= OnWeaponPrepare;
            Hook_Weapon.canHit -= OnWeaponCanHit;
            Hook_EvilSword.onExecute -= OnEvilSwordExecute;
            Hook_Hero.applyAttackResult -= OnHeroApplyAttackResult;
            Hook_Hero.init -= OnHeroInit;
            Hook_Hero.checkContinueMode -= OnCheckContinueMode;
            Hook_Hero.tryToPreventDeath -= OnTryToPreventDeath;
            Hook_Hero.kill -= OnHeroKill;
            Logger.Information("[EvilSwordYan] 已卸载");
        }

        /// <summary>
        /// 资源加载完成：加载 mod 自带的 res.pak（武器数据补丁 + fxWeapon 图集）。
        /// ⚠️ 必须实现 IOnAfterLoadingAssets 接口，否则这个方法永远不会被调用 ——
        /// 数据补丁不加载，游戏里就完全看不到改动（踩过的坑）。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(EvilSwordYanMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(YanFx.Hs(pakPath));
                    Logger.Information($"[EvilSwordYan] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[EvilSwordYan] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[EvilSwordYan] res.pak 加载失败");
            }
        }

        /// <summary>开局自检：把当前生效的 EvilSword weapon 数据打进日志（确认数据补丁真的合进来了）。</summary>
        void IOnGameInit.OnGameInit()
        {
            try
            {
                dynamic all = Data.Class.weapon.all;
                int len = (int)all.length;
                for (int i = 0; i < len; i++)
                {
                    dynamic row = all.getDyn(i);
                    if (row == null) continue;
                    string item = row.item?.ToString() ?? "";
                    if (!item.Contains(WeaponId)) continue;

                    dynamic sc = row.strikeChain;
                    int segs = sc == null ? 0 : (int)sc.length;
                    var fx = new System.Text.StringBuilder();
                    for (int k = 0; k < segs; k++)
                    {
                        dynamic seg = sc.getDyn(k);
                        if (k > 0) fx.Append(", ");
                        fx.Append(seg?.fxId?.ToString() ?? "?");
                    }
                    Logger.Information($"[EvilSwordYan] 数据自检: weapon[{i}] item={item} 段数={segs} fxId=[{fx}]");
                    return;
                }
                Logger.Warning("[EvilSwordYan] 数据自检: 没找到 EvilSword 的 weapon 数据");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[EvilSwordYan] 数据自检失败: {ex.Message}");
            }
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (_pullCooldown > 0.0) _pullCooldown -= dt;
        }

        #endregion

        #region 武器判定

        /// <summary>这把武器是不是本模组的诅咒之刃（类型判定为主，物品 id 兜底）。</summary>
        private static bool IsYan(Weapon w)
        {
            if (w == null) return false;
            try
            {
                if (w is EvilSword) return true;
                string id = w.wInfos?.item?.ToString() ?? "";
                return id.Contains(WeaponId);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsYanAttack(AttackData a)
        {
            if (a == null) return false;
            try
            {
                if (a.sourceWeapon is EvilSword) return true;
                if (a.sourceItem == null) return false;
                dynamic data = a.sourceItem._itemData;
                string id = data?.id?.ToString() ?? "";
                return id.Contains(WeaponId);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 2) 攻速 2 倍

        private void OnWeaponPrepare(Hook_Weapon.orig_prepare orig, Weapon self, double attackSpeed)
        {
            if (IsYan(self)) attackSpeed *= SPEED_MUL;
            orig(self, attackSpeed);
        }

        #endregion

        #region 3) 攻击无视墙体

        private bool OnWeaponCanHit(Hook_Weapon.orig_canHit orig, Weapon self, Entity e, Area area)
        {
            bool hit = orig(self, e, area);
            if (hit || !IsYan(self)) return hit;

            // 原版因墙体/视线挡住 → 用忽略墙体的攻击框判定补一次
            return YanFx.InAttackAreaIgnoringWalls(self, e, area);
        }

        #endregion

        #region 4) 每一击附带「苍」（吸附 + 特效，不含伤害）

        private bool OnEvilSwordExecute(Hook_EvilSword.orig_onExecute orig, EvilSword self)
        {
            TryCastBlue(self);
            return orig(self);
        }

        /// <summary>
        /// 挥击瞬间发动「苍」：把范围内的敌人往自己身上拽（不造成伤害），
        /// 敌人被拉近后正好吃下这一刀的 hitFrame。
        /// </summary>
        private void TryCastBlue(EvilSword self)
        {
            if (self == null || _pullCooldown > 0.0) return;

            try
            {
                Hero hero = self.owner;
                if (hero == null || hero.destroyed || hero.life <= 0) return;

                // 特效：以英雄为圆心的青色领域 + 朝向前方的贯穿光带
                // （显示半径设上限，避免 50 格吸附时全屏泛蓝挡视野；吸附范围仍是 PULL_RADIUS_TILES）
                if (YanFx.CenterPx(hero, out double hx, out double hy))
                {
                    double dir = YanFx.FacingDir(hero);
                    double fxR = SysMath.Min(PULL_RADIUS_TILES, PULL_FX_RADIUS_MAX);
                    YanFx.BlueField(hero, hx, hy, fxR);
                    YanFx.BeamLine(hero, hx, hy, fxR * YanFx.TILE_PX, dir);
                }

                int pulled = YanFx.PullEnemies(hero, PULL_RADIUS_TILES, PULL_POWER);
                if (pulled > 0) _pullCooldown = PULL_MIN_INTERVAL;
            }
            catch (Exception ex)
            {
                Logger.Information($"[EvilSwordYan] 「苍」发动失败: {ex.Message}");
            }
        }

        #endregion

        #region 5) 死亡复活 + 每死一次伤害增强 + 播报 + 3 秒无敌

        /// <summary>新游戏（新建 Hero）时重置死亡计数与伤害倍率。</summary>
        private void OnHeroInit(Hook_Hero.orig_init orig, Hero self)
        {
            orig(self);
            _deathCount = 0;
            _dmgMult = 1.0;
            Logger.Information("[EvilSwordYan] 新游戏开始，死亡计数与伤害倍率已重置（×1）");
        }

        /// <summary>
        /// 禁用原版"辅助模式续关"流程：否则死亡会被续关界面接管（返回 true），
        /// 我们的复活钩子就被跳过。
        /// </summary>
        private bool OnCheckContinueMode(Hook_Hero.orig_checkContinueMode orig, Hero self, AttackData a)
        {
            return false;
        }

        /// <summary>致命伤害：原版判定为必死时，改为满血复活并返回 true 阻止死亡。</summary>
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

        /// <summary>直接 kill() 的死亡路径（诅咒之刃被击中、死神斩杀等）：跳过死亡直接复活。</summary>
        private void OnHeroKill(Hook_Hero.orig_kill orig, Hero self)
        {
            Revive(self);
        }

        /// <summary>
        /// 满血复活：生命回满 + 清除死亡标记 + 死亡计数 +1 + 伤害倍率翻倍（只增强伤害）
        /// + 3 秒保护罩（affect 28）+ 游戏内播报。
        /// </summary>
        private void Revive(Hero self)
        {
            try
            {
                self.fullHeal();
                self.onDieDone = false;
                _deathCount++;

                if (_dmgMult < MAX_DMG_MUL)
                {
                    _dmgMult = SysMath.Min(MAX_DMG_MUL, _dmgMult * DMG_MUL_PER_DEATH);
                }

                // 3 秒无敌（原版 Global Shield：攻击被格挡 + 可见护盾气泡）
                try
                {
                    double v = 0.0;
                    self.setAffectS(SHIELD_AFFECT, SHIELD_SECONDS, ref v, null);
                }
                catch (Exception ex)
                {
                    Logger.Information($"[EvilSwordYan] 无敌施加失败: {ex.Message}");
                }

                // 游戏内播报（纯中文，避免字体里没有的符号：× / → 等）
                try
                {
                    if (self._level?.game?.log != null)
                    {
                        string msg = $"炎之呼吸 第 {_deathCount} 次死亡，伤害提升至 {(int)_dmgMult} 倍，获得 {SHIELD_SECONDS:0} 秒无敌";
                        self._level.game.log.text(YanFx.Hs(msg), null, null, null);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Information($"[EvilSwordYan] 播报失败: {ex.Message}");
                }

                Logger.Information($"[EvilSwordYan] 死亡已阻止：满血复活（第 {_deathCount} 次，伤害提升至 {(int)_dmgMult} 倍，无敌 {SHIELD_SECONDS:0} 秒）");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[EvilSwordYan] 复活失败");
            }
        }

        /// <summary>伤害增强：诅咒之刃造成的伤害按当前倍率放大（只增强伤害，不改攻速/范围）。</summary>
        private void OnHeroApplyAttackResult(Hook_Hero.orig_applyAttackResult orig, Hero self, AttackData attack)
        {
            if (_dmgMult > 1.0 && IsYanAttack(attack))
            {
                try { attack.dmgMultiplier *= _dmgMult; } catch { }
            }
            orig(self, attack);
        }

        #endregion
    }
}
