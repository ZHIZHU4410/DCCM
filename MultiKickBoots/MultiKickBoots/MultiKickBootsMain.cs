using dc;
using dc.en;
using dc.hl;
using dc.hl.types;
using dc.level;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero;
using dc.tool.weap;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.Runtime.CompilerServices;

namespace MultiKickBootsMod
{
    /// <summary>
    /// 让 MultiKickBoots 的每一段攻击都触发最后一段的画面扭曲 + 额外效果。
    /// 原版"仅最后段"的效果：
    ///   kickShockWave（画面扭曲，纯视觉）
    ///   setAffectS(17) 眩晕 + setAffectS(104) 持续伤害
    ///   bump 击退 + tryToKickGrenades 弹反炮弹
    /// </summary>
    public class MultiKickBootsMain : ModBase, IOnHeroUpdate
    {
        private MultiKickBoots? _activeWeapon;
        private int _lastCycle = -1;

        public MultiKickBootsMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUse;
            System.Console.WriteLine("[MultiKickBoots] 每段画面扭曲 Mod 已加载");
        }

        private void OnWeaponUse(
            Hook_HeroWeaponsManager.orig_onWeaponUse orig,
            HeroWeaponsManager self,
            Weapon w,
            int slot)
        {
            orig(self, w, slot);
            if (w is MultiKickBoots mk)
            {
                _activeWeapon = mk;
                _lastCycle = -1;
            }
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (_activeWeapon == null) return;
            if (_activeWeapon.destroyed || _activeWeapon.owner == null)
            {
                _activeWeapon = null;
                return;
            }

            try
            {
                int cycle = _activeWeapon.get_cycle();
                if (cycle == _lastCycle) return;
                _lastCycle = cycle;

                // 最后一段由原版 onExecute 完整处理（含 kickShockWave + 所有效果）
                if (_activeWeapon.isLastCycle())
                {
                    _activeWeapon = null;
                    return;
                }

                ApplyPerCycleEffects(_activeWeapon);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[MultiKickBoots] 异常: {ex.Message}");
            }
        }

        private void ApplyPerCycleEffects(MultiKickBoots weapon)
        {
            Hero owner = weapon.owner;
            if (owner?._level == null) return;

            double bump = weapon.get_curSkillInf().props.bump ?? 0.0;

            // 玩家中心世界坐标
            double worldX = ((double)owner.cx + owner.xr) * 24.0;
            double worldY = ((double)owner.cy + owner.yr) * 24.0 - owner.hei * 0.5;

            // 1. kickShockWave — 画面扭曲波纹（纯视觉，每段都放）
            int fxColor = weapon.get_curSkillInf().fxProps.fxInnerColor ?? 0;
            owner._level.fx.kickShockWave(worldX, worldY, 120.0, fxColor);

            // 2. 遍历敌人施加效果（原版用 team.opponentsIterator）
            TeamIterator? it = null;
            if (owner._team != null)
                it = owner._team.opponentsIterator.reset(owner._team);

            if (it != null)
            {
                while (it.hasNext())
                {
                    Entity e = it.next();
                    if (e == null || e.destroyed) continue;
                    if (!weapon.canHit(e, null)) continue;

                    // setAffectS(17, 3.0) — 眩晕
                    e.setAffectS(17, 3.0, Ref<double>.Null, null);

                    // setAffectS(104, 0.66) — 持续伤害
                    double dmg = weapon.itemInf.props.power2 * 100.0
                        * (1.0 + weapon.item.getDamageBonus());
                    double dmgRef = dmg;
                    e.setAffectS(104, 0.66, new Ref<double>(ref dmgRef), null);

                    // bump 击退
                    double dir = ((double)e.cx + e.xr) * 24.0 < worldX ? -1.0 : 1.0;
                    e.bump(bump * dir * e.getDiminishingFactor(42, 5, 40, null), 0.0, null);
                }
            }

            // 3. 踢飞手雷/弹反炮弹 — 需要 Area 对象确定范围
            double rand = Random.Shared.NextDouble() * 0.3;
            double kickStrength = (0.85 + rand) * bump;
            int tier = owner.getRelevantTierFor(weapon.item);

            // 尝试从 areas 数组取出 Area（用 Unsafe.As 绕过 HashlinkObject 转型限制）
            try
            {
                var areas = weapon.areas;
                if (areas != null)
                {
                    int cycle = weapon.get_cycle();
                    if (cycle < areas.length)
                    {
                        object rawArea = areas.array[cycle];
                        if (rawArea != null)
                        {
                            Area area = Unsafe.As<Area>(rawArea);
                            owner.tryToKickGrenades(area, weapon.item, kickStrength, tier, null, null);
                        }
                    }
                }
            }
            catch
            {
                // Unsafe.As 失败时跳过弹反（不影响其他效果）
            }
        }
    }
}
