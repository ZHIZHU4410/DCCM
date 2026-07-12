using System.Collections.Generic;

namespace PlayableMOB;

public class Configs
{
	public bool enabled = true;

	// Per-monster toggle: key = monster name, value = enabled (defaults to true when absent)
	public Dictionary<string, bool> monsterEnabled = new();

	// Shared config: enforcer section holds global keys + override flag
	public MobConfig enforcer = new MobConfig(new Dictionary<string, KeyBind>
	{
		{ "toggle",    new KeyBind { primary = 80 } },  // P
		{ "cyclePrev", new KeyBind { primary = 49 } },  // 1
		{ "cycleNext", new KeyBind { primary = 50 } },  // 2
		{ "jump",      new KeyBind { primary = 87 } },  // W
		{ "left",      new KeyBind { primary = 65 } },  // A
		{ "right",     new KeyBind { primary = 68 } },  // D
		{ "down",      new KeyBind { primary = 83 } },  // S
		{ "skill1",    new KeyBind { primary = 74 } },  // J
		{ "skill2",    new KeyBind { primary = 75 } },  // K
		{ "skill3",    new KeyBind { primary = 76 } },  // L
		{ "skill4",    new KeyBind { primary = 85 } },  // U
		{ "skill5",    new KeyBind { primary = 73 } },  // I
	});
}
