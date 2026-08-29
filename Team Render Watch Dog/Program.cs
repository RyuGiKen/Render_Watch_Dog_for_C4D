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

namespace TeamRenderWatchDog
{
    internal class Program
    {
        /// <summary>
        /// 进程路径
        /// </summary>
        private const string TargetPath = @"C:\Program Files\Maxon Cinema 4D 2026\Cinema 4D Team Render Client.exe";
        /// <summary>
        /// 进程名称
        /// </summary>
        private const string ProcessName = "Cinema 4D Team Render Client";
        /// <summary>
        /// 检查间隔（秒）
        /// </summary>
        private const int CheckIntervalSeconds = 60;
        /// <summary>
        /// 最大挂起次数
        /// </summary>
        private const int MaxHangCount = 4;
        /// <summary>
        /// 端口号
        /// </summary>
        const int mPort = 5401;
        /// <summary>
        /// 异常记录路径
        /// </summary>
        const string ReportPath = @"C:\Users\12407024\AppData\Roaming\Maxon\Maxon Cinema 4D 2026_1ABCDC12_c\_bugreports\_BugReport.txt";
        /// <summary>
        /// 缓存目录
        /// </summary>
        const string CachePath = @"C:\Users\12407024\AppData\Roaming\Maxon\Maxon Cinema 4D 2026_1ABCDC12_c\teamrender_client\users\client";
        /// <summary>
        /// 跟踪进程挂起次数
        /// </summary>
        private static Dictionary<int, int> processHangCount = new Dictionary<int, int>();
        /// <summary>
        /// 上次启动时间
        /// </summary>
        static DateTime LastStartTime = DateTime.Now;
        /// <summary>
        /// 连续工作限制（分钟）
        /// </summary>
        const int WorkingTime = 60;
        /// <summary>
        /// 休息时间（分钟）
        /// </summary>
        const int RestTime = 3;

        static void Main(string[] args)
        {
            Console.WriteLine($"开始监控进程: {ProcessName}");
            Console.WriteLine($"端口号: {mPort}");
            Console.WriteLine($"目标路径: {TargetPath}");
            Console.WriteLine($"异常路径: {ReportPath}");
            Console.WriteLine($"检查间隔: {CheckIntervalSeconds}秒");
            Console.WriteLine($"连续挂起{MaxHangCount}次后将终止进程");
            Console.WriteLine($"连续运行{WorkingTime}分钟后将休息{RestTime}分钟");
            Console.WriteLine("按 Ctrl+C 退出监控");
            Console.WriteLine("----------------------------------------");
            LastStartTime = DateTime.Now;
            try
            {
                // 主监控循环
                while (true)
                {
                    try
                    {
                        Process targetProcess = FindTargetProcess();

                        TimeSpan workedtime = DateTime.Now - LastStartTime;
                        if (targetProcess != null && workedtime.TotalMinutes > WorkingTime)//长时间工作过热，中间休息降温
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 连续工作{workedtime:hh\\:mm\\:ss\\:fff}，休息降温");
                            KillProcess(targetProcess, false);
                            Thread.Sleep(RestTime * 60 * 1000);
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 休息结束");
                        }

                        if (targetProcess == null)
                        {
                            targetProcess = StartProcess();
                            Thread.Sleep(CheckIntervalSeconds * 1000);
                        }
                        CheckAndMonitorProcess(targetProcess);
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
        /// <summary>
        /// 监测进程是否正常
        /// </summary>
        /// <param name="targetProcess"></param>
        private static void CheckAndMonitorProcess(Process targetProcess)
        {
            bool portUsing = CheckAndPrint(mPort);
            if (targetProcess == null)
            {
                // 进程不存在，清除相关记录
                //CleanUpOldRecords();
                processHangCount = new Dictionary<int, int>();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 目标进程不存在，端口[{mPort}]：{(portUsing ? "占用" : "断开")}");
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

                    string str = $" 进程{(newBugReport ? "异常! " : "无响应! ")} PID: {targetProcess.Id} 端口[{mPort}]：{(portUsing ? "占用" : "断开")}";
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]{str}, 挂起次数: {hangCount}/{MaxHangCount}");

                    if (hangCount >= MaxHangCount)
                    {
                        // 连续无响应，终止进程
                        KillProcess(targetProcess, true);
                        processHangCount.Remove(targetProcess.Id);
                    }
                }
                else
                {
                    // 进程正常响应，重置挂起计数
                    if (processHangCount.ContainsKey(targetProcess.Id))
                    {
                        processHangCount.Remove(targetProcess.Id);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 进程恢复正常，重置挂起计数. PID: {targetProcess.Id} 端口[{mPort}]：{(portUsing ? "占用" : "断开")}");
                    }
                    else
                    {
                        TimeSpan workedtime = DateTime.Now - LastStartTime;
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 进程[{targetProcess.Id}]正常运行，端口[{mPort}]：{(portUsing ? "占用" : "断开")}  已正常工作{workedtime:hh\\:mm\\:ss\\:fff}");
                    }
                }
            }
            finally
            {
                targetProcess.Dispose();
            }
        }
        /// <summary>
        /// 查找进程
        /// </summary>
        /// <returns></returns>
        private static Process FindTargetProcess()
        {
            Process[] processes = Process.GetProcessesByName(ProcessName);
            foreach (Process process in processes)
            {
                try
                {
                    //if (process.MainModule != null && string.Equals(process.MainModule.FileName, TargetPath, StringComparison.OrdinalIgnoreCase))//验证路径匹配
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
        /// <summary>
        /// 响应状态
        /// </summary>
        /// <param name="process"></param>
        /// <returns></returns>
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
        /// <summary>
        /// 计数
        /// </summary>
        /// <param name="processId"></param>
        /// <returns></returns>
        private static int GetAndIncrementHangCount(int processId)
        {
            if (!processHangCount.ContainsKey(processId))
            {
                processHangCount[processId] = 0;
            }

            return ++processHangCount[processId];
        }
        /// <summary>
        /// 结束进程
        /// </summary>
        /// <param name="process"></param>
        /// <param name="restart"></param>
        private static void KillProcess(Process process, bool restart)
        {
            if (process != null)
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
                Thread.Sleep(CheckIntervalSeconds * 2 * 1000);
            }
            if (restart)
                StartProcess();
        }
        /// <summary>
        /// 清空缓存目录下的所有文件与子目录（保留目录本身）
        /// </summary>
        private static void ClearCache()
        {
            try
            {
                // 删除所有文件
                foreach (string file in Directory.GetFiles(CachePath))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 删除文件失败: {file} - {ex.Message}");
                    }
                }

                // 删除所有子目录（递归）
                foreach (string dir in Directory.GetDirectories(CachePath))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 删除目录失败: {dir} - {ex.Message}");
                    }
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 清空缓存完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 清空缓存异常: {ex.Message}");
            }
        }
        /// <summary>
        /// 启动进程
        /// </summary>
        /// <returns></returns>
        static Process StartProcess()
        {
            Process process = null;
            ClearCache();
            try
            {
                process = Process.Start(TargetPath);
                LastStartTime = DateTime.Now;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] V 启动进程成功. PID: {process.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] X 启动进程失败: {ex.Message}");
            }
            Thread.Sleep(CheckIntervalSeconds * 1000);
            return process;
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
        /// <summary>
        /// 异常文件近期有更新
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="activeInterval"></param>
        /// <returns></returns>
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

                return (now - lastWrite).TotalSeconds < activeInterval.TotalSeconds * (MaxHangCount + 1.5f);
            }
            catch { }
            return false;
        }
    }
}
