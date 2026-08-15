#nullable disable

using System;
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
	/// 自由视角开启时，角色照常用 WASD 行动，视角用 IJKL / 方向键平移，
	/// 用 Q / E 缩放；按 F 立即回到跟随角色，按 C 切换模式，F8 开关 HUD。
	/// 自带完整的选项菜单和屏幕 HUD。
	/// </summary>
	public class CameraModMain : ModBase, IOnHeroUpdate, IOnGameExit, IModMenu
	{
		// ---------------- 快捷键 ----------------
		private const int VK_C = 0x43; // 切换自由摄像机
		private const int VK_F = 0x46; // 回到角色（跟随）
		private const int VK_F8 = 0x77; // 开关 HUD
		private const int VK_I = 0x49; // 向上平移
		private const int VK_J = 0x4A; // 向左平移
		private const int VK_K = 0x4B; // 向下平移
		private const int VK_L = 0x4C; // 向右平移
		private const int VK_Q = 0x51; // 缩小
		private const int VK_E = 0x45; // 放大
		private const int VK_OEM_PLUS = 0xBB; // 放大（备用键）
		private const int VK_OEM_MINUS = 0xBD; // 缩小（备用键）
		private const int VK_SHIFT = 0x10; // 快速平移
		private const int VK_CONTROL = 0x11; // 慢速平移
		private const int VK_UP = 0x26;
		private const int VK_DOWN = 0x28;
		private const int VK_LEFT = 0x25;
		private const int VK_RIGHT = 0x27;

		private const double TileSize = 24.0; // 一格 = 24 世界像素
		private const double DiagonalNorm = 0.7071067811865476; // 斜向移动归一化

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
		private static double _zoomTarget = 1.0; // 期望的缩放值
		private static double _originalMinZoom = 1.0; // 进入自由视角前游戏原来的最小缩放
		private static bool _minZoomCaptured;

		private static bool _prevToggleDown;
		private static bool _prevFollowDown;
		private static bool _prevHudDown;
		private static bool _errLogged;

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
			_freeCam = config.Value.freeCamActive;
			_zoomTarget = Clamp(config.Value.zoom, config.Value.minZoom, config.Value.maxZoom);
			_log.Information("[CameraMod] 已加载：C=自由视角，IJKL/方向键=平移，Q/E=缩放，F=跟随角色，F8=HUD");
		}

		// ================= 选项菜单（IModMenu） =================

		public string GetName() => "CameraMod";

		public void BuildMenu(dc.ui.Options options)
		{
			((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(
				StringUtils.AsHaxeString("摄像机 MOD 设置"));
			((dc.ui.OptionsBase)options).createScroller(0.0);

			bool enabled = config.Value.enabled;
			((dc.ui.OptionsBase)options).addToggleWidget(
				StringUtils.AsHaxeString("启用摄像机 Mod"),
				StringUtils.AsHaxeString("整个模组的总开关"),
				(HlFunc<bool>)delegate
				{
					Enabled = !Enabled;
					config.Save();
					return Enabled;
				},
				new Ref<bool>(ref enabled),
				((dc.ui.OptionsBase)options).scrollerFlow);

			bool freeCam = config.Value.freeCamActive;
			((dc.ui.OptionsBase)options).addToggleWidget(
				StringUtils.AsHaxeString("自由摄像机模式"),
				StringUtils.AsHaxeString("让视角与角色分离（游戏内按 C 快速切换）"),
				(HlFunc<bool>)delegate
				{
					config.Value.freeCamActive = !config.Value.freeCamActive;
					config.Save();
					return config.Value.freeCamActive;
				},
				new Ref<bool>(ref freeCam),
				((dc.ui.OptionsBase)options).scrollerFlow);

			bool hudVisible = config.Value.hudVisible;
			((dc.ui.OptionsBase)options).addToggleWidget(
				StringUtils.AsHaxeString("显示 HUD"),
				StringUtils.AsHaxeString("屏幕上的摄像机信息面板（游戏内按 F8 快速切换）"),
				(HlFunc<bool>)delegate
				{
					config.Value.hudVisible = !config.Value.hudVisible;
					config.Save();
					return config.Value.hudVisible;
				},
				new Ref<bool>(ref hudVisible),
				((dc.ui.OptionsBase)options).scrollerFlow);

			bool keyHints = config.Value.showKeyHints;
			((dc.ui.OptionsBase)options).addToggleWidget(
				StringUtils.AsHaxeString("显示按键提示"),
				StringUtils.AsHaxeString("在 HUD 中显示快捷键说明"),
				(HlFunc<bool>)delegate
				{
					config.Value.showKeyHints = !config.Value.showKeyHints;
					config.Save();
					return config.Value.showKeyHints;
				},
				new Ref<bool>(ref keyHints),
				((dc.ui.OptionsBase)options).scrollerFlow);

			((dc.ui.OptionsBase)options).addSeparator(
				StringUtils.AsHaxeString("移动"),
				((dc.ui.OptionsBase)options).scrollerFlow);

			((dc.ui.OptionsBase)options).addSliderWidget(
				StringUtils.AsHaxeString("平移速度"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.panSpeed = v;
					config.Save();
				},
				config.Value.panSpeed,
				Ref<double>.In(50.0),
				((dc.ui.OptionsBase)options).scrollerFlow,
				Ref<bool>.In(false),
				Ref<bool>.In(true),
				Ref<double>.In(50.0),
				Ref<double>.In(4000.0),
				null,
				Ref<int>.In(0));

			((dc.ui.OptionsBase)options).addSliderWidget(
				StringUtils.AsHaxeString("缩放速度"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.zoomSpeed = v;
					config.Save();
				},
				config.Value.zoomSpeed,
				Ref<double>.In(0.1),
				((dc.ui.OptionsBase)options).scrollerFlow,
				Ref<bool>.In(false),
				Ref<bool>.In(true),
				Ref<double>.In(0.1),
				Ref<double>.In(5.0),
				null,
				Ref<int>.In(0));

			((dc.ui.OptionsBase)options).addSeparator(
				StringUtils.AsHaxeString("缩放范围"),
				((dc.ui.OptionsBase)options).scrollerFlow);

			((dc.ui.OptionsBase)options).addSliderWidget(
				StringUtils.AsHaxeString("最小缩放（缩小）"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.minZoom = v;
					config.Save();
				},
				config.Value.minZoom,
				Ref<double>.In(0.05),
				((dc.ui.OptionsBase)options).scrollerFlow,
				Ref<bool>.In(false),
				Ref<bool>.In(true),
				Ref<double>.In(0.3),
				Ref<double>.In(1.0),
				null,
				Ref<int>.In(0));

			((dc.ui.OptionsBase)options).addSliderWidget(
				StringUtils.AsHaxeString("最大缩放（放大）"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.maxZoom = v;
					config.Save();
				},
				config.Value.maxZoom,
				Ref<double>.In(0.1),
				((dc.ui.OptionsBase)options).scrollerFlow,
				Ref<bool>.In(false),
				Ref<bool>.In(true),
				Ref<double>.In(1.5),
				Ref<double>.In(8.0),
				null,
				Ref<int>.In(0));

			((dc.ui.OptionsBase)options).addSeparator(
				StringUtils.AsHaxeString("行为"),
				((dc.ui.OptionsBase)options).scrollerFlow);

			bool smoothMove = config.Value.smoothMove;
			((dc.ui.OptionsBase)options).addToggleWidget(
				StringUtils.AsHaxeString("平滑平移"),
				StringUtils.AsHaxeString("镜头移动带阻尼缓动"),
				(HlFunc<bool>)delegate
				{
					config.Value.smoothMove = !config.Value.smoothMove;
					config.Save();
					return config.Value.smoothMove;
				},
				new Ref<bool>(ref smoothMove),
				((dc.ui.OptionsBase)options).scrollerFlow);

			bool smoothZoom = config.Value.smoothZoom;
			((dc.ui.OptionsBase)options).addToggleWidget(
				StringUtils.AsHaxeString("平滑缩放"),
				StringUtils.AsHaxeString("缩放过渡动画"),
				(HlFunc<bool>)delegate
				{
					config.Value.smoothZoom = !config.Value.smoothZoom;
					config.Save();
					return config.Value.smoothZoom;
				},
				new Ref<bool>(ref smoothZoom),
				((dc.ui.OptionsBase)options).scrollerFlow);

			((dc.ui.OptionsBase)options).updateScroller();
		}

		private static bool Enabled
		{
			get => config.Value.enabled;
			set => config.Value.enabled = value;
		}

		// ================= 每帧更新 =================

		void IOnHeroUpdate.OnHeroUpdate(double dt)
		{
			try
			{
				if (!config.Value.enabled)
				{
					CleanupHud();
					// 模组被禁用时保证镜头没有被“丢下”
					if (_freeCam)
					{
						_freeCam = false;
						var g = dc.pr.Game.Class.ME;
						if (g != null && g.curLevel != null && g.curLevel.viewport != null)
						{
							Entity h = g.hero as Entity;
							if (h != null && !h.destroyed && g.curLevel.viewport.tracked == null)
							{
								g.curLevel.viewport.track(h, true);
							}
						}
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
						_zoomTarget = Clamp(config.Value.zoom, config.Value.minZoom, config.Value.maxZoom);
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
			bool cDown = IsKeyDown(VK_C);
			if (cDown && !_prevToggleDown)
			{
				if (_freeCam) EnterFollow(vp, hero);
				else EnterFreeCam(level, vp, hero);
			}
			_prevToggleDown = cDown;

			bool fDown = IsKeyDown(VK_F);
			if (fDown && !_prevFollowDown && _freeCam)
			{
				EnterFollow(vp, hero);
			}
			_prevFollowDown = fDown;

			bool f8Down = IsKeyDown(VK_F8);
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
			_zoomTarget = Clamp(config.Value.zoom, config.Value.minZoom, config.Value.maxZoom);

			if (!_minZoomCaptured)
			{
				_originalMinZoom = vp.minZoom;
				_minZoomCaptured = true;
			}
			vp.minZoom = SysMath.Min(_originalMinZoom, config.Value.minZoom);

			config.Save();
			_log.Information("[CameraMod] 自由视角已开启（IJKL/方向键平移，Q/E 缩放，F 跟随，C 切换）");
		}

		private static void EnterFollow(Viewport vp, Entity hero)
		{
			_freeCam = false;
			config.Value.freeCamActive = false;
			config.Value.zoom = _zoomTarget;

			if (_minZoomCaptured)
			{
				vp.minZoom = _originalMinZoom;
				_minZoomCaptured = false;
			}

			// 立即重新跟踪角色（保留当前缩放）
			vp.track(hero, true);
			config.Save();
			_log.Information("[CameraMod] 已回到跟随角色");
		}

		// ================= 自由视角逻辑 =================

		private static void UpdateFreeCam(dc.pr.Level level, Viewport vp, double dt)
		{
			double pixelScale = dc.Main.Class.ME.pixelScale;
			if (pixelScale < 0.1) pixelScale = 3.0;
			// 以屏幕速度为准：世界像素/秒 = 屏幕像素/秒 / (pixelScale * zoom)
			double speed = config.Value.panSpeed / (pixelScale * SysMath.Max(0.1, vp.zoom));
			if (IsKeyDown(VK_SHIFT)) speed *= 4.0;
			if (IsKeyDown(VK_CONTROL)) speed *= 0.25;

			double dx = 0.0;
			double dy = 0.0;
			if (IsKeyDown(VK_LEFT) || IsKeyDown(VK_J)) dx -= 1.0;
			if (IsKeyDown(VK_RIGHT) || IsKeyDown(VK_L)) dx += 1.0;
			if (IsKeyDown(VK_UP) || IsKeyDown(VK_I)) dy -= 1.0;
			if (IsKeyDown(VK_DOWN) || IsKeyDown(VK_K)) dy += 1.0;
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
			if (IsKeyDown(VK_Q) || IsKeyDown(VK_OEM_MINUS)) _zoomTarget -= zs * dt;
			if (IsKeyDown(VK_E) || IsKeyDown(VK_OEM_PLUS)) _zoomTarget += zs * dt;
			_zoomTarget = Clamp(_zoomTarget, config.Value.minZoom, config.Value.maxZoom);

			double cur = vp.zoom;
			if (SysMath.Abs(cur - _zoomTarget) > 0.0005)
			{
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
			else if (cur != _zoomTarget)
			{
				vp.set_zoom(_zoomTarget);
				vp.updateSizes();
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
				"ZOOM: " + F2(vp.zoom) + "x   [Q/E]",
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
					_freeCam
						? "IJKL/Arrows pan  Q/E zoom  F follow  C toggle  F8 HUD"
						: "C free camera  Q/E zoom  F8 HUD",
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
				config.Value.zoom = _zoomTarget;
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
	}
}
