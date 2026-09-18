using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 输出目录帧文件扫描与命名模板展开。
    /// 模板帧号占位符写作 [x..]（一个或多个 x），x 的个数即补零位数。
    /// 例：D:\Output\Image_[xxxx].png + 帧 100 => D:\Output\Image_0100.png
    /// </summary>
    public static class FrameScanner
    {
        private static readonly Regex TokenRegex = new Regex(@"\[x+\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// 把模板中的 [x..] 替换为补零后的帧号。若模板无占位符，则原样返回。
        /// </summary>
        public static string ExpandTemplate(string template, int frame)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            return TokenRegex.Replace(template, m =>
            {
                int width = m.Value.Length - 2; // 去掉两侧的方括号
                if (width < 1) width = 1;
                return frame.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            });
        }

        /// <summary>
        /// 该帧的输出文件是否已存在（用于跳过已完成帧、支持断点续渲）。
        /// </summary>
        public static bool IsFrameComplete(string template, int frame)
        {
            string path = ExpandTemplate(template, frame);
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }
    }
}
