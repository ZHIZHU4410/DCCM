using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using dc;
using dc.en;
using dc.en.inter;
using dc.tool;
using dc.tool.atk;
using dc.tool.weap;
using HaxeProxy.Runtime;
using Hashlink;
using Hashlink.Proxy.Objects;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;

namespace ChronoBlade
{
    /// <summary>
    /// 时之刃 (Chrono Blade) 主模块。
    ///
    /// 武器数据由 patch_chronoblade_cdb.py 生成的 data.cdb 提供（构建时 diff 进 res.pak）：
    ///   item   表 : id = "ChronoBlade"
    ///   weapon 表 : 3 段 strikeChain（均以 AtkKatanaA 起手，靠 set_cycle 区分招数）
    ///
    /// 本文件负责：
    ///   1) Hook tool.$Weapon.create，把 ChronoBlade 这个 item 映射到 ChronoBlade 类；
    ///   2) Hook Katana.hitFromWeapon —— 斩击命中敌人时刻罗马数字（该方法不可被 C# 重写）；
    ///   3) 加载 res.pak（含 data.cdb 补丁）；
    ///   4) 每帧推进特效；热键 P 打开"选择武器"面板、X 打开"选择弹药"面板；
    ///   5) 游戏退出时清理。
    /// </summary>
    public class ChronoBladeMain : ModBase, IOnGameExit, IOnAfterLoadingAssets, IOnHeroUpdate, IOnGameInit
    {
        public ChronoBladeMain(ModInfo info) : base(info) { }

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>本体武器 id。</summary>
        private const string WeapId = "ChronoBlade";

        // 所有热键都改成从 ChronoKeys.Config 读取（见 ChronoConfig.cs），不再硬编码。
        private bool _weaponPanelKeyWasDown;

        /// <summary>在主手/背包里找一把 Zaphkiel（十二之弹枪，用于手动换弹）。</summary>
        private TimeBullet? FindTimeBullet()
        {
            try
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                var wm = hero?.weaponsManager;
                if (wm == null) return null;

                var found = FindIn(wm.mainWeapons);
                return found ?? FindIn(wm.backpackWeapons);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 英雄**主手槽**里是否拿着 Zaphkiel（十二之弹枪）。
        /// 只算主手 —— 背包里的不算"手上"，身后的背景也就不会出现。
        /// </summary>
        private static bool HoldingZaphkiel()
        {
            try
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero == null || hero.destroyed) return false;
                return FindIn(hero.weaponsManager?.mainWeapons) != null;
            }
            catch
            {
                return false;
            }
        }

        private static TimeBullet? FindIn(dc.hl.types.ArrayObj? arr)
        {
            if (arr == null) return null;
            for (int i = 0; i < arr.length; i++)
            {
                if (arr.getDyn(i) is TimeBullet tb && !tb.destroyed) return tb;
            }
            return null;
        }

        /// <summary>武器名称 → 构造工厂（真实表在 ChronoWeaponFactory 里）。</summary>
        public static Dictionary<string, Func<Hero, InventItem, Weapon>> WeaponCreateMap
            => ChronoWeaponFactory.WeaponCreateMap;

        private HashlinkHooks.HookHandle? _createHook;
        private HashlinkHooks.HookHandle? _weaponExecHook;
        private HashlinkHooks.HookHandle? _damageHook;

        /// <summary>
        /// 伤害事件挂点候选（类名, 方法名）。hashlink 的类名带包路径，
        /// 不同版本写法不一，所以逐个试，第一个成功的就留下。
        /// </summary>
        private static readonly (string Type, string Func)[] DamageHookCandidates =
        {
            ("dc.en.Entity", "onDamage"),
            ("en.Entity", "onDamage"),
            ("Entity", "onDamage"),
            ("$Entity", "onDamage"),
            ("dc.Entity", "onDamage"),
        };

