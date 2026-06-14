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
using ModCore;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;   
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace QueenRapier
{
    public class QueenRapierMain : ModBase, IOnHeroUpdate
    {
        public static List<Queencut> queenStrikeStates = new List<Queencut>();
        public static QueenRapier? queen_rapier;
        private static readonly Random random = new Random();

        // 原切割配置
        private const int TotalHits = 12;               // 总共 12 次（包含原版第一次）
        private const double SlashInterval = 0.12;      // 每次间隔（秒）
        private const double RandomRadius = 230.0;       // 随机位置的半径（像素）

        // 残影相关配置
        private const double TrailAlpha = 0.6;           // 残影透明度（0~1）
        private const double TrailDuration = 0.55;       // 残影持续时间（秒）
        private const double TrailScale = 0.0;           // 残影缩放比例
        private static readonly int TrailColor = unchecked((int)0xFFa0dbdc); // ARGB 青灰色

        public struct Queencut
        {
            public Entity targetEntity;   // 目标实体
            public int remainingHits;     // 剩余切割次数（不含第一次）
            public double time;           // 累计时间
        }

        public QueenRapierMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            base.Logger.Information("随机乱斩！每次命中后在目标周围随机位置进行5次切割");
            Hook_QueenRapier.queenStrike += this.Hook_myqueenStrike;
        }

        public void Hook_myqueenStrike(Hook_QueenRapier.orig_queenStrike orig, QueenRapier self, Entity angle, double y, double x, double target)
        {
            // 保留原版第一次斩击
            orig(self, angle, y, x, target);
            QueenRapierMain.queen_rapier = self;

            if (angle != null && !angle.destroyed && angle.life > 0)
            {
                Queencut newCut = new Queencut
                {
                    targetEntity = angle,
                    remainingHits = TotalHits - 1,   // 第一次已经打完
                    time = 0.0
                };

                if (CanAddQueen(angle))
                {
                    queenStrikeStates.Add(newCut);
                }
            }
        }

        private bool CanAddQueen(Entity e)
        {
            foreach (Queencut cut in queenStrikeStates)
                if (cut.targetEntity == e) return false;
            return true;
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero hero = Module<Game>.Instance.HeroInstance;
            if (hero == null) return;
            CheckAndUpdateStrikeStates(dt, hero);
        }

        public void CheckAndUpdateStrikeStates(double deltaTime, Hero hero)
        {
            if (queenStrikeStates == null || queenStrikeStates.Count == 0) return;

            for (int i = queenStrikeStates.Count - 1; i >= 0; i--)
            {
                Queencut currentCut = queenStrikeStates[i];
                Entity e = currentCut.targetEntity;

                if (e != null && !e.destroyed && e.life > 0)
                {
                    currentCut.time += deltaTime;
                    if (currentCut.time >= SlashInterval)
                    {
                        // 获取目标当前中心坐标（跟随移动）
                        double centerX = (e.cx + e.xr) * 24.0;
                        double centerY = (e.cy + e.yr) * 24.0 - e.hei * 0.5;

                        // 随机生成这次斩击的位置和角度
                        double randomAngle = random.NextDouble() * 2.0 * System.Math.PI;
                        double randomDistance = random.NextDouble() * RandomRadius;
                        double posX = centerX + System.Math.Cos(randomAngle) * randomDistance;
                        double posY = centerY + System.Math.Sin(randomAngle) * randomDistance;
                        double strikeDir = random.NextDouble() * 2.0 * System.Math.PI;

                        if (queen_rapier != null && !queen_rapier.destroyed)
                        {
                            queen_rapier.queenStrike(e, posX, posY, strikeDir);
                            // 在斩击位置生成一个残影
                            CreateSlashTrail(hero, posX, posY);
                        }

                        currentCut.remainingHits--;
                        currentCut.time = 0.0;

                        if (currentCut.remainingHits <= 0)
                        {
                            queenStrikeStates.RemoveAt(i);
                        }
                        else
                        {
                            queenStrikeStates[i] = currentCut;
                        }
                    }
                    else
                    {
                        queenStrikeStates[i] = currentCut;
                    }
                }
                else
                {
                    queenStrikeStates.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 在指定世界坐标生成一个短暂的英雄残影
        /// </summary>
        private void CreateSlashTrail(Hero hero, double x, double y)
        {
            if (hero == null) return;

            // 从英雄当前外观创建一个残影（动画继承英雄当前动作）
            var trail = OnionSkin.Class.fromEntity(
                hero,
                null,                               // 动画（null = 使用英雄当前动画）
                TrailColor,
                Ref<double>.In(TrailAlpha),
                Ref<double>.In(TrailDuration),
                Ref<bool>.Null,
                Ref<bool>.Null,
                Ref<double>.Null
            );

            // 将残影放置到斩击位置（世界坐标）
            trail.x = x;
            trail.y = y;

            // 按配置缩放
            trail.scaleX *= TrailScale;
            trail.scaleY *= TrailScale;

            // 物理参数（可选，让残影有轻微惯性）
            trail.ds = 0.0;
            trail.frict = 0.87;
        }
    }
}
