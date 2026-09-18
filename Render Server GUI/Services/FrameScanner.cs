using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 输出目录帧文件扫描与命名模板展开。
    /// 帧号占位符写作一段连续的星号（如 ****），星号个数即补零位数。
    /// 选星号是因为 Windows 文件名不允许出现 *，正常渲染产物不会与占位符冲突，避免误判。
    /// 例：D:\Output\Image_****.png + 帧 100 => D:\Output\Image_0100.png
    /// 成品判定只做一件事：文件存在且非空（0 字节的半截文件不算完成）。
    /// </summary>
    public static class FrameScanner
    {
        // 一段连续的 *
        private static readonly Regex TokenRegex = new Regex(@"\*+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>把模板中每段连续的 * 替换为补零后的帧号（位数=星号个数）。无占位符则原样返回。</summary>
        public static string ExpandTemplate(string template, int frame)
        {
            if (string.IsNullOrEmpty(template))
                return template;

            return TokenRegex.Replace(template, m =>
            {
                int width = m.Value.Length; // 星号个数即补零位数
                if (width < 1) width = 1;
                return frame.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            });
        }

        /// <summary>按帧号展开出的完整输出路径。</summary>
        public static string FramePath(string template, int frame) => ExpandTemplate(template, frame);

        /// <summary>
        /// 把模板转成文件系统通配（每个占位星号 -> 一个 ? ，即"该位有且仅有一个字符"），
        /// 用于在目录里判断"是否存在符合同一命名规则的文件"（可覆盖多通道加前/后缀的情况）。
        /// 例：D:\Output\Image_****.png => D:\Output\Image_????.png
        /// </summary>
        public static string ToGlob(string template)
        {
            if (string.IsNullOrEmpty(template))
                return template;
            return TokenRegex.Replace(template, m => new string('?', m.Value.Length));
        }

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

        /// <summary>
        /// 该帧产物是否"已落定"：存在、非空、且距最后写入已过 minAgeSeconds 秒。
        /// 用于确认渲染真正写完（避开正在写入的半截文件）；minAgeSeconds&lt;=0 时等价于 IsFrameRendered。
        /// </summary>
        public static bool IsFrameSettled(string template, int frame, int minAgeSeconds)
        {
            try
            {
                string path = ExpandTemplate(template, frame);
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length <= 0) return false;
                if (minAgeSeconds <= 0) return true;
                double age = (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalSeconds;
                return age >= minAgeSeconds;
            }
            catch
            {
                return false;
            }
        }
    }
}
