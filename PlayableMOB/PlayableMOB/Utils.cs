using System;
using System.Collections.Generic;
using ModCore;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.hero;
using dc.hl;
using dc.hxd;
using dc.hxd.res;
using dc.level;
using dc.libs;
using dc.libs.data;
using dc.libs.heaps.slib;
using dc.libs.misc;
using dc.pr;
using dc.tool;

namespace PlayableMOB;

public static class Utils
{
	public static Random random = new Random();

	public static bool[] VKB = new bool[255];

	/// <summary>
	/// Tier used when creating a transformed monster, derived from the hero's
	/// current scroll count. The monster constructor maps this tier to both
	/// damage (dmgTier -> mob stat tiers -> sourceTier in AttackData) and
	/// life (lifeTier -> trueLifeTier -> scaleMobLifeToTier), so the monster's
	/// damage and max HP grow with the hero's scrolls.
	/// </summary>
	public static int ScrollTier()
	{
		try
		{
			Hero hero = dc.pr.Game.Class.ME.hero;
			if (hero == null) return 1;
			int scrolls = hero.brutalityTier + hero.tacticTier + hero.survivalTier;
			double factor = PlayableMOB.config.Value.scrollPowerFactor;
			if (factor <= 0) factor = 1.0;
			return System.Math.Max(1, (int)System.Math.Round(scrolls * factor));
		}
		catch
		{
			return 1;
		}
	}

	/// <summary>Mob damage curve: base * 1.09^(t-1) * (1 + 0.55*(t-1)).</summary>
	private static double DamageCurve(double tier)
	{
		double n = tier - 1;
		return System.Math.Pow(1.09, n) * (1.0 + 0.55 * n);
	}

	/// <summary>Mob life curve: base * 1.08^(t-1) * (1 + 0.12*(t-1)).</summary>
	private static double LifeCurve(double tier)
	{
		double n = tier - 1;
		return System.Math.Pow(1.08, n) * (1.0 + 0.12 * n);
	}

	/// <summary>
	/// Finds the tier whose curve value equals the base tier's curve value
	/// multiplied by <paramref name="multiplier"/> (exact 10x boost etc.).
	/// </summary>
	private static int TierForMultiplier(double baseTier, double multiplier, bool damage)
	{
		double target = (damage ? DamageCurve(baseTier) : LifeCurve(baseTier)) * multiplier;
		int lo = 1, hi = 200;
		while (lo < hi)
		{
			int mid = (lo + hi) / 2;
			double v = damage ? DamageCurve(mid) : LifeCurve(mid);
			if (v >= target) hi = mid;
			else lo = mid + 1;
		}
		return lo;
	}

	/// <summary>Damage tier: scroll tier boosted so final damage is x10 (configurable).</summary>
	public static int DamageTier()
	{
		return TierForMultiplier(ScrollTier(), PlayableMOB.config.Value.damageMultiplier, true);
	}

	/// <summary>Life tier: scroll tier boosted so final max life is x10 (configurable).</summary>
	public static int LifeTier()
	{
		return TierForMultiplier(ScrollTier(), PlayableMOB.config.Value.lifeMultiplier, false);
	}

	public static void log(string str)
	{
		PlayableMOB? inst = PlayableMOB.inst;
		if (inst != null)
		{
			((Module)inst).Logger.Information(str);
		}
	}

	public static dc.String getI18n(string text)
	{
		return Lang.Class.t.get(StringUtils.AsHaxeString(text), (object)null);
	}

	public static void setI18n(GetText gt, string lang)
	{
		string key = lang;
		if (!I18n.text.ContainsKey(key))
		{
			key = "en";
		}
		foreach (var (text3, text4) in I18n.text[key])
		{
			gt.texts.set(StringUtils.AsHaxeString(text3), (object)StringUtils.AsHaxeString(text4));
		}
	}

	public static void VK_press(KeyBind vk)
	{
		if (vk.primary.HasValue)
		{
			VKB[vk.primary.Value] = true;
		}
		else if (vk.secondary.HasValue)
		{
			VKB[vk.secondary.Value] = true;
		}
		else if (vk.third.HasValue)
		{
			VKB[vk.third.Value] = true;
		}
	}

