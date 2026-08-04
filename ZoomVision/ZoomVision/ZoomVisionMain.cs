#nullable disable

using System;
using System.Runtime.InteropServices;
using dc;
using dc.ui;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Storage;
using ModCore.Utilities;
using HaxeProxy.Runtime;

namespace ZoomVision
{
	/// <summary>
	/// Standalone camera-zoom mod: press T to zoom the viewport in, press T
	/// again to restore the normal zoom. The option menu adds a master toggle
	/// and a slider for the zoom multiplier. It talks directly to the level
	/// viewport and does not depend on any boss / monster.
	/// </summary>
	public class ZoomVisionMain : ModBase, IOnHeroUpdate, IOnGameExit, IModMenu
	{
		private const int VK_T = 0x54;
		private const double ZoomSec = 0.4;
		private const double MinScale = 1.0; // 1x = normal
		private const double MaxScale = 3.0; // 3x = biggest zoom
		private const double ScaleStep = 0.1;

		public static Config<Configs> config { get; } = new Config<Configs>("ZoomVision");

		private static bool Enabled
		{
			get => config.Value.enabled;
			set => config.Value.enabled = value;
		}

		/// <summary>Zoom multiplier from the options (1.0 = normal).</summary>
		private static double ZoomScale => config.Value.zoomScale;

		private static bool _lastTDown;
		private static bool _zoomed;
		private static int _errCd;

		public ZoomVisionMain(ModInfo info) : base(info) { }

		public override void Initialize()
		{
			base.Initialize();
			System.Console.WriteLine("[ZoomVision] 视野放大 mod 已加载：按 T 放大 / 再按 T 恢复。选项菜单可调总开关与倍率。");
		}

		// -- IModMenu --
		public string GetName() => "ZoomVision";

		public void BuildMenu(dc.ui.Options options)
		{
			((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(
				StringUtils.AsHaxeString("ZOOMVISION SETTINGS"));
			((dc.ui.OptionsBase)options).createScroller(0.0);

			bool enabled = Enabled;
			((dc.ui.OptionsBase)options).addToggleWidget(
				StringUtils.AsHaxeString("Enable zoom"),
				StringUtils.AsHaxeString("Press T to zoom in / restore"),
				(HlFunc<bool>)delegate
				{
					Enabled = !Enabled;
					config.Save();
					return Enabled;
				},
				new Ref<bool>(ref enabled),
				((dc.ui.OptionsBase)options).scrollerFlow);

			((dc.ui.OptionsBase)options).addSliderWidget(
				StringUtils.AsHaxeString("Zoom multiplier"),
				(HlAction<double>)delegate(double v)
				{
					config.Value.zoomScale = v;
					config.Save();
				},
				ZoomScale,
				Ref<double>.In(ScaleStep),
				((dc.ui.OptionsBase)options).scrollerFlow,
				Ref<bool>.In(false), // showPercent
				Ref<bool>.In(true),  // showRawValue
				Ref<double>.In(MinScale),
				Ref<double>.In(MaxScale),
				null,
				Ref<int>.In(0));

			((dc.ui.OptionsBase)options).updateScroller();
		}

		void IOnHeroUpdate.OnHeroUpdate(double dt)
		{
			try
			{
				var game = dc.pr.Game.Class.ME;
				if (game == null || game.curLevel == null || game.curLevel.viewport == null) return;
				if (game.hero == null || ((Entity)game.hero).destroyed) return;

				bool tDown = IsKeyDown(VK_T);
				if (tDown && !_lastTDown)
				{
					var vp = game.curLevel.viewport;
					if (!Enabled)
					{
						// Master switch off: make sure the view is restored.
						if (_zoomed) { vp.zoomFromTo(vp.zoom, 1.0, ZoomSec, null); _zoomed = false; }
					}
					else
					{
						_zoomed = !_zoomed;
						double target = _zoomed ? 1.0 / System.Math.Max(MinScale, ZoomScale) : 1.0;
						vp.zoomFromTo(vp.zoom, target, ZoomSec, null);
						System.Console.WriteLine("[ZoomVision] viewport zoom -> " + (_zoomed ? (1.0 / target) + "x" : "1x"));
					}
				}
				_lastTDown = tDown;
			}
			catch (Exception ex)
			{
				if (_errCd <= 0)
				{
					System.Console.WriteLine("[ZoomVision] update FAILED: " + ex.Message);
					_errCd = 300;
				}
				else _errCd--;
			}
		}

		void IOnGameExit.OnGameExit()
		{
			_zoomed = false;
			_lastTDown = false;
			System.Console.WriteLine("[ZoomVision] cleaned up.");
		}

		[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
		private static extern short GetAsyncKeyState(int vkey);

		private static bool IsKeyDown(int vkey)
		{
			try { return ((int)GetAsyncKeyState(vkey) & 32768) != 0; }
			catch { return false; }
		}
	}

	/// <summary>Persistent config for the ZoomVision mod.</summary>
	public class Configs
	{
		public bool enabled = true;
		public double zoomScale = 2.0; // 2x zoom by default
	}
}
