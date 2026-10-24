using System.Reflection;
using System.Text;
using CommandModule;
using CommandModule.Commands;
using DBSrv.Storage;
using DBSrv.Storage.MySQL;
using GameSrv.Word;
using M2Server.Player;
using M2Server.Items;
using MySqlConnector;
using OpenMir2;
using OpenMir2.Data;
using OpenMir2.Enums;
using OpenMir2.Packets.ClientPackets;
using OpenMir2.Packets.ServerPackets;
using SystemModule;
using SystemModule.Actors;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
LogService.Logger = new Serilog.LoggerConfiguration().CreateLogger();
int checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
    Console.WriteLine("PASS " + label);
}

// Use the actual level calculation, including every representable player level.
foreach (var (job, divisor) in new[] { (PlayerJob.Warrior, 13d), (PlayerJob.Wizard, 90d), (PlayerJob.Taoist, 42d) })
{
    int previous = 0;
    for (int level = 1; level <= 255; level++)
    {
        var player = new PlayObject { Job = job };
        player.Abil.Level = (byte)level;
        player.RecalcLevelAbilitys();
        int expected = Math.Min(255, 12 + (int)Math.Round(level * level / divisor, MidpointRounding.AwayFromZero));
        Check(player.Abil.MaxHandWeight == expected && expected >= previous, $"{job} hand capacity level {level}");
        previous = expected;
    }
}

var world = new WorldServer();
SystemShare.WorldEngine = world;
SystemShare.Config.StartPermission = 0;
SystemShare.Config.PermissionSystem = false;
string directory = Path.Combine(AppContext.BaseDirectory, "synthetic-admin-fixture");
Directory.CreateDirectory(directory);
SystemShare.Config.EnvirDir = directory;
string adminFile = Path.Combine(directory, "AdminList.txt");
File.WriteAllText(adminFile, "; Synthetic fixture only\n  * FixtureAdministrator *\n1 FixtureOperator 203.0.113.*\n", new UTF8Encoding(true));
Check(AdminPermissions.Reload(), "load whitespace and legacy administrator markers");
Check(AdminPermissions.Resolve("FixtureAdministrator", "203.0.113.5") == 10, "listed administrator receives configured level");
Check(AdminPermissions.Resolve("fixtureoperator", "203.0.113.5") == 9, "listed operator and case-insensitive role matching");
Check(AdminPermissions.Resolve("OrdinaryFixture", "203.0.113.5") == 0, "ordinary role remains unprivileged");
SystemShare.Config.PermissionSystem = true;
Check(AdminPermissions.Resolve("FixtureOperator", "203.0.113.50") == 9, "configured address prefix accepts matching client");
Check(AdminPermissions.Resolve("FixtureOperator", "203.0.114.50") == 0, "mismatched address receives no administrator privilege");
Check(AdminPermissions.Resolve("FixtureAdministrator", "203.0.114.50") == 10, "explicit wildcard address works without indexing error");
Check(AdminPermissions.Resolve("FixtureAdministrator", "") == 0, "missing client address cannot pass address enforcement");
File.WriteAllText(adminFile, "* FixtureAdministrator *\n* FixtureAdministrator *\n", new UTF8Encoding(true));
Check(!AdminPermissions.Reload() && world.AdminList.Count == 2, "duplicate entry fails atomically and retains previous list");
File.WriteAllText(adminFile, "invalid entry\n", new UTF8Encoding(true));
Check(!AdminPermissions.Reload() && world.AdminList.Count == 2, "malformed entry does not partially replace administrator list");
File.Delete(adminFile);
Check(!AdminPermissions.Reload() && world.AdminList.Count == 2, "missing file reports failure and retains administrator list");
File.WriteAllText(adminFile, "* ReplacementFixture *\n", new UTF8Encoding(true));
Check(AdminPermissions.Reload() && AdminPermissions.Resolve("FixtureAdministrator", "203.0.113.5") == 0, "valid reload revokes removed role on next resolution");
Check(AdminPermissions.Resolve("ReplacementFixture", "203.0.113.5") == 10, "valid reload grants only replacement entry");
SystemShare.Config.PermissionSystem = false;

