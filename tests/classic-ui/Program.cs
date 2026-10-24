#nullable enable
using System.Reflection;
using M2Server;
using M2Server.Net;
using M2Server.Items;
using M2Server.Player;
using OpenMir2;
using OpenMir2.Data;
using OpenMir2.Packets.ClientPackets;
using OpenMir2.Packets.ServerPackets;
using SystemModule;
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
LogService.Logger = new Serilog.LoggerConfiguration().CreateLogger();
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
var channel=DispatchProxy.Create<INetChannel,CaptureChannel>();
var capture=(CaptureChannel)(object)channel;M2Share.NetChannel=channel;
var player=new PlayObject();
var send=typeof(PlayObject).GetMethod("SendSaveItemList",BindingFlags.NonPublic|BindingFlags.Instance)!;
CommandMessage Header(byte[] p)=>SerializerUtil.Deserialize<CommandMessage>(p.AsSpan(ServerMessage.PacketSize,CommandMessage.Size));
send.Invoke(player,new object[]{123});
Check(capture.Packets.Count==1,"empty warehouse emits one reply");
var head=Header(capture.Packets[0]);
Check(head.Ident==Messages.SM_SAVEITEMLIST && head.Recog==123 && head.Tag==0 && head.Series==0,"empty reply retains merchant and paging identity");
Check(capture.Packets[0].Length==ServerMessage.PacketSize+CommandMessage.Size,"empty warehouse uses a header-only frame, not the body-dropping path");
SystemShare.ItemSystem=new GameItemSystem();
((GameItemSystem)SystemShare.ItemSystem).AddItem(new StdItem{Name="fixture-item",StdMode=30});
for(int i=1;i<=51;i++)player.StorageItemList.Add(new UserItem{Index=1,MakeIndex=i,Dura=1000,DuraMax=2000});
capture.Packets.Clear();send.Invoke(player,new object[]{123});
Check(capture.Packets.Count==2,"large persisted warehouse responds in two chunks");
int[] counts=capture.Packets.Select(p=>HUtil32.GetString(p[(ServerMessage.PacketSize+CommandMessage.Size)..]).Count(c=>c=='/')).ToArray();
Check(counts.SequenceEqual(new[]{50,1}),"second chunk does not repeat the first fifty items");
Check(Header(capture.Packets[0]).Tag==0 && Header(capture.Packets[1]).Tag==1 && Header(capture.Packets[1]).Series==1,"nonempty page metadata preserved");
Check(player.StorageItemList.Count==51,"list request never changes stored items");
player.StorageItemList.Clear();player.StorageItemList.Add(new UserItem{Index=999,MakeIndex=700});capture.Packets.Clear();
send.Invoke(player,new object[]{123});Check(capture.Packets.Count==1 && capture.Packets[0].Length==32,"filtered invalid row still completes the request");
capture.Packets.Clear();
var takeOn=typeof(PlayObject).GetMethod("ClientTakeOnItems",BindingFlags.NonPublic|BindingFlags.Instance)!;
foreach(byte slot in new byte[]{10,11,12,13,14,15,16,255})takeOn.Invoke(player,new object[]{slot,1,"fixture-item"});
Check(capture.Packets.Count==8 && capture.Packets.All(p=>Header(p).Ident==Messages.SM_TAKEON_FAIL),"later equipment slots rejected with normal failure receipts");
Check(player.ItemList.Count==0 && player.StorageItemList.Count==1,"rejected extensions do not mutate bag or storage");
player.BonusPoint=10;player.BonusAbil.DC=2;
var operate=typeof(PlayObject).GetMethod("Operate",BindingFlags.NonPublic|BindingFlags.Instance)!;
operate.Invoke(player,new object[]{new ProcessMessage{wIdent=Messages.CM_ADJUST_BONUS,nParam1=100,Msg="invalid-payload"}});
Check(player.BonusPoint==10 && player.BonusAbil.DC==2,"free-point command is ignored without corrupting legacy fields");
Console.WriteLine("TOTAL "+checks+" PASS; isolated packet capture only");
public class CaptureChannel:DispatchProxy{
 public List<byte[]> Packets=new();
 protected override object? Invoke(MethodInfo? method,object?[]? args){
  if(method!.Name=="AddGateBuffer")Packets.Add((byte[])args![1]!);
  if(method.ReturnType==typeof(Task))return Task.CompletedTask;
  return null;
 }
}
