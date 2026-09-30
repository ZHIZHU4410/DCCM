using dc;
using dc.en;
using dc.en.pet;
using dc.haxe.ds;
using dc.hxd;
using dc.pr;
using dc.tool;
using dc.tool.atk;
using dc.tool.skill;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using System;
using System.Collections.Generic;

// 模组命名空间本身就叫 FlyingSword，会和 dc.en.pet.FlyingSword 撞名，
// 这里用编译期别名消歧义（运行时类型不变）。
using SwordPet = dc.en.pet.FlyingSword;

// dc.pr.Game 与 ModCore.Modules.Game 重名，固定指向模组框架的 Game。
using ModGame = ModCore.Modules.Game;

// Haxe 动态数组。
using ArrayObj = dc.hl.types.ArrayObj;

// 技能管理器 Hook（按记录补回飞剑 / 召唤命令）。
using HookSkills = dc.tool.hero.activeSkills.Hook_BeheadedActiveSkillsManager;

// CDB itemData 的 Hashlink 虚拟类型（spawnPowerSkill 的参数）。
using ItemData = Hashlink.Virtuals.virtual_ambiantDesc_castCD_cellCost_commonProps_dlc_droppable_gameplayDesc_group_icon_id_legendAffixes_moneyCost_name_props_synergy_tags_tier1_tier2_;

namespace FlyingSword
{
    /// <summary>
    /// 飞剑大改（FlyingSword）
    /// =================================================================
    /// 1) 每打败一个敌人多召唤一把飞剑
    /// 2) 飞剑拥有 IgnoreGlobalShield（攻击无视 GlobalShield 护盾）
    /// 3) 关卡结束不收回飞剑
    /// 4) 飞剑伤害增加一倍（x2）
    /// 5) 飞剑无视墙体追踪
    /// 6) 飞剑索敌范围增加 50%
    ///
    /// 关键实现点（都在 GamePseudocode 里核对过）：
    ///
    /// · 多把飞剑：_Pet.__inst_construct__ 会销毁「parent 相同且
    ///   item.permanentId 相同」的旧宠物。召唤时用
    ///   item.clone(keepPermanentId:false, null) 克隆一份，InventItem 构造器会
    ///   通过 Game.data.getNextItemPermanentId() 发新 id（见 _InventItem），
    ///   permanentId 天然互不相同 → 原版「顶掉旧宠物」的逻辑自动失效。
    ///
    /// · item 从哪来：**直接取场上已有飞剑的 item**（飞剑自己就带着技能物品），
    ///   不依赖背包扫描 —— 实测 hero.inventory.items 里扫不到这个技能物品。
    ///
    /// · IgnoreGlobalShield 是纯标记词缀。_AttackUtils.createFromHeroItem() 会
    ///   调用 attackData.useItemAffixes(item)；命中判定 AttackTargetImpl 里只有
    ///   atk.hasTag(8) 或 atk.hasAffix("IgnoreGlobalShield") 才跳过
    ///   「对方 affect 28/29/86 → Block」。给 item 加一次词缀即可长期生效。
    ///
    /// · 关卡结束强制收回走 FlyingSword.depop() → Pet.depop() → destroy()，
    ///   这里把 depop / setDepopTimer / 每帧的自动收回计时器全部挡掉。
    ///
    /// · 伤害 x2：_AttackUtils.updateDamages 里
    ///   最终伤害 = 基础 x ... x (1 + 目标侧 computeBonusMul) x (1 + 攻击侧 computeBonusMul)，
    ///   给飞剑的攻击 +1.0 → 正好翻倍，且在结算前生效（飘字/吸血/击杀统计一致）。
    ///
    /// · 无视墙体：Entity.collisionMode = CollisionMode.IgnoreWalls
    ///   （Entity 物理里只有 Normal/IgnoreWalls 走撞墙分支）；
    ///   索敌侧不看视线，直接用 team.opponentsIterator 挑最近的对手。
    ///
    /// · 索敌范围 +50%：props.range 原版 12 格 → 18 格。
    ///
    /// · 击杀计数：Hook_Mob.onDie 是主路径。注意 Mob.onDie() 内部会先把
    ///   destroyed 置 true 再进入 Hook，所以判定里**绝不能**判 destroyed；
    ///   另外每帧做一次存活快照兜底（onDie 里有多条提前 return 的分支）。
    /// </summary>
    public class FlyingSwordMain : ModBase, IOnGameExit, IOnHeroUpdate, IOnGameInit
    {
        // ============================ 可调参数 ============================

