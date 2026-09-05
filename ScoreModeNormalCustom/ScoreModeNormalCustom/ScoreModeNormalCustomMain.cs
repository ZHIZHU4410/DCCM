using dc;
using dc.cdb;
using dc.en;
using dc.en.inter;
using dc.en.loot;
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
    /// 普通模式 / 自定义模式 · 挑战式积分制（只做怪物/Boss/探索积分，不启用 game.scoring，
    /// 因此不会触发 ScoringDoor、每日计时器失败流程等地图改动）
    ///
    /// 仿照每日挑战（挑战模式）的核心体验：
    ///  1. 击杀得分   —— mob._infos.score（精英 ×scoreModeEliteValue；Boss ×10）
    ///  2. 连杀加成   —— ComboMult 连杀球：连续击杀会掉落 ComboMult 球（affect 103），
    ///                    拾取后 15 秒内每层在每次得分时 +5 加成（与挑战模式同源：countAffect(103)*5）
    ///  3. 区域限时   —— 每张图限时（默认 4:30），在限时内通过该图时按剩余时间给予奖励分；
    ///                    纯奖励性质，超时不惩罚、不中断流程。
    ///                    换图检测为每帧轮询 hero._level（避开带 ref 参数的 Game.loadMainLevel
    ///                    hook —— 游戏以 null ref 调用它会导致崩溃），结算后再把计时重置为新图；
    ///                    死亡回牢房只重置、不发奖。
    ///  4. 探索得分点 —— 开宝箱 +20、诅咒宝箱 +5、隐藏墙/隐藏地面 +20、金块 +8、商店购买 +50（仿原版数值）
    ///  5. 右上角 HUD —— 分数 / 剩余时间 / 连杀加成（仿挑战模式 ScoringInfo 布局）
    /// </summary>
    public class ScoreModeNormalCustomMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        // ===== 积分规则 =====
        private const int BOSS_SCORE_MULT = 10;          // Boss 额外倍率
        private const int MIN_MOB_SCORE = 5;             // 数据分值为 0 的杂兵保底分
        private const int COMBO_BONUS_PER_LAYER = 5;     // 每层 ComboMult 加成（与挑战一致）
        private const int COMBO_SPAWN_EVERY = 5;         // 每连杀 N 只普通怪掉一颗 ComboMult 球
        private const int EXPLORE_CHEST = 20;            // 开普通宝箱
        private const int EXPLORE_CURSED = 5;            // 开诅咒宝箱
        private const int EXPLORE_HIDDEN = 20;           // 隐藏墙 / 隐藏地面
        private const int EXPLORE_GOLD = 8;              // 金块
        private const int EXPLORE_SHOP = 50;             // 商店购买

        // ===== 区域限时 =====
        private const double LEVEL_TIME_LIMIT_S = 270.0; // 每图限时 4:30（仿挑战模式初始时间）
        private const int TIME_BONUS_PER_SEC = 2;        // 剩余每 1 秒的奖励分

        // ===== 本局状态 =====
        private long _totalScore = 0;
        private dc.ui.Text? _textScore = null;
        private dc.ui.Text? _textTime = null;
        private dc.ui.Text? _textCombo = null;
        private bool _scoreLogged = false;
        private double _levelStartGameTimeS = -1.0;      // 当前图开始时的 gameTimeS（-1 = 未开始）
        private int _normalKillCount = 0;                // 普通怪连杀计数（用于掉 ComboMult 球）
        private int _lastComboLayers = 0;
        private string _lastScoreText = "";
        private string _lastTimeText = "";
        private bool _uiDirty = true;                    // UI 是否需要重新布局
        private bool _heroJustDied = false;              // 英雄刚死亡（防止死亡回牢房被误判为过关奖励）
        private dc.pr.Level? _curLevel = null;           // 当前英雄所在关卡（用于轮询检测换图）

        public ScoreModeNormalCustomMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            // 怪物/Boss 死亡（英雄击杀）时加分
            Hook_Hero.onMobDeath += OnHeroKillMob;
            // 英雄死亡标记（区域限时结算时排除死亡回牢房）
            Hook_Hero.onDie += OnHeroDied;
            // 新开一局（Game 重新构造）时清零
            Hook__Game.__constructor__ += OnGameConstructor;
            // HUD 初始化时创建右上角积分显示
            Hook_HUD.initHero += OnHUDInit;

            // ===== 探索得分点（仿挑战模式数值）=====
            Hook_TreasureChest.open += OnTreasureChestOpen;
            Hook_CursedChest.reveal += OnCursedChestReveal;
            Hook_HiddenBlock.onDie += OnHiddenBlockDie;
            Hook_HiddenGroundBlock.onDie += OnHiddenGroundBlockDie;
            Hook_GoldNugget.onDie += OnGoldNuggetDie;
            Hook_ShopBooth.buy += OnShopBoothBuy;

            System.Console.WriteLine("[ScoreModeNormalCustom] 挑战式积分制已加载：击杀/连杀/区域限时/开宝箱 均计分");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.onMobDeath -= OnHeroKillMob;
            Hook_Hero.onDie -= OnHeroDied;
            Hook__Game.__constructor__ -= OnGameConstructor;
            Hook_HUD.initHero -= OnHUDInit;
            Hook_TreasureChest.open -= OnTreasureChestOpen;
            Hook_CursedChest.reveal -= OnCursedChestReveal;
            Hook_HiddenBlock.onDie -= OnHiddenBlockDie;
            Hook_HiddenGroundBlock.onDie -= OnHiddenGroundBlockDie;
            Hook_GoldNugget.onDie -= OnGoldNuggetDie;
            Hook_ShopBooth.buy -= OnShopBoothBuy;
            LogFinalScore();
            System.Console.WriteLine("[ScoreModeNormalCustom] 模组已卸载");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 每帧轮询换图：区域限时结算 + 新图计时（避免带 ref 参数的 hook 引发崩溃）
            UpdateLevelTimer();

            if (_textScore == null || _textTime == null)
            {
                TryCreateScoreTexts();
            }
            else if (_uiDirty)
            {
                LayoutTexts();
                _uiDirty = false;
            }
            // 每帧刷新：剩余时间 / 连杀层数显示
            RefreshTimeText();
            RefreshComboText();
        }

        // ------------------------------------------------------------------
        // 新一局：Game 构造完成后清零
        // ------------------------------------------------------------------
        private void OnGameConstructor(Hook__Game.orig___constructor__ orig, Game self, User user, GameData data)
        {
            orig(self, user, data);
            _totalScore = 0;
            _scoreLogged = false;
            _levelStartGameTimeS = -1.0;
            _normalKillCount = 0;
            _heroJustDied = false;
            _curLevel = null;
            _lastScoreText = "";
            _lastTimeText = "";
            _uiDirty = true;
            RemoveTexts();
            System.Console.WriteLine("[ScoreModeNormalCustom] 新开局，积分已清零");
        }

        // ------------------------------------------------------------------
        // HUD 初始化：创建右上角文本
        // ------------------------------------------------------------------
        private void OnHUDInit(Hook_HUD.orig_initHero orig, HUD self)
        {
            orig(self);
            TryCreateScoreTexts();
        }

        private void TryCreateScoreTexts()
        {
            try
            {
                if (_textScore != null && _textTime != null) return;
                var root = Main.Class.ME?.root;
                if (root == null) return; // UI 尚未就绪，稍后 OnHeroUpdate 重试

                RemoveTexts();

                // 分数（大，青白色，仿挑战 textScore）
                _textScore = Assets.Class.makeText(ToHaxeString("0"), null, true, null);
                _textScore.set_textColor(0xFFFFFF);
                _textScore.scaleX = 2.0f;
                _textScore.scaleY = 2.0f;
                _textScore.posChanged = true;
                root.addChild(_textScore);

                // 剩余时间（挑战式：>30s 橙黄，<=30s 红）
                _textTime = Assets.Class.makeText(ToHaxeString("--:--"), null, true, null);
                _textTime.set_textColor(0xF59605);
                _textTime.scaleX = 1.4f;
                _textTime.scaleY = 1.4f;
                _textTime.posChanged = true;
                root.addChild(_textTime);

                // 连杀加成徽章（有连杀层时才显示）
                _textCombo = Assets.Class.makeText(ToHaxeString(""), null, true, null);
                _textCombo.set_textColor(0x85D4FF);
                _textCombo.scaleX = 1.5f;
                _textCombo.scaleY = 1.5f;
                _textCombo.posChanged = true;
                root.addChild(_textCombo);

                _uiDirty = true;
                UpdateScoreText();
                RefreshTimeText();
                RefreshComboText();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ScoreModeNormalCustom] 创建积分显示失败: " + ex.Message);
            }
        }

        private void RemoveTexts()
        {
            try { if (_textScore != null) { _textScore.remove(); _textScore = null; } } catch { }
            try { if (_textTime != null) { _textTime.remove(); _textTime = null; } } catch { }
            try { if (_textCombo != null) { _textCombo.remove(); _textCombo = null; } } catch { }
            _lastScoreText = "";
            _lastTimeText = "";
            _lastComboLayers = 0;
            _uiDirty = true;
        }

        // 右上角布局：分数 / 时间 / 连杀 三行右对齐到同一条右缘竖线，
        // 顶部往下排：分数(大字) -> 时间 -> 连杀。仿挑战模式右上角计分板。
        // 注意：h2d 对象改 x/y/scale 后必须 posChanged=true，否则渲染矩阵不刷新导致错位（偏右/滞后）。
        private double _layoutScoreW = 0;
        private double _layoutTimeW = 0;
        private double _layoutComboW = 0;
        private void LayoutTexts()
        {
            try
            {
                double stageW = GetStageWidth();
                double margin = 36.0; // 右缘留足空间，避免贴边导致看不清

                double rightX = stageW - margin; // 右对齐参考线
                double baseY = 40.0;
                double lineGap = 4.0;

                if (_textScore != null)
                {
                    double w = (double)_textScore.get_textWidth() * _textScore.scaleX;
                    if (w > 0) _layoutScoreW = w;
                    else w = EstimateTextWidth(_textScore); // 字体未就绪时按字符数估算，防止瞬移到最右
                    double x = rightX - w;
                    if (x < 0) x = 0;
                    _textScore.x = (float)x;
                    _textScore.y = (float)baseY;
                    _textScore.posChanged = true;
                    baseY += (double)_textScore.get_textHeight() * _textScore.scaleY + lineGap;
                }
                if (_textTime != null)
                {
                    double w = (double)_textTime.get_textWidth() * _textTime.scaleX;
                    if (w > 0) _layoutTimeW = w;
                    else w = EstimateTextWidth(_textTime);
                    double x = rightX - w;
                    if (x < 0) x = 0;
                    _textTime.x = (float)x;
                    _textTime.y = (float)baseY;
                    _textTime.posChanged = true;
                    baseY += (double)_textTime.get_textHeight() * _textTime.scaleY + lineGap;
                }
                if (_textCombo != null && _lastComboLayers > 0)
                {
                    double w = (double)_textCombo.get_textWidth() * _textCombo.scaleX;
                    if (w > 0) _layoutComboW = w;
                    else w = EstimateTextWidth(_textCombo);
                    double x = rightX - w;
                    if (x < 0) x = 0;
                    _textCombo.x = (float)x;
                    _textCombo.y = (float)baseY;
                    _textCombo.posChanged = true;
                }
            }
            catch { }
        }

        // 字体 glyph 未就绪时 get_textWidth 可能为 0，按字符数估算宽度，保证位置稳定
        private static double EstimateTextWidth(dc.h2d.Text t)
        {
            try
            {
                if (t == null) return 0;
                int len = t.text != null ? t.text.length : 0;
                return len * 22.0 * t.scaleX; // 约 22px/字符
            }
            catch { return 0; }
        }

        private void UpdateScoreText()
        {
            try
            {
                if (_textScore == null) return;
                string s = _totalScore.ToString();
                if (s == _lastScoreText) return;
                _lastScoreText = s;
                _textScore.set_text(ToHaxeString(s));
                _uiDirty = true;
            }
            catch { }
        }

        // 剩余时间：当前图限时剩余（分:秒），挑战配色
        private void RefreshTimeText()
        {
            try
            {
                if (_textTime == null) return;
                double remain = GetLevelTimeRemainS();
                if (remain < 0) remain = 0;
                int sec = (int)System.Math.Ceiling(remain);
                string s = (sec / 60) + ":" + (sec % 60).ToString("00");
                if (s != _lastTimeText)
                {
                    _lastTimeText = s;
                    _textTime.set_text(ToHaxeString(s));
                    _textTime.set_textColor(sec <= 30 ? 0xF20D0D : 0xF59605);
                    _uiDirty = true;
                }
            }
            catch { }
        }

        // 连杀加成徽章：显示 "连杀 +NN (Nx)"（有层时），无层则清空隐藏
        private void RefreshComboText()
        {
            try
            {
                if (_textCombo == null) return;
                int layers = GetComboLayers();
                if (layers <= 0)
                {
                    if (_lastComboLayers != 0)
                    {
                        _lastComboLayers = 0;
                        _textCombo.set_text(ToHaxeString(""));
                        _uiDirty = true;
                    }
                    return;
                }
                if (layers == _lastComboLayers) return;
                _lastComboLayers = layers;
                string txt = "连杀 +" + (layers * COMBO_BONUS_PER_LAYER) + " (" + layers + "x)";
                _textCombo.set_text(ToHaxeString(txt));
                _uiDirty = true;
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 换图轮询（每帧）：区域限时结算 + 新图计时
        // 通过比较 hero._level 引用检测"换图"：
        //  - hero._level 变了 = 从旧图进入新图（普通过关 / 死亡回牢房 / 开局第一张图）
        //  - 结算仅在：之前已有计时、英雄刚没死、非训练/BossRush、进入新图时执行
        // 注意：不能用带 ref 参数的 Hook_Game.loadMainLevel，游戏以 null ref 调用会导致崩溃。
        // ------------------------------------------------------------------
        private void UpdateLevelTimer()
        {
            try
            {
                var g = Game.Class.ME;
                if (g == null || g.hero == null)
                {
                    _curLevel = null;
                    _levelStartGameTimeS = -1.0;
                    return;
                }

                bool trainingOrBr = g.isTraining() || g.isBossRush();
                if (trainingOrBr)
                {
                    // 训练房 / BossRush 不计时（不结算、不启动）
                    _curLevel = null;
                    _levelStartGameTimeS = -1.0;
                    return;
                }

                var hero = g.hero;
                if (hero == null || hero.destroyed || hero.life <= 0)
                {
                    // 英雄死亡/暂不存在：留待复活后重新建档；不做结算
                    _heroJustDied = true;
                    _curLevel = null;
                    return;
                }

                var lvl = hero._level;
                if (lvl == null)
                {
                    _curLevel = null;
                    _levelStartGameTimeS = -1.0;
                    return;
                }

                // 关卡没变：无事
                if (lvl == _curLevel) return;

                // 换图了（_curLevel != null 说明是从旧图来的；null 说明是开局首次建档）
                bool died = _heroJustDied;
                _heroJustDied = false;

                if (!died && _curLevel != null && _levelStartGameTimeS >= 0)
                {
                    double now = GetGameTimeS();
                    if (now >= 0)
                    {
                        double elapsed = now - _levelStartGameTimeS;
                        double remain = LEVEL_TIME_LIMIT_S - elapsed;
                        if (elapsed > 1.0 && remain > 0)
                        {
                            int timeBonus = (int)System.Math.Ceiling(remain) * TIME_BONUS_PER_SEC;
                            AddScore(hero, timeBonus, "区域限时奖励", false);
                        }
                    }
                }

                _curLevel = lvl;
                _levelStartGameTimeS = GetGameTimeS();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ScoreModeNormalCustom] 区域限时轮询异常: " + ex.Message);
            }
        }

        // 英雄死亡标记：死亡后回到牢房会换图，但不能当作"通过本图"
        private void OnHeroDied(Hook_Hero.orig_onDie orig, Hero self)
        {
            try { _heroJustDied = true; } catch { }
            orig(self);
        }

        private double GetLevelTimeRemainS()
        {
            try
            {
                double now = GetGameTimeS();
                if (now < 0 || _levelStartGameTimeS < 0) return LEVEL_TIME_LIMIT_S;
                double remain = LEVEL_TIME_LIMIT_S - (now - _levelStartGameTimeS);
                if (remain < 0) return 0;
                return remain;
            }
            catch { return LEVEL_TIME_LIMIT_S; }
        }

        private static double GetGameTimeS()
        {
            try
            {
                var g = Game.Class.ME;
                if (g == null || g.data == null) return -1;
                return g.data.gameTimeS;
            }
            catch { return -1; }
        }

        private static double GetStageWidth()
        {
            try
            {
                int w = dc.libs.Process.Class.CUSTOM_STAGE_WIDTH;
                if (w > 0) return w;
                var inst = dc.hxd.Window.Class.inst;
                if (inst != null) return inst.get_width();
                return 1920;
            }
            catch { return 1920; }
        }

        // ------------------------------------------------------------------
        // 击杀计分：怪物 / Boss（英雄击杀）
        // ------------------------------------------------------------------
        private void OnHeroKillMob(Hook_Hero.orig_onMobDeath orig, Hero self, dc.en.Mob mob)
        {
            orig(self, mob);
            try
            {
                // 注意：不能检查 mob.destroyed —— Mob.onDie() 里先 base.onDie()（destroy→destroyed=true）
                // 之后才 notifyHeroOfDeath() 触发本回调，此时 destroyed 恒为 true。
                if (mob == null || self == null || self._level == null) return;
                if (mob._level == null || mob._level != self._level) return;

                bool isElite = mob.elite;
                bool isBoss = mob is Boss;

                int baseScore = GetMobScore(mob);
                if (baseScore <= 0) baseScore = MIN_MOB_SCORE;

                long points = baseScore;
                if (isElite && mob.scoreModeEliteValue > 1)
                {
                    points *= mob.scoreModeEliteValue;
                }
                if (isBoss)
                {
                    points *= BOSS_SCORE_MULT;
                }

                AddScore(mob, points, "击杀", true);

                // ===== ComboMult 连杀球掉落 =====
                TrySpawnComboMult(mob, isElite, isBoss);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ScoreModeNormalCustom] 击杀计分异常: " + ex.Message);
            }
        }

        private void TrySpawnComboMult(dc.en.Mob mob, bool isElite, bool isBoss)
        {
            try
            {
                bool spawn = false;
                if (isBoss || isElite)
                {
                    spawn = true; // 精英 / Boss 必掉
                }
                else
                {
                    _normalKillCount++;
                    if (_normalKillCount >= COMBO_SPAWN_EVERY)
                    {
                        _normalKillCount = 0;
                        spawn = true; // 每连杀 N 只普通怪掉一颗
                    }
                }
                if (!spawn) return;
                if (mob._level == null || mob._level.game == null) return;

                // 在怪物死亡位置生成 ComboMult 球（挑战模式同款拾取物：拾取 +15s affect103）
                new ComboMultDrop(mob._level, mob.cx, mob.cy).init();
                if (isBoss)
                {
                    new ComboMultDrop(mob._level, mob.cx, mob.cy).init(); // Boss 双球
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ScoreModeNormalCustom] ComboMult 球生成失败: " + ex.Message);
            }
        }

        /// <summary>读取怪物数据里的基础分；_infos 为空时按 id 解析（与游戏原版同法）。</summary>
        private static int GetMobScore(dc.en.Mob mob)
        {
            try
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
            catch { return 0; }
        }

        // ------------------------------------------------------------------
        // 统一加分：基础分 + 连杀加成（countAffect(103) * 5），弹原版 ScoreTip 飘分
        // ------------------------------------------------------------------
        private void AddScore(dc.Entity from, long baseVal, string why, bool withCombo)
        {
            try
            {
                long total = baseVal;
                int layers = 0;
                if (withCombo)
                {
                    layers = GetComboLayers();
                    total += (long)layers * COMBO_BONUS_PER_LAYER;
                }
                _totalScore += total;
                UpdateScoreText();

                // 从实体位置弹出原版飘分（每日挑战同款 ScoreTip 特效）
                try
                {
                    if (from != null && from._level != null && from._level.fx != null)
                    {
                        from._level.fx.popScore(from, (int)System.Math.Min(total, int.MaxValue), layers);
                    }
                }
                catch { }

                System.Console.WriteLine($"[ScoreModeNormalCustom] {why}  +{total} 分（累计 {_totalScore}）");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ScoreModeNormalCustom] 加分异常: " + ex.Message);
            }
        }

        private static int GetComboLayers()
        {
            try
            {
                var g = Game.Class.ME;
                if (g == null || g.hero == null) return 0;
                return g.hero.countAffect(103);
            }
            catch { return 0; }
        }

        // ==================================================================
        // 探索得分点（仿原版各数值；普通模式下 game.addScore 无效果，故自行加分）
        // ==================================================================

        private void OnTreasureChestOpen(Hook_TreasureChest.orig_open orig, TreasureChest self, Hero by)
        {
            // 未开过才加分（防重复）
            if (self != null && !self.isOpen)
            {
                AddScore(self, EXPLORE_CHEST, "开宝箱", true);
            }
            orig(self, by);
        }

        private void OnCursedChestReveal(Hook_CursedChest.orig_reveal orig, CursedChest self, Hero by)
        {
            if (self != null && !self.isOpen)
            {
                AddScore(self, EXPLORE_CURSED, "开诅咒宝箱", true);
            }
            orig(self, by);
        }

        private void OnHiddenBlockDie(Hook_HiddenBlock.orig_onDie orig, HiddenBlock self)
        {
            if (self != null && !self.destroyed)
            {
                AddScore(self, EXPLORE_HIDDEN, "隐藏墙", true);
            }
            orig(self);
        }

        private void OnHiddenGroundBlockDie(Hook_HiddenGroundBlock.orig_onDie orig, HiddenGroundBlock self)
        {
            if (self != null && !self.destroyed)
            {
                AddScore(self, EXPLORE_HIDDEN, "隐藏地面", true);
            }
            orig(self);
        }

        private void OnGoldNuggetDie(Hook_GoldNugget.orig_onDie orig, GoldNugget self)
        {
            if (self != null && !self.destroyed)
            {
                AddScore(self, EXPLORE_GOLD, "金块", true);
            }
            orig(self);
        }

        private void OnShopBoothBuy(Hook_ShopBooth.orig_buy orig, ShopBooth self, Hero by, Ref<bool> showFx)
        {
            orig(self, by, showFx);
            // 购买成功会销毁摊位；成功才加分
            try
            {
                if (self != null && self.destroyed)
                {
                    AddScore(self, EXPLORE_SHOP, "商店购买", true);
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("[ScoreModeNormalCustom] 商店计分异常: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
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
