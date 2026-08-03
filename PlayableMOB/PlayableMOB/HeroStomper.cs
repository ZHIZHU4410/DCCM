using System.Collections.Generic;
using HaxeProxy.Runtime;
using dc; using dc.en; using dc.en.hero; using dc.en.mob; using dc.level; using dc.pr; using dc.tool.skill;

namespace PlayableMOB;

public class HeroStomper : Stomper
{
	private int jhf;
	public static HeroStomper? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? ccSkill;
	// jump is OldSkill at base.jump (dodge-like field) — but Stomper's jump field name differs

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroStomper(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)m).dir = ((Entity)h).dir; ((Entity)m).init(); m.playerInit();
		}
	}
	public HeroStomper(Level lv, int x, int y, int dt, int lt) : base(lv, x, y, dt, lt) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { ccSkill = (OldMobSkill)(dynamic)s.getDyn(t-1); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && ccSkill != null) { ccSkill.coolDownF = 0; ccSkill.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
