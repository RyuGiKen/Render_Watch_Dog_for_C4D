using System;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>控制器运行状态。</summary>
    public enum RunnerStatus
    {
        Stopped,
        Running,
        Stopping,
        Error
    }

    /// <summary>日志级别。</summary>
    public enum LogLevel
    {
        Info,
        Warn,
        Error
    }

    /// <summary>一条日志。</summary>
    public class LogEntry
    {
        public DateTime Time { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; }

        public LogEntry(LogLevel level, string message)
        {
            Time = DateTime.Now;
            Level = level;
            Message = message;
        }
    }

    /// <summary>逐帧进度信息。</summary>
    public class FrameProgressInfo
    {
        public int Completed { get; set; }
        public int Total { get; set; }
        public int CurrentFrame { get; set; }
        public string Stage { get; set; }
    }

    /// <summary>
    /// 渲染/看门狗控制器的统一契约。TeamRenderController 与 FrameRenderController 各自实现，
    /// 在后台线程运行，通过事件把日志 / 状态 / 进度回抛给 UI（UI 侧负责 Invoke 回主线程）。
    /// </summary>
    public interface IRenderController
    {
        bool IsRunning { get; }

        event EventHandler<LogEntry> Log;
        event EventHandler<RunnerStatus> StatusChanged;
        event EventHandler<FrameProgressInfo> FrameProgressChanged;

        /// <summary>用给定参数启动（内部自判是否为对应模式的参数）。</summary>
        void Start(ModeProfile profile);

        /// <summary>请求停止，会杀掉仍在运行的子进程后结束后台线程。</summary>
        void Stop();
    }
}