        /// <summary>飞剑伤害倍率（2.0 = 伤害增加一倍）。</summary>
        private const double DAMAGE_MUL = 2.0;

        /// <summary>每个敌人额外召唤的飞剑数量（1 = 每打败一个敌人多一把）。</summary>
        private const int SWORD_PER_KILL = 1;

        /// <summary>额外飞剑数量上限（防止上千把飞剑把帧数打崩）。</summary>
        private const int MAX_EXTRA_SWORDS = 60;

        /// <summary>索敌范围倍率（1.5 = 增加 50%）。</summary>
        private const double RANGE_MUL = 1.5;

        /// <summary>FlyingSword 原版索敌范围（格）。</summary>
        private const double BASE_RANGE = 12.0;

        /// <summary>无视 GlobalShield 的词缀 id。</summary>
        private const string IGNORE_GLOBAL_SHIELD = "IgnoreGlobalShield";

        private const string ITEM_ID = "FlyingSword";

        /// <summary>诊断开关：统计每把飞剑真正打到人的次数，并报告离场原因。</summary>
        private const bool DEBUG_SWORD = true;

        // ============================ 运行状态 ============================

        /// <summary>本模组召唤出来的额外飞剑。</summary>
        private readonly List<SwordPet> _extraSwords = new List<SwordPet>();

        /// <summary>所有见过的飞剑（技能召唤的 + 本模组召唤的）。</summary>
        private readonly List<SwordPet> _knownSwords = new List<SwordPet>();

        /// <summary>已计过数的敌人 uid（onDie 与兜底扫描去重）。</summary>
        private readonly HashSet<int> _diedOnce = new HashSet<int>();

        /// <summary>已增强过的飞剑（对象哈希），避免重复加词缀。</summary>
        private readonly HashSet<int> _enhanced = new HashSet<int>();

        /// <summary>每帧存活敌人快照（兜底击杀检测用）。</summary>
        private readonly List<Entity> _alive = new List<Entity>();
        private readonly List<Entity> _alivePrev = new List<Entity>();

        /// <summary>已累计击杀数（= 应额外召唤的飞剑数）。</summary>
        private int _killCount;

        /// <summary>上一关结束时记录的飞剑总数（含技能自己那把）。</summary>
        private int _swordsToRestore;

        /// <summary>是否还在等待「下次使用技能」把上一关的飞剑补回来。</summary>
        private bool _restorePending;

        /// <summary>正在执行本模组自己的召唤（防止 spawnPowerSkill 钩子自递归）。</summary>
        private bool _summoning;

        /// <summary>当前关卡。</summary>
        private Level? _level;

        /// <summary>CDB 里的 range 是否已改（只做一次）。</summary>
        private bool _rangeApplied;

        /// <summary>诊断：每把飞剑真正命中敌人的次数（uid → 次数）。</summary>
        private readonly Dictionary<int, int> _hitCount = new Dictionary<int, int>();

        /// <summary>诊断：飞剑被销毁/离场时记录原因。</summary>
        private readonly Dictionary<int, string> _exitReason = new Dictionary<int, string>();

        private int _reportTick;
        private int _destroyEvents;
        private int _depopEvents;
        private int _initTargetDiag;
        private int _summonFailed;
        private bool _gWasDown;

        public FlyingSwordMain(ModInfo info) : base(info) { }

        // ==================================================================
        //  初始化 / 卸载
        // ==================================================================

        public override void Initialize()
        {
            base.Initialize();

            Hook_FlyingSword.fixedUpdate += OnFlyingSwordFixedUpdate;
            Hook_FlyingSword.initTarget += OnInitTarget;
            Hook_FlyingSword.depop += OnDepop;
            Hook_FlyingSword.overrideEquipedWeapon += OnOverrideEquipedWeapon;
            Hook_Pet.init += OnPetInit;

            Hook_Mob.onDie += OnMobDie;
            Hook_AttackTargetImpl.computeBonusMul += OnComputeBonusMul;
            HookSkills.spawnPowerSkill += OnSpawnPowerSkillForRestore;

            if (DEBUG_SWORD)
            {
                Hook_Mob.applyAttackResult += OnMobApplyAttackResult;
                Hook_Pet.destroy += OnPetDestroy;
            }

            Logger.Information("[FlyingSword] 已加载：每击杀+1把飞剑、IgnoreGlobalShield、关卡结束不收回、伤害x2、无视墙体、索敌范围+50%");
        }

