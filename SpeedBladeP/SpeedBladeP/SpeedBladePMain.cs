using dc;
using dc.en;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;

namespace SpeedBladeP;

public class SpeedBladePMain : ModBase, IOnGameExit, IOnHeroUpdate
{
	private int _comboHits;
	private int _lastCycle = -1;
	private double _comboTimeout;
	private double _lastLife;
	private const double ComboTimeoutSec = 3.0;
	private const int SpeedBuffHits = 5;
	private const double SpeedBuffDuration = 6.0;
	private const double SpeedPerHit = 0.20;
	private const double SizePerHit = 0.05;
	private const double MaxSize = 2.0;
	private const double SizeResetSpeed = 3.0;
	private double _origWidPx = -1, _origHeiPx = -1;

	public SpeedBladePMain(ModInfo info) : base(info) { }

	public override void Initialize()
	{
		base.Initialize();
		System.Console.WriteLine("[SpeedBladeP] Loaded");
	}

	void IOnHeroUpdate.OnHeroUpdate(double dt)
	{
		Hero? hero = Game.Instance.HeroInstance;
		if (hero?.weaponsManager == null) return;

		var weap = hero.weaponsManager.lastWeaponUsed;
		if (weap is not SpeedBlade sb) { _comboHits = 0; _lastCycle = -1; return; }

		// Reset combo on damage taken
		double curLife = ((Entity)hero).life;
		if (curLife < _lastLife) { _comboHits = 0; _lastCycle = -1; }
		_lastLife = curLife;

		// Apply attack speed continuously (prepare() overwrites each attack)
		sb._attackSpeed = 1.0 + (_comboHits * SpeedPerHit);

		// Model size + attack range grow with combo
		double targetScale = 1.0 + (_comboHits * SizePerHit);
		if (targetScale > MaxSize) targetScale = MaxSize;
		double curScale = ((Entity)hero).sprScaleX;
		if (curScale < targetScale)
		{
			double ns = curScale + SizeResetSpeed * dt;
			if (ns > targetScale) ns = targetScale;
			((Entity)hero).sprScaleX = ns;
			((Entity)hero).sprScaleY = ns;
		}
		else if (_comboHits == 0 && curScale > 1.0)
		{
			double ns = curScale - SizeResetSpeed * dt;
			if (ns < 1.0) ns = 1.0;
			((Entity)hero).sprScaleX = ns;
			((Entity)hero).sprScaleY = ns;
		}

		// Scale weapon attack areas with combo
		try
		{
			dynamic areas = sb.areas;
			if (areas != null)
			{
				double areaScale = 1.0 + (_comboHits * SizePerHit);
				if (areaScale > MaxSize) areaScale = MaxSize;
				int areaCount = ((dynamic)areas).length;
				for (int i = 0; i < areaCount; i++)
				{
					dynamic a = ((dynamic)areas).getDyn(i);
					if (a == null) continue;
					if (_origWidPx < 0) { _origWidPx = a.widPx; _origHeiPx = a.heiPx; }
					if (_origWidPx > 0) { a.widPx = _origWidPx * areaScale; a.heiPx = _origHeiPx * areaScale; }
				}
				// Restore on reset
				if (_comboHits == 0 && _origWidPx > 0)
				{
					for (int i = 0; i < areaCount; i++)
					{
						dynamic a = ((dynamic)areas).getDyn(i);
						if (a != null) { a.widPx = _origWidPx; a.heiPx = _origHeiPx; }
					}
				}
			}
		}
		catch { }

		int cycle = sb.get_cycle();
		int detectRange = 6 + _comboHits; // range grows with combo

		// Combo hit: cycle advanced or wrapped + enemy nearby
		if (cycle != _lastCycle && _lastCycle >= 0 && (cycle > _lastCycle || cycle == 0))
		{
			if (DidHitEnemy(hero, detectRange))
			{
				_comboHits++;
				_comboTimeout = ComboTimeoutSec;

				if (_comboHits >= SpeedBuffHits && !hero.hasAnySpeedBuff())
				{
					((Entity)hero).setAffectS(72, SpeedBuffDuration, Ref<double>.Null, null);
				}
			}
		}
		_lastCycle = cycle;

		// Combo timeout
		if (_comboTimeout > 0)
		{
			_comboTimeout -= dt;
			if (_comboTimeout <= 0) { _comboHits = 0; _lastCycle = -1; }
		}
	}

	private static bool DidHitEnemy(Hero hero, int range)
	{
		var level = ((Entity)hero)._level;
		if (level == null) return false;
		dynamic entities = level.entities;
		int len = 0; try { len = ((dynamic)entities).length; } catch { return false; }
		for (int i = 0; i < len; i++)
		{
			dynamic e; try { e = ((dynamic)entities).getDyn(i); } catch { continue; }
			if (e is not Mob mob) continue;
			if (mob.life <= 0 || mob.destroyed) continue;
			int dx = hero.cx - mob.cx;
			int dy = hero.cy - mob.cy;
			if (dx * dx + dy * dy <= range) return true;
		}
		return false;
	}

	void IOnGameExit.OnGameExit() { }
}
