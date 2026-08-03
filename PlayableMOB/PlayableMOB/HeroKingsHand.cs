using System;
using System.Collections.Generic;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.mob;
using dc.en.mob.boss;
using dc.hxd;
using dc.libs.heaps.slib;
using dc.libs.heaps.slib._AnimManager;
using dc.level;
using dc.pr;
using dc.tool;
using dc.tool.atk;
using dc.tool.skill;

namespace PlayableMOB;

/// <summary>
/// Player-controlled Hand of the King.
///
/// Skills are fetched by id from <see cref="Mob.oldSkills"/> instead of by
/// positional index (the boss skill list changes between game versions).
/// The boss AI stays dormant because the constructor arms a very long
/// "battle start" cooldown (157286400), so <c>inCombat()</c> is false and
/// <c>fixedUpdate()</c> never picks attacks by itself.
/// </summary>
public class HeroKingsHand : KingsHand
{
	private bool canTurn = true, canMove = true;
	private MobState curState = MobState.Idle;
	private double stateCd;
	private int jumpHoldFrames;
	private int errLogCd;
	private bool shieldDashActive;
	private int shieldFxCd;

	public static HeroKingsHand? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	// Main kit (mapped to skill1..skill5 bindings)
	private OldMobSkill? circleSlash;   // ccCircle1
	private OldMobSkill? charge;        // ccCharge1
	private OldMobSkill? heavySlash;    // ccHeavy1
	private OldMobSkill? stomp;         // ccStomp1
	private OldMobSkill? groundStomp;   // globalStomp
	// Extra kit (mapped to fixed keys H / X / C)
	private OldMobSkill? shieldCharge;  // shieldCharge
	private OldMobSkill? grenade;       // grenade
	private OldMobSkill? megaBomb;      // castMegaBomb

