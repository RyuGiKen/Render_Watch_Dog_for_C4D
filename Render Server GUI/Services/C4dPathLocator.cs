using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RenderServerGui.Models;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 自动定位 Cinema 4D 主程序与异常记录文件路径（全部只读，不需管理员权限）。
    /// 主程序：文件关联(.c4d) → 注册表卸载项 → %ProgramFiles% 通配（年份最高）。
    /// 异常记录：主程序 ProductVersion 取年份 → %APPDATA%\Maxon\Maxon Cinema 4D &lt;年&gt;_&lt;hash&gt;&lt;模式后缀&gt;\_bugreports\_BugReport.txt；
    /// 同年份多安装取 _bugreports 最近修改的那个。任一环节找不到一律返回 null（留空让用户手选，绝不猜错路径）。
    /// </summary>
    public static class C4dPathLocator
    {
        /// <summary>各模式的主程序文件名。</summary>
        public static string ExeFileName(RenderMode mode)
        {
            switch (mode)
            {
                case RenderMode.TeamRender: return "Cinema 4D Team Render Client.exe";
                case RenderMode.Commandline: return "Commandline.exe";
                default: return "Cinema 4D.exe";
            }
        }

        /// <summary>各模式异常记录目录的变体后缀：GUI 无、命令行 _x、Team Render 客户端 _c。</summary>
        static string VariantSuffix(RenderMode mode)
        {
            switch (mode)
            {
                case RenderMode.TeamRender: return "_c";
                case RenderMode.Commandline: return "_x";
                default: return "";
            }
        }

        /// <summary>定位安装目录（含 Cinema 4D.exe），找不到返回 null。</summary>
        public static string FindInstallDir()
        {
            string dir = FromFileAssociation();
            if (dir != null) return dir;
            dir = FromUninstallRegistry();
            if (dir != null) return dir;
            return FromProgramFilesGlob();
        }

        static string FromFileAssociation()
        {
            try
            {
                using (var ext = Registry.ClassesRoot.OpenSubKey(".c4d"))
                {
                    string progId = ext?.GetValue(null) as string;
                    if (string.IsNullOrEmpty(progId)) return null;
                    using (var cmd = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
                    {
                        string exe = ParseExeFromCommand(cmd?.GetValue(null) as string);
                        if (exe == null) return null;
                        exe = Environment.ExpandEnvironmentVariables(exe);
                        if (!File.Exists(exe)) return null;
                        string d = Path.GetDirectoryName(exe);
                        return (d != null && Directory.Exists(d)) ? d : null;
                    }
                }
            }
            catch { return null; }
        }

        static string FromUninstallRegistry()
        {
            var hives = new[]
            {
                RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
                RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32),
                RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
            };
            int bestYear = 0; string bestDir = null;
            foreach (var hive in hives)
            {
                try
                {
                    using (var un = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (un == null) continue;
                        foreach (var name in un.GetSubKeyNames())
                        {
                            try
                            {
                                using (var k = un.OpenSubKey(name))
                                {
                                    string dn = k?.GetValue("DisplayName") as string;
                                    if (dn == null || dn.IndexOf("Cinema 4D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                    if (dn.IndexOf("Server", StringComparison.OrdinalIgnoreCase) >= 0) continue; // 排除 Team Render Server
                                    string loc = k?.GetValue("InstallLocation") as string;
                                    if (string.IsNullOrWhiteSpace(loc) || !Directory.Exists(loc)) continue;
                                    if (!File.Exists(Path.Combine(loc, "Cinema 4D.exe"))) continue;
                                    int y = YearFromVersionString(k?.GetValue("DisplayVersion") as string);
                                    if (y > bestYear) { bestYear = y; bestDir = loc; }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
                finally { hive.Dispose(); }
            }
            return bestDir;
        }

        static string FromProgramFilesGlob()
        {
            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (string.IsNullOrEmpty(pf) || !Directory.Exists(pf)) return null;
                int bestYear = 0; string bestDir = null;
                foreach (var dir in Directory.GetDirectories(pf, "Maxon Cinema 4D*"))
                {
                    if (!File.Exists(Path.Combine(dir, "Cinema 4D.exe"))) continue;
                    int y = YearFromFolderName(Path.GetFileName(dir));
                    if (y > bestYear) { bestYear = y; bestDir = dir; }
                }
                return bestDir;
            }
            catch { return null; }
        }

        /// <summary>定位某模式的主程序 exe 完整路径，找不到返回 null。</summary>
        public static string FindExePath(RenderMode mode)
        {
            string dir = FindInstallDir();
            if (dir == null) return null;
            string exe = Path.Combine(dir, ExeFileName(mode));
            return File.Exists(exe) ? exe : null;
        }

        /// <summary>定位某模式的异常记录文件路径（_bugreports 目录存在即可，txt 崩溃前可缺），找不到返回 null。</summary>
        public static string FindBugReportPath(RenderMode mode)
        {
            string installDir = FindInstallDir();
            if (installDir == null) return null;

            int year = 0;
            string guiExe = Path.Combine(installDir, "Cinema 4D.exe");
            if (File.Exists(guiExe)) year = YearFromProductVersion(guiExe);
            if (year == 0) year = YearFromFolderName(Path.GetFileName(installDir));
            if (year == 0) return null;

            string maxon = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Maxon");
            if (!Directory.Exists(maxon)) return null;

            string suffix = VariantSuffix(mode);
            var re = new Regex(@"^Maxon Cinema 4D " + year + @"_([0-9A-Za-z]+)" + Regex.Escape(suffix) + @"$", RegexOptions.IgnoreCase);

            DirectoryInfo best = null; DateTime bestTime = DateTime.MinValue;
            try
            {
                foreach (var d in Directory.GetDirectories(maxon, "Maxon Cinema 4D " + year + "*"))
                {
                    var di = new DirectoryInfo(d);
                    if (!re.IsMatch(di.Name)) continue;
                    string bug = Path.Combine(d, "_bugreports");
                    if (!Directory.Exists(bug)) continue;
                    DateTime t; try { t = Directory.GetLastWriteTime(bug); } catch { t = DateTime.MinValue; }
                    if (t >= bestTime) { bestTime = t; best = di; }
                }
            }
            catch { return null; }

            if (best == null) return null;
            return Path.Combine(best.FullName, "_bugreports", "_BugReport.txt");
        }

        // ---------- helpers ----------
        static string ParseExeFromCommand(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            int q1 = raw.IndexOf('"');
            if (q1 >= 0)
            {
                int q2 = raw.IndexOf('"', q1 + 1);
                if (q2 > q1) return raw.Substring(q1 + 1, q2 - q1 - 1);
            }
            int sp = raw.IndexOf(' ');
            return sp > 0 ? raw.Substring(0, sp) : raw;
        }

        static int YearFromProductVersion(string exePath)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(exePath);
                int y = YearFromVersionString(vi.ProductVersion);
                if (y == 0) y = YearFromVersionString(vi.FileVersion);
                return y;
            }
            catch { return 0; }
        }

        static int YearFromVersionString(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var m = Regex.Match(s, @"(19|20)\d{2}");
            return m.Success ? int.Parse(m.Value) : 0;
        }

        static int YearFromFolderName(string name)
        {
            var m = Regex.Match(name ?? "", @"(19|20)\d{2}");
            return m.Success ? int.Parse(m.Value) : 0;
        }
    }
}
