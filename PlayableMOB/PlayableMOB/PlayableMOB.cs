using System;
using HaxeProxy.Runtime;
using ModCore;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Storage;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.hero;
using dc.level;
using dc.pr;

namespace PlayableMOB;

public class PlayableMOB : ModBase, IOnHeroUpdate, IModMenu
{
	private bool overridden = false;

	public static PlayableMOB? inst { get; private set; }

	public static Config<Configs> config { get; } = new Config<Configs>("PlayableMOB");

	public PlayableMOB(ModInfo info)
		: base(info)
	{
		inst = this;
		info.Version = "1.0.0";
		info.DCCMVersion = "35.9.23";
	}

	public override void Initialize()
	{
		((Module)this).Logger.Information("PlayableMOB mod initialized");
	}

	public string GetName()
	{
		return "Playable Enforcer";
	}

	public void BuildMenu(dc.ui.Options options)
	{
		((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(StringUtils.AsHaxeString("Playable Enforcer Settings".ToUpper()));
		((dc.ui.OptionsBase)options).createScroller(0.0);

		bool enabled = config.Value.enabled;
		((dc.ui.OptionsBase)options).addToggleWidget(
			StringUtils.AsHaxeString("Activate mod"),
			StringUtils.AsHaxeString("Achievements are disabled while this mod is activated"),
			(HlFunc<bool>)delegate
			{
				config.Value.enabled = !config.Value.enabled;
				return config.Value.enabled;
			},
			new Ref<bool>(ref enabled),
			((dc.ui.OptionsBase)options).scrollerFlow
		);

		bool flag = !config.Value.enforcer.overrideHero;
		((dc.ui.OptionsBase)options).addToggleWidget(
			StringUtils.AsHaxeString("Disable override"),
			StringUtils.AsHaxeString("Play as both the Enforcer and the Beheaded"),
			(HlFunc<bool>)delegate
			{
				config.Value.enforcer.overrideHero = !config.Value.enforcer.overrideHero;
				return !config.Value.enforcer.overrideHero;
			},
			new Ref<bool>(ref flag),
			((dc.ui.OptionsBase)options).scrollerFlow
		);

		((dc.ui.OptionsBase)options).updateScroller();
	}

	private void disableHero()
	{
		Hero hero = dc.pr.Game.Class.ME.hero;
		((Entity)hero).visible = false;
		hero.heroHead.heroHasHead = false;
		hero.controller.manualLock = true;
		((Entity)hero).setAffectS(5, 99999.0, Ref<double>.Null, (bool?)null);
		bool hasEntityTouchChecks = ((Entity)hero).hasEntityTouchChecks;
		((Entity)hero).disableAllPhysics(new Ref<bool>(ref hasEntityTouchChecks));
		overridden = true;
	}

	private void enableHero()
	{
		Hero hero = dc.pr.Game.Class.ME.hero;
		((Entity)hero).visible = true;
		hero.heroHead.heroHasHead = true;
		hero.controller.manualLock = false;
		((Entity)hero).removeAllAffects(5);
		((Entity)hero).set_sprAlpha(1.0);
		bool hasEntityTouchChecks = ((Entity)hero).hasEntityTouchChecks;
		((Entity)hero).enableAllPhysics(new Ref<bool>(ref hasEntityTouchChecks));
		overridden = false;
	}

	private void heroTrack(Entity? e)
	{
		if (config.Value.enforcer.overrideHero && e != null)
		{
			Hero hero = dc.pr.Game.Class.ME.hero;
			((Entity)hero).cx = e.cx;
			((Entity)hero).cy = e.cy;
			((Entity)hero).dir = e.dir;
		}
	}

	void IOnHeroUpdate.OnHeroUpdate(double dt)
	{
		Hero hero = dc.pr.Game.Class.ME.hero;
		if (hero == null || (hero != null && ((Entity)hero)._level == null))
		{
			return;
		}
		dc.level.Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)hero).cx, ((Entity)hero).cy);
		if (((dc.libs.Process)dc.pr.Game.Class.ME).paused)
		{
			return;
		}
		if (config.Value.enforcer.overrideHero)
		{
			if ((HeroEnforcer.inst != null || HeroMage360.inst != null) && !overridden)
			{
				disableHero();
			}
			else if (HeroEnforcer.inst == null && HeroMage360.inst == null && overridden)
			{
				enableHero();
			}
		}
		else if (overridden)
		{
			enableHero();
		}
		if (!config.Value.enabled)
		{
			return;
		}
		// Key 1: toggle Enforcer (create/destroy)
		if (Utils.pressed(config.Value.enforcer.bindings["switchEnforcer"]))
		{
			if (roomAt != null && HeroEnforcer.inst == null)
			{
				if (HeroMage360.inst != null) ((Entity)HeroMage360.inst).destroy();
				HeroEnforcer.create(hero);
			}
			else if (HeroEnforcer.inst != null && !((Entity)HeroEnforcer.inst).destroyed)
			{
				((Entity)HeroEnforcer.inst).destroy();
				((Entity)hero).set_sprAlpha(1.0);
			}
		}
		// Key 2: toggle Mage360 (create/destroy)
		if (Utils.pressed(config.Value.enforcer.bindings["switchMage"]))
		{
			if (roomAt != null && HeroMage360.inst == null)
			{
				if (HeroEnforcer.inst != null) ((Entity)HeroEnforcer.inst).destroy();
				HeroMage360.create(hero);
			}
			else if (HeroMage360.inst != null && !((Entity)HeroMage360.inst).destroyed)
			{
				((Entity)HeroMage360.inst).destroy();
				((Entity)hero).set_sprAlpha(1.0);
			}
		}
		// Key P: generic toggle (destroy whichever exists, or create Enforcer)
		if (Utils.pressed(config.Value.enforcer.bindings["toggle"]))
		{
			if (roomAt != null && HeroEnforcer.inst == null && HeroMage360.inst == null)
			{
				HeroEnforcer.create(hero);
			}
			else if (HeroEnforcer.inst != null && !((Entity)HeroEnforcer.inst).destroyed)
			{
				((Entity)HeroEnforcer.inst).destroy();
				((Entity)hero).set_sprAlpha(1.0);
			}
			else if (HeroMage360.inst != null && !((Entity)HeroMage360.inst).destroyed)
			{
				((Entity)HeroMage360.inst).destroy();
				((Entity)hero).set_sprAlpha(1.0);
			}
		}
		if (HeroEnforcer.inst != null)
			heroTrack((Entity?)(object)HeroEnforcer.inst);
		else if (HeroMage360.inst != null)
			heroTrack((Entity?)(object)HeroMage360.inst);
	}
}
