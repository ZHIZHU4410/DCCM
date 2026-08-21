#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using dc;
using dc.en;
using dc.h2d;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Storage;
using ModCore.Utilities;
using HaxeProxy.Runtime;
using Serilog;
using SysMath = System.Math;

namespace CameraMod
{
	/// <summary>
	/// 自由摄像机 Mod：让视角与角色解耦。
	/// 自由视角开启时，角色照常用 WASD 行动，视角用可自定义的按键平移（默认 IJKL / 方向键），
	/// 用 Q/E 缩放；按 F 立即回到跟随角色，按 C 切换模式，F8 开关 HUD。
	/// 所有键位都可在选项菜单「按键设置」中自定义。
	/// 正常（跟随）模式下视野完全跟随游戏自身（原始默认 zoom = 1.0，不会被模组锁定）；
	/// 按 F/C 恢复正常模式时，视野一定回归游戏原始默认视野。
	/// 自带完整的选项菜单和屏幕 HUD。
	/// </summary>
	public class CameraModMain : ModBase, IOnHeroUpdate, IOnGameExit, IModMenu
	{
		// ---------------- 备用/功能键（Windows 虚拟键码） ----------------
		private const int VK_UP = 0x26;    // 方向键上（平移备用键）
		private const int VK_DOWN = 0x28;  // 方向键下（平移备用键）
		private const int VK_LEFT = 0x25;  // 方向键左（平移备用键）
		private const int VK_RIGHT = 0x27; // 方向键右（平移备用键）
		private const int VK_OEM_PLUS = 0xBB;  // 放大（备用键）
		private const int VK_OEM_MINUS = 0xBD; // 缩小（备用键）
		private const int VK_ESCAPE = 0x1B; // 改键时取消
		private const int VK_BACK = 0x08;   // 改键时清除绑定
		private const int VK_DELETE = 0x2E; // 改键时清除绑定

		// ---------------- 默认键位（与 Configs 里的默认值一致） ----------------
		private const int DEF_TOGGLE = 0x43; // C
		private const int DEF_FOLLOW = 0x46; // F
		private const int DEF_HUD = 0x77;    // F8
		private const int DEF_PAN_UP = 0x49;    // I
		private const int DEF_PAN_DOWN = 0x4B;  // K
		private const int DEF_PAN_LEFT = 0x4A;  // J
		private const int DEF_PAN_RIGHT = 0x4C; // L
		private const int DEF_ZOOM_IN = 0x45;  // E
		private const int DEF_ZOOM_OUT = 0x51; // Q
		private const int DEF_FAST = 0x10;  // Shift
		private const int DEF_SLOW = 0x11;  // Ctrl

		private const double TileSize = 24.0; // 一格 = 24 世界像素
		private const double DiagonalNorm = 0.7071067811865476; // 斜向移动归一化
		// 游戏原始视野的缩放值：dc/_Viewport.__inst_construct__ 与 Viewport.unserializeInit 均为 zoom = 1.0
		private const double DefaultZoom = 1.0;

		// HUD 文字颜色（引擎格式为 0xRRGGBB）
		private const int ColorBlack = 0x000000;
		private const int ColorWhite = 0xFFFFFF;
		private const int ColorGreen = 0x66FF88;
		private const int ColorCyan = 0x6FD3FF;
		private const int ColorYellow = 0xFFD966;
		private const int ColorBlue = 0xAAAAFF;
		private const int ColorGray = 0x9FB4C7;
		private const int ColorGray2 = 0xCCCCCC;

		public static Config<Configs> config { get; } = new Config<Configs>("CameraMod");

		private static ILogger _log;
		private static bool _freeCam; // 当前是否处于自由视角
		private static double _camX; // 自由视角的摄像机目标 X（世界像素）
		private static double _camY; // 自由视角的摄像机目标 Y（世界像素）
		private static double _zoomTarget = -1.0; // 期望的缩放值；<0 表示未设定，跟随游戏原始视野（默认 1.0）
		private static double _originalMinZoom = 1.0; // 进入自由视角前游戏原来的最小缩放
		private static bool _minZoomCaptured;
		private static bool _zoomChanging; // 是否正处于我们发起的缩放过渡中（用于跟随模式下的收尾）

		private static bool _prevToggleDown;
		private static bool _prevFollowDown;
		private static bool _prevHudDown;
		private static bool _errLogged;

		// 改键 UI：每个按键行注册一个刷新回调，恢复默认键位时统一刷新显示
		private static readonly List<Action> _keyRefresh = new List<Action>();

		// HUD 控件（屏幕坐标系，挂在 level.root 的 UI 层）
		private static dc.pr.Level _hudLevel;
		private static Text _titleText;
		private static Text _titleShadow;
		private static Text _modeText;
		private static Text _modeShadow;
		private static Text _posText;
		private static Text _posShadow;
		private static Text _zoomText;
		private static Text _zoomShadow;
		private static Text _heroText;
		private static Text _heroShadow;
		private static Text _distText;
		private static Text _distShadow;
		private static Text _keysText;
		private static Text _keysShadow;

		public CameraModMain(ModInfo info) : base(info) { }

		public override void Initialize()
		{
			base.Initialize();
			_log = Logger;
			NormalizeBindings();
			_freeCam = config.Value.freeCamActive;
			// 注意：不在这里从 config.zoom 初始化 _zoomTarget。
			// 普通（跟随）模式下应保持游戏原始视野（Viewport 默认 zoom=1.0），
			// 只有玩家按缩放键或进入自由视角时才允许改变缩放。
			_log.Information("[CameraMod] 已加载：C=自由视角，IJKL/方向键=平移，Q/E=缩放，F=跟随角色，F8=HUD（可在设置中自定义键位）");
		}

		// ================= 选项菜单（IModMenu） =================

		public string GetName() => "CameraMod";

