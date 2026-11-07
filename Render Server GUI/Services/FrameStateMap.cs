using System;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 任务级帧状态表：索引 = 绝对帧号，长度 End+1，0..Start-1 恒为 Outside。
    /// 0=Outside 非渲染帧（范围外）；1=Missing 未落定（缺帧）；2=Settled 已落定。
    ///
    /// 任务启动时由 FrameScanner.ScanSettledStates 一次目录枚举填充；
    /// 运行期的游标推进与块尾限位只查内存，块结束后由 FrameRenderController 增量更新。
    /// 存在的意义：C4D 的 -frame a b 1 是连续区间，块尾必须停在下一个已落定帧之前，
    /// 否则会把区间内已有成品一起卷入重渲（既浪费算力，又可能在崩溃时把好帧写成半截）。
    /// </summary>
    public sealed class FrameStateMap
    {
        public const byte Outside = 0;
        public const byte Missing = 1;
        public const byte Settled = 2;

        private readonly byte[] _states;

        /// <summary>任务起始帧。</summary>
        public int Start { get; private set; }
        /// <summary>任务结束帧。</summary>
        public int End { get; private set; }

        public FrameStateMap(int start, int end)
        {
            if (end < 0) end = 0;
            if (start < 0) start = 0;
            Start = start;
            End = end;
            // 0..start-1 天然为 Outside；范围内初始也视为 Outside，由 Load 填充 Missing/Settled
            _states = new byte[end + 1];
        }

        /// <summary>取某帧状态；越界返回 Outside。</summary>
        public byte Get(int frame)
            => frame >= 0 && frame < _states.Length ? _states[frame] : Outside;

        /// <summary>写某帧状态；范围外忽略，保证 [Start,End] 之外的语义不被破坏。</summary>
        public void Set(int frame, byte state)
        {
            if (frame >= Start && frame <= End) _states[frame] = state;
        }

        public bool IsSettled(int frame) => Get(frame) == Settled;

        /// <summary>整表载入启动扫描结果（数组长度不一致时按较短者拷贝，缺失部分保持 Outside）。</summary>
        public void Load(byte[] states)
        {
            if (states == null) return;
            Array.Copy(states, _states, Math.Min(states.Length, _states.Length));
        }

        /// <summary>区间内已落定帧数。</summary>
        public int CountSettled(int from, int to)
        {
            int lo = Math.Max(from, Start), hi = Math.Min(to, End), n = 0;
            for (int f = lo; f <= hi; f++) if (_states[f] == Settled) n++;
            return n;
        }

        /// <summary>区间批量置状态；自动夹到 [Start,End]。</summary>
        public void Mark(int from, int to, byte state)
        {
            int lo = Math.Max(from, Start), hi = Math.Min(to, End);
            for (int f = lo; f <= hi; f++) _states[f] = state;
        }

        /// <summary>区间内第一个未落定帧号；全部落定返回 -1。</summary>
        public int FirstUnsettled(int from, int to)
        {
            int lo = Math.Max(from, Start), hi = Math.Min(to, End);
            for (int f = lo; f <= hi; f++) if (_states[f] != Settled) return f;
            return -1;
        }
    }
}
