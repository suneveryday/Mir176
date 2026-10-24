#nullable enable
using System.Reflection;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using GameSrv.Gm;
using GameSrv.Word;
using M2Server.Classic;
using M2Server.Items;
using M2Server.Maps;
using M2Server.Player;
using OpenMir2;
using OpenMir2.Data;
using SystemModule;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
LogService.Logger=new Serilog.LoggerConfiguration().CreateLogger();
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
Check(!ClassicCatalog.IsLoaded && !ClassicCatalog.AllowsItem(115,"天魔神甲") && !ClassicCatalog.AllowsMap("D616"),"catalog checks fail closed before loading");
ClassicCatalog.Load(args[0]);
Check(ClassicCatalog.ItemCount==264 && ClassicCatalog.SkillCount==33 && ClassicCatalog.MapCount==265,"complete classic policy counts exposed");
Check(ClassicCatalog.Sha256==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))).ToLowerInvariant(),"profile fingerprint binds actual loaded catalog bytes");
string directory=Path.Combine(Path.GetTempPath(),"classic-gm-identity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
try{
 string path=Path.Combine(directory,"instance.id");string id=ClassicGmProfile.LoadInstance(path);
 Check(Guid.TryParse(id,out var uuid)&&uuid!=Guid.Empty,"new independent world gets a nonempty identity");
 Check(ClassicGmProfile.LoadInstance(path)==id,"restart preserves world identity");
 Check(ClassicGmProfile.LoadInstance(Path.Combine(directory,"other.id"))!=id,"different world never inherits identity");
 Check(ClassicGmProfile.Matches("Mir2Classic176",uuid.ToString("D"),id),"GUID binding accepts equivalent representation");
 Check(!ClassicGmProfile.Matches("Mir2home",id,id)&&!ClassicGmProfile.Matches("Mir2Classic176",Guid.NewGuid().ToString(),id)&&!ClassicGmProfile.Matches("Mir2Classic176","",id),"wrong project, world or missing binding rejected");
 var profile=ClassicGmProfile.Describe(id);Check(profile.MaximumLevel==48&&profile.SkillCount==33&&profile.SupportedActions.Length==9&&!profile.SupportedActions.Contains("set_unlimited_teleport_scrolls"),"classic capabilities omit unlimited scrolls");
 File.WriteAllText(path,"invalid-identity");bool rejected=false;try{ClassicGmProfile.LoadInstance(path);}catch(InvalidDataException){rejected=true;}
 Check(rejected&&File.ReadAllText(path)=="invalid-identity","invalid identity is retained, never silently replaced");
}finally{Directory.Delete(directory,true);}

var apply=typeof(WorldServer).GetMethod("ApplyGmCommand",BindingFlags.Static|BindingFlags.NonPublic)!;
PlayObject Player()=>new(){ChrName="classic-regression",Envir=new Envirnoment(),LoginNoticeOk=true,Abil={HP=10,MaxHP=10},WAbil={HP=10,MaxHP=10}};
GmCommand Command(string action)=>new(){Character="classic-regression",Action=action,CommandId=Guid.NewGuid().ToString("D")};
GmResult Apply(PlayObject p,GmCommand c)=>(GmResult)apply.Invoke(null,[p,c])!;
var itemSystem=new GameItemSystem();itemSystem.AddItem(new StdItem{Name="later-item"});SystemShare.ItemSystem=itemSystem;
var p=Player();var grant=Command("add_item");grant.ItemIndex=1;grant.ItemName="later-item";
Check(Apply(p,grant).Error=="outside_classic_catalog"&&p.ItemList.Count==0&&p.GmState.Commands.Count==0,"GM cannot grant later item even if accidentally loaded");
var search=typeof(WorldServer).GetMethod("SearchGmItems",BindingFlags.Static|BindingFlags.NonPublic)!;
var found=(GmResult)search.Invoke(null,[new GmCommand{Query="later",MatchMode="contains"}])!;
Check(found.Success&&found.Items.Count==0&&found.TotalMatches==0,"item search excludes accidentally loaded later items");
var skill=Command("set_skill_level");skill.SkillId=99;skill.SkillLevel=3;
Check(Apply(p,skill).Error=="outside_classic_catalog"&&p.GmState.Commands.Count==0,"later skill mutation rejected without receipt");
skill.SkillId=33;Check(Apply(p,skill).Error=="skill_not_learned","classic unlearned skill retains existing error semantics");
var scroll=Command("set_unlimited_teleport_scrolls");scroll.Enabled=true;
Check(Apply(p,scroll).Error=="invalid_command"&&!p.GmState.UnlimitedTeleportScrolls&&p.GmState.Commands.Count==0,"classic unlimited scrolls remain disabled");
foreach(var action in new[]{"set_hp_lock","set_mp_lock"}){
 var c=Command(action);c.Enabled=true;Check(Apply(p,c).Success,"GM debug toggle retained "+action);
 int count=p.GmState.Commands.Count;Check(Apply(p,c).Success&&p.GmState.Commands.Count==count,"retry dedup retained "+action);
 c.Enabled=false;Check(Apply(p,c).Error=="command_conflict","changed retry payload rejected "+action);
}
// The optional HTTP listener runs only inside the isolated test container.
string httpHome=Path.Combine(Path.GetTempPath(),"classic-gm-http-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(httpHome);
string? previousToken=Environment.GetEnvironmentVariable("MIR2HOME_GM_TOKEN_FILE");
try{
 string secret=Path.Combine(httpHome,"token");File.WriteAllText(secret,new string('a',64));Environment.SetEnvironmentVariable("MIR2HOME_GM_TOKEN_FILE",secret);
 using var service=new GmHttpServer();await service.StartAsync(default);
 using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri("http://127.0.0.1:7210")};
 var response=await client.GetAsync("/api/classic/profile");Check(response.StatusCode==HttpStatusCode.Unauthorized,"classic profile requires authentication");
 client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string('a',64));
 response=await client.GetAsync("/api/classic/profile");var metadata=JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("server");
 Check(response.IsSuccessStatusCode&&metadata.GetProperty("catalogSha256").GetString()==ClassicCatalog.Sha256,"real isolated HTTP profile exposes loaded policy fingerprint");
 string instance=metadata.GetProperty("instanceId").GetString()!;
 response=await client.PostAsync("/api/classic/commands",new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));Check(response.StatusCode==HttpStatusCode.Conflict,"classic command endpoint requires explicit world binding");
 client.DefaultRequestHeaders.Add(ClassicGmProfile.ProjectHeader,ClassicGmProfile.Project);client.DefaultRequestHeaders.Add(ClassicGmProfile.InstanceHeader,Guid.NewGuid().ToString("N"));
 response=await client.GetAsync("/api/characters?name=test");Check(response.StatusCode==HttpStatusCode.Conflict,"bound read rejects a different world before queueing");
 client.DefaultRequestHeaders.Remove(ClassicGmProfile.InstanceHeader);client.DefaultRequestHeaders.Add(ClassicGmProfile.InstanceHeader,instance);
 response=await client.GetAsync("/api/characters?name=test");Check(response.StatusCode==HttpStatusCode.ServiceUnavailable,"matching bound read reaches unchanged world readiness guard");
 response=await client.PostAsync("/api/classic/commands",new StringContent("{",System.Text.Encoding.UTF8,"application/json"));Check(response.StatusCode==HttpStatusCode.BadRequest,"scoped endpoint preserves strict JSON validation");
 client.DefaultRequestHeaders.Remove(ClassicGmProfile.InstanceHeader);client.DefaultRequestHeaders.Remove(ClassicGmProfile.ProjectHeader);
 response=await client.GetAsync("/api/characters?name=test");Check(response.StatusCode==HttpStatusCode.ServiceUnavailable,"legacy v1 read remains compatible without binding headers");
 await service.StopAsync(default);
}finally{Environment.SetEnvironmentVariable("MIR2HOME_GM_TOKEN_FILE",previousToken);Directory.Delete(httpHome,true);}