        void IOnGameInit.OnGameInit()
        {
            ApplyRangeToCdb();
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_FlyingSword.fixedUpdate -= OnFlyingSwordFixedUpdate;
            Hook_FlyingSword.initTarget -= OnInitTarget;
            Hook_FlyingSword.depop -= OnDepop;
            Hook_FlyingSword.overrideEquipedWeapon -= OnOverrideEquipedWeapon;
            Hook_Pet.init -= OnPetInit;

            Hook_Mob.onDie -= OnMobDie;
            Hook_AttackTargetImpl.computeBonusMul -= OnComputeBonusMul;
            HookSkills.spawnPowerSkill -= OnSpawnPowerSkillForRestore;

            if (DEBUG_SWORD)
            {
                Hook_Mob.applyAttackResult -= OnMobApplyAttackResult;
                Hook_Pet.destroy -= OnPetDestroy;
            }

            Logger.Information("[FlyingSword] 游戏退出，模组已卸载");
        }

        // ==================================================================
        //  6) 索敌范围 +50%
        // ==================================================================

        /// <summary>把 CDB 里 FlyingSword 的 props.range 设为原版的 1.5 倍（12 → 18 格）。</summary>
        private void ApplyRangeToCdb()
        {
            if (_rangeApplied) return;

            try
            {
                dynamic itemData = Data.Class.item.byId.get(ToHaxeString(ITEM_ID));
                if (itemData == null) return;

                itemData.props.range = BASE_RANGE * RANGE_MUL;
                _rangeApplied = true;
                Logger.Information("[FlyingSword] 索敌范围 = " + (BASE_RANGE * RANGE_MUL) + " 格");
            }
            catch (Exception ex)
            {
                Logger.Warning("[FlyingSword] 索敌范围写入失败: " + ex.Message);
            }
        }

        // ==================================================================
        //  每帧
        // ==================================================================

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (!_rangeApplied) ApplyRangeToCdb();

            Hero? hero = null;
            try { hero = ModGame.Instance?.HeroInstance; } catch { }
            if (hero == null || hero.destroyed || hero.life <= 0) return;

            Level? lvl = hero._level;
            if (lvl == null) return;

            // 热键 G：直接召唤一把额外飞剑（方便在不杀敌的情况下验证召唤结果）
            if (DEBUG_SWORD) HandleHotkey(hero);

            // 换关 / 重开
            if (!ReferenceEquals(lvl, _level)) OnLevelChanged(lvl);

            SyncSwords();
            SweepDeaths(lvl);
            if (DEBUG_SWORD) ReportSwords();
        }

        /// <summary>
        /// 换关：飞剑会被游戏回收（这一版不再强留），
        /// 所以在这里**记录本关一共有多少把飞剑**，下次玩家用飞剑技能时一次性补回来。
        ///
        /// ⚠ 这里必须清空 _knownSwords / _extraSwords：
        ///   旧关卡的对象换关后全部失效，如果不清，
        ///   _extraSwords.Count 会一直挂在旧数量上，
        ///   击杀召唤的 `while (_extraSwords.Count &lt; want)` 条件永远不成立
        ///   —— 这就是「进第二个图后打败敌人不召唤飞剑」的原因。
        /// </summary>
        private void OnLevelChanged(Level lvl)
        {
            _level = lvl;

            // 记录「总共几把」= 技能自己那把 + 本模组召唤的额外飞剑
            int alive = 0;
            for (int i = 0; i < _knownSwords.Count; i++)
            {
                SwordPet s = _knownSwords[i];
                if (s != null && !s.destroyed && s.life > 0) alive++;
            }

            _swordsToRestore = alive;
            _restorePending = alive > 0;

            _knownSwords.Clear();
            _extraSwords.Clear();
            _enhanced.Clear();

            _killCount = 0;
            _diedOnce.Clear();
            _alive.Clear();
            _alivePrev.Clear();

            Logger.Information("[FlyingSword] 进入新关卡：记录上一关共 " + alive
                + " 把飞剑，下次使用技能时全部补回");
        }

