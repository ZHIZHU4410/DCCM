using System.Collections.Generic;

namespace PlayableMOB;

public class I18n
{
	public const string PREFIX = "PlayableMOBMod_";

	public const string SETTINGS_NAME = "PlayableMOBMod_SettingsName";

	public const string SETTINGS_TITLE = "PlayableMOBMod_SettingsTitle";

	public const string SETTINGS_ENABLED = "PlayableMOBMod_SettingsEnabled";

	public const string SETTINGS_ENABLED_SUB = "PlayableMOBMod_SettingsEnabledSubtext";

	public const string SETTINGS_OVERRIDE = "PlayableMOBMod_SettingsOverride";

	public const string SETTINGS_OVERRIDE_SUB = "PlayableMOBMod_SettingsOverrideSubtext";

	public const string SETTINGS_KEYBOARD = "PlayableMOBMod_SettingsKeyboard";

	public const string SETTINGS_KEYBOARD_SUB = "PlayableMOBMod_SettingsKeyboardSubtext";

	public const string SETTINGS_DEFAULT = "PlayableMOBMod_SettingsDefault";

	public const string SETTINGS_DEFAULT_SUB = "PlayableMOBMod_SettingsDefaultSubtext";

	public const string BINDINGS_TOGGLE = "PlayableMOBMod_toggle";

	public const string BINDINGS_UP = "PlayableMOBMod_up";

	public const string BINDINGS_LEFT = "PlayableMOBMod_left";

	public const string BINDINGS_DOWN = "PlayableMOBMod_down";

	public const string BINDINGS_RIGHT = "PlayableMOBMod_right";

	public const string BINDINGS_SHIELDBASH = "PlayableMOBMod_shieldBash";

	public const string BINDINGS_SLASH = "PlayableMOBMod_slash";

	public static Dictionary<string, Dictionary<string, string>> text = new Dictionary<string, Dictionary<string, string>>
	{
		{
			"en",
			new Dictionary<string, string>
			{
				{ "PlayableMOBMod_SettingsName", "Playable Enforcer Settings" },
				{
					"PlayableMOBMod_SettingsTitle",
					"Playable Enforcer Settings".ToUpper()
				},
				{ "PlayableMOBMod_SettingsEnabled", "Activate mod" },
				{ "PlayableMOBMod_SettingsEnabledSubtext", "Achievements are disabled while this mod is activated" },
				{ "PlayableMOBMod_SettingsOverride", "Disable override" },
				{ "PlayableMOBMod_SettingsOverrideSubtext", "Play as both the Enforcer and the Beheaded" },
				{ "PlayableMOBMod_SettingsKeyboard", "Rebind keyboard bindings" },
				{ "PlayableMOBMod_SettingsKeyboardSubtext", "Configure keyboard bindings for this mod" },
				{ "PlayableMOBMod_SettingsDefault", "Default bindings" },
				{ "PlayableMOBMod_SettingsDefaultSubtext", "Reset to default keyboard bindings for this mod" },
				{ "PlayableMOBMod_toggle", "Toggle" },
				{ "PlayableMOBMod_up", "Up" },
				{ "PlayableMOBMod_left", "Left" },
				{ "PlayableMOBMod_down", "Down" },
				{ "PlayableMOBMod_right", "Right" },
				{ "PlayableMOBMod_shieldBash", "Skill: Shield Bash" },
				{ "PlayableMOBMod_slash", "Skill: Slash" }
			}
		},
		{
			"zh",
			new Dictionary<string, string>
			{
				{ "PlayableMOBMod_SettingsName", "可操控盾兵模组设定" },
				{ "PlayableMOBMod_SettingsTitle", "可操控盾兵模组设定" },
				{ "PlayableMOBMod_SettingsEnabled", "激活模组" },
				{ "PlayableMOBMod_SettingsEnabledSubtext", "模组激活时无法获得成就" },
				{ "PlayableMOBMod_SettingsOverride", "关闭覆盖角色" },
				{ "PlayableMOBMod_SettingsOverrideSubtext", "同时操控盾兵和细胞人" },
				{ "PlayableMOBMod_SettingsKeyboard", "重新绑定键盘按键" },
				{ "PlayableMOBMod_SettingsKeyboardSubtext", "设置此模组的键盘按键" },
				{ "PlayableMOBMod_SettingsDefault", "恢复默认按键" },
				{ "PlayableMOBMod_SettingsDefaultSubtext", "重置此模组的键盘按键" },
				{ "PlayableMOBMod_toggle", "切换角色" },
				{ "PlayableMOBMod_up", "上" },
				{ "PlayableMOBMod_left", "左" },
				{ "PlayableMOBMod_down", "下" },
				{ "PlayableMOBMod_right", "右" },
				{ "PlayableMOBMod_shieldBash", "技能：盾击" },
				{ "PlayableMOBMod_slash", "技能：劈砍" }
			}
		},
		{
			"zh-tw",
			new Dictionary<string, string>
			{
				{ "PlayableMOBMod_SettingsName", "可操控盾兵模組設置" },
				{ "PlayableMOBMod_SettingsTitle", "可操控盾兵模組設置" },
				{ "PlayableMOBMod_SettingsEnabled", "啟動模組" },
				{ "PlayableMOBMod_SettingsEnabledSubtext", "模組啟動時無法獲得成就" },
				{ "PlayableMOBMod_SettingsOverride", "關閉覆蓋角色" },
				{ "PlayableMOBMod_SettingsOverrideSubtext", "同時操控盾兵和主角" },
				{ "PlayableMOBMod_SettingsKeyboard", "重新綁定鍵盤輸入" },
				{ "PlayableMOBMod_SettingsKeyboardSubtext", "設置此模組的鍵盤輸入" },
				{ "PlayableMOBMod_SettingsDefault", "恢復默認鍵盤輸入" },
				{ "PlayableMOBMod_SettingsDefaultSubtext", "重置此模組的鍵盤輸入" },
				{ "PlayableMOBMod_toggle", "切換角色" },
				{ "PlayableMOBMod_up", "上" },
				{ "PlayableMOBMod_left", "左" },
				{ "PlayableMOBMod_down", "下" },
				{ "PlayableMOBMod_right", "右" },
				{ "PlayableMOBMod_shieldBash", "技能：盾擊" },
				{ "PlayableMOBMod_slash", "技能：劈砍" }
			}
		}
	};
}
