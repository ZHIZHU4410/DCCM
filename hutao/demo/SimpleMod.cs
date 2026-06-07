﻿using dc;
using dc.cine;
using dc.en;
using dc.en.active;
using dc.en.bu;
using dc.en.inter;
using dc.en.mob;
using dc.en.mob.boss;
using dc.en.mob.boss.giant;
using dc.en.pet;
using dc.h2d;
using dc.h2d.col;
using dc.h3d.impl;
using dc.h3d.mat;
using dc.h3d.pass;
using dc.haxe.io;
using dc.hl;
using dc.hl.types;
using dc.hxbit.enumSer;
using dc.hxd;
using dc.hxd.fs;
using dc.hxd.res;
using dc.hxd.snd;
using dc.hxsl;
using dc.level;
using dc.light;
using dc.pow;
using dc.pr;
using dc.shader;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero;
using dc.tool.hero.activeSkills;
using dc.tool.mod.script;
using dc.tool.weap;
using dc.ui;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using HaxeProxy.Runtime.Internals.Cache;
using Hashlink.Proxy.Objects; // 添加用于字符串转换
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SampleSimple
{
    public class SimpleMod : ModBase, IOnGameExit, IOnHeroUpdate
    {
        // ---------- 关卡深度映射 ----------
        private Dictionary<string, int> biomeWorldDepthMap = new Dictionary<string, int>()
        {
            { "PrisonStart", 0 }, { "PrisonCourtyard", 1 }, { "SewerShort", 1 }, { "PrisonDepths", 1 }, { "PrisonCorrupt", 1 },
            { "PrisonRoof", 2 }, { "Ossuary", 2 }, { "SewerDepths", 2 }, { "Bridge", 3 }, { "BeholderPit", 3 },
            { "StiltVillage", 4 }, { "AncientTemple", 4 }, { "Cemetery", 4 }, { "ClockTower", 5 }, { "Crypt", 5 },
            { "TopClockTower", 6 }, { "Cavern", 5 }, { "Giant", 6 }, { "Castle", 7 }, { "Distillery", 7 },
            { "Throne", 8 }, { "Astrolab", 9 }, { "Observatory", 10 },
            { "Greenhouse", 1 }, { "Swamp", 2 }, { "SwampHeart", 3 }, { "Tumulus", 4 }, { "Cliff", 5 },
            { "GardenerStage", 6 }, { "Shipwreck", 7 }, { "Lighthouse", 8 }, { "QueenArena", 10 },
            { "PurpleGarden", 1 }, { "DookuCastle", 2 }, { "DookuCastleHard", 7 }, { "DeathArena", 3 }, { "DookuArena", 8 }
        };

        private readonly Random _random = new Random();
        private int _currentLevelIndex = 0;
        private double _killDamageBoost = 0.0;
        private int _addedMaxLife = 0;
        private int _killCount = 0;
        private dc.ui.Text? _boostDisplayText;
        private const int VK_F1 = 0x70;
        private bool _isF1KeyPressed = false;
        private bool _isDisplayEnabled = true;
        private bool _isNKeyPressed = false;
        private bool _isMKeyPressed = false;
        private const int VK_N = 0x4E;
        private const int VK_M = 0x4D;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public SimpleMod(ModInfo info) : base(info) { }

        // ---------- 本地字符串转换方法 ----------
        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        public override void Initialize()
        {
            base.Initialize();

            // 直接订阅 HUD 初始化事件，无需接口
            Hook_HUD.initHero += OnHUDInit;

            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUseHook;
            Hook_Entity.applyAttackResult += OnEntityApplyAttackResultHook;
            Hook_Hero.onMobDeath += OnHeroMobDeathHook;

            System.Console.WriteLine("SimpleMod 初始化完成！");
            System.Console.WriteLine("操作指南：[N]传送上一关 [M]传送下一关 [F1]切换增伤显示");
        }

        private void OnHUDInit(Hook_HUD.orig_initHero orig, HUD self)
        {
            orig(self);
            CreateBoostDisplayText();
        }

        private void CreateBoostDisplayText()
        {
            try
            {
                if (_boostDisplayText != null)
                {
                    _boostDisplayText.remove();
                    _boostDisplayText = null;
                }

                var root = Main.Class.ME?.root;
                if (root == null)
                {
                    System.Console.WriteLine("[SimpleMod] 无法获取 UI root，稍后重试");
                    return;
                }
                var initText = Lang.Class.t.untranslated(ToHaxeString("增伤: 0.0% | 最大HP: ?"));
                _boostDisplayText = Assets.Class.makeText(initText, null, true, null);
                _boostDisplayText.set_textColor(dc.ui.Text.Class.COLORS.get(ToHaxeString("ST")));
                _boostDisplayText.set_textAlign(new Align.Left());
                _boostDisplayText.scaleX = 2.7f;
                _boostDisplayText.scaleY = 2.7f;
                _boostDisplayText.x = 20;
                _boostDisplayText.y = 100;
                _boostDisplayText.visible = _isDisplayEnabled;
                root.addChild(_boostDisplayText);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[SimpleMod] 创建 UI 文本失败：{ex.Message}");
            }
        }

        private void OnWeaponUseHook(Hook_HeroWeaponsManager.orig_onWeaponUse orig, HeroWeaponsManager self, Weapon w, int slot)
        {
            orig(self, w, slot);
            if (self.hero != null)
            {
                Hero hero = self.hero;
                if (hero.life > 1)
                {
                    double currentLife = (double)hero.life;
                    int hpCost = (int)(currentLife * 0.1);
                    if (hpCost < 1) hpCost = 1;
                    hero.life = System.Math.Max(1, hero.life - hpCost);
                }
            }
        }

        private void OnEntityApplyAttackResultHook(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attackData)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero != null && attackData.source == hero)
            {
                double currentHpRatio = (double)hero.life / (double)hero.maxLife;
                double missingHpPercent = 1.0 - currentHpRatio;
                double dynamicCap = 1.0 + (0.01 * _killCount);
                double bleedDamageBoost = missingHpPercent * dynamicCap;
                double totalMultiplier = 1.0 + bleedDamageBoost + _killDamageBoost;
                attackData.finalDmg = (int)((double)attackData.finalDmg * totalMultiplier);
            }
            orig(self, attackData);
        }
        private void OnHeroMobDeathHook(Hook_Hero.orig_onMobDeath orig, Hero self, dc.en.Mob mob)
        {
            orig(self, mob);
            if (self != null)
            {
                _killCount++;
                double progress = (double)_killCount / 420.0; 
                _killDamageBoost = System.Math.Exp(2.3 * progress) - 1; 
                const double BASE_HP = 100.0; 
                int targetTotalAddedHp = (int)(BASE_HP * _killDamageBoost * 3.0); // 将最大生命值增长翻倍
                if (targetTotalAddedHp > _addedMaxLife) 
                { 
                    int hpToGive = targetTotalAddedHp - _addedMaxLife; 
                    self.maxLife += hpToGive; 
                    _addedMaxLife += hpToGive; 
                } 
                
                // --- 4. 杀怪回血 (最大生命值的 5%) ---
                // 随着生命值膨胀，回血量也会变多
                int healAmount = (int)((double)self.maxLife * 0.05); 
                self.heal(healAmount); 
            } 
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // F1 切换显示状态
            bool isF1PressedNow = GetAsyncKeyState(VK_F1) < 0;
            if (isF1PressedNow && !_isF1KeyPressed)
            {
                _isDisplayEnabled = !_isDisplayEnabled;
                System.Console.WriteLine($"[SimpleMod] 显示状态切换为: {(_isDisplayEnabled ? "开启" : "关闭")}");
                
                if (_boostDisplayText != null)
                    _boostDisplayText.visible = _isDisplayEnabled;
            }
            _isF1KeyPressed = isF1PressedNow;

            if (_isDisplayEnabled)
                UpdateBoostDisplay();

            // N / M 传送
            bool isNPressedNow = GetAsyncKeyState(VK_N) < 0;
            if (isNPressedNow && !_isNKeyPressed) TeleportToPrevLevel();
            _isNKeyPressed = isNPressedNow;

            bool isMPressedNow = GetAsyncKeyState(VK_M) < 0;
            if (isMPressedNow && !_isMKeyPressed) TeleportToNextLevel();
            _isMKeyPressed = isMPressedNow;
        }

        private void UpdateBoostDisplay()
        {
            if (_boostDisplayText == null)
            {
                CreateBoostDisplayText();
                return;
            }
            
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null) return;
            
            double currentHpRatio = (double)hero.life / (double)hero.maxLife;
            double missingHpPercent = 1.0 - currentHpRatio;
            double dynamicCap = 1.0 + (0.01 * _killCount);
            
            double maxPossibleBleedPercent = dynamicCap * 100.0;
            double currentBleedBoostPercent = missingHpPercent * dynamicCap * 100.0;
            string displayText = $"增伤: +{(_killDamageBoost * 100):F1}% | " +
                                $"残血: +{currentBleedBoostPercent:F1}%/{maxPossibleBleedPercent:F1}% | " +
                                $"击杀: {_killCount} | 最大HP: {hero.maxLife}";
            _boostDisplayText.set_text(ToHaxeString(displayText));
        }

        private void TeleportToPrevLevel()
        {
            try
            {
                dc.en.Hero me = ModCore.Modules.Game.Instance.HeroInstance;
                if (me == null) return;

                string currentMapId = me._level.map.id.ToString();
                if (string.IsNullOrEmpty(currentMapId))
                {
                    System.Console.WriteLine("[传送] 无法获取当前地图ID");
                    return;
                }

                if (!biomeWorldDepthMap.TryGetValue(currentMapId, out int currentWorldDepth))
                {
                    System.Console.WriteLine($"[传送] 当前地图ID {currentMapId} 不在配置列表中");
                    return;
                }

                int targetWorldDepth = currentWorldDepth - 1;
                if (targetWorldDepth <= 0)
                {
                    System.Console.WriteLine("[传送] 已在最浅深度（0），无法向前传送");
                    return;
                }

                List<string> targetMapKeys = biomeWorldDepthMap
                    .Where(kvp => kvp.Value == targetWorldDepth)
                    .Select(kvp => kvp.Key)
                    .ToList();

                if (targetMapKeys.Count == 0)
                {
                    System.Console.WriteLine($"[传送] 未找到深度为 {targetWorldDepth} 的地图");
                    return;
                }

                string targetMapKey = targetMapKeys[_random.Next(targetMapKeys.Count)];
                dc.cine.LevelTransition.Class.@goto(ToHaxeString(targetMapKey));
                System.Console.WriteLine($"[传送] 成功从 {currentMapId} (深度{currentWorldDepth}) 传送至 {targetMapKey} (深度{targetWorldDepth})");

                _currentLevelIndex = targetWorldDepth;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[传送-上一级] 发生错误：{ex.Message}");
            }
        }

        private void TeleportToNextLevel()
        {
            try
            {
                dc.en.Hero me = ModCore.Modules.Game.Instance.HeroInstance;
                if (me == null) return;

                string currentMapId = me._level.map.id.ToString();
                if (string.IsNullOrEmpty(currentMapId))
                {
                    System.Console.WriteLine("[传送] 无法获取当前地图ID");
                    return;
                }

                if (!biomeWorldDepthMap.TryGetValue(currentMapId, out int currentWorldDepth))
                {
                    System.Console.WriteLine($"[传送] 当前地图ID {currentMapId} 不在配置列表中");
                    return;
                }

                int targetWorldDepth = currentWorldDepth + 1;
                int maxWorldDepth = biomeWorldDepthMap.Values.Max();
                if (targetWorldDepth > maxWorldDepth)
                {
                    System.Console.WriteLine($"[传送] 已在最深深度（{maxWorldDepth}），无法向后传送");
                    return;
                }

                List<string> targetMapKeys = biomeWorldDepthMap
                    .Where(kvp => kvp.Value == targetWorldDepth)
                    .Select(kvp => kvp.Key)
                    .ToList();

                if (targetMapKeys.Count == 0)
                {
                    System.Console.WriteLine($"[传送] 未找到深度为 {targetWorldDepth} 的地图");
                    return;
                }

                string targetMapKey = targetMapKeys[_random.Next(targetMapKeys.Count)];
                dc.cine.LevelTransition.Class.@goto(ToHaxeString(targetMapKey));
                System.Console.WriteLine($"[传送] 成功从 {currentMapId} (深度{currentWorldDepth}) 传送至 {targetMapKey} (深度{targetWorldDepth})");

                _currentLevelIndex = targetWorldDepth;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[传送-下一级] 发生错误：{ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            // 取消所有 Hook 订阅，避免内存泄漏
            Hook_HUD.initHero -= OnHUDInit;
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUseHook;
            Hook_Entity.applyAttackResult -= OnEntityApplyAttackResultHook;
            Hook_Hero.onMobDeath -= OnHeroMobDeathHook;

            System.Console.WriteLine("游戏退出，SimpleMod 资源清理");
            if (_boostDisplayText != null)
            {
                _boostDisplayText.remove();
                _boostDisplayText = null;
            }
            _killDamageBoost = 0.0;
        }
    }
}