using System;
using System.Collections.Generic;
using System.Text;

namespace RenderServerGui.Services
{
    /// <summary>一个可直接交给 C4D 命令行渲染的连续段（等差数列）。</summary>
    public sealed class FrameSegment
    {
        /// <summary>段首帧。</summary>
        public int Start { get; set; }
        /// <summary>段尾帧（含）。</summary>
        public int End { get; set; }
        /// <summary>步进；1 为逐帧，&gt;1 为等差帧。</summary>
        public int Step { get; set; }

        /// <summary>该段包含的帧数。</summary>
        public int Count
        {
            get
            {
                if (Step <= 0 || End < Start) return 0;
                return (End - Start) / Step + 1;
            }
        }

        /// <summary>段的简短描述。</summary>
        public override string ToString()
        {
            if (Start == End) return Start.ToString();
            // 2 帧写成 a,b 更直观（如 5,16 不该显示成 5-16x11）；
            // 只有等差段帧数>=3 才用带步进表示，避免误解乘号。
            if (Step > 1 && Count >= 3) return string.Format("{0}-{1}:{2}", Start, End, Step);
            if (Step > 1 && Count == 2) return string.Format("{0},{1}", Start, End);
            return string.Format("{0}-{1}", Start, End);
        }
    }

    /// <summary>
    /// 帧串表达式解析与连续段拆分。供 C4D 读取器、UI、调度器共用。
    /// 语法（逗号分隔，逐段解析）：
    ///   单个帧        5
    ///   连续区间      1-10          逐帧
    ///   带步进区间    0-20:5        帧 0,5,10,15,20
    ///   混合          1-3,10,20-22  去重升序
    /// 空表达式表示回退到调用方给定的 [start,end] 连续区间（兼容旧配置）。
    /// </summary>
    public static class FrameSpec
    {
        private const int MaxFrames = 200000;

        /// <summary>解析帧串；为空时回退到 [fallbackStart, fallbackEnd] 逐帧区间。</summary>
        public static List<int> Parse(string spec, int fallbackStart, int fallbackEnd)
        {
            var set = new SortedSet<int>();
            if (!string.IsNullOrWhiteSpace(spec))
            {
                foreach (string raw in spec.Split(','))
                {
                    string p = raw.Trim();
                    if (p.Length == 0) continue;

                    // 第一段决定 lo/hi，冒号后为步进
                    string stepPart = null;
                    int colon = p.IndexOf(':');
                    if (colon > 0)
                    {
                        stepPart = p.Substring(colon + 1).Trim();
                        p = p.Substring(0, colon).Trim();
                    }

                    int dash = p.IndexOf('-', 1);   // 从下标1开始，允许前导负号
                    if (dash > 0)
                    {
                        int lo, hi;
                        if (!int.TryParse(p.Substring(0, dash), out lo)) continue;
                        if (!int.TryParse(p.Substring(dash + 1), out hi)) continue;
                        if (hi < lo) { int t = lo; lo = hi; hi = t; }

                        int step = 1;
                        if (stepPart != null && int.TryParse(stepPart, out step) && step > 1)
                        {
                            for (int f = lo; f <= hi; f += step)
                            {
                                set.Add(f);
                                if (set.Count >= MaxFrames) break;
                            }
                        }
                        else
                        {
                            long count = (long)hi - lo + 1;
                            if (count > MaxFrames) hi = lo + MaxFrames - 1;
                            for (int f = lo; f <= hi; f++)
                            {
                                set.Add(f);
                                if (set.Count >= MaxFrames) break;
                            }
                        }
                    }
                    else
                    {
                        int f;
                        if (int.TryParse(p, out f)) set.Add(f);
                    }
                    if (set.Count >= MaxFrames) break;
                }
            }

            if (set.Count == 0 && fallbackEnd >= fallbackStart)
            {
                long span = (long)fallbackEnd - fallbackStart + 1;
                if (span > MaxFrames) fallbackEnd = fallbackStart + MaxFrames - 1;
                for (int f = fallbackStart; f <= fallbackEnd; f++) set.Add(f);
            }
            return new List<int>(set);
        }

        /// <summary>
        /// 把帧列表拆成若干等差数列段，每段可直接转成 C4D 的 -frame 参数。
        /// 帧差相同的连续元素合并为一段（如 0,5,10 -> 段 0-20 step5）。
        /// </summary>
        public static List<FrameSegment> SplitSegments(List<int> frames)
        {
            var result = new List<FrameSegment>();
            if (frames == null || frames.Count == 0) return result;

            var sorted = new List<int>(frames);
            sorted.Sort();
            // 去重
            for (int i = sorted.Count - 1; i > 0; i--)
                if (sorted[i] == sorted[i - 1]) sorted.RemoveAt(i);

            int idx = 0;
            while (idx < sorted.Count)
            {
                int start = sorted[idx];
                int step = (idx + 1 < sorted.Count) ? sorted[idx + 1] - sorted[idx] : 1;
                if (step <= 0) step = 1;
                int end = start;
                idx++;

                while (idx < sorted.Count && sorted[idx] - end == step)
                {
                    end = sorted[idx];
                    idx++;
                }
                // 只剩一个元素时 step 归 1（单帧段无需步进）
                if (start == end) step = 1;
                result.Add(new FrameSegment { Start = start, End = end, Step = step });
            }
            return result;
        }

        /// <summary>把帧列表规范化为帧串表达式（升序、去重、等差段用带步进区间）。</summary>
        public static string Format(List<int> frames)
        {
            var segs = SplitSegments(frames);
            if (segs.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var s in segs)
            {
                if (s.Start == s.End)
                {
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(s.Start);
                }
                else if (s.Step > 1 && s.Count >= 3)
                {
                    // 等差段至少 3 帧才用带步进区间；2 帧写成 a,b 更直观
                    if (sb.Length > 0) sb.Append(',');
                    sb.AppendFormat("{0}-{1}:{2}", s.Start, s.End, s.Step);
                }
                else if (s.Step > 1 && s.Count == 2)
                {
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(s.Start); sb.Append(','); sb.Append(s.End);
                }
                else
                {
                    if (sb.Length > 0) sb.Append(',');
                    sb.AppendFormat("{0}-{1}", s.Start, s.End);
                }
            }
            return sb.ToString();
        }
    }
}
