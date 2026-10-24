using System.Reflection;
using System.Text.Json;
using GameSrv.Gm;
using GameSrv.Word;
using M2Server.Player;
using M2Server.Maps;
using OpenMir2;
using OpenMir2.Data;
using SystemModule;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
LogService.Logger = new Serilog.LoggerConfiguration().CreateLogger();
for (int i = 0; i <= byte.MaxValue; i++) SystemShare.Config.NeedExps[i] = 1000 + i * 100;
var apply = typeof(WorldServer).GetMethod("ApplyGmCommand", BindingFlags.NonPublic | BindingFlags.Static)!;
int count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
PlayObject Player(byte level = 10) {
    var p = new PlayObject { ChrName = "level-regression", Envir = new Envirnoment(), LoginNoticeOk = true };
    p.Abil.Level = level; p.WAbil.HP = 10;
    return p;
}
GmCommand Command(long amount = 0) => new() { Character = "level-regression", Action = "level_up", Amount = amount, CommandId = Guid.NewGuid().ToString("D") };
GmResult Apply(PlayObject p, GmCommand c) => (GmResult)apply.Invoke(null, new object[] { p, c })!;

foreach (var amount in new long[] { 0, 1, 5, 30 }) {
    var p = Player(); var c = Command(amount); var r = Apply(p, c);
    Check(r.Success && p.Abil.Level == 10 + (amount == 0 ? 1 : amount), "legacy/default and configurable increase " + amount);
    Check(r.Status == "applied_pending_save" && p.GmState.Commands.Count == 1, "one pending receipt " + amount);
    Check(p.WAbil.Level == p.Abil.Level && p.Abil.MaxExp == SystemShare.Config.NeedExps[p.Abil.Level], "abilities and experience threshold recalculated " + amount);
    int after = p.Abil.Level;
    Check(Apply(p, c).Success && p.Abil.Level == after && p.GmState.Commands.Count == 1, "same command retry never adds levels twice " + amount);
    c.Amount++;
    Check(Apply(p, c).Error == "command_conflict" && p.Abil.Level == after, "same ID with different increase rejected " + amount);
}
foreach (long amount in new[] { -1L, 255L, long.MaxValue }) {
    var p = Player(); var r = Apply(p, Command(amount));
    Check(r.Error == "invalid_command" && p.Abil.Level == 10 && p.GmState.Commands.Count == 0, "invalid/overflow quantity rejected atomically " + amount);
}
{
    var p = Player(); var c = Command(2); c.Enabled = true;
    Check(Apply(p, c).Error == "invalid_command" && p.Abil.Level == 10, "unrelated flag rejected");
}
{
    var p = Player(43); var c = Command(6);
    Check(Apply(p, c).Error == "level_limit" && p.Abil.Level == 43 && p.GmState.Commands.Count == 0, "over-limit request adds no partial levels");
    c.Amount = 5;
    Check(Apply(p, c).Success && p.Abil.Level == 48, "exact upper boundary accepted");
    Check(Apply(p, Command()).Error == "level_limit" && p.Abil.Level == 48, "already-max legacy increase rejected");
}
{
    var p = Player(1); var c = Command(47);
    Check(Apply(p, c).Success && p.Abil.Level == 48, "maximum increase accepted from level one");
    var id = Guid.Parse(c.CommandId).ToString("N");
    p.GmState.Commands[id].Revision = 7; p.GmAcknowledgedRevision = 7;
    Check(Apply(p, c).Status == "persisted" && p.Abil.Level == 48, "durable retry at level limit returns original receipt");
    var restored = JsonSerializer.Deserialize<GmCharacterState>(JsonSerializer.Serialize(p.GmState))!;
    p.GmState.Commands.Clear(); foreach (var pair in restored.Commands) p.GmState.Commands.Add(pair.Key, pair.Value);
    Check(Apply(p, c).Status == "persisted" && p.Abil.Level == 48, "serialized receipt retains quantity and prevents a second grant");
    var query = new GmCommand { Action = "inspect", Character = p.ChrName, CommandId = c.CommandId };
    Check(Apply(p, query).Status == "persisted" && p.Abil.Level == 48, "exact read-only confirmation retains level");
}
foreach (var mode in new[] { "dead", "transferring", "not-ready" }) {
    var p = Player(); p.Death = mode == "dead"; p.SwitchData = mode == "transferring"; p.LoginNoticeOk = mode != "not-ready";
    Check(Apply(p, Command(5)).Error == "character_unavailable" && p.Abil.Level == 10 && p.GmState.Commands.Count == 0, "unavailable character rejected " + mode);
}
Console.WriteLine("TOTAL " + count + " PASS; isolated in-memory characters only");
