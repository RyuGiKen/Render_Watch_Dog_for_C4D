using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 进程健康检测工具，逐字复用自 Team Render Watch Dog 的判定思路：
    /// 端口占用、进程 Responding、_BugReport.txt 近期被改写。
    /// 供 TeamRenderController 与 FrameRenderController 共用。
    /// </summary>
    public static class ProcessHealth
    {
        /// <summary>
        /// 检查本机 TCP 端口是否被占用（与原程序一致：Loopback 监听成功=空闲，异常=占用）。
        /// </summary>
        public static bool IsPortUsed(int port)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return false;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return true;
            }
            catch
            {
                return true; // 其他异常也视为端口被占用
            }
        }

        /// <summary>
        /// 进程是否响应。无法判定时视为已挂（返回 false）。
        /// 注意：无窗口进程（如 Commandline.exe）Responding 恒为 false，调用方需按模式决定是否采用此信号。
        /// </summary>
        public static bool IsResponding(Process process)
        {
            if (process == null) return false;
            try
            {
                return process.Responding;
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                                       ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 异常记录文件是否在给定有效窗口内被改写。用 LastWriteTimeUtc，未来时间戳直接视为不活跃。
        /// 判定窗口完全由调用方传入：看门狗传 CheckIntervalSeconds*(MaxHang+1.5)，逐帧引擎另用基线法。
        /// </summary>
        public static bool IsBugReportRecent(string filePath, TimeSpan activeInterval)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(filePath);
                var now = DateTime.UtcNow;
                if (lastWrite > now)
                    return false;

                return (now - lastWrite) < activeInterval;
            }
            catch
            {
                return false;
            }
        }
    }
}
