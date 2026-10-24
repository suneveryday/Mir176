using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
if(args.Length!=2)throw new ArgumentException("Provide baseline and candidate gateway folders");
var oldContext=new Context(args[0]);var newContext=new Context(args[1]);
var oldAssembly=oldContext.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(args[0],"GameGate.dll")));
var newAssembly=newContext.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(args[1],"GameGate.dll")));
string Signature(MemberInfo m)=>m.MemberType+" "+m.Name+" "+m;
var before=oldAssembly.GetExportedTypes().ToDictionary(t=>t.FullName!);var after=newAssembly.GetExportedTypes().ToDictionary(t=>t.FullName!);var failures=new List<string>();int members=0;
if(!before.Keys.Order().SequenceEqual(after.Keys.Order()))failures.Add("Exported type set changed");
foreach(var p in before){if(!after.TryGetValue(p.Key,out var type))continue;var oldMembers=p.Value.GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Select(Signature).Order().ToArray();var newMembers=type.GetMembers(BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Select(Signature).Order().ToArray();members+=oldMembers.Length;if(!oldMembers.SequenceEqual(newMembers))failures.Add("Changed public members: "+p.Key);}
if(oldAssembly.GetName().FullName!=newAssembly.GetName().FullName)failures.Add("Assembly identity changed");
if(!oldAssembly.GetReferencedAssemblies().Select(n=>n.FullName).Order().SequenceEqual(newAssembly.GetReferencedAssemblies().Select(n=>n.FullName).Order()))failures.Add("Dependency identities changed");
Console.WriteLine(JsonSerializer.Serialize(new{baselineSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(args[0],"GameGate.dll")))),candidateSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(args[1],"GameGate.dll")))),exportedTypes=before.Count,members,failures}));
return failures.Count==0?0:1;
public class Context:AssemblyLoadContext {private readonly string directory;public Context(string directory):base(isCollectible:true){this.directory=Path.GetFullPath(directory);}protected override Assembly? Load(AssemblyName name){var p=Path.Combine(directory,name.Name+".dll");return File.Exists(p)?LoadFromAssemblyPath(p):null;}}
