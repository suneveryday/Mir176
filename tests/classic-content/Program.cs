using System.Text.Json.Nodes;
using M2Server.Classic;
using M2Server.Items;
using M2Server.Player;
using OpenMir2;
using OpenMir2.Data;
using SystemModule;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
LogService.Logger = new Serilog.LoggerConfiguration().CreateLogger();
ClassicCatalog.Load(args[0]);
int count=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
void Reject(Action action,string name){try{action();throw new Exception("accepted: "+name);}catch(InvalidDataException){Check(true,name);}}
ClassicCatalog.RequireItem(115,"天魔神甲");ClassicCatalog.RequireSkill(33,"冰咆哮");ClassicCatalog.RequireMap("D616");
Reject(()=>ClassicCatalog.RequireItem(115,"天尊道袍"),"item identity mismatch rejected");
Reject(()=>ClassicCatalog.RequireSkill(99,"灭天火"),"later skill rejected");
Reject(()=>ClassicCatalog.RequireMap("H001"),"later map rejected");
var catalog=JsonNode.Parse(File.ReadAllText(args[0]))!;
catalog["items"]!.AsArray().Add(catalog["items"]![0]!.DeepClone());
string invalid=Path.Combine(AppContext.BaseDirectory,"invalid-policy.json");File.WriteAllText(invalid,catalog.ToJsonString());
Reject(()=>ClassicCatalog.Load(invalid),"duplicate catalog does not replace active policy");
ClassicCatalog.RequireItem(115,"天魔神甲");Check(true,"last complete policy retained after failed load");
var items=new GameItemSystem();for(int i=1;i<115;i++)items.AddItem(new StdItem{Name=""});items.AddItem(new StdItem{Name="天魔神甲",Weight=62});
Check(items.GetStdItem(114)==null && items.GetStdItem(115)?.Name=="天魔神甲" && items.GetStdItemIdx("天魔神甲")==115,"sparse item identifiers survive omitted later items");
Check(items.GetStdItemIdx("")==0 && items.GetStdItem("")==null,"empty reserved slots never become real items");
var player=new PlayObject();player.Abil.Level=48;player.Abil.Exp=0;player.Abil.MaxExp=int.MaxValue;typeof(PlayObject).GetMethod("GetExp",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(player,new object[]{int.MaxValue});
Check(player.Abil.Level==48 && player.Abil.Exp==0,"engineering level boundary prevents experience overflow");
Check(!player.RetainGmTeleportScroll(null),"classic scrolls remain consumable");
var elite=new M2Server.Monster.Monsters.CowKingMonster();
ClassicMonsterIdentity.Bind(elite,"祖玛卫士3");
Check(elite.ChrName=="祖玛卫士" && ClassicMonsterIdentity.TemplateName(elite)=="祖玛卫士3","elite display does not merge its template or loot identity");
// A fresh installation must tolerate absent, empty and blank binding lists.
string bindingDirectory=Path.Combine(Path.GetTempPath(),"classic-binding-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(bindingDirectory);string previousEnvir=SystemShare.Config.EnvirDir;
try {
 SystemShare.Config.EnvirDir=bindingDirectory;
 M2Server.M2Share.ItemBindIPaddr=new List<ItemBind>();
 M2Server.M2Share.ItemBindAccount=new List<ItemBind>();
 M2Server.M2Share.ItemBindChrName=new List<ItemBind>();
 var loaders=new (string File,Func<bool> Load,IList<ItemBind> Items)[]{
  ("ItemBindIPaddr.txt",M2Server.M2Share.LoadItemBindIPaddr,M2Server.M2Share.ItemBindIPaddr),
  ("ItemBindAccount.txt",M2Server.M2Share.LoadItemBindAccount,M2Server.M2Share.ItemBindAccount),
  ("ItemBindChrName.txt",M2Server.M2Share.LoadItemBindChrName,M2Server.M2Share.ItemBindChrName)};
 foreach(var loader in loaders){
  loader.Load();Check(File.Exists(Path.Combine(bindingDirectory,loader.File)),loader.File+" created on clean install");
  Check(loader.Load() && loader.Items.Count==0,loader.File+" empty file accepted");
  File.WriteAllText(Path.Combine(bindingDirectory,loader.File),"\r\n  \r\n; comment\r\n115 123 test-binding\r\n");
  Check(loader.Load() && loader.Items.Count==1 && loader.Items[0].nMakeIdex==123,loader.File+" skips blank and comment lines, keeps valid binding");
 }
} finally {SystemShare.Config.EnvirDir=previousEnvir;Directory.Delete(bindingDirectory,true);}
Console.WriteLine("TOTAL "+count+" PASS; offline objects only");
