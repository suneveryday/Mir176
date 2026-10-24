using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Mir176.Launcher;

internal static class LauncherTests
{
    private static int passed;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(string section, string key, string fallback, StringBuilder value, uint length, string path);
    private static string NativeRead(string path, string section, string key)
    {
        var value = new StringBuilder(256);
        GetPrivateProfileString(section, key, "", value, 256, path);
        return value.ToString();
    }
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static void Reject(Action action, string label)
    { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new Exception(label); }

    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        try
        {
            Check(Endpoint.Parse(" 203.0.113.42 ", "7000").Address == "203.0.113.42", "IPv4 and trimming");
            Check(Endpoint.Parse("play.example", "65535").Port == 65535, "hostname and maximum port");
            Check(Endpoint.Parse("localhost", "1").Port == 1, "minimum port");
            Check(Endpoint.Parse("play.example", " 7000 ").Port == 7000, "trim copied port");
            foreach (string bad in new[] { "", "999.1.1.1", "01.2.3.4", "1.2.3", "a..example", "-a.example", "a-.example",
                "https://play.example", "play.example:7000", "play.example\r\nPort=9", "::1", new string('a', 31), "游戏.example" })
                Reject(delegate { Endpoint.Parse(bad, "7000"); }, "reject address " + passed);
            foreach (string bad in new[] { "0", "65536", "-1", "a", "7\n000" })
                Reject(delegate { Endpoint.Parse("play.example", bad); }, "reject port " + passed);
            string original = "; keep\r\n[Server]\r\nAddress=old.example\r\nPort=7000\r\nCustom=keep\r\n[Client]\r\nWidth=1024\r\nHeight=768\r\nResourceDir=Resource\r\n备注=保留\r\n";
            Endpoint next = Endpoint.Parse("new.example", "7500");
            string updated = ClientConfiguration.Update(original, next);
            Check(updated.Contains("Address=new.example\r\nPort=7500"), "replace endpoint");
            Check(updated.Contains("Custom=keep\r\n[Client]\r\nWidth=1024\r\nHeight=768"), "preserve unrelated settings");
            Check(updated.Contains("备注=保留") && updated.StartsWith("; keep\r\n"), "preserve comments and Chinese text");
            Check(ClientConfiguration.Update("[Client]\nWidth=1024\n", next).Contains("[Server]\nAddress=new.example"), "append missing server section");
            Check(ClientConfiguration.Update("[server]\naddress=old\n", next).Contains("Port=7500"), "case insensitive and missing port");
            foreach (string bad in new[] { "[Server]\n[Server]\n", "[Server]\nAddress=a\nAddress=b\n", "[Server]\nPort=1\nPort=2\n" })
            {
                bool rejected = false;
                try { ClientConfiguration.Update(bad, next); } catch (IOException) { rejected = true; }
                Check(rejected, "reject ambiguous configuration");
            }
            string config = Path.Combine(root, "Mir2Classic176.ini");
            foreach (Encoding encoding in new Encoding[] { Encoding.GetEncoding(936), new UTF8Encoding(true), new UnicodeEncoding(false, true) })
            {
                File.WriteAllText(config, original, encoding);
                ClientConfiguration.Save(root, next);
                Check(File.ReadAllText(config, encoding).Contains("备注=保留"), "preserve text for input encoding " + encoding.CodePage);
                Check(ClientConfiguration.Load(root)[0] == "new.example", "reload saved endpoint " + encoding.CodePage);
                Check(NativeRead(config, "Server", "Address") == "new.example", "native game INI reader sees address " + encoding.CodePage);
                Check(NativeRead(config, "Client", "Width") == "1024", "native game INI reader preserves display " + encoding.CodePage);
                if (encoding.CodePage == 65001)
                {
                    byte[] normalized = File.ReadAllBytes(config);
                    Check(normalized[0] == 255 && normalized[1] == 254, "normalize UTF8 BOM to native-compatible Unicode");
                }
            }
            File.WriteAllText(config, "[Server]\r\nAddress=old.example\r\nPort=7000\r\n", new UTF8Encoding(true));
            ClientConfiguration.Save(root, next);
            Check(NativeRead(config, "Server", "Address") == "new.example", "native reader accepts first server section after normalization");
            Check(Directory.Exists(Path.Combine(root, "Data")), "create optional Data directory");
            Check(Directory.GetFiles(root, "*.tmp").Length == 0, "no partial temporary config");
            byte[] before = File.ReadAllBytes(config);
            bool blocked = false;
            try { new LauncherService(root, delegate { return true; }, delegate { throw new Exception("must not start"); }).Execute("other.example", "7000", true); }
            catch (InvalidOperationException) { blocked = true; }
            Check(blocked && Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(config)), "running client blocks save and launch");
            string missing = Path.Combine(root, "missing"); Directory.CreateDirectory(missing);
            blocked = false;
            try { new LauncherService(missing, delegate { return false; }, delegate { }).Execute("play.example", "7000", true); }
            catch (IOException) { blocked = true; }
            Check(blocked && !File.Exists(Path.Combine(missing, "Mir2Classic176.ini")), "missing client blocks without rewriting config");
            File.WriteAllText(Path.Combine(root, "Mir2Classic176.exe"), "test fixture only");
            Directory.CreateDirectory(Path.Combine(root, "Resource")); Directory.CreateDirectory(Path.Combine(root, "Map"));
            ProcessStartInfo launched = null;
            var service = new LauncherService(root, delegate { return false; }, delegate(ProcessStartInfo info) { launched = info; });
            Application.EnableVisualStyles();
            using (var form = new LauncherWindow(service))
            {
                form.Show(); Application.DoEvents();
                Check(form.AddressBox.Text == "new.example" && form.PortBox.Text == "7500", "UI loads existing endpoint");
                form.AddressBox.Text = "bad:address"; form.PlayButton.PerformClick();
                Check(launched == null && form.Status.ForeColor == Color.Firebrick, "UI invalid address does not start");
                form.AddressBox.Text = "play.example"; form.PortBox.Text = "7501"; form.SaveButton.PerformClick();
                Check(launched == null && ClientConfiguration.Load(root)[1] == "7501", "UI save only");
                form.PlayButton.PerformClick();
                Check(launched != null && launched.FileName == Path.Combine(root, "Mir2Classic176.exe"), "UI launches fixed local executable");
                Check(launched.Arguments == "" && !launched.UseShellExecute && launched.WorkingDirectory == root, "no encoded arguments or shell; correct working directory");
                form.Close();
            }
            using (var form = new LauncherWindow(new LauncherService(missing, delegate { return false; }, delegate { })))
            {
                form.Show(); Application.DoEvents();
                Check(form.AddressBox.Text == "" && form.PortBox.Text == "7000", "first run has no bundled server address");
                using (var image = new Bitmap(form.Width, form.Height))
                { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "launcher-window.png")); }
                form.Close();
            }
            // Compile-time fixture replaces only this isolated test executable.
            File.Copy(args[1], Path.Combine(root, "Mir2Classic176.exe"), true);
            new LauncherService(root, delegate { return false; }, delegate(ProcessStartInfo info) {
                using (Process p = Process.Start(info)) { if (!p.WaitForExit(5000) || p.ExitCode != 0) throw new Exception("stub failed"); }
            }).Execute("stub.example", "7100", true);
            Check(File.ReadAllText(Path.Combine(root, "stub-result.txt")) == root + "\n0", "actual stub process receives working directory and no arguments");
            Console.WriteLine("TOTAL " + passed + " PASS; no game connection or real character changes");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