		public void BuildMenu(dc.ui.Options options)
		{
			dc.ui.OptionsBase ob = (dc.ui.OptionsBase)options;
			((dc.ui.Text)ob.title).set_text(StringUtils.AsHaxeString("摄像机 MOD 设置"));
			ob.createScroller(0.0);

			// 标准设置页布局：左右留白 + 80% 宽度（与游戏自带的按键设置页一致）
			try
			{
				int stageW = GetStageWidth();
				double ps = dc.Main.Class.ME.pixelScale;
				if (ps < 0.1) ps = 3.0;
				ob.scrollerFlow.set_paddingLeft((int)(stageW * 0.1) + (int)(ps * 40.0));
				int w = (int)(stageW * 0.8);
				ob.scrollerFlow.set_minWidth((int?)w);
				ob.scrollerFlow.set_maxWidth((int?)w);
			}
			catch { }

			bool enabled = config.Value.enabled;
			ob.addToggleWidget(
				StringUtils.AsHaxeString("启用摄像机 Mod"),
				StringUtils.AsHaxeString("整个模组的总开关"),
				(HlFunc<bool>)delegate
				{
					Enabled = !Enabled;
					config.Save();
					return Enabled;
				},
				new Ref<bool>(ref enabled),
				ob.scrollerFlow);

			bool freeCam = config.Value.freeCamActive;
			ob.addToggleWidget(
				StringUtils.AsHaxeString("自由摄像机模式"),
				StringUtils.AsHaxeString("让视角与角色分离（游戏内按 C 快速切换）"),
				(HlFunc<bool>)delegate
				{
					config.Value.freeCamActive = !config.Value.freeCamActive;
					config.Save();
					return config.Value.freeCamActive;
				},
				new Ref<bool>(ref freeCam),
				ob.scrollerFlow);

			bool hudVisible = config.Value.hudVisible;
			ob.addToggleWidget(
				StringUtils.AsHaxeString("显示 HUD"),
				StringUtils.AsHaxeString("屏幕上的摄像机信息面板（游戏内按 F8 快速切换）"),
				(HlFunc<bool>)delegate
				{
					config.Value.hudVisible = !config.Value.hudVisible;
					config.Save();
					return config.Value.hudVisible;
				},
				new Ref<bool>(ref hudVisible),
				ob.scrollerFlow);

			bool keyHints = config.Value.showKeyHints;
			ob.addToggleWidget(
				StringUtils.AsHaxeString("显示按键提示"),
				StringUtils.AsHaxeString("在 HUD 中显示快捷键说明"),
				(HlFunc<bool>)delegate
				{
					config.Value.showKeyHints = !config.Value.showKeyHints;
					config.Save();
					return config.Value.showKeyHints;
				},
				new Ref<bool>(ref keyHints),
				ob.scrollerFlow);

			ob.addSeparator(
				StringUtils.AsHaxeString("移动"),
				ob.scrollerFlow);

			ob.addSliderWidget(
				StringUtils.AsHaxeString("平移速度"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.panSpeed = v;
					config.Save();
				},
				config.Value.panSpeed,
				Ref<double>.In(50.0),
				ob.scrollerFlow,
				Ref<bool>.In(false),
				Ref<bool>.In(true),
				Ref<double>.In(50.0),
				Ref<double>.In(4000.0),
				null,
				Ref<int>.In(0));

			ob.addSliderWidget(
				StringUtils.AsHaxeString("缩放速度"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.zoomSpeed = v;
					config.Save();
				},
				config.Value.zoomSpeed,
				Ref<double>.In(0.1),
				ob.scrollerFlow,
				Ref<bool>.In(false),
				Ref<bool>.In(true),
				Ref<double>.In(0.1),
				Ref<double>.In(5.0),
				null,
				Ref<int>.In(0));

			ob.addSeparator(
				StringUtils.AsHaxeString("缩放范围"),
				ob.scrollerFlow);

			ob.addSliderWidget(
				StringUtils.AsHaxeString("最小缩放（缩小）"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.minZoom = v;
					config.Save();
				},
				config.Value.minZoom,
				Ref<double>.In(0.05),
				ob.scrollerFlow,
				Ref<bool>.In(false),
				Ref<bool>.In(true),
				Ref<double>.In(0.3),
				Ref<double>.In(1.0),
				null,
				Ref<int>.In(0));

			ob.addSliderWidget(
				StringUtils.AsHaxeString("最大缩放（放大）"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.maxZoom = v;
					config.Save();
				},
				config.Value.maxZoom,
				Ref<double>.In(0.1),
				ob.scrollerFlow,
				Ref<bool>.In(false),
				Ref<bool>.In(true),
				Ref<double>.In(1.5),
				Ref<double>.In(8.0),
				null,
				Ref<int>.In(0));

			ob.addSeparator(
				StringUtils.AsHaxeString("行为"),
				ob.scrollerFlow);

			bool smoothMove = config.Value.smoothMove;
			ob.addToggleWidget(
				StringUtils.AsHaxeString("平滑平移"),
				StringUtils.AsHaxeString("镜头移动带阻尼缓动"),
				(HlFunc<bool>)delegate
				{
					config.Value.smoothMove = !config.Value.smoothMove;
					config.Save();
					return config.Value.smoothMove;
				},
				new Ref<bool>(ref smoothMove),
				ob.scrollerFlow);

			bool smoothZoom = config.Value.smoothZoom;
			ob.addToggleWidget(
				StringUtils.AsHaxeString("平滑缩放"),
				StringUtils.AsHaxeString("缩放过渡动画"),
				(HlFunc<bool>)delegate
				{
					config.Value.smoothZoom = !config.Value.smoothZoom;
					config.Save();
					return config.Value.smoothZoom;
				},
				new Ref<bool>(ref smoothZoom),
				ob.scrollerFlow);

			// ── 按键设置 ──
			_keyRefresh.Clear();
			ob.addSeparator(
				StringUtils.AsHaxeString("按键设置"),
				ob.scrollerFlow);
			ob.addSeparator(
				StringUtils.AsHaxeString("选中一行后按回车（或鼠标点击），再按下要绑定的键；Esc 取消，退格/Delete 清除"),
				ob.scrollerFlow);

			AddKeyBindRow(options, "切换自由摄像机", () => config.Value.keyToggleCam, v => config.Value.keyToggleCam = v);
			AddKeyBindRow(options, "回到角色（跟随）", () => config.Value.keyFollow, v => config.Value.keyFollow = v);
			AddKeyBindRow(options, "开关 HUD", () => config.Value.keyHud, v => config.Value.keyHud = v);
			AddKeyBindRow(options, "平移：上", () => config.Value.keyPanUp, v => config.Value.keyPanUp = v);
			AddKeyBindRow(options, "平移：下", () => config.Value.keyPanDown, v => config.Value.keyPanDown = v);
			AddKeyBindRow(options, "平移：左", () => config.Value.keyPanLeft, v => config.Value.keyPanLeft = v);
			AddKeyBindRow(options, "平移：右", () => config.Value.keyPanRight, v => config.Value.keyPanRight = v);
			AddKeyBindRow(options, "放大", () => config.Value.keyZoomIn, v => config.Value.keyZoomIn = v);
			AddKeyBindRow(options, "缩小", () => config.Value.keyZoomOut, v => config.Value.keyZoomOut = v);
			AddKeyBindRow(options, "快速平移（按住）", () => config.Value.keyFastPan, v => config.Value.keyFastPan = v);
			AddKeyBindRow(options, "慢速平移（按住）", () => config.Value.keySlowPan, v => config.Value.keySlowPan = v);

			bool arrowKeys = config.Value.arrowKeysAlsoPan;
			ob.addToggleWidget(
				StringUtils.AsHaxeString("方向键也可平移"),
				StringUtils.AsHaxeString("自由视角下方向键作为平移备用键"),
				(HlFunc<bool>)delegate
				{
					config.Value.arrowKeysAlsoPan = !config.Value.arrowKeysAlsoPan;
					config.Save();
					return config.Value.arrowKeysAlsoPan;
				},
				new Ref<bool>(ref arrowKeys),
				ob.scrollerFlow);

			AddResetBindingsRow(options);

			ob.updateScroller();
		}

