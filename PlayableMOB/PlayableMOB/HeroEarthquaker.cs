using System.Collections.Generic;
using HaxeProxy.Runtime;
using dc; using dc.en; using dc.en.hero; using dc.en.mob; using dc.level; using dc.pr; using dc.tool.skill;

namespace PlayableMOB;

public class HeroEarthquaker : Earthquaker
{
	private int jhf;
	public static HeroEarthquaker? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? cc1, cc2, cc3, cc4, quake;

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroEarthquaker(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, 38, 38);
			((Entity)m).dir = ((Entity)h).dir; ((Entity)m).init(); m.playerInit();
		}
	}
	public HeroEarthquaker(Level lv, int x, int y, int dt, int lt) : base(lv, x, y, dt, lt) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { quake = (OldMobSkill)(dynamic)s.getDyn(t-1); } catch { }
			try { cc4   = (OldMobSkill)(dynamic)s.getDyn(t-2); } catch { }
			try { cc3   = (OldMobSkill)(dynamic)s.getDyn(t-3); } catch { }
			try { cc2   = (OldMobSkill)(dynamic)s.getDyn(t-4); } catch { }
			try { cc1   = (OldMobSkill)(dynamic)s.getDyn(t-5); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && cc1 != null) { cc1.coolDownF = 0; cc1.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && cc2 != null) { cc2.coolDownF = 0; cc2.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && cc3 != null) { cc3.coolDownF = 0; cc3.prepare(null); }
		if (Utils.pressed(keys["skill4"]) && cc4 != null) { cc4.coolDownF = 0; cc4.prepare(null); }
		if (Utils.pressed(keys["skill5"]) && quake != null) { quake.coolDownF = 0; quake.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
