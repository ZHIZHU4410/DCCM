using System;
using System.Collections.Generic;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.hero;
using dc.en.mob;
using dc.en.mob.boss;
using dc.hxd;
using dc.level;
using dc.pr;
using dc.tool.skill;

namespace PlayableMOB;

/// <summary>
/// Player-controlled Queen.
///
/// Skills are read from Queen's own public fields (assigned in initSkills).
/// The base behaviour AI is disabled so the boss never acts on its own; the
/// player triggers every skill via prepareOnOwnerTarget (which also aims the
/// attack area, fixing the "first hit deals no damage" issue).
/// </summary>
public class HeroQueen : Queen
{
	private bool canTurn = true, canMove = true;
	private MobState curState = MobState.Idle;
	private double stateCd;
	private int jumpHoldFrames;
	private int errLogCd;

	public static HeroQueen? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? lunge;      // lungeAttack
	private OldMobSkill? firewave;   // firewaveAtk
	private OldMobSkill? shockWave;  // shockWaveAtk
	private OldMobSkill? grab;       // grabAttack
	private OldMobSkill? backDash;   // backDashSkill
	private OldMobSkill? overshield; // overshieldAttack
	private OldMobSkill? taunt;      // tauntAtk
	private OldMobSkill? splitScreen; // splitScreenTest (dimensional slash)

