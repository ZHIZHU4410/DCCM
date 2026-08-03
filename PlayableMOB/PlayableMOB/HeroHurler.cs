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

public class HeroHurler : Hurler
{
	private int jhf;
	public static HeroHurler? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;
	private OldMobSkill? s1, s2, s3;

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroHurler(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)m).dir = ((Entity)h).dir;
			((Entity)m).init(); m.playerInit();
		}
	}
	public HeroHurler(Level l, int x, int y, int d, int lt) : base(l, x, y, d, lt) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { s1 = (OldMobSkill)(dynamic)s.getDyn(t - 3); } catch { }
			try { s2 = (OldMobSkill)(dynamic)s.getDyn(t - 2); } catch { }
			try { s3 = (OldMobSkill)(dynamic)s.getDyn(t - 1); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		H(s1); H(s2); H(s3);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}
	static void H(OldMobSkill? sk) { if (sk == null) return; var o = sk.dynOnExecute; sk.dynOnExecute = r => o?.Invoke(r); }
	public override void fixedUpdate() { base.fixedUpdate(); if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && s1 != null) { s1.coolDownF = 0; s1.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && s2 != null) { s2.coolDownF = 0; s2.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && s3 != null) { s3.coolDownF = 0; s3.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