		private static bool Enabled
		{
			get => config.Value.enabled;
			set => config.Value.enabled = value;
		}

		// ================= 按键设置 UI =================

		/// <summary>创建一行“改键”控件：名称 | 当前键 | 改键提示。选中按回车或鼠标点击后，按下任意键即可绑定。</summary>
		private static void AddKeyBindRow(dc.ui.Options options, string label, Func<int> getKey, Action<int> setKey)
		{
			dc.ui.OptionsBase ob = (dc.ui.OptionsBase)options;
			Flow scroller = ob.scrollerFlow;

			dc.ui.OptionWidget row = new dc.ui.OptionWidget(ob, (dc.h2d.Object)(object)scroller);
			row.verticalAlign = new FlowAlign.Middle();
			row.enableInteractive = false;
			row.isInScroller = true;
			row.selectionIsMiddle = true;
			row.horizontalSpacing = 0;
			row.paddingLeft = 0;

			int avail = scroller.minWidth.HasValue ? scroller.minWidth.Value - scroller.paddingLeft : 900;

			// 名称列
			Flow nameFlow = new Flow((dc.h2d.Object)(object)row);
			int? colName = (int?)(int)((double)avail * 0.55);
			nameFlow.minWidth = colName;
			nameFlow.maxWidth = colName;
			Assets.Class.makeText.Invoke(StringUtils.AsHaxeString(label), null, true, nameFlow);

			// 当前键列（可点击）
			Flow keyFlow = new Flow((dc.h2d.Object)(object)row);
			int? colKey = (int?)(int)((double)avail * 0.25);
			keyFlow.minWidth = colKey;
			keyFlow.maxWidth = colKey;
			keyFlow.horizontalAlign = new FlowAlign.Middle();
			dc.ui.Text keyText = Assets.Class.makeText.Invoke(
				StringUtils.AsHaxeString(KeyName(getKey())), null, true, keyFlow);

			// 提示列
			Flow hintFlow = new Flow((dc.h2d.Object)(object)row);
			int? colHint = (int?)(int)((double)avail * 0.2);
			hintFlow.minWidth = colHint;
			hintFlow.maxWidth = colHint;
			hintFlow.horizontalAlign = new FlowAlign.Middle();
			Assets.Class.makeText.Invoke(StringUtils.AsHaxeString("改键"), null, true, hintFlow);

			_keyRefresh.Add(() =>
			{
				try { keyText.set_text(StringUtils.AsHaxeString(KeyName(getKey()))); } catch { }
			});

			ob.widgets.push((object)row);

			int waitFrames = 0;
			bool capturing = false;

			row.onValidate = (HlAction)delegate
			{
				if (capturing) return;
				capturing = true;
				waitFrames = 30; // 等回车键松开，避免把回车本身绑定进去
				ShowPressText(row, ob);
			};

			row.onUpdate = (HlAction)delegate
			{
				if (!capturing) return;
				// 选中状态丢失（例如按方向键导航走了）→ 取消本次改键
				if (ob.curWidgetId != ob.widgets.indexOf((object)row, (int?)null))
				{
					capturing = false;
					try { ob.pressText.visible = false; } catch { }
					return;
				}
				if (waitFrames > 0)
				{
					waitFrames--;
					return;
				}
				int code = PollAnyKeyDown();
				if (code < 0) return;
				capturing = false;
				try { ob.pressText.visible = false; } catch { }
				if (code == VK_ESCAPE)
				{
					// 取消，不改键
				}
				else if (code == VK_BACK || code == VK_DELETE)
				{
					setKey(0); // 清除绑定
					config.Save();
				}
				else
				{
					setKey(code);
					config.Save();
				}
				try { keyText.set_text(StringUtils.AsHaxeString(KeyName(getKey()))); } catch { }
			};

			// 鼠标点击该行进入改键
			keyFlow.set_enableInteractive(true);
			dc.h2d.Interactive it = keyFlow.interactive;
			if (it != null)
			{
				it.propagateEvents = true;
				it.onClick = delegate
				{
					try
					{
						ob.select(ob.widgets.indexOf((object)row, (int?)null), Ref<bool>.Null);
						ob.onValidate();
					}
					catch { }
				};
			}
		}

