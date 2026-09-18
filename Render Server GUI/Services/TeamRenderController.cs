using System;
using System.Threading;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>
    /// Team Render 看门狗控制器（阶段一：框架占位）。
    /// 阶段二将把 Team Render Watch Dog 的完整循环逻辑移植到此：
    /// 找进程 / 过热休息 / 三重异常判定 / 挂起计数 / 清缓存 / 杀进程重启。
    /// </summary>
    public class TeamRenderController : IRenderController
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
            RaiseLog(LogLevel.Warn, "Team Render 看门狗引擎尚未接入（当前为框架占位），下一阶段实现完整监控循环。");
            RaiseLog(LogLevel.Info, $"已载入参数：进程={profile?.ProcessName} 端口={profile?.Port} 间隔={profile?.CheckIntervalSeconds}s");

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "TeamRenderController" };
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
            // 占位：仅保持运行状态直到收到停止请求，不做实际监控。
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
