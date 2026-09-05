using dc;
using dc.en;
using dc.tool;
using dc.tool.weap;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.Collections.Generic;

namespace CrossX10
{
    /// <summary>
    /// Cross（十字架武器 tool.weap.Cross）强化模组：
    /// 1) 模型放大 10 倍 —— 飞出的主十字架（crossAvril）与沿途残影（CrossFake / crossAvrilFlat）均放大 10 倍；
    /// 2) 攻击判定范围放大 10 倍 —— 实体碰撞半径 radius（18 → 180px，约 7.5 格）放大 10 倍，
    ///    使十字架飞行/停留/回程时能命中更大范围（圆形判定，垂直方向同样覆盖）；
    /// 3) 无视墙体 —— 碰撞模式切换为 CollisionMode.IgnoreWalls，飞行与回收途中直接穿过所有墙体/单向板；
    /// 4) 弹药上限 20 —— 把物品数据 Cross 的 commonProps.ammo 改为 20（可连发），
    ///    最多同时 20 个十字架在场，飞回时逐个 +1 补回（原版 retrieve 逻辑），按住攻击即可连发。
    ///
    /// 实现原理：
    /// 游戏内每次投掷都会新建 CrossEntity 并调用 init() → initGfx()，
    /// 因此在 dc.en.Hook_CrossEntity.initGfx 之后挂接处理：
    ///   - 主精灵缩放走 Entity 的 sprScaleX/sprScaleY（spriteUpdate 每帧应用）；
    ///   - 残影 CrossFake 是普通 HSprite，直接设置 scaleX/scaleY（pivot 已居中）；
    ///   - radius 决定攻击命中圆的半径（pr.Level.resolveCircularCollisions 使用 entity.radius）；
    ///   - collisionMode = IgnoreWalls 后，Entity 水平移动循环不再做墙体阻挡分支；
    ///   - 弹药上限：Data.Class.item.byId["Cross"].commonProps.ammo = 20（getMaxAmmo/retrieve 都读它），
    ///     并对英雄已持有的 Cross 武器 InventItem 一次性把当前弹药补到 20（之后靠原版飞回 +1 补回）。
    /// 仅作用于 Cross 武器本体（RichterCross 等 weapon == null 的投掷不受影响）。
    /// </summary>
    public class CrossX10Main : ModBase, IOnGameExit, IOnGameInit, IOnHeroUpdate
    {
        /// <summary>放大倍数：模型 / 攻击判定范围</summary>
        private const double SCALE = 10.0;

        /// <summary>十字架武器生成的 CrossEntity 原版碰撞半径（_CrossEntity.__inst_construct__ 固定 18px）</summary>
        private const double BASE_RADIUS = 18.0;

        /// <summary>十字架弹药上限（可同时在场数量）</summary>
        private const int MAX_AMMO = 20;

        /// <summary>十字架武器在物品库中的 id</summary>
        private const string ITEM_ID = "Cross";

        private bool _dataPatched = false;

        /// <summary>记录已经补过弹药的武器实例，只补一次，之后交给原版飞回 +1 逻辑</summary>
        private readonly HashSet<InventItem> _refilled = new HashSet<InventItem>();

        public CrossX10Main(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_CrossEntity.initGfx += OnCrossEntityInitGfx;
            System.Console.WriteLine($"[CrossX10] 已挂载：Cross 十字架 模型/判定范围 x{SCALE} + 无视墙体 + 弹药上限 {MAX_AMMO}");
        }

        void IOnGameInit.OnGameInit()
        {
            ApplyItemDataPatch();
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (!_dataPatched) ApplyItemDataPatch();
            TopUpEquippedCross();
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_CrossEntity.initGfx -= OnCrossEntityInitGfx;
            System.Console.WriteLine("[CrossX10] 已卸载");
        }

        // ------------------------------------------------------------------
        // 弹药上限 20：修改物品数据 commonProps.ammo（getMaxAmmo / retrieve 都读取该值）
        // ------------------------------------------------------------------
        private void ApplyItemDataPatch()
        {
            try
            {
                object? record = Data.Class.item.byId.get(ToHaxeString(ITEM_ID));
                if (record == null)
                {
                    return; // 数据还没就绪，下次再试
                }
                dynamic itemData = record;
                object? cp = itemData.commonProps;
                if (cp == null)
                {
                    return; // 数据还没就绪，下次再试
                }
                dynamic commonProps = cp;
                commonProps.ammo = MAX_AMMO;
                _dataPatched = true;
                System.Console.WriteLine($"[CrossX10] 已将物品 {ITEM_ID} 弹药上限改为 {MAX_AMMO}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[CrossX10] 修改物品弹药上限失败(将重试): " + ex.Message);
            }
        }

        /// <summary>
        /// 英雄已装备的 Cross 武器，一次性把当前弹药补到上限；
        /// 之后投掷会正常消耗（可连发到 20 个在场），飞回时由原版 retrieve() 逐个 +1。
        /// </summary>
        private void TopUpEquippedCross()
        {
            try
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero == null || hero.destroyed || hero.life <= 0) return;

                var wm = hero.weaponsManager;
                if (wm?.mainWeapons == null) return;

                int n = wm.mainWeapons.length;
                for (int i = 0; i < n; i++)
                {
                    object? raw = wm.mainWeapons.array[i];
                    if (raw is not Cross w) continue;
                    InventItem? item = w.item;
                    if (item == null) continue;
                    if (_refilled.Add(item))
                    {
                        item.ammo = MAX_AMMO;
                        System.Console.WriteLine($"[CrossX10] 十字架弹药补至 {MAX_AMMO}，可连发");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[CrossX10] 检查装备十字架弹药失败: " + ex.Message);
            }
        }

        /// <summary>
        /// CrossEntity.initGfx 之后执行：
        /// 仅在武器为 tool.weap.Cross（RichterCross 的 weapon 为 null）时应用强化。
        /// </summary>
        private void OnCrossEntityInitGfx(Hook_CrossEntity.orig_initGfx orig, CrossEntity self)
        {
            // 保留原版：创建主精灵 + 4 个残影 CrossFake
            orig(self);

            try
            {
                if (self == null || self.destroyed) return;

                // 只强化十字架武器（tool.weap.Cross）投出的十字架；
                // RichterCross（Castlevania 技能）等 weapon 为 null 的十字架保持原样。
                if (self.weapon is not Cross) return;

                // ---- 1) 主十字架模型放大 10 倍（spriteUpdate 每帧以 sprScaleX/Y * dir 设置 sprite.scale）----
                self.sprScaleX = SCALE;
                self.sprScaleY = SCALE;

                // ---- 2) 攻击判定（圆形碰撞半径）放大 10 倍，命中范围与视觉大小匹配 ----
                self.radius = BASE_RADIUS * SCALE;

                // ---- 3) 无视墙体：飞行与回收途中直接穿过墙壁/单向板 ----
                self.collisionMode = new CollisionMode.IgnoreWalls();

                // ---- 4) 沿途残影（CrossFake）模型也放大 10 倍（pivot 居中，直接缩放精灵）----
                var fakes = self.fakeCrosses;
                if (fakes != null)
                {
                    int n = fakes.length;
                    for (int i = 0; i < n; i++)
                    {
                        object? raw = fakes.array[i];
                        if (raw is CrossFake cf)
                        {
                            cf.scaleX = SCALE;
                            cf.scaleY = SCALE;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[CrossX10] initGfx 强化处理异常: " + ex);
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
