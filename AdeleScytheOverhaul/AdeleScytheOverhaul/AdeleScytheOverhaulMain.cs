#nullable disable

using dc;
using dc.en;
using dc.hl.types;
using dc.libs.heaps.slib;
using dc.tool;
using dc.tool.weap;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;

// 武器每一段连击的定义（Haxe 匿名结构）。名字太长，起个别名，只在 dynOnAttackAnim 钩子里用。
using WeaponSkillInf = Hashlink.Virtuals.virtual_animId_animSpd_area_breachBonus_canCrit_charge_coolDown_critMul_dynamicCharge_earlyCombo_fxId_fxProps_glowColor_hitFrame_lockCtrlAfter_onionSkinFrame_onionSkinOffX_power_props_sfxCharge_sfxHit_sfxProps_sfxRelease_;

namespace AdeleScytheOverhaul
{
    /// <summary>
    /// 死亡镰刀（AdeleScythe / Death's Scythe）强化模组
    /// =================================================
    /// 需求对应实现：
    ///   1) **只保留最后一击**
    ///      → `Hook_Weapon.set_cycle`：AdeleScythe 的连击序号永远钉在 `strikeChain.Length - 1`
    ///        （= 3，也就是第 4 段 / 最后一击）。这正是原版词条 `LastCycleSpam` 的语义
    ///        （GamePseudocode/dc.tool/Weapon.cs:243 `set_cycle` 里 `if (item.hasAffix("LastCycleSpam")) return _cycle = skills.length - 1;`），
    ///        所以"最后一击"自身的一整套行为（前冲、震屏、追加判定、刀光 FXAtkReaperToolD）全部保持原版。
    ///
    ///   2) **范围 ×3**、3) **伤害 ×3**、攻速相关的追加判定间隔 ÷3
    ///      → 数据补丁 `Assets/data.cdb_/weapon/AdeleScythe.json`（由 patch_adele_data.py 生成）：
    ///          strikeChain[3].power[0]         80   → 240
    ///          strikeChain[3].area.width       8.5  → 25.5   （圆：半径 102px → 306px）
    ///          strikeChain[3].area.height      6.5  → 19.5
    ///          strikeChain[3].area.offsetCaseX -3.7 → -11.1
    ///          strikeChain[3].area.offsetCaseY -1   → -3
    ///          strikeChain[3].props.tick       6    → 2
    ///        运行时的开关（`AdeleWeaponData.Sync`）在"强化值 ↔ 原版值"之间来回切，所以菜单里能当场还原。
    ///
    ///   4) **攻速 ×3**
    ///      → `Hook_Weapon.prepare` 把 attackSpeed 乘 3（= 原版 `WeaponSkill.getCastSpeed()`
    ///        里的 `weapon.get_attackSpeed()`，也就是游戏自己的攻速倍率，见 KillAttackSpeed）。
    ///
    ///   5) **无视墙体**
    ///      → `Hook_Weapon.canHit` + `Hook_AdeleScythe.checkAreahit` 换成 `AdeleHitBox.Test`：
    ///        只做"存活校验 + 攻击框几何"，**不碰**原版那段墙体/视线/门的判定。
    ///        范围仍然由被放大 ×3 的攻击框决定（不是"全图必中"）。
    ///
    /// 【总开关】仿 DamageAuraBoost 的 AuraFeatures：
    ///   总开关一关 → 数值还原成原版 + 连击恢复原版 4 段 + 不再无视墙体 + 攻速恢复原版；
    ///   开关状态存在 coremod/config/AdeleScytheOverhaul.json，
    ///   可在 游戏内「选项 → 模组 → 死亡镰刀强化」里切换，或配置热键（默认不绑键）。
    /// </summary>
    public class AdeleScytheOverhaulMain : ModBase, IOnGameExit, IOnAfterLoadingAssets, IOnHeroUpdate, IOnGameInit, IModMenu
    {
        /// <summary>本模组只认这一把武器（item 表里的 id）。</summary>
        public const string ItemId = "AdeleScythe";