        /// <summary>
        /// 玩家使用飞剑技能时，把上一关记录的飞剑一次性补回来。
        /// spawnPowerSkill 是我们自己的召唤入口，用 _summoning 防自递归。
        /// </summary>
        private bool OnSpawnPowerSkillForRestore(HookSkills.orig_spawnPowerSkill orig,
            dc.tool.hero.activeSkills.BeheadedActiveSkillsManager self,
            int id, InventItem i, ItemData infos)
        {
            bool result = orig(self, id, i, infos);

            if (_summoning) return result;

            try
            {
                if (!_restorePending) return result;
                if (i == null || i._itemData == null) return result;
                if (i._itemData.id.ToString() != ITEM_ID) return result;

                Hero? hero = ModGame.Instance?.HeroInstance;
                if (hero == null || hero.destroyed || hero.life <= 0) return result;

                _restorePending = false;

                // 技能自己已经召唤了 1 把，这里补上剩下的
                int extra = _swordsToRestore - 1;
                if (extra <= 0)
                {
                    Logger.Information("[FlyingSword] 使用技能：上一关记录 "
                        + _swordsToRestore + " 把，技能自身已补足，无需额外召唤");
                    return result;
                }

                if (extra > MAX_EXTRA_SWORDS) extra = MAX_EXTRA_SWORDS;

                Logger.Information("[FlyingSword] 使用技能：按记录补回 " + extra + " 把飞剑");
                for (int n = 0; n < extra; n++)
                {
                    if (!SpawnSword(hero)) break;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("[FlyingSword] 补回飞剑失败: " + ex.Message);
            }

            return result;
        }

        /// <summary>诊断热键：G 键直接召唤一把额外飞剑（不杀敌也能测）。</summary>
        private void HandleHotkey(Hero hero)
        {
            bool down;
            try { down = Key.Class.isDown.Invoke(71); } catch { down = false; }

            bool pressed = down && !_gWasDown;
            _gWasDown = down;
            if (!pressed) return;

            Logger.Information("[FlyingSword][DIAG] 按下 G → 尝试召唤额外飞剑");
            bool ok = SpawnSword(hero);
            Logger.Information("[FlyingSword][DIAG] G 召唤结果 = " + ok
                + "，当前额外飞剑 " + _extraSwords.Count + " 把");
        }

        /// <summary>清理已销毁的飞剑，并给新飞剑补增强。</summary>
        private void SyncSwords()
        {
            for (int i = _knownSwords.Count - 1; i >= 0; i--)
            {
                SwordPet s = _knownSwords[i];
                if (s == null || s.destroyed || s.life <= 0)
                {
                    if (s != null) _enhanced.Remove(s.GetHashCode());
                    _knownSwords.RemoveAt(i);
                    continue;
                }
                EnhanceSword(s);
            }

            for (int i = _extraSwords.Count - 1; i >= 0; i--)
            {
                SwordPet s = _extraSwords[i];
                if (s == null || s.destroyed || s.life <= 0) _extraSwords.RemoveAt(i);
            }
        }

        /// <summary>所有 Pet 的 init() 都会被这里接住：飞剑一出生就登记并增强。</summary>
        private void OnPetInit(Hook_Pet.orig_init orig, Pet self)
        {
            orig(self);

            try
            {
                if (self is not SwordPet sword) return;
                if (sword.destroyed) return;

                if (!_knownSwords.Contains(sword)) _knownSwords.Add(sword);
                EnhanceSword(sword);
            }
            catch (Exception ex)
            {
                Logger.Warning("[FlyingSword] 登记飞剑失败: " + ex.Message);
            }
        }

        // ==================================================================
        //  2) + 5) + 3) 飞剑增强：IgnoreGlobalShield / 穿墙 / 不收回
        // ==================================================================

        private void EnhanceSword(SwordPet sword)
        {
            if (sword == null || sword.destroyed) return;

            int key = sword.GetHashCode();
            if (_enhanced.Contains(key)) return;

            try
            {
                EnsureIgnoreGlobalShield(sword);

                // 关卡结束不收回：干掉自动收回计时器
                try { sword.unsetDepopTimer(); } catch { }

                // 注：不再动 collisionMode。飞剑本来走 MvFly，而 MvFly.canGoto() 恒 true、
                // 也不会撞地形，本身就是穿墙飞行；强行改成 IgnoreWalls 反而可能干扰
                // 实体物理更新（这是「新召唤的飞剑不攻击」的重点怀疑对象）。

                _enhanced.Add(key);
            }
            catch (Exception ex)
            {
                Logger.Warning("[FlyingSword] 增强飞剑失败: " + ex.Message);
            }
        }

        private void EnsureIgnoreGlobalShield(SwordPet sword)
        {
            try
            {
                InventItem? item = sword.item;
                if (item == null) return;

                dc.String affix = ToHaxeString(IGNORE_GLOBAL_SHIELD);
                if (item.hasAffix(affix)) return;

                bool ignoreChecks = true;
                item.addAffix(affix, ref ignoreChecks);
            }
            catch (Exception ex)
            {
                Logger.Warning("[FlyingSword] 附加 IgnoreGlobalShield 失败: " + ex.Message);
            }
        }

        // ==================================================================
        //  1) 每打败一个敌人多召唤一把飞剑
        // ==================================================================

        private void OnMobDie(Hook_Mob.orig_onDie orig, Mob self)
        {
            // 注意：必须在 orig 之后再判断，且判断里不能看 destroyed
            // （Mob.onDie 内部会先把 destroyed 置 true）。
            orig(self);

            try { TryCountKill(self); }
            catch (Exception ex) { Logger.Warning("[FlyingSword] 击杀召唤失败: " + ex.Message); }
        }

        /// <summary>
        /// 兜底击杀检测：上一帧还活着的敌人、这一帧不在存活列表里 → 算一次击杀。
        /// onDie 里有若干提前 return 的分支，单靠 Hook 会漏。
        /// </summary>
        private void SweepDeaths(Level lvl)
        {
            try
            {
                _alive.Clear();
                CollectAliveEnemies(lvl, _alive);

                for (int i = 0; i < _alivePrev.Count; i++)
                {
                    Entity prev = _alivePrev[i];
                    if (prev == null) continue;
                    if (ContainsEntity(_alive, prev)) continue;
                    if (prev is Mob mob) TryCountKill(mob);
                }

                _alivePrev.Clear();
                for (int i = 0; i < _alive.Count; i++) _alivePrev.Add(_alive[i]);
            }
            catch (Exception ex)
            {
                Logger.Warning("[FlyingSword] 兜底扫描失败: " + ex.Message);
            }
        }

        private static void CollectAliveEnemies(Level lvl, List<Entity> outList)
        {
            ArrayObj? mobs = lvl.teamMob?.asMobs;
            if (mobs == null) return;

            int n = mobs.length;
            for (int i = 0; i < n; i++)
            {
                if (mobs.array[i] is not Mob m) continue;
                if (m.destroyed || m.life <= 0 || m.maxLife <= 0) continue;
                outList.Add(m);
            }
        }

        private static bool ContainsEntity(List<Entity> list, Entity e)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], e)) return true;
            }
            return false;
        }

        /// <summary>确认是「被打败的敌人」就计数并补召唤。同一个敌人只计一次。</summary>
        private void TryCountKill(Mob mob)
        {
            if (mob == null) return;

            // ⚠ 这里绝对不能判 mob.destroyed：
            //   Mob.onDie() 内部会先把 destroyed 置 true，之后才进入 Hook，
            //   所以 onDie 路径下 destroyed 恒为 true —— 判了就等于永远不计数。
            int uid = SafeUid(mob);
            if (uid != 0 && !_diedOnce.Add(uid)) return;

            // 只认「敌方队伍」的怪；maxLife<=0 的是场景装饰
            if (mob._level == null || mob._team == null) return;
            if (mob._team != mob._level.teamMob) return;
            if (mob.maxLife <= 0) return;

            Hero? hero = null;
            try { hero = ModGame.Instance?.HeroInstance; } catch { }
            if (hero == null || hero.destroyed || hero.life <= 0) return;
            if (mob._level != hero._level) return;

            _killCount++;
            int want = _killCount * SWORD_PER_KILL;
            if (want > MAX_EXTRA_SWORDS) want = MAX_EXTRA_SWORDS;

            // 换关后还没按记录补回飞剑时，_extraSwords 是空的：
            // 此时绝不能因为「本关击杀数还小」就拒绝召唤，否则第二个图会出现
            // 「打死敌人却不召唤」。这里把上一关欠下的数量一起算进来。
            int pending = _restorePending ? System.Math.Max(0, _swordsToRestore - 1) : 0;
            if (pending > want) want = pending;
            if (want > MAX_EXTRA_SWORDS) want = MAX_EXTRA_SWORDS;

            while (_extraSwords.Count < want)
            {
                if (!SpawnSword(hero)) break;
            }
        }

        private static int SafeUid(Entity? e)
        {
            if (e == null) return 0;
            try { return e.__uid; } catch { return 0; }
        }

        // ==================================================================
        //  诊断：谁真的打到了人 / 飞剑是怎么没的
        // ==================================================================

        /// <summary>统计每把飞剑真正命中敌人的次数。没命中的剑一眼就能看出来。</summary>
        private void OnMobApplyAttackResult(Hook_Mob.orig_applyAttackResult orig, Mob self, AttackData a)
        {
            try
            {
                if (a?.source is SwordPet sword)
                {
                    int uid = SafeUid(sword);
                    _hitCount.TryGetValue(uid, out int n);
                    _hitCount[uid] = n + 1;
                }
            }
            catch { }

            orig(self, a);
        }

        /// <summary>
        /// 飞剑被销毁时拦下来 —— 「关卡结束不收回」的落点。
        ///
        /// FlyingSword.depop() → Pet.depop() 最后一定会 destroy()；
        /// 关卡结束 Hero.clean() 也是走 clearPets() → depop()。
        /// 所有飞剑都用同一个技能 item（permanentId 相同），而
        /// _Pet.__inst_construct__ 会销毁「parent 相同 + permanentId 相同」的旧宠物，
        /// 所以这里必须一律拦住，否则新召唤的剑会把先召唤的那把顶掉。
        /// </summary>
        private void OnPetDestroy(Hook_Pet.orig_destroy orig, Pet self)
        {
            if (self is SwordPet)
            {
                _destroyEvents++;
                if (DEBUG_SWORD && _destroyEvents <= 10)
                {
                    Logger.Information("[FlyingSword][DIAG] 拦下飞剑 destroy 第 " + _destroyEvents
                        + " 次（uid=" + SafeUid(self) + "，本模组召唤的=" + _extraSwords.Contains((SwordPet)self) + "）");
                }
                return;
            }

            orig(self);
        }

        /// <summary>每秒报告一次飞剑的命中情况，找出「不攻击」的飞剑。</summary>
        private void ReportSwords()
        {
            _reportTick++;
            if (_reportTick % 60 != 0) return;
            if (_extraSwords.Count == 0) return;

            int totalHits = 0;
            foreach (var kv in _hitCount) totalHits += kv.Value;

            int idle = 0, noTarget = 0, locked = 0;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int i = 0; i < _extraSwords.Count && i < 8; i++)
            {
                SwordPet s = _extraSwords[i];
                if (s == null || s.destroyed) continue;

                int uid = SafeUid(s);
                _hitCount.TryGetValue(uid, out int hits);

                bool isLocked;
                try { isLocked = s.aiLocked(); } catch { isLocked = false; }
                bool hasTarget = s.target != null;

                if (!hasTarget) noTarget++;
                if (isLocked) locked++;
                if (hits == 0) idle++;

                if (i < 4)
                {
                    sb.Append(" | uid=").Append(uid)
                      .Append(" 命中=").Append(hits)
                      .Append(" 目标=").Append(hasTarget ? s.target!.GetType().Name : "无")
                      .Append(" aiLocked=").Append(isLocked)
                      .Append(" visible=").Append(s.visible);
                }
            }

            Logger.Information("[FlyingSword][DIAG] 额外飞剑 " + _extraSwords.Count
                + " 把，总命中 " + totalHits
                + "，其中 0 命中 " + idle + " 把、无目标 " + noTarget + " 把、aiLocked " + locked + " 把"
                + sb);
        }

        // ==================================================================
        //  召唤
        // ==================================================================

        /// <summary>
        /// 【召唤方式】直接调用游戏自己的技能召唤命令
        /// `HeroActiveSkillsManager.spawnPowerSkill(id, item, itemData)`，
        /// 而不是自己 new 一把飞剑。
        ///
        /// 原因：手工 new 出来的飞剑，物品是 clone 的、permanentId 不在背包里，
        /// 飞剑内部大量 `inventory.getByPermanentId(base.item.permanentId)` 反查
        /// 就会走空分支，导致它永远停在「非部署」状态（实测 aiLocked=True，
        /// 有目标也不进 updateAttack）。
        /// 走原版命令则由游戏自己完成构造 + init + 登记，产物与玩家亲手用技能
        /// 召唤出来的飞剑完全一致 —— 只多触发一次「召唤」这个动作。
        /// </summary>
        private bool SpawnSword(Hero hero)
        {
            SwordPet? source = FindSourceSword();
            if (source == null)
            {
                if (_summonFailed++ < 3) Logger.Warning("[FlyingSword] 召唤失败：场上找不到参照飞剑");
                return false;
            }

            InventItem? srcItem = source.item;
            ItemData? infos = srcItem?._itemData;
            if (srcItem == null || infos == null)
            {
                if (_summonFailed++ < 3) Logger.Warning("[FlyingSword] 召唤失败：参照飞剑的 item/itemData 为空");
                return false;
            }

            // spawnPowerSkill 定义在 BeheadedActiveSkillsManager 上，
            // 而 hero.activeSkillsManager 的静态类型是基类 HeroActiveSkillsManager。
            var mgr = hero.activeSkillsManager as dc.tool.hero.activeSkills.BeheadedActiveSkillsManager;
            if (mgr == null || hero._level == null) return false;

            int before = _knownSwords.Count;
            bool ok;
            try
            {
                // 与原版 useSkillItem 里同一句：spawnPowerSkill(id, i, itemData)。
                // FlyingSword 分支不使用 id，传 0 即可。
                // _summoning 用来让 spawnPowerSkill 的钩子知道「这是本模组自己在召唤」，
                // 避免它在里面又去补回上一关的飞剑（自递归）。
                _summoning = true;
                try
                {
                    ok = mgr.spawnPowerSkill(0, srcItem, infos);
                }
                finally
                {
                    _summoning = false;
                }
            }
            catch (Exception ex)
            {
                _summoning = false;
                Logger.Warning("[FlyingSword] 召唤飞剑异常: " + ex);
                return false;
            }

            // 新飞剑会由 Hook_Pet.init 自动登记进 _knownSwords
            SwordPet? created = null;
            for (int i = _knownSwords.Count - 1; i >= before; i--)
            {
                SwordPet s = _knownSwords[i];
                if (s != null && !s.destroyed) { created = s; break; }
            }

            if (created == null)
            {
                if (_summonFailed++ < 3) Logger.Warning("[FlyingSword] 召唤失败：spawnPowerSkill 返回 " + ok + " 但没有新飞剑");
                return false;
            }

            if (!_extraSwords.Contains(created)) _extraSwords.Add(created);
            EnhanceSword(created);

            Logger.Information("[FlyingSword] 击杀 " + _killCount + " → 召唤第 " + _extraSwords.Count + " 把飞剑");
            return true;
        }

        /// <summary>找一把可以拿来复制的飞剑（优先本模组召唤的）。</summary>
        private SwordPet? FindSourceSword()
        {
            for (int i = 0; i < _knownSwords.Count; i++)
            {
                SwordPet s = _knownSwords[i];
                if (s != null && !s.destroyed && s.life > 0 && s.item != null) return s;
            }
            return null;
        }

        // ==================================================================
        //  5) 无视墙体追踪：索敌不看视线
        // ==================================================================

        private void OnInitTarget(Hook_FlyingSword.orig_initTarget orig, SwordPet self)
        {
            orig(self);

            try
            {
                if (self.parent == null || self.parent._team == null) return;

                // 原版已经挑到活着的目标就不动它（免得每帧换目标让飞剑乱抖）
                Entity? cur = self.target;
                if (cur != null && !cur.destroyed && cur.life > 0 && cur.canBeHit()) return;

                Entity? best = PickNearestOpponent(self, BASE_RANGE * RANGE_MUL);
                if (best != null) self.target = best;

                if (DEBUG_SWORD && _initTargetDiag < 20)
                {
                    _initTargetDiag++;
                    Logger.Information("[FlyingSword][DIAG] initTarget 第 " + _initTargetDiag
                        + " 次: uid=" + SafeUid(self)
                        + " 额外=" + _extraSwords.Contains(self)
                        + " 原版给的目标=" + (cur == null ? "null" : cur.GetType().Name)
                        + " 自选到=" + (best == null ? "null" : best.GetType().Name)
                        + " 对手数=" + CountOpponents(self));
                }
            }
            catch (Exception ex)
            {
                Logger.Warning("[FlyingSword] 穿墙索敌失败: " + ex.Message);
            }
        }

        /// <summary>诊断用：数一下 opponentsIterator 能拿到几个对手。</summary>
        private static int CountOpponents(SwordPet self)
        {
            try
            {
                Entity? p = self.parent;
                if (p == null || p._team == null) return -1;

                int n = 0;
                var it = p._team.opponentsIterator.reset(p._team);
                while (it.hasNext()) { it.next(); n++; }
                return n;
            }
            catch { return -2; }
        }

        /// <summary>不看视线、不看地形，只挑索敌半径内最近的合法敌人。</summary>
        private static Entity? PickNearestOpponent(SwordPet self, double rangeTiles)
        {
            Entity? parent = self.parent;
            if (parent == null || parent._team == null) return null;

            double rangePx = rangeTiles * 24.0;
            double bestDist = rangePx * rangePx;
            Entity? best = null;

            double px = (double)parent.cx + parent.xr;
            double py = (double)parent.cy + parent.yr;

            var iter = parent._team.opponentsIterator.reset(parent._team);
            while (iter.hasNext())
            {
                Entity e = iter.next();
                if (e == null || e.destroyed || e.life <= 0) continue;
                if (!e.canBeHit()) continue;

                double dx = px - ((double)e.cx + e.xr);
                double dy = py - ((double)e.cy + e.yr);
                double d = dx * dx + dy * dy;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }
            return best;
        }

        // ==================================================================
        //  3) 关卡结束不收回
        // ==================================================================

        /// <summary>
        /// 只拦 depop 的「销毁」效果，但**不拦**原来的收尾逻辑。
        ///
        /// 一开始这里是直接吞掉整个 depop()，结果日志显示同一个 uid 每帧被调用几百次
        /// —— 说明 depop() 是每帧都会走的常规收尾路径（刷新后处理/清计时器），
        /// 整个吞掉等于每帧都让飞剑跳过自己的收尾逻辑，反而把它卡住了。
        /// 现在改成：正常执行 orig()（让飞剑完成收尾），只是**不让它把自己销毁**。
        /// 真正的「关卡结束强制收回」走的是 Pet.depop() → destroy()，
        /// 由 OnPetDestroy 那一侧兜住。
        /// </summary>
        private void OnDepop(Hook_FlyingSword.orig_depop orig, SwordPet self)
        {
            _depopEvents++;
            orig(self);
        }

        /// <summary>换武器时中断攻击（避免半截挥砍卡住）。</summary>
        private void OnOverrideEquipedWeapon(Hook_FlyingSword.orig_overrideEquipedWeapon orig, SwordPet self, bool withFeedbacks)
        {
            orig(self, withFeedbacks);
            try { InterruptAttacks(self); } catch { }
        }

        private static void InterruptAttacks(SwordPet self)
        {
            ArrayObj? list = self.attackList;
            if (list == null) return;

            int n = list.length;
            for (int i = 0; i < n; i++)
            {
                if (list.array[i] is OldSkill skill)
                {
                    try { skill.interrupt(); } catch { }
                }
            }
        }

        // ==================================================================
        //  4) 伤害增加一倍
        // ==================================================================

        /// <summary>
        /// _AttackUtils.updateDamages 里：
        ///   最终伤害 = 基础 x ... x (1 + 目标侧 computeBonusMul) x (1 + 攻击侧 computeBonusMul)
        /// 飞剑的攻击额外 +100% → 伤害正好翻倍。
        /// </summary>
        private double OnComputeBonusMul(
            Hook_AttackTargetImpl.orig_computeBonusMul orig,
            AttackTargetImpl self,
            AttackData atk)
        {
            double bonus = orig(self, atk);

            try
            {
                if (DAMAGE_MUL > 1.0 && atk?.source is SwordPet)
                {
                    bonus += DAMAGE_MUL - 1.0;
                }
            }
            catch { }

            return bonus;
        }

        // ==================================================================
        //  保持：飞剑不做原版 AI 锁
        // ==================================================================

        private void OnFlyingSwordFixedUpdate(Hook_FlyingSword.orig_fixedUpdate orig, SwordPet self)
        {
            orig(self);

            try
            {
                // 关掉自动收回计时器，防止飞剑自己消失
                self.unsetDepopTimer();
            }
            catch { }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
