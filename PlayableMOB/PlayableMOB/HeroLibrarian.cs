using System.Collections.Generic;
using HaxeProxy.Runtime;
using dc;
using dc.en;
using dc.en.hero;
using dc.en.mob;
using dc.level;
using dc.pr;
using dc.tool.skill;

namespace PlayableMOB;

public class HeroLibrarian : Librarian
{
	private int jhf;
	public static HeroLibrarian? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;
	private OldSkill? s1; // Librarian uses OldSkill, not OldMobSkill

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroLibrarian(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, 38, 38);
			((Entity)m).dir = ((Entity)h).dir;
			((Entity)m).init(); m.playerInit();
		}
	}
	public HeroLibrarian(Level l, int x, int y, int d, int lt) : base(l, x, y, d, lt) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { s1 = (OldSkill)(dynamic)s.getDyn(t - 1); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		if (s1 != null) { var o = s1.dynOnExecute; s1.dynOnExecute = r => o?.Invoke(r); }
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}
	public override void fixedUpdate() { base.fixedUpdate(); if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && s1 != null) { s1.coolDownF = 0; s1.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
