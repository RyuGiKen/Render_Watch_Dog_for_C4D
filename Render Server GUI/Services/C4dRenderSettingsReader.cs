using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.IO.MemoryMappedFiles;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 从工程内读到的一条渲染设置（取「最可能是当前活动」的那一组，也可读全部）。
    /// 只承载本工具需要的字段：输出路径/模板、帧范围、以及用于提示的模式说明。
    /// </summary>
    public sealed class C4dSceneRenderInfo
    {
        /// <summary>二进制文件中的块序号（0 起）。</summary>
        public int BlockIndex { get; set; }
        /// <summary>该块在文件中的字节偏移，便于排查。</summary>
        public long BlockOffset { get; set; }
        /// <summary>块内条目数。</summary>
        public int EntryCount { get; set; }

        /// <summary>RDATA_FRAMESEQUENCE：0=手动 1=当前帧 2=全部帧 3=预览范围 4=自定义。</summary>
        public int FrameSequence { get; set; }
        /// <summary>帧范围模式的可读名称。</summary>
        public string FrameSequenceName { get; set; }

        /// <summary>工程里记录的原始输出路径（RDATA_PATH，通常不含帧号与扩展名）。</summary>
        public string OutputPath { get; set; }
        /// <summary>按本工具命名规则组装出的输出模板（连续星号=帧号占位，星号个数=补零位数）。</summary>
        public string OutputTemplate { get; set; }
        /// <summary>输出格式插件 ID，用于判断扩展名。</summary>
        public int FormatId { get; set; }
        /// <summary>输出格式扩展名（含点，如 .png）；未知为空。</summary>
        public string FormatExtension { get; set; }

        /// <summary>帧率。</summary>
        public double Fps { get; set; }
        /// <summary>实际渲染起始帧（仅 FrameRangeKnown=true 时有效）。</summary>
        public int StartFrame { get; set; }
        /// <summary>实际渲染结束帧（仅 FrameRangeKnown=true 时有效）。</summary>
        public int EndFrame { get; set; }
        /// <summary>是否从文件里可靠读到了具体帧号。当前帧/全部帧/预览范围模式为 false。</summary>
        public bool FrameRangeKnown { get; set; }
        /// <summary>原始帧号序列是否被截断（不连续时只取第一段连续帧）。</summary>
        public bool Truncated { get; set; }
        /// <summary>补充说明的 i18n key；UI 用 Localizer.Tf(NoteKey, Note, NoteArgs) 输出。</summary>
        public string NoteKey { get; set; }
        /// <summary>补充说明的格式化参数。</summary>
        public object[] NoteArgs { get; set; }
        /// <summary>补充说明的简体中文原文（缺翻译时的回退文案，也便于排查）。</summary>
        public string Note { get; set; }
        /// <summary>是否用文档时间轴补全了帧范围（ALLFRAMES/PREVIEWRANGE 模式）。</summary>
        public bool FromDocumentTimeline { get; set; }
        /// <summary>是否从文件二进制的活动标志（块前 0F 10 00 00）判定为活动组。</summary>
        public bool IsActiveFromFlag { get; set; }
    }

    /// <summary>
    /// 直接读取 Cinema 4D（.c4d）二进制工程里的渲染设置，不需要安装/启动 C4D，也不调用 Python。
    ///
    /// 格式依据（在 C4D 2026.3 的 .c4d 上逆向并逐字段校验过）：
    ///   * 文件头 QC4DC4D6（或 XC4DC4D6），整体为 Motorola 大端字节序，未压缩；
    ///   * 渲染设置以 HyperFile / BaseContainer 条目流存放，单条目布局：
    ///         0F | id : u32be | 0F | type : u32be | type : u8 | value
    ///   * type 取值（HYPERFILEVALUE_*）：
    ///         15 INT32(4B) / 19 FLOAT-Real(8B double) / 20 FLOAT64(8B) / 21 BOOL(4B)
    ///         22 TIME(16B = 两个 double: numerator / denominator 秒)
    ///         130 STRING / 131 FILENAME = u32be 字节长度 + UTF-16BE 文本
    ///   * 渲染设置块 = 一段连续条目，同时含 RDATA_XRES(5008) 与 RDATA_PATH(5041)；
    ///   * CUSTOM 模式的帧串 RDATA_FRAME_RANGE_STRING(5015) 不在主块链里，需全文件扫描后按偏移就近关联。
    ///
    /// 边界（重要）：
    ///   * ALLFRAMES / PREVIEWRANGE / CURRENTFRAME 的实际帧号都来自文档时间轴结构。
    ///     它在文件里以裸 BaseTime 数组 [cur, min, max, min, max]（17 字节/元素，
    ///     marker=0x16，两个 double 秒数）存放，可离线读出：
    ///       ALLFRAMES/PREVIEWRANGE -> [min, max] * fps；CURRENTFRAME -> cur * fps。
    ///     本类用 FindDocumentTimeline 提取；读不到时返回 FrameRangeKnown=false。
    ///   * 一个 .c4d 可含多组渲染设置（且可能有树状层级/残留块）。本类用启发式挑「最可能活动」
    ///     的一组：输出路径非空 + 格式非默认 + 开启保存；全都没特征时取文件里第一组。
    ///   * 帧范围不连续时（自定义帧串如 "1-2,90"、或手动步进&gt;1），按需求只取第一段连续帧。
    /// </summary>
    public static class C4dRenderSettingsReader
    {
        // ---- RDATA 常量 ID（取自 C4D 自带 Python stub，数值跨版本稳定）----
        private const int IdFrameRangeString = 5015;
        private const int IdFrameSequence = 5016;
        private const int IdFrameFrom = 5017;
        private const int IdFrameTo = 5018;
        private const int IdFrameRate = 5020;
        private const int IdFormat = 5033;
        private const int IdNameFormat = 5035;
        private const int IdPath = 5041;
        private const int IdSaveImage = 5076;
        private const int IdFrameStep = 5081;
        private const int IdXRes = 5008;

        private const int SeqManual = 0;
        private const int SeqCurrentFrame = 1;
        private const int SeqAllFrames = 2;
        private const int SeqPreviewRange = 3;
        private const int SeqCustom = 4;

        private const int TypeString = 130;
        private const int TypeFilename = 131;
        private const byte EntryMarker = 0x0F;

        /// <summary>读取工程里「最可能是当前活动」的那组渲染设置。失败抛异常，由调用方记录日志。</summary>
        public static C4dSceneRenderInfo ReadActive(string c4dPath)
        {
            return PickActive(ReadAll(c4dPath));
        }

        /// <summary>
        /// 从 ReadAll 的结果里挑一组：输出路径非空 + 格式非默认 + 开启保存更像正在使用的设置。
        /// 平局时取文件里靠前的一组。单独暴露是为了让 UI 只解析一次就能同时拿到「全部组」与「选中组」。
        /// </summary>
        public static C4dSceneRenderInfo PickActive(List<C4dSceneRenderInfo> all)
        {
            if (all == null || all.Count == 0)
                throw new InvalidDataException("文件中未找到渲染设置块");

            // 权威标志优先：文件里若有块带活动标记（0F 10 00 00），直接用它。
            for (int i = 0; i < all.Count; i++)
                if (all[i].IsActiveFromFlag) return all[i];

            // 回退启发式：输出路径非空 + 格式非默认 + 开启保存更像正在使用的设置。
            int best = 0, bestScore = int.MinValue;
            for (int i = 0; i < all.Count; i++)
            {
                int score = 0;
                if (!string.IsNullOrEmpty(all[i].OutputPath)) score += 4;
                if (all[i].FormatId != 0 && all[i].FormatId != 1100) score += 2; // 1100=默认 TIFF
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return all[best];
        }

        /// <summary>读取工程内全部渲染设置块，按文件偏移升序。</summary>
        public static List<C4dSceneRenderInfo> ReadAll(string c4dPath)
        {
            if (string.IsNullOrWhiteSpace(c4dPath)) throw new ArgumentException("工程路径为空");
            // 内存映射读取：不把整个文件读进托管数组，规避 File.ReadAllBytes 的 ~2GB int 上限。
            // 支持 7GB+ 工程；操作系统按需分页，常驻内存很小。
            long fileLen = new FileInfo(c4dPath).Length;
            if (fileLen < 16) throw new InvalidDataException("文件过小，不是有效的 .c4d");
            if (fileLen > 7L * 1024 * 1024 * 1024)
                throw new InvalidDataException("工程文件超过 7GB 上限");

            using (var mapped = new MappedFile(c4dPath))
            {
            return ReadAllCore(mapped);
            }
        }

        private static List<C4dSceneRenderInfo> ReadAllCore(MappedFile data)
        {
            string magic = data.ReadAscii(0, 4);
            if (magic != "QC4D" && magic != "XC4D")
                throw new InvalidDataException("不是可识别的 Cinema 4D 二进制工程（文件头 " + magic + "）");

            // C4D 把渲染设置对象写在文档末尾：先从尾部 32MB 扫描（毫秒级），
            // 找不到再回退全文件，保证小文件/异常布局仍正确。
            var blocks = FindRenderBlocks(data, data.ScanStart);
            if (blocks.Count == 0 && data.ScanStart > 0)
                blocks = FindRenderBlocks(data, 0);
            var rangeStrings = FindFrameRangeStrings(data, data.ScanStart);
            if (rangeStrings.Count == 0 && data.ScanStart > 0)
                rangeStrings = FindFrameRangeStrings(data, 0);
            double[] timeline = FindDocumentTimeline(data, data.ScanStart);   // [currentSec, minSec, maxSec]
            if (timeline == null && data.ScanStart > 0)
                timeline = FindDocumentTimeline(data, 0);   // 尾部没有时回退全文件

            // 从二进制推断文档帧率（BaseTime 分母 LCM + 常见帧率约束）
            double docFps = DetectDocumentFps(data, data.ScanStart);
            if (docFps <= 0) docFps = 30.0;

            // CUSTOM 帧串归属：每个帧串归给「它前面最近的那一个块」。
            // 注意不能反过来按块去找后面的帧串——那样同一个帧串会被它后面的
            // 所有块共享，多组 CUSTOM 设置时会串味。
            var rangeByBlock = new Dictionary<int, string>();
            foreach (var kv in rangeStrings)
            {
                int owner = -1;
                for (int i = 0; i < blocks.Count; i++)
                {
                    if (blocks[i].Offset <= kv.Key) owner = i;
                    else break;                       // 块按偏移升序，后面的不用再看
                }
                if (owner >= 0 && !rangeByBlock.ContainsKey(owner))
                    rangeByBlock[owner] = kv.Value;
            }

            var result = new List<C4dSceneRenderInfo>();

            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                var map = new Dictionary<int, object>();
                foreach (var e in b.Entries)
                    if (!map.ContainsKey(e.Id)) map[e.Id] = e.Value;

                string rangeStr = null;
                rangeByBlock.TryGetValue(i, out rangeStr);

                int seq = GetInt(map, IdFrameSequence, -1);
                double fps = GetDouble(map, IdFrameRate, 30.0);
                if (fps <= 0) fps = 30.0;
                string rawPath = GetString(map, IdPath, string.Empty);
                int formatId = GetInt(map, IdFormat, 0);
                int nameFormat = GetInt(map, IdNameFormat, 0);
                int step = GetInt(map, IdFrameStep, 1);
                if (step < 1) step = 1;
                bool saveImage = GetInt(map, IdSaveImage, 1) != 0;
                string ext = FormatToExtension(formatId);

                var info = new C4dSceneRenderInfo
                {
                    BlockIndex = i,
                    BlockOffset = b.Offset,
                    EntryCount = b.Entries.Count,
                    FrameSequence = seq,
                    FrameSequenceName = SequenceName(seq),
                    OutputPath = rawPath,
                    OutputTemplate = BuildOutputTemplate(rawPath, nameFormat, ext),
                    FormatId = formatId,
                    FormatExtension = ext,
                    Fps = docFps,
                    FrameRangeKnown = false,
                    Truncated = false,
                    NoteKey = null,
                    NoteArgs = null,
                    Note = string.Empty,
                    IsActiveFromFlag = b.IsActive
                };

                ResolveFrameRange(info, map, seq, docFps, step, rangeStr, timeline);
                if (!saveImage && string.IsNullOrEmpty(info.Note))
                {
                    info.NoteKey = "log.sceneNoteSaveOff";
                    info.NoteArgs = null;
                    info.Note = "工程内未勾选保存图像，输出路径可能不会被写入";
                }
                result.Add(info);
            }
            return result;
        }

        // ---------------------------------------------------------------
        // 帧范围
        // ---------------------------------------------------------------

        /// <summary>按帧范围模式推导实际帧号；不连续时只保留第一段连续帧。</summary>
        private static void ResolveFrameRange(C4dSceneRenderInfo info, Dictionary<int, object> map,
                                             int seq, double fps, int step, string rangeStr,
                                             double[] timeline)
        {
            if (seq == SeqManual)
            {
                double fromSec = BaseTimeSeconds(map, IdFrameFrom);
                double toSec = BaseTimeSeconds(map, IdFrameTo);
                int a = (int)Math.Round(fromSec * fps, MidpointRounding.AwayFromZero);
                int b = (int)Math.Round(toSec * fps, MidpointRounding.AwayFromZero);
                if (b < a) { int t = a; a = b; b = t; }

                info.StartFrame = a;
                info.EndFrame = b;
                info.FrameRangeKnown = true;
                if (step > 1)
                {
                    // 帧号集合 a, a+step, ... 不连续；GUI 只能表达连续区间 -> 取第一段（单帧）
                    info.EndFrame = a;
                    info.Truncated = true;
                    info.NoteKey = "log.sceneNoteManualStep";
                    info.NoteArgs = new object[] { step, a };
                    info.Note = string.Format("手动步进={0}，帧号不连续，仅采用首帧 {1}", step, a);
                }
                return;
            }

            if (seq == SeqCustom && !string.IsNullOrEmpty(rangeStr))
            {
                var frames = ExpandRangeString(rangeStr);
                if (frames.Count > 0)
                {
                    int a = frames[0], b = frames[0];
                    for (int i = 1; i < frames.Count; i++)
                    {
                        if (frames[i] == b + 1) b = frames[i];
                        else break;                       // 遇到跳号即停：只取第一段连续帧
                    }
                    info.StartFrame = a;
                    info.EndFrame = b;
                    info.FrameRangeKnown = true;
                    info.Truncated = b < frames[frames.Count - 1];
                    if (info.Truncated)
                    {
                        info.NoteKey = "log.sceneNoteTruncated";
                        info.NoteArgs = new object[] { rangeStr, a, b };
                        info.Note = string.Format("自定义帧串 {0} 不连续，仅采用第一段 {1}-{2}", rangeStr, a, b);
                    }
                    return;
                }
            }

            // CURRENTFRAME：帧号 = 文档「当前帧」时间 * fps（起止同一帧）
            if (seq == SeqCurrentFrame && timeline != null && !double.IsNaN(timeline[0]))
            {
                int f = (int)Math.Round(timeline[0] * fps, MidpointRounding.AwayFromZero);
                info.StartFrame = f;
                info.EndFrame = f;
                info.FrameRangeKnown = true;
                info.FromDocumentTimeline = true;
                info.NoteKey = "log.sceneNoteCurrentFrame";
                info.NoteArgs = new object[] { f };
                info.Note = string.Format("帧范围模式「CURRENTFRAME」按文档当前帧解析：第 {0} 帧", f);
                return;
            }

            // ALLFRAMES / PREVIEWRANGE：帧范围 = 文档时间轴 [min, max]
            if ((seq == SeqAllFrames || seq == SeqPreviewRange) && timeline != null)
            {
                int a = (int)Math.Round(timeline[1] * fps, MidpointRounding.AwayFromZero);
                int b = (int)Math.Round(timeline[2] * fps, MidpointRounding.AwayFromZero);
                if (b >= a && b > 0)
                {
                    info.StartFrame = a;
                    info.EndFrame = b;
                    info.FrameRangeKnown = true;
                    info.FromDocumentTimeline = true;
                    info.NoteKey = "log.sceneNoteTimeline";
                    info.NoteArgs = new object[] { info.FrameSequenceName, a, b };
                    info.Note = string.Format("帧范围模式「{0}」按文档时间轴解析：{1}-{2} 帧",
                                              info.FrameSequenceName, a, b);
                    return;
                }
            }

            // 其余模式（CURRENTFRAME / 时间轴读不到）：C4D 动态解析，离线无法确定具体帧号
            info.FrameRangeKnown = false;
            info.NoteKey = "log.sceneNoteDynamic";
            info.NoteArgs = new object[] { info.FrameSequenceName };
            info.Note = string.Format("帧范围模式「{0}」由 C4D 动态解析，文件内不含具体帧号，保留手填范围",
                                      info.FrameSequenceName);
        }

        /// <summary>BaseTime（numerator/denominator 秒）-> 秒数。</summary>
        private static double BaseTimeSeconds(Dictionary<int, object> map, int id)
        {
            object v;
            if (!map.TryGetValue(id, out v)) return 0.0;
            var bt = v as BaseTimeValue;
            if (bt == null) return 0.0;
            return bt.Denominator == 0 ? 0.0 : bt.Numerator / bt.Denominator;
        }

        /// <summary>
        /// 展开 CUSTOM 帧串，如 "1-2,90" -> {1,2,90}，结果去重升序。
        /// 带总量上限，避免异常帧串（如 1-999999999）撑爆内存。
        /// </summary>
        private static List<int> ExpandRangeString(string s)
        {
            var set = new SortedSet<int>();
            const int maxFrames = 200000;
            foreach (string part in (s ?? string.Empty).Split(','))
            {
                string p = part.Trim();
                if (p.Length == 0) continue;
                int dash = p.IndexOf('-');
                if (dash > 0)
                {
                    int lo, hi;
                    if (!int.TryParse(p.Substring(0, dash), out lo)) continue;
                    if (!int.TryParse(p.Substring(dash + 1), out hi)) continue;
                    if (hi < lo) { int t = lo; lo = hi; hi = t; }
                    long count = (long)hi - lo + 1;
                    if (count > maxFrames) hi = lo + maxFrames - 1;
                    for (int f = lo; f <= hi; f++)
                    {
                        set.Add(f);
                        if (set.Count >= maxFrames) break;
                    }
                }
                else
                {
                    int f;
                    if (int.TryParse(p, out f)) set.Add(f);
                }
                if (set.Count >= maxFrames) break;
            }
            return new List<int>(set);
        }

        private static string SequenceName(int seq)
        {
            switch (seq)
            {
                case SeqManual: return "MANUAL";
                case SeqCurrentFrame: return "CURRENTFRAME";
                case SeqAllFrames: return "ALLFRAMES";
                case SeqPreviewRange: return "PREVIEWRANGE";
                case SeqCustom: return "CUSTOM";
                default: return "UNKNOWN(" + seq + ")";
            }
        }

        // ---------------------------------------------------------------
        // 输出模板
        // ---------------------------------------------------------------

        /// <summary>输出格式插件 ID -> 扩展名（含点）。</summary>
        private static string FormatToExtension(int formatId)
        {
            switch (formatId)
            {
                case 1100: return ".tif";    // FILTER_TIF
                case 1110: return ".tif";    // FILTER_TIF_B3D
                case 1101: return ".tga";
                case 1102: return ".bmp";
                case 1103: return ".iff";
                case 1104: return ".jpg";    // FILTER_JPG
                case 1106: return ".psd";
                case 1111: return ".psb";
                case 1023671: return ".png"; // FILTER_PNG
                case 1023737: return ".dpx";
                case 1016606: return ".exr"; // FILTER_EXR
                case 1001379: return ".hdr";
                case 1122: return ".avi";    // 视频：非逐帧序列，调用方日志会提示
                case 1125: return ".mov";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 把 C4D 的 RDATA_PATH 转成本工具的输出模板：路径 + 帧号占位（****）+ 扩展名。
        /// C4D 默认补零 4 位；FrameScanner 用 (\d+) 匹配帧号，位数不符也能识别，故不会漏判。
        /// 若路径里已经含星号占位符，则原样保留（尊重用户在工程里自定义的占位）。
        /// </summary>
        private static string BuildOutputTemplate(string rawPath, int nameFormat, string ext)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return string.Empty;
            string p = rawPath.Trim();
            if (p.IndexOf('*') >= 0) return p;   // 用户已写好占位符，原样尊重

            // 工程里若已写了扩展名，先去掉，避免拼成 out.png****.png
            string oldExt = null;
            try { oldExt = Path.GetExtension(p); } catch { /* 非法路径字符时忽略 */ }
            if (!string.IsNullOrEmpty(oldExt) && IsKnownImageExtension(oldExt))
                p = p.Substring(0, p.Length - oldExt.Length);

            // RDATA_NAMEFORMAT 决定帧号位数：
            //   0 Name0000.TIF / 1 Name0000 / 2 Name.0000 / 6 Name.0000.TIF -> 4 位
            //   3 Name000.TIF  / 4 Name000  / 5 Name.000                  -> 3 位
            int digits = (nameFormat == 3 || nameFormat == 4 || nameFormat == 5) ? 3 : 4;
            string token = new string('*', digits);

            // 分隔符规则：
            //   * 路径末位是数字时，C4D 会在基础名与帧号之间补下划线，避免
            //     "testImage2" + "0001" 混淆成 "testImage20001"；
            //   * NAMEFORMAT 2/5/6 对应 Name.0000 形式，帧号前带点；
            //   * 两者同时出现时以下划线为准，避免生成 "name_.0001" 这种双分隔符。
            char last = p.Length > 0 ? p[p.Length - 1] : '\0';
            string sep;
            if (char.IsDigit(last)) sep = "_";
            else if (nameFormat == 2 || nameFormat == 5 || nameFormat == 6) sep = ".";
            else sep = string.Empty;

            return p + sep + token + ext;
        }

        private static bool IsKnownImageExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            string e = ext.ToLowerInvariant();
            switch (e)
            {
                case ".tif": case ".tiff": case ".tga": case ".bmp": case ".iff":
                case ".jpg": case ".jpeg": case ".psd": case ".psb": case ".png":
                case ".dpx": case ".exr": case ".hdr": case ".avi": case ".mov":
                    return true;
                default: return false;
            }
        }

        // ---------------------------------------------------------------
        // 二进制解析
        // ---------------------------------------------------------------

        private sealed class BaseTimeValue
        {
            public double Numerator;
            public double Denominator;
        }

        private struct Entry
        {
            public int Id;
            public int Type;
            public object Value;
        }

        private sealed class Block
        {
            public long Offset;
            public List<Entry> Entries;
            public bool IsActive;
        }

        // ------------------------------------------------------------------
        // 底层随机访问：用内存映射读取，偏移一律 long，规避 ~2GB 的 int 上限
        // ------------------------------------------------------------------

        /// <summary>
        /// 内存映射的只读文件视图。通过 long 偏移随机读单字节/定长数值，
        /// 不把文件加载进托管数组；7GB 工程也只按需分页，内存占用很小。
        /// </summary>
        private sealed class MappedFile : IDisposable
        {
            private readonly MemoryMappedFile _mmf;
            private readonly MemoryMappedViewAccessor _acc;
            public long Length { get; private set; }
            /// <summary>扫描起点：优先从文件尾部窗口开始（C4D 渲染设置总在文档末尾）。</summary>
            public long ScanStart { get; set; }

            public MappedFile(string path)
            {
                Length = new FileInfo(path).Length;
                ScanStart = Math.Max(0, Length - 32L * 1024 * 1024);   // 尾部 32MB 优先
                _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
                _acc = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            }

            public byte Byte(long o) { return _acc.ReadByte(o); }

            public uint U32BE(long o)
            {
                return ((uint)_acc.ReadByte(o) << 24) | ((uint)_acc.ReadByte(o + 1) << 16) |
                       ((uint)_acc.ReadByte(o + 2) << 8) | _acc.ReadByte(o + 3);
            }

            public double F64BE(long o)
            {
                ulong v = 0;
                for (int i = 0; i < 8; i++) v = (v << 8) | _acc.ReadByte(o + i);
                return BitConverter.Int64BitsToDouble(unchecked((long)v));
            }

            public void ReadBlock(long o, byte[] buf) { _acc.ReadArray(o, buf, 0, buf.Length); }

            public string ReadAscii(long o, int count)
            {
                var buf = new byte[count];
                _acc.ReadArray(o, buf, 0, count);
                return Encoding.ASCII.GetString(buf);
            }

            public string ReadUtf16BE(long o, int byteCount)
            {
                var buf = new byte[byteCount];
                _acc.ReadArray(o, buf, 0, byteCount);
                return Encoding.BigEndianUnicode.GetString(buf);
            }

            /// <summary>从 from 起找 pattern 首次出现位置（long 偏移）。</summary>
            public long IndexOf(byte[] pattern, long from)
            {
                long n = Length;
                int m = pattern.Length;
                if (from < 0) from = 0;
                if (m == 0 || from > n - m) return -1;
                // 取信息量最大字节（非 00/FF）做锚，减少候选命中
                int k = 0;
                while (k < m && (pattern[k] == 0x00 || pattern[k] == 0xFF)) k++;
                if (k == m) k = m - 1;
                byte anchor = pattern[k];
                var buf = new byte[256 * 1024];
                long p = from;
                while (p <= n - m)
                {
                    int want = (int)Math.Min((long)buf.Length, n - p);
                    _acc.ReadArray(p, buf, 0, want);
                    for (int j = 0; j <= want - m; j++)
                    {
                        if (buf[j + k] != anchor) continue;
                        bool ok = true;
                        for (int t = 0; t < m; t++)
                            if (buf[j + t] != pattern[t]) { ok = false; break; }
                        if (ok) return p + j;
                    }
                    p += want - m + 1;   // 保留重叠区，避免跨块边界漏匹配
                    if (want < buf.Length) break;
                }
                return -1;
            }

            public void Dispose()
            {
                if (_acc != null) _acc.Dispose();
                if (_mmf != null) _mmf.Dispose();
            }
        }

        private static uint ReadU32(MappedFile d, long o) { return d.U32BE(o); }

        private static double ReadF64BE(MappedFile d, long o) { return d.F64BE(o); }

        /// <summary>定长类型的 value 字节数；返回 -1 表示不支持。</summary>
        private static int FixedSize(int type)
        {
            switch (type)
            {
                case 11: case 12: return 1;   // CHAR / UCHAR
                case 13: case 14: return 2;   // INT16 / UINT16
                case 15: case 16: case 21: return 4; // INT32 / UINT32 / BOOL
                case 17: case 18: case 19: case 20: return 8; // INT64 / UINT64 / FLOAT / FLOAT64
                case 22: return 16;          // TIME
                case 23: return 24;          // VECTOR
                case 25: return 96;          // MATRIX
                default: return -1;
            }
        }

        /// <summary>尝试读取一个 BaseContainer 条目，返回其结束偏移；失败返回 false。</summary>
        private static bool TryReadEntry(MappedFile d, long o, out Entry entry, out long next)
        {
            entry = default(Entry);
            next = o;
            if (o < 0 || o + 11 > d.Length) return false;
            if (d.Byte(o) != EntryMarker || d.Byte(o + 5) != EntryMarker) return false;
            uint t32 = ReadU32(d, o + 6);
            int t8 = d.Byte(o + 10);
            if (t32 != (uint)t8) return false;
            uint rawId = ReadU32(d, o + 1);
            if (rawId > int.MaxValue) return false;       // ID 超出 int 范围，跳过
            int id = (int)rawId;
            long vs = o + 11;
            object value = null;
            int size;

            if (t8 == TypeString || t8 == TypeFilename)
            {
                uint len = ReadU32(d, vs);
                if (len > 1000000 || vs + 4 + len > d.Length) return false;
                int n = (int)len;
                if (n % 2 != 0) n--;                     // UTF-16BE 必须是偶数长度
                value = n > 0 ? d.ReadUtf16BE(vs + 4, n) : string.Empty;
                size = 4 + (int)len;
            }
            else
            {
                size = FixedSize(t8);
                if (size < 0 || vs + size > d.Length) return false;
                switch (t8)
                {
                    case 15: case 16: case 21:
                        value = (int)ReadU32(d, vs);
                        break;
                    case 19: case 20:
                        value = ReadF64BE(d, vs);
                        break;
                    case 22:
                        value = new BaseTimeValue
                        {
                            Numerator = ReadF64BE(d, vs),
                            Denominator = ReadF64BE(d, vs + 8)
                        };
                        break;
                    default:
                        value = null;                     // 其余定长类型本工具用不到，占位即可
                        break;
                }
            }

            entry = new Entry { Id = id, Type = t8, Value = value };
            next = vs + size;
            return true;
        }

        /// <summary>从起点顺序解析连续条目，直到遇到不合法条目。</summary>
        private static List<Entry> ReadChain(MappedFile d, long start)
        {
            var list = new List<Entry>();
            long pos = start;
            while (pos + 11 <= d.Length)
            {
                Entry e; long next;
                if (!TryReadEntry(d, pos, out e, out next)) break;
                list.Add(e);
                pos = next;
            }
            return list;
        }

        /// <summary>以 RDATA_XRES(5008) 的大端字节为锚点定位全部渲染设置块。</summary>
        private static List<Block> FindRenderBlocks(MappedFile d, long scanFrom)
        {
            var blocks = new List<Block>();
            var seen = new HashSet<long>();
            byte[] anchor = { 0x00, 0x00, 0x13, 0x90 }; // 5008 BE
            long idx = scanFrom;
            while (true)
            {
                long p = d.IndexOf(anchor, idx);
                if (p < 0) break;
                idx = p + 1;

                Block best = null;
                long from = Math.Max(0, p - 128);
                for (long o = from; o <= p; o++)
                {
                    if (seen.Contains(o)) continue;
                    // 条目头还须满足第 6 字节也是 0x0F（布局 0F|id4|0F），先过滤掉绝大多数位置，
                    // 避免对随机字节调用 ReadChain（大文件里这里是主要耗时点）。
                    if (o + 11 > d.Length || d.Byte(o) != EntryMarker || d.Byte(o + 5) != EntryMarker) continue;
                    var chain = ReadChain(d, o);
                    if (chain.Count < 30) continue;
                    bool hasX = false, hasPath = false;
                    foreach (var e in chain)
                    {
                        if (e.Id == IdXRes) hasX = true;
                        else if (e.Id == IdPath) hasPath = true;
                    }
                    if (!hasX || !hasPath) continue;
                    if (best == null || chain.Count > best.Entries.Count)
                    {
                        // 活动标志：块起始前约 24 字节处有 0F 10 00 00 XX（第二字节 0x10 = active）。
                        // 该标志与 C4D 官方 GetActiveRenderData() 的活动设置一致。
                        bool active = false;
                        long chkFrom = Math.Max(0, o - 48);
                        for (long q = chkFrom; q + 4 <= o; q++)
                        {
                            if (d.Byte(q) == 0x0F && d.Byte(q + 1) == 0x10 &&
                                d.Byte(q + 2) == 0x00 && d.Byte(q + 3) == 0x00)
                            { active = true; break; }
                        }
                        best = new Block { Offset = o, Entries = chain, IsActive = active };
                    }
                }
                if (best != null)
                {
                    seen.Add(best.Offset);
                    blocks.Add(best);
                }
            }
            blocks.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return blocks;
        }

        /// <summary>帧范围字符串是否是合法格式（数字、逗号、连字符、空格、分号）。</summary>
        private static bool IsFrameRangeText(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 200) return false;
            bool hasDigit = false;
            foreach (char c in s)
            {
                if (c >= '0' && c <= '9') { hasDigit = true; continue; }
                if (c == ',' || c == '-' || c == ' ' || c == ';' || c == ':') continue;
                return false;
            }
            return hasDigit;
        }

        /// <summary>
        /// 扫描帧范围字符串，支持两种存储格式：
        ///   1) 标准条目头 0F|id=5015|0F|type=130/131|len|UTF-16BE
        ///   2) 裸字符串 [u32 len][UTF-16BE]（无条目头）
        /// 返回 (偏移, 文本)。
        /// </summary>
        private static List<KeyValuePair<long, string>> FindFrameRangeStrings(MappedFile d, long scanFrom)
        {
            var list = new List<KeyValuePair<long, string>>();

            // 格式1：标准条目头
            byte[] pat = { EntryMarker, 0x00, 0x00, 0x13, 0x97, EntryMarker }; // 5015 BE
            long idx = scanFrom;
            while (true)
            {
                long p = d.IndexOf(pat, idx);
                if (p < 0) break;
                idx = p + 1;
                Entry e; long next;
                if (!TryReadEntry(d, p, out e, out next)) continue;
                if (e.Type != TypeString && e.Type != TypeFilename) continue;
                var s = e.Value as string;
                if (!string.IsNullOrEmpty(s) && IsFrameRangeText(s))
                    list.Add(new KeyValuePair<long, string>(p, s));
            }

            // 格式2：裸字符串 [u32 len][UTF-16BE]，len 为偶数且 2..100
            long n = d.Length;
            for (long p = scanFrom; p + 4 <= n; p++)
            {
                uint len = d.U32BE(p);
                if (len < 2 || len > 100 || (len & 1) != 0) continue;
                long end = p + 4 + len;
                if (end > n) continue;
                var s = d.ReadUtf16BE(p + 4, (int)len);
                if (IsFrameRangeText(s))
                    list.Add(new KeyValuePair<long, string>(p, s));
            }
            return list;
        }

        /// <summary>
        /// 从文件推断文档帧率（fps）。
        /// C4D 的文档 fps 不通过可靠 ID 存储（DOCUMENT_FPS=1001 被插件海量复用），
        /// 但时间轴 BaseTime 的分子/分母 = 帧号/帧率（可能约分）。
        /// 例：cur=91/30 -> 分母30=fps；cur=16/3 -> 160/30 约分，分母3 是 fps 的因子。
        /// 算法：取所有 BaseTime 分母的 LCM，在常见帧率中选最接近 30（C4D 默认）的。
        /// 实测多种工程均能正确推断出常见帧率。
        /// </summary>
        private static double DetectDocumentFps(MappedFile d, long scanFrom)
        {
            var dens = new List<double>();
            long n = d.Length;
            const int Elem = 17;
            long limit = n - Elem * 4;
            for (long baseOff = scanFrom; baseOff <= limit; baseOff += 1024 * 1024)
            {
                long want = Math.Min(1024 * 1024 + Elem * 5, n - baseOff);
                var buf = new byte[(int)want];
                d.ReadBlock(baseOff, buf);
                long hi = want - Elem * 4;
                for (int j = 0; j <= hi; j++)
                {
                    if (buf[j] != 0x16) continue;
                    double num, den;
                    if (!TryBareTimeParts(buf, j, out num, out den)) continue;
                    if (den >= 1 && den <= 240) dens.Add(den);
                }
            }
            if (dens.Count == 0) return 30.0;

            // 分母的最小公倍数
            double lcm = 1;
            foreach (double x in dens)
            {
                double g = Gcd(lcm, x);
                lcm = (lcm / g) * x;
                if (lcm > 240) { lcm = 240; break; }
            }
            // 常见帧率中选 LCM 的倍数且最接近 30 的
            double[] common = { 24, 25, 30, 50, 60 };
            double best = 30.0, bestDiff = double.MaxValue;
            foreach (double c in common)
            {
                if (Math.Abs(c % lcm) > 1e-9 && Math.Abs(lcm % c) > 1e-9) continue;
                double diff = Math.Abs(c - 30.0);
                if (diff < bestDiff) { bestDiff = diff; best = c; }
            }
            return best;
        }

        private static double Gcd(double a, double b)
        {
            while (b > 0.5) { double t = a % b; a = b; b = t; }
            return a < 0.5 ? 1 : a;
        }

        private static bool TryBareTimeParts(byte[] b, int o, out double num, out double den)
        {
            num = 0; den = 0;
            if (o + 17 > b.Length || b[o] != 0x16) return false;
            ulong nu = 0, du = 0;
            for (int i = 0; i < 8; i++) { nu = (nu << 8) | b[o + 1 + i]; du = (du << 8) | b[o + 9 + i]; }
            num = BitConverter.Int64BitsToDouble(unchecked((long)nu));
            den = BitConverter.Int64BitsToDouble(unchecked((long)du));
            return den >= 1 && den <= 240 && num >= 0 && num <= 100000;
        }

        /// <summary>
        /// 提取文档时间轴 [currentSec, minSec, maxSec]。
        /// Cinema 4D 把文档播放范围存成裸 BaseTime 数组，不走标准条目头：
        ///   元素 17 字节 = 0x16 | numerator:double(be) | denominator:double(be)
        /// 形态 [cur, min, max, min, max]（一对重复两遍）。找不到返回 null。
        /// </summary>
        private static double[] FindDocumentTimeline(MappedFile d, long scanFrom)
        {
            long n = d.Length;
            const int Elem = 17;
            double[] fallback4 = null;   // 4 元素形态：[NaN, min, max]

            // 单趟扫描同时尝试 5 元素与 4 元素形态，避免两次全文件遍历。
            long limit = n - Elem * 4;
            // 分块批量读取，避免逐字节调用 accessor（2GB 文件里那是主要瓶颈）。
            // 块之间保留 Elem*4 重叠，防止跨边界的 BaseTime 数组被切断。
            const int Chunk = 1 * 1024 * 1024;
            long overlap = Elem * 5;
            for (long baseOff = scanFrom; baseOff <= limit; baseOff += Chunk)
            {
                long want = Math.Min((long)Chunk + overlap, n - baseOff);
                var buf = new byte[(int)want];
                d.ReadBlock(baseOff, buf);
                long hi = want - Elem * 4;
                for (int j = 0; j <= hi; j++)
                {
                    if (buf[j] != 0x16) continue;
                    double v0, v1, v2, v3;
                    if (!TryBareTimeBuf(buf, j, out v0)) continue;
                    if (!TryBareTimeBuf(buf, j + Elem, out v1)) continue;
                    if (!TryBareTimeBuf(buf, j + 2 * Elem, out v2)) continue;
                    if (!TryBareTimeBuf(buf, j + 3 * Elem, out v3)) continue;

                    // 5 元素形态 [cur, min, max, min, max]
                    if (j + Elem * 5 <= buf.Length && Math.Abs(v1 - v3) < 1e-6)
                    {
                        double v4;
                        if (TryBareTimeBuf(buf, j + 4 * Elem, out v4) &&
                            Math.Abs(v2 - v4) < 1e-6 && v2 > v1)
                            return new double[] { v0, v1, v2 };   // [current, min, max]
                    }

                    // 4 元素形态 [min, max, min, max]
                    if (fallback4 == null &&
                        Math.Abs(v0 - v2) < 1e-6 && Math.Abs(v1 - v3) < 1e-6 && v1 > v0)
                        fallback4 = new double[] { double.NaN, v0, v1 };
                }
            }
            return fallback4;
        }

        /// <summary>从内存块读裸 BaseTime 元素（与 TryBareTime 同规则）。</summary>
        private static bool TryBareTimeBuf(byte[] b, int o, out double sec)
        {
            sec = 0;
            if (o + 17 > b.Length || b[o] != 0x16) return false;
            // 大端 double：逐字节组装 ulong 再转 double
            ulong nu = 0, du = 0;
            for (int i = 0; i < 8; i++) { nu = (nu << 8) | b[o + 1 + i]; du = (du << 8) | b[o + 9 + i]; }
            double num = BitConverter.Int64BitsToDouble(unchecked((long)nu));
            double den = BitConverter.Int64BitsToDouble(unchecked((long)du));
            if (den == 0 || double.IsNaN(num) || double.IsNaN(den) ||
                double.IsInfinity(num) || double.IsInfinity(den)) return false;
            if (Math.Abs(num) > 1e9 || Math.Abs(den) > 1e9) return false;
            double v = num / den;
            if (v < 0 || v > 10000) return false;
            sec = v;
            return true;
        }

        /// <summary>读一个裸 BaseTime 元素（0x16 + 两个 double 秒数），值须在 [0,10000] 秒。</summary>
        private static bool TryBareTime(MappedFile d, long o, out double sec)
        {
            sec = 0;
            if (o + 17 > d.Length || d.Byte(o) != 0x16) return false;
            double num = ReadF64BE(d, o + 1);
            double den = ReadF64BE(d, o + 9);
            if (den == 0 || double.IsNaN(num) || double.IsNaN(den) ||
                double.IsInfinity(num) || double.IsInfinity(den)) return false;
            if (Math.Abs(num) > 1e9 || Math.Abs(den) > 1e9) return false;
            double v = num / den;
            if (v < 0 || v > 10000) return false;
            sec = v;
            return true;
        }

        private static int GetInt(Dictionary<int, object> map, int id, int fallback)
        {
            object v;
            if (!map.TryGetValue(id, out v)) return fallback;
            if (v is int) return (int)v;
            if (v is double) return (int)(double)v;
            return fallback;
        }

        private static double GetDouble(Dictionary<int, object> map, int id, double fallback)
        {
            object v;
            if (!map.TryGetValue(id, out v)) return fallback;
            if (v is double) return (double)v;
            if (v is int) return (int)v;
            return fallback;
        }

        private static string GetString(Dictionary<int, object> map, int id, string fallback)
        {
            object v;
            if (!map.TryGetValue(id, out v)) return fallback;
            var s = v as string;
            return s ?? fallback;
        }
    }
}
