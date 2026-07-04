using System.Collections.Generic;

namespace PlayableMOB;

public class MobConfig
{
	public bool overrideHero;

	public Dictionary<string, KeyBind> bindings;

	public MobConfig(Dictionary<string, KeyBind> bindings)
	{
		overrideHero = true;
		this.bindings = bindings;
	}
}