        /// <summary>dynOnAttackAnim 的钩子要留着引用才能 -= 注销，所以存成字段。</summary>
        private Hook_AdeleScythe.hook_dynOnAttackAnim _fxScaleHook;

        /// <summary>异常日志限流用的计数器。</summary>
        private int _errorCount;

        public AdeleScytheOverhaulMain(ModInfo info) : base(info) { }

        /// <summary>同时写控制台与模组日志文件（logs\log_latest.log）。</summary>
        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        // ---------------------------------------------------------------- 生命周期

        public override void Initialize()
        {
            base.Initialize();

            try { Hook_Weapon.set_cycle += OnWeaponSetCycle; }
            catch (Exception ex) { Logger.Error(ex, "[AdeleScytheOverhaul] Hook_Weapon.set_cycle 挂载失败"); }

            try { Hook_Weapon.prepare += OnWeaponPrepare; }
            catch (Exception ex) { Logger.Error(ex, "[AdeleScytheOverhaul] Hook_Weapon.prepare 挂载失败"); }

            try { Hook_Weapon.canHit += OnWeaponCanHit; }
            catch (Exception ex) { Logger.Error(ex, "[AdeleScytheOverhaul] Hook_Weapon.canHit 挂载失败"); }

            try { Hook_AdeleScythe.checkAreahit += OnCheckAreaHit; }
            catch (Exception ex) { Logger.Error(ex, "[AdeleScytheOverhaul] Hook_AdeleScythe.checkAreahit 挂载失败"); }

            try
            {
                _fxScaleHook = OnAdeleDynOnAttackAnim;
                Hook_AdeleScythe.dynOnAttackAnim += _fxScaleHook;
            }
            catch (Exception ex) { Logger.Error(ex, "[AdeleScytheOverhaul] Hook_AdeleScythe.dynOnAttackAnim 挂载失败"); }

            // 刀光放大的兜底路径（按 FX id 认领，不依赖 AdeleScythe 的内部字段）
            try { Hook_Fx.playWeaponFx += OnPlayWeaponFx; }
            catch (Exception ex) { Logger.Error(ex, "[AdeleScytheOverhaul] Hook_Fx.playWeaponFx 挂载失败"); }

            Write("[AdeleScytheOverhaul] 已加载：只保留最后一击 + 范围×3 + 伤害×3 + 攻速×3 + 无视墙体");
        }

        /// <summary>资源加载完成：手动加载 mod 自带的 res.pak（数据补丁）。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(AdeleScytheOverhaulMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Write($"[AdeleScytheOverhaul] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[AdeleScytheOverhaul] 未找到 res.pak: {pakPath}（数值补丁不会生效）");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[AdeleScytheOverhaul] res.pak 加载失败");
            }