var target = new PlayObject { ChrName = "CreditFixture", Permission = 0 };
var administrator = new PlayObject { ChrName = "CommandFixture", Permission = 10 };
var players = (IList<IPlayerActor>)typeof(WorldServer).GetField("PlayObjectList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world)!;
players.Add(target);
var credit = new CreditPointCommand();
credit.Register(new CommandAttribute("CreditFixtureCommand", "fixture", 10), typeof(CreditPointCommand).GetMethod("Execute")!);
foreach (var (operation, amount, initial, expected) in new[] {
    ("=", "255", 0, 255), ("=", "0", 255, 0), ("+", "5", 250, 255), ("-", "5", 5, 0),
    ("+", "1", 255, 255), ("-", "1", 0, 0), ("=", "256", 50, 50), ("+", "2147483647", 50, 50),
    ("=", "nonnumeric", 50, 50), ("=", "-1", 50, 50), ("++", "1", 50, 50) })
{
    target.CreditPoint = (byte)initial;
    credit.Execute(new[] { target.ChrName, operation, amount }, administrator);
    Check(target.CreditPoint == expected, $"credit {operation} {amount} from {initial}");
}
target.CreditPoint = 77;
credit.Execute(Array.Empty<string>(), administrator);
credit.Execute(new[] { target.ChrName, "=" }, administrator);
credit.Execute(new[] { target.ChrName, "=", "1", "extra" }, administrator);
Check(target.CreditPoint == 77, "missing and extra arguments do not mutate credit");
credit.Register(new CommandAttribute("CreditFixtureCommand", "fixture", 10), typeof(CreditPointCommand).GetMethod("Execute")!);
string denied = credit.Handle("CreditFixture = 5", target);
Check(target.CreditPoint == 77 && !string.IsNullOrEmpty(denied), "ordinary player cannot invoke administrative credit command");
credit.Handle("CreditFixture = 50", administrator);
Check(target.CreditPoint == 50, "authorized command dispatcher accepts valid credit assignment");

// Real storage-plugin round trips against a disposable, explicitly named database.
string connectionString = Environment.GetEnvironmentVariable("MIR2_TEST_DB") ?? throw new Exception("MIR2_TEST_DB required");
var builder = new MySqlConnectionStringBuilder(connectionString);
if (builder.Database != "repair_test" || builder.Server != "testdb") throw new Exception("Only the isolated synthetic test database is permitted");
using var database = new MySqlConnection(connectionString);
database.Open();
void Sql(string sql) { using var c = new MySqlCommand(sql, database); c.ExecuteNonQuery(); }
long Scalar(string sql) { using var c = new MySqlCommand(sql, database); return Convert.ToInt64(c.ExecuteScalar()); }
Sql("INSERT INTO characters(ChrName,CreditPoint,Deleted) VALUES('LegacyCreditFixture',-1,1),('PositiveCreditFixture',127,1)");
string migration = File.ReadAllText(args.Single());
Sql(migration);
Check(Scalar("SELECT CreditPoint FROM characters WHERE ChrName='LegacyCreditFixture'") == 255, "credit migration preserves legacy signed-byte value");
Check(Scalar("SELECT CreditPoint FROM characters WHERE ChrName='PositiveCreditFixture'") == 127, "credit migration preserves existing positive value");
Sql(migration);
Check(Scalar("SELECT CreditPoint FROM characters WHERE ChrName='LegacyCreditFixture'") == 255, "credit migration can be repeated without reinterpreting unsigned values");
var storage = new PlayDataStorage(new StorageOption { ConnectionString = connectionString });
var snapshot = new CharacterDataInfo();
foreach (var property in typeof(CharacterData).GetProperties().Where(p => p.PropertyType == typeof(string))) property.SetValue(snapshot.Data, "");
snapshot.Header = new RecordHeader { Name = "PersistenceFixture" };
snapshot.Data.ChrName = "PersistenceFixture";
snapshot.Data.Account = "fixture_account";
snapshot.Data.CurMap = "0"; snapshot.Data.HomeMap = "0";
Check(storage.Add(snapshot), "create synthetic role through actual storage plugin");
ServerUserItem Item(int id, int seed) => new() { MakeIndex = id, Index = 225, Dura = 31000, DuraMax = 41000,
    Desc = Enumerable.Range(0, 14).Select(i => (byte)((seed + i * 17) % 256)).ToArray() };
snapshot.Data.HumItems[0] = Item(7003, 10);
snapshot.Data.HumItems[1] = Item(7001, 255);
snapshot.Data.BagItems[5] = Item(7008, 20);
snapshot.Data.BagItems[23] = Item(7002, 30);
snapshot.Data.StorageItems[7] = Item(7006, 40);
snapshot.Data.StorageItems[19] = Item(7004, 50);
snapshot.PersistenceRevision = 1;
snapshot.Data.CreditPoint = 255;
Check(storage.Update(snapshot.Data.ChrName, snapshot), "save sparse equipment bag warehouse and credit 255");
CharacterDataInfo Load()
{
    var reader = new PlayDataStorage(new StorageOption { ConnectionString = connectionString }); reader.LoadQuickList();
    CharacterDataInfo loaded = null;
    Check(reader.Get("PersistenceFixture", ref loaded), "fresh storage instance reads complete character snapshot");
    return loaded;
}
void SameItems(CharacterDataInfo expected, CharacterDataInfo actual, string label)
{
    var groups = new[] { (expected.Data.HumItems, actual.Data.HumItems), (expected.Data.BagItems, actual.Data.BagItems), (expected.Data.StorageItems, actual.Data.StorageItems) };
    foreach (var (left, right) in groups)
    for (int i = 0; i < left.Length; i++)
    {
        if (left[i] == null || left[i].Index == 0) continue;
        Check(right[i].MakeIndex == left[i].MakeIndex && right[i].Index == left[i].Index && right[i].Desc.SequenceEqual(left[i].Desc)
            && right[i].Dura == left[i].Dura && right[i].DuraMax == left[i].DuraMax, label + " slot " + i);
    }
}
var first = Load(); SameItems(snapshot, first, "all fourteen attributes keyed by instance");
Check(first.Data.CreditPoint == 255, "credit upper boundary survives database reload");
var relocated = first.Data.HumItems[1]; first.Data.HumItems[1] = new ServerUserItem(); first.Data.BagItems[30] = relocated;
var warehouse = first.Data.StorageItems[19]; first.Data.StorageItems[19] = first.Data.BagItems[5]; first.Data.BagItems[5] = warehouse;
first.PersistenceRevision++;
Check(storage.Update("PersistenceFixture", first), "save relocated same-name weapons");
var second = Load(); SameItems(first, second, "attributes survive slot and inventory movement");
second.PersistenceRevision++;
Check(storage.Update("PersistenceFixture", second), "ordinary save after reload retains attributes");
var third = Load(); SameItems(second, third, "second relogin retains every attribute");
long playerId = Scalar("SELECT ID FROM characters WHERE ChrName='PersistenceFixture'");
Sql($"DELETE FROM characters_item_attr WHERE PlayerId={playerId} AND MakeIndex=7006");
var missing = Load();
Check(missing.Data.StorageItems[7].Desc.All(x => x == 0), "legacy item without attribute row defaults to zero without borrowing another item");
Check(missing.Data.BagItems[30].Desc.SequenceEqual(relocated.Desc), "missing unrelated attribute snapshot leaves upgraded weapon intact");
Sql($"INSERT INTO characters_item_attr(PlayerId,MakeIndex,Value0,Value1,Value2,Value3,Value4,Value5,Value6,Value7,Value8,Value9,Value10,Value11,Value12,Value13) SELECT PlayerId,MakeIndex,Value0,Value1,Value2,Value3,Value4,Value5,Value6,Value7,Value8,Value9,Value10,Value11,Value12,Value13 FROM characters_item_attr WHERE PlayerId={playerId} AND MakeIndex=7001");
var corruptReader = new PlayDataStorage(new StorageOption { ConnectionString = connectionString }); corruptReader.LoadQuickList();
CharacterDataInfo corrupt = null;
Check(!corruptReader.Get("PersistenceFixture", ref corrupt), "duplicate attribute records fail loading rather than silently overwrite");
Console.WriteLine($"TOTAL {checks} PASS; isolated synthetic data only");