	public static void VK_release(KeyBind vk)
	{
		if (vk.primary.HasValue)
		{
			VKB[vk.primary.Value] = false;
		}
		else if (vk.secondary.HasValue)
		{
			VKB[vk.secondary.Value] = false;
		}
		else if (vk.third.HasValue)
		{
			VKB[vk.third.Value] = false;
		}
	}

	/// <summary>
	/// Instantly re-anchors the hidden hero a few tiles in front of the mob.
	/// Boss skills call lookAt(hero) / check hero.cx vs own cx at cast time;
	/// the per-frame hero tracking lags one frame behind a recent turn, which
	/// makes those skills flip and hit behind. Syncing right before prepare()
	/// fixes the direction.
	/// </summary>
	public static void SyncHeroToFront(Entity monster)
	{
		try
		{
			Hero h = dc.pr.Game.Class.ME.hero;
			if (h == null || ((Entity)h).destroyed || monster == null || monster.destroyed) return;
			((Entity)h).cx = monster.cx + monster.dir * 1;
			((Entity)h).cy = monster.cy;
			((Entity)h).dir = monster.dir;
		}
		catch { }
	}

	public static bool pressed(KeyBind vk)
	{
		return vk != null && ((vk.primary.HasValue && (Key.Class.isPressed.Invoke(vk.primary.Value) || VKB[vk.primary.Value])) || (vk.secondary.HasValue && (Key.Class.isPressed.Invoke(vk.secondary.Value) || VKB[vk.secondary.Value])) || (vk.third.HasValue && (Key.Class.isPressed.Invoke(vk.third.Value) || VKB[vk.third.Value])));
	}

	public static bool held(KeyBind vk)
	{
		return vk != null && ((vk.primary.HasValue && (Key.Class.isDown.Invoke(vk.primary.Value) || VKB[vk.primary.Value])) || (vk.secondary.HasValue && (Key.Class.isDown.Invoke(vk.secondary.Value) || VKB[vk.secondary.Value])) || (vk.third.HasValue && (Key.Class.isDown.Invoke(vk.third.Value) || VKB[vk.third.Value])));
	}

	public static void mobInit(dc.en.Mob e)
	{
		((Entity)e).delayer = new Delayer(60.0);
		((Entity)e).tw = new Tweenie(60.0);
		((Entity)e).createAttackSource();
		((Entity)e).createAttackTarget();
		((Entity)e).initGfx();
		((Entity)e).initClonesGfx();
		if (((Entity)e)._level != null && ((Entity)e)._level.minimap != null && !((Process)((Entity)e)._level.minimap).destroyed)
		{
			((Entity)e).minimapTracking();
		}
		((Entity)e).initDone = true;
		((Entity)e).isOnScreen = false;
		((Entity)e).isOutOfGame = true;
		if (((Entity)e).isInQuadTree())
		{
			((Entity)e)._level.qTree.tryInsert(((Entity)e).cx, ((Entity)e).cy, (Entity)(object)e);
		}
		e.initCDBData();
		e.baseMoveSpeedMul = (e._infos.props.moveSpeedMul.HasValue ? e._infos.props.moveSpeedMul.Value : 1.0);
		e.baseMovePauseMul = (e._infos.props.movePauseMul.HasValue ? e._infos.props.movePauseMul.Value : 1.0);
		if (e._infos != null && e._infos.glowInnerColor.HasValue)
		{
			int value = e._infos.glowInnerColor.Value;
			int? num = null;
			try
			{
				num = e._infos.glowOuterColor;
			}
			catch
			{
			}
			((Entity)e).setGlowColor(value, num, (double?)null, (HSprite)null);
		}
		e.initSkills();
		e.initMove();
		if (DLC.Class.mobIsPressHidden.Invoke(e.type))
		{
			((Entity)e).destroy();
		}
		if (e.canApplyColorSwap())
		{
			e.applyColorSwap();
		}
		((Entity)e).onSprAlphaChanged = e.onMobAlphaChanged;
		((Entity)e).removeAllAffects(5);
	}

