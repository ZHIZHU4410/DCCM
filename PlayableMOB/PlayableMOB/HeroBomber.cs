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

public class HeroBomber : Bomber
{
	private int jumpHoldFrames;
	public static HeroBomber? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? ccSkill, bombSkill, diveSkill;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			var m = new HeroBomber(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, 38, 38);
			((Entity)m).dir = ((Entity)hero).dir;
			((Entity)m).init();
			m.playerInit();
		}
	}

	public HeroBomber(Level lvl, int x, int y, int dmgTier, int lifeTier) : base(lvl, x, y, dmgTier, lifeTier) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null)
		{
			dynamic s = base.oldSkills;
			int t = 0; try { t = ((dynamic)s).length; } catch { }
			try { ccSkill   = (OldMobSkill)(dynamic)s.getDyn(t - 3); } catch { }
			try { bombSkill = (OldMobSkill)(dynamic)s.getDyn(t - 2); } catch { }
			try { diveSkill = (OldMobSkill)(dynamic)s.getDyn(t - 1); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		Hijack(ccSkill); Hijack(bombSkill); Hijack(diveSkill);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	static void Hijack(OldMobSkill? sk) { if (sk == null) return; var o = sk.dynOnExecute; sk.dynOnExecute = r => o?.Invoke(r); }

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;

		if (Utils.pressed(keys["skill1"]) && ccSkill != null)   { ccSkill.coolDownF = 0; ccSkill.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && bombSkill != null) { bombSkill.coolDownF = 0; bombSkill.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && diveSkill != null) { diveSkill.coolDownF = 0; diveSkill.prepare(null); }

		MonsterMovement.Apply((Entity)this, this, keys, ref jumpHoldFrames);
	}

	public override void destroy() { inst = null; base.destroy(); }
}