	public static void create(Hero hero)
	{
		if (inst != null && !((Entity)inst).destroyed) return;

		try
		{
			Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)hero).cx, ((Entity)hero).cy);
			// The _Boss constructor looks for a "battleZone" CustomSpot; without it
			// battleZone stays null and any combat-zone access crashes.
			InjectMarker(roomAt, "battleZone", 0, 0, 100, 100);
			// preUpdate() builds playZone from a "playZone" CustomSpot; without one
			// it dereferences a null marker on the first frame. Use a compact
			// arena around the spawn point (game playZone markers are small).
			int pzW = System.Math.Min(48, roomAt.wid);
			int pzH = System.Math.Min(32, roomAt.hei);
			InjectMarker(roomAt, "playZone",
				System.Math.Max(0, ((Entity)hero).cx - roomAt.x - pzW / 2),
				System.Math.Max(0, ((Entity)hero).cy - roomAt.y - pzH / 2),
				pzW, pzH);

			var m = new HeroKingsHand(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)m).dir = ((Entity)hero).dir;
			((Entity)m).init();
			Utils.log("HeroKingsHand created at " + ((Entity)m).cx + "," + ((Entity)m).cy
				+ " dmgTier=" + Utils.DamageTier() + " lifeTier=" + Utils.LifeTier()
				+ " scrolls=" + (dc.pr.Game.Class.ME.hero.brutalityTier + dc.pr.Game.Class.ME.hero.tacticTier + dc.pr.Game.Class.ME.hero.survivalTier));
		}
		catch (Exception ex)
		{
			Utils.log("HeroKingsHand.create FAILED: " + ex.GetType().FullName + " | " + ex.Message + "\n" + ex.StackTrace);
			// Never leave a half-constructed boss as the active monster.
			if (inst != null)
			{
				try { inst.destroy(); } catch { }
				inst = null;
			}
			PlayableMOB.activeMonster = null;
		}
	}

	private static void InjectMarker(Room room, string id, int cx, int cy, int width, int height)
	{
		bool flag = false;
		if (room.getMarker(StringUtils.AsHaxeString("CustomSpot"), StringUtils.AsHaxeString(id), new Ref<bool>(ref flag)) == null)
		{
			var m0 = Utils.copyMarker((dynamic)((dynamic)room.markers).getDyn(0));
			m0.kind = StringUtils.AsHaxeString("CustomSpot");
			m0.cx = cx; m0.cy = cy; m0.width = width; m0.height = height;
			m0.customId = StringUtils.AsHaxeString(id);
			room.markers.push((object)m0);
		}
	}

	public HeroKingsHand(Level lvl, int x, int y, int dmgTier, int lifeTier)
		: base(lvl, x, y, dmgTier, lifeTier) { }

	public override void giveAchievements() { }
	public override void giveHeadFeedback(dc.String h) { }
	public override void giveHeads() { }
	public override void tpHeroBackToTraining() { }

	void reset()
	{
		canTurn = true; canMove = true; curState = MobState.Idle; stateCd = 0;
		shieldDashActive = false;
		((Entity)this).dx = 0;
	}

	public override void init()
	{
		Utils.bossInit((dc.en.mob.Boss)this);
		((Entity)this).isOutOfGame = false;
		// checkForLevelUp() dereferences levelUpSteps.length every frame when a
		// nemesis target exists; the boss normally fills it from its phase
		// setup which we never run. Empty it: no crash, no auto phase-up.
		try { ((dc.en.mob.Boss)this).removeAllLevelUpSteps(); } catch { }

		// battleZone / playZone were injected before construction, so both are
		// valid. Keep cameraTrackingDisabled=true (set by bossInit) to avoid
		// the boss camera hijacking the viewport.

		circleSlash = GetSkill("ccCircle1");
		charge = GetSkill("ccCharge1");
		heavySlash = GetSkill("ccHeavy1");
		stomp = GetSkill("ccStomp1");
		groundStomp = GetSkill("globalStomp");
		shieldCharge = GetSkill("shieldCharge");
		grenade = GetSkill("grenade");
		megaBomb = GetSkill("castMegaBomb");

		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);

		Hijack(circleSlash, MobState.ShieldSlash, 0.4);
		Hijack(charge, MobState.ShieldBash, 0.5);
		Hijack(heavySlash, MobState.Slash2, 0.7);
		Hijack(stomp, MobState.Slash1, 0.6);
		Hijack(groundStomp, MobState.Slash2, 1.0);
		HijackShieldCharge();
		Hijack(grenade, MobState.ShieldBash, 0.5);
		Hijack(megaBomb, MobState.Slash2, 0.8);

		((dc.en.Mob)this).interruptSkills();
		// shieldCharge's charge callback calls lookAt(aTarget); with a null
		// target the boss never turns and the dash direction is lost. Point
		// it at the (hidden) hero — the AI is disabled so it never
		// auto-attacks.
		((dc.en.Mob)this).aTarget = dc.pr.Game.Class.ME.hero;
		try { ((dc.en.Mob)this).resetQueuedOldSkill(); } catch { }

		reset();
		inst = this; PlayableMOB.activeMonster = (Entity)this;

		try
		{
			Utils.log("HeroKingsHand init done: inCombat=" + inCombat()
				+ " battleCd=" + ((Entity)this).cd.fastCheck.exists(157286400)
				+ " skills=" + (circleSlash != null ? "ccCircle1" : "-") + "/"
				+ (charge != null ? "ccCharge1" : "-") + "/"
				+ (heavySlash != null ? "ccHeavy1" : "-") + "/"
				+ (stomp != null ? "ccStomp1" : "-") + "/"
				+ (groundStomp != null ? "globalStomp" : "-") + "/"
				+ (shieldCharge != null ? "shieldCharge" : "-") + "/"
				+ (grenade != null ? "grenade" : "-") + "/"
				+ (megaBomb != null ? "castMegaBomb" : "-"));
		}
		catch (Exception ex)
		{
			Utils.log("HeroKingsHand init diagnostics failed: " + ex.Message);
		}
	}

	/// <summary>
	/// The boss AI is fully disabled. KingsHand.preUpdate() re-targets the
	/// (hidden) hero every frame, which makes the base mob AI queue attacks
	/// against the player's own body. Swallow any AI-queued attack: the
	/// player triggers skills manually with prepare().
	/// </summary>
	public override void queueAttack(OldMobSkill a, bool requiresTarget, int? data)
	{
		Utils.log("HeroKingsHand: AI attack swallowed (" + (a != null ? a.id.ToString() : "null") + ")");
	}

	/// <summary>
	/// The base Mob AI (behaviourAi) auto-attacks its aTarget every frame
	/// through direct prepare() calls — it does NOT go through queueAttack.
	/// Since the boss's aTarget is the (hidden) player body, this made the
	/// king swing on its own. Fully disable the base behaviour AI; the player
	/// is the only one who triggers skills.
	/// </summary>
	public override void behaviourAi()
	{
		// no-op: player-controlled boss
	}

	/// <summary>
	/// Guard the game's preUpdate (playZone creation etc.). If anything in
	/// the boss's own update chain throws, log the real exception instead of
	/// letting the mod hook mash the stack into an opaque Level.update crash.
	/// </summary>
	public override void preUpdate()
	{
		try
		{
			base.preUpdate();
		}
		catch (Exception ex)
		{
			if (errLogCd <= 0)
			{
				Utils.log("HeroKingsHand base.preUpdate FAILED: " + ex);
				errLogCd = 300;
			}
			else errLogCd--;
		}
	}

	private OldMobSkill? GetSkill(string id)
	{
		try
		{
			return (OldMobSkill)((dc.en.Mob)this).getOldSkill(StringUtils.AsHaxeString(id));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Wraps a skill's callbacks so the original game logic still runs
	/// (sfx / anim / hitboxes) while the player regains control on interrupt
	/// and the state machine tracks the skill's active window.
	/// </summary>
	private void Hijack(OldMobSkill? sk, MobState st, double cd)
	{
		if (sk == null) return;

		HlAction origStart = sk.dynOnChargeStart;
		sk.dynOnChargeStart = (HlAction)delegate
		{
			origStart?.Invoke();
			canTurn = false; canMove = false;
		};

		HlAction<double> origInterrupt = sk.dynOnInterrupt;
		sk.dynOnInterrupt = delegate(double r)
		{
			origInterrupt?.Invoke(r);
			reset();
		};

		HlAction<double> origExecute = sk.dynOnExecute;
		sk.dynOnExecute = delegate(double r)
		{
			origExecute?.Invoke(r);
			curState = st; stateCd = cd;
		};
	}

	/// <summary>
	/// shieldCharge-specific hijack: on execute (runShield animation) mark the
	/// dash window so fixedUpdate keeps the boss running forward for exactly
	/// as long as the run animation plays.
	/// </summary>
	private void HijackShieldCharge()
	{
		var sk = shieldCharge;
		if (sk == null) return;

		HlAction origStart = sk.dynOnChargeStart;
		sk.dynOnChargeStart = (HlAction)delegate
		{
			origStart?.Invoke();
			canTurn = false; canMove = false;
		};

		HlAction<double> origInterrupt = sk.dynOnInterrupt;
		sk.dynOnInterrupt = delegate(double r)
		{
			origInterrupt?.Invoke(r);
			reset();
		};

		HlAction<double> origExecute = sk.dynOnExecute;
		sk.dynOnExecute = delegate(double r)
		{
			origExecute?.Invoke(r);
			curState = MobState.Slash1; stateCd = 0.7;
			shieldDashActive = true;
		};
	}

	public override void fixedUpdate()
	{
		if (((Entity)this).destroyed) return;
		try
		{
			base.fixedUpdate();
		}
		catch (Exception ex)
		{
			if (errLogCd <= 0)
			{
				Utils.log("HeroKingsHand base.fixedUpdate FAILED: " + ex);
				errLogCd = 300;
			}
			else errLogCd--;
		}
		if (!PlayableMOB.config.Value.enabled) return;
		if (curState == MobState.Dead) return;
		if (((Entity)this).isUnconscious()) { reset(); return; }

		// State cleanup: wait for the active skill to finish animating.
		bool charging = (circleSlash != null && circleSlash.chargeF > 0)
			|| (charge != null && charge.chargeF > 0)
			|| (heavySlash != null && heavySlash.chargeF > 0)
			|| (stomp != null && stomp.chargeF > 0)
			|| (groundStomp != null && groundStomp.chargeF > 0)
			|| (shieldCharge != null && shieldCharge.chargeF > 0)
			|| (grenade != null && grenade.chargeF > 0)
			|| (megaBomb != null && megaBomb.chargeF > 0);
		if (!charging && curState != MobState.Idle)
		{
			stateCd -= 1.0 / 60.0;
			if (stateCd <= 0) reset();
		}

		// shieldCharge: pressing H skips the runShieldLoad wind-up and goes
		// straight into the runShield run (execute). The forward dash and the
		// path damage run for the exact same window as that run animation.
		if (shieldDashActive)
		{
			// Physics-based forward dash: the engine's own tile collision
			// stops the boss at walls instead of phasing through them.
			// 0.7 tiles/frame over the 0.5s window keeps the halved distance.
			((Entity)this).dx = ((Entity)this).dir * 0.7;
			// Stretch the runShield animation ~+70% so one run cycle lasts as
			// long as the dash window (1/1.7 ≈ 0.588 speed multiplier).
			ShieldChargeHit();
			// Safety net: if the anim manager ever stops or switches away from
			// runShield mid-dash, replay it so the boss keeps running for the
			// whole window.
			try
			{
				var anim = ((Entity)this).spr.get_anim();
				bool runActive = false;
				var stack = anim.stack;
				if (stack != null && stack.length > 0)
				{
					var top = (AnimInstance)stack.getDyn(0);
					runActive = top != null && top.group != null
						&& ((object)top.group).ToString() == "runShield"
						&& !top.paused;
				}
				if (!runActive)
				{
					anim.play(StringUtils.AsHaxeString("runShield"), (int?)1, (bool?)false).loop((int?)null);
				}
			}
			catch { }
			// Dash visuals: the vanilla boss draws the front shield fan plus
			// electric sparks while running the shield charge.
			try
			{
				var fx = ((Entity)this)._level.fx;
				if (fx != null)
				{
					fx.khFrontShieldCharge((Entity)this, 67.2, 16767232, 14104348);
					if (shieldFxCd <= 0)
					{
						double ex = ((double)((Entity)this).cx + ((Entity)this).xr) * 24.0 + ((Entity)this).dir * 24;
						double ey = ((double)((Entity)this).cy + ((Entity)this).yr) * 24.0 - ((Entity)this).hei * 0.5 + 10.0;
						fx.electricCharge(ex, ey, 15373589, 1.0);
						shieldFxCd = 3;
					}
				}
			}
			catch (Exception ex)
			{
				if (errLogCd <= 0) { Utils.log("HeroKingsHand shieldCharge fx FAILED: " + ex); errLogCd = 300; }
				else errLogCd--;
			}
		}
		if (shieldFxCd > 0) shieldFxCd--;

		if (curState == MobState.Idle)
		{
			if (Utils.pressed(keys["skill1"])) TryUse("J/ccCircle1", circleSlash);
			if (Utils.pressed(keys["skill2"])) TryUse("K/ccCharge1", charge);
			if (Utils.pressed(keys["skill3"])) TryUse("L/ccHeavy1", heavySlash);
			if (Utils.pressed(keys["skill4"])) TryUse("U/ccStomp1", stomp);
			if (Utils.pressed(keys["skill5"])) TryUse("I/globalStomp", groundStomp);
			if (Key.Class.isPressed.Invoke(72)) TryUseShieldCharge();
			if (Key.Class.isPressed.Invoke(88)) TryUse("X/grenade", grenade);
			if (Key.Class.isPressed.Invoke(67)) TryUse("C/castMegaBomb", megaBomb);
		}

		// Movement
		if (canMove && !((Entity)this).moveBlocked())
		{
			if (Utils.held(keys["right"])) { if (canTurn) ((Entity)this).dir = 1; ((Entity)this).dx = 0.15 * ((dc.en.Mob)this).getMoveSpeedMul(); }
			else if (Utils.held(keys["left"])) { if (canTurn) ((Entity)this).dir = -1; ((Entity)this).dx = -0.15 * ((dc.en.Mob)this).getMoveSpeedMul(); }
		}

		// Jump with hold mechanic
		bool onGround = ((Entity)this).cy == ((Entity)this)._level.map.getGroundY(((Entity)this).cx, ((Entity)this).cy);
		if (Utils.pressed(keys["jump"]) && onGround && canMove) { ((Entity)this).dy = -0.5; jumpHoldFrames = 8; }
		if (Utils.held(keys["jump"]) && jumpHoldFrames > 0 && ((Entity)this).dy < 0) { ((Entity)this).dy -= 0.06; jumpHoldFrames--; }
		if (!Utils.held(keys["jump"])) jumpHoldFrames = 0;
		if (Utils.held(keys["down"]) && onGround) ((Entity)this).dx = 0;
	}

	private void TryUse(string label, OldMobSkill? sk)
	{
		Utils.SyncHeroToFront((Entity)this);
		if (sk == null)
		{
			Utils.log("HeroKingsHand: " + label + " pressed but skill not loaded");
			return;
		}
		bool ready = sk.isReady();
		sk.coolDownF = 0;
		// Use the same target-aiming path as the AI's queued hits
		// (prepareOnOwnerTarget forces the attack area onto the target and
		// sets chargeArea). Plain prepare() leaves the area unaimed, which is
		// why the player-triggered first hit of a chain dealt no damage.
		bool ok = sk.prepareOnOwnerTarget(true, null);
		Utils.log("HeroKingsHand: " + label + " pressed -> ready=" + ready
			+ " prepared=" + ok + " active=" + sk.active
			+ " cd=" + sk.coolDownF + " charge=" + sk.chargeF);
	}

	/// <summary>
	/// H/shieldCharge: the runShield animation is a state anim driven by the
	/// boss cooldown flag 134217728 (the same flag the vanilla chargeComplete
	/// arms). Instead of going through the skill's charge/execute pipeline
	/// (which plays the runShieldLoad wind-up first), arm that flag directly
	/// for the dash window: the boss starts running forward on the same frame
	/// the key is pressed, and the run + dash share the exact same duration.
	/// </summary>
	private void TryUseShieldCharge()
	{
		Utils.SyncHeroToFront((Entity)this);
		if (shieldCharge == null)
		{
			Utils.log("HeroKingsHand: H/shieldCharge pressed but skill not loaded");
			return;
		}
		try
		{
			// Cancel any leftover charge so the load animation never plays.
			shieldCharge.interrupt();
			shieldCharge.coolDownF = 0;
			// Arm the runShield state gate so the anim manager keeps this run
			// animation active for the whole dash window.
			SetShieldRunWindow(0.5);
			// Collector K-dash style: play the run animation at NORMAL speed.
			// Use loop(null) — the same config the vanilla state anims use
			// (plays=999999, playDuration=-1) — so the short 12-frame clip
			// loops continuously instead of being cut off by a fixed
			// playDuration (loop(frames) caused the early end).
			try
			{
				var anim = ((Entity)this).spr.get_anim();
				anim.play(StringUtils.AsHaxeString("runShield"), (int?)1, (bool?)false).loop((int?)null);
			}
			catch { }
			Utils.playEvent("sfx/enm/enm_hand_ccrelease1.wav");
			canTurn = false; canMove = false;
			curState = MobState.Slash1; stateCd = 0.5;
			shieldDashActive = true;
			Utils.log("HeroKingsHand: H/shieldCharge -> run+dash window 0.5s");
		}
		catch (Exception ex)
		{
			Utils.log("HeroKingsHand: H/shieldCharge FAILED: " + ex.GetType().FullName + " | " + ex.Message + "\n" + ex.StackTrace);
			reset();
		}
	}

	/// <summary>
	/// Arms / refreshes the boss cooldown flag that drives the runShield state
	/// animation, so the run lasts exactly as long as the requested window.
	/// </summary>
	private void SetShieldRunWindow(double seconds)
	{
		var cd = ((Entity)this).cd;
		if (cd == null) return;
		double frames = seconds * cd.baseFps;
		var ci = (dc.tool._Cooldown.CdInst)cd.fastCheck.get(134217728);
		if (ci != null)
		{
			ci.frames = frames;
		}
		else
		{
			ci = new dc.tool._Cooldown.CdInst(134217728, frames);
			cd.fastCheck.set(134217728, ci);
			cd.cdList.push((object)ci);
		}
	}

	/// <summary>
	/// Replicates the vanilla shieldCharge path damage (KingsHand.fixedUpdate
	/// block, gated behind inCombat() in the original game): hits opponents in
	/// front of the king while the charge runs, with the same per-target
	/// effect cooldown, stun and bump as the boss's own charge.
	/// </summary>
	private void ShieldChargeHit()
	{
		try
		{
			if (((Entity)this)._team == null) return;
			TeamIterator it = ((Entity)this)._team.opponentsIterator.reset(((Entity)this)._team);
			int dir = ((Entity)this).dir;
			double px = ((double)((Entity)this).cx + ((Entity)this).xr) * 24.0;
			while (it.hasNext())
			{
				Entity e = it.next();
				if (e == null || e.life <= 0 || e.destroyed || !e.canBeHitBy((Entity)this)) continue;
				double epx = ((double)e.cx + e.xr) * 24.0;
				// Only enemies ahead of the dash direction and close enough.
				if ((epx >= px ? 1 : -1) != dir) continue;
				if (System.Math.Abs(px - epx) > 86.4) continue;
				if (e.cy < ((Entity)this).cy - 2 || ((Entity)this).cy + 1 < e.cy) continue;
				// Vanilla shieldCharge per-target hit cooldown (effectCD=1.7s).
				if (e.cd != null && e.cd.fastCheck.exists(1887436800)) continue;
				if (e.cd != null)
				{
					var ci = new dc.tool._Cooldown.CdInst(1887436800, 1.7 * e.cd.baseFps);
					e.cd.fastCheck.set(1887436800, ci);
					e.cd.cdList.push((object)ci);
				}
				// power 4 is the King's Hand shieldCharge value from data.cdb.
				AttackData atk = AttackUtils.Class.createFromMob.Invoke((Entity)this, (object)4.0, null);
				AttackUtils.Class.hit.Invoke(atk, e);
				if (atk.isSuccess())
				{
					double zero = 0.0;
					e.setAffectS(8, 0.4, ref zero, null); // stun (props.duration)
					e.bump((double)dir * 1.2, 0.0, null);
				}
				else if (atk.hitResult != new HitResult.Block())
				{
					// Shielded enemy: shove it away and bounce the king back.
					e.bump((double)dir * 0.6, 0.3, null);
					((Entity)this).cancelVelocities();
					((Entity)this).bump((double)-dir * 0.3, 0.0, null);
				}
			}
		}
		catch (Exception ex)
		{
			if (errLogCd <= 0) { Utils.log("HeroKingsHand shieldCharge hit FAILED: " + ex); errLogCd = 300; }
			else errLogCd--;
		}
	}

	public override void onDie()
	{
		curState = MobState.Dead; canTurn = canMove = false; shieldDashActive = false;
		circleSlash?.interrupt(); charge?.interrupt(); heavySlash?.interrupt(); stomp?.interrupt();
		groundStomp?.interrupt(); shieldCharge?.interrupt(); grenade?.interrupt(); megaBomb?.interrupt();
		base.onDie();
	}

	public override void destroy() { inst = null; base.destroy(); }
}
