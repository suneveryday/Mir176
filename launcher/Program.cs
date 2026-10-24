using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Mir176 Classic Launcher")]
[assembly: AssemblyVersion("0.1.6.0")]
[assembly: AssemblyFileVersion("0.1.6.0")]

namespace Mir176.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            string id;
            using (SHA256 hash = SHA256.Create())
                id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()))).Replace("-", "");
            bool created;
            using (var mutex = new Mutex(true, "Local\\Mir176Launcher-" + id, out created))
            {
                if (!created) { MessageBox.Show("此客户端目录的登录器已经打开。", "Mir176"); return; }
                var service = new LauncherService(directory, LauncherService.IsClientRunning,
                    delegate(ProcessStartInfo info) { using (Process process = Process.Start(info)) { } });
                Application.Run(new LauncherWindow(service));
                mutex.ReleaseMutex();
            }
        }
    }
}
