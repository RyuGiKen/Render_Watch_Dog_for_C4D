using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    internal class Program
    {
        // 配置参数
        private const string TargetPath = @"C:\Program Files\Maxon Cinema 4D 2026\Cinema 4D Team Render Client.exe";
        private const string ProcessName = "Cinema 4D Team Render Client";
        private const int CheckIntervalSeconds = 30; // 检查间隔（秒）
        private const int MaxHangCount = 4; // 最大挂起次数
        const int mPort = 5401;
        const string ReportPath = "C:\\Users\\12407024\\AppData\\Roaming\\Maxon\\Maxon Cinema 4D 2026_1ABCDC12_c\\_bugreports\\_BugReport.txt";
        // 用于跟踪进程挂起次数
        private static Dictionary<int, int> processHangCount = new Dictionary<int, int>();

        static void Main(string[] args)
        {
            Console.WriteLine($"开始监控进程: {ProcessName}");
            Console.WriteLine($"目标路径: {TargetPath}");
            Console.WriteLine($"检查间隔: {CheckIntervalSeconds}秒");
            Console.WriteLine($"连续挂起{MaxHangCount}次后将终止进程");
            Console.WriteLine("按 Ctrl+C 退出监控");
            Console.WriteLine("----------------------------------------");

            try
            {
                // 主监控循环
                while (true)
                {
                    try
                    {
                        CheckAndMonitorProcess();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检查过程中发生错误: {ex.Message}");
                    }

                    // 等待下一次检查
                    Thread.Sleep(CheckIntervalSeconds * 1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"监控程序异常终止: {ex.Message}");
            }
        }

        private static void CheckAndMonitorProcess()
        {
            // 查找目标进程
            Process targetProcess = FindTargetProcess();
            bool portUsing = CheckAndPrint(mPort);
            if (targetProcess == null)
            {
                // 进程不存在，清除相关记录
                //CleanUpOldRecords();
                processHangCount = new Dictionary<int, int>();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 目标进程不存在，端口[{mPort}]：" + (portUsing ? "占用" : "断开"));
                return;
            }

            try
            {
                // 检查进程是否响应
                bool isResponding = CheckProcessResponding(targetProcess);
                //bool portUsing = CheckAndPrint(mPort);
                bool newBugReport = CheckFileRecentlyModified(ReportPath, new TimeSpan(0, 0, CheckIntervalSeconds));

                if (!isResponding || !portUsing || newBugReport)
                {
                    // 进程无响应
                    int hangCount = GetAndIncrementHangCount(targetProcess.Id);

                    string str = $"进程无响应! PID: {targetProcess.Id}";
                    if (newBugReport)
                        str = $"进程异常! PID: {targetProcess.Id}";
                    else if (!portUsing)
                        str = $"端口[{mPort}]断开!";

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {str}, 挂起次数: {hangCount}/{MaxHangCount}");

                    if (hangCount >= MaxHangCount)
                    {
                        // 连续三次无响应，终止进程
                        KillProcess(targetProcess);
                        processHangCount.Remove(targetProcess.Id);
                    }
                }
                else
                {
                    // 进程正常响应，重置挂起计数
                    if (processHangCount.ContainsKey(targetProcess.Id))
                    {
                        processHangCount.Remove(targetProcess.Id);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 进程恢复正常，重置挂起计数. PID: {targetProcess.Id}");
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 进程[{targetProcess.Id}]正常运行，端口[{mPort}]：" + (portUsing ? "占用" : "断开"));
                    }
                }
            }
            finally
            {
                targetProcess.Dispose();
            }
        }

        private static Process FindTargetProcess()
        {
            // 方法1: 按名称查找
            Process[] processes = Process.GetProcessesByName(ProcessName);

            foreach (Process process in processes)
            {
                try
                {
                    // 方法2: 验证路径匹配
                    //if (process.MainModule != null &&
                    //    string.Equals(process.MainModule.FileName, TargetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return process;
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception ||
                                            ex is InvalidOperationException ||
                                            ex is NotSupportedException)
                {
                    // 无权限访问进程信息，跳过
                    continue;
                }
            }

            return null;
        }

        private static bool CheckProcessResponding(Process process)
        {
            try
            {
                // 尝试检查进程是否响应
                return process.Responding;
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                                       ex is System.ComponentModel.Win32Exception)
            {
                // 如果无法检查响应状态，认为进程已挂
                return false;
            }
        }

        private static int GetAndIncrementHangCount(int processId)
        {
            if (!processHangCount.ContainsKey(processId))
            {
                processHangCount[processId] = 0;
            }

            return ++processHangCount[processId];
        }

        private static void KillProcess(Process process)
        {
            try
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 正在终止进程... PID: {process.Id}, 名称: {process.ProcessName}");

                process.Kill();

                // 等待进程完全退出
                if (process.WaitForExit(5000))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] V 进程已成功终止. PID: {process.Id}");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ！ 进程终止超时，但已发送终止信号. PID: {process.Id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] X 终止进程失败: {ex.Message}");
            }
        }

        private static void CleanUpOldRecords()
        {
            // 清理不存在的进程记录
            List<int> processesToRemove = new List<int>();

            foreach (var kvp in processHangCount)
            {
                try
                {
                    Process.GetProcessById(kvp.Key);
                }
                catch (ArgumentException)
                {
                    // 进程不存在
                    processesToRemove.Add(kvp.Key);
                }
            }

            foreach (int pid in processesToRemove)
            {
                processHangCount.Remove(pid);
            }
        }
        /// <summary>
        /// 检查端口是否被使用
        /// </summary>
        public static bool IsPortUsed(int port, string protocol = "TCP")
        {
            try
            {
                if (protocol.ToUpper() == "TCP")
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return false;
                }
                else
                {
                    var client = new UdpClient(port);
                    return false;
                }
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
        /// 检查端口并打印结果
        /// </summary>
        public static bool CheckAndPrint(int port, string protocol = "TCP")
        {
            bool result = IsPortUsed(port, protocol);
            if (result)
            {
                //Console.WriteLine($"端口 {port}/{protocol} 已被占用");
            }
            else
            {
                //Console.WriteLine($"端口 {port}/{protocol} 可用");
            }
            return result;
        }
        static bool CheckFileRecentlyModified(string filePath, TimeSpan activeInterval)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                // 关键：用 *Write* 时间，别用 LastAccessTime（Windows 会缓存/禁用）
                var lastWrite = File.GetLastWriteTimeUtc(filePath);

                // 防御：未来时间戳（时钟回拨/时区问题/拷贝文件导致）直接视为不活跃更稳
                var now = DateTime.UtcNow;
                if (lastWrite > now)
                    return false;

                return (now - lastWrite).TotalSeconds < activeInterval.TotalSeconds * (MaxHangCount + 2);
            }
            catch { }
            return false;
        }
    }
}
