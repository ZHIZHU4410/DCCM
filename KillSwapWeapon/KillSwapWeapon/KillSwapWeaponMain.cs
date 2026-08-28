using dc;
using dc.en;
using dc.h2d;
using dc.level;
using dc.tool;
using dc.tool.hero;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;

namespace KillSwapWeapon
{
    /// <summary>
    /// 击杀随机换武器：每击败一个怪物，1号位置（第一个武器槽位 mainWeapons[0] / posID 0）的武器
    /// 从武器池中随机更换一把（随机结果不会与当前武器相同）。
    /// - 换出的武器为【传奇武器】：带 Legendary 词缀 + Colorless（随最高属性）+ 该武器的传奇专属词缀 + 随机词缀。
    /// - 等级与当前地图匹配（item level = 当前地图 lootLevel），伤害不会偏低。
    /// - 换武器走游戏原生刷新流程（Hero.onEquipedItemsChange），HUD 与背包同步更新。
    /// - 屏幕左上角常驻 HUD：显示击杀数、当前 1号位武器（名称+等级）。
    /// </summary>
    public class KillSwapWeaponMain : ModBase, IOnGameExit, IOnHeroUpdate, IOnGameInit
    {
        /// <summary>1号位置 = 槽位 0（主武器）。如需改为 2号位置，改成 1。</summary>
        private const int SLOT = 0;

        /// <summary>当前局累计换武器次数（跨关卡保留，仅用于 HUD 显示）。</summary>
        private int _swapCount;

        private readonly Random _random = new Random();

        /// <summary>实际使用的武器池（BuildPool 在游戏初始化后根据配置构建）。</summary>
        private string[] _pool = System.Array.Empty<string>();

        // ================= 左上角 HUD =================
        private const int HUD_COLOR_TITLE = 0x00E5FF;    // 标题：青色
        private const int HUD_COLOR_WHITE = 0xFFFFFF;    // 正文：白色
        private const int HUD_COLOR_YELLOW = 0xFFE066;   // 武器行：黄色
        private const int HUD_COLOR_SHADOW = 0x000000;   // 阴影：黑色
        private const double HUD_X = 20.0;               // 左上角 x
        private const double HUD_Y = 18.0;               // 左上角 y
        private const double HUD_LINE_H = 18.0;          // 行距
        private const int HUD_LINES = 3;                 // 行数（每行 = 阴影 + 正文 两个 Text）

        private dc.h2d.Text[]? _hudTexts;               // [行0阴影, 行0正文, 行1阴影, 行1正文, ...]
        private dc.pr.Level? _hudLevel;                 // 当前挂载的关卡（切换关卡时重建）
        private readonly string[] _lastHudText = new string[HUD_LINES];

        /// <summary>
        /// 内置默认安全池（可被 config 中 WeaponIds 覆盖；若 config 中未配置则使用此池）。
        /// 已剔除的易崩溃武器：
        /// - 双持武器（换一半会破坏配对状态）：DualDaggers / TickScytheLeft / TickScytheRight / CombinedTickScythe /
        ///   SnakeFang / ExplosiveCrossBow / ExplosiveCrossBowOffHand / HardLightSword / MachetePistol / DualBow / Lantern
        /// - 长摁/持续攻击武器（按住持续攻击时被销毁重建会崩溃，如 FlameThrower 空访问 .isCircle）：
        ///   FlameThrower / Lightning / LightningWhip / AlchemicGun / MagicSalve / LaserGlaive / Burner
        /// - 触碰信号武器：KingScepter（构造时注册 touchSignal 回调，换武器后回调悬空，
        ///   英雄一触碰实体就在回调内空访问崩溃）
        /// - MedusaHead（美杜莎头，石化机制在换武器后异常，已剔除）
        /// </summary>
        public static readonly string[] DefaultWeaponIds = new string[]
        {
            // 近战
            "DashShield", "StartSword", "AdminWeapon", "QuickSword", "RevengeSword", "BackStabber",
            "Bleeder", "BroadSword", "Shovel", "EvilSword", "BleedCrit",
            "SpeedBlade", "GiantKiller", "BulletBlade", "SismicBlade", "Spear", "ImpaleSpear",
            "KingsSpear", "Rapier", "DashSword", "StunMace", "BumpBoots", "SpikedBoots",
            "MultiKickBoots", "QuickFists", "Whip", "HookWhip", "OilSword",
            "LowHealth", "PerfectHalberd", "TentacleWhip", "Pan", "ParryBlade",
            "RhythmicBlade", "Crowbar", "GiantStaff",
            "NotFlyingSword", "Katana", "Tombstone", "HeavyAxe",
            "Club", "ClubBroken", "PureNail", "SkulBone",
            "Trident", "HandHook", "Shark", "WreckingBall", "QueenRapier",
            "CupidityDagger", "GoldDigger", "GoldDiggerEvolved", "BaseballBat", "NunchuckPan",
            "Starfury", "TPSword", "WiggleWhip", "Bible", "AreaShield", "ThunderShield",
            "Rampart", "VampireKiller", "AdeleScythe", "RichterVampireKiller",
            // 远程
            "Pistol", "Scissor", "Comb", "Misericord", "WarriorShield",
            "InfiniteBow", "LongBow", "SonicCrossbow", "CloseCombatBow", "FastBow", "FrostBow",
            "Boomerang", "BleedAxe", "ThrowingSpear",
            "MarkBow", "PreciseBow", "ThrowingKnife", "ThrowingTorch", "ThrowingIce",
            "FireBall", "Freeze", "Blowgun",
            "BarrelLauncher", "ThrowingCards", "HeavyBow", "MoneyShooter", "MagicBow", "ThrowableStuff",
            "HydraSpell", "Cross", "ThrowingAxe", "Anathema"
        };