	public static void create(Hero hero)
	{
		if (inst != null && !((Entity)inst).destroyed) return;

		try
		{
			Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)hero).cx, ((Entity)hero).cy);
			// The _Boss constructor looks for a "battleZone" CustomSpot; keep it
			// valid in any room.
			InjectMarker(roomAt, "battleZone", 0, 0, 100, 100);

			var m = new HeroQueen(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)m).dir = ((Entity)hero).dir;
			// bossLevel 6 = final slice phase: splitScreenTest (dimensional
			// slash) gets 6x cut lines and a near-zero cooldown.
			m.bossLevel = 6;
			Utils.bossInit((dc.en.mob.Boss)m);
			m.playerInit();
			Utils.log("HeroQueen created at " + ((Entity)m).cx + "," + ((Entity)m).cy
				+ " dmgTier=" + Utils.DamageTier() + " lifeTier=" + Utils.LifeTier()
				+ " scrolls=" + (hero.brutalityTier + hero.tacticTier + hero.survivalTier));
		}
		catch (Exception ex)
		{
			Utils.log("HeroQueen.create FAILED: " + ex.GetType().FullName + " | " + ex.Message + "\n" + ex.StackTrace);
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

	public HeroQueen(Level lvl, int x, int y, int dmgTier, int lifeTier)
		: base(lvl, x, y, dmgTier, lifeTier) { }

	public override void giveAchievements() { }
	public override void giveHeadFeedback(dc.String h) { }
	public override void giveHeads() { }
	public override void tpHeroBackToTraining() { }

	void reset()
	{
		canTurn = true; canMove = true; curState = MobState.Idle; stateCd = 0;
	}

	public void playerInit()
	{
		((Entity)this).isOutOfGame = false;
		// checkForLevelUp() dereferences levelUpSteps.length every frame when a
		// nemesis target exists; the boss normally fills it from its phase
		// setup which we never run. Empty it: no crash, no auto phase-up.
		try { ((dc.en.mob.Boss)this).removeAllLevelUpSteps(); } catch { }

		// initSkills only creates splitScreenTest when bossLevel is even; the
		// regular kit lives in initOffensiveSkills/initDefensiveSkills which
		// the boss normally runs instead. Add them back manually.
		try { initOffensiveSkills(); } catch (Exception ex) { Utils.log("HeroQueen initOffensiveSkills FAILED: " + ex); }
		try { initDefensiveSkills(); } catch (Exception ex) { Utils.log("HeroQueen initDefensiveSkills FAILED: " + ex); }

		// Queen's initSkills assigns these public fields; read them directly.
		lunge = this.lungeAttack;
		firewave = this.firewaveAtk;
		shockWave = this.shockWaveAtk;
		grab = this.grabAttack;
		backDash = this.backDashSkill;
		overshield = this.overshieldAttack;
		taunt = this.tauntAtk;
		splitScreen = GetSkill("splitScreenTest");

		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);

		Hijack(lunge, MobState.ShieldSlash, 0.5);
		Hijack(firewave, MobState.ShieldBash, 0.6);
		Hijack(shockWave, MobState.Slash1, 0.7);
		Hijack(grab, MobState.Slash2, 0.8);
		Hijack(backDash, MobState.Slash1, 0.4);
		Hijack(overshield, MobState.ShieldBash, 0.3);
		Hijack(taunt, MobState.Slash2, 0.5);

		((dc.en.Mob)this).interruptSkills();
		((dc.en.Mob)this).aTarget = dc.pr.Game.Class.ME.hero;
		try { ((dc.en.Mob)this).nemesisTarget = dc.pr.Game.Class.ME.hero; } catch { }
		try { ((dc.en.Mob)this).resetQueuedOldSkill(); } catch { }

		reset();
		inst = this; PlayableMOB.activeMonster = (Entity)this;

		Utils.log("HeroQueen init done: skills="
			+ (lunge != null ? "lungeAttack" : "-") + "/"
			+ (firewave != null ? "firewave" : "-") + "/"
			+ (shockWave != null ? "shockWave" : "-") + "/"
			+ (grab != null ? "grab" : "-") + "/"
			+ (backDash != null ? "backDash" : "-") + "/"
			+ (overshield != null ? "overshield" : "-") + "/"
			+ (taunt != null ? "taunt" : "-") + "/"
			+ (splitScreen != null ? "splitScreenTest" : "-"));
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

	/// <summary>The base behaviour AI auto-attacks; fully disabled.</summary>
	public override void behaviourAi()
	{
		// no-op: player-controlled boss
	}

	/// <summary>Swallow any AI-queued attack (belt &amp; suspenders).</summary>
	public override void queueAttack(OldMobSkill a, bool requiresTarget, int? data)
	{
		Utils.log("HeroQueen: AI attack swallowed (" + (a != null ? a.id.ToString() : "null") + ")");
	}

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
				Utils.log("HeroQueen base.preUpdate FAILED: " + ex);
				errLogCd = 300;
			}
			else errLogCd--;
		}
	}

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
				Utils.log("HeroQueen base.fixedUpdate FAILED: " + ex);
				errLogCd = 300;
			}
			else errLogCd--;
		}
		if (!PlayableMOB.config.Value.enabled) return;
		if (curState == MobState.Dead) return;
		if (((Entity)this).isUnconscious()) { reset(); return; }

		// Skills need a target to aim; prefer whatever the threat system picked,
		// fall back to the (hidden) hero at the boss position.
		if (((dc.en.Mob)this).aTarget == null)
		{
			((dc.en.Mob)this).aTarget = dc.pr.Game.Class.ME.hero;
		}

		bool charging = (lunge != null && lunge.chargeF > 0)
			|| (firewave != null && firewave.chargeF > 0)
			|| (shockWave != null && shockWave.chargeF > 0)
			|| (grab != null && grab.chargeF > 0)
			|| (backDash != null && backDash.chargeF > 0)
			|| (overshield != null && overshield.chargeF > 0)
			|| (taunt != null && taunt.chargeF > 0);
		if (!charging && curState != MobState.Idle)
		{
			stateCd -= 1.0 / 60.0;
			if (stateCd <= 0) reset();
		}

		if (curState == MobState.Idle)
		{
			if (Utils.pressed(keys["skill1"])) TryUse("J/lungeAttack", lunge);
			if (Utils.pressed(keys["skill2"])) TryUse("K/firewave", firewave);
			if (Utils.pressed(keys["skill3"])) TryUse("L/shockWave", shockWave);
			if (Utils.pressed(keys["skill4"])) TryUse("U/grab", grab);
			if (Key.Class.isPressed.Invoke(72)) TryUse("H/backDash", backDash);
			if (Key.Class.isPressed.Invoke(88)) TryUse("X/overshield", overshield);
			if (Key.Class.isPressed.Invoke(67)) TryUse("C/taunt", taunt);
			if (Key.Class.isPressed.Invoke(73)) FireSplitScreen(); // I
		}

		// Movement
		if (canMove && !((Entity)this).moveBlocked())
		{
			if (Utils.held(keys["right"])) { if (canTurn) ((Entity)this).dir = 1; ((Entity)this).dx = 0.15 * ((dc.en.Mob)this).getMoveSpeedMul(); }
			else if (Utils.held(keys["left"])) { if (canTurn) ((Entity)this).dir = -1; ((Entity)this).dx = -0.15 * ((dc.en.Mob)this).getMoveSpeedMul(); }
		}

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
			Utils.log("HeroQueen: " + label + " pressed but skill not loaded");
			return;
		}
		bool ready = sk.isReady();
		sk.coolDownF = 0;
		bool ok = sk.prepareOnOwnerTarget(true, null);
		Utils.log("HeroQueen: " + label + " pressed -> ready=" + ready
			+ " prepared=" + ok + " active=" + sk.active
			+ " cd=" + sk.coolDownF + " charge=" + sk.chargeF);
	}

	/// <summary>
	/// Dimensional slash on O. splitScreenTest's own execute only spawns the
	/// visual (its damage is gated behind the AI's cut-screen counter), so use
	/// Queen.singleCutLineAttack instead: it fires one full slash (visual +
	/// delayed damage) per call. Fire several at random angles for the
	/// bossLevel-6 "many slashes" feel.
	/// </summary>
	private void FireSplitScreen()
	{
		Utils.SyncHeroToFront((Entity)this);
		if (splitScreen == null)
		{
			Utils.log("HeroQueen: I pressed but splitScreenTest not loaded");
			return;
		}
		splitScreen.coolDownF = 0;
		double cx = (((Entity)this).cx + ((Entity)this).xr) * 24.0;
		double cy = (((Entity)this).cy + ((Entity)this).yr) * 24.0 - ((Entity)this).hei * 0.5;
		// Fan the cut lines out: full-circle angles, and each line's center is
		// shifted perpendicular to its own direction so they no longer all
		// cross at the boss position.
		int count = 10;
		int fired = 0;
		for (int i = 0; i < count; i++)
		{
			double angle = (i / (double)count) * System.Math.PI * 2.0 + (Utils.random.NextDouble() - 0.5) * 0.4;
			double shift = (Utils.random.NextDouble() - 0.5) * 2.0 * 120.0;
			double lx = cx - System.Math.Sin(angle) * shift;
			double ly = cy + System.Math.Cos(angle) * shift;
			try
			{
				singleCutLineAttack(lx, ly, angle);
				fired++;
			}
			catch (Exception ex)
			{
				Utils.log("HeroQueen slash " + i + " FAILED: " + ex);
			}
		}
		curState = MobState.Slash2; stateCd = 1.0;
		Utils.log("HeroQueen: I/splitScreenTest -> fired " + fired + " slashes");
	}

	public override void onDie()
	{
		curState = MobState.Dead; canTurn = canMove = false;
		lunge?.interrupt(); firewave?.interrupt(); shockWave?.interrupt(); grab?.interrupt();
		backDash?.interrupt(); overshield?.interrupt(); taunt?.interrupt(); splitScreen?.interrupt();
		base.onDie();
	}

	public override void destroy() { inst = null; base.destroy(); }
}
