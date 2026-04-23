﻿﻿﻿﻿using dc;
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
// using dc.pr;  // 移除，避免与 ModCore.Modules.Game 冲突
using dc.shader;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero.activeSkills;
using dc.tool.mod.script;
using dc.tool.weap;
using dc.ui;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using HaxeProxy.Runtime.Internals.Cache;
using ModCore;  // 添加，以使用 Module<T>
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utitities;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SampleSimple
{
    public class SimpleMod : ModBase, IOnHeroUpdate
    {
        public static List<Queencut> queenStrikeStates = new List<Queencut>();
        public static QueenRapier? queen_rapier;
        private static readonly Random random = new Random();

        public struct Queencut
        {
            public Entity angle;
            public double y;
            public double x;
            public double target;
            public double time;
            public double time_max;
        }

        public SimpleMod(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            base.Logger.Information("无限拔刀!");
            Hook_QueenRapier.queenStrike += this.Hook_myqueenStrike;
        }

        public void Hook_myqueenStrike(Hook_QueenRapier.orig_queenStrike orig, QueenRapier self, Entity angle, double y, double x, double target)
        {
            orig(self, angle, y, x, target);
            SimpleMod.queen_rapier = self;

            if (angle != null && !angle.destroyed && angle.life > 0)
            {
                Queencut newCut = new Queencut
                {
                    angle = angle,
                    y = y,
                    x = x,
                    target = target,
                    time = 0.0,
                    time_max = 0.5
                };
                if (CanAddQueen(angle))
                {
                    queenStrikeStates.Add(newCut);
                    base.Logger.Information($"斩击数据已添加，列表当前元素数：{queenStrikeStates.Count}");
                }
            }
        }

        private bool CanAddQueen(Entity e)
        {
            foreach (Queencut cut in queenStrikeStates)
                if (cut.angle == e) return false;
            return true;
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // Module<T> 位于 ModCore 命名空间，Game 位于 ModCore.Modules
            Hero hero = Module<Game>.Instance.HeroInstance;
            if (hero == null) return;
            CheckAndUpdateStrikeStates(dt);
        }

        public void CheckAndUpdateStrikeStates(double deltaTime)
        {
            if (queenStrikeStates == null || queenStrikeStates.Count == 0) return;

            bool hasValidCut = false;
            for (int i = queenStrikeStates.Count - 1; i >= 0; i--)
            {
                Queencut currentCut = queenStrikeStates[i];
                if (currentCut.angle != null && !currentCut.angle.destroyed && currentCut.angle.life > 0)
                {
                    hasValidCut = true;
                    Queencut updatedCut = currentCut;
                    updatedCut.time += deltaTime;

                    if (updatedCut.time >= 0.5)
                    {
                        if (queen_rapier != null && !queen_rapier.destroyed)
                        {
                            queen_rapier.queenStrike(
                                updatedCut.angle,
                                updatedCut.y + (random.NextDouble() - 0.5) * 3.0,
                                updatedCut.x + (random.NextDouble() - 0.5) * 3.0,
                                random.NextDouble() * 3.1415926
                            );
                            base.Logger.Information("触发二次斩击，目标：" + updatedCut.angle.GetType().Name);
                        }
                        updatedCut.time_max *= 0.99;
                        updatedCut.time = 0.5 - updatedCut.time_max;
                    }
                    queenStrikeStates[i] = updatedCut;
                }
                else
                {
                    queenStrikeStates.RemoveAt(i);
                    base.Logger.Information($"移除无效斩击数据，列表当前元素数：{queenStrikeStates.Count}");
                }
            }
            if (!hasValidCut && queenStrikeStates.Count > 0)
            {
                queenStrikeStates.Clear();
                base.Logger.Information("无有效斩击，清空列表");
            }
        }
    }
}