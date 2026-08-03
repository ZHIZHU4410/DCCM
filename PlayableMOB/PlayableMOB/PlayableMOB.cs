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
using ModCore.Events.Interfaces.Game.Save;
using dc;
using dc.en;
using dc.en.hero;
using dc.level;
using dc.pr;

namespace PlayableMOB;

public class PlayableMOB : ModBase, IOnHeroUpdate, IModMenu, IOnBeforeSavingSave, IOnAfterLoadingSave
{
	private bool overridden = false;

	public static PlayableMOB? inst { get; private set; }
	public static Config<Configs> config { get; } = new Config<Configs>("PlayableMOB");

	// Active monster tracking — each Hero class sets this in create() and clears in destroy()
	public static Entity? activeMonster;

	// 怪物注册表（按类型分组排列，顺序即切换顺序，分类参考 res/data.cdb 的 group）
	// —— 普通怪（group 0-3，索引 0..14，共 15 个）——
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
		HeroLibrarian.create,
		HeroComboter.create,
		HeroU28VacuumCleaner.create,
		HeroHurler.create,
		HeroBatKamikaze.create,
		// —— 特殊怪（group 4，索引 15..18，共 4 个）——
		HeroShopMimic.create,
		HeroMedusa.create,
		HeroAxeStatue.create,
		HeroTick.create,
		// —— BOSS（group 5，索引 19..27，共 9 个）——
		HeroKingsHand.create,
		HeroQueen.create,
		HeroDooku.create,
		HeroDeath.create,
		HeroGardenerBoss.create,
		HeroCollectorBoss.create,
		HeroTimeKeeper.create,
		HeroBehemoth.create,
		HeroGiant.create,
	};

	// 怪物英文显示名（也是配置项 monsterEnabled 的键，顺序必须与 monsterFactories 一致）
	private static readonly List<string> monsterNames = new()
	{
		"Enforcer",
		"Mage 360",
		"Shield",
		"Bomber",
		"Golem",
		"Fat Zombie",
		"Earthquaker",
		"Stomper",
		"Rampager",
		"Arbiter",
		"Librarian",
		"Comboter",
		"Vacuum Cleaner",
		"Hurler",
		"Bat Kamikaze",
		"Shop Mimic",
		"Medusa",
		"Axe Statue",
		"Tick",
		"Kings Hand",
		"Queen",
		"Dooku",
		"Death",
		"Gardener Boss",
		"Collector",
		"Time Keeper",
		"Behemoth",
		"Giant",
	};

	// 怪物中文名（仅用于设置菜单显示，便于辨认）
	private static readonly List<string> monsterNamesCn = new()
	{
		"盾斧", "360法师", "盾牌兵", "轰炸机", "魔像", "胖子僵尸", "撼地者", "践踏者",
		"狂暴者", "仲裁者", "图书管理员", "连击者", "吸尘器", "投掷者", "自爆蝙蝠",
		"宝箱怪", "美杜莎", "斧头雕像", "蜱虫",
		"国王之手", "女王", "德古拉", "死神", "稻草人", "收藏家", "时间守护者", "看守者", "巨人",
	};

	// 设置菜单分类：分类名 -> 在 monsterFactories 中的起止索引（前闭后开）
	private static readonly (string Category, int Start, int End)[] monsterCategories =
	{
		("普通怪", 0, 15),
		("特殊怪", 15, 19),
		("BOSS", 19, 28),
	};

	// 每个 BOSS 的技能键位介绍（索引 0..8 对应 BOSS 分类内的第 0..8 个，即怪物索引 19..27）
	private static readonly string[] bossSkillDocs = new string[]
	{
		"技能：J圆斩 K冲锋 L重斩 U践踏 I地裂 H盾冲 X手雷 C大炸弹",
		"技能：J突刺 K火波 L冲击波 U抓取 H后跳 X过载盾 C嘲讽 I次元斩",
		"技能：J射击 K岩浆球 U弹幕 I地刺 H隐身 X抓取 C噬魂",
		"技能：J镰刀连击 K大镰下劈 L大镰上挑 U掷镰 I魂弹 H魂爆 X魂终极",
		"技能：J锄头 K叉子 L镰刀A U镰刀B I铲子 H上铲 X藤蔓 C践踏",
		"技能：J冲刺 K旋转 L激光 U践踏 I火墙",
		"技能：J前斩 K冲刺 L钩爪 I重击 H手里剑 X烟雾弹+升级斩",
		"技能：J普攻 K冲击波 L跳砸 U火焰护甲",
		"技能：J激光",
	};

	/// <summary>Returns true if the monster at the given index is enabled (defaults to true).</summary>
	private static bool IsMonsterEnabled(int index)
	{
		if (index < 0 || index >= monsterNames.Count) return false;
		string name = monsterNames[index];
		return !config.Value.monsterEnabled.TryGetValue(name, out bool en) || en;
	}

	/// <summary>Finds the next enabled monster index in the given direction, wrapping around.</summary>
	private static int FindNextEnabled(int start, int direction)
	{
		for (int i = 1; i < MonsterCount; i++)
		{
			int idx = (start + direction * i + MonsterCount) % MonsterCount;
			if (IsMonsterEnabled(idx)) return idx;
		}
		return start; // all disabled — stay put
	}

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

		// ── 按类型分组的怪物开关 ──
		foreach (var cat in monsterCategories)
		{
			// 分类标题（如：普通怪 / 特殊怪 / BOSS）
			((dc.ui.OptionsBase)options).addSeparator(
				StringUtils.AsHaxeString(cat.Category),
				((dc.ui.OptionsBase)options).scrollerFlow);
			for (int i = cat.Start; i < cat.End; i++)
			{
				string name = monsterNames[i];
				string cnName = (i < monsterNamesCn.Count) ? monsterNamesCn[i] : "";
				string label = string.IsNullOrEmpty(cnName) ? name : name + "（" + cnName + "）";
				int idx = i;
				bool en = IsMonsterEnabled(idx);
				((dc.ui.OptionsBase)options).addToggleWidget(
					StringUtils.AsHaxeString(label),
					StringUtils.AsHaxeString(""),
					(HlFunc<bool>)delegate {
						config.Value.monsterEnabled[name] = !IsMonsterEnabled(idx);
						return config.Value.monsterEnabled[name];
					},
					new Ref<bool>(ref en),
					((dc.ui.OptionsBase)options).scrollerFlow);
				// BOSS 开关下方直接附上该 Boss 的技能键位介绍
				int docIdx = i - 19;
				if (docIdx >= 0 && docIdx < bossSkillDocs.Length)
				{
					((dc.ui.OptionsBase)options).addSeparator(
						StringUtils.AsHaxeString(bossSkillDocs[docIdx]),
						((dc.ui.OptionsBase)options).scrollerFlow);
				}
			}
		}

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
			// Keep the (hidden) hero a few tiles in front of the mob.
			// Many boss skills aim/turn using aTarget.cx < self.cx; with the
			// hero exactly on top of the boss those checks degenerate and the
			// skills fire in the wrong direction.
			((Entity)hero).cx = e.cx + e.dir * 1;
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
			if (!IsMonsterEnabled(index)) return;
			Utils.log("PlayableMOB: CreateByIndex index=" + index
				+ " name=" + (index < monsterNames.Count ? monsterNames[index] : "?"));
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

	// ------------------------------------------------------------------
	// Save safety: a custom mod entity (e.g. HeroKingsHand) has no hxbit
	// CLID, so serializing it into a save file makes HxbitModule abort the
	// game to protect the save. Destroy the active monster before any save
	// so the transformation never leaks into the save data.
	// ------------------------------------------------------------------
	void IOnBeforeSavingSave.OnBeforeSavingSave(IOnBeforeSavingSave.EventData data)
	{
		if (AnyAlive)
		{
			Utils.log("PlayableMOB: destroying active monster before save");
			DestroyCurrent();
		}
		if (overridden) enableHero();
	}

	void IOnAfterLoadingSave.OnAfterLoadingSave(dc.User data)
	{
		// Never keep a monster reference across a save load; each monster
		// create() re-checks its own singleton anyway.
		activeMonster = null;
		overridden = false;
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
			currentIndex = FindNextEnabled(currentIndex, -1);
			if (roomAt != null) CreateByIndex(hero, currentIndex);
			((Entity)hero).set_sprAlpha(1.0);
		}
		// Key 2: next monster
		if (Utils.pressed(config.Value.enforcer.bindings["cycleNext"]))
		{
			Utils.log("PlayableMOB: cycleNext pressed, currentIndex=" + currentIndex);
			DestroyCurrent();
			currentIndex = FindNextEnabled(currentIndex, 1);
			if (roomAt != null) CreateByIndex(hero, currentIndex);
			((Entity)hero).set_sprAlpha(1.0);
		}
		// Key P: toggle (destroy or create current)
		if (Utils.pressed(config.Value.enforcer.bindings["toggle"]))
		{
			if (AnyAlive) { DestroyCurrent(); ((Entity)hero).set_sprAlpha(1.0); }
			else if (roomAt != null && IsMonsterEnabled(currentIndex)) CreateByIndex(hero, currentIndex);
		}

		if (activeMonster != null && !activeMonster.destroyed) heroTrack(activeMonster);
	}
}
