using System;
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
        public RenderMode Mode { get; set; }

        public ModeProfile TeamRender { get; set; }

        public ModeProfile Cinema4D { get; set; }

        public ModeProfile Commandline { get; set; }

        /// <summary>settings.xml 的完整路径（exe 同目录）。</summary>
        public static string SettingsFilePath
        {
            get
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(dir, "settings.xml");
            }
        }

        /// <summary>返回一个用三套预设初始化好的默认配置。</summary>
        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                Mode = RenderMode.TeamRender,
                TeamRender = ModeProfile.ForPreset(RenderMode.TeamRender),
                Cinema4D = ModeProfile.ForPreset(RenderMode.Cinema4D),
                Commandline = ModeProfile.ForPreset(RenderMode.Commandline)
            };
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
                    return cfg;
                }
            }
            catch
            {
                return CreateDefault();
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
                File.Move(tmp, path); // 原子替换，避免半写损坏
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
