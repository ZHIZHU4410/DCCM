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

public class HeroShield : Shield
{
	private int jumpHoldFrames;
	public static HeroShield? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? dashSkill;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			var m = new HeroShield(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, 38, 38);
			((Entity)m).dir = ((Entity)hero).dir;
			((Entity)m).init();
			m.playerInit();
		}
	}

	public HeroShield(Level lvl, int x, int y, int dmgTier, int lifeTier) : base(lvl, x, y, dmgTier, lifeTier) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null)
		{
			dynamic s = base.oldSkills;
			try { dashSkill = (OldMobSkill)(dynamic)s.getDyn(((dynamic)s).length - 1); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		if (dashSkill != null)
		{
			var oi = dashSkill.dynOnInterrupt; dashSkill.dynOnInterrupt = r => { oi?.Invoke(r); };
			var oe = dashSkill.dynOnExecute; dashSkill.dynOnExecute = r => oe?.Invoke(r);
		}
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;

		// Skill 1: dash
		if (Utils.pressed(keys["skill1"]) && dashSkill != null)
		{
			dashSkill.coolDownF = 0.0;
			dashSkill.prepare(null);
		}

		MonsterMovement.Apply((Entity)this, this, keys, ref jumpHoldFrames);
	}

	public override void destroy() { inst = null; base.destroy(); }
}
