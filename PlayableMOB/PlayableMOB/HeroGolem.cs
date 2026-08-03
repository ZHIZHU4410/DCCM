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

public class HeroGolem : Golem
{
	private int jumpHoldFrames;
	public static HeroGolem? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? punchSkill, teleSkill, stunSkill;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			var m = new HeroGolem(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)m).dir = ((Entity)hero).dir;
			((Entity)m).init();
			m.playerInit();
		}
	}

	public HeroGolem(Level lvl, int x, int y, int dmgTier, int lifeTier) : base(lvl, x, y, dmgTier, lifeTier) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null)
		{
			dynamic s = base.oldSkills;
			int t = 0; try { t = ((dynamic)s).length; } catch { }
			try { punchSkill = (OldMobSkill)(dynamic)s.getDyn(t - 3); } catch { }
			try { teleSkill  = (OldMobSkill)(dynamic)s.getDyn(t - 2); } catch { }
			try { stunSkill  = (OldMobSkill)(dynamic)s.getDyn(t - 1); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		Hijack(punchSkill); Hijack(teleSkill); Hijack(stunSkill);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	static void Hijack(OldMobSkill? sk) { if (sk == null) return; var o = sk.dynOnExecute; sk.dynOnExecute = r => o?.Invoke(r); }

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;

		if (Utils.pressed(keys["skill1"]) && punchSkill != null) { punchSkill.coolDownF = 0; punchSkill.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && teleSkill != null)  { teleSkill.coolDownF = 0; teleSkill.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && stunSkill != null)  { stunSkill.coolDownF = 0; stunSkill.prepare(null); }

		MonsterMovement.Apply((Entity)this, this, keys, ref jumpHoldFrames);
	}

	public override void destroy() { inst = null; base.destroy(); }
}
