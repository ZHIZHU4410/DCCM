using System;
using System.Collections.Generic;
using ModCore;
using ModCore.Utilities;
using dc;
using dc.en;
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
