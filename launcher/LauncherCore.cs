using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Mir176.Launcher
{
    public sealed class Endpoint
    {
        public readonly string Address;
        public readonly int Port;
        private Endpoint(string address, int port) { Address = address; Port = port; }

        public static Endpoint Parse(string address, string port)
        {
            address = (address ?? "").Trim();
            port = (port ?? "").Trim();
            if (address.Length == 0 || address.Length > 30 || !Regex.IsMatch(address, @"\A[A-Za-z0-9.-]+\z"))
                throw new ArgumentException("请输入 IPv4 地址或域名，最长 30 个字符，不要填写网址或端口。");
            if (Regex.IsMatch(address, @"\A[0-9.]+\z"))
            {
                string[] parts = address.Split('.');
                int number;
                if (parts.Length != 4 || parts.Any(p => p.Length == 0 || p.Length > 3 ||
                    (p.Length > 1 && p[0] == '0') || !int.TryParse(p, out number) || number > 255))
                    throw new ArgumentException("IPv4 地址格式不正确。");
            }
            else if (address.Split('.').Any(label => label.Length == 0 ||
                !Regex.IsMatch(label, @"\A[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\z")))
                throw new ArgumentException("域名格式不正确。");
            int parsedPort;
            if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out parsedPort) ||
                parsedPort < 1 || parsedPort > 65535)
                throw new ArgumentException("登录端口必须为 1–65535 之间的整数。");
            return new Endpoint(address, parsedPort);
        }
    }

    public static class ClientConfiguration
    {
        private static bool IsSection(string line, string name)
        { return string.Equals(line.Trim(), "[" + name + "]", StringComparison.OrdinalIgnoreCase); }

        private static bool IsKey(string line, string name)
        {
            int equals = line.IndexOf('=');
            return equals >= 0 && string.Equals(line.Substring(0, equals).Trim(), name, StringComparison.OrdinalIgnoreCase);
        }

        public static string Update(string original, Endpoint endpoint)
        {
            if (original.IndexOf('\0') >= 0) throw new IOException("配置文件包含无效字符，未做更改。");
            string newline = original.Contains("\r\n") ? "\r\n" : "\n";
            var lines = original.Replace("\r\n", "\n").Split('\n').ToList();
            var sections = Enumerable.Range(0, lines.Count).Where(i => IsSection(lines[i], "Server")).ToList();
            if (sections.Count > 1) throw new IOException("配置有重复的 Server 分节，未做更改。");
            if (sections.Count == 0)
            {
                lines.Add("[Server]");
                lines.Add("Address=" + endpoint.Address);
                lines.Add("Port=" + endpoint.Port.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                int start = sections[0] + 1, end = start;
                while (end < lines.Count && !lines[end].Trim().StartsWith("[")) end++;
                foreach (string key in new[] { "Address", "Port" })
                {
                    var matches = Enumerable.Range(start, end - start).Where(i => IsKey(lines[i], key)).ToList();
                    if (matches.Count > 1) throw new IOException("配置有重复的服务器字段，未做更改。");
                    string value = key == "Address" ? endpoint.Address : endpoint.Port.ToString(CultureInfo.InvariantCulture);
                    if (matches.Count == 1) lines[matches[0]] = key + "=" + value;
                    else { lines.Insert(end, key + "=" + value); end++; }
                }
            }
            return string.Join(newline, lines);
        }

        public static string[] ReadEndpoint(string text)
        {
            string address = "", port = "7000";
            bool server = false;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Trim().StartsWith("[")) server = IsSection(line, "Server");
                if (!server) continue;
                if (IsKey(line, "Address")) address = line.Substring(line.IndexOf('=') + 1).Trim();
                if (IsKey(line, "Port")) port = line.Substring(line.IndexOf('=') + 1).Trim();
            }
            return new[] { address, port };
        }

        private static string Read(string path, out Encoding encoding)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254)
                encoding = new UnicodeEncoding(false, true, true);
            else if (bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191)
            {
                // The game's Windows INI API cannot read a UTF-8 BOM before [Server].
                string decoded = new UTF8Encoding(true, true).GetString(bytes).TrimStart('\uFEFF');
                encoding = new UnicodeEncoding(false, true, true);
                return decoded;
            }
            else
                encoding = Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            string text = encoding.GetString(bytes);
            return text.TrimStart('\uFEFF');
        }

        public static string[] Load(string directory)
        {
            string path = Path.Combine(directory, "Mir2Classic176.ini");
            Encoding encoding;
            return File.Exists(path) ? ReadEndpoint(Read(path, out encoding)) : new[] { "", "7000" };
        }

        public static void Save(string directory, Endpoint endpoint)
        {
            string path = Path.Combine(directory, "Mir2Classic176.ini");
            Encoding encoding = new UTF8Encoding(false);
            string text = File.Exists(path) ? Read(path, out encoding) :
                "[Client]\r\nResourceDir=Resource\r\nWidth=800\r\nHeight=600\r\nFullScreen=0\r\n[Diagnostics]\r\nEnabled=0\r\n";
            string updated = Update(text, endpoint);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, updated, encoding);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                Directory.CreateDirectory(Path.Combine(directory, "Data"));
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    public sealed class LauncherService
    {
        public readonly string DirectoryPath;
        private readonly Func<string, bool> isRunning;
        private readonly Action<ProcessStartInfo> start;

        public LauncherService(string directory, Func<string, bool> running, Action<ProcessStartInfo> startProcess)
        { DirectoryPath = Path.GetFullPath(directory); isRunning = running; start = startProcess; }

        public void Execute(string address, string port, bool launch)
        {
            Endpoint endpoint = Endpoint.Parse(address, port);
            string client = Path.Combine(DirectoryPath, "Mir2Classic176.exe");
            if (isRunning(client)) throw new InvalidOperationException("请先正常退出当前客户端，再修改服务器设置。");
            if (launch && (!File.Exists(client) || !Directory.Exists(Path.Combine(DirectoryPath, "Resource")) ||
                !Directory.Exists(Path.Combine(DirectoryPath, "Map"))))
                throw new IOException("请把登录器放在完整客户端目录，与 Mir2Classic176.exe、Resource 和 Map 放在一起。");
            ClientConfiguration.Save(DirectoryPath, endpoint);
            if (launch) start(new ProcessStartInfo(client) {
                WorkingDirectory = DirectoryPath, Arguments = "", UseShellExecute = false
            });
        }

        public static bool IsClientRunning(string path)
        {
            foreach (Process process in Process.GetProcessesByName("Mir2Classic176"))
            {
                using (process)
                {
                    try { if (string.Equals(process.MainModule.FileName, path, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch (System.ComponentModel.Win32Exception) { return true; }
                    catch (InvalidOperationException) { /* Process exited during inspection. */ }
                }
            }
            return false;
        }
    }
}
