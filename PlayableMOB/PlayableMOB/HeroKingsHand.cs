using System.Collections.Generic;
using HaxeProxy.Runtime;
using dc; using dc.en; using dc.en.hero; using dc.en.mob; using dc.en.mob.boss; using dc.level; using dc.pr; using dc.tool.skill;

namespace PlayableMOB;

public class HeroKingsHand : KingsHand
{
	private int jhf;
	public static HeroKingsHand? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? s1, s2, s3, s4, s5;

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroKingsHand(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, 38, 38);
			((Entity)m).dir = ((Entity)h).dir;
			Utils.bossInit((dc.en.mob.Boss)m); m.playerInit();
		}
	}
	public HeroKingsHand(Level lv, int x, int y, int dt, int lt) : base(lv, x, y, dt, lt) { }

	public void playerInit()
	{
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { s5 = (OldMobSkill)(dynamic)s.getDyn(t-1); } catch { }
			try { s4 = (OldMobSkill)(dynamic)s.getDyn(t-2); } catch { }
			try { s3 = (OldMobSkill)(dynamic)s.getDyn(t-3); } catch { }
			try { s2 = (OldMobSkill)(dynamic)s.getDyn(t-4); } catch { }
			try { s1 = (OldMobSkill)(dynamic)s.getDyn(t-5); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		Hijack(s1); Hijack(s2); Hijack(s3); Hijack(s4); Hijack(s5);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	static void Hijack(OldMobSkill? sk) { if (sk == null) return; var o = sk.dynOnExecute; sk.dynOnExecute = r => o?.Invoke(r); }

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && s1 != null) { s1.coolDownF = 0; s1.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && s2 != null) { s2.coolDownF = 0; s2.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && s3 != null) { s3.coolDownF = 0; s3.prepare(null); }
		if (Utils.pressed(keys["skill4"]) && s4 != null) { s4.coolDownF = 0; s4.prepare(null); }
		if (Utils.pressed(keys["skill5"]) && s5 != null) { s5.coolDownF = 0; s5.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
