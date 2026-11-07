using System;
using System.Collections.Generic;
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
    /// 成品判定分两档：IsFrameRendered=存在且非空（排除 0 字节）；IsFrameSettled 再要求距最后写入≥指定秒（排除仍在写入的半截文件）。
    /// </summary>
    public static class FrameScanner
    {
        /// <summary>匹配模板中"一段连续的星号"帧号占位符。</summary>
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

        /// <summary>
        /// 目录内所有"符合同一命名规则"的产物文件中最新的最后写入时刻（UTC）。
        /// 用于无进展检测：产物 mtime 前进 = 渲染仍在正常产出（含正在写盘的半截文件）。
        /// 目录不存在、无产物或出错返回 DateTime.MinValue。
        /// </summary>
        public static DateTime GetLatestWriteTimeUtc(string template)
        {
            try
            {
                if (string.IsNullOrEmpty(template) || !TokenRegex.IsMatch(template))
                    return DateTime.MinValue;

                string dir = Path.GetDirectoryName(template);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return DateTime.MinValue;

                var di = new DirectoryInfo(dir);
                DateTime latest = DateTime.MinValue;
                foreach (FileInfo fi in di.GetFiles(Path.GetFileName(ToGlob(template))))
                {
                    if (fi.LastWriteTimeUtc > latest) latest = fi.LastWriteTimeUtc;
                }
                return latest;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 按命名模板解析目录里"符合同一命名规则"的文件帧号，返回去重升序列表。
        /// 与 ScanSettledStates / ScanBlock 共用 BuildFrameNameRegex，保证三处命名口径完全一致。
        /// 带前后缀的多通道文件整名不匹配→忽略；模板无占位符或目录不存在时返回空。
        /// </summary>
        public static List<int> EnumerateRenderedFrames(string template)
        {
            var result = new List<int>();
            Regex re = BuildFrameNameRegex(template);
            if (re == null) return result;

            string dir = Path.GetDirectoryName(template);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return result;

            var set = new HashSet<int>();
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    int frame;
                    if (TryParseFrame(re, Path.GetFileName(f), out frame)) set.Add(frame);
                }
            }
            catch { return result; }

            result.AddRange(set);
            result.Sort();
            return result;
        }

        /// <summary>
        /// 把模板的"文件名部分"转成整名匹配正则：占位星号段 -> 捕获组 (\d+)，其余字符转义。
        /// 用 (\d+) 而非等长通配，是为了容忍实际帧号位数与模板星号数不符（ToGlob 做不到这一点）。
        /// 模板无占位符 / 正则构造失败返回 null。
        /// </summary>
        private static Regex BuildFrameNameRegex(string template)
        {
            if (string.IsNullOrEmpty(template) || !TokenRegex.IsMatch(template)) return null;

            string filePattern = Path.GetFileName(template);
            var sb = new System.Text.StringBuilder("^");
            int i = 0;
            while (i < filePattern.Length)
            {
                var m = TokenRegex.Match(filePattern, i);
                if (m.Success && m.Index == i)
                {
                    sb.Append(@"(\d+)");
                    i += m.Length;
                }
                else
                {
                    int next = m.Success ? m.Index : filePattern.Length;
                    sb.Append(Regex.Escape(filePattern.Substring(i, next - i)));
                    i = next;
                }
            }
            sb.Append('$');

            try { return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase); }
            catch { return null; }
        }

        /// <summary>整名匹配并解析帧号；不匹配或解析失败返回 false。</summary>
        private static bool TryParseFrame(Regex re, string fileName, out int frame)
        {
            frame = 0;
            var mm = re.Match(fileName);
            if (!mm.Success) return false;
            return int.TryParse(mm.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out frame);
        }

        /// <summary>
        /// 一次目录枚举生成任务级帧状态表：索引 = 绝对帧号，长度 end+1。
        /// 0=范围外，1=未落定，2=已落定。判定口径与 IsFrameSettled 一致：存在·非空·距最后写入≥minAgeSeconds。
        /// minAgeSeconds&lt;=0 时只要求存在且非空（用于收尾复核，与时间无关）。
        /// 用 DirectoryInfo.EnumerateFiles 而非逐帧 FileInfo：Length / LastWriteTimeUtc 随枚举由
        /// WIN32_FIND_DATA 带回，不额外触发逐文件 stat。
        /// </summary>
        public static byte[] ScanSettledStates(string template, int start, int end, int minAgeSeconds)
        {
            if (end < 0) return new byte[0];
            var states = new byte[end + 1];
            for (int f = Math.Max(0, start); f <= end; f++) states[f] = FrameStateMap.Missing;

            Regex re = BuildFrameNameRegex(template);
            if (re == null) return states;

            string dir = Path.GetDirectoryName(template);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return states;

            try
            {
                foreach (FileInfo fi in new DirectoryInfo(dir).EnumerateFiles())
                {
                    int frame;
                    if (!TryParseFrame(re, fi.Name, out frame)) continue;
                    if (frame < start || frame > end) continue;
                    if (fi.Length <= 0) continue;                 // 0 字节：崩溃残留，视为缺帧
                    if (minAgeSeconds > 0 &&
                        (DateTime.UtcNow - fi.LastWriteTimeUtc).TotalSeconds < minAgeSeconds) continue;
                    states[frame] = FrameStateMap.Settled;
                }
            }
            catch { /* 目录不可读时保留已扫描结果，与 IsFrameSettled 的 catch→false 语义一致 */ }

            return states;
        }

        /// <summary>
        /// 一次目录枚举得到块内状态：存在且非空的帧数、块内最新写入时刻、首个缺失帧号。
        /// 只看 [from,to] 内的帧，避免别的任务/通道在写同一目录时干扰本块的无进展判定
        /// （旧实现 GetLatestWriteTimeUtc 扫的是整个目录）。
        /// </summary>
        public static BlockScan ScanBlock(string template, int from, int to)
        {
            var scan = new BlockScan(from, to);
            Regex re = BuildFrameNameRegex(template);
            if (re == null) return scan;

            string dir = Path.GetDirectoryName(template);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return scan;

            try
            {
                foreach (FileInfo fi in new DirectoryInfo(dir).EnumerateFiles())
                {
                    int frame;
                    if (!TryParseFrame(re, fi.Name, out frame)) continue;
                    if (frame < from || frame > to) continue;
                    if (fi.LastWriteTimeUtc > scan.LatestWriteUtc) scan.SetLatestWriteUtc(fi.LastWriteTimeUtc);
                    if (fi.Length > 0) scan.MarkPresent(frame);
                }
            }
            catch { /* 保留已扫描结果 */ }

            return scan;
        }
    }

    /// <summary>块内一次目录枚举的结果（帧号按 from..to 相对存放）。</summary>
    public sealed class BlockScan
    {
        private readonly bool[] _present;
        private readonly int _from;
        private int _nonEmptyCount;

        /// <summary>块内帧范围。</summary>
        public int From { get; private set; }
        /// <summary>块内帧范围。</summary>
        public int To { get; private set; }
        /// <summary>块内"存在且非空"的帧数。</summary>
        public int NonEmptyCount { get { return _nonEmptyCount; } }
        /// <summary>块内产物最近一次写入时刻（UTC）；无产物为 DateTime.MinValue。</summary>
        public DateTime LatestWriteUtc { get; private set; }

        public BlockScan(int from, int to)
        {
            _from = from;
            From = from;
            To = to;
            _present = new bool[Math.Max(0, to - from + 1)];
            LatestWriteUtc = DateTime.MinValue;
        }

        internal void SetLatestWriteUtc(DateTime utc) { LatestWriteUtc = utc; }

        internal void MarkPresent(int frame)
        {
            int idx = frame - _from;
            if (idx < 0 || idx >= _present.Length || _present[idx]) return;
            _present[idx] = true;
            _nonEmptyCount++;
        }

        public bool IsPresent(int frame)
        {
            int idx = frame - _from;
            return idx >= 0 && idx < _present.Length && _present[idx];
        }

        /// <summary>块内第一个"不存在或为空"的帧号；全部存在且非空返回 -1。</summary>
        public int FirstMissing()
        {
            for (int i = 0; i < _present.Length; i++) if (!_present[i]) return _from + i;
            return -1;
        }
    }
}
