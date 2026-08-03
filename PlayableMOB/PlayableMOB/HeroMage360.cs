using System;
using System.Collections.Generic;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.hero;
using dc.en.mob;
using dc.level;
using dc.pr;
using dc.tool.skill;

namespace PlayableMOB;

public class HeroMage360 : Mage360
{
	private MobState curState = MobState.Idle;
	private double stateCd = 0.0;
	private int jumpHoldFrames = 0;

	public static HeroMage360? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? shootSkill;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			Level lvl = dc.pr.Game.Class.ME.curLevel;
			if (lvl == null) return;
			HeroMage360 mage = new HeroMage360(lvl, ((Entity)hero).cx, ((Entity)hero).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)mage).dir = ((Entity)hero).dir;
			// Go through Entity.init → Mage360.init (loads tracks) → Mob.init
			((Entity)mage).init();
			// Then player-specific setup
			mage.playerInit();
		}
	}

	public HeroMage360(Level lvl, int x, int y, int dmgTier, int lifeTier)
		: base(lvl, x, y, dmgTier, lifeTier)
	{
	}

	public void reset()
	{
		curState = MobState.Idle;
		stateCd = 0.0;
		jumpHoldFrames = 0;
		if (shootSkill != null) shootSkill.coolDownF = 0.0;
		if (base.dodge != null) base.dodge.coolDownF = 0.0;
	}

	public override void onReload()
	{
		dc.tool.Cooldown cd = ((Entity)this).cd;
		if (cd != null)
			cd.init((HlAction<dc.String, int>)((Entity)this).onCooldownEnd);
		((Entity)this).init();
		playerInit();
		if (!PlayableMOB.config.Value.enabled)
			((Entity)this).destroy();
	}

	public void playerInit()
	{

		// Get shoot skill from end of oldSkills array
		if (base.oldSkills != null)
		{
			dynamic skills = base.oldSkills;
			int total = 0;
			try { total = ((dynamic)skills).length; } catch { }
			if (total >= 3)
			{
				try { shootSkill = (OldMobSkill)(dynamic)skills.getDyn(total - 1); } catch { }
			}
		}

		// Set team
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);

		// Hijack
		if (shootSkill != null)
		{
			var origInt = shootSkill.dynOnInterrupt;
			shootSkill.dynOnInterrupt = delegate(double r) { origInt?.Invoke(r); reset(); };
			var origExec = shootSkill.dynOnExecute;
			shootSkill.dynOnExecute = delegate(double r) { origExec?.Invoke(r); curState = MobState.ShieldSlash; stateCd = 0.8; };
		}
		if (base.dodge != null)
		{
			var origInt = base.dodge.dynOnInterrupt;
			base.dodge.dynOnInterrupt = delegate(double r) { origInt?.Invoke(r); reset(); };
			var origExec = base.dodge.dynOnExecute;
			base.dodge.dynOnExecute = delegate(double r) { origExec?.Invoke(r); curState = MobState.ShieldBash; stateCd = 0.3; };
		}

		reset();
		inst = this;
		PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();

		if (!PlayableMOB.config.Value.enabled) return;
		if (curState == MobState.Dead) return;
		if (((Entity)this).isUnconscious()) { reset(); return; }

		bool anyCharging = (shootSkill != null && shootSkill.chargeF > 0.0)
			|| (base.dodge != null && base.dodge.chargeF > 0.0);
		if (!anyCharging && curState != MobState.Idle)
		{
			stateCd -= 1.0 / 60.0;
			if (stateCd <= 0.0) reset();
		}

		if (Utils.pressed(keys["skill1"]) || Utils.pressed(keys["skill2"]))
		{
			if (shootSkill != null) shootSkill.coolDownF = 0.0;
			if (base.dodge != null) base.dodge.coolDownF = 0.0;
		}

		if (curState == MobState.Idle)
		{
			if (Utils.pressed(keys["skill1"]) && shootSkill != null)
				shootSkill.prepare(null);
			if (Utils.pressed(keys["skill2"]) && base.dodge != null)
				base.dodge.prepare(null);
		}

		if (!((Entity)this).moveBlocked())
		{
			if (Utils.held(keys["right"])) { ((Entity)this).dir = 1; ((Entity)this).dx = 0.15 * base.getMoveSpeedMul(); }
			else if (Utils.held(keys["left"])) { ((Entity)this).dir = -1; ((Entity)this).dx = -0.15 * base.getMoveSpeedMul(); }
		}

		bool onGround = ((Entity)this).cy == ((Entity)this)._level.map.getGroundY(((Entity)this).cx, ((Entity)this).cy);
		if (Utils.pressed(keys["jump"]) && onGround) { ((Entity)this).dy = -0.5; jumpHoldFrames = 8; }
		if (Utils.held(keys["jump"]) && jumpHoldFrames > 0 && ((Entity)this).dy < 0.0) { ((Entity)this).dy = ((Entity)this).dy - 0.06; jumpHoldFrames--; }
		if (!Utils.held(keys["jump"])) jumpHoldFrames = 0;
		if (Utils.held(keys["down"]) && onGround) ((Entity)this).dx = 0.0;
	}

	public override void onDie()
	{
		curState = MobState.Dead;
		shootSkill?.interrupt();
		base.dodge?.interrupt();
		base.onDie();
	}

	public override void destroy()
	{
		inst = null;
		base.destroy();
	}
}