            // 记录一下已写入状态（仅日志用；数值同步本身每次挥刀都会无条件重写，是自愈的）
            AdeleWeaponData.ResetWrittenState();
        }

        /// <summary>开局自检：写下当前开关状态，并把默认配置落盘（第一次运行时生成可编辑的 json）。</summary>
        void IOnGameInit.OnGameInit()
        {
            try
            {
                var cfg = AdeleKeys.Config.Value;
                Write("[AdeleScytheOverhaul] 开关状态 —— " +
                      $"总开关={cfg.EnableMod} 只保留最后一击={cfg.EnableLastHit} 范围×3={cfg.EnableRange} " +
                      $"伤害×3={cfg.EnableDamage} 攻速×3={cfg.EnableAttackSpeed} 无视墙体={cfg.EnableWallPierce} " +
                      $"刀光×3={cfg.EnableFxScale}");
                Write($"[AdeleScytheOverhaul] 配置文件: {AdeleKeys.Config.ConfigPath}" +
                      "（也可在游戏的 选项 → 模组 → 死亡镰刀强化 菜单里直接改）");

                try { if (AdeleKeys.Config.Value != null) AdeleKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[AdeleScytheOverhaul] 写配置失败: {ex.Message}");
            }
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 热键切换（默认一个都没绑；只在"刚按下"那一帧动作）
            try { AdeleFeatures.PollHotkeys(Write); } catch { }

            // 刀光要逐帧维持（frameData 每帧都在变，位置补偿得用当帧数据重算）
            MaintainTrackedFx();
        }

        /// <summary>
        /// 逐帧重算被追踪刀光的缩放与位置。
        ///
        /// 为什么要逐帧：<see cref="AdeleFxAnchor.Apply"/> 需要当帧的 <c>frameData</c>
        /// （realX/realY/realWid/realHei/wid/hei 每帧都不一样），只在 FX 出生时算一次的话
        /// 动画自身的位移会被"冻住"，看着像贴纸。
        /// 精灵播放完（killAfterPlay）后 <c>destroyed</c> 为 true，这里顺手清掉引用。
        /// </summary>
        private void MaintainTrackedFx()
        {
            HSprite fx = _trackedFx;
            if (fx == null) return;

            try
            {
                if (fx.destroyed || ++_trackedFxFrames > TrackedFxMaxFrames)
                {
                    _trackedFx = null;
                    return;
                }
                if (!AdeleFeatures.IsOn(AdeleFeature.FxScale)) return;

                var cfg = AdeleKeys.Config.Value;
                AdeleFxAnchor.Apply(fx, cfg.FxScale, cfg.FxOffsetX, cfg.FxOffsetY);
                LogFxDiagOnce(fx, cfg);
            }
            catch { _trackedFx = null; }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Weapon.set_cycle -= OnWeaponSetCycle; } catch { }
            try { Hook_Weapon.prepare -= OnWeaponPrepare; } catch { }
            try { Hook_Weapon.canHit -= OnWeaponCanHit; } catch { }
            try { Hook_AdeleScythe.checkAreahit -= OnCheckAreaHit; } catch { }
            try { if (_fxScaleHook != null) Hook_AdeleScythe.dynOnAttackAnim -= _fxScaleHook; } catch { }
            try { Hook_Fx.playWeaponFx -= OnPlayWeaponFx; } catch { }

            AdeleFeatures.ResetHotkeyState();
            AdeleWeaponData.ResetWrittenState();
            _fxScaleLogged = false;
            _fxNullLogged = false;
            _fxDiagLogged = false;
            _trackedFx = null;
            _trackedFxFrames = 0;
            Write("[AdeleScytheOverhaul] 游戏退出，模组已卸载");
        }

        // ---------------------------------------------------------------- 功能 1：只保留最后一击

        /// <summary>
        /// 把连击序号钉在最后一段。
        ///
        /// 原版 `Weapon.set_cycle` 只在物品带 `LastCycleSpam` 词条时才做这件事；
        /// 这里对 AdeleScythe 直接做同样的事（`_cycle = skills.length - 1`），
        /// 因此 `isLastCycle()` 恒为 true：
        ///   · `incrementCycle()` 里 `set_cycle(0)` 会被我们重新顶回 3 → 连击永远停在第 4 段；
        ///   · `AdeleScythe.onExecute` / `hitFromWeapon` / `dynOnAttackAnim` 里那些硬编码的
        ///     `get_cycle() == 3` 分支会**一直**命中 → 前冲、震屏、追判、刀光全都在（这是关键：
        ///     如果改成"只留 1 段 strikeChain"，cycle 会恒为 0，这些分支反而全都不触发）。
        /// </summary>
        private int OnWeaponSetCycle(Hook_Weapon.orig_set_cycle orig, Weapon self, int v)
        {
            if (self is AdeleScythe && AdeleFeatures.IsOn(AdeleFeature.LastHit))
            {
                try
                {
                    int last = LastCycleIndexOf(self);
                    if (last >= 0 && v != last) return orig(self, last);
                }
                catch (Exception ex) { LogThrottled("set_cycle", ex); }
            }
            return orig(self, v);
        }

        /// <summary>strikeChain 的段数 - 1（= 最后一段的下标）；取不到返回 -1。</summary>
        private static int LastCycleIndexOf(Weapon w)
        {
            ArrayObj chain = w?.wInfos?.strikeChain;
            if (chain == null) return -1;
            int n = chain.length;
            return n > 0 ? n - 1 : -1;
        }

        // ---------------------------------------------------------------- 功能 2/3/4：数值同步 + 攻速 ×3

        /// <summary>
        /// 每次挥刀前：
        ///   1) 把 CDB 记录（<c>wInfos.strikeChain[3]</c>）与运行时攻击框 <c>areas[3]</c>
        ///      同步成当前开关对应的值 —— 所以菜单里一改，**下一次挥刀**就生效；
        ///   2) 把"攻击后摇"写进运行中的技能对象（原版只在武器构造时从 CDB 抄一次，
        ///      不直接改技能对象的话就得重新装备武器才生效）；
        ///   3) 攻速 ×3（= 原版 <c>WeaponSkill.getCastSpeed() = base.getCastSpeed() * weapon.get_attackSpeed()</c>，
        ///      也就是游戏自己的攻速倍率，不影响武器基础攻速以外的任何天赋加成算法）。
        /// </summary>
        private void OnWeaponPrepare(Hook_Weapon.orig_prepare orig, Weapon self, double attackSpeed)
        {
            if (self is AdeleScythe scythe)
            {
                try
                {
                    var cfg = AdeleKeys.Config.Value;

                    bool fastTick = AdeleFeatures.IsOn(AdeleFeature.AttackSpeed);
                    double speed = fastTick ? Clamp(cfg.SpeedMult, 1.0, 20.0) : 1.0;

                    AdeleWeaponData.Sync(scythe,
                                         AdeleFeatures.IsOn(AdeleFeature.Range),
                                         AdeleFeatures.IsOn(AdeleFeature.Damage),
                                         fastTick,
                                         AdeleFeatures.IsOn(AdeleFeature.NoRecovery),
                                         speed);

                    if (fastTick) attackSpeed *= speed;
                }
                catch (Exception ex) { LogThrottled("prepare", ex); }
            }

            orig(self, attackSpeed);
        }

        // ---------------------------------------------------------------- 功能 5：无视墙体

        /// <summary>
        /// 主判定（`AdeleScythe.onExecute` 里对每个敌人调的 `canHit(entity, null)`）：
        /// 换成"只有几何、没有墙"的 <see cref="AdeleHitBox.Test"/>。
        /// 只在 <paramref name="area"/> 为 null（= 用当前连击段的攻击框）时接管，
        /// 外部显式传攻击框的调用一律走原版。
        /// </summary>
        private bool OnWeaponCanHit(Hook_Weapon.orig_canHit orig, Weapon self, Entity e, Area area)
        {
            if (area == null && self is AdeleScythe && AdeleFeatures.IsOn(AdeleFeature.WallPierce))
            {
                int cycle = 0;
                try { cycle = self.get_cycle(); } catch { }
                return AdeleHitBox.Test(self, e, cycle);
            }
            return orig(self, e, area);
        }

        /// <summary>
        /// 最后一击的追加判定（原版 `AdeleScythe.checkAreahit`，由 Delayer 在 props.tick 帧后回调）。
        /// 原版这里有一整份"几何 + 墙体 + 视线 + 门"的拷贝，我们只保留几何部分，
        /// 通过后照原版一样调 `hitFromWeapon(e, previousCycle)` —— 伤害/词条/特效全部走原版链路。
        /// </summary>
        private void OnCheckAreaHit(Hook_AdeleScythe.orig_checkAreahit orig, AdeleScythe self, Entity e, int previousCycle)
        {
            if (!AdeleFeatures.IsOn(AdeleFeature.WallPierce))
            {
                orig(self, e, previousCycle);
                return;
            }

            try
            {
                // 原版开头的合法性校验，逐条照抄（GamePseudocode/dc.tool.weap/AdeleScythe.cs:448）
                if (self.destroyed || self.owner == null || self.owner.destroyed) return;
                if (self.owner.isUnconscious()) return;
                dc.pr.Game game = dc.pr.Game.Class.ME;
                if (game != null && game.hasCinematic()) return;

                // 几何通过就打（不再问墙）
                if (!AdeleHitBox.Test(self, e, previousCycle)) return;

                int cycle = previousCycle;
                self.hitFromWeapon(e, ref cycle);
            }
            catch (Exception ex) { LogThrottled("checkAreahit", ex); }
        }

        // ---------------------------------------------------------------- 可选：刀光特效 ×3（纯视觉）

        /// <summary>当前正在被逐帧重算的刀光精灵（两条 FX 路径都会往这里登记）。</summary>
        private HSprite _trackedFx;

        /// <summary>追踪帧数；超过上限就撒手（防止精灵没被 destroy 时一直写它）。</summary>
        private int _trackedFxFrames;

        /// <summary>追踪上限：60fps 下约 10 秒，远大于刀光寿命。</summary>
        private const int TrackedFxMaxFrames = 600;

        /// <summary>刀光放大成功的日志只打一条，避免每次挥刀刷屏。</summary>
        private bool _fxScaleLogged;

        /// <summary>lastFx 拿不到时的日志也只打一条。</summary>
        private bool _fxNullLogged;

        /// <summary>刀光锚点自检日志也只打一条。</summary>
        private bool _fxDiagLogged;

        /// <summary>最后一击在 CDB 里没有 fxId，名字是原版在 <c>AdeleScythe.dynOnAttackAnim</c> 里硬编码的。</summary>
        private const string LastHitFxNamePart = "AtkReaperToolD";

        /// <summary>
        /// 把刀光精灵登记下来（缩放/位置的实际设置在 <see cref="MaintainTrackedFx"/> 里逐帧做）。
        /// </summary>
        private void TrackFx(HSprite spr)
        {
            if (spr == null) return;

            if (!ReferenceEquals(_trackedFx, spr)) _trackedFxFrames = 0;
            _trackedFx = spr;
            _fxNullLogged = false;

            if (!_fxScaleLogged)
            {
                _fxScaleLogged = true;
                Write("[AdeleScytheOverhaul] 已接管最后一击刀光：按倍率放大，并逐帧把可见中心拉回人物身上");
            }
        }

        /// <summary>
        /// 刀光锚点自检日志（一局只打一条）。
        ///
        /// 打出**运行时**的 frameData 与算出来的补偿量，用来核对：
        ///   · 运行时读到的 wid/hei/realX/realY/realWid/realHei 是否与
        ///     res/atlas/fxWeapon.atlas 里的实测值一致（不一致说明我对格式的理解有偏差）；
        ///   · pivot 的 centerFactor 是否真是 0.5（不是的话缩放基准就不同）。
        /// 拿这条日志就能把"还差多少像素"精确算出来，不用再靠肉眼估。
        /// </summary>
        private void LogFxDiagOnce(HSprite fx, AdeleConfig cfg)
        {
            if (_fxDiagLogged) return;
            _fxDiagLogged = true;

            try
            {
                FrameData f = fx.frameData;
                SpritePivot p = fx.pivot;
                if (f == null) { Write("[AdeleScytheOverhaul] 刀光自检: frameData 为空"); return; }

                double cfx = p != null ? p.centerFactorX : 0.0;
                double cfy = p != null ? p.centerFactorY : 0.0;
                bool usingFactor = p != null && p.usingFactor;

                double bx = f.wid / 2.0 - f.realX - f.realWid * (usingFactor ? cfx : 0.0);
                double by = f.hei / 2.0 - f.realY - f.realHei * (usingFactor ? cfy : 0.0);

                Write("[AdeleScytheOverhaul] 刀光锚点自检 —— " +
                      $"frame={fx.frame} wid={f.wid} hei={f.hei} realX={f.realX} realY={f.realY} " +
                      $"realWid={f.realWid} realHei={f.realHei} usingFactor={usingFactor} " +
                      $"centerFactor=({cfx:0.###},{cfy:0.###}) " +
                      $"包围盒中心=({bx:0.0},{by:0.0}) 补偿后 sprite=({fx.x:0.0},{fx.y:0.0}) " +
                      $"倍率={cfg.FxScale:0.##} 偏移=({cfg.FxOffsetX:0.#},{cfg.FxOffsetY:0.#})");
            }
            catch (Exception ex) { Write($"[AdeleScytheOverhaul] 刀光自检失败: {ex.Message}"); }
        }

        /// <summary>
        /// 兜底路径：直接在 FX 工厂的出口按 id 认领最后一击的刀光。
        ///
        /// 与 <see cref="OnAdeleDynOnAttackAnim"/> 那条路**互相独立**：两条路登记的是同一个精灵，
        /// 真正的缩放/位移统一由 <see cref="AdeleFxAnchor.Apply"/> 逐帧写绝对值（不会双重放大）。
        /// 万一 <c>AdeleScythe.dynOnAttackAnim</c> 的钩子没被触发，这条仍然兜得住。
        /// </summary>
        private HSprite OnPlayWeaponFx(Hook_Fx.orig_playWeaponFx orig, Fx self, Entity e, dc.String id,
                                       Ref<double> speed, ArrayBytes_Int offset,
                                       int? innerColorOverride, int? outerColorOverride)
        {
            HSprite spr = orig(self, e, id, speed, offset, innerColorOverride, outerColorOverride);

            if (spr == null || !AdeleFeatures.IsOn(AdeleFeature.FxScale)) return spr;

            try
            {
                // dc.String 没有 C# 的隐式转换，用 ToString() 取纯文本
                //（本仓库 ForgeQualityForge.Plain() 用的就是这条）
                string fxId = id?.ToString();
                if (string.IsNullOrEmpty(fxId) || !fxId.Contains(LastHitFxNamePart)) return spr;

                TrackFx(spr);
                MaintainTrackedFx();
            }
            catch { /* 纯视觉，失败就算了 */ }

            return spr;
        }

        /// <summary>
        /// 最后一击的刀光放大。
        ///
        /// 为什么钩这里：`AdeleScythe` 最后一击（cycle 3）在数据里**没有 fxId**，
        /// 所以 `dynOnFxFrame`（那个逐帧 `playWeaponAnim` 的分支）对它完全不干活；
        /// 它唯一的特效就是 `dynOnAttackAnim` 里为 cycle==3 播的
        /// `fx.playWeaponFx(owner, "FXAtkReaperToolD", ...)`，结果存在 `base.lastFx`。
        ///
        /// 这里只负责"认出这个精灵"；缩放与位置补偿交给
        /// <see cref="AdeleFxAnchor.Apply"/>（逐帧、用当帧 frameData 重算），
        /// 所以放大后特效的构图和原版一致，人物仍然在特效的同一相对位置上。
        /// </summary>
        private void OnAdeleDynOnAttackAnim(Hook_AdeleScythe.orig_dynOnAttackAnim orig,
                                            AdeleScythe self, WeaponSkill s, WeaponSkillInf cinf)
        {
            orig(self, s, cinf);

            if (!AdeleFeatures.IsOn(AdeleFeature.FxScale)) return;

            try
            {
                // 只接管最后一击的刀光（连击序号被"只保留最后一击"钉在 3，所以基本每次都命中）
                if (self.get_cycle() != AdeleWeaponData.LastCycle) return;

                HSprite fx = self.lastFx;
                if (fx == null)
                {
                    if (!_fxNullLogged)
                    {
                        _fxNullLogged = true;
                        Write("[AdeleScytheOverhaul] 刀光放大：lastFx 为空（原版这次没写出最后一击的刀光），已跳过");
                    }
                    return;
                }

                TrackFx(fx);
                MaintainTrackedFx();
            }
            catch { /* 纯视觉，失败就算了 */ }
        }

        // ---------------------------------------------------------------- 选项菜单（IModMenu）
        //
        // 与 DamageAuraBoost 一致：游戏的「选项 → 模组 → 死亡镰刀强化」这一页不是自动生成的，
        // 而是在 BuildMenu() 里自己 addToggleWidget 建出来（开关值写回配置并落盘）。

        /// <summary>这一页在「选项 → 模组」里的名字。</summary>
        public string GetName() => "死亡镰刀强化";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("ADELESCYTHE OVERHAUL 设置"));
                b.createScroller(0.0);

                // 每个功能一个复选框（总开关排最前），用 AdeleFeatures.All 循环生成
                foreach (var f in AdeleFeatures.All)
                {
                    var feature = f;                       // 闭包捕获：别直接用循环变量
                    bool on = AdeleFeatures.RawGet(feature);
                    string key = AdeleFeatures.KeyName(feature);
                    string hint = AdeleFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(AdeleFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !AdeleFeatures.RawGet(feature);
                            AdeleFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                b.updateScroller();

                // ---- 刀光的三条滑条（写法照抄 ZoomVision / LowHpAlert 的 addSliderWidget）----
                // 顺序说明：addSliderWidget(标题, 回调, 当前值, step, 父Flow,
                //                            showPercent, showRawValue, min, max, button=null, paddingLeft)
                var cfg = AdeleKeys.Config.Value;

                b.addSliderWidget(
                    StringUtils.AsHaxeString("攻速倍率"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.SpeedMult = v;
                        try { AdeleKeys.Config.Save(); } catch { }
                    },
                    cfg.SpeedMult,
                    Ref<double>.In(0.5),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(1.0),
                    Ref<double>.In(20.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("刀光倍率"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.FxScale = v;
                        try { AdeleKeys.Config.Save(); } catch { }
                    },
                    cfg.FxScale,
                    Ref<double>.In(0.1),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.5),
                    Ref<double>.In(8.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("刀光前后偏移(px)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.FxOffsetX = v;
                        try { AdeleKeys.Config.Save(); } catch { }
                    },
                    cfg.FxOffsetX,
                    Ref<double>.In(5.0),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(-400.0),
                    Ref<double>.In(400.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("刀光上下偏移(px)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.FxOffsetY = v;
                        try { AdeleKeys.Config.Save(); } catch { }
                    },
                    cfg.FxOffsetY,
                    Ref<double>.In(5.0),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(-400.0),
                    Ref<double>.In(400.0),
                    null,
                    Ref<int>.In(0));

                b.updateScroller();
                Write($"[AdeleScytheOverhaul] 选项菜单已建立：{AdeleFeatures.All.Length} 个功能开关 + 4 条滑条");
            }
            catch (Exception ex)
            {
                Write($"[AdeleScytheOverhaul] 建立选项菜单失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>异常日志限流：同一个钩子最多刷 5 条，避免每帧刷屏。</summary>
        private void LogThrottled(string where, Exception ex)
        {
            if (_errorCount >= 5) return;
            _errorCount++;
            Write($"[AdeleScytheOverhaul] {where} 出错（第 {_errorCount} 次，最多记 5 次）: {ex.Message}");
        }

        /// <summary>把滑条值夹到合法区间（滑条本身也有区间，这里再兜一层，防止手改配置写成负数/天文数字）。</summary>
        private static double Clamp(double v, double min, double max)
        {
            if (double.IsNaN(v)) return min;
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
