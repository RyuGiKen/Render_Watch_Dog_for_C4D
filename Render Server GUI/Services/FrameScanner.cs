using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 输出目录帧文件扫描与命名模板展开。
    /// 模板帧号占位符写作 [x..]（一个或多个 x），x 的个数即补零位数。
    /// 例：D:\Output\Image_[xxxx].png + 帧 100 => D:\Output\Image_0100.png
    /// 成品判定只做一件事：文件存在且非空（0 字节的半截文件不算完成）。
    /// </summary>
    public static class FrameScanner
    {
        private static readonly Regex TokenRegex = new Regex(@"\[x+\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>把模板中的 [x..] 替换为补零后的帧号。若模板无占位符，则原样返回。</summary>
        public static string ExpandTemplate(string template, int frame)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            return TokenRegex.Replace(template, m =>
            {
                int width = m.Value.Length - 2; // 去掉两侧方括号
                if (width < 1) width = 1;
                return frame.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            });
        }

        /// <summary>按帧号展开出的完整输出路径。</summary>
        public static string FramePath(string template, int frame) => ExpandTemplate(template, frame);

        /// <summary>该帧是否已产出有效成品：文件存在且大小 &gt; 0（排除崩溃瞬间留下的空/半截文件）。</summary>
        public static bool IsFrameRendered(string template, int frame)
        {
            try
            {
                string path = ExpandTemplate(template, frame);
                var fi = new FileInfo(path);
                return fi.Exists && fi.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
