using System.Collections.Generic;
using HaxeProxy.Runtime;
using dc; using dc.en; using dc.en.hero; using dc.en.mob; using dc.level; using dc.pr; using dc.tool.skill;

namespace PlayableMOB;

public class HeroTick : Tick
{
	private int jhf;
	public static HeroTick? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? highSlash, followHigh, longSlash, followLong, jumpOver;

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroTick(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)m).dir = ((Entity)h).dir;
			// Tick overrides init() — go through Entity.init chain
			((Entity)m).init(); m.playerInit();
		}
	}
	public HeroTick(Level lv, int x, int y, int dt, int lt) : base(lv, x, y, dt, lt) { }

	public void playerInit()
	{
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { jumpOver   = (OldMobSkill)(dynamic)s.getDyn(t-1); } catch { }
			try { followLong = (OldMobSkill)(dynamic)s.getDyn(t-2); } catch { }
			try { longSlash  = (OldMobSkill)(dynamic)s.getDyn(t-3); } catch { }
			try { followHigh = (OldMobSkill)(dynamic)s.getDyn(t-4); } catch { }
			try { highSlash  = (OldMobSkill)(dynamic)s.getDyn(t-5); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && highSlash != null)  { highSlash.coolDownF = 0; highSlash.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && followHigh != null) { followHigh.coolDownF = 0; followHigh.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && longSlash != null)  { longSlash.coolDownF = 0; longSlash.prepare(null); }
		if (Utils.pressed(keys["skill4"]) && followLong != null) { followLong.coolDownF = 0; followLong.prepare(null); }
		if (Utils.pressed(keys["skill5"]) && jumpOver != null)   { jumpOver.coolDownF = 0; jumpOver.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
