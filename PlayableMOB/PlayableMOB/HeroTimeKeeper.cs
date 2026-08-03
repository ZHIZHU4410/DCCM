using System;
using System.Collections.Generic;
using HaxeProxy.Runtime;
using ModCore.Utilities;
using dc;
using dc.en;
using dc.en.hero;
using dc.en.mob;
using dc.en.mob.boss;
using dc.en.mob.boss._TimeKeeper;
using dc.hl;
using dc.hl.types;
using dc.hxd;
using dc.hxd.res;
using dc.level;
using dc.pr;
using dc.tool.skill;

namespace PlayableMOB;

/// <summary>
/// Player-controlled Time Keeper (La Gardienne du Temps). Skills are fetched
/// by id, base AI is disabled and stats scale with the hero's scrolls.
/// </summary>
public class HeroTimeKeeper : TimeKeeper
{
	private bool canTurn = true, canMove = true;
	private MobState curState = MobState.Idle;
	private double stateCd;
	private int jumpHoldFrames;
	private int errLogCd;

	public static HeroTimeKeeper? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	// TimeKeeper's skills are created with createOldSkill() (plain OldSkill,
	// not OldMobSkill), so they are fetched by id and cast to OldSkill.
	// (swordRain is an AI-only cast during the boss fight, not an OldSkill.)
	private OldSkill? front, dash, hook, bigFront, shuriken, smokeBomb, levelUpRadius;

