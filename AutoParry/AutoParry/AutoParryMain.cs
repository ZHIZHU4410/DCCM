#nullable disable

using dc;
using dc.en;
using dc.en.active;
using dc.en.dookuInteractions;
using dc.en.hero;
using dc.en.inter;
using dc.en.mob;
using dc.hl;
using dc.hl.types;
using dc.level;
using dc.libs.heaps.slib;
using dc.pow;
using dc.pr;
using dc.pr.infection;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero.activeSkills;
using dc.tool.mainSkills;
using dc.tool.mod.script;
using dc.tool.weap;
using dc.ui;
using dc.ui.sel;
using Hashlink;
using Hashlink.Proxy;
using Hashlink.Proxy.Clousre;
using Hashlink.Proxy.DynamicAccess;
using Hashlink.Proxy.Objects;
using Hashlink.Proxy.Values;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Storage;
using ModCore.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace AutoParry
{
    /// <summary>
    /// Auto parry mod: when enabled, holding a shield automatically triggers a
    /// perfect parry on incoming attacks. The master switch lives in the
    /// in-game options menu (IModMenu), no hotkey required.
    /// </summary>
    public class AutoParryMain : ModBase, IOnGameExit, IModMenu
    {
        public static Config<Configs> config { get; } = new Config<Configs>("AutoParry");

        private static bool Enabled
        {
            get => config.Value.enabled;
            set => config.Value.enabled = value;
        }

        public AutoParryMain(ModInfo info) : base(info) { }

        #region Lifecycle

        public override void Initialize()
        {
            base.Initialize();
            Hook_BaseShield.onOwnerAttackResultReceived += OnShieldAttackResult;
            // Core: intercept before damage is applied, so no HP is lost
            Hook_Entity.applyAttackResult += OnEntityApplyAttackResult;

            System.Console.WriteLine("[AutoParry] Auto parry mod loaded. Toggle it from the options menu.");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_BaseShield.onOwnerAttackResultReceived -= OnShieldAttackResult;
            Hook_Entity.applyAttackResult -= OnEntityApplyAttackResult;
            System.Console.WriteLine("[AutoParry] Game exit, resources cleaned.");
        }

        #endregion

        #region Options menu

        public string GetName() => "AutoParry";

        public void BuildMenu(dc.ui.Options options)
        {
            ((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(
                StringUtils.AsHaxeString("AUTOPARRY SETTINGS"));
            ((dc.ui.OptionsBase)options).createScroller(0.0);

            bool enabled = Enabled;
            ((dc.ui.OptionsBase)options).addToggleWidget(
                StringUtils.AsHaxeString("Enable auto parry"),
                StringUtils.AsHaxeString("While holding a shield, incoming attacks are blocked and perfect parried automatically"),
                (HlFunc<bool>)delegate
                {
                    Enabled = !Enabled;
                    config.Save();
                    return Enabled;
                },
                new Ref<bool>(ref enabled),
                ((dc.ui.OptionsBase)options).scrollerFlow);

            ((dc.ui.OptionsBase)options).updateScroller();
        }

        #endregion

        #region Hook implementations

        /// <summary>
        /// Entity hit hook: intercepts before damage is applied.
        /// Only works when auto parry is enabled and the player holds a shield,
        /// blocking the damage and letting OnShieldAttackResult handle the parry.
        /// </summary>
        private void OnEntityApplyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            // Check whether the hit target is the player hero
            Hero hero = attack?.lastHitTarget as Hero;
            if (hero == null && self is Hero s)
                hero = s;

            // Conditions: auto parry on + player holds shield + attack has a
            // source + not a trap/tag-7/tag-29 attack
            bool shouldBlock = Enabled
                && hero != null
                && attack != null
                && attack.source != null
                && !attack.hasTag(7)
                && !attack.hasTag(29);

            if (shouldBlock)
            {
                // Not calling orig() = damage fully blocked, no HP loss.
                // OnShieldAttackResult is triggered by the engine afterwards
                // and performs the parry animation + counter.
                return;
            }

            orig(self, attack);
        }

        /// <summary>
        /// Shield hit callback: while holding the shield, automatically trigger
        /// a perfect parry (animation, counter damage, bullet reflect, grenade bounce).
        /// </summary>
        private void OnShieldAttackResult(Hook_BaseShield.orig_onOwnerAttackResultReceived orig, BaseShield self, AttackData attack)
        {
            bool shouldAutoParry = Enabled
                && self?.owner != null
                && attack != null
                && attack.source != null
                && !attack.hasTag(7)
                && !attack.hasTag(29);

            if (!shouldAutoParry)
            {
                orig(self, attack);
                return;
            }

            try
            {
                self.owner.dir = -attack.source.dir;
                self.startParry();
                self.triggerParryFeedbacks();
                self.applyStunAndBumpFromParry(attack);
                self.interrupt();
                self.requireRelease = true;
                self.owner.unlockControls();
                attack.removeTag(7);
                self.owner.recoil(attack.dirSourceToTarget() * 7);
                self.onShieldBlock(attack, true);

                if (attack.carrier != null)
                {
                    if (attack.carrier is Bullet bullet)
                        self.counterBullet(attack, bullet, true);
                    else if (attack.carrier is Grenade grenade)
                        self.counterGrenade(grenade);
                }

                double tempValue = self.item?.getShieldAbsorb() ?? 0;
                var ignoreResist = new Ref<double>(ref tempValue);
                self.owner.setAffectS(98, 0.5, ignoreResist, null);
                self.owner.setAffectS(96, 0.5, ignoreResist, null);
                self.shieldCounterAttack(attack, true);
                attack.hitResult = new HitResult.Block();
                self.owner?.spr?.get_anim()?.playCustomSequence(self.parryAnimId, 0, 4, null);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[AutoParry] Parry handling error: {ex.Message}");
                orig(self, attack);
            }
        }

        #endregion
    }

    /// <summary>Persistent config for the AutoParry mod.</summary>
    public class Configs
    {
        public bool enabled = true;
    }
}
