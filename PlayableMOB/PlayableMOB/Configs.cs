using System.Collections.Generic;

namespace PlayableMOB;

public class Configs
{
	public bool enabled = true;

	public MobConfig enforcer = new MobConfig(new Dictionary<string, KeyBind>
	{
		{
			"toggle",
			new KeyBind
			{
				primary = 80
			}
		},
		{
			"up",
			new KeyBind
			{
				primary = 87
			}
		},
		{
			"left",
			new KeyBind
			{
				primary = 65
			}
		},
		{
			"down",
			new KeyBind
			{
				primary = 83
			}
		},
		{
			"right",
			new KeyBind
			{
				primary = 68
			}
		},
		{
			"shieldBash",
			new KeyBind
			{
				primary = 81
			}
		},
		{
			"slash",
			new KeyBind
			{
				primary = 74
			}
		},
		{
			"jump",
			new KeyBind
			{
				primary = 87
			}
		},
		{
			"switchEnforcer",
			new KeyBind
			{
				primary = 49
			}
		},
		{
			"switchMage",
			new KeyBind
			{
				primary = 50
			}
		}
	});

	public MobConfig mage360 = new MobConfig(new Dictionary<string, KeyBind>
	{
		{ "toggle",          new KeyBind { primary = 80 } },
		{ "up",              new KeyBind { primary = 87 } },
		{ "left",            new KeyBind { primary = 65 } },
		{ "down",            new KeyBind { primary = 83 } },
		{ "right",           new KeyBind { primary = 68 } },
		{ "jump",            new KeyBind { primary = 87 } },
		{ "shoot",           new KeyBind { primary = 74 } },
		{ "dodge",           new KeyBind { primary = 75 } },
		{ "switchEnforcer",  new KeyBind { primary = 49 } },
		{ "switchMage",      new KeyBind { primary = 50 } },
	});
}
