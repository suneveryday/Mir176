using OpenMir2;
using OpenMir2.Common;
using OpenMir2.Data;

namespace SystemModule;

public static class AdminPermissions
{
    public static bool Reload()
    {
        try
        {
            string file = SystemShare.GetEnvirFilePath("AdminList.txt");
            if (!File.Exists(file)) return false;
            using var lines = new StringList();
            lines.LoadFromFile(file);
            var entries = new List<AdminInfo>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith(';')) continue;
                string[] fields = line.Split(new[] { ' ', '\t', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 3 || fields[0].Length != 1) return false;
                char marker = fields[0][0];
                int level = marker == '*' ? 10 : marker >= '1' && marker <= '9' ? 10 - (marker - '0') : -1;
                if (level < 1 || !names.Add(fields[1])) return false;
                entries.Add(new AdminInfo { Level = (byte)level, ChrName = fields[1], IPaddr = fields[2] });
            }
            // Publish only a complete list. A failed reload leaves the previous list intact.
            SystemShare.WorldEngine.AdminList = entries;
            return true;
        }
        catch (Exception)
        {
            LogService.Warn("管理员名单读取失败，保留原名单。");
            return false;
        }
    }

    public static byte Resolve(string character, string address)
    {
        foreach (var entry in SystemShare.WorldEngine.AdminList)
        {
            if (!string.Equals(entry.ChrName, character, StringComparison.OrdinalIgnoreCase)) continue;
            if (SystemShare.Config.PermissionSystem && !MatchesAddress(address, entry.IPaddr)) return 0;
            return (byte)Math.Min(10, (int)entry.Level);
        }
        return (byte)Math.Min(10, (int)SystemShare.Config.StartPermission);
    }

    private static bool MatchesAddress(string address, string pattern)
    {
        if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(pattern)) return false;
        if (pattern == "*") return true;
        int wildcard = pattern.IndexOf('*');
        if (wildcard >= 0)
            return wildcard == pattern.Length - 1 && address.StartsWith(pattern[..wildcard], StringComparison.OrdinalIgnoreCase);
        return string.Equals(address, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