        public override void Initialize()
        {
            base.Initialize();

            // 第 1a 的刻印挂点：自动尝试多个候选类名，挂上哪个就记哪个。
            //
            // 踩坑记录：Hook_Katana.hitFromWeapon / Hook_Katana.tryHitDash 整局都不触发；
            // tool.atk.$AttackUtils 与 en.Entity 这两个我猜的类名都会 KeyNotFoundException。
            // 所以不再硬编码一个名字，改成依次尝试 + 明确日志。
            foreach (var (typeName, funcName) in DamageHookCandidates)
            {
                try
                {
                    _damageHook = HashlinkHooks.Instance.CreateHook(typeName, funcName, OnEntityDamage);
                    _damageHook.Enable();
                    Write($"[ChronoBlade] 已 Hook 刻印挂点: {typeName}.{funcName}");
                    break;
                }
                catch
                {
                    // 试下一个
                }
            }

            if (_damageHook == null)
            {
                Logger.Warning("[ChronoBlade] 所有伤害挂点候选都失败，刻印将只依赖备用路径");
            }

            try
            {
                Hook_Katana.hitFromWeapon += OnKatanaHitFromWeapon;
                Write("[ChronoBlade] 已 Hook Katana.hitFromWeapon（备用刻印路径）");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ChronoBlade] Hook Katana.hitFromWeapon 失败: {ex.Message}");
            }

