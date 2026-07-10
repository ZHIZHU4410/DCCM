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

public class HeroTimeKeeper : TimeKeeper
{
	private MobState curState = MobState.Idle;
	private double stateCd;
	private int jumpHoldFrames;

	public static HeroTimeKeeper? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldSkill? frontAtk, shurikenAtk, dashAtk;

	public static void create(Hero hero)
	{
		if (inst == null)
		{
			Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)hero).cx, ((Entity)hero).cy);
			InjectMarker(roomAt, "battleZone");
			var m = new HeroTimeKeeper(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, 38, 38);
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

	public HeroTimeKeeper(Level lvl, int x, int y, int dmgTier, int lifeTier)
		: base(lvl, x, y, dmgTier, lifeTier) { }

	public override void giveAchievements() { }
	public override void giveHeadFeedback(dc.String h) { }
	public override void giveHeads() { }
	public override void tpHeroBackToTraining() { }

	void reset()
	{
		curState = MobState.Idle; stateCd = 0;
	}

	public override void init()
	{
		Utils.bossInit((dc.en.mob.Boss)this);
		// Keep isOutOfGame=true (set by bossInit) — prevents collisionMode crash with enemies

		if (base.oldSkills != null)
		{
			dynamic skills = base.oldSkills;
			int total = ((dynamic)skills).length;
			if (total >= 7)
			{
				try { shurikenAtk = (OldSkill)(dynamic)skills.getDyn(total - 7); } catch { }
				try { frontAtk = (OldSkill)(dynamic)skills.getDyn(total - 5); } catch { }
				try { dashAtk = (OldSkill)(dynamic)skills.getDyn(total - 1); } catch { }
			}
		}

		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);

		Hijack(frontAtk, MobState.ShieldSlash, 0.4);
		Hijack(shurikenAtk, MobState.ShieldBash, 0.5);
		Hijack(dashAtk, MobState.Slash1, 0.5);

		((dc.en.Mob)this).interruptSkills();
		((dc.en.Mob)this).aTarget = null;

		reset();
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	private void Hijack(OldSkill? sk, MobState st, double cd)
	{
		if (sk == null) return;
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

		bool charging = (frontAtk != null && frontAtk.chargeF > 0)
			|| (shurikenAtk != null && shurikenAtk.chargeF > 0)
			|| (dashAtk != null && dashAtk.chargeF > 0);
		if (!charging && curState != MobState.Idle)
		{
			stateCd -= 1.0 / 60.0;
			if (stateCd <= 0) reset();
		}

		if (curState == MobState.Idle)
		{
			if (Key.Class.isPressed.Invoke(74) && frontAtk != null)    { frontAtk.coolDownF = 0; frontAtk.prepare(null); }
			if (Key.Class.isPressed.Invoke(75) && shurikenAtk != null) { shurikenAtk.coolDownF = 0; shurikenAtk.prepare(null); }
			if (Key.Class.isPressed.Invoke(85) && dashAtk != null)     { dashAtk.coolDownF = 0; dashAtk.prepare(null); }
		}

		MonsterMovement.Apply((Entity)this, this, keys, ref jumpHoldFrames);
	}

	public override void onDie()
	{
		curState = MobState.Dead;
		frontAtk?.interrupt(); shurikenAtk?.interrupt(); dashAtk?.interrupt();
		base.onDie();
	}

	public override void destroy() { inst = null; base.destroy(); }
}