		/// <summary>创建一行“恢复默认键位”控件。</summary>
		private static void AddResetBindingsRow(dc.ui.Options options)
		{
			dc.ui.OptionsBase ob = (dc.ui.OptionsBase)options;
			Flow scroller = ob.scrollerFlow;

			dc.ui.OptionWidget row = new dc.ui.OptionWidget(ob, (dc.h2d.Object)(object)scroller);
			row.verticalAlign = new FlowAlign.Middle();
			row.enableInteractive = false;
			row.isInScroller = true;
			row.selectionIsMiddle = true;
			row.horizontalSpacing = 0;
			row.paddingLeft = 0;

			int avail = scroller.minWidth.HasValue ? scroller.minWidth.Value - scroller.paddingLeft : 900;

			Flow nameFlow = new Flow((dc.h2d.Object)(object)row);
			int? colName = (int?)(int)((double)avail * 0.55);
			nameFlow.minWidth = colName;
			nameFlow.maxWidth = colName;
			Assets.Class.makeText.Invoke(StringUtils.AsHaxeString("恢复默认键位"), null, true, nameFlow);

			Flow actFlow = new Flow((dc.h2d.Object)(object)row);
			int? colAct = (int?)(int)((double)avail * 0.45);
			actFlow.minWidth = colAct;
			actFlow.maxWidth = colAct;
			actFlow.horizontalAlign = new FlowAlign.Middle();
			dc.ui.Text actText = Assets.Class.makeText.Invoke(StringUtils.AsHaxeString("回车执行"), null, true, actFlow);

			ob.widgets.push((object)row);

			row.onValidate = (HlAction)delegate
			{
				ResetAllBindings();
				foreach (Action a in _keyRefresh)
				{
					try { a(); } catch { }
				}
				try { actText.set_text(StringUtils.AsHaxeString("已恢复默认")); } catch { }
			};

			actFlow.set_enableInteractive(true);
			dc.h2d.Interactive it = actFlow.interactive;
			if (it != null)
			{
				it.propagateEvents = true;
				it.onClick = delegate
				{
					try
					{
						ob.select(ob.widgets.indexOf((object)row, (int?)null), Ref<bool>.Null);
						ob.onValidate();
					}
					catch { }
				};
			}
		}

		/// <summary>把「按键输入中…」提示文字放到指定行中间。</summary>
		private static void ShowPressText(dc.ui.OptionWidget row, dc.ui.OptionsBase ob)
		{
			try
			{
				dc.ui.Text pt = ob.pressText;
				pt.set_text(StringUtils.AsHaxeString("按键输入中…"));
				double cx = row.getGlobalX() + ((Flow)row).get_outerWidth() * 0.5;
				double cy = row.getGlobalY() + ((Flow)row).get_outerHeight() * 0.5;
				pt.x = cx - (double)pt.textWidth * ((dc.h2d.Object)pt).scaleX * 0.5;
				pt.y = cy - (double)pt.textHeight * ((dc.h2d.Object)pt).scaleY * 0.5;
				pt.posChanged = true;
				pt.visible = true;
			}
			catch { }
		}

		private static void ResetAllBindings()
		{
			config.Value.keyToggleCam = DEF_TOGGLE;
			config.Value.keyFollow = DEF_FOLLOW;
			config.Value.keyHud = DEF_HUD;
			config.Value.keyPanUp = DEF_PAN_UP;
			config.Value.keyPanDown = DEF_PAN_DOWN;
			config.Value.keyPanLeft = DEF_PAN_LEFT;
			config.Value.keyPanRight = DEF_PAN_RIGHT;
			config.Value.keyZoomIn = DEF_ZOOM_IN;
			config.Value.keyZoomOut = DEF_ZOOM_OUT;
			config.Value.keyFastPan = DEF_FAST;
			config.Value.keySlowPan = DEF_SLOW;
			config.Save();
		}

		private static void NormalizeBindings()
		{
			Configs c = config.Value;
			if (!IsValidKey(c.keyToggleCam)) c.keyToggleCam = DEF_TOGGLE;
			if (!IsValidKey(c.keyFollow)) c.keyFollow = DEF_FOLLOW;
			if (!IsValidKey(c.keyHud)) c.keyHud = DEF_HUD;
			if (!IsValidKey(c.keyPanUp)) c.keyPanUp = DEF_PAN_UP;
			if (!IsValidKey(c.keyPanDown)) c.keyPanDown = DEF_PAN_DOWN;
			if (!IsValidKey(c.keyPanLeft)) c.keyPanLeft = DEF_PAN_LEFT;
			if (!IsValidKey(c.keyPanRight)) c.keyPanRight = DEF_PAN_RIGHT;
			if (!IsValidKey(c.keyZoomIn)) c.keyZoomIn = DEF_ZOOM_IN;
			if (!IsValidKey(c.keyZoomOut)) c.keyZoomOut = DEF_ZOOM_OUT;
			if (!IsValidKey(c.keyFastPan)) c.keyFastPan = DEF_FAST;
			if (!IsValidKey(c.keySlowPan)) c.keySlowPan = DEF_SLOW;
			config.Save();
		}