if(args.Length>1){
 var host=Assembly.LoadFrom(args[1]).GetType("Program")!;
 var configure=host.GetMethod("ConfigureGm",BindingFlags.Static|BindingFlags.NonPublic)!;
 var copy=host.GetMethod("CopyTree",BindingFlags.Static|BindingFlags.NonPublic)!;
 string? enabledBefore=Environment.GetEnvironmentVariable("CLASSIC_GM_ENABLED"),pathBefore=Environment.GetEnvironmentVariable("CLASSIC_GM_TOKEN_FILE");
 string home=Path.Combine(Path.GetTempPath(),"classic-gm-host-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(home);
 ProcessStartInfo Info()=>new("dotnet");
 void Configure(ProcessStartInfo info,string name)=>configure.Invoke(null,[info,name]);
 bool RejectConfig(){try{Configure(Info(),"GameSrv");return false;}catch(TargetInvocationException e)when(e.InnerException is InvalidDataException){return true;}}
 try{
  foreach(string? mode in new string?[]{null,"false"}){
   Environment.SetEnvironmentVariable("CLASSIC_GM_ENABLED",mode);var info=Info();info.Environment["MIR2HOME_GM_TOKEN_FILE"]="inherited-unused";Configure(info,"GameSrv");Check(!info.Environment.ContainsKey("MIR2HOME_GM_TOKEN_FILE"),"GM disabled and inherited token removed: "+(mode??"unset"));
  }
  Environment.SetEnvironmentVariable("CLASSIC_GM_ENABLED","yes");Check(RejectConfig(),"ambiguous GM opt-in fails closed");
  Environment.SetEnvironmentVariable("CLASSIC_GM_ENABLED","true");Environment.SetEnvironmentVariable("CLASSIC_GM_TOKEN_FILE",null);Check(RejectConfig(),"enabled GM requires separate secret");
  Environment.SetEnvironmentVariable("CLASSIC_GM_TOKEN_FILE","relative-token");Check(RejectConfig(),"relative secret path rejected");
  string token=Path.Combine(home,"token");Environment.SetEnvironmentVariable("CLASSIC_GM_TOKEN_FILE",token);File.WriteAllText(token,"short");Check(RejectConfig(),"short secret rejected");
  File.WriteAllText(token,new string('a',63)+"!");Check(RejectConfig(),"invalid secret character rejected");File.WriteAllText(token,new string('a',64));
  foreach(string name in new[]{"LoginSrv","DBSrv","GameSrv","LoginGate","SelGate","GameGate"}){var info=Info();info.Environment["MIR2HOME_GM_TOKEN_FILE"]="inherited-unused";Configure(info,name);Check(info.Environment.TryGetValue("MIR2HOME_GM_TOKEN_FILE",out var supplied)==(name=="GameSrv")&&(name!="GameSrv"||supplied==token),"explicit secret scoped only to GameSrv: "+name);}
  string source=Path.Combine(home,"source"),target=Path.Combine(home,"target");Directory.CreateDirectory(Path.Combine(source,"GameSrv"));Directory.CreateDirectory(Path.Combine(target,"GameSrv"));
  File.WriteAllText(Path.Combine(source,"GameSrv/classic-gm-instance.id"),"must-not-copy");File.WriteAllText(Path.Combine(source,"GameSrv/program.txt"),"rebuilt");
  copy.Invoke(null,[source,target]);Check(!File.Exists(Path.Combine(target,"GameSrv/classic-gm-instance.id"))&&File.ReadAllText(Path.Combine(target,"GameSrv/program.txt"))=="rebuilt","package copy excludes foreign world identity");
  File.WriteAllText(Path.Combine(target,"GameSrv/classic-gm-instance.id"),"existing-world");copy.Invoke(null,[source,target]);Check(File.ReadAllText(Path.Combine(target,"GameSrv/classic-gm-instance.id"))=="existing-world","package refresh preserves existing world identity");
 }finally{Environment.SetEnvironmentVariable("CLASSIC_GM_ENABLED",enabledBefore);Environment.SetEnvironmentVariable("CLASSIC_GM_TOKEN_FILE",pathBefore);Directory.Delete(home,true);}
}
Console.WriteLine("TOTAL "+checks+" PASS; in-memory actors, isolated loopback listener and host configuration only; no game deployment");
