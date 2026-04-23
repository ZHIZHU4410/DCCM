﻿using dc;
using dc.en;
using dc.en.inter;
using dc.h2d;
using dc.libs;
using dc.tool.atk;
using dc.tool.mod.script;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SampleSimple
{
    public class SimpleMod : ModBase, IOnGameExit, IOnHeroUpdate
    {
        // ---------- 配置参数 (参数可配) ----------
        public int TotalSlashes = 5;            // 一次指令触发的总挥砍次数
        public double SlashInterval = 4.0;      // 每次挥砍之间的间隔时间 (单位：逻辑帧)
        public double AttackRange = 4.0;        // 攻击判定的有效范围

        // ---------- 状态机私有变量 ----------
        private int _remainingSlashes = 0;      // 剩余挥砍计数
        private double _timer = 0;              // 计时器
        private bool _isJKeyPressed = false;    // 防止按键连发

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vkey);
        private const int VK_J = 0x4A; // 使用 J 键模拟攻击指令

        public SimpleMod(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            System.Console.WriteLine("✅ SimpleMod 已加载。按 J 键触发 5 次连斩逻辑。");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 1. 监听攻击指令
            bool isJPressed = GetAsyncKeyState(VK_J) < 0;
            if (isJPressed && !_isJKeyPressed)
            {
                StartMultiSlashEffect();
            }
            _isJKeyPressed = isJPressed;

            // 2. 状态机逻辑：处理多次判定
            UpdateSlashLogic(dt);
        }

        private void StartMultiSlashEffect()
        {
            if (_remainingSlashes > 0) return; 

            _remainingSlashes = TotalSlashes;
            _timer = SlashInterval; 
            System.Console.WriteLine($"[SimpleMod] 触发连斩：预定执行 {TotalSlashes} 次攻击");
        }

        private void UpdateSlashLogic(double dt)
        {
            if (_remainingSlashes <= 0) return;

            _timer += dt;

            if (_timer >= SlashInterval)
            {
                ExecuteSingleHit(); 
                
                _timer = 0;
                _remainingSlashes--;

                if (_remainingSlashes <= 0)
                {
                    System.Console.WriteLine("[SimpleMod] 连斩序列结束");
                }
            }
        }

        private void ExecuteSingleHit()
        {
            try
            {
                var hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero == null || hero.destroyed) return;

                // 视觉效果
                hero._level.viewport.shakeS(0.2, 0.05, 0.05);

                if (hero._team != null)
                {
                    var opponentIterator = hero._team.opponentsIterator.reset(hero._team);
                    while (opponentIterator.hasNext())
                    {
                        Entity target = opponentIterator.next();
                        if (target == null || target.destroyed || target.life <= 0) continue;

                        // 【修复点】：明确使用 System.Math 解决命名冲突
                        double dist = System.Math.Abs(target.cx - hero.cx) + System.Math.Abs(target.cy - hero.cy);

                        if (dist <= AttackRange)
                        {
                            ApplyDamageToTarget(hero, target);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[SimpleMod] 判定执行异常: {ex.Message}");
            }
        }

        private void ApplyDamageToTarget(Hero source, Entity target)
        {
            bool ignoreResist = false;
            target.bump(0.15 * source.dir, 0, ignoreResist);
        }

        // 修改为显式接口实现或普通公共方法，确保编译通过
        public void OnGameExit()
        {
            System.Console.WriteLine("SimpleMod 卸载");
        }
    }
}