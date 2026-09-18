using System;
using System.Threading;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 单帧逐帧调度控制器（阶段一：框架占位），供 Cinema 4D 与 Commandline 两种模式共用。
    /// 阶段二将实现：展开帧模板→判已完成跳过→启动 exe -render 帧号→轮询(退出码/产物/超时/_BugReport)
    /// →异常杀进程重启→帧间冷却→下一帧，直至结束帧。
    /// </summary>
    public class FrameRenderController : IRenderController
    {
        private Thread _worker;
        private volatile bool _stopRequested;

        public bool IsRunning { get; private set; }

        public event EventHandler<LogEntry> Log;
        public event EventHandler<RunnerStatus> StatusChanged;
        public event EventHandler<FrameProgressInfo> FrameProgressChanged;

        public void Start(ModeProfile profile)
        {
            if (IsRunning) return;

            _stopRequested = false;
            IsRunning = true;
            RaiseStatus(RunnerStatus.Running);
            RaiseLog(LogLevel.Warn, "逐帧渲染引擎尚未接入（当前为框架占位），下一阶段实现帧调度。");
            RaiseLog(LogLevel.Info, $"已载入参数：exe={profile?.ExePath} 帧范围={profile?.StartFrame}-{profile?.EndFrame} 模板={profile?.OutputTemplate}");

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "FrameRenderController" };
            _worker.Start();
        }

        public void Stop()
        {
            if (!IsRunning) return;
            _stopRequested = true;
            RaiseStatus(RunnerStatus.Stopping);
            RaiseLog(LogLevel.Info, "正在停止…");
        }

        private void WorkerLoop()
        {
            while (!_stopRequested)
            {
                Thread.Sleep(200);
            }

            IsRunning = false;
            RaiseStatus(RunnerStatus.Stopped);
            RaiseLog(LogLevel.Info, "已停止。");
        }

        private void RaiseLog(LogLevel level, string message)
            => Log?.Invoke(this, new LogEntry(level, message));

        private void RaiseStatus(RunnerStatus status)
            => StatusChanged?.Invoke(this, status);
    }
}
