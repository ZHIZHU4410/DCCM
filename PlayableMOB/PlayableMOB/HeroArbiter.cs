using System.Collections.Generic;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc; using dc.en; using dc.en.hero; using dc.en.mob; using dc.level; using dc.pr; using dc.tool.skill;

namespace PlayableMOB;

public class HeroArbiter : Arbiter
{
	private int jhf;
	public static HeroArbiter? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? shoot0, shoot1, shoot2;
	// dodge is base.dodge (OldSkill)

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroArbiter(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, 38, 38);
			((Entity)m).dir = ((Entity)h).dir;
			// Arbiter overrides init() to load tracks — go through Entity.init chain
			((Entity)m).init();
			m.playerInit();
		}
	}
	public HeroArbiter(Level lv, int x, int y, int dt, int lt) : base(lv, x, y, dt, lt) { }

	public void playerInit()
	{
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { shoot2 = (OldMobSkill)(dynamic)s.getDyn(t-1); } catch { }
			try { shoot1 = (OldMobSkill)(dynamic)s.getDyn(t-2); } catch { }
			try { shoot0 = (OldMobSkill)(dynamic)s.getDyn(t-3); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && shoot0 != null) { shoot0.coolDownF = 0; shoot0.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && shoot1 != null) { shoot1.coolDownF = 0; shoot1.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && shoot2 != null) { shoot2.coolDownF = 0; shoot2.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
