using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using RenderServerGui.Services;

namespace RenderServerGui
{
    /// <summary>应用入口：单例互斥、装配全局异常兜底后运行主窗体。</summary>
    internal static class Program
    {
        /// <summary>单例互斥体：同一 exe 同一时间只允许运行一份（进程退出时由系统自动释放）。</summary>
        private const string SingleInstanceMutexName = @"Local\RenderServerGui.{7C3A9E21-4D8F-4F1B-9A6C-2E5B8D0F3A47}";

        /// <summary>主入口：先做单例检查，再注册未处理异常兜底并启动主窗体。</summary>
        [STAThread]
        private static void Main()
        {
            using (var mutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew))
            {
                if (!createdNew) return; // 已有实例在运行：静默退出

                // UI 线程异常弹窗且不崩窗；非 UI 线程异常记录并提示
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

                // 加载多语言表（exe 目录下 i18n/*.txt）；具体语言在 MainForm 读取配置后设定
                Localizer.Load(Path.Combine(AppContext.BaseDirectory, "i18n"));

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new UI.MainForm());
            }
        }

        /// <summary>UI 线程未处理异常的兜底处理。</summary>
        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ShowError("界面线程异常", e.Exception);
        }

        /// <summary>非 UI 线程未处理异常的兜底处理。</summary>
        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowError("后台线程异常", e.ExceptionObject as Exception, e.IsTerminating);
        }

        /// <summary>统一以弹窗呈现异常；弹窗自身失败时退回控制台输出，避免二次抛异常。</summary>
        private static void ShowError(string title, Exception ex, bool terminating = false)
        {
            string msg = (terminating ? "程序遇到严重错误即将终止。\n\n" : "程序遇到未处理异常，已尽量维持运行。\n\n")
                         + (ex != null ? ex.Message + "\n\n" + ex.StackTrace : "未知错误");
            try
            {
                MessageBox.Show(msg, "Render Server GUI - " + title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                Console.WriteLine(title + ": " + msg);
            }
        }
    }
}
