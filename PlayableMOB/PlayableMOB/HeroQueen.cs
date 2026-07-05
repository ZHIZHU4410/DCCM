using System.Collections.Generic;
using HaxeProxy.Runtime;
using dc; using dc.en; using dc.en.hero; using dc.en.mob; using dc.en.mob.boss; using dc.level; using dc.pr; using dc.tool.skill;

namespace PlayableMOB;

public class HeroQueen : Queen
{
	private int jhf;
	public static HeroQueen? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? quickHigh, lunge, firewave, shockWave, grabHero;

	public static void create(Hero h)
	{
		if (inst == null)
		{
			try
			{
				var m = new HeroQueen(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, 38, 38);
				((Entity)m).dir = ((Entity)h).dir;
				Utils.bossInit((dc.en.mob.Boss)m);
				try { m.playerInit(); } catch { m.destroy(); return; }
			}
			catch { return; }
		}
	}
	public HeroQueen(Level lv, int x, int y, int dt, int lt) : base(lv, x, y, dt, lt) { }

	public void playerInit()
	{
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { grabHero  = (OldMobSkill)(dynamic)s.getDyn(t-1); } catch { }
			try { shockWave = (OldMobSkill)(dynamic)s.getDyn(t-2); } catch { }
			try { firewave  = (OldMobSkill)(dynamic)s.getDyn(t-3); } catch { }
			try { lunge     = (OldMobSkill)(dynamic)s.getDyn(t-4); } catch { }
			try { quickHigh = (OldMobSkill)(dynamic)s.getDyn(t-5); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && quickHigh != null) { quickHigh.coolDownF = 0; quickHigh.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && lunge != null)     { lunge.coolDownF = 0; lunge.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && firewave != null)  { firewave.coolDownF = 0; firewave.prepare(null); }
		if (Utils.pressed(keys["skill4"]) && shockWave != null) { shockWave.coolDownF = 0; shockWave.prepare(null); }
		if (Utils.pressed(keys["skill5"]) && grabHero != null)  { grabHero.coolDownF = 0; grabHero.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
