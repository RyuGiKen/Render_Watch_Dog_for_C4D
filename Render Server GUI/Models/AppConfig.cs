using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace RenderServerGui.Models
{
    /// <summary>
    /// 应用整体配置：三种模式各自的参数 + 当前选中的模式。
    /// 通过零依赖的 XmlSerializer 持久化到 exe 同目录的 settings.xml。
    /// </summary>
    [Serializable]
    public class AppConfig
    {
        /// <summary>当前选中的模式（启动时恢复）。</summary>
        public RenderMode Mode { get; set; }

        /// <summary>界面语言码（如 zh-CN / zh-HK / en-US），默认简体中文。</summary>
        public string Language { get; set; } = "zh-CN";

        /// <summary>
        /// 是否"禁用渲染时阻止系统睡眠"。反向存储：旧版 settings.xml 无此节点时反序列化为 false，
        /// 即默认启用防睡眠（复选框默认勾选）。
        /// </summary>
        public bool DisableKeepAwake { get; set; }

        /// <summary>Team Render 模式参数。</summary>
        public ModeProfile TeamRender { get; set; }

        /// <summary>Cinema 4D 模式参数。</summary>
        public ModeProfile Cinema4D { get; set; }

        /// <summary>Commandline 模式参数。</summary>
        public ModeProfile Commandline { get; set; }

        /// <summary>
        /// Cinema4D 与 Commandline **共用的任务列表**（自 v3.5）。两模式的调度参数各自独立，
        /// 但任务列表与任务参数是同一份数据。Load() 会把 Cinema4D.Tasks / Commandline.Tasks 接到此实例上。
        /// 旧 settings.xml 无此节点时，由两模式各自的旧 Tasks（或更旧的单任务字段）合并去重迁移而来。
        /// </summary>
        public List<RenderTask> SharedTasks { get; set; }

        /// <summary>settings.xml 的完整路径（exe 同目录）。</summary>
        public static string SettingsFilePath
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(dir, "settings.xml");
            }
        }

        /// <summary>返回一个用三套预设初始化好的默认配置（含一份共享任务列表）。</summary>
        public static AppConfig CreateDefault()
        {
            var cfg = new AppConfig
            {
                Mode = RenderMode.TeamRender,
                TeamRender = ModeProfile.ForPreset(RenderMode.TeamRender),
                Cinema4D = ModeProfile.ForPreset(RenderMode.Cinema4D),
                Commandline = ModeProfile.ForPreset(RenderMode.Commandline)
            };
            cfg.SharedTasks = new List<RenderTask> { RenderTask.CreateDefault() };
            cfg.Cinema4D.Tasks = cfg.SharedTasks;
            cfg.Commandline.Tasks = cfg.SharedTasks;
            return cfg;
        }

        /// <summary>取某模式对应的参数对象。</summary>
        public ModeProfile ProfileOf(RenderMode mode)
        {
            switch (mode)
            {
                case RenderMode.TeamRender: return TeamRender;
                case RenderMode.Cinema4D: return Cinema4D;
                case RenderMode.Commandline: return Commandline;
                default: return TeamRender;
            }
        }

        /// <summary>把某模式的参数写回配置。</summary>
        public void SetProfile(RenderMode mode, ModeProfile profile)
        {
            switch (mode)
            {
                case RenderMode.TeamRender: TeamRender = profile; break;
                case RenderMode.Cinema4D: Cinema4D = profile; break;
                case RenderMode.Commandline: Commandline = profile; break;
            }
        }

        /// <summary>
        /// 从磁盘加载；不存在或出错时返回默认配置。
        /// </summary>
        public static AppConfig Load()
        {
            try
            {
                string path = SettingsFilePath;
                if (!File.Exists(path))
                    return CreateDefault();

                var serializer = new XmlSerializer(typeof(AppConfig));
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var cfg = (AppConfig)serializer.Deserialize(fs);
                    // 防御旧文件缺字段
                    cfg.TeamRender = cfg.TeamRender ?? ModeProfile.ForPreset(RenderMode.TeamRender);
                    cfg.Cinema4D = cfg.Cinema4D ?? ModeProfile.ForPreset(RenderMode.Cinema4D);
                    cfg.Commandline = cfg.Commandline ?? ModeProfile.ForPreset(RenderMode.Commandline);
                    if (string.IsNullOrWhiteSpace(cfg.Language)) cfg.Language = "zh-CN";
                    cfg.MigrateSharedTasks();
                    return cfg;
                }
            }
            catch
            {
                return CreateDefault();
            }
        }

        /// <summary>
        /// 把任务列表统一为一份共享的 SharedTasks，并把 Cinema4D.Tasks / Commandline.Tasks 接到同一实例。
        /// 兼容三代数据：① 新文件已有 SharedTasks；② 上一版分模式 Cinema4D.Tasks/Commandline.Tasks（合并去重）；
        /// ③ 更旧的单任务字段（各模式 MigrateToTasks 先转成一条）。合并时按任务全字段去重，主键重排为唯一递增。
        /// </summary>
        public void MigrateSharedTasks()
        {
            if (SharedTasks == null || SharedTasks.Count == 0)
            {
                Cinema4D.MigrateToTasks();     // 无 Tasks 时从旧单任务字段兜底生成
                Commandline.MigrateToTasks();
                SharedTasks = MergeDedupe(Cinema4D.Tasks, Commandline.Tasks);
            }
            NormalizeIds(SharedTasks);
            Cinema4D.Tasks = SharedTasks;      // 两模式共用同一列表实例
            Commandline.Tasks = SharedTasks;
        }

        /// <summary>合并两个任务列表，按全字段（工程/模板/起止帧/分块/超时）去重，保留首次出现顺序。</summary>
        private static List<RenderTask> MergeDedupe(List<RenderTask> a, List<RenderTask> b)
        {
            var result = new List<RenderTask>();
            var seen = new HashSet<string>();
            foreach (var src in new[] { a, b })
            {
                if (src == null) continue;
                foreach (var t in src)
                {
                    if (t == null) continue;
                    string key = string.Join("\u0001",
                        t.SceneFile ?? "", t.OutputTemplate ?? "",
                        t.StartFrame.ToString(), t.EndFrame.ToString(),
                        t.MaxChunkLength.ToString(), t.FrameTimeoutSeconds.ToString());
                    if (seen.Add(key)) result.Add(t);
                }
            }
            if (result.Count == 0) result.Add(RenderTask.CreateDefault());
            return result;
        }

        /// <summary>确保主键唯一且从 1 递增（迁移合并后可能重复/为 0）。</summary>
        private static void NormalizeIds(List<RenderTask> list)
        {
            var used = new HashSet<int>();
            int next = 1;
            foreach (var t in list)
            {
                if (t.TaskId > 0 && !used.Contains(t.TaskId))
                {
                    used.Add(t.TaskId);
                    if (t.TaskId >= next) next = t.TaskId + 1;
                }
                else
                {
                    while (used.Contains(next)) next++;
                    t.TaskId = next;
                    used.Add(next);
                }
            }
        }

        /// <summary>
        /// 保存到磁盘，返回是否成功（失败时把异常文本通过 out 抛出）。
        /// </summary>
        public bool Save(out string error)
        {
            error = null;
            try
            {
                var serializer = new XmlSerializer(typeof(AppConfig));
                string path = SettingsFilePath;
                string tmp = path + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(fs, this);
                }
                // File.Copy overwrite=true 才能重复保存；File.Move 在目标已存在时会抛异常
                File.Copy(tmp, path, true);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try
                {
                    string tmp = SettingsFilePath + ".tmp";
                    if (File.Exists(tmp)) File.Delete(tmp);
                }
                catch { }
            }
        }
    }
}
