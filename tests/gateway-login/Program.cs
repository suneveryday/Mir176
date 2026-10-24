using System.Reflection;
using System.Collections;
using GameGate;
using GameGate.Services;
using OpenMir2;
using OpenMir2.Packets.ServerPackets;
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
LogService.Logger = new Serilog.LoggerConfiguration().CreateLogger();
GameGate.Conf.ConfigManager.Instance.GateConfig.IsProcClientHardwareID = false;
GateShare.PunishList = new Dictionary<string,ClientSession>(StringComparer.OrdinalIgnoreCase);
var login = typeof(ClientSession).GetMethod("ClientLogin", BindingFlags.NonPublic | BindingFlags.Instance)!;
int count=0;
void Check(bool ok,string name) { if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name); }
(bool Accepted, ClientSession Session) Admit(string account,string name="测试角色",string certificate="7",string version="120020522",string code="1200205220",int? packetSize=null) {
 var session=new ClientSession(0,new SessionInfo {SessionId=731,SckHandle=44},null,null);
 string data=$"**{account}/{name}/{certificate}/{version}/{code}/0123456789abcdef";
 object[] args={data,packetSize ?? (int)Math.Ceiling(HUtil32.GetBytes(data).Length*4.0/3)+3,"",false};
 login.Invoke(session,args);return ((bool)args[3],session);
}
if(args.Contains("--expect-old")) {
 Check(!Admit("qa7").Accepted,"old gateway rejects a PC-valid three-character account");
 Check(Admit("qa77").Accepted,"old gateway control accepts four-character account");
 Console.WriteLine("TOTAL "+count+" PASS baseline reproduction; isolated memory only");return;
}
foreach(int length in new[]{3,4,10,12}) {
 var result=Admit(new string('q',length));Check(result.Accepted,"valid account length "+length);
 Check(result.Session.Session.Account==new string('q',length)&&result.Session.Session.ChrName=="测试角色","exact authenticated account and role preserved "+length);
 var queue=(IList)typeof(ClientSession).GetProperty("MsgList",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(result.Session)!;
 Check(queue.Count==1,"exactly one login handoff queued "+length);
 var message=(GameGate.DelayMessage)queue[0]!;
 var header=System.Runtime.InteropServices.MemoryMarshal.Read<ServerMessage>(message.Buffer);
 Check(header.SessionId==731&&header.Socket==44,"connection identity preserved "+length);
}
foreach(int length in new[]{0,1,2,13})Check(!Admit(new string('q',length)).Accepted,"invalid account length rejected "+length);
foreach(string name in new[]{"abc","测试角色",new string('n',14)})Check(Admit("qa7",name).Accepted,"valid role name length "+name.Length);
foreach(int length in new[]{0,1,2,15})Check(!Admit("qa7",new string('n',length)).Accepted,"invalid role name length rejected "+length);
Check(!Admit("qa7",certificate:"").Accepted,"empty certificate rejected");
Check(!Admit("qa7",certificate:"123456789").Accepted,"oversized certificate rejected");
Check(!Admit("qa7",version:"1234567").Accepted,"invalid version field rejected");
Check(!Admit("qa7",code:"123456789").Accepted,"invalid run-code field rejected");
Check(!Admit("qa7",packetSize:15).Accepted&&!Admit("qa7",packetSize:254).Accepted,"packet size guards unchanged");
Console.WriteLine("TOTAL "+count+" PASS actual game admission; isolated memory only, no accounts/sockets/database");