		private static bool IsValidKey(int k) => k >= 0 && k < 256;

		private static int GetStageWidth()
		{
			try
			{
				int w = dc.libs.Process.Class.CUSTOM_STAGE_WIDTH;
				if (w > 0) return w;
			}
			catch { }
			try
			{
				int w = dc.hxd.Window.Class.inst.windowWidth;
				if (w > 0) return w;
			}
			catch { }
			return 1920;
		}

		/// <summary>把 Windows 虚拟键码转成可读的名字（用于 HUD 提示和设置菜单显示）。</summary>
		private static string KeyName(int vk)
		{
			if (vk <= 0) return "未设置";
			switch (vk)
			{
				case 0x08: return "Backspace";
				case 0x09: return "Tab";
				case 0x0D: return "Enter";
				case 0x10: return "Shift";
				case 0x11: return "Ctrl";
				case 0x12: return "Alt";
				case 0x1B: return "Esc";
				case 0x20: return "Space";
				case 0x21: return "PageUp";
				case 0x22: return "PageDown";
				case 0x23: return "End";
				case 0x24: return "Home";
				case 0x25: return "Left";
				case 0x26: return "Up";
				case 0x27: return "Right";
				case 0x28: return "Down";
				case 0x2C: return "PrintScreen";
				case 0x2D: return "Insert";
				case 0x2E: return "Delete";
				case 0x5B: return "Win";
				case 0x5C: return "Win";
				case 0x90: return "NumLock";
				case 0x91: return "ScrollLock";
				case 0x6A: return "Numpad*";
				case 0x6B: return "Numpad+";
				case 0x6D: return "Numpad-";
				case 0x6E: return "Numpad.";
				case 0x6F: return "Numpad/";
				case 0xBA: return ";";
				case 0xBB: return "+";
				case 0xBC: return ",";
				case 0xBD: return "-";
				case 0xBE: return ".";
				case 0xBF: return "/";
				case 0xC0: return "`";
				case 0xDB: return "[";
				case 0xDC: return "\\";
				case 0xDD: return "]";
				case 0xDE: return "'";
				case 0xE2: return "\\";
			}
			if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
			if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
			if (vk >= 0x60 && vk <= 0x69) return "Numpad" + (vk - 0x60);
			if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
			return "键" + vk;
		}

		/// <summary>扫描当前按下的第一个键（用于改键）。排除鼠标按键。</summary>
		private static int PollAnyKeyDown()
		{
			for (int i = 1; i < 256; i++)
			{
				if (i == 5 || i == 6) continue; // 鼠标左键 / 右键
				if (IsKeyDown(i)) return i;
			}
			return -1;
		}

		// ================= 每帧更新 =================

		void IOnHeroUpdate.OnHeroUpdate(double dt)
		{
			try
			{
				if (!config.Value.enabled)
				{
					CleanupHud();
					// 模组被禁用时保证镜头没有被“丢下”，并回归原始视野
					if (_freeCam)
					{
						_freeCam = false;
						config.Value.freeCamActive = false;
						var g = dc.pr.Game.Class.ME;
						if (g != null && g.curLevel != null && g.curLevel.viewport != null)
						{
							Viewport dVp = g.curLevel.viewport;
							RestoreOriginalView(dVp);
							Entity h = g.hero as Entity;
							if (h != null && !h.destroyed && dVp.tracked == null)
							{
								dVp.track(h, true);
							}
						}
						config.Save();
					}
					return;
				}

				var game = dc.pr.Game.Class.ME;
				if (game == null || game.curLevel == null || game.curLevel.viewport == null) return;
				dc.pr.Level level = game.curLevel;
				Viewport vp = level.viewport;
				Entity hero = game.hero as Entity;
				if (hero == null || hero.destroyed) return;

				if (_hudLevel != level)
				{
					_hudLevel = level;
					CleanupHud();
					if (_freeCam)
					{
						// 自由视角下进入新关卡：把镜头先放到角色身边
						double hx = ((double)hero.cx + hero.xr) * TileSize;
						double hy = ((double)hero.cy + hero.yr) * TileSize - hero.hei * 0.5;
						_camX = hx;
						_camY = hy;
						_zoomTarget = Clamp(vp.zoom, config.Value.minZoom, config.Value.maxZoom);
					}
				}

				HandleHotkeys(level, vp, hero);

				if (_freeCam)
				{
					vp.stopTracking();
					UpdateFreeCam(level, vp, dt);
				}
				else if (vp.tracked == null)
				{
					// 跟随模式下如果镜头没有目标（新关卡/复活），重新盯住角色
					vp.track(hero, true);
				}

				// Q/E 缩放无论是否自由视角都生效
				UpdateZoom(vp, dt);

				UpdateHud(level, vp, hero);
			}
			catch (Exception ex)
			{
				if (!_errLogged)
				{
					if (_log != null) _log.Error(ex, "[CameraMod] 每帧更新失败");
					_errLogged = true;
				}
			}
		}

		private static void HandleHotkeys(dc.pr.Level level, Viewport vp, Entity hero)
		{
			bool cDown = IsKeyDown(config.Value.keyToggleCam);
			if (cDown && !_prevToggleDown)
			{
				if (_freeCam) EnterFollow(vp, hero);
				else EnterFreeCam(level, vp, hero);
			}
			_prevToggleDown = cDown;

			bool fDown = IsKeyDown(config.Value.keyFollow);
			if (fDown && !_prevFollowDown && _freeCam)
			{
				EnterFollow(vp, hero);
			}
			_prevFollowDown = fDown;

			bool f8Down = IsKeyDown(config.Value.keyHud);
			if (f8Down && !_prevHudDown)
			{
				config.Value.hudVisible = !config.Value.hudVisible;
				config.Save();
			}
			_prevHudDown = f8Down;
		}

