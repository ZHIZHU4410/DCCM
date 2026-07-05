using System;
using System.Collections.Generic;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.hero;
using dc.en.mob;
using dc.en.mob.boss;
using dc.en.mob.boss.collector;
using dc.h2d;
using dc.level;
using dc.libs;
using dc.pr;
using dc.tool;
using dc.tool.skill;

namespace PlayableMOB;

public class HeroCollectorBoss : Collector
{
	private bool canTurn = true, canMove = true;
	private MobState curState = MobState.Idle;
	private double moveMul = 1.0, stateCd;
	private int jumpHoldFrames;

	public static HeroCollectorBoss? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	// Saved originals
	private HlAction<double>? dDashI, dSpinI, dLaserI, dStompI, dFireI, dBallI, dBombI;
	private HlAction<double>? dDashE, dSpinE, dStompE, dFireE;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)hero).cx, ((Entity)hero).cy);
			// Create battleZone marker if missing (like PlayableBoss)
			bool flag = false;
			if (roomAt.getMarker(StringUtils.AsHaxeString("CustomSpot"), StringUtils.AsHaxeString("battleZone1"), new Ref<bool>(ref flag)) == null)
			{
				var m0 = Utils.copyMarker((dynamic)((dynamic)roomAt.markers).getDyn(0));
				m0.kind = StringUtils.AsHaxeString("CustomSpot");
				m0.cx = 0; m0.cy = 0; m0.width = 100; m0.height = 100;
				m0.customId = StringUtils.AsHaxeString("battleZone1");
				roomAt.markers.push((object)m0);
			}
			var m = new HeroCollectorBoss(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, 38, 38);
			((Entity)m).dir = ((Entity)hero).dir;
			((Entity)m).init();
		}
	}

	public HeroCollectorBoss(Level lvl, int x, int y, int dmgTier, int lifeTier)
		: base(lvl, x, y, dmgTier, lifeTier, Ref<bool>.Null) { }

	public override void giveAchievements() { }
	public override void giveHeadFeedback(dc.String h) { }
	public override void giveHeads() { }
	public override void tpHeroBackToTraining() { }

	void reset()
	{
		canTurn = true; canMove = true; curState = MobState.Idle;
		moveMul = 1.0; stateCd = 0;
		var c = (Collector)this;
		c.smallDashSkill.coolDownF = c.spinSkill.coolDownF = 0;
		c.laserBeamSkill.coolDownF = c.bigStompSkill.coolDownF = 0;
		c.fireWallsSkill.coolDownF = c.energyBallSkill.coolDownF = 0;
		c.throwBombSkill.coolDownF = 0;
	}

	public override void onReload()
	{
		var cd = ((Entity)this).cd;
		if (cd != null) cd.init((HlAction<dc.String, int>)((Entity)this).onCooldownEnd);
		((Entity)this).init();
		if (!PlayableMOB.config.Value.enabled) ((Entity)this).destroy();
	}

	public override void init()
	{
		var c = (Collector)this;
		Utils.bossInit((dc.en.mob.Boss)this);
		((Entity)this).isOutOfGame = false;
		c.phase = 4;

		Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)this).cx, ((Entity)this).cy);
		bool required = false;
		((Boss)this).battleZone = (Marker)(dynamic)(roomAt.getMarker(StringUtils.AsHaxeString("CustomSpot"), StringUtils.AsHaxeString("battleZone1"), new Ref<bool>(ref required)) ?? ((dynamic)roomAt.markers).getDyn(0));
		c.rseed = new Rand(dc.pr.Game.Class.ME.curLevel.map.seed);
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		((Boss)this).cameraTrackingDisabled = false;

		// --- Dash (skill1): save originals, call them in our wrappers ---
		var dashCS = c.smallDashSkill.dynOnChargeStart;
		c.smallDashSkill.dynOnChargeStart = (HlAction)delegate { dashCS.Invoke(); canMove = false; ((Entity)this).cancelVelocities(); };
		dDashE = c.smallDashSkill.dynOnExecute;
		c.smallDashSkill.dynOnExecute = r => { dDashE?.Invoke(r); canTurn = false; curState = MobState.ShieldSlash; stateCd = 0.4; };
		dDashI = c.smallDashSkill.dynOnInterrupt; c.smallDashSkill.dynOnInterrupt = r => { dDashI?.Invoke(r); reset(); };

		// --- Spin (skill2) ---
		var spinCS = c.spinSkill.dynOnChargeStart;
		c.spinSkill.dynOnChargeStart = (HlAction)delegate { spinCS.Invoke(); canMove = false; moveMul = 0.5; };
		var spinCh = c.spinSkill.dynOnCharging;
		c.spinSkill.dynOnCharging = r => { spinCh.Invoke(r); if (!Utils.held(keys["skill2"])) c.spinSkill.interrupt(); };
		dSpinE = c.spinSkill.dynOnExecute;
		c.spinSkill.dynOnExecute = r => { dSpinE?.Invoke(r); canMove = true; curState = MobState.Slash1; };
		dSpinI = c.spinSkill.dynOnInterrupt; c.spinSkill.dynOnInterrupt = r => { dSpinI?.Invoke(r); reset(); };

		// --- Laser (skill3): save + call originals ---
		var laserCS = c.laserBeamSkill.dynOnChargeStart;
		c.laserBeamSkill.dynOnChargeStart = (HlAction)delegate { laserCS.Invoke(); canTurn = false; };
		var laserExec = c.laserBeamSkill.dynOnExecute;
		c.laserBeamSkill.dynOnExecute = r => { laserExec.Invoke(r); c.laserBeamSkill.coolDownF = 0; curState = MobState.Slash2; if (!Utils.held(keys["skill3"])) { bool h = ((Entity)this).hasEntityTouchChecks; ((Entity)this).enableAllPhysics(new Ref<bool>(ref h)); ((Entity)this).removeAllAffects(5); ((Entity)this).setAffectS(63, 3, Ref<double>.Null, null); reset(); } };
		dLaserI = c.laserBeamSkill.dynOnInterrupt; c.laserBeamSkill.dynOnInterrupt = r => { dLaserI?.Invoke(r); reset(); };

		// --- Stomp (skill4): save + call originals ---
		var stompCS = c.bigStompSkill.dynOnChargeStart;
		c.bigStompSkill.dynOnChargeStart = (HlAction)delegate { stompCS.Invoke(); canTurn = false; canMove = false; };
		dStompE = c.bigStompSkill.dynOnExecute;
		c.bigStompSkill.dynOnExecute = r => { dStompE?.Invoke(r); canMove = false; curState = MobState.ShieldBash; stateCd = 0.6; };
		dStompI = c.bigStompSkill.dynOnInterrupt; c.bigStompSkill.dynOnInterrupt = r => { dStompI?.Invoke(r); reset(); };

		// --- Fire Walls (skill5): save + call originals ---
		var fwCS = c.fireWallsSkill.dynOnChargeStart;
		c.fireWallsSkill.dynOnChargeStart = (HlAction)delegate { fwCS.Invoke(); canMove = false; };
		dFireE = c.fireWallsSkill.dynOnExecute;
		c.fireWallsSkill.dynOnExecute = r => { dFireE?.Invoke(r); c.curFW = 5; curState = MobState.Idle; };
		dFireI = c.fireWallsSkill.dynOnInterrupt; c.fireWallsSkill.dynOnInterrupt = r => { dFireI?.Invoke(r); reset(); };

		// --- Unmapped skills: just hijack interrupt ---
		dBallI = c.energyBallSkill.dynOnInterrupt; c.energyBallSkill.dynOnInterrupt = r => { dBallI?.Invoke(r); reset(); };
		dBombI = c.throwBombSkill.dynOnInterrupt; c.throwBombSkill.dynOnInterrupt = r => { dBombI?.Invoke(r); reset(); };

		reset();
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		if (((Entity)this).destroyed) return;
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (curState == MobState.Dead) return;
		if (((Entity)this).isUnconscious()) { reset(); return; }

		var c = (Collector)this;

		// State cleanup
		bool charging = c.smallDashSkill.chargeF > 0 || c.spinSkill.chargeF > 0 || c.laserBeamSkill.chargeF > 0 || c.bigStompSkill.chargeF > 0 || c.fireWallsSkill.chargeF > 0;
		if (!charging && curState != MobState.Idle)
		{
			stateCd -= 1.0 / 60.0;
			if (stateCd <= 0) reset();
		}

		if (curState == MobState.Idle)
		{
			if (Utils.pressed(keys["skill1"])) { c.smallDashSkill.coolDownF = 0; c.smallDashSkill.prepare(null); }
			if (Utils.pressed(keys["skill2"])) { c.spinSkill.coolDownF = 0; c.spinSkill.prepare(null); }
			if (Utils.pressed(keys["skill3"])) { c.laserBeamSkill.coolDownF = 0; c.laserBeamSkill.prepare(null); }
			if (Utils.pressed(keys["skill4"])) { c.bigStompSkill.coolDownF = 0; c.bigStompSkill.prepare(null); }
			if (Utils.pressed(keys["skill5"])) { c.fireWallsSkill.coolDownF = 0; c.fireWallsSkill.prepare(null); }
		}

		// Spin speed ramp
		if (curState == MobState.Slash1 && Utils.held(keys["skill2"])) { moveMul += 0.25; if (moveMul > 5) moveMul = 5; }

		// Movement
		if (canMove && !((Entity)this).moveBlocked())
		{
			if (Utils.held(keys["right"])) { ((Entity)this).dir = 1; ((Entity)this).dx = 0.15 * ((dc.en.Mob)this).getMoveSpeedMul() * moveMul; }
			else if (Utils.held(keys["left"])) { ((Entity)this).dir = -1; ((Entity)this).dx = -0.15 * ((dc.en.Mob)this).getMoveSpeedMul() * moveMul; }
		}

		// Jump
		bool onGround = ((Entity)this).cy == ((Entity)this)._level.map.getGroundY(((Entity)this).cx, ((Entity)this).cy);
		if (Utils.pressed(keys["jump"]) && onGround && canMove) { ((Entity)this).dy = -0.5; jumpHoldFrames = 8; }
		if (Utils.held(keys["jump"]) && jumpHoldFrames > 0 && ((Entity)this).dy < 0) { ((Entity)this).dy -= 0.06; jumpHoldFrames--; }
		if (!Utils.held(keys["jump"])) jumpHoldFrames = 0;
		if (Utils.held(keys["down"]) && onGround) ((Entity)this).dx = 0;
	}

	public override void onDie()
	{
		curState = MobState.Dead; canTurn = canMove = false;
		var c = (Collector)this;
		c.smallDashSkill?.interrupt(); c.spinSkill?.interrupt(); c.laserBeamSkill?.interrupt();
		c.bigStompSkill?.interrupt(); c.fireWallsSkill?.interrupt(); c.energyBallSkill?.interrupt(); c.throwBombSkill?.interrupt();
		base.onDie();
	}

	public override void destroy() { inst = null; base.destroy(); }
}
