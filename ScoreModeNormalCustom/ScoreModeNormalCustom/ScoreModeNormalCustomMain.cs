using dc;
using dc.cdb;
using dc.en;
using dc.en.mob;
using dc.h2d;
using dc.pr;
using dc.tool;
using dc.ui;
using Hashlink.Proxy.Objects;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using System;

namespace ScoreModeNormalCustom
{
    /// <summary>
    /// 普通模式 / 自定义模式 · 积分制（只设计怪物与 Boss，不涉及地图/门/计时器）
    ///
    /// 把每日挑战的“积分”核心搬到普通模式与自定义模式：
    /// - 英雄每击杀一只怪物，获得该怪物数据里的基础分（与每日挑战同源：mob._infos.score）；
    /// - 精英怪按 scoreModeEliteValue 倍率加分（与每日挑战完全一致）；
    /// - Boss 额外享受 BOSS_SCORE_MULT 倍加成；
    /// - 击杀位置弹出原版 ScoreTip 飘分（fx.popScore，与每日挑战相同特效）；
    /// - 左上角 HUD 实时显示本局累计积分；新开一局自动清零。
    ///
    /// 说明：刻意不触碰 game.scoring / isScoring()，因此不会触发 ScoringDoor、
    /// 每日挑战计时器、EndScoreMode 等地图/流程改动。
    /// </summary>
    public class ScoreModeNormalCustomMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        // ===== 积分规则 =====
        private const int BOSS_SCORE_MULT = 10;   // Boss 额外倍率

        // ===== 本局状态 =====
        private long _totalScore = 0;
        private dc.ui.Text? _scoreText = null;

        // ===== 退出日志 =====
        private bool _scoreLogged = false;

        public ScoreModeNormalCustomMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            // 怪物/Boss 死亡（英雄击杀）时加分
            Hook_Hero.onMobDeath += OnHeroKillMob;
            // 新开一局（Game 重新构造）时清零积分
            Hook__Game.__constructor__ += OnGameConstructor;
            // HUD 初始化时创建积分显示
            Hook_HUD.initHero += OnHUDInit;
            System.Console.WriteLine("[ScoreModeNormalCustom] 积分制已加载：普通/自定义模式下击杀怪物与Boss可获得积分");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.onMobDeath -= OnHeroKillMob;
            Hook__Game.__constructor__ -= OnGameConstructor;
            Hook_HUD.initHero -= OnHUDInit;
            LogFinalScore();
            System.Console.WriteLine("[ScoreModeNormalCustom] 模组已卸载");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 仅刷新 HUD 文本（内容在加分时已更新，这里兜底确保文本存在）
            if (_scoreText == null)
            {
                TryCreateScoreText();
            }
        }

        // ------------------------------------------------------------------
        // 新一局：Game 构造完成后清零
        // ------------------------------------------------------------------
        private void OnGameConstructor(Hook__Game.orig___constructor__ orig, Game self, User user, GameData data)
        {
            orig(self, user, data);
            _totalScore = 0;
            _scoreLogged = false;
            if (_scoreText != null)
            {
                try { _scoreText.remove(); } catch { }
                _scoreText = null;
            }
            System.Console.WriteLine("[ScoreModeNormalCustom] 新开局，积分已清零");
        }

        // ------------------------------------------------------------------
        // HUD 初始化：创建左上角积分文本
        // ------------------------------------------------------------------
        private void OnHUDInit(Hook_HUD.orig_initHero orig, HUD self)
        {
            orig(self);
            TryCreateScoreText();
        }

        private void TryCreateScoreText()
        {
            try
            {
                if (_scoreText != null) return;
                var root = Main.Class.ME?.root;
                if (root == null) return; // UI 尚未就绪，稍后 OnHeroUpdate 重试

                var label = Lang.Class.t.untranslated(ToHaxeString("积分"));
                _scoreText = Assets.Class.makeText(label, null, true, null);
                _scoreText.set_textColor(0xFFFFFF);
                _scoreText.set_textAlign(new Align.Left());
                _scoreText.scaleX = 1.6f;
                _scoreText.scaleY = 1.6f;
                _scoreText.x = 24;
                _scoreText.y = 48;
                root.addChild(_scoreText);
                UpdateScoreText();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ScoreModeNormalCustom] 创建积分显示失败: " + ex.Message);
            }
        }

        private void UpdateScoreText()
        {
            try
            {
                if (_scoreText == null) return;
                string text = "积分 " + _totalScore.ToString();
                _scoreText.set_text(ToHaxeString(text));
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 击杀计分：只处理怪物 / Boss
        // ------------------------------------------------------------------
        private void OnHeroKillMob(Hook_Hero.orig_onMobDeath orig, Hero self, dc.en.Mob mob)
        {
            orig(self, mob);

            try
            {
                // 注意：不能检查 mob.destroyed —— Mob.onDie() 里先调用了 base.onDie()
                // （Entity.onDie → destroy → destroyed=true），之后才 notifyHeroOfDeath()
                // 触发本回调，此时 destroyed 恒为 true，若提前 return 则永远不加分。
                if (mob == null) return;

                // 基础分：与每日挑战同源（mob._infos.score）
                int baseScore = GetMobScore(mob);
                if (baseScore <= 0) baseScore = 5; // 个别杂兵数据分值为 0，给保底分，保证击杀必得分

                long points = baseScore;

                // 精英倍率（与每日挑战一致：scoreModeEliteValue）
                if (mob.elite && mob.scoreModeEliteValue > 1)
                {
                    points *= mob.scoreModeEliteValue;
                }

                // Boss 额外倍率
                if (mob is Boss)
                {
                    points *= BOSS_SCORE_MULT;
                }

                _totalScore += points;
                UpdateScoreText();

                // 击杀位置弹出原版飘分（每日挑战同款 ScoreTip 特效）
                try
                {
                    if (self?._level?.fx != null && mob._level == self._level)
                    {
                        self._level.fx.popScore(mob, (int)System.Math.Min(points, int.MaxValue), 0);
                    }
                }
                catch { }

                System.Console.WriteLine($"[ScoreModeNormalCustom] 击杀 {mob.type}  +{points} 分（累计 {_totalScore}）");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ScoreModeNormalCustom] 计分异常: " + ex.Message);
            }
        }

        /// <summary>读取怪物数据里的基础分；_infos 为空时按 id 解析（与游戏原版同法）。</summary>
        private static int GetMobScore(dc.en.Mob mob)
        {
            var infos = mob._infos;
            if (infos == null)
            {
                var data = (virtual_active_blueprints_canBeElite_colorSwaps_dlc_flesh1_flesh2_genTags_glowInnerColor_glowOuterColor_group_icon_id_index_life_maxPerPlatform_maxPerRoom_metaItems_minPfHeight_minPfSize_mobTags_name_newSkill_particles_pfCost_props_score_skill_volteDelay_weight_)
                    (object)Data.Class.mob.byId.get(mob.type);
                if (data != null)
                {
                    mob._infos = data;
                    infos = data;
                }
            }
            if (infos == null) return 0;
            return infos.score;
        }

        private void LogFinalScore()
        {
            if (_scoreLogged) return;
            _scoreLogged = true;
            System.Console.WriteLine($"[ScoreModeNormalCustom] 本局最终积分：{_totalScore}");
        }

        // ---------- 字符串转换 ----------
        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