        public KillSwapWeaponMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_Hero.onMobDeath += OnHeroKillMob;
            System.Console.WriteLine($"[KillSwapWeapon] 模组已加载：每击败一个怪物，{SLOT + 1}号位置武器随机更换为【传奇武器】（等级匹配当前地图）");
        }

        /// <summary>游戏数据初始化完成后构建武器池（此时才能校验武器 id 是否存在）。</summary>
        void IOnGameInit.OnGameInit()
        {
            try
            {
                BuildPool();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[KillSwapWeapon] 构建武器池失败: {ex.Message}");
                _pool = (string[])DefaultWeaponIds.Clone();
            }
        }

        /// <summary>
        /// 根据配置构建实际武器池：
        /// - config.WeaponIds 非空 → 使用其中有效的武器（无效 id 跳过并提示）。
        /// - 空列表 / 全部无效 → 回退内置默认安全池。
        /// </summary>
        private void BuildPool()
        {
            List<string> configured = new List<string>();
            foreach (string raw in KillSwapWeaponConfig.Instance.Value.WeaponIds)
            {
                string id = raw?.Trim() ?? "";
                if (id.Length == 0 || configured.Contains(id)) continue;
                configured.Add(id);
            }

            if (configured.Count == 0)
            {
                _pool = (string[])DefaultWeaponIds.Clone();
                System.Console.WriteLine($"[KillSwapWeapon] 配置中未自定义武器池，使用内置默认池（{_pool.Length} 把）");
                return;
            }

            List<string> valid = new List<string>();
            foreach (string id in configured)
            {
                try
                {
                    var itemData = Data.Class.item.byId.get(ToHaxeString(id));
                    if (itemData != null) valid.Add(id);
                    else System.Console.WriteLine($"[KillSwapWeapon] 配置中的武器 id 不存在，已跳过: {id}");
                }
                catch
                {
                    System.Console.WriteLine($"[KillSwapWeapon] 配置中的武器 id 校验失败，已跳过: {id}");
                }
            }

            if (valid.Count == 0)
            {
                _pool = (string[])DefaultWeaponIds.Clone();
                System.Console.WriteLine($"[KillSwapWeapon] 自定义武器池全部无效，回退内置默认池（{_pool.Length} 把）");
            }
            else
            {
                _pool = valid.ToArray();
                System.Console.WriteLine($"[KillSwapWeapon] 已启用自定义武器池（{_pool.Length} 把）：{string.Join(", ", _pool)}");
            }
        }

        /// <summary>击杀事件：每击败一个怪物更换一次 1号位置武器。</summary>
        private void OnHeroKillMob(Hook_Hero.orig_onMobDeath orig, Hero self, dc.en.Mob m)
        {
            orig(self, m);

            try
            {
                if (self == null || self.destroyed || self.life <= 0) return;
                SwapSlotWeapon(self);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[KillSwapWeapon] 更换武器失败: {ex.Message}");
            }
        }

