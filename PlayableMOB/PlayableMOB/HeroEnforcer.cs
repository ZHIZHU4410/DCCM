using System;
using System.Collections.Generic;
using Hashlink.Marshaling;
using Hashlink.Proxy.Objects;
using Hashlink.Virtuals;
using dc.libs.heaps.slib;
using dc.haxe.ds;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.hero;
using dc.en.mob;
using dc.level;
using dc.pr;
using dc.tool;
using dc.tool.skill;

namespace PlayableMOB;

public class HeroEnforcer : Enforcer
{
	private bool canTurn = true;

	private bool canMove = true;

	private MobState curState = MobState.Idle;

	private double stateCd = 0.0;

	private int jumpHoldFrames = 0;

	public static HeroEnforcer? inst { get; private set; }

	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	// Skill references from oldSkills array
	private OldMobSkill? shieldBashSkill;
	private OldMobSkill? shieldedSlashSkill;
	private OldMobSkill? shieldlessSlash1Skill;
	private OldMobSkill? shieldlessSlash2Skill;

	// Saved original callbacks
	private HlFunc<bool>? shieldBash_origCanUse;
	private HlFunc<bool>? shieldedSlash_origCanUse;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			HeroEnforcer heroEnforcer = new HeroEnforcer(Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)heroEnforcer).dir = ((Entity)hero).dir;
			((Entity)heroEnforcer).init();
		}
	}

	public HeroEnforcer(Level lvl, int x, int y, int dmgTier, int lifeTier)
		: base(lvl, x, y, dmgTier, lifeTier)
	{
	}

	public void reset()
	{
		canTurn = true;
		canMove = true;
		curState = MobState.Idle;
		stateCd = 0.0;
		jumpHoldFrames = 0;
		if (shieldBashSkill != null)
		{
			shieldBashSkill.coolDownF = 0.0;
		}
		if (shieldedSlashSkill != null)
		{
			shieldedSlashSkill.coolDownF = 0.0;
		}
		if (shieldlessSlash1Skill != null)
		{
			shieldlessSlash1Skill.coolDownF = 0.0;
		}
		if (shieldlessSlash2Skill != null)
		{
			shieldlessSlash2Skill.coolDownF = 0.0;
		}
	}

	public override void onReload()
	{
		dc.tool.Cooldown cd = ((Entity)this).cd;
		if (cd != null)
		{
			cd.init((HlAction<dc.String, int>)((Entity)this).onCooldownEnd);
		}
		((Entity)this).init();
		if (!PlayableMOB.config.Value.enabled)
		{
			((Entity)this).destroy();
		}
	}

	public override void init()
	{
		Utils.mobInit((dc.en.Mob)this);

		// Get skill references from end of oldSkills array
		// Base Mob adds aggrTeleport+necromancedTeleport first,
		// Enforcer's 4 skills are the LAST 4 added
		if (base.oldSkills != null)
		{
			dynamic skills = base.oldSkills;
			int total = ((dynamic)skills).length;
			if (total >= 4)
			{
				try { shieldBashSkill = (OldMobSkill)(dynamic)skills.getDyn(total - 4); } catch { }
				try { shieldedSlashSkill = (OldMobSkill)(dynamic)skills.getDyn(total - 3); } catch { }
				try { shieldlessSlash1Skill = (OldMobSkill)(dynamic)skills.getDyn(total - 2); } catch { }
				try { shieldlessSlash2Skill = (OldMobSkill)(dynamic)skills.getDyn(total - 1); } catch { }
			}
		}

		// Hijack canUse: shield skills only when shielded
		if (shieldBashSkill != null)
		{
			shieldBash_origCanUse = shieldBashSkill.canUse;
			shieldBashSkill.canUse = new HlFunc<bool>(() => base.shielded);
		}
		if (shieldedSlashSkill != null)
		{
			shieldedSlash_origCanUse = shieldedSlashSkill.canUse;
			shieldedSlashSkill.canUse = new HlFunc<bool>(() => base.shielded);
		}

		// Set team to player's team
		((Entity)this).set_team(Game.Class.ME.curLevel.teamHero);

		// Hijack interrupt/execute callbacks
		HijackInterrupt(shieldBashSkill);
		HijackInterrupt(shieldedSlashSkill);
		HijackInterrupt(shieldlessSlash1Skill);
		HijackInterrupt(shieldlessSlash2Skill);

		HijackExecute(shieldBashSkill, MobState.ShieldBash);
		HijackExecute(shieldedSlashSkill, MobState.ShieldSlash);
		HijackExecute(shieldlessSlash1Skill, MobState.Slash1);
		HijackExecute(shieldlessSlash2Skill, MobState.Slash2);

		reset();
		inst = this;
		PlayableMOB.activeMonster = (Entity)this;
	}

	private void HijackInterrupt(OldMobSkill? skill)
	{
		if (skill == null) return;
		HlAction<double> orig = skill.dynOnInterrupt;
		skill.dynOnInterrupt = delegate(double ratio)
		{
			orig?.Invoke(ratio);
			reset();
		};
	}

	private void HijackExecute(OldMobSkill? skill, MobState state)
	{
		if (skill == null) return;
		HlAction<double> orig = skill.dynOnExecute;
		skill.dynOnExecute = delegate(double ratio)
		{
			orig?.Invoke(ratio);
			curState = state;
			stateCd = 0.5; // Auto-reset after 0.5s
		};
	}

	public override void fixedUpdate()
	{
		if (((Entity)this).destroyed) return;
		base.fixedUpdate();

		if (!PlayableMOB.config.Value.enabled)
			return;

		if (curState == MobState.Dead)
			return;

		if (((Entity)this).isUnconscious())
		{
			reset();
			return;
		}

		// State auto-reset via timer
		bool anyCharging = false;
		if (shieldBashSkill != null && shieldBashSkill.chargeF > 0.0) anyCharging = true;
		if (shieldedSlashSkill != null && shieldedSlashSkill.chargeF > 0.0) anyCharging = true;
		if (shieldlessSlash1Skill != null && shieldlessSlash1Skill.chargeF > 0.0) anyCharging = true;
		if (shieldlessSlash2Skill != null && shieldlessSlash2Skill.chargeF > 0.0) anyCharging = true;

		if (!anyCharging && curState != MobState.Idle)
		{
			stateCd -= 1.0 / 60.0; // Decrement by frame time
			if (stateCd <= 0.0)
			{
				reset();
			}
		}

		// Clear AI-set cooldowns when player wants to attack
		if (Utils.pressed(keys["skill1"]) || Utils.pressed(keys["skill2"]))
		{
			if (shieldBashSkill != null) shieldBashSkill.coolDownF = 0.0;
			if (shieldedSlashSkill != null) shieldedSlashSkill.coolDownF = 0.0;
			if (shieldlessSlash1Skill != null) shieldlessSlash1Skill.coolDownF = 0.0;
			if (shieldlessSlash2Skill != null) shieldlessSlash2Skill.coolDownF = 0.0;
		}

		// Player skill activation (only when idle)
		if (curState == MobState.Idle)
		{
			if (Utils.pressed(keys["skill1"]))
			{
				if (base.shielded && shieldedSlashSkill != null)
					shieldedSlashSkill.prepare(null);
				else if (!base.shielded && shieldlessSlash1Skill != null)
					shieldlessSlash1Skill.prepare(null);
			}

			if (base.shielded && Utils.pressed(keys["skill2"]) && shieldBashSkill != null)
			{
				shieldBashSkill.prepare(null);
			}
		}

		// Movement — getMoveSpeedMul() already includes shield/unshielded speed diff
		if (canMove && !((Entity)this).moveBlocked() && !isCrouched())
		{
			if (Utils.held(keys["right"]))
			{
				((Entity)this).dir = 1;
				((Entity)this).dx = 0.15 * base.getMoveSpeedMul();
			}
			else if (Utils.held(keys["left"]))
			{
				((Entity)this).dir = -1;
				((Entity)this).dx = -0.15 * base.getMoveSpeedMul();
			}
		}

		// Jump: W key — press to jump, hold for extra height
		bool onGround = ((Entity)this).cy == ((Entity)this)._level.map.getGroundY(((Entity)this).cx, ((Entity)this).cy);
		if (Utils.pressed(keys["jump"]) && onGround && canMove)
		{
			((Entity)this).dy = -0.5;
			jumpHoldFrames = 8;
		}
		// Jump hold: keep boosting upward while key held
		if (Utils.held(keys["jump"]) && jumpHoldFrames > 0 && ((Entity)this).dy < 0.0)
		{
			((Entity)this).dy = ((Entity)this).dy - 0.06;
			jumpHoldFrames--;
		}
		if (!Utils.held(keys["jump"]))
		{
			jumpHoldFrames = 0;
		}

		// Hold down to stop
		if (Utils.held(keys["down"]) && onGround)
		{
			((Entity)this).dx = 0.0;
		}
	}

	public override void onDie()
	{
		curState = MobState.Dead;
		canTurn = false;
		canMove = false;
		shieldBashSkill?.interrupt();
		shieldedSlashSkill?.interrupt();
		shieldlessSlash1Skill?.interrupt();
		shieldlessSlash2Skill?.interrupt();
		base.onDie();
	}

	public override void destroy()
	{
		inst = null;
		base.destroy();
	}

	public bool isCrouched()
	{
		return Utils.held(keys["down"]) && ((Entity)this).cy == ((Entity)this)._level.map.getGroundY(((Entity)this).cx, ((Entity)this).cy);
	}
}
