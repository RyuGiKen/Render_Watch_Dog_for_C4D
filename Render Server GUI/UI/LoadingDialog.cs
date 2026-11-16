using System;
using System.Drawing;
using System.Windows.Forms;

namespace RenderServerGui.UI
{
    /// <summary>
    /// 读取大文件期间显示的轻量进度提示。纯代码构建，无 Designer 依赖。
    /// 显示一条信息 + 不确定进度条（Marquee），不提供取消（解析很快，取消无意义）。
    /// 由调用方在后台线程开始前 Show(this)，结束后 Close()。
    /// </summary>
    internal sealed class LoadingDialog : Form
    {
        private readonly Label _label;

        /// <param name="text">提示文字（应已本地化）。</param>
        public LoadingDialog(string text)
        {
            _label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(8),
                Text = text ?? string.Empty
            };

            var bar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Minimum = 0,
                Maximum = 100
            };

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 12) };
            panel.Controls.Add(bar);

            Controls.Add(panel);
            Controls.Add(_label);

            Text = string.Empty;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ControlBox = false;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(360, 92);
        }

        /// <summary>线程安全地更新提示文字。</summary>
        public void SetTextSafe(string text)
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(new Action(() => _label.Text = text));
                else _label.Text = text;
            }
            catch { /* 关闭过程中忽略 */ }
        }
    }
}
