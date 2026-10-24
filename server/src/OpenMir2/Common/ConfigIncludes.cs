using System;
using System.Collections.Generic;
using System.IO;

namespace OpenMir2.Common
{
    /// <summary>Expand a configuration completely before its caller publishes any records.</summary>
    public static class ConfigIncludes
    {
        public static bool Expand(StringList lines, string directory, string command, out string error)
        {
            try
            {
                var result = new List<string>();
                var active = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                int budget = 1000000;
                ExpandLines(lines, Path.GetFullPath(directory), command, active, result, 0, ref budget);
                lines.Clear();
                foreach (var line in result) lines.Add(line);
                error = null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void ExpandLines(StringList lines, string directory, string command,
            HashSet<string> active, List<string> result, int depth, ref int budget)
        {
            if (depth > 32) throw new InvalidDataException($"{command}嵌套超过32层。");
            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                string raw = lines[lineIndex];
                if (--budget < 0) throw new InvalidDataException($"{command}展开超过行数限制。");
                string line = raw.Trim();
                int split = line.IndexOfAny(new[] { ' ', '\t' });
                string token = split < 0 ? line : line[..split];
                if (!token.Equals(command, StringComparison.OrdinalIgnoreCase)) { result.Add(raw); continue; }
                string name = split < 0 ? string.Empty : line[split..].Trim().Trim('"');
                if (name.Length == 0) throw new InvalidDataException($"{command}缺少文件名。");
                string path = Path.GetFullPath(Path.Combine(directory, name.Replace('\\', Path.DirectorySeparatorChar)));
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
                    throw new InvalidDataException($"{command}文件不在配置目录: {name}");
                if (!active.Add(path)) throw new InvalidDataException($"{command}循环引用: {path}");
                try
                {
                    using var child = new StringList();
                    if (!File.Exists(path)) throw new FileNotFoundException($"{command}子文件不存在: {path}");
                    child.LoadFromFile(path);
                    ExpandLines(child, directory, command, active, result, depth + 1, ref budget);
                }
                finally { active.Remove(path); }
            }
        }
    }
}
