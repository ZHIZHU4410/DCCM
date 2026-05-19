using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using CoreLibrary.Core.Extensions;
using dc.h2d;
using dc.pr;
using dc.ui;
using EnemiesVsEnemies.PlantsVsZombies.Inter;
using Hashlink.Proxy.Objects;
using ModCore.Modules;

namespace EnemiesVsEnemies.PlantsVsZombies.Core
{
    public class PlantSpawner
    {
        public static PlantSpawner? Instance;
        public List<PlantEntity> CreatedPlants = new();

        private Level? _pendingLevel;
        private int _pendingCx;
        private int _pendingCy;
        private string[] _pendingOptions = Array.Empty<string>();
        private bool _selectionOpen;
        private bool _isSelectionKeyDown;
        private dc.ui.Text? _selectionPopup;

        private const int VK_ESCAPE = 0x1B;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public PlantSpawner() { }

        public void SpawnPlant(Level lvl, int cx, int cy, string plantType)
        {
            var plant = new PlantEntity(lvl, cx, cy, plantType);
            plant.init();
            CreatedPlants.Add(plant);
        }

        public void RemovePlant(PlantEntity plant)
        {
            CreatedPlants.Remove(plant);
            plant.destroy();
        }

        public void RequestPlacement(Level lvl, int cx, int cy)
        {
            if (_selectionOpen)
            {
                PlantsVsZombiesMod.LogInfo("正在选择植物，当前放置已忽略。");
                return;
            }

            _pendingLevel = lvl;
            _pendingCx = cx;
            _pendingCy = cy;
            _pendingOptions = PlantsVsZombiesMod.config.Value.Presets.Keys.ToArray();
            OpenSelection();
        }

        public void Update()
        {
            if (!_selectionOpen) return;
            HandleSelectionInput();
        }

        private void OpenSelection()
        {
            CloseSelection();

            var root = Main.Class.ME?.root;
            if (root == null) return;

            var lines = new List<string>
            {
                "选择植物："
            };

            for (int i = 0; i < _pendingOptions.Length; i++)
            {
                lines.Add($"{i + 1}. {_pendingOptions[i]}");
            }

            lines.Add("按 1-9 选择，按 ESC 取消");

            var text = string.Join("\n", lines);
            _selectionPopup = Assets.Class.makeText(Lang.Class.t.untranslated(ToHaxeString(text)), null, true, null);
            _selectionPopup.set_textColor(dc.ui.Text.Class.COLORS.get(ToHaxeString("ST")));
            _selectionPopup.set_textAlign(new Align.Left());
            _selectionPopup.scaleX = 2.4f;
            _selectionPopup.scaleY = 2.4f;
            _selectionPopup.x = 20;
            _selectionPopup.y = 60;
            root.addChild(_selectionPopup);
            _selectionOpen = true;
            _isSelectionKeyDown = false;
        }

        private void HandleSelectionInput()
        {
            bool anyKeyDown = false;
            for (int i = 0; i < _pendingOptions.Length && i < 9; i++)
            {
                int vk = 0x30 + (i + 1);
                bool keyDown = GetAsyncKeyState(vk) < 0;
                if (keyDown)
                {
                    anyKeyDown = true;
                    if (!_isSelectionKeyDown)
                    {
                        SpawnPlantAtPending(_pendingOptions[i]);
                        return;
                    }
                }
            }

            bool escDown = GetAsyncKeyState(VK_ESCAPE) < 0;
            if (escDown)
            {
                anyKeyDown = true;
                if (!_isSelectionKeyDown)
                {
                    PlantsVsZombiesMod.LogInfo("已取消植物选择。");
                    CloseSelection();
                    return;
                }
            }

            _isSelectionKeyDown = anyKeyDown;
        }

        private void SpawnPlantAtPending(string plantType)
        {
            if (_pendingLevel != null)
            {
                SpawnPlant(_pendingLevel, _pendingCx, _pendingCy, plantType);
                PlantsVsZombiesMod.LogInfo($"已放置植物：{plantType}（{_pendingCx},{_pendingCy}）");
            }
            CloseSelection();
        }

        private void CloseSelection()
        {
            _selectionOpen = false;
            _pendingLevel = null;
            _pendingOptions = Array.Empty<string>();
            _isSelectionKeyDown = false;
            if (_selectionPopup != null)
            {
                _selectionPopup.remove();
                _selectionPopup = null;
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
