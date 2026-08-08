using dc;
using dc.cine;
using dc.cine.queen;
using dc.en;
using dc.libs;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;

namespace AutoEnding
{
    /// <summary>
    /// 自动结局 — 开局（进入关卡）2 秒后自动播放结局动画。
    /// 使用 KillQueenCinem（女王结局动画）：播完自动进入 Credits(QueenKilled)，
    /// 该流程不触发 AfterCredits，不会崩溃；只播放动画即可。
    /// 通过英雄实例变化识别新的一局，只检测一次，不会刷日志。
    /// </summary>
    public class AutoEndingMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        private const double ENDING_DELAY = 2.0;   // 加载完成后延迟秒数

        private Hero? _lastHero = null;
        private bool _runDetected = false;
        private bool _endingTriggered = false;
        private double _timer = 0.0;

        public AutoEndingMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            System.Console.WriteLine("[AutoEnding] 已加载 — 开局2秒后自动播放结局动画");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero._level == null) return;

            // 英雄实例变化 = 新的一局：重置状态（只执行一次，不刷日志）
            if (!ReferenceEquals(hero, _lastHero))
            {
                _lastHero = hero;
                _runDetected = false;
                _endingTriggered = false;
                _timer = 0.0;
            }

            // 检测到开局：开始 2 秒倒计时
            if (!_runDetected)
            {
                _runDetected = true;
                _timer = ENDING_DELAY;
                System.Console.WriteLine($"[AutoEnding] 开局加载完成，{ENDING_DELAY:F0} 秒后播放结局动画");
            }

            if (_timer > 0.0)
            {
                _timer -= dt;
                if (_timer <= 0.0)
                {
                    _timer = 0.0;
                    TriggerEnding();
                }
            }
        }

        /// <summary>
        /// 播放女王结局动画；万一失败降级为直接播 Credits 结局片尾并回主菜单。
        /// </summary>
        private void TriggerEnding()
        {
            if (_endingTriggered) return;
            _endingTriggered = true;

            try
            {
                new KillQueenCinem();
                System.Console.WriteLine("[AutoEnding] 结局动画已开始（女王结局）");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[AutoEnding] 播放结局动画失败: {ex.Message}，降级为 Credits");
                try
                {
                    Credits credits = new Credits(new EndRunKind.QueenKilled());
                    ((Process)credits).onDisposeCb = new HlAction(ReturnToMainMenu);
                    System.Console.WriteLine("[AutoEnding] 结局片尾已开始（降级方案）");
                }
                catch (Exception ex2)
                {
                    System.Console.WriteLine($"[AutoEnding] 触发结局失败: {ex2.Message}");
                }
            }
        }

        private void ReturnToMainMenu()
        {
            try
            {
                Boot.Class.ME?.returnToMainMenu();
                System.Console.WriteLine("[AutoEnding] 已返回主菜单");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[AutoEnding] 返回主菜单失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            _lastHero = null;
            _runDetected = false;
            _endingTriggered = false;
            _timer = 0.0;
            System.Console.WriteLine("[AutoEnding] 游戏退出，模组已卸载");
        }
    }
}
