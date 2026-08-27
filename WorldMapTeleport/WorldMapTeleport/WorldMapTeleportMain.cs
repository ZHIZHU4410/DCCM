using dc;
using dc.cine;
using dc.ui;
using dc.ui.hud;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Mods;
using ModCore.Utilities;
using System;
using System.Runtime.InteropServices;

namespace WorldMapTeleport
{
    /// <summary>
    /// 世界地图传送模组（World Map Teleport）
    ///
    /// 功能：在地图界面里按 Q 进入世界地图后（游戏自带功能，可纵览全部路线/地图），
    ///       方向键移动选择框（worldMapFrameSelect）到目标地图，
    ///       再按【空格】即可直接传送到当前选中的地图。
    ///
    /// 实现：钩住 dc.ui.hud.MiniMap.update —— 该函数在游戏暂停（地图打开）时
    ///       依然每帧执行（世界地图的方向键选择就是在里面处理的），
    ///       所以在这里用 GetAsyncKeyState 检测空格最可靠。
    /// </summary>
    public class WorldMapTeleportMain : ModBase, IOnGameExit
    {
        // ---------- 空格键 ----------
        private const int VK_SPACE = 0x20;

        // 上一帧空格是否按下（边沿检测，避免一按触发多次）
        private bool _spaceWasDown = false;

        // 世界地图提示语（显示在小地图副标题位置）
        private const string HintText = "WorldMapTP: 空格 = 传送到选中的地图 / SPACE = teleport to selected map";

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public WorldMapTeleportMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_MiniMap.update += OnMiniMapUpdate;
            System.Console.WriteLine("[WorldMapTeleport] 已加载：世界地图中移动选择框后按 空格 传送到选中地图");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_MiniMap.update -= OnMiniMapUpdate;
            System.Console.WriteLine("[WorldMapTeleport] 已卸载");
        }

        // ------------------------------------------------------------------
        // MiniMap.update 钩子：每帧执行（含地图全屏/世界地图打开时）
        // ------------------------------------------------------------------
        private void OnMiniMapUpdate(Hook_MiniMap.orig_update orig, MiniMap self)
        {
            bool spaceDown = IsKeyDown(VK_SPACE);
            bool spacePressedNow = spaceDown && !_spaceWasDown;
            _spaceWasDown = spaceDown;

            if (spacePressedNow)
            {
                TryTeleportFromWorldMap(self);
            }

            orig(self);

            // 在世界地图视图下显示操作提示
            try
            {
                if (self != null && self.get_showWorldMap() && self.subText != null)
                {
                    self.subText.set_text(Lang.Class.t.untranslated(HintText.AsHaxeString()));
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[WorldMapTeleport] 提示文字异常: " + ex.Message);
            }
        }

        private static bool IsKeyDown(int vkey)
        {
            return (GetAsyncKeyState(vkey) & 0x8000) != 0;
        }

        // ------------------------------------------------------------------
        // 世界地图 → 传送
        // ------------------------------------------------------------------
        private static void TryTeleportFromWorldMap(MiniMap miniMap)
        {
            try
            {
                // 只在“世界地图”视图下生效（普通小地图/地图界面不触发）
                if (miniMap == null || !miniMap.get_showWorldMap())
                {
                    return;
                }

                WorldMap worldMap = miniMap.worldMapStruct;
                if (worldMap == null)
                {
                    System.Console.WriteLine("[WorldMapTeleport] worldMapStruct 为空");
                    return;
                }

                // 当前选择框指向的关卡卡片
                dc.h2d.Object selected = worldMap.getSelectedLevelObject();
                if (selected == null || selected.name == null)
                {
                    System.Console.WriteLine("[WorldMapTeleport] 未选中任何地图");
                    return;
                }

                string levelId = selected.name.ToString();
                if (string.IsNullOrEmpty(levelId))
                {
                    return;
                }

                // 校验该 id 是否存在于关卡数据库（避免无效 id 触发崩溃）
                if (!Data.Class.level.byId.exists(levelId.AsHaxeString()))
                {
                    System.Console.WriteLine("[WorldMapTeleport] 地图 " + levelId + " 不在关卡数据库中，已跳过");
                    return;
                }

                System.Console.WriteLine("[WorldMapTeleport] 传送至地图: " + levelId);
                LevelTransition.Class.@goto(levelId.AsHaxeString());
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[WorldMapTeleport] 传送失败: " + ex.Message);
            }
        }
    }
}
