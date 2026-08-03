using System.Collections.Generic;
using HaxeProxy.Runtime;
using dc; using dc.en; using dc.en.hero; using dc.en.inter; using dc.en.mob; using dc.level; using dc.pr; using dc.tool.skill;

namespace PlayableMOB;

public class HeroShopMimic : ShopMimic
{
	private int jhf;
	public static HeroShopMimic? inst { get; private set; }
	public Dictionary<string, KeyBind> keys => PlayableMOB.config.Value.enforcer.bindings;

	private OldMobSkill? cc, midRange, ranged, hook, jumpAway;

	public static void create(Hero h)
	{
		if (inst == null)
		{
			var m = new HeroShopMimic(dc.pr.Game.Class.ME.curLevel, ((Entity)h).cx, ((Entity)h).cy, Utils.DamageTier(), Utils.LifeTier(), new MerchantType.Heals(), new BonusAttackType.All(), null);
			((Entity)m).dir = ((Entity)h).dir;
			// ShopMimic overrides init() — go through Entity.init chain
			((Entity)m).init(); m.playerInit();
		}
	}
	public HeroShopMimic(Level lv, int x, int y, int dt, int lt, MerchantType mt, BonusAttackType ba, ItemDrop? itm) : base(lv, x, y, dt, lt, mt, ba, itm) { }

	public void playerInit()
	{
		if (base.oldSkills != null) { dynamic s = base.oldSkills; int t = ((dynamic)s).length;
			try { jumpAway = (OldMobSkill)(dynamic)s.getDyn(t-1); } catch { }
			try { hook     = (OldMobSkill)(dynamic)s.getDyn(t-2); } catch { }
			try { ranged   = (OldMobSkill)(dynamic)s.getDyn(t-3); } catch { }
			try { midRange = (OldMobSkill)(dynamic)s.getDyn(t-4); } catch { }
			try { cc       = (OldMobSkill)(dynamic)s.getDyn(t-5); } catch { }
		}
		((Entity)this).set_team(dc.pr.Game.Class.ME.curLevel.teamHero);
		inst = this; PlayableMOB.activeMonster = (Entity)this;
	}

	public override void fixedUpdate()
	{
		base.fixedUpdate();
		if (!PlayableMOB.config.Value.enabled) return;
		if (Utils.pressed(keys["skill1"]) && cc != null)       { cc.coolDownF = 0; cc.prepare(null); }
		if (Utils.pressed(keys["skill2"]) && midRange != null) { midRange.coolDownF = 0; midRange.prepare(null); }
		if (Utils.pressed(keys["skill3"]) && ranged != null)   { ranged.coolDownF = 0; ranged.prepare(null); }
		if (Utils.pressed(keys["skill4"]) && hook != null)     { hook.coolDownF = 0; hook.prepare(null); }
		if (Utils.pressed(keys["skill5"]) && jumpAway != null) { jumpAway.coolDownF = 0; jumpAway.prepare(null); }
		MonsterMovement.Apply((Entity)this, this, keys, ref jhf);
	}
	public override void destroy() { inst = null; base.destroy(); }
}