	public static void bossInit(dc.en.mob.Boss e, bool skipMove = false)
	{
		((Entity)e).delayer = new Delayer(60.0);
		((Entity)e).tw = new Tweenie(60.0);
		((Entity)e).createAttackSource();
		((Entity)e).createAttackTarget();
		((Entity)e).initGfx();
		((Entity)e).initClonesGfx();
		if (((Entity)e)._level != null && ((Entity)e)._level.minimap != null && !((Process)((Entity)e)._level.minimap).destroyed)
			((Entity)e).minimapTracking();
		((Entity)e).initDone = true;
		((Entity)e).isOnScreen = false;
		((Entity)e).isOutOfGame = true;
		if (((Entity)e).isInQuadTree())
			((Entity)e)._level.qTree.tryInsert(((Entity)e).cx, ((Entity)e).cy, (Entity)(object)e);
		((dc.en.Mob)e).initCDBData();
		((dc.en.Mob)e).baseMoveSpeedMul = (((dc.en.Mob)e)._infos.props.moveSpeedMul.HasValue ? ((dc.en.Mob)e)._infos.props.moveSpeedMul.Value : 1.0);
		((dc.en.Mob)e).baseMovePauseMul = (((dc.en.Mob)e)._infos.props.movePauseMul.HasValue ? ((dc.en.Mob)e)._infos.props.movePauseMul.Value : 1.0);
		if (((dc.en.Mob)e)._infos != null && ((dc.en.Mob)e)._infos.glowInnerColor.HasValue)
		{
			int value = ((dc.en.Mob)e)._infos.glowInnerColor.Value;
			int? num = null;
			try { num = ((dc.en.Mob)e)._infos.glowOuterColor; } catch { }
			((Entity)e).setGlowColor(value, num, (double?)null, (HSprite)null);
		}
		((dc.en.Mob)e).initSkills();
		if (skipMove)
		{
			// Some bosses (DookuBeast) override initMove() with arena-only
			// code that dereferences null fields outside their fight (e.g.
			// rseed / combatRoom). Fall back to the standard ground mover.
			try { ((dc.en.Mob)e).move = new dc.tool.mv.MobWalk((dc.en.Mob)e); } catch { }
		}
		else
		{
			((dc.en.Mob)e).initMove();
		}
		if (DLC.Class.mobIsPressHidden.Invoke(((dc.en.Mob)e).type))
			((Entity)e).destroy();
		if (((dc.en.Mob)e).canApplyColorSwap())
			((dc.en.Mob)e).applyColorSwap();
		((Entity)e).onSprAlphaChanged = ((dc.en.Mob)e).onMobAlphaChanged;
		// Boss-specific: get boss room (may return current room if no boss room)
		try { e.bossRoom = e.getBossRoom(); } catch { e.bossRoom = ((Entity)e)._level.map.getRoomAt(((Entity)e).cx, ((Entity)e).cy); }
		((dc.en.Mob)e).removeFlawlessLoots();
		e.cameraTrackingDisabled = true;
		e.ready = true;
		((Entity)e).removeAllAffects(5);
	}

	public static dc.level._LevelAudio.Event playEvent(string path)
	{
		dc.level.LevelAudio lAudio = Game.Class.ME.curLevel.lAudio;
		dc.hxd.res.Loader val = Res.Class.get_loader.Invoke();
		dc.hxd.res.Resource val2 = val.loadCache(StringUtils.AsHaxeString(path), (Class)(object)Sound.Class);
		return lAudio.playEvent((Sound)val2, (double?)null, (double?)null, (dc.String?)null);
	}

	public static dc.level._LevelAudio.Event playEventOn(string path, Entity e)
	{
		dc.level.LevelAudio lAudio = Game.Class.ME.curLevel.lAudio;
		dc.hxd.res.Loader val = Res.Class.get_loader.Invoke();
		dc.hxd.res.Resource val2 = val.loadCache(StringUtils.AsHaxeString(path), (Class)(object)Sound.Class);
		return lAudio.playEventOn((Sound)val2, e, (double?)null, (double?)null, (dc.String?)null);
	}

	public static Marker copyMarker(Marker m)
	{
		if (m == null)
		{
			return new Marker((dc.String?)null, 0, 0, 0, 0, 0.0, 0.0, 0, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, (dc.String?)null, false, (dc.String?)null);
		}
		return new Marker(m.kind, m.cx, m.cy, m.width, m.height, m.xr, m.yr, m.dir, m.customId, m.itemId, m.lightId, m.mobId, m.levelId, m.layerId, m.offset, m.playMode, m.playSpeed, m.blendMode, m.color, m.color2, m.ignoreTwitch, m.rotation);
	}
}
