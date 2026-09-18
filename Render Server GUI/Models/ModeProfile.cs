using System;

namespace RenderServerGui.Models
{
    /// <summary>
    /// 单个模式的完整参数集。三种模式共用同一数据结构，各自只使用相关字段：
    /// Team Render 使用看门狗相关字段；Cinema 4D / Commandline 使用逐帧相关字段。
    /// </summary>
    [Serializable]
    public class ModeProfile
    {
        // ---------- 路径 / 进程（三模式共用，切换模式即切换这一组路径）----------

        /// <summary>主程序完整路径。</summary>
        public string ExePath { get; set; }

        /// <summary>进程名称（不含扩展名），用于 FindTargetProcess。</summary>
        public string ProcessName { get; set; }

        /// <summary>异常记录文件 _BugReport.txt 路径。</summary>
        public string ReportPath { get; set; }

        // ---------- Team Render 看门狗专用 ----------

        /// <summary>缓存目录（启动前清空）。</summary>
        public string CachePath { get; set; }

        /// <summary>Team Render 端口。</summary>
        public int Port { get; set; }

        /// <summary>检查间隔（秒）。</summary>
        public int CheckIntervalSeconds { get; set; }

        /// <summary>最大挂起次数，达到后杀进程。</summary>
        public int MaxHangCount { get; set; }

        /// <summary>连续工作多少分钟后休息降温。</summary>
        public int WorkingMinutes { get; set; }

        /// <summary>休息时长（分钟）。</summary>
        public int RestMinutes { get; set; }

        /// <summary>启动前是否清空缓存目录。</summary>
        public bool ClearCache { get; set; }

        // ---------- 逐帧模式专用 ----------

        /// <summary>
        /// 工程文件完整路径（*.c4d）。逐帧命令行形如： exe -render "工程.c4d" -frame 帧号。
        /// </summary>
        public string SceneFile { get; set; }

        /// <summary>
        /// 输出文件命名模板，帧号占位符写作一段连续的星号（如 ****），星号个数即补零位数。
        /// 用星号是因为文件名不允许含 *，不会与真实产物冲突。
        /// 例：D:\Output\Image_****.png，第 0 帧展开为 D:\Output\Image_0000.png。
        /// </summary>
        public string OutputTemplate { get; set; }

        /// <summary>起始帧。</summary>
        public int StartFrame { get; set; }

        /// <summary>结束帧。</summary>
        public int EndFrame { get; set; }

        /// <summary>帧间冷却（秒），避免过热。</summary>
        public int CooldownSeconds { get; set; }

        /// <summary>单帧最长渲染时间（秒），超过即判挂起并重启本帧。</summary>
        public int FrameTimeoutSeconds { get; set; }

        /// <summary>单帧模式轮询检查间隔（秒）。</summary>
        public int FrameCheckIntervalSeconds { get; set; }

        /// <summary>内层监控：连续异常计数的上限，达到即判本次尝试失败（沿用 Team Render 去抖思路）。</summary>
        public int MaxAbnormalCount { get; set; }

        /// <summary>外层：同一帧最多重启尝试的次数，达到即按策略跳过/停止。</summary>
        public int MaxFrameFailCount { get; set; }

        /// <summary>达到帧最大失败次数后对失败帧的处理策略。</summary>
        public OnFailBehaviour OnFail { get; set; }

        /// <summary>
        /// 按模式返回一套默认可用参数（对应三组已知路径）。
        /// </summary>
        public static ModeProfile ForPreset(RenderMode mode)
        {
            const string exeDir = @"C:\Program Files\Maxon Cinema 4D 2026";
            const string roaming = @"C:\Users\12407024\AppData\Roaming\Maxon";

            // 逐帧模式的公共默认值
            var profile = new ModeProfile
            {
                CachePath = string.Empty,
                Port = 5401,
                CheckIntervalSeconds = 60,
                MaxHangCount = 4,
                WorkingMinutes = 60,
                RestMinutes = 3,
                ClearCache = true,

                OutputTemplate = @"D:\Output\Image_****.png",
                StartFrame = 0,
                EndFrame = 100,
                CooldownSeconds = 60,
                FrameTimeoutSeconds = 600,
                FrameCheckIntervalSeconds = 30,
                MaxAbnormalCount = 5,
                MaxFrameFailCount = 4,
                OnFail = OnFailBehaviour.Skip
            };

            switch (mode)
            {
                case RenderMode.TeamRender:
                    profile.ExePath = exeDir + @"\Cinema 4D Team Render Client.exe";
                    profile.ProcessName = "Cinema 4D Team Render Client";
                    profile.ReportPath = roaming + @"\Maxon Cinema 4D 2026_1ABCDC12_c\_bugreports\_BugReport.txt";
                    profile.CachePath = roaming + @"\Maxon Cinema 4D 2026_1ABCDC12_c\teamrender_client\users\client";
                    break;

                case RenderMode.Cinema4D:
                    profile.ExePath = exeDir + @"\Cinema 4D.exe";
                    profile.ProcessName = "Cinema 4D";
                    profile.ReportPath = roaming + @"\Maxon Cinema 4D 2026_1ABCDC12\_bugreports\_BugReport.txt";
                    profile.SceneFile = @"D:\Proj\scene.c4d";
                    break;

                case RenderMode.Commandline:
                    profile.ExePath = exeDir + @"\Commandline.exe";
                    profile.ProcessName = "Commandline";
                    profile.ReportPath = roaming + @"\Maxon Cinema 4D 2026_1ABCDC12_X\_bugreports\_BugReport.txt";
                    profile.SceneFile = @"D:\Proj\scene.c4d";
                    break;
            }

            return profile;
        }
    }
}
