#nullable disable
using System;
using System.Collections.Generic;

namespace GojoLimitless
{
    /// <summary>
    /// 全局日志出口。玩法类不持有 ModBase 引用，统一从这里打日志。
    ///
    /// 另外维护一个**最近日志的环形缓冲**，供 HUD 直接显示
    /// （<see cref="UiCfg.HudShowLog"/>）—— 这样不用切出去看 log 文件就能自检。
    /// </summary>
    public static class Log
    {
        private const string Prefix = "[GojoLimitless] ";

        /// <summary>HUD 能回溯多少条。</summary>
        private const int RingSize = 24;

        private static readonly string[] Ring = new string[RingSize];
        private static int _ringHead;
        private static int _ringCount;

        private static Serilog.ILogger _logger;

        public static void Attach(Serilog.ILogger logger) => _logger = logger;

        public static void Info(string msg) => Write(msg, false);

        public static void Warn(string msg) => Write(msg, true);

        public static void Error(string msg) => Write(msg, true);

        public static void Exception(Exception ex, string msg)
        {
            System.Console.WriteLine(Prefix + msg + " :: " + ex);
            try { _logger?.Error(ex, Prefix + msg); } catch { }
            Push($"[ERR] {msg}");
        }

        public static void Debug(string msg)
        {
            try
            {
                UiCfg ui = Cfg.V.Ui;
                if (ui != null && ui.VerboseLog) Write(msg, false);
            }
            catch { }
        }

        /// <summary>
        /// 只为 HUD 写的一条（不进 Serilog、不刷控制台）。
        /// 用在"想让它出现在 HUD 上但不值得写日志文件"的高频事件上。
        /// </summary>
        public static void Hud(string msg)
        {
            try { Push(msg); } catch { }
        }

        // ------------------------------------------------------------ 环形缓冲

        private static void Push(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;

            // 太长会撑爆 HUD 一行，截一下
            if (msg.Length > 120) msg = msg.Substring(0, 117) + "...";

            lock (Ring)
            {
                Ring[_ringHead] = msg;
                _ringHead = (_ringHead + 1) % RingSize;
                if (_ringCount < RingSize) _ringCount++;
            }
        }

        /// <summary>取最近 <paramref name="count"/> 条（老的在前）。</summary>
        public static string[] Recent(int count)
        {
            if (count <= 0) return Array.Empty<string>();

            lock (Ring)
            {
                int n = Math.Min(count, _ringCount);
                var result = new string[n];
                for (int i = 0; i < n; i++)
                {
                    // 最老的排在最前：从 head - n 开始
                    int idx = ((_ringHead - n + i) % RingSize + RingSize) % RingSize;
                    result[i] = Ring[idx] ?? "";
                }
                return result;
            }
        }

        private static void Write(string msg, bool isWarn)
        {
            string line = Prefix + msg;
            System.Console.WriteLine(line);
            try
            {
                if (_logger != null)
                {
                    if (isWarn) _logger.Warning(line);
                    else _logger.Information(line);
                }
            }
            catch { }

            Push((isWarn ? "[!] " : "") + msg);
        }
    }
}
