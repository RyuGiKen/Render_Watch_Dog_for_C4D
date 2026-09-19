using System;
using System.Threading;
using System.Windows.Forms;

namespace RenderServerGui
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // 全局异常兜底：UI 线程异常弹窗提示且不崩窗；非 UI 线程异常记录并提示。
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UI.MainForm());
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ShowError("界面线程异常", e.Exception);
        }

        private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowError("后台线程异常", e.ExceptionObject as Exception, e.IsTerminating);
        }

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
                // 弹窗本身失败则退回到最简输出，避免二次抛异常
                Console.WriteLine(title + ": " + msg);
            }
        }
    }
}
