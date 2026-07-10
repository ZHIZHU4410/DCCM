using System;
using System.Collections.Generic;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.mob;
using dc.en.mob.boss;
using dc.hxd;
using dc.level;
using dc.pr;
using dc.tool.skill;

namespace PlayableMOB;

public class HeroKingsHand : KingsHand
{
	private bool canTurn = true, canMove = true;
	private MobState curState = MobState.Idle;
	private double stateCd;
	private int jumpHoldFrames;

	public static HeroKingsHand? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? ccCircle1, ccCharge1, ccStomp1, globalStompSkill;
	private OldMobSkill? ccHeavy1, shieldChargeSkill, grenadeSkill, megaBombSkill;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)hero).cx, ((Entity)hero).cy);
			// KEY FIX: inject "battleZone" BEFORE construction.
			// _Boss.__inst_construct__ searches for CustomSpot "battleZone" to set battleZone.
			// Without this marker, battleZone=null and any access to battleZone.cx crashes.
			InjectMarker(roomAt, "battleZone");
			var m = new HeroKingsHand(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, 38, 38);
			((Entity)m).dir = ((Entity)hero).dir;
			((Entity)m).init();
		}
	}

	private static void InjectMarker(Room room, string id)
	{
		bool flag = false;
		if (room.getMarker(StringUtils.AsHaxeString("CustomSpot"), StringUtils.AsHaxeString(id), new Ref<bool>(ref flag)) == null)
		{
			var m0 = Utils.copyMarker((dynamic)((dynamic)room.markers).getDyn(0));
			m0.kind = StringUtils.AsHaxeString("CustomSpot");
			m0.cx = 0; m0.cy = 0; m0.width = 100; m0.height = 100;
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
	}

	public override void init()
	{
		Utils.bossInit((dc.en.mob.Boss)this);
		((Entity)this).isOutOfGame = false;

		// battleZone was already set correctly by _Boss constructor
		// (because we injected "battleZone" marker before construction).
		// Keep cameraTrackingDisabled=true (set by bossInit) — avoids camera crash.

		if (base.oldSkills != null)
		{
			dynamic skills = base.oldSkills;
			int total = ((dynamic)skills).length;
			if (total >= 14)
			{
				try { ccCircle1 = (OldMobSkill)(dynamic)skills.getDyn(total - 14); } catch { }
				try { ccHeavy1 = (OldMobSkill)(dynamic)skills.getDyn(total - 12); } catch { }
				try { ccStomp1 = (OldMobSkill)(dynamic)skills.getDyn(total - 11); } catch { }
				try { ccCharge1 = (OldMobSkill)(dynamic)skills.getDyn(total - 9); } catch { }
				try { globalStompSkill = (OldMobSkill)(dynamic)skills.getDyn(total - 6); } catch { }
				try { shieldChargeSkill = (OldMobSkill)(dynamic)skills.getDyn(total - 5); } catch { }
				try { grenadeSkill = (OldMobSkill)(dynamic)skills.getDyn(total - 3); } catch { }
				try { megaBombSkill = (OldMobSkill)(dynamic)skills.getDyn(total - 2); } catch { }
			}
		}

		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);

		Hijack(ccCircle1, MobState.ShieldSlash, 0.4);
		Hijack(ccCharge1, MobState.ShieldBash, 0.5);
		Hijack(ccStomp1, MobState.Slash1, 0.6);
		Hijack(globalStompSkill, MobState.Slash2, 1.0);
		Hijack(ccHeavy1, MobState.Slash2, 0.7);
		Hijack(shieldChargeSkill, MobState.Slash1, 0.7);
		Hijack(grenadeSkill, MobState.ShieldBash, 0.5);
		Hijack(megaBombSkill, MobState.Slash2, 0.8);

		((dc.en.Mob)this).interruptSkills();
		((dc.en.Mob)this).aTarget = null;

		reset();
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	private void Hijack(OldMobSkill? sk, MobState st, double cd)
	{
		if (sk == null) return;
		sk.dynOnChargeStart = (HlAction)delegate { canTurn = false; canMove = false; };
		HlAction<double> origI = sk.dynOnInterrupt;
		sk.dynOnInterrupt = delegate(double r) { origI?.Invoke(r); reset(); };
		HlAction<double> origE = sk.dynOnExecute;
		sk.dynOnExecute = delegate(double r) { origE?.Invoke(r); curState = st; stateCd = cd; };
	}

	public override void fixedUpdate()
	{
		if (((Entity)this).destroyed) return;
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (curState == MobState.Dead) return;
		if (((Entity)this).isUnconscious()) { reset(); return; }

		bool charging = (ccCircle1 != null && ccCircle1.chargeF > 0)
			|| (ccCharge1 != null && ccCharge1.chargeF > 0)
			|| (ccStomp1 != null && ccStomp1.chargeF > 0)
			|| (globalStompSkill != null && globalStompSkill.chargeF > 0)
			|| (ccHeavy1 != null && ccHeavy1.chargeF > 0)
			|| (shieldChargeSkill != null && shieldChargeSkill.chargeF > 0)
			|| (grenadeSkill != null && grenadeSkill.chargeF > 0)
			|| (megaBombSkill != null && megaBombSkill.chargeF > 0);
		if (!charging && curState != MobState.Idle)
		{
			stateCd -= 1.0 / 60.0;
			if (stateCd <= 0) reset();
		}

		if (curState == MobState.Idle)
		{
			if (Key.Class.isPressed.Invoke(74) && ccCircle1 != null)       { ccCircle1.coolDownF = 0; ccCircle1.prepare(null); }
			if (Key.Class.isPressed.Invoke(75) && ccCharge1 != null)       { ccCharge1.coolDownF = 0; ccCharge1.prepare(null); }
			if (Key.Class.isPressed.Invoke(85) && ccStomp1 != null)        { ccStomp1.coolDownF = 0; ccStomp1.prepare(null); }
			if (Key.Class.isPressed.Invoke(73) && globalStompSkill != null){ globalStompSkill.coolDownF = 0; globalStompSkill.prepare(null); }
			if (Key.Class.isPressed.Invoke(71) && ccHeavy1 != null)        { ccHeavy1.coolDownF = 0; ccHeavy1.prepare(null); }
			if (Key.Class.isPressed.Invoke(72) && shieldChargeSkill != null){ shieldChargeSkill.coolDownF = 0; shieldChargeSkill.prepare(null); }
			if (Key.Class.isPressed.Invoke(88) && grenadeSkill != null)    { grenadeSkill.coolDownF = 0; grenadeSkill.prepare(null); }
			if (Key.Class.isPressed.Invoke(67) && megaBombSkill != null)   { megaBombSkill.coolDownF = 0; megaBombSkill.prepare(null); }
		}

		if (canMove && !((Entity)this).moveBlocked())
		{
			if (Utils.held(keys["right"])) { if (canTurn) ((Entity)this).dir = 1; ((Entity)this).dx = 0.15 * ((dc.en.Mob)this).getMoveSpeedMul(); }
			else if (Utils.held(keys["left"])) { if (canTurn) ((Entity)this).dir = -1; ((Entity)this).dx = -0.15 * ((dc.en.Mob)this).getMoveSpeedMul(); }
		}

		bool onGround = ((Entity)this).cy == ((Entity)this)._level.map.getGroundY(((Entity)this).cx, ((Entity)this).cy);
		if (Key.Class.isPressed.Invoke(32) && onGround && canMove) { ((Entity)this).dy = -0.5; jumpHoldFrames = 8; }
		if (Key.Class.isDown.Invoke(32) && jumpHoldFrames > 0 && ((Entity)this).dy < 0) { ((Entity)this).dy -= 0.06; jumpHoldFrames--; }
		if (!Key.Class.isDown.Invoke(32)) jumpHoldFrames = 0;
		if (Utils.held(keys["down"]) && onGround) ((Entity)this).dx = 0;
	}

	public override void onDie()
	{
		curState = MobState.Dead; canTurn = canMove = false;
		ccCircle1?.interrupt(); ccCharge1?.interrupt(); ccStomp1?.interrupt();
		globalStompSkill?.interrupt(); ccHeavy1?.interrupt(); shieldChargeSkill?.interrupt();
		grenadeSkill?.interrupt(); megaBombSkill?.interrupt();
		base.onDie();
	}

	public override void destroy() { inst = null; base.destroy(); }
}
