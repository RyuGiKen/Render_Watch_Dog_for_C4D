using System;
using System.IO;
using RenderServerGui.Services;

namespace RenderServerGui.Models
{
    /// <summary>
    /// 逐帧模式中的一条渲染任务（工程+输出+帧范围+分块+超时）。
    /// 任务之间按列表顺序串行执行；全局参数（冷却、检查间隔、异常次数等）由 ModeProfile 统一提供。
    /// </summary>
    [Serializable]
    public class RenderTask
    {
        /// <summary>
        /// 存档主键：添加任务时按现有最大值+1 分配，删除不回收（号可不连续）。
        /// 仅作存档标识，不代表任务内容——允许用户不改任务名而改动全部参数。
        /// </summary>
        public int TaskId { get; set; }

        /// <summary>工程文件完整路径（*.c4d）。</summary>
        public string SceneFile { get; set; }

        /// <summary>输出文件命名模板，帧号占位符写作一段连续的星号（如 ****），星号个数即补零位数。</summary>
        public string OutputTemplate { get; set; }

        /// <summary>起始帧。</summary>
        public int StartFrame { get; set; }

        /// <summary>结束帧。</summary>
        public int EndFrame { get; set; }

        /// <summary>最大分块长度：一个进程一次连渲的帧数上限。1=单帧；执行时自动夹到任务范围。</summary>
        public int MaxChunkLength { get; set; }

        /// <summary>无进展超时（秒）：连续这么久没有"新帧落定"即判挂起杀进程重试。</summary>
        public int FrameTimeoutSeconds { get; set; }

        /// <summary>
        /// 列表显示用的简短描述：本地化前缀 + 主键 + 工程文件名 + 帧范围，如「任务001  scene1 [0-100]」。
        /// 「任务」二字取 i18n 键 ui.taskWord，随界面语言切换。
        /// 不序列化（XmlIgnore），每次从字段现算。
        /// </summary>
        [System.Xml.Serialization.XmlIgnore]
        public string DisplayLabel
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(SceneFile)
                    ? Localizer.T("ui.taskNoScene", "(未设置工程)")
                    : Path.GetFileNameWithoutExtension(SceneFile);
                return $"{Localizer.T("ui.taskWord", "任务")}{TaskId:D3}  {name} [{StartFrame}-{EndFrame}]";
            }
        }

        /// <summary>用默认值构造一条空任务（主键=1，由调用方按需改分配）。</summary>
        public static RenderTask CreateDefault()
        {
            return new RenderTask
            {
                TaskId = 1,
                SceneFile = string.Empty,
                OutputTemplate = @"D:\Output\Image_****.png",
                StartFrame = 0,
                EndFrame = 100,
                MaxChunkLength = 1,
                FrameTimeoutSeconds = 600
            };
        }

        /// <summary>浅拷贝渲染参数（不含主键 TaskId，添加时统一分配新号）。</summary>
        public RenderTask Clone()
        {
            return new RenderTask
            {
                SceneFile = this.SceneFile,
                OutputTemplate = this.OutputTemplate,
                StartFrame = this.StartFrame,
                EndFrame = this.EndFrame,
                MaxChunkLength = this.MaxChunkLength,
                FrameTimeoutSeconds = this.FrameTimeoutSeconds
            };
        }
    }
}
