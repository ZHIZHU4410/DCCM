using System;
using System.Collections.Generic;
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

	// Active monster tracking — each Hero class sets this in create() and clears in destroy()
	public static Entity? activeMonster;

	// Monster registry — index 0 = Enforcer, cycle with keys 1/2
	private static readonly List<Action<Hero>> monsterFactories = new()
	{
		HeroEnforcer.create,
		HeroMage360.create,
		HeroShield.create,
		HeroBomber.create,
		HeroGolem.create,
		HeroFatZombie.create,
		HeroEarthquaker.create,
		HeroStomper.create,
		HeroRampager.create,
		HeroArbiter.create,
		HeroTick.create,
		HeroShopMimic.create,
		HeroComboter.create,
		HeroU28VacuumCleaner.create,
		HeroHurler.create,
		HeroBatKamikaze.create,
		HeroLibrarian.create,
		HeroMedusa.create,
		HeroAxeStatue.create,
	};

	private static int currentIndex = 0;
	private static int MonsterCount => monsterFactories.Count;

	public PlayableMOB(ModInfo info) : base(info)
	{
		inst = this;
		info.Version = "1.0.0";
		info.DCCMVersion = "35.9.23";
	}

	public override void Initialize()
	{
		((Module)this).Logger.Information("PlayableMOB mod initialized");
	}

	public string GetName() => "Playable MOB";

	public void BuildMenu(dc.ui.Options options)
	{
		((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(StringUtils.AsHaxeString("Playable MOB Settings".ToUpper()));
		((dc.ui.OptionsBase)options).createScroller(0.0);

		bool enabled = config.Value.enabled;
		((dc.ui.OptionsBase)options).addToggleWidget(
			StringUtils.AsHaxeString("Activate mod"),
			StringUtils.AsHaxeString("Achievements disabled while active"),
			(HlFunc<bool>)delegate { config.Value.enabled = !config.Value.enabled; return config.Value.enabled; },
			new Ref<bool>(ref enabled), ((dc.ui.OptionsBase)options).scrollerFlow);

		bool flag = !config.Value.enforcer.overrideHero;
		((dc.ui.OptionsBase)options).addToggleWidget(
			StringUtils.AsHaxeString("Disable override"),
			StringUtils.AsHaxeString("Play as both mob and Beheaded"),
			(HlFunc<bool>)delegate { config.Value.enforcer.overrideHero = !config.Value.enforcer.overrideHero; return !config.Value.enforcer.overrideHero; },
			new Ref<bool>(ref flag), ((dc.ui.OptionsBase)options).scrollerFlow);

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

	private static bool AnyAlive => activeMonster != null && !activeMonster.destroyed;

	private static void DestroyCurrent()
	{
		activeMonster?.destroy();
		activeMonster = null;
	}

	private static void CreateByIndex(Hero hero, int index)
	{
		if (index >= 0 && index < MonsterCount)
		{
			try
			{
				monsterFactories[index](hero);
			}
			catch (Exception ex)
			{
				((Module)inst!).Logger.Error($"Failed to create monster at index {index}: {ex.Message}");
				// Skip to next monster on failure
				currentIndex = (index + 1) % MonsterCount;
			}
		}
	}

	void IOnHeroUpdate.OnHeroUpdate(double dt)
	{
		Hero hero = dc.pr.Game.Class.ME.hero;
		if (hero == null || ((Entity)hero)._level == null) return;
		dc.level.Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)hero).cx, ((Entity)hero).cy);
		if (((dc.libs.Process)dc.pr.Game.Class.ME).paused) return;

		if (config.Value.enforcer.overrideHero)
		{
			if (AnyAlive && !overridden) disableHero();
			else if (!AnyAlive && overridden) enableHero();
		}
		else if (overridden) { enableHero(); }

		if (!config.Value.enabled) return;

		// Key 1: previous monster
		if (Utils.pressed(config.Value.enforcer.bindings["cyclePrev"]))
		{
			DestroyCurrent();
			currentIndex = (currentIndex - 1 + MonsterCount) % MonsterCount;
			if (roomAt != null) CreateByIndex(hero, currentIndex);
			((Entity)hero).set_sprAlpha(1.0);
		}
		// Key 2: next monster
		if (Utils.pressed(config.Value.enforcer.bindings["cycleNext"]))
		{
			DestroyCurrent();
			currentIndex = (currentIndex + 1) % MonsterCount;
			if (roomAt != null) CreateByIndex(hero, currentIndex);
			((Entity)hero).set_sprAlpha(1.0);
		}
		// Key P: toggle (destroy or create current)
		if (Utils.pressed(config.Value.enforcer.bindings["toggle"]))
		{
			if (AnyAlive) { DestroyCurrent(); ((Entity)hero).set_sprAlpha(1.0); }
			else if (roomAt != null) CreateByIndex(hero, currentIndex);
		}

		if (activeMonster != null && !activeMonster.destroyed) heroTrack(activeMonster);
	}
}
