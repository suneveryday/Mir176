using System.Reflection;
using M2Server.Actor;
using M2Server.Maps;
using M2Server.Monster.Monsters;
using M2Server.Player;
using OpenMir2;
using OpenMir2.Data;
using OpenMir2.Enums;
using SystemModule;
using SystemModule.Data;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
LogService.Logger = new Serilog.LoggerConfiguration().CreateLogger();
var getMessage = typeof(BaseObject).GetMethod("GetMessage", BindingFlags.Instance | BindingFlags.NonPublic,
    null, new[] { typeof(ProcessMessage).MakeByRefType() }, null)!;
int checks = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    checks++;
    Console.WriteLine("PASS " + name);
}
List<ProcessMessage> Drain(BaseObject actor)
{
    var result = new List<ProcessMessage>();
    while (true)
    {
        object[] parameters = { new ProcessMessage() };
        if (!(bool)getMessage.Invoke(actor, parameters)!) return result;
        result.Add((ProcessMessage)parameters[0]);
    }
}
var monster = new CowKingMonster();
var player = new PlayObject();
player.SendMsg(monster, Messages.RM_PUSH, 2, 10, 10, 0);
player.SendMsg(monster, Messages.RM_DISAPPEAR, 0, 0, 0, 0);
var delivered = Drain(player).Select(x => x.wIdent).ToArray();
if (args.Contains("--expect-old"))
{
    Check(delivered.SequenceEqual(new[] { Messages.RM_DISAPPEAR, Messages.RM_PUSH }),
        "baseline delivers disappearance before the older push (ghost recreation trigger)");
    Console.WriteLine("BASELINE DEFECT REPRODUCED; no game session");
    return;
}
Check(delivered.SequenceEqual(new[] { Messages.RM_PUSH, Messages.RM_DISAPPEAR }),
    "older push is delivered before disappearance");

using (var map = new Envirnoment { Width = 60, Height = 60, MapName = "synthetic_visibility" })
{
    typeof(Envirnoment).GetProperty("CellArray", BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(map, Enumerable.Range(0, 3600).Select(_ => new MapCellInfo()).ToArray());
    var observer = new PlayObject { Envir = map, CurrX = 10, CurrY = 10 };
    var moving = new CowKingMonster { Envir = map, CurrX = 11, CurrY = 10 };
    SystemShare.ActorMgr.Add(observer);
    SystemShare.ActorMgr.Add(moving);
    map.AddMapObject(10, 10, CellType.Play, observer.ActorId, observer);
    map.AddMapObject(11, 10, CellType.Monster, moving.ActorId, moving);
    observer.SearchViewRange();
    Drain(observer);
    moving.SendRefMsg(Messages.RM_PUSH, 2, 11, 10, 0, "");
    map.DeleteFromMap(11, 10, CellType.Monster, moving.ActorId, moving);
    moving.CurrX = 45;
    moving.CurrY = 45;
    map.AddMapObject(45, 45, CellType.Monster, moving.ActorId, moving);
    observer.SearchViewRange();
    Check(Drain(observer).Where(x => x.ActorId == moving.ActorId).Select(x => x.wIdent)
        .SequenceEqual(new[] { Messages.RM_PUSH, Messages.RM_DISAPPEAR }),
        "real broadcast and view scan remove a monster moved out of view after a push");
}

// Mix actual world-loop, actor-broadcast and self-message entry points. Message IDs
// deliberately cross the old truncated-byte priority boundary.
var identifiers = new[] { Messages.RM_PUSH, Messages.RM_STRUCK, Messages.RM_TURN,
    Messages.RM_WALK, Messages.RM_SPACEMOVE_SHOW, Messages.RM_DISAPPEAR,
    Messages.RM_CLEAROBJECTS, Messages.RM_CHANGEMAP };
var expected = new List<int>();
for (int i = 0; i < 256; i++)
{
    int ident = identifiers[i % identifiers.Length];
    switch (i % 3)
    {
        case 0: player.SendMsg(monster, ident, 0, i, 0, 0); break;
        case 1: player.SendMsg(ident, 0, i, 0, 0); break;
        default: player.AddMessage(new SendMessage { wIdent = ident, nParam1 = i }); break;
    }
    expected.Add(i);
}
Check(Drain(player).Select(x => x.nParam1).SequenceEqual(expected),
    "256 mixed ordinary messages retain their causal order");

player.SendPriorityMsg(Messages.RM_TURN, 0, 1, 0, 0, Priority: MessagePriority.Low);
player.SendMsg(Messages.RM_TURN, 0, 2, 0, 0);
player.SendPriorityMsg(Messages.RM_TURN, 0, 3, 0, 0, Priority: MessagePriority.High);
player.SendPriorityMsg(Messages.RM_TURN, 0, 4, 0, 0, Priority: MessagePriority.High);
player.AddMessage(new SendMessage { wIdent = Messages.RM_TURN, nParam1 = 5 });
Check(Drain(player).Select(x => x.nParam1).SequenceEqual(new[] { 3, 4, 2, 5, 1 }),
    "explicit high/normal/low priorities remain effective and stable");

player.SendSelfDelayMsg(Messages.RM_TURN, 0, 6, 0, 0, "", 60_000);
player.SendMsg(Messages.RM_TURN, 0, 7, 0, 0);
Check(Drain(player).Select(x => x.nParam1).SequenceEqual(new[] { 7 }),
    "a future delayed message is not delivered and does not block ordinary messages");
player.SendMsg(Messages.RM_TURN, 0, 8, 0, 0);
Check(Drain(player).Select(x => x.nParam1).SequenceEqual(new[] { 8 }),
    "a requeued future message still permits later ordinary messages");

player = new PlayObject();
player.SendSelfDelayMsg(Messages.RM_TURN, 0, 9, 0, 0, "", 0);
player.SendMsg(Messages.RM_TURN, 0, 10, 0, 0);
Check(Drain(player).Select(x => x.nParam1).SequenceEqual(new[] { 10, 9 }),
    "due delayed messages run after ordinary messages");

player = new PlayObject();
Parallel.For(0, 4, producer =>
{
    for (int i = 0; i < 1000; i++)
        player.AddMessage(new SendMessage { wIdent = Messages.RM_TURN, nParam1 = producer, nParam2 = i });
});
var concurrent = Drain(player);
Check(concurrent.Count == 4000, "concurrent producers lose and duplicate no messages");
Check(Enumerable.Range(0, 4).All(producer => concurrent.Where(x => x.nParam1 == producer)
    .Select(x => x.nParam2).SequenceEqual(Enumerable.Range(0, 1000))),
    "concurrent producers retain each producer's enqueue order");
Console.WriteLine("TOTAL " + checks + " PASS; isolated synthetic actors only");