		private static void EnterFreeCam(dc.pr.Level level, Viewport vp, Entity hero)
		{
			_freeCam = true;
			config.Value.freeCamActive = true;
			// 从当前镜头位置开始自由移动
			_camX = vp.x;
			_camY = vp.y;
			_zoomTarget = Clamp(vp.zoom, config.Value.minZoom, config.Value.maxZoom);
			_zoomChanging = false;

			// 记录游戏当前的 minZoom，恢复正常模式时还原（minZoom 是游戏自身状态，必须还回去）
			if (!_minZoomCaptured)
			{
				_originalMinZoom = vp.minZoom;
				_minZoomCaptured = true;
			}
			vp.minZoom = SysMath.Min(_originalMinZoom, config.Value.minZoom);

			config.Save();
			_log.Information("[CameraMod] 自由视角已开启（可改键：平移/缩放/跟随/切换/HUD）");
		}

		private static void EnterFollow(Viewport vp, Entity hero)
		{
			_freeCam = false;
			config.Value.freeCamActive = false;

			// 恢复正常模式：缩放回归进入自由视角前的原始视野
			RestoreOriginalView(vp);

			// 立即重新跟踪角色
			if (vp != null && hero != null && !hero.destroyed)
			{
				vp.track(hero, true);
			}
			config.Save();
			_log.Information("[CameraMod] 已回到跟随角色（视野已恢复原始）");
		}

		/// <summary>
		/// 恢复正常模式：视野回归游戏原始视野（默认缩放 1.0），并把 minZoom 还原给游戏。
		/// 无论进入自由视角前视野是否被道具/能力/过场改变，回到跟随模式后都回到默认视野。
		/// </summary>
		private static void RestoreOriginalView(Viewport vp)
		{
			if (vp == null) return;
			try
			{
				if (_minZoomCaptured)
				{
					vp.minZoom = _originalMinZoom;
					_minZoomCaptured = false;
				}
				// 回归游戏原始视野（Viewport 默认 zoom = 1.0，见 dc/_Viewport.cs）
				config.Value.zoom = DefaultZoom;
				if (SysMath.Abs(vp.zoom - DefaultZoom) > 0.0001)
				{
					vp.set_zoom(DefaultZoom);
					vp.updateSizes();
				}
				// 交还缩放控制权：之后完全跟随游戏自身的缩放（含道具/能力/过场带来的视野变化）
				_zoomTarget = -1.0;
				_zoomChanging = false;
			}
			catch { }
		}

		// ================= 自由视角逻辑 =================

		private static void UpdateFreeCam(dc.pr.Level level, Viewport vp, double dt)
		{
			double pixelScale = dc.Main.Class.ME.pixelScale;
			if (pixelScale < 0.1) pixelScale = 3.0;
			// 以屏幕速度为准：世界像素/秒 = 屏幕像素/秒 / (pixelScale * zoom)
			double speed = config.Value.panSpeed / (pixelScale * SysMath.Max(0.1, vp.zoom));
			if (IsKeyDown(config.Value.keyFastPan)) speed *= 4.0;
			if (IsKeyDown(config.Value.keySlowPan)) speed *= 0.25;

			double dx = 0.0;
			double dy = 0.0;
			bool arrows = config.Value.arrowKeysAlsoPan;
			if (IsKeyDown(config.Value.keyPanLeft) || (arrows && IsKeyDown(VK_LEFT))) dx -= 1.0;
			if (IsKeyDown(config.Value.keyPanRight) || (arrows && IsKeyDown(VK_RIGHT))) dx += 1.0;
			if (IsKeyDown(config.Value.keyPanUp) || (arrows && IsKeyDown(VK_UP))) dy -= 1.0;
			if (IsKeyDown(config.Value.keyPanDown) || (arrows && IsKeyDown(VK_DOWN))) dy += 1.0;
			if (dx != 0.0 && dy != 0.0)
			{
				dx *= DiagonalNorm;
				dy *= DiagonalNorm;
			}

			_camX += dx * speed * dt;
			_camY += dy * speed * dt;

			ClampToLevel(level, vp);

			vp.tx = _camX;
			vp.ty = _camY;
			if (!config.Value.smoothMove)
			{
				// 不平滑时直接把镜头挪到位
				vp.x = _camX;
				vp.y = _camY;
				vp.dx = 0.0;
				vp.dy = 0.0;
				vp.updateRealPos();
			}
			// 平滑模式：让游戏自己的 viewport.update() 把 x/y 向 tx/ty 阻尼靠近
		}

		private static void UpdateZoom(Viewport vp, double dt)
		{
			double zs = SysMath.Max(0.05, config.Value.zoomSpeed);
			bool zoomOutKey = IsKeyDown(config.Value.keyZoomOut) || IsKeyDown(VK_OEM_MINUS);
			bool zoomInKey = IsKeyDown(config.Value.keyZoomIn) || IsKeyDown(VK_OEM_PLUS);

			if (_freeCam)
			{
				// 自由视角：缩放目标保持，随按键连续变化
				if (_zoomTarget < 0.0) _zoomTarget = vp.zoom;
				if (zoomOutKey) _zoomTarget -= zs * dt;
				if (zoomInKey) _zoomTarget += zs * dt;
				_zoomTarget = Clamp(_zoomTarget, config.Value.minZoom, config.Value.maxZoom);
				SmoothZoom(vp, dt);
			}
			else if (zoomInKey || zoomOutKey)
			{
				// 跟随模式：按下缩放键时以当前视野为基准临时缩放
				if (_zoomTarget < 0.0) _zoomTarget = vp.zoom;
				if (zoomOutKey) _zoomTarget -= zs * dt;
				if (zoomInKey) _zoomTarget += zs * dt;
				_zoomTarget = Clamp(_zoomTarget, config.Value.minZoom, config.Value.maxZoom);
				SmoothZoom(vp, dt);
			}
			else if (_zoomChanging)
			{
				// 刚松开缩放键，还在收尾过渡：继续完成
				SmoothZoom(vp, dt);
			}
			else
			{
				// 无操作：完全跟随游戏自身的缩放。
				// 游戏（道具/能力/过场等）改变 vp.zoom 时同步我们的目标，
				// 绝不把游戏自己改的缩放钉死。
				if (_zoomTarget < 0.0 || SysMath.Abs(vp.zoom - _zoomTarget) > 0.001)
				{
					_zoomTarget = vp.zoom;
				}
			}
		}

