using System;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>控制器运行状态，用于驱动界面状态灯与按钮可用性。</summary>
    public enum RunnerStatus
    {
        /// <summary>已停止（含正常结束）。</summary>
        Stopped,
        /// <summary>运行中。</summary>
        Running,
        /// <summary>正在停止（等待当前动作收尾）。</summary>
        Stopping,
        /// <summary>因错误终止。</summary>
        Error
    }

    /// <summary>日志级别，决定界面日志着色。</summary>
    public enum LogLevel
    {
        /// <summary>常规信息。</summary>
        Info,
        /// <summary>需要注意的告警。</summary>
        Warn,
        /// <summary>错误。</summary>
        Error
    }

    /// <summary>一条日志记录。</summary>
    public class LogEntry
    {
        /// <summary>产生时间。</summary>
        public DateTime Time { get; set; }
        /// <summary>级别。</summary>
        public LogLevel Level { get; set; }
        /// <summary>文本内容。</summary>
        public string Message { get; set; }

        /// <summary>以当前时间构造一条日志。</summary>
        public LogEntry(LogLevel level, string message)
        {
            Time = DateTime.Now;
            Level = level;
            Message = message;
        }
    }

    /// <summary>逐帧/分块进度快照，供进度条与状态显示。</summary>
    public class FrameProgressInfo
    {
        /// <summary>已完成（含跳过）的帧数。</summary>
        public int Completed { get; set; }
        /// <summary>任务总帧数。</summary>
        public int Total { get; set; }
        /// <summary>当前帧号。</summary>
        public int CurrentFrame { get; set; }
        /// <summary>当前阶段简述。</summary>
        public string Stage { get; set; }
    }

    /// <summary>
    /// 渲染/看门狗控制器的统一契约。TeamRenderController 与 FrameRenderController 各自实现，
    /// 在后台线程运行，通过事件把日志 / 状态 / 进度回抛给 UI（UI 侧负责 Invoke 回主线程）。
    /// </summary>
    public interface IRenderController
    {
        /// <summary>控制器是否正在运行。</summary>
        bool IsRunning { get; }

        /// <summary>产生一条日志时触发。</summary>
        event EventHandler<LogEntry> Log;
        /// <summary>运行状态变化时触发。</summary>
        event EventHandler<RunnerStatus> StatusChanged;
        /// <summary>进度更新时触发（Team Render 无帧概念，不触发此事件）。</summary>
        event EventHandler<FrameProgressInfo> FrameProgressChanged;

        /// <summary>用给定模式参数在后台线程启动监控/调度。</summary>
        void Start(ModeProfile profile);

        /// <summary>请求停止：会结束仍在运行的子进程后收尾后台线程。</summary>
        void Stop();
    }
}
