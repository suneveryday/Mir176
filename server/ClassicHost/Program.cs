using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using MySqlConnector;

internal static class Program
{
    private static readonly string[] Services = ["LoginSrv", "DBSrv", "GameSrv", "LoginGate", "SelGate", "GameGate"];
    private static readonly string State = Environment.GetEnvironmentVariable("CLASSIC_STATE") ?? "/state";
    private static readonly Encoding Legacy = CreateEncoding();
    private static Encoding CreateEncoding() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(936); }
    [DllImport("libc", EntryPoint = "kill")] private static extern int SendSignal(int pid, int signal);

    private static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--verify-package" })) return VerifyPackage();
        if (args.SequenceEqual(new[] { "--healthcheck" }))
            return File.Exists(Path.Combine(State, "ready")) && await PortsReady() ? 0 : 1;
        if (args.Length != 0) throw new ArgumentException("Unknown classic host option.");
        var children = new List<Process>();
        using var stop = new CancellationTokenSource();
        using var term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.Cancel(); });
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.Cancel(); });
        try
        {
            Directory.CreateDirectory(State);
            File.Delete(Path.Combine(State, "ready"));
            string identity = Path.Combine(State, "project-identity");
            if (File.Exists(identity) && File.ReadAllText(identity) != "Mir2Classic176") throw new InvalidDataException("State belongs to a different project.");
            File.WriteAllText(identity, "Mir2Classic176");
            CopyTree("/opt/classic/services", State);
            Configure();
            string password = ReadPassword();
            var builder = new MySqlConnectionStringBuilder { Server = "database", UserID = "classic", Password = password, Database = "classic_data", ConnectionTimeout = 3 };
            bool databaseReady = false;
            for (int i=0;i<60 && !stop.IsCancellationRequested;i++)
            {
                try { using var db = new MySqlConnection(builder.ConnectionString); await db.OpenAsync(stop.Token); databaseReady = true; break; }
                catch (MySqlException) { await Task.Delay(1000, stop.Token); }
            }
            if (!databaseReady) throw new InvalidOperationException("Classic database did not become ready.");
            foreach (string service in Services)
            {
                var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(State, service), UseShellExecute = false };
                info.ArgumentList.Add(Path.Combine(State, service, service+".dll"));
                ConfigureGm(info, service);
                children.Add(Process.Start(info) ?? throw new InvalidOperationException("Cannot start classic service."));
                await Task.Delay(300, stop.Token);
            }
            for (int i=0;i<120;i++)
            {
                if (children.Any(x => x.HasExited)) throw new InvalidOperationException("A classic service exited during initialization.");
                if (await PortsReady()) { File.WriteAllText(Path.Combine(State,"ready"),"candidate"); break; }
                await Task.Delay(1000,stop.Token);
            }
            if (!File.Exists(Path.Combine(State,"ready"))) throw new InvalidOperationException("Classic gates did not become ready.");
            Console.WriteLine("Mir2Classic176 candidate services are ready; historical and gameplay acceptance remain separate.");
            while (!stop.IsCancellationRequested)
            {
                if (children.Any(x => x.HasExited)) throw new InvalidOperationException("A classic service exited unexpectedly.");
                await Task.Delay(1000, stop.Token);
            }
            return 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 0; }
        catch (Exception error) { Console.Error.WriteLine("Classic startup failed: "+error.GetType().Name); return 1; }
        finally
        {
            File.Delete(Path.Combine(State,"ready"));
            foreach (var child in children.AsEnumerable().Reverse())
            {
                if (!child.HasExited) SendSignal(child.Id,15);
                try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(child.StartInfo.ArgumentList[0].EndsWith("GameSrv.dll",StringComparison.Ordinal) ? 100 : 10)); }
                catch (TimeoutException) { child.Kill(true); await child.WaitForExitAsync(); }
                child.Dispose();
            }
        }
    }
    private static int VerifyPackage()
    {
        var hashes = new List<string>();
        foreach (string service in Services)
        {
            string directory = Path.Combine("/opt/classic/services", service);
            _ = AssemblyName.GetAssemblyName(Path.Combine(directory, service+".dll"));
            foreach (string suffix in new[] { ".deps.json", ".runtimeconfig.json" })
                using (JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory,service+suffix)))) { }
            hashes.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory,"OpenMir2.dll")))));
        }
        if (hashes.Distinct().Count()!=1) throw new InvalidDataException("Service dependencies differ.");
        _ = AssemblyName.GetAssemblyName("/opt/classic/services/DBSrv/DBSrv.Storage.MySQL.dll");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes("/opt/classic/content-manifest.json"));
        int checkedFiles=0;
        foreach (var row in manifest.RootElement.GetProperty("files").EnumerateObject())
        {
            if (!row.Name.StartsWith("GameSrv/",StringComparison.Ordinal)) continue;
            string path=Path.GetFullPath(Path.Combine("/opt/classic/services",row.Name));
            if(!path.StartsWith("/opt/classic/services/GameSrv/",StringComparison.Ordinal))throw new InvalidDataException("Invalid manifest path.");
            if(!Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(row.Value.GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Packaged content hash differs.");
            checkedFiles++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { project="Mir2Classic176",services=6,mysqlPlugin=true,identicalSharedAssemblies=true,verifiedContentFiles=checkedFiles,startedGameServices=false }));
        return 0;
    }
    private static void CopyTree(string from,string to)
    {
        foreach (string directory in Directory.EnumerateDirectories(from,"*",SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to,Path.GetRelativePath(from,directory)));
        foreach (string file in Directory.EnumerateFiles(from,"*",SearchOption.AllDirectories))
        {
            string relative=Path.GetRelativePath(from,file).Replace('\\','/');
            if (relative.EndsWith("/classic-gm-instance.id", StringComparison.Ordinal)) continue;
            string target=Path.Combine(to,relative);
            bool mutable=relative.StartsWith("GameSrv/Castle/",StringComparison.Ordinal) || relative.StartsWith("GameSrv/Guild/",StringComparison.Ordinal) || relative.StartsWith("GameSrv/Envir/UserData/",StringComparison.Ordinal);
            if(!mutable || !File.Exists(target))File.Copy(file,target,true);
        }
    }
    private static Task<bool> PortsReady()
    {
        // Inspect sockets without creating fake sessions in the legacy gates.
        var network = IPGlobalProperties.GetIPGlobalProperties();
        var listeners = network.GetActiveTcpListeners().Select(x => x.Port).ToHashSet();
        var connections = network.GetActiveTcpConnections().Where(x => x.State == TcpState.Established).ToArray();
        bool ready = new[] { 5000, 7000, 7100, 7200 }.All(listeners.Contains)
            && new[] { 5000, 5100, 5500, 6000 }.All(port => connections.Any(x => x.LocalEndPoint.Port == port))
            && connections.Count(x => x.LocalEndPoint.Port == 5600) >= 2;
        return Task.FromResult(ready);
    }
    internal static void ConfigureGm(ProcessStartInfo info, string service)
    {
        info.Environment.Remove("MIR2HOME_GM_TOKEN_FILE");
        string? enabled = Environment.GetEnvironmentVariable("CLASSIC_GM_ENABLED");
        if (enabled != null && enabled != "true" && enabled != "false")
            throw new InvalidDataException("CLASSIC_GM_ENABLED must be true or false.");
        if (enabled != "true" || service != "GameSrv") return;
        string? path = Environment.GetEnvironmentVariable("CLASSIC_GM_TOKEN_FILE");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("An explicitly mounted classic GM token is required.");
        string token = File.ReadAllText(path).Trim();
        if (token.Length < 43 || token.Length > 128 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw new InvalidDataException("Classic GM token is invalid.");
        info.Environment["MIR2HOME_GM_TOKEN_FILE"] = path;
    }

    private static string ReadPassword()
    {
        string password=File.ReadAllText("/run/secrets/db_password").Trim();
        if(password.Length!=64 || password.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Classic secret must be generated by the setup tool.");
        return password;
    }
    private static void Write(string service,string name,string value) => File.WriteAllText(Path.Combine(State,service,name),value.Replace("\r\n","\n").Replace("\n","\r\n"),Legacy);
    private static void Configure()
    {
        string address=Environment.GetEnvironmentVariable("CLASSIC_PUBLIC_ADDRESS") ?? throw new InvalidDataException("Published address is required.");
        if(address.Length is <1 or >30 || address.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='.' && c!='-'))throw new InvalidDataException("Invalid published address.");
        static int Port(string name) { if(!int.TryParse(Environment.GetEnvironmentVariable(name),out int n)||n is <1 or >65535)throw new InvalidDataException("Published port is required.");return n; }
        int select=Port("CLASSIC_SELECT_PORT"),game=Port("CLASSIC_GAME_PORT");
        string Connection(string db) => new MySqlConnectionStringBuilder {Server="database",UserID="classic",Password=ReadPassword(),Database=db,CharacterSet="utf8mb4",AllowUserVariables=true}.ConnectionString;
        Write("LoginSrv","logsrv.conf",$"[Server]\nDBServer=127.0.0.1\nDBSPort=6000\nGateAddr=127.0.0.1\nGatePort=5500\nServerAddr=127.0.0.1\nServerPort=5600\nMonAddr=127.0.0.1\nMonPort=3000\nTestServer=1\nDynamicIPMode=0\n[DataBase]\nConnctionString={Connection("classic_account")}\n");
        Write("LoginSrv","ServerAddr.txt","127.0.0.1\n");
        Write("LoginSrv","UserLimit.txt","Mir2Classic176 Mir2Classic176 100\n");
        Write("DBSrv","AddrTable.txt","127.0.0.1\n");
        Write("DBSrv","ServerInfo.txt",$"127.0.0.1 {address} {game}\n");
        Write("GameSrv","!runaddr.txt","127.0.0.1\n");
        Write("LoginSrv","AddrTable.txt",$"Mir2Classic176 Mir2Classic176 127.0.0.1 {address} {address}:{select}\n");
        Write("DBSrv","dbsvr.conf",$"[Setup]\nServerName=Mir2Classic176\nServerAddr=127.0.0.1\nServerPort=6000\nGateAddr=127.0.0.1\nGatePort=5100\nMapFile=../GameSrv/Envir/MapInfo.txt\nDynamicIPMode=0\n[Server]\nIDSAddr=127.0.0.1\nIDSPort=5600\n[DataBase]\nStoreage=MySQL\nConnctionString={Connection("classic_db")}\n");
        Write("GameSrv","server.conf",$"[Server]\nServerName=Mir2Classic176\nServerNumber=0\nServerIndex=0\nShutdownSeconds=10\nTestServer=TRUE\nTestLevel=1\nTestGold=0\nTestServerUserLimit=100\nGateAddr=127.0.0.1\nGatePort=5000\nDBAddr=127.0.0.1\nDBPort=6000\nIDSAddr=127.0.0.1\nIDSPort=5600\nEnableMarket=0\nEnableChatServer=0\n[DataBase]\nConnctionString={Connection("classic_data")}\n[Share]\nBaseDir=./\nEnvirDir=Envir/\nMapDir=Map/\nNoticeDir=Envir/Notice/\nGuildDir=Guild/\nGuildFile=Guild/List.txt\nCastleDir=Castle/\nCastleFile=Castle/List.txt\nConLogDir=Log/\n[Setup]\nHomeMap=0\nHomeX=289\nHomeY=618\nCanShoutMsgLevel=7\nItemExpRate=100\nCastleWarTime=10800000\nGuildWarTime=10800000\nUpgradeWeaponMaxPoint=7\nUpgradeWeaponPrice=10000\n");
        Write("GameSrv","!servertable.txt",$"0 127.0.0.1 {address} {game}\n");
        Write("LoginGate","config.conf","[LoginGate]\nCount=1\nServerAddr0=127.0.0.1\nServerPort0=5500\nGateAddr0=0.0.0.0\nGatePort0=7000\nShowDebug=0\n");
        Write("SelGate","config.conf","[SelGate]\nCount=1\nServerAddr0=127.0.0.1\nServerPort0=5100\nGateAddr0=0.0.0.0\nGatePort0=7100\nShowDebug=0\n");
        Write("GameGate","config.conf","[GameGate]\nServerWorkThread=1\nServerAddr1=127.0.0.1\nServerPort1=5000\nGateAddress1=0.0.0.0\nGatePort1=7200\nMessageWorkThread=1\nDebug=0\n");
        foreach(string dir in new[]{"Guild","Log","Envir/Notice","Envir/UserData"})Directory.CreateDirectory(Path.Combine(State,"GameSrv",dir));
        string guilds=Path.Combine(State,"GameSrv/Guild/List.txt");if(!File.Exists(guilds))File.WriteAllText(guilds,"");
    }
}
