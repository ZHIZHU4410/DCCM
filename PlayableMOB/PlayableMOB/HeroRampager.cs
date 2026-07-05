using System.Collections.Generic;
using HaxeProxy.Runtime;
using dc; using dc.en; using dc.en.hero; using dc.en.mob; using dc.level; using dc.pr; using dc.tool.skill;

namespace PlayableMOB;

public class HeroRampager : Rampager
{
	private int jhf;
	public static HeroRampager? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? jumpBack, rampage, chain1, chain2;

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroRampager(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, 38, 38);
			((Entity)m).dir = ((Entity)h).dir; ((Entity)m).init(); m.playerInit();
		}
	}
	public HeroRampager(Level lv, int x, int y, int dt, int lt) : base(lv, x, y, dt, lt) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { rampage = (OldMobSkill)(dynamic)s.getDyn(t-1); } catch { }
			try { chain2  = (OldMobSkill)(dynamic)s.getDyn(t-2); } catch { }
			try { chain1  = (OldMobSkill)(dynamic)s.getDyn(t-3); } catch { }
			try { jumpBack= (OldMobSkill)(dynamic)s.getDyn(t-4); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && jumpBack != null) { jumpBack.coolDownF = 0; jumpBack.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && chain1 != null)   { chain1.coolDownF = 0; chain1.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && chain2 != null)   { chain2.coolDownF = 0; chain2.prepare(null); }
		if (Utils.pressed(keys["skill4"]) && rampage != null)  { rampage.coolDownF = 0; rampage.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
