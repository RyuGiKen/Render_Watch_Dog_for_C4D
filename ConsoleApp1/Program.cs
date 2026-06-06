using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

            if (targetProcess == null)
            {
                // 进程不存在，清除相关记录
                //CleanUpOldRecords();
                processHangCount = new Dictionary<int, int>();
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 目标进程不存在，跳过处理");
                return;
            }

            try
            {
                // 检查进程是否响应
                bool isResponding = CheckProcessResponding(targetProcess);

                if (!isResponding)
                {
                    // 进程无响应
                    int hangCount = GetAndIncrementHangCount(targetProcess.Id);

                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 进程无响应! PID: {targetProcess.Id}, 挂起次数: {hangCount}/{MaxHangCount}");

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
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 进程[{targetProcess.Id}]正常运行");
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
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ 进程已成功终止. PID: {process.Id}");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ⚠ 进程终止超时，但已发送终止信号. PID: {process.Id}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✗ 终止进程失败: {ex.Message}");
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
    }
}