		private static void SmoothZoom(Viewport vp, double dt)
		{
			double cur = vp.zoom;
			if (SysMath.Abs(cur - _zoomTarget) > 0.0005)
			{
				_zoomChanging = true;
				double next;
				if (config.Value.smoothZoom)
				{
					// 帧率无关的指数平滑
					double k = 1.0 - SysMath.Pow(0.002, dt);
					next = cur + (_zoomTarget - cur) * k;
					if (SysMath.Abs(next - _zoomTarget) < 0.0005) next = _zoomTarget;
				}
				else
				{
					next = _zoomTarget;
				}
				vp.set_zoom(next);
				vp.updateSizes();
			}
			else
			{
				if (cur != _zoomTarget)
				{
					vp.set_zoom(_zoomTarget);
					vp.updateSizes();
				}
				_zoomChanging = false;
			}
			// 画面缩放由 level.postUpdate() 每帧根据 viewport.zoom 应用到 scroller
		}

		private static void ClampToLevel(dc.pr.Level level, Viewport vp)
		{
			if (level == null || level.map == null) return;
			try
			{
				// 与游戏计算 viewport 边界相同的逻辑：取两个矩形相交部分
				int minX = SysMath.Max(level.map.viewportRect.xMin, level.map.dynamicViewportRect.xMin);
				int minY = SysMath.Max(level.map.viewportRect.yMin, level.map.dynamicViewportRect.yMin);
				int maxX = SysMath.Min(level.map.viewportRect.xMax, level.map.dynamicViewportRect.xMax);
				int maxY = SysMath.Min(level.map.viewportRect.yMax, level.map.dynamicViewportRect.yMax);

				double loX = minX * TileSize + vp.wid * 0.5;
				double hiX = maxX * TileSize - vp.wid * 0.5;
				double loY = minY * TileSize + vp.hei * 0.5;
				double hiY = maxY * TileSize - vp.hei * 0.5;
				if (hiX < loX) { double mid = (loX + hiX) * 0.5; loX = hiX = mid; }
				if (hiY < loY) { double mid = (loY + hiY) * 0.5; loY = hiY = mid; }
				_camX = Clamp(_camX, loX, hiX);
				_camY = Clamp(_camY, loY, hiY);
			}
			catch
			{
				// 边界数据还没准备好，这帧先跳过
			}
		}

		// ================= HUD =================

		private static void UpdateHud(dc.pr.Level level, Viewport vp, Entity hero)
		{
			if (!config.Value.hudVisible)
			{
				CleanupHud();
				return;
			}
			EnsureHud(level);
			if (_titleText == null) return;

			double camTileX = vp.realX / TileSize;
			double camTileY = vp.realY / TileSize;
			double heroCx = ((double)hero.cx + hero.xr) / 1.0;
			double heroCy = ((double)hero.cy + hero.yr) / 1.0;
			double heroWorldX = heroCx * TileSize;
			double heroWorldY = heroCy * TileSize - hero.hei * 0.5;
			double distPx = SysMath.Sqrt(
				(vp.realX - heroWorldX) * (vp.realX - heroWorldX) +
				(vp.realY - heroWorldY) * (vp.realY - heroWorldY));
			double distTiles = distPx / TileSize;

			SetText(_modeText, _modeShadow,
				"MODE: " + (_freeCam ? "FREE CAM" : "FOLLOW HERO"),
				_freeCam ? ColorGreen : ColorGray2);
			SetText(_posText, _posShadow,
				"CAM X: " + F1(camTileX) + "   Y: " + F1(camTileY) + "  (tiles)",
				ColorWhite);
			SetText(_zoomText, _zoomShadow,
				"ZOOM: " + F2(vp.zoom) + "x   [" + KeyName(config.Value.keyZoomOut) + "/" + KeyName(config.Value.keyZoomIn) + "]",
				ColorYellow);
			SetText(_heroText, _heroShadow,
				"HERO X: " + F1(heroCx) + "   Y: " + F1(heroCy) + "  (tiles)",
				ColorBlue);
			SetText(_distText, _distShadow,
				"DIST: " + F1(distTiles) + " tiles",
				ColorBlue);

			if (config.Value.showKeyHints)
			{
				SetText(_keysText, _keysShadow,
					_freeCam ? KeyHintFreeCam() : KeyHintFollow(),
					ColorGray);
				_keysText.visible = true;
				_keysShadow.visible = true;
			}
			else
			{
				_keysText.visible = false;
				_keysShadow.visible = false;
			}
		}

		private static string KeyHintFreeCam()
		{
			return KeyName(config.Value.keyPanUp) + "/" + KeyName(config.Value.keyPanDown) + "/"
				+ KeyName(config.Value.keyPanLeft) + "/" + KeyName(config.Value.keyPanRight)
				+ (config.Value.arrowKeysAlsoPan ? "+Arrows" : "") + " pan  "
				+ KeyName(config.Value.keyZoomOut) + "/" + KeyName(config.Value.keyZoomIn) + " zoom  "
				+ KeyName(config.Value.keyFollow) + " follow  "
				+ KeyName(config.Value.keyToggleCam) + " toggle  "
				+ KeyName(config.Value.keyHud) + " HUD";
		}

