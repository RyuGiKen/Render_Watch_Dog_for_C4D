using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RenderServerGui.Services
{
    /// <summary>
    /// 轻量本地化器：从 exe 目录下的 i18n/*.txt 读取多语言表（每语言一个文件，key=value，
    /// 首行 #name= 提供该语言的显示名；加语言=加文件，无需改代码）。
    /// 查不到键时回退到调用处保留的中文备用词（TryGetLocalizationString 语义），
    /// 因此丢失配置文件也不会报错、界面照常显示简体中文。
    /// 线程安全：语言切换只是原子替换当前表引用，后台日志线程读到的始终是完整表。
    /// </summary>
    public static class Localizer
    {
        /// <summary>默认/回退语言码（其文本以代码内中文备用词形式存在，文件可缺省）。</summary>
        public const string DefaultLang = "zh-CN";

        private static readonly object _gate = new object();
        private static Dictionary<string, Dictionary<string, string>> _tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static volatile Dictionary<string, string> _active = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly List<KeyValuePair<string, string>> _languages = new List<KeyValuePair<string, string>>();

        /// <summary>当前语言码。</summary>
        public static string CurrentCode { get; private set; } = DefaultLang;

        /// <summary>可选语言列表：(语言码, 该语言本名)，用于填充下拉。</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Languages => _languages;

        /// <summary>从指定 i18n 目录加载所有语言文件。</summary>
        public static void Load(string i18nDir)
        {
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var langs = new List<KeyValuePair<string, string>>();

            try
            {
                if (Directory.Exists(i18nDir))
                {
                    foreach (string file in Directory.GetFiles(i18nDir, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    {
                        string code = Path.GetFileNameWithoutExtension(file);
                        var map = new Dictionary<string, string>(StringComparer.Ordinal);
                        string name = code;
                        foreach (string raw in File.ReadAllLines(file, Encoding.UTF8))
                        {
                            string line = raw.Trim();
                            if (line.Length == 0) continue;
                            if (line.StartsWith("#"))
                            {
                                if (line.StartsWith("#name=", StringComparison.OrdinalIgnoreCase))
                                    name = line.Substring(6).Trim();
                                continue;
                            }
                            int eq = line.IndexOf('=');
                            if (eq <= 0) continue;
                            map[line.Substring(0, eq).Trim()] = Unescape(line.Substring(eq + 1));
                        }
                        tables[code] = map;
                        langs.Add(new KeyValuePair<string, string>(code, name));
                    }
                }
            }
            catch { /* 读取失败则退化为纯备用词（中文）显示，不抛异常 */ }

            // 让默认语言排在最前，其余保持文件名字典序
            var ordered = langs
                .OrderBy(kv => string.Equals(kv.Key, DefaultLang, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_gate)
            {
                _tables = tables;
                _languages.Clear();
                _languages.AddRange(ordered);
                ApplyActive(CurrentCode);
            }
        }

        /// <summary>还原值中的 \n、\t、\\ 转义（文件为单行 key=value，用转义表示换行/制表/反斜杠）。</summary>
        private static string Unescape(string v)
        {
            if (v == null || v.IndexOf('\\') < 0) return v;
            var sb = new StringBuilder(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length)
                {
                    char n = v[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 't') sb.Append('\t');
                    else if (n == '\\') sb.Append('\\');
                    else { sb.Append('\\').Append(n); }
                }
                else sb.Append(v[i]);
            }
            return sb.ToString();
        }

        /// <summary>切换当前语言（未知码则回退默认）。</summary>
        public static void SetLanguage(string code)
        {
            lock (_gate)
            {
                CurrentCode = code;
                ApplyActive(code);
            }
        }

        private static void ApplyActive(string code)
        {
            if (_tables.TryGetValue(code, out var t) && t != null)
                _active = t;
            else
                _active = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>取当前语言下 key 的译文；取不到则 out 回 fallback 并返回 false（备用词兜底）。</summary>
        public static bool TryGetLocalizationString(string key, string fallback, out string value)
        {
            var table = _active; // 读快照，切换不影响进行中的读取
            if (key != null && table.TryGetValue(key, out value))
                return true;
            value = fallback;
            return false;
        }

        /// <summary>取译文，缺省回退到中文备用词。</summary>
        public static string T(string key, string fallback)
        {
            return TryGetLocalizationString(key, fallback, out var v) ? v : fallback;
        }

        /// <summary>取译文并按 {0}{1}… 格式化；格式化失败时返回未格式化的原文，绝不抛异常。</summary>
        public static string Tf(string key, string fallback, params object[] args)
        {
            string s = T(key, fallback);
            if (args == null || args.Length == 0) return s;
            try { return string.Format(s, args); }
            catch { return s; }
        }
    }
}
