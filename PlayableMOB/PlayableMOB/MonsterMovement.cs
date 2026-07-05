using System.Collections.Generic;
using dc;
using dc.en;

namespace PlayableMOB;

public static class MonsterMovement
{
	public static void Apply(Entity e, Mob m, Dictionary<string, KeyBind> keys, ref int jumpHoldFrames)
	{
		// Horizontal movement
		if (!e.moveBlocked())
		{
			if (Utils.held(keys["right"]))
			{
				e.dir = 1;
				e.dx = 0.15 * m.getMoveSpeedMul();
			}
			else if (Utils.held(keys["left"]))
			{
				e.dir = -1;
				e.dx = -0.15 * m.getMoveSpeedMul();
			}
		}

		// Jump with hold mechanic
		bool onGround = e.cy == e._level.map.getGroundY(e.cx, e.cy);
		if (Utils.pressed(keys["jump"]) && onGround)
		{
			e.dy = -0.5;
			jumpHoldFrames = 8;
		}
		if (Utils.held(keys["jump"]) && jumpHoldFrames > 0 && e.dy < 0.0)
		{
			e.dy = e.dy - 0.06;
			jumpHoldFrames--;
		}
		if (!Utils.held(keys["jump"]))
			jumpHoldFrames = 0;

		// Hold down to stop
		if (Utils.held(keys["down"]) && onGround)
			e.dx = 0.0;
	}
}