		private static string KeyHintFollow()
		{
			return KeyName(config.Value.keyToggleCam) + " free camera  "
				+ KeyName(config.Value.keyZoomOut) + "/" + KeyName(config.Value.keyZoomIn) + " zoom  "
				+ KeyName(config.Value.keyHud) + " HUD";
		}

		private static void EnsureHud(dc.pr.Level level)
		{
			if (_titleText != null) return;
			if (level == null || level.root == null) return;
			Font font = dc.Assets.Class.font12;
			if (font == null) return;

			try
			{
				int layer = dc.Const.Class.ROOT_DP_CTX_UI;
				_titleShadow = MakeText(font, level, 0, ColorBlack, true);
				_titleText = MakeText(font, level, 0, ColorCyan, false);
				_modeShadow = MakeText(font, level, 1, ColorBlack, true);
				_modeText = MakeText(font, level, 1, ColorGreen, false);
				_posShadow = MakeText(font, level, 2, ColorBlack, true);
				_posText = MakeText(font, level, 2, ColorWhite, false);
				_zoomShadow = MakeText(font, level, 3, ColorBlack, true);
				_zoomText = MakeText(font, level, 3, ColorYellow, false);
				_heroShadow = MakeText(font, level, 4, ColorBlack, true);
				_heroText = MakeText(font, level, 4, ColorBlue, false);
				_distShadow = MakeText(font, level, 5, ColorBlack, true);
				_distText = MakeText(font, level, 5, ColorBlue, false);
				_keysShadow = MakeText(font, level, 7, ColorBlack, true);
				_keysText = MakeText(font, level, 7, ColorGray, false);

				foreach (Text t in new Text[] { _titleShadow, _titleText, _modeShadow, _modeText, _posShadow, _posText, _zoomShadow, _zoomText, _heroShadow, _heroText, _distShadow, _distText, _keysShadow, _keysText })
				{
					level.root.addChildAt(t, layer);
				}

				SetText(_titleText, _titleShadow, "CAMERA MOD", ColorCyan);
			}
			catch (Exception ex)
			{
				if (_log != null) _log.Error(ex, "[CameraMod] HUD 创建失败");
				CleanupHud();
			}
		}

		private static Text MakeText(Font font, dc.pr.Level level, int line, int color, bool isShadow)
		{
			Text t = new Text(font, level.root);
			t.textColor = color;
			t.x = isShadow ? 21.0 : 20.0;
			t.y = isShadow ? 19.0 + line * 26.0 : 18.0 + line * 26.0;
			t.visible = false;
			return t;
		}

		private static void SetText(Text main, Text shadow, string value, int color)
		{
			if (main == null) return;
			dc.String s = StringUtils.AsHaxeString(value);
			main.set_text(s);
			main.textColor = color;
			main.visible = true;
			if (shadow != null)
			{
				shadow.set_text(s);
				shadow.visible = true;
			}
		}

		private static void CleanupHud()
		{
			foreach (Text t in new Text[] { _titleShadow, _titleText, _modeShadow, _modeText, _posShadow, _posText, _zoomShadow, _zoomText, _heroShadow, _heroText, _distShadow, _distText, _keysShadow, _keysText })
			{
				if (t == null) continue;
				try
				{
					if (t.parent != null) t.parent.removeChild(t);
				}
				catch { }
			}
			_titleText = null; _titleShadow = null;
			_modeText = null; _modeShadow = null;
			_posText = null; _posShadow = null;
			_zoomText = null; _zoomShadow = null;
			_heroText = null; _heroShadow = null;
			_distText = null; _distShadow = null;
			_keysText = null; _keysShadow = null;
		}

		// ================= 辅助函数 =================

		void IOnGameExit.OnGameExit()
		{
			try
			{
				CleanupHud();
				_hudLevel = null;
				if (_zoomTarget > 0.0) config.Value.zoom = _zoomTarget;
				config.Save();
			}
			catch { }
			if (_log != null) _log.Information("[CameraMod] 已清理");
		}

		private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
		private static string F2(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

		private static double Clamp(double v, double lo, double hi)
		{
			if (hi < lo) hi = lo;
			if (v < lo) return lo;
			if (v > hi) return hi;
			return v;
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		private static extern short GetAsyncKeyState(int vkey);

		private static bool IsKeyDown(int vkey)
		{
			try { return ((int)GetAsyncKeyState(vkey) & 32768) != 0; }
			catch { return false; }
		}
	}

	/// <summary>CameraMod 的持久化配置。</summary>
	public class Configs
	{
		public bool enabled = true;
		public bool freeCamActive = false;
		public bool hudVisible = true;
		public bool showKeyHints = true;
		public double panSpeed = 900.0;
		public double zoomSpeed = 1.2;
		public double minZoom = 0.5;
		public double maxZoom = 3.0;
		public double zoom = 1.0;
		public bool smoothMove = true;
		public bool smoothZoom = true;

		// ---- 可自定义键位（Windows 虚拟键码，可在设置菜单「按键设置」中修改）----
		public int keyToggleCam = 0x43;   // C 切换自由摄像机
		public int keyFollow = 0x46;      // F 回到角色（跟随）
		public int keyHud = 0x77;         // F8 开关 HUD
		public int keyPanUp = 0x49;       // I 平移：上
		public int keyPanDown = 0x4B;     // K 平移：下
		public int keyPanLeft = 0x4A;     // J 平移：左
		public int keyPanRight = 0x4C;    // L 平移：右
		public int keyZoomIn = 0x45;      // E 放大
		public int keyZoomOut = 0x51;     // Q 缩小
		public int keyFastPan = 0x10;     // Shift 快速平移（按住）
		public int keySlowPan = 0x11;     // Ctrl 慢速平移（按住）
		public bool arrowKeysAlsoPan = true; // 方向键作为平移备用键
	}
}