        // ================= 左上角 HUD =================

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            try
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero == null || hero._level == null || hero._level.root == null) return;
                UpdateHud(hero);
            }
            catch
            {
                // HUD 单帧失败不影响游戏
            }
        }

        /// <summary>每帧刷新 HUD：切换关卡时重建，文本变化时才重设。</summary>
        private void UpdateHud(Hero hero)
        {
            dc.pr.Level level = hero._level;

            // 关卡切换（level.root 会随关卡销毁）或创建失败未就绪 → （重新）创建 HUD
            if (_hudTexts == null || _hudLevel != level)
            {
                CleanupHud();
                _hudLevel = level;
                CreateHud(level);
                if (_hudTexts == null) return;   // 字体等未就绪，下一帧重试
            }

            // 当前 1号位武器（名称 + 等级）
            string weaponName = "-";
            int weaponLevel = 0;
            InventItem? slotItem = hero.inventory?.getEquippedWeaponOn(SLOT);
            if (slotItem != null)
            {
                if (slotItem.kind is InventItemKind.Weapon wk) weaponName = wk.Index.ToString();
                weaponLevel = slotItem._itemLevel;
            }

            string[] lines = new string[HUD_LINES];
            lines[0] = "KILL SWAP";
            lines[1] = $"KILLS: {_swapCount}";
            lines[2] = $"WEAPON: {weaponName} Lv{weaponLevel}";

            dc.h2d.Text[]? texts = _hudTexts;
            if (texts == null) return;

            for (int i = 0; i < HUD_LINES; i++)
            {
                if (lines[i] == _lastHudText[i]) continue;
                _lastHudText[i] = lines[i];
                dc.String s = ToHaxeString(lines[i]);
                texts[i * 2]!.set_text(s);          // 阴影
                texts[i * 2]!.visible = true;
                texts[i * 2 + 1]!.set_text(s);      // 正文
                texts[i * 2 + 1]!.visible = true;
            }
        }

        /// <summary>在 level.root 的 UI 层创建 HUD 文本（每行：阴影 + 正文）。</summary>
        private void CreateHud(dc.pr.Level level)
        {
            try
            {
                Font font = dc.Assets.Class.font12;
                if (font == null) return;
                int layer = dc.Const.Class.ROOT_DP_CTX_UI;

                var texts = new dc.h2d.Text[HUD_LINES * 2];
                for (int i = 0; i < HUD_LINES; i++)
                {
                    texts[i * 2] = MakeHudText(font, level, i, HUD_COLOR_SHADOW, shadow: true);
                    texts[i * 2 + 1] = MakeHudText(font, level, i, HudLineColor(i), shadow: false);
                }
                foreach (dc.h2d.Text t in texts)
                {
                    if (t != null) level.root.addChildAt(t, layer);
                }

                _hudTexts = texts;
                for (int i = 0; i < HUD_LINES; i++) _lastHudText[i] = "";
                System.Console.WriteLine("[KillSwapWeapon] 左上角 HUD 已创建");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[KillSwapWeapon] HUD 创建失败: {ex.Message}");
                CleanupHud();
            }
        }

        private static dc.h2d.Text MakeHudText(Font font, dc.pr.Level level, int line, int color, bool shadow)
        {
            var t = new dc.h2d.Text(font, level.root);
            t.textColor = color;
            t.x = HUD_X + (shadow ? 1.0 : 0.0);
            t.y = HUD_Y + line * HUD_LINE_H + (shadow ? 1.0 : 0.0);
            t.visible = false;
            return t;
        }

        private static int HudLineColor(int line)
        {
            switch (line)
            {
                case 0: return HUD_COLOR_TITLE;
                case 2: return HUD_COLOR_YELLOW;
                default: return HUD_COLOR_WHITE;
            }
        }

        /// <summary>从父容器移除全部 HUD 文本。</summary>
        private void CleanupHud()
        {
            if (_hudTexts != null)
            {
                foreach (dc.h2d.Text t in _hudTexts)
                {
                    if (t == null) continue;
                    try
                    {
                        if (t.parent != null) t.parent.removeChild(t);
                    }
                    catch
                    {
                        // 父容器可能已随关卡销毁
                    }
                }
            }
            _hudTexts = null;
            _hudLevel = null;
        }

        /// <summary>把 1号位置（SLOT）的武器替换为一把随机传奇武器。</summary>
        private void SwapSlotWeapon(Hero hero)
        {
            if (hero.inventory == null || hero.weaponsManager == null) return;
            HeroWeaponsManager wm = hero.weaponsManager;
            if (wm.mainWeapons == null || wm.mainWeapons.length <= SLOT) return;

            // 当前 1号位的物品（若为空则无可更换）
            InventItem? oldItem = hero.inventory.getEquippedWeaponOn(SLOT);
            if (oldItem == null) return;

            // 防崩溃：若 1号位武器正在使用中（攻击动画/持续攻击/蓄力中），本次跳过，
            // 否则销毁重建会破坏正在运行的技能（如 FlameThrower 持续喷射时崩溃）。
            if (wm.mainWeapons.length > SLOT && wm.mainWeapons.array[SLOT] is Weapon curWeapon)
            {
                if (curWeapon.isPlayingAttackAnim() || curWeapon.isCharging())
                {
                    System.Console.WriteLine("[KillSwapWeapon] 1号位武器使用中，本次击杀暂不更换（下次击杀再换）");
                    return;
                }
            }

            // 当前武器 id（用于避免随机到同一把）
            string? currentId = null;
            if (oldItem.kind is InventItemKind.Weapon wk) currentId = wk.Index.ToString();

            // 随机挑选一把（校验物品数据存在、且与当前不同）
            string? picked = PickRandomWeaponId(currentId);
            if (picked == null) return;

            // 生成一把与当前地图等级匹配的传奇武器
            InventItem newItem = CreateLegendaryWeapon(hero, picked);
            newItem.posID = SLOT;

            // 替换背包中的物品（保持槽位不变）
            hero.inventory.replace(oldItem, newItem);

            // 游戏原生刷新流程：销毁旧武器实例并按 inventory 重建 + 刷新 HUD
            bool updateHUD = true;
            bool duringHeroInit = false;
            bool duringItemTransform = false;
            hero.onEquipedItemsChange(ref updateHUD, ref duringHeroInit, ref duringItemTransform);

            _swapCount++;
            System.Console.WriteLine($"[KillSwapWeapon] 已击败怪物，{SLOT + 1}号位武器更换为【传奇】: {picked} (等级 {newItem._itemLevel})，累计换武器 {_swapCount} 次");
        }

        /// <summary>
        /// 创建一把传奇武器：等级 = 当前地图 lootLevel；词缀流程与游戏内传奇一致——
        /// Legendary 词缀 → 该武器传奇专属词缀（legendAffixes）→ 随机词缀 → Colorless（随最高属性）。
        /// </summary>
        private InventItem CreateLegendaryWeapon(Hero hero, string picked)
        {
            // 当前地图掉落等级（与游戏内该地图掉落物等级完全一致）
            int lootLevel = hero._level.map.lootLevel;

            InventItem item = new InventItem(new InventItemKind.Weapon(ToHaxeString(picked)));
            item.setItemLevel(lootLevel);

            bool ignoreChecks = true;

            // 1) 传奇词缀（addAffix 内部会自动解析 _itemData）
            item.addAffix(ToHaxeString("Legendary"), ref ignoreChecks);

            // 2) 该武器的传奇专属词缀（如 +150% 伤害等），与游戏内传奇一致
            try
            {
                object? rawData = item._itemData;
                dynamic? itemData = rawData;
                dynamic? legendAffixes = itemData?.legendAffixes;
                if (legendAffixes != null)
                {
                    int n = (int)legendAffixes.length;
                    for (int i = 0; i < n; i++)
                    {
                        object? rawAf = legendAffixes.getDyn(i);
                        dynamic? af = rawAf;
                        if (af?.affix != null)
                        {
                            item.addAffix((dc.String)af.affix, ref ignoreChecks);
                        }
                    }
                }
            }
            catch
            {
                // 个别武器无传奇专属词缀，忽略
            }

            // 3) 随机词缀（统计类，与游戏 generateStats 相同来源）
            try
            {
                ItemGen itemGen = new ItemGen(hero._level.map.seed, false);
                int tierCount = item.getRandomTierAffixCount();
                for (int i = 0; i < tierCount; i++)
                {
                    itemGen.addRandomTierAffix(item);
                }
            }
            catch
            {
                // 随机词缀失败不影响传奇武器本身
            }

            // 4) 无色：传奇武器随三项属性中最高的一项加成（伤害更高）
            item.addAffix(ToHaxeString("Colorless"), ref ignoreChecks);

            return item;
        }

        /// <summary>从当前武器池随机选一把（最多尝试 8 次，跳过物品数据不存在或与当前相同的）。</summary>
        private string? PickRandomWeaponId(string? currentId)
        {
            if (_pool.Length == 0) return null;   // 武器池未构建（游戏未初始化完成）
            for (int i = 0; i < 8; i++)
            {
                string id = _pool[_random.Next(_pool.Length)];
                if (id == currentId) continue;
                try
                {
                    var itemData = Data.Class.item.byId.get(ToHaxeString(id));
                    if (itemData != null) return id;
                }
                catch
                {
                    // 该 id 解析失败，换下一个随机项
                }
            }
            return null;
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.onMobDeath -= OnHeroKillMob;
            _swapCount = 0;
            CleanupHud();
            System.Console.WriteLine("[KillSwapWeapon] 游戏退出，模组已卸载");
        }
    }
}