            // 怪物死亡 → 在尸体位置播放 TIMEJIBAI 死亡特效
            try
            {
                Hook_Entity.onDie += OnEntityDie;
                Write("[ChronoBlade] 已 Hook Entity.onDie（死亡特效）");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ChronoBlade] Hook Entity.onDie 失败: {ex.Message}");
            }

            // ★ 核心挂点：tool.Weapon.onExecute
            //
            // 说明：原本照 Katana 项目挂 Hook_Katana.onExecute，但实测（日志里我方武器确认在手、
            // 攻击却没有任何钩子日志）说明这个挂点没被调用 —— 攻击实际是从基类 tool.Weapon.onExecute
            // 分派下去的。挂基类方法覆盖所有近战武器攻击，再按"手里的物品是不是时之刃"做过滤。
            try
            {
                _weaponExecHook = HashlinkHooks.Instance.CreateHook(
                    "tool.Weapon", "onExecute", OnAnyWeaponExecute);
                _weaponExecHook.Enable();
                Write("[ChronoBlade] 已 Hook tool.Weapon.onExecute（武器攻击总入口）");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ChronoBlade] Hook tool.Weapon.onExecute 失败");
            }

            // 备用挂点：Katana.onExecute（若上面那条不生效，这条兜住）
            try
            {
                Hook_Katana.onExecute += OnKatanaExecute;
                Write("[ChronoBlade] 已 Hook Katana.onExecute（备用）");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ChronoBlade] Hook Katana.onExecute 失败");
            }

            // 注册自定义武器构造
            ChronoWeaponFactory.Register();

            // Hook tool.$Weapon.create —— 委托类型必须与原函数签名完全一致，
            // 绝不能用 object，否则 HashlinkHookManager.CreateAdaptDelegate 会抛
            // MissingMethodException: Method 'System.Object.Invoke' not found 并崩游戏。
            try
            {
                _createHook = HashlinkHooks.Instance.CreateHook(
                    "tool.$Weapon", "create", ChronoWeaponFactory.Hook_create);
                _createHook.Enable();
                Write("[ChronoBlade] 已 Hook tool.$Weapon.create");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ChronoBlade] Hook tool.$Weapon.create 失败");
            }

            Logger.Information("[ChronoBlade] 时之刃已注册：1a 居合前冲 / 2a 一周飞镖 / 3a 时钟剑雨");
        }

        /// <summary>
        /// 同时写控制台与模组日志文件（logs\log_latest.log）。
        /// 只写 Console 的话日志文件里看不到，白排查 —— 这是上一轮踩的坑。
        /// </summary>
        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        /// <summary>
        /// ★ 挂 tool.Weapon.onExecute 的处理器：所有武器攻击都会进这里。
        /// 原函数原型：bool onExecute()（无参数），因此委托就是 bool orig(Weapon self)。
        /// </summary>
        private bool OnAnyWeaponExecute(ChronoWeaponExecute.orig_Weapon_onExecute orig, Weapon self)
        {
            try
            {
                bool ours = IsOurWeapon(self);

                if (_execLogCount < 20)
                {
                    _execLogCount++;
                    string item = "?";
                    try { item = self?.wInfos?.item?.ToString() ?? "?"; } catch { }
                    Write($"[ChronoBlade] Weapon.onExecute 触发 #{_execLogCount}: 类型={self?.GetType().Name} item={item} 是我方={ours}");
                }

                if (!ours)
                {
                    return orig(self);
                }

                // ---- Zaphkiel（十二之弹枪）：开火时蹦罗马数字（换弹只按 R）----
                if (self is TimeBullet gun)
                {
                    bool r = orig(self);
                    gun.OnFired();
                    return r;
                }

                // ---- 第 1a：居合前冲斩 ----
                // 在调用原版之前置满蓄力 + nextIsChargeAtk，原版就会走冲刺分支
                // （瞬移前冲 + 斩击路径上敌人依次被斩）。
                var blade = self as ChronoBlade;
                var kat = self as dc.tool.weap.Katana;

                if (kat != null)
                {
                    int cycle = -1;
                    try { cycle = kat.get_cycle(); } catch { }

                    try
                    {
                        kat.nextIsChargeAtk = true;
                        kat.katanaChargeF = ChronoBlade.FullChargeF;
                        Write($"[ChronoBlade] 第 1a 注入居合：cycle={cycle} nextIsChargeAtk=true 满蓄力={ChronoBlade.FullChargeF}");
                    }
                    catch (Exception ex)
                    {
                        Write($"[ChronoBlade] 注入居合失败: {ex.Message}");
                    }

                    // ---- 第 2a / 第 3a：一周飞镖 / 时钟剑雨（表现层特效 + 实体层投射物）----
                    //
                    // ⚠️ 必须在这里触发，不能放在 ChronoBlade.RunAttack 里！
                    //    原因：`RunAttack` 是挂在 `Hook_Katana.onExecute` 上的，而那个挂点
                    //    **实测整局都不触发**（见 Initialize 里的注释），所以它里面的分派从来没跑过 ——
                    //    这正是"2a / 3a 不见了"的根因：AddCycleEffect 是死代码。
                    //    真正会进的入口只有这一个 `tool.Weapon.onExecute`。
                    if (blade != null)
                    {
                        try { blade.AddCycleEffect(cycle); }
                        catch (Exception ex)
                        {
                            Write($"[ChronoBlade] 第 2a/3a 触发异常: {ex.Message}");
                        }
                    }
                    else if (IsOurItem(kat))
                    {
                        // 只在这一条日志出现时才有意义：说明是"物品是时之刃，但拿到的是原版 Katana 实例"
                        // （ChronoWeaponFactory 的 create 钩子没命中），此时没有 ChronoBlade 对象可调，
                        // 2a/3a 自然不会触发。
                        Write("[ChronoBlade] ⚠ 拿到的时之刃是原版 Katana 实例（create 钩子没命中），第 2a/3a 不会触发");
                    }
                }

                return orig(self);
            }
            catch (Exception ex)
            {
                Write($"[ChronoBlade] onExecute 处理器异常: {ex}");
                return orig(self);
            }
        }

        private int _execLogCount;

        /// <summary>判断这把是不是本模组的武器（时之刃 / Zaphkiel）。</summary>
        private static bool IsOurWeapon(Weapon? w)
        {
            if (w == null) return false;

            // 类型判定是主路径：Weapon.create 钩子已经把它构造成本模组的子类
            if (w is ChronoBlade || w is TimeBullet) return true;

            // 兜底：万一 create 钩子没命中（拿到的是原版 Pistol / Katana 实例），
            // 就看它绑定的物品 id。
            try
            {
                string id = w.wInfos?.item?.ToString() ?? "";
                return id.Contains("ChronoBlade") || id.Contains("TimeBullet") || id.Contains("Zaphkiel");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 招式总入口。判断"这把是不是时之刃"，然后把请求投射到那一刀上。
        ///
        /// 两条路径都覆盖：
        ///   1) 拿到的是我们的 C# 子类 ChronoBlade → 走 RunAttack（完整招式调度）
        ///   2) 拿到的是原版 Katana 实例但物品是时之刃 → 同样注入居合前冲
        /// </summary>
        private bool OnKatanaExecute(Hook_Katana.orig_onExecute orig, dc.tool.weap.Katana self)
        {
            var blade = self as ChronoBlade;
            if (blade != null)
            {
                return blade.RunAttack(() => orig(self));
            }

            // 兜底：原版 Katana 实例 + 时之刃数据
            if (IsOurItem(self))
            {
                try
                {
                    self.nextIsChargeAtk = true;
                    self.katanaChargeF = 60;
                    Write("[ChronoBlade] 兜底路径：原版 Katana 实例 + 时之刃数据 → 注入居合前冲");
                }
                catch (Exception ex)
                {
                    Write($"[ChronoBlade] 兜底注入失败: {ex.Message}");
                }
            }
            else
            {
                // 这条日志是关键证据：说明"攻击时手里拿的不是时之刃"
                Write($"[ChronoBlade] 攻击命中钩子：手持的不是时之刃（item={SafeItem(self)}），未注入居合");
            }

            return orig(self);
        }

        private static string SafeItem(dc.tool.weap.Katana self)
        {
            try { return self?.wInfos?.item?.ToString() ?? "?"; }
            catch { return "?"; }
        }

        /// <summary>判断这把武器绑定的物品 id 是不是本模组的时之刃。</summary>
        private static bool IsOurItem(dc.tool.weap.Katana self)
        {
            try
            {
                string id = self?.wInfos?.item?.ToString() ?? "";
                return id.Contains("ChronoBlade");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 每次伤害结算都会经过这里。只对"时之刃造成的伤害 + 目标是怪物"刻印，
        /// 其他任何伤害源（陷阱、投掷物、别的武器）一概不动。
        ///
        /// ⚠️ 关键：坐标要在调用 orig 之前取好。
        ///   orig(self, a) 会把伤害真正结算掉 —— 如果是斩杀，怪物当场死亡，
        ///   之后再读 mob.life 就是 0、spr 也可能已经没了（这就是"斩杀时看不到罗马数字"的原因）。
        /// </summary>
        private void OnEntityDamage(ChronoEntityDamage.orig_Entity_onDamage orig, Entity self, AttackData a)
        {
            // 先判断是不是我的时之刃（用调用前的 attackData，最可靠）
            bool mine = false;
            try { mine = a?.sourceWeapon is ChronoBlade || a?.sourceWeapon is TimeBullet; } catch { }

            // 调用前先把位置与存活状态快照下来
            double px = 0, py = 0;
            bool isMob = self is dc.en.Mob;
            if (mine && isMob)
            {
                try
                {
                    px = (self.cx + self.xr) * 24.0;
                    py = (self.cy + self.yr) * 24.0 - self.hei * 0.5;
                }
                catch { }
            }

            orig(self, a);

            if (!mine || !isMob) return;

            try
            {
                // Zaphkiel：命中后施加对应的时间系效果（坐标用调用前的快照，斩杀也能生效）
                if (a?.sourceWeapon is TimeBullet gun)
                {
                    // 带上传奇词条 ChronoBulletDouble 时，效果翻倍
                    ChronoBullets.Apply(gun.BulletIndex, self as dc.en.Mob,
                                        px, py, ModCore.Modules.Game.Instance.HeroInstance,
                                        gun.IsLegendaryDouble);
                    return;
                }

                var blade = a?.sourceWeapon as ChronoBlade;
                if (blade != null)
                {
                    // 传快照坐标：即使怪物已经被这一击打死，数字也照样刻在原地
                    blade.EngraveAt(self as dc.en.Mob, px, py);
                }
            }
            catch { }
        }

        /// <summary>
        /// 怪物死亡时在它的位置播放 TIMEJIBAI 死亡特效。
        /// 原版 Mob.onDie 在死亡瞬间调用，此时 cx/cy 与 spr 位置都还有效，适合取坐标。
        /// </summary>
        private void OnEntityDie(Hook_Entity.orig_onDie orig, Entity self)
        {
            // 配置里可以关掉死亡特效
            bool enabled = true;
            try { enabled = ChronoKeys.Config.Value.EnableDeathEffect; } catch { }

            // 先记下坐标，再调原版（原版可能会清掉 sprite / 改变实体状态）
            double px = 0, py = 0;
            bool isMob = enabled && self is dc.en.Mob;
            try
            {
                if (isMob)
                {
                    px = (self.cx + self.xr) * 24.0;
                    py = (self.cy + self.yr) * 24.0 - self.hei * 0.5;
                }
            }
            catch { }

            orig(self);

            if (!isMob) return;

            try
            {
                ChronoFx.PlayDeathEffectAt(self as dc.en.Mob, px, py);
            }
            catch { }
        }

        /// <summary>斩击命中敌人：若武器是时之刃，就在敌人身上刻一个罗马数字。</summary>
        private void OnKatanaHitFromWeapon(Hook_Katana.orig_hitFromWeapon orig, dc.tool.weap.Katana self,
                                          Entity e, Ref<int> _cycle)
        {
            orig(self, e, _cycle);
            try
            {
                if (self is ChronoBlade blade && e is dc.en.Mob mob)
                {
                    blade.Engrave(mob);
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- 资源
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(ChronoBladeMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(pakPath.AsHaxeString());
                    Logger.Information($"[ChronoBlade] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[ChronoBlade] 未找到 res.pak: {pakPath}");
                }

                // Zaphkiel 的拾取音效（Assets/sfx/CHUXIAN.WAV → pak 内 sfx/CHUXIAN.WAV）
                LoadPickupSound();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[ChronoBlade] res.pak 加载失败");
            }
        }

        /// <summary>拾取音效在 pak 里的路径（大小写各试一次，避免打包器改了大小写）。</summary>
        private const string PickupSoundPath = "sfx/CHUXIAN.WAV";

        private static readonly string[] PickupSoundCandidates =
        {
            "sfx/CHUXIAN.WAV", "sfx/CHUXIAN.wav", "sfx/chuxian.wav", "sfx/chuxian.WAV"
        };

        private void LoadPickupSound()
        {
            try
            {
                var loader = dc.hxd.Res.Class.get_loader();
                foreach (string path in PickupSoundCandidates)
                {
                    try
                    {
                        if (!loader.exists(path.AsHaxeString())) continue;
                        _pickupSfx = (dc.hxd.res.Sound)loader.loadCache(path.AsHaxeString(),
                                                                        dc.hxd.res.Sound.Class);
                        Logger.Information($"[ChronoBlade] 拾取音效已加载: {path}");
                        return;
                    }
                    catch { }
                }
                Logger.Warning($"[ChronoBlade] res.pak 里没找到拾取音效（试过 {string.Join(" / ", PickupSoundCandidates)}）");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ChronoBlade] 拾取音效加载失败: {ex.Message}");
            }
        }

        /// <summary>开局自检：确认 data.cdb 补丁真的合进来（物品表里能查到 ChronoBlade）。</summary>
        void IOnGameInit.OnGameInit()
        {
            ChronoCdbProbe.Dump(this, WeapId);
            ChronoBlade.AttachLogger(Logger);
            ChronoFx.AttachLogger(Logger);
            TimeBullet.AttachLogger(Logger);
            ChronoBullets.AttachLogger(Logger);
            ChronoWeaponFactory.AttachLogger(Logger);
            ChronoPanelLog.Attach(Logger);
            // 图集在启动阶段可能还没就绪，这里只做一次尝试，真正的重试在每帧里
            ChronoFx.TryPreloadNumeralAtlas();

            // 两把武器默认解锁（不需要捡蓝图）
            UnlockDefaultItems();

            // 确保配置文件存在（第一次运行时写出默认按键），方便玩家直接改
            LogConfig();
        }

        /// <summary>
        /// 让时之刃与 Zaphkiel **默认解锁**。
        /// 解锁状态存在玩家存档的 itemMeta 里（不是 CDB 字段），所以在开局时直接补进去。
        /// </summary>
        private void UnlockDefaultItems()
        {
            try
            {
                var game = dc.pr.Game.Class.ME;
                var meta = game?.user?.itemMeta;
                if (meta == null)
                {
                    // 存档/用户对象还没就绪：不改 _unlockDone，下一帧再试；
                    // 这条只打一次，避免刷屏。
                    if (!_unlockWarned)
                    {
                        _unlockWarned = true;
                        Logger.Information("[ChronoBlade] 默认解锁等待中：user.itemMeta 还没就绪（会继续重试）");
                    }
                    return;
                }

                bool allOk = true;
                foreach (string id in new[] { WeapId, TimeBullet.name })
                {
                    bool already = false;
                    try { already = meta.hasUnlockedItem(id.AsHaxeString()); } catch { }

                    if (already)
                    {
                        Logger.Information($"[ChronoBlade] {id} 已是解锁状态");
                        continue;
                    }

                    try
                    {
                        meta.unlockItem(id.AsHaxeString());
                        Logger.Information($"[ChronoBlade] 已默认解锁: {id}");
                    }
                    catch (Exception ex)
                    {
                        allOk = false;
                        Logger.Warning($"[ChronoBlade] 解锁 {id} 失败: {ex.Message}");
                    }
                }

                _unlockDone = allOk;
                if (allOk) Logger.Information("[ChronoBlade] 默认解锁完成（时之刃 + Zaphkiel）");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ChronoBlade] 默认解锁失败: {ex.Message}");
            }
        }

        private bool _unlockDone;
        private bool _unlockWarned;

        private static dc.String ToHaxe(string s) => new HashlinkString(s).AsHaxe<dc.String>();

        /// <summary>
        /// 把当前生效的按键配置写进日志，并把默认值落盘（第一次运行时生成配置文件）。
        /// 配置文件：coremod/config/ChronoBlade.json
        /// </summary>
        private void LogConfig()
        {
            try
            {
                var cfg = ChronoKeys.Config.Value;
                Logger.Information(
                    "[ChronoBlade] 按键配置 —— " +
                    $"选择武器面板={cfg.KeyWeaponPanel} 选择弹药={cfg.KeySelectBullet} " +
                    $"刻印自测={cfg.KeyTestNumeral} 死亡特效={cfg.EnableDeathEffect}");
                Logger.Information(
                    $"[ChronoBlade] 配置文件: {ChronoKeys.Config.ConfigPath}（也可在游戏的 选项 → 模组 菜单里直接改键）");

                // 落盘，保证第一次运行就有一个可编辑的文件
                try { ChronoKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ChronoBlade] 写配置失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 每帧
        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            ChronoFx.Update(dt);

            // Zaphkiel（十二之弹枪）：主手拿着它时，在英雄**身后**循环播放 TIMEBEIJING 背景
            try
            {
                Hero? ah = ModCore.Modules.Game.Instance.HeroInstance;
                ChronoFx.UpdateAura(dt, ah, HoldingZaphkiel());
            }
            catch (Exception ex)
            {
                Logger.Warning($"[ChronoBlade] 身后背景更新异常: {ex.Message}");
            }

            // Zaphkiel：采样位置历史（倒流/溯行用）+ 处理"送往未来"的到点拉回
            Hero? ph = ModCore.Modules.Game.Instance.HeroInstance;
            ChronoFx.SetHero(ph);
            ChronoBullets.Update(dt, ph);
            ChronoBullets.PostUpdate();

            // 记录关卡变化 —— 十二之弹"回到上一关"用（不依赖 serverStats 的语义）
            ChronoBullets.TrackLevel(ph);

            // 图集延迟重试：等英雄（也就是关卡资源）就绪后再试，成功后短路
            if (!_atlasReady)
            {
                Hero? h = ModCore.Modules.Game.Instance.HeroInstance;
                if (h != null && !h.destroyed && h._level != null)
                {
                    _atlasReady = ChronoFx.TryPreloadNumeralAtlas();
                }
            }

            // 第 2a / 第 3a 现在是**武器连击**的一部分（由 ChronoBlade.onExecute 按 cycle 触发），
            // 不再是独立热键技能 —— 所以这里没有"技能按键"要处理。

            var cfg = ChronoKeys.Config.Value;

            // 默认解锁要等存档/用户对象就绪，没成功就每帧再试（幂等，成功后短路）。
            // 放在暂停判断**之前**：暂停只是"这一帧不处理输入"，不该拖住初始化。
            if (!_unlockDone) UnlockDefaultItems();

            Hero? uh = ModCore.Modules.Game.Instance.HeroInstance;

            // ⚠ 面板打开时游戏是**真暂停**的：IOnHeroUpdate 根本不会被调用（英雄在暂停的 Game 底下）。
            //    正常情况下这里碰不到 paused，但为了万无一失（任何来源的暂停）还是显式挡一道，
            //    免得在暂停里又叠一个面板上去。
            //    面板自己的按键由游戏主循环通过 Process 栈 / ControllerAccess 处理，不需要我们插手。
            bool gamePaused = false;
            try { gamePaused = dc.pr.Game.Class.ME?.paused ?? false; } catch { }

            if (gamePaused)
            {
                // 暂停中：把按键状态钉住，避免"恢复后第一帧误触发"面板
                _weaponPanelKeyWasDown = true;
                _selectKeyWasDown = true;
                _testKeyWasDown = true;
                return;
            }

            // 选择武器面板（P）—— 面板里只列本模组新增的两把武器；打开即真暂停
            bool panelDown = IsKeyDown(ChronoKeys.Resolve(cfg.KeyWeaponPanel, 0x50));
            if (panelDown && !_weaponPanelKeyWasDown)
            {
                ChronoWeaponPanel.Open();
            }
            _weaponPanelKeyWasDown = panelDown;

            // 选择弹药面板（X）—— **换弹机制已取消**，这是唯一的换弹入口；打开即真暂停
            bool selectDown = IsKeyDown(ChronoKeys.Resolve(cfg.KeySelectBullet, 0x58));
            if (selectDown && !_selectKeyWasDown)
            {
                var gun = FindTimeBullet();
                if (gun == null) Write("[ChronoBlade] 手里没有 Zaphkiel，选择弹药面板不打开");
                else ChronoAmmoPanel.Open(gun, gun.BulletIndex);
            }
            _selectKeyWasDown = selectDown;

            // 拾取 Zaphkiel 的音效：从"主手没有"变成"主手有"的那一帧播一次
            bool holds = FindIn(uh?.weaponsManager?.mainWeapons) != null;
            if (holds && !_hadZaphkiel) PlayPickupSound();
            _hadZaphkiel = holds;

            // 刻印渲染自测 —— 键位读配置
            bool testDown = IsKeyDown(ChronoKeys.Resolve(cfg.KeyTestNumeral, 0xDD));
            if (testDown && !_testKeyWasDown)
            {
                TestNumeral();
            }
            _testKeyWasDown = testDown;

            // 每 10 秒打印一次"手里拿的是什么"，确认时之刃到底有没有装备上
            _diagTimer += dt;
            if (_diagTimer >= 10.0)
            {
                _diagTimer = 0;
                ChronoDiag.LogWeapons(this);
            }
        }

        private static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        // ---------------------------------------------------------------- 面板与暂停
        //
        // 两个面板（P 选择武器 / X 选择弹药）都**不再由本文件管理暂停**。
        //
        // 上一版的教训（写在这里，避免以后又走回去）：
        //   曾经在 dc.pr.Game.update 的钩子里切换 `Game.paused`。但 paused = true 之后
        //   **这个 update 根本不再被调用** → 解除暂停的代码永远跑不到，连 60 秒兜底也写在
        //   同一个钩子里，救不回来。教训：解除开关不能装在它自己要关掉的那扇门后面。
        //
        // 现在的做法（= 原版暂停菜单 / 训练场选武器那套 Process 进程栈）：
        //   面板继承 dc.ui.sel.GridSelector（FreeWeaponSelector 也出自它），构造函数里会
        //     · 把自己挂到 Main.Class.ME 上（注意：**不是** Game 底下）
        //     · 调 pauseGame() → HUD.hide() + Game.modalPause() = 真暂停
        //   于是 Game（连同它底下的整个 Level：英雄/怪物/弹幕/动画/粒子）全停在那一帧，
        //   而 Main 那条链照常每帧 update —— 输入、关闭、恢复全由游戏主循环负责
        //   （close() 里就是 Game.Class.ME.resume()），结构上不可能锁死。
        //
        // 所以本文件只剩"读热键 → 开面板"，见 IOnHeroUpdate；面板内部见 ChronoPanels.cs。

        /// <summary>播放 Zaphkiel 的拾取音效（sfx/CHUXIAN.WAV）。</summary>
        private void PlayPickupSound()
        {
            if (_pickupSfx == null)
            {
                Write("[ChronoBlade] 拾取 Zaphkiel（音效未加载）");
                return;
            }

            try
            {
                dc.hxd.snd.ChannelGroup? group = null;
                try { group = dc.Audio.Class.ME?.sfxChanGroup; } catch { }
                _pickupSfx.play(false, 1.0, group, null);
                Write("[ChronoBlade] 拾取 Zaphkiel：已播放 CHUXIAN 音效");
            }
            catch (Exception ex)
            {
                Write($"[ChronoBlade] 拾取音效播放失败: {ex.Message}");
            }
        }

        private dc.hxd.res.Sound? _pickupSfx;
        private bool _hadZaphkiel;
        private bool _selectKeyWasDown;

        private double _diagTimer;
        private bool _atlasReady;

        /// <summary>按 ] 时在最近的怪物身上直接画一个罗马数字，单独验证刻印渲染链路。</summary>
        private void TestNumeral()
        {
            try
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero == null || hero.destroyed) return;

                var mob = ChronoMobFinder.Nearest(hero, 40.0);
                if (mob == null)
                {
                    Write("[ChronoBlade] 刻印测试：附近没找到怪物（无法在英雄身上测试，Param2 必须是 Mob）");
                    return;
                }

                Write($"[ChronoBlade] 刻印测试：目标怪 life={mob.life} 位置=({mob.cx},{mob.cy})");
                ChronoFx.ShowNumeral(hero, mob, "VIII");
            }
            catch (Exception ex)
            {
                Write($"[ChronoBlade] 刻印测试失败: {ex}");
            }
        }

        private bool _testKeyWasDown;

        // 武器/弹药的"掉落 + 拾取"流程在 ChronoPanels.cs 里（面板确认时调用），
        // 这里原来的 SummonWeaponDrop / DescribeItem / StripKey 直召路径已随热键一起删除。


        void IOnGameExit.OnGameExit()
        {
            try
            {
                _createHook?.Disable();
                Hook_Katana.hitFromWeapon -= OnKatanaHitFromWeapon;
                WeaponCreateMap.Clear();
                ChronoFx.Clear();
            }
            catch { }
            System.Console.WriteLine("[ChronoBlade] 游戏退出，模组已卸载");
        }
    }
}