	public static void create(Hero hero)
	{
		if (inst != null && !((Entity)inst).destroyed) return;
		HeroTimeKeeper? m = null;
		try
		{
			Room roomAt = dc.pr.Game.Class.ME.curLevel.map.getRoomAt(((Entity)hero).cx, ((Entity)hero).cy);
			InjectMarker(roomAt, "battleZone", 0, 0, 100, 100);
			m = new HeroTimeKeeper(dc.pr.Game.Class.ME.curLevel, ((Entity)hero).cx, ((Entity)hero).cy, Utils.DamageTier(), Utils.LifeTier());
			((Entity)m).dir = ((Entity)hero).dir;
			((Entity)m).init();
			Utils.log("HeroTimeKeeper created at " + ((Entity)m).cx + "," + ((Entity)m).cy
				+ " dmgTier=" + Utils.DamageTier() + " lifeTier=" + Utils.LifeTier()
				+ " scrolls=" + (hero.brutalityTier + hero.tacticTier + hero.survivalTier));
		}
		catch (Exception ex)
		{
			Utils.log("HeroTimeKeeper.create FAILED: " + ex.GetType().FullName + " | " + ex.Message + "\n" + ex.StackTrace);
			if (m != null) { try { m.destroy(); } catch { } }
			if (inst != null) { try { inst.destroy(); } catch { } inst = null; }
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

	public HeroTimeKeeper(Level lvl, int x, int y, int dmgTier, int lifeTier)
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
		// The boss phase setup (which fills levelUpSteps) never runs here;
		// empty the list so checkForLevelUp() cannot crash or auto level up.
		try { ((dc.en.mob.Boss)this).removeAllLevelUpSteps(); } catch { }
		// TimeKeeper.init() normally loads the weapon animation tracks; the
		// shuriken launch (and hook) read animationTracks.get(...) and crash
		// when it is null.
		try
		{
			Loader loader = Loader.Class.currentInstance;
			Resource res = loader.loadCache(StringUtils.AsHaxeString("atlas/timeKeeper_tracks.json"), (dc.hl.Class)(object)Resource.Class);
			var m = typeof(dc._Assets).GetMethod("getAnimationTracks", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
			var tracks = m != null ? (dc.haxe.ds.StringMap)m.Invoke(null, new object[] { res }) : null;
			((TimeKeeper)this).animationTracks = tracks;
			Utils.log("HeroTimeKeeper animationTracks ok: " + (tracks != null));
		}
		catch (Exception ex) { Utils.log("HeroTimeKeeper animationTracks FAILED: " + ex); }

		front = GetSkill("front");
		dash = GetSkill("dash");
		hook = GetSkill("hook");
		bigFront = GetSkill("bigFront");
		shuriken = GetSkill("shuriken");
		smokeBomb = GetSkill("smokeBomb");
		levelUpRadius = GetSkill("levelUpRadius");

		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);

		Hijack(front, MobState.ShieldSlash, 0.4);
		Hijack(dash, MobState.ShieldBash, 0.5);
		Hijack(hook, MobState.Slash1, 0.5);
		Hijack(bigFront, MobState.ShieldSlash, 0.7);
		Hijack(shuriken, MobState.ShieldBash, 0.5);
		UpgradeHook();
		// smokeBomb/levelUpRadius use plain instance-method callbacks, and
		// replacing those with C# delegates makes the Hashlink VM resolve the
		// calls to the wrong entity (observed: Queen.fixedUpdate). Keep their
		// original callbacks and track state manually instead.

		((dc.en.Mob)this).interruptSkills();
		((dc.en.Mob)this).aTarget = dc.pr.Game.Class.ME.hero;
		// Skills like smokeBomb/shuriken read nemesisTarget.cx on release, so
		// it must point at a live entity (the hidden hero). Autonomous
		// dodging is prevented separately by forcing isInDanger=false every
		// frame and overriding behaviourAi() to a no-op.
		try { ((dc.en.Mob)this).nemesisTarget = dc.pr.Game.Class.ME.hero; } catch { }
		try { ((dc.en.Mob)this).resetQueuedOldSkill(); } catch { }

		reset();
		inst = this; PlayableMOB.activeMonster = (Entity)this;

		Utils.log("HeroTimeKeeper init done: skills="
			+ (front != null ? "front" : "-") + "/"
			+ (dash != null ? "dash" : "-") + "/"
			+ (hook != null ? "hook" : "-") + "/"
			+ (bigFront != null ? "bigFront" : "-") + "/"
			+ (shuriken != null ? "shuriken" : "-") + "/"
			+ (smokeBomb != null ? "smokeBomb" : "-") + "/"
			+ (levelUpRadius != null ? "levelUpRadius" : "-"));
	}

	private OldSkill? GetSkill(string id)
	{
		try { return ((dc.en.Mob)this).getOldSkill(StringUtils.AsHaxeString(id)); }
		catch { return null; }
	}

	private void Hijack(OldSkill? sk, MobState st, double cd)
	{
		if (sk == null) return;
		HlAction origStart = sk.dynOnChargeStart;
		sk.dynOnChargeStart = (HlAction)delegate { origStart?.Invoke(); canTurn = false; canMove = false; };
		HlAction<double> origInterrupt = sk.dynOnInterrupt;
		sk.dynOnInterrupt = delegate(double r) { origInterrupt?.Invoke(r); reset(); };
		HlAction<double> origExecute = sk.dynOnExecute;
		sk.dynOnExecute = delegate(double r) { origExecute?.Invoke(r); curState = st; stateCd = cd; };
	}

	/// <summary>
	/// Hook-on-hit callback: after the vanilla hook hit effects, yank the
	/// hooked enemy right in front of the Time Keeper.
	/// </summary>
	private void UpgradeHook()
	{
		try
		{
			var hk = ((TimeKeeper)this).hook;
			if (hk == null) return;
			// The vanilla targetGetter only returns the Hero (and door
			// socles), so a player-controlled keeper can never hook monsters.
			// Feed it every entity in the level instead; the chain itself
			// skips same-team / unhittable targets, so the hook now grabs the
			// first enemy it touches.
			hk.targetGetter = (HlFunc<ArrayObj>)(() =>
			{
				try { return ((Entity)this)._level.entities; }
				catch { return null; }
			});
			HlAction<Entity> origHook = hk.onHook;
			hk.onHook = (HlAction<Entity>)delegate(Entity e)
			{
				try { origHook?.Invoke(e); } catch (Exception ex) { Utils.log("HeroTimeKeeper hook orig FAILED: " + ex); }
				try
				{
					if (e != null && !e.destroyed)
					{
						e.cancelVelocities();
						e.cx = ((Entity)this).cx + ((Entity)this).dir * 2;
						e.cy = ((Entity)this).cy;
					}
				}
				catch (Exception ex) { Utils.log("HeroTimeKeeper hook pull FAILED: " + ex); }
			};
		}
		catch (Exception ex) { Utils.log("HeroTimeKeeper UpgradeHook FAILED: " + ex); }
	}

	public override void behaviourAi() { }

	public override void queueAttack(OldMobSkill a, bool requiresTarget, int? data)
	{
		Utils.log("HeroTimeKeeper: AI attack swallowed (" + (a != null ? a.id.ToString() : "null") + ")");
	}

	public override void preUpdate()
	{
		try { base.preUpdate(); }
		catch (Exception ex)
		{
			if (errLogCd <= 0) { Utils.log("HeroTimeKeeper base.preUpdate FAILED: " + ex); errLogCd = 300; }
			else errLogCd--;
		}
	}

	public override void fixedUpdate()
	{
		if (((Entity)this).destroyed) return;
		try { base.fixedUpdate(); }
		catch (Exception ex)
		{
			if (errLogCd <= 0) { Utils.log("HeroTimeKeeper base.fixedUpdate FAILED: " + ex); errLogCd = 300; }
			else errLogCd--;
		}
		// Defensive: the danger-check never sees a nearby target, so the
		// keeper never auto-flees/auto-dashes.
		try { ((TimeKeeper)this).isInDanger = false; } catch { }
		if (!PlayableMOB.config.Value.enabled) return;
		if (curState == MobState.Dead) return;
		if (((Entity)this).isUnconscious()) { reset(); return; }
		if (((dc.en.Mob)this).aTarget == null) ((dc.en.Mob)this).aTarget = dc.pr.Game.Class.ME.hero;
		// levelUpRadius's release locks the AI for 99999s; that lock makes
		// subsequent skill prepares skip their lockAiS(0.2) and crash. The AI
		// is disabled anyway, so clear the lock every frame.
		try { ((dc.en.Mob)this).unlockAi(); } catch { }

		bool charging = (front != null && front.chargeF > 0)
			|| (dash != null && dash.chargeF > 0)
			|| (hook != null && hook.chargeF > 0)
			|| (bigFront != null && bigFront.chargeF > 0)
			|| (shuriken != null && shuriken.chargeF > 0)
			|| (smokeBomb != null && smokeBomb.chargeF > 0)
			|| (levelUpRadius != null && levelUpRadius.chargeF > 0);
		if (!charging && curState != MobState.Idle)
		{
			stateCd -= 1.0 / 60.0;
			if (stateCd <= 0) reset();
		}

		if (curState == MobState.Idle)
		{
			if (Utils.pressed(keys["skill1"])) TryUse("J/front", front);
			if (Utils.pressed(keys["skill2"])) TryUse("K/dash", dash);
			if (Utils.pressed(keys["skill3"])) TryUse("L/hook", hook);
			if (Utils.pressed(keys["skill5"])) TryUse("I/bigFront", bigFront);
			if (Key.Class.isPressed.Invoke(72)) TryUse("H/shuriken", shuriken);
			if (Key.Class.isPressed.Invoke(88)) TryUseSmokeBomb();
		}

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

	private void TryUse(string label, OldSkill? sk)
	{
		Utils.SyncHeroToFront((Entity)this);
		if (sk == null) { Utils.log("HeroTimeKeeper: " + label + " pressed but skill not loaded"); return; }
		try
		{
			bool ready = sk.isReady();
			sk.coolDownF = 0;
			bool ok = sk.prepare(null);
			Utils.log("HeroTimeKeeper: " + label + " pressed -> ready=" + ready + " prepared=" + ok);
		}
		catch (Exception ex)
		{
			Utils.log("HeroTimeKeeper: " + label + " FAILED: " + ex.GetType().FullName + " | " + ex.Message + "\n" + ex.StackTrace);
			try { sk.interrupt(); } catch { }
			reset();
		}
	}

	/// <summary>smokeBomb without a Hijack wrapper (see playerInit note).</summary>
	private void TryUseSmokeBomb()
	{
		if (smokeBomb == null) { Utils.log("HeroTimeKeeper: X/smokeBomb pressed but skill not loaded"); return; }
		Utils.SyncHeroToFront((Entity)this);
		// Reset stale skill state so repeat casts don't hit leftover values.
		try { smokeBomb.chargeMul = 1.0; } catch { }
		try { smokeBomb.chargeF = 0; } catch { }
		try { smokeBomb.coolDownF = 0; } catch { }
		Utils.log("HeroTimeKeeper X diag: move=" + (((dc.en.Mob)this).move != null)
			+ " chargeMul=" + smokeBomb.chargeMul
			+ " chargeMaxF=" + smokeBomb.chargeMaxF
			+ " chargeF=" + smokeBomb.chargeF);
		try
		{
			bool ready = smokeBomb.isReady();
			smokeBomb.coolDownF = 0;
			bool ok = smokeBomb.prepare(null);
			Utils.log("HeroTimeKeeper: X/smokeBomb pressed -> ready=" + ready + " prepared=" + ok);
			// Near-zero state lock so X can be mashed infinitely.
			if (ok)
			{
				// Attach the level-up slash (sword ring + fx) to the smoke bomb.
				PlayLevelUpSlash();
				curState = MobState.Slash1; stateCd = 0.12;
			}
		}
		catch (Exception ex)
		{
			Utils.log("HeroTimeKeeper: X/smokeBomb FAILED: " + ex.GetType().FullName + " | " + ex.Message + "\n" + ex.StackTrace);
			try { smokeBomb.interrupt(); } catch { }
			reset();
		}
	}

	/// <summary>
	/// Plays the level-up slash effect (fresh sword ring + blade fx) without
	/// touching the real levelUpRadius skill (its execute corrupts the
	/// Hashlink VM in normal rooms). Attached to the X smoke bomb.
	/// </summary>
	private void PlayLevelUpSlash()
	{
		Utils.SyncHeroToFront((Entity)this);
		// Fresh sword ring every cast (re-showing the same swords crashes).
		RebuildRadiusSwords();
		try
		{
			var tk = (TimeKeeper)this;
			var rs = tk.radiusSwords;
			if (rs != null)
			{
				double baseR = 60.0;
				try { baseR = tk.levelUpRadiusArea.widPx * 0.5; } catch { }
				for (int i = 0; i < rs.length; i++)
				{
					object o = rs.getDyn(i);
					if (o != null) { try { ((RadiusSword)o).show(baseR); } catch { } }
				}
			}
		}
		catch (Exception ex) { Utils.log("HeroTimeKeeper sword ring FAILED: " + ex); }
		try
		{
			var fx = ((Entity)this)._level.fx;
			if (fx != null)
			{
				int play = 1;
				fx.playTimeKeeperAttack(this, StringUtils.AsHaxeString("fxKingsBladeCast"), ref play, 16773535, 16530435);
			}
		}
		catch (Exception ex) { Utils.log("HeroTimeKeeper C fx FAILED: " + ex); }
		Utils.log("HeroTimeKeeper: level-up slash attached to X");
		curState = MobState.Slash2; stateCd = 0.12;
	}

	private void RebuildRadiusSwords()
	{
		try
		{
			var tk = (TimeKeeper)this;
			var old = tk.radiusSwords;
			if (old != null)
			{
				for (int i = 0; i < old.length; i++)
				{
					object o = old.getDyn(i);
					if (o != null) { try { ((RadiusSword)o).destroy(); } catch { } }
				}
			}
			var ns = (ArrayObj)ArrayUtils.CreateDyn().array;
			for (int i = 0; i < 12; i++) ns.push((object)new RadiusSword(tk, i, 12));
			tk.radiusSwords = ns;
		}
		catch (Exception ex) { Utils.log("HeroTimeKeeper RebuildRadiusSwords FAILED: " + ex); }
	}

	public override void onDie()
	{
		curState = MobState.Dead; canTurn = canMove = false;
		front?.interrupt(); dash?.interrupt(); hook?.interrupt();
		bigFront?.interrupt(); shuriken?.interrupt(); smokeBomb?.interrupt(); levelUpRadius?.interrupt();
		base.onDie();
	}

	public override void destroy() { inst = null; base.destroy(); }
}
