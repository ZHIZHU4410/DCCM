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

public class HeroFatZombie : FatZombie
{
	private int jumpHoldFrames;
	public static HeroFatZombie? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? rollSkill, jumpSkill;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			var m = new HeroFatZombie(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)m).dir = ((Entity)hero).dir;
			((Entity)m).init();
			m.playerInit();
		}
	}

	public HeroFatZombie(Level lvl, int x, int y, int dmgTier, int lifeTier) : base(lvl, x, y, dmgTier, lifeTier) { }

	public void playerInit()
	{
		Utils.mobInit((dc.en.Mob)this);
		if (base.oldSkills != null)
		{
			dynamic s = base.oldSkills;
			int t = 0; try { t = ((dynamic)s).length; } catch { }
			try { rollSkill = (OldMobSkill)(dynamic)s.getDyn(t - 2); } catch { }
			try { jumpSkill = (OldMobSkill)(dynamic)s.getDyn(t - 1); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		Hijack(rollSkill); Hijack(jumpSkill);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	static void Hijack(OldMobSkill? sk) { if (sk == null) return; var o = sk.dynOnExecute; sk.dynOnExecute = r => o?.Invoke(r); }

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;

		if (Utils.pressed(keys["skill1"]) && rollSkill != null) { rollSkill.coolDownF = 0; rollSkill.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && jumpSkill != null) { jumpSkill.coolDownF = 0; jumpSkill.prepare(null); }

		MonsterMovement.Apply((Entity)this, this, keys, ref jumpHoldFrames);
	}

	public override void destroy() { inst = null; base.destroy(); }
}
