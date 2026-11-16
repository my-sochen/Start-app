// PROPOSED source only. No UIA runtime reference or activation. Target DLLs are PE bytes.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

record CanonicalRow(int parent,string type,string Name,string ClassName,string AutomationId,int ProcessId,int NativeWindowHandle);
record Case(string id,CanonicalRow[] rows);
record Corpus(Case[] cases);
sealed class FixedStop:Exception { public string Code {get;} public FixedStop(string code){Code=code;} }
class Program {
 static readonly List<object> Records=new();
 static string Stage="START";
 static string? CurrentAssembly;
 static readonly Dictionary<string,string> TypeAssemblies=new() {
 ["System.Windows.Automation.CacheRequest"]="UIAutomationClient",
 ["System.Windows.Automation.TreeWalker"]="UIAutomationClient",
 ["System.Windows.Automation.AutomationElement"]="UIAutomationClient",
 ["System.Windows.Automation.Automation"]="UIAutomationClient",
 ["System.Windows.Automation.ValuePattern"]="UIAutomationClient",
 ["System.Windows.Automation.TogglePattern"]="UIAutomationClient",
 ["System.Windows.Automation.ControlType"]="UIAutomationTypes",
 ["System.Text.Json.JsonSerializer"]="System.Text.Json" };
 static readonly Dictionary<string,HashSet<string>> Allow=new() {
 ["System.Windows.Automation.CacheRequest"]=new(new[]{".ctor","Add","Activate","Clone","Push","Pop","get_TreeScope","set_TreeScope","get_TreeFilter","set_TreeFilter","get_AutomationElementMode","set_AutomationElementMode"}),
 ["System.Windows.Automation.TreeWalker"]=new(new[]{"GetFirstChild","GetNextSibling","ControlViewWalker","get_ControlViewWalker"}),
 ["System.Windows.Automation.AutomationElement"]=new(new[]{"GetCachedPropertyValue","GetUpdatedCache","GetRuntimeId","TryGetCachedPattern","GetCachedPattern","get_Current","get_Cached","ControlTypeProperty","ProcessIdProperty","NativeWindowHandleProperty","IsEnabledProperty","IsOffscreenProperty","ClassNameProperty","AutomationIdProperty","NameProperty","RuntimeIdProperty","IsValuePatternAvailableProperty","IsTogglePatternAvailableProperty"}),
 ["System.Windows.Automation.Automation"]=new(new[]{"Compare"}),
 ["System.Windows.Automation.ValuePattern"]=new(new[]{"Pattern","ValueProperty"}),
 ["System.Windows.Automation.TogglePattern"]=new(new[]{"Pattern","ToggleStateProperty"}),
 ["System.Windows.Automation.ControlType"]=new(new[]{"get_ProgrammaticName","Edit","Document","CheckBox","Text","Window","Pane"}),
 ["System.Text.Json.JsonSerializer"]=new(new[]{"Serialize"}) };
 static int Main(string[] args) {
  try {
   if(args.Length!=4)throw new FixedStop("ARGUMENTS");
   // args0 measured manifest, args1 corpus, args2 goldens, args3 measured manifest SHA. Runner captures stdout.
   Stage="INPUT_MANIFEST";
   byte[] manifestBytes=File.ReadAllBytes(args[0]);
   if(Convert.ToHexString(SHA256.HashData(manifestBytes))!=args[3])throw new FixedStop("MANIFEST_BYTES_CHANGED");
   using var manifest=JsonDocument.Parse(manifestBytes);
   var records=Records;var found=new HashSet<string>();var assemblyNames=new HashSet<string>();int members=0;Stage="PE_METADATA";
   if(manifest.RootElement.GetProperty("assemblies").GetArrayLength()!=6)throw new FixedStop("ASSEMBLY_COUNT");
   foreach(var input in manifest.RootElement.GetProperty("assemblies").EnumerateArray()) {
    string path=Path.GetFullPath(input.GetProperty("path").GetString()!);
    string nameExpected=input.GetProperty("assemblyName").GetString()!;
    string dirExpected=Path.GetFullPath(input.GetProperty("directory").GetString()!);
    if(Path.GetDirectoryName(path)!=dirExpected||Path.GetFileName(path)!=nameExpected+".dll")throw new FixedStop("ASSEMBLY_PATH_BINDING");
    if(!assemblyNames.Add(nameExpected))throw new FixedStop("DUPLICATE_ASSEMBLY_NAME");
    string group=(nameExpected=="UIAutomationClient"||nameExpected=="UIAutomationTypes")?"Microsoft.WindowsDesktop.App":"Microsoft.NETCore.App";
    if(!new[]{"System.Private.CoreLib","System.Runtime","System.Reflection.Metadata","System.Text.Json","UIAutomationClient","UIAutomationTypes"}.Contains(nameExpected))throw new FixedStop("UNEXPECTED_ASSEMBLY");
    string root=@"C:\Program Files\dotnet\shared\"+group+@"\";
    if(!dirExpected.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!System.Text.RegularExpressions.Regex.IsMatch(dirExpected.Substring(root.Length),"^10\\.0\\.[0-9]+$"))throw new FixedStop("ASSEMBLY_ROOT_BINDING");
    CurrentAssembly=nameExpected;var info=new FileInfo(path);
    if(info.Length>256*1024*1024)throw new FixedStop("FILE_CAP");
    byte[] bytes=File.ReadAllBytes(path);string hash=Convert.ToHexString(SHA256.HashData(bytes));
    if(hash!=input.GetProperty("sha256").GetString())throw new FixedStop("MEASURED_INPUT_CHANGED");
    using var stream=new MemoryStream(bytes,false);using var pe=new PEReader(stream);
    if(!pe.HasMetadata)throw new FixedStop("NO_METADATA");var md=pe.GetMetadataReader();var a=md.GetAssemblyDefinition();
    if(md.GetString(a.Name)!=nameExpected)throw new FixedStop("ASSEMBLY_NAME_BINDING");
    records.Add(new{kind="assembly",path,sha256=hash,name=md.GetString(a.Name),version=a.Version.ToString(),mvid=md.GetGuid(md.GetModuleDefinition().Mvid),machine=pe.PEHeaders.CoffHeader.Machine.ToString()});
    var sig=new Names();
    foreach(var th in md.TypeDefinitions) {
     var t=md.GetTypeDefinition(th);string full=md.GetString(t.Namespace)+"."+md.GetString(t.Name);
     if(!Allow.TryGetValue(full,out var names))continue;
     if(TypeAssemblies[full]!=nameExpected)throw new FixedStop("TYPE_ASSEMBLY_BINDING");
     foreach(var mh in t.GetMethods()) {
      var m=md.GetMethodDefinition(mh);string name=md.GetString(m.Name);if(!names.Contains(name)||(m.Attributes&MethodAttributes.MemberAccessMask)!=MethodAttributes.Public)continue;
      var v=m.DecodeSignature(sig,0);string text=v.ReturnType+" "+name+"("+string.Join(",",v.ParameterTypes)+")";
      if(text.Length>4096||++members>512)throw new FixedStop("MEMBER_CAP");
      string key=nameExpected+"|"+full+"|"+((m.Attributes&MethodAttributes.Static)!=0?"static":"instance")+"|"+v.GenericParameterCount+"|"+text;
      found.Add(key);records.Add(new{kind="method",assembly=nameExpected,type=full,signature=text,genericArity=v.GenericParameterCount,isStatic=(m.Attributes&MethodAttributes.Static)!=0});
     }
     foreach(var fh in t.GetFields()) {
      var f=md.GetFieldDefinition(fh);string name=md.GetString(f.Name);if(!names.Contains(name)||(f.Attributes&FieldAttributes.FieldAccessMask)!=FieldAttributes.Public)continue;
      string text=f.DecodeSignature(sig,0);if(text.Length>4096||++members>512)throw new FixedStop("MEMBER_CAP");records.Add(new{kind="field_declared_type_only",assembly=nameExpected,type=full,name,declaredType=text});
     }
    }
   }
   // Assembly-specific EXACT metadata signature checks. No runtime invocation.
   var required=new[]{
    "UIAutomationClient|System.Windows.Automation.CacheRequest|instance|0|Void Add(System.Windows.Automation.AutomationProperty)",
    "UIAutomationClient|System.Windows.Automation.CacheRequest|instance|0|System.IDisposable Activate()",
    "UIAutomationClient|System.Windows.Automation.TreeWalker|instance|0|System.Windows.Automation.AutomationElement GetFirstChild(System.Windows.Automation.AutomationElement,System.Windows.Automation.CacheRequest)",
    "UIAutomationClient|System.Windows.Automation.TreeWalker|instance|0|System.Windows.Automation.AutomationElement GetNextSibling(System.Windows.Automation.AutomationElement,System.Windows.Automation.CacheRequest)",
    "UIAutomationClient|System.Windows.Automation.AutomationElement|instance|0|Object GetCachedPropertyValue(System.Windows.Automation.AutomationProperty,Boolean)",
    "UIAutomationClient|System.Windows.Automation.AutomationElement|instance|0|Int32[] GetRuntimeId()",
    "UIAutomationClient|System.Windows.Automation.AutomationElement|instance|0|Boolean TryGetCachedPattern(System.Windows.Automation.AutomationPattern,Object&)",
    "UIAutomationClient|System.Windows.Automation.Automation|static|0|Boolean Compare(System.Windows.Automation.AutomationElement,System.Windows.Automation.AutomationElement)"};
   bool exactMetadata=true;
   foreach(string requirement in required){bool match=found.Contains(requirement);records.Add(new{kind="exact_signature_check",requirement,match});exactMetadata&=match;}
   // Record every mismatch, still permit independent JSON qualification in same bounded job.
   Stage="SYNTHETIC_JSON";CurrentAssembly=null;
   var corpus=JsonSerializer.Deserialize<Corpus>(File.ReadAllText(args[1]))!;
   if(corpus.cases.Length!=12)throw new FixedStop("CORPUS_COUNT");
   var extra=new[]{new Case("lone-high-surrogate-runtime-string",new[]{new CanonicalRow(-1,"ControlType.Text",new string((char)0xD800,1),"","",1,0)}),new Case("lone-low-surrogate-runtime-string",new[]{new CanonicalRow(-1,"ControlType.Text",new string((char)0xDC00,1),"","",1,0)})};
   var gold=File.ReadAllLines(args[2]).Select(l=>JsonDocument.Parse(l)).ToDictionary(d=>d.RootElement.GetProperty("id").GetString()!,d=>d.RootElement.GetProperty("utf8Base64").GetString()!);
   if(gold.Count!=14)throw new FixedStop("GOLDEN_COUNT");
   foreach(var c in corpus.cases.Concat(extra)) {
    var baseline=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(c.rows.Select(r=>new{parent=r.parent,type=r.type,r.Name,r.ClassName,r.AutomationId,r.ProcessId,r.NativeWindowHandle})));
    var candidate=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(c.rows));
    string b64=Convert.ToBase64String(baseline);bool projection=baseline.SequenceEqual(candidate);bool pinned=gold.TryGetValue(c.id,out var expected)&&expected==b64;
    records.Add(new{kind="json",c.id,projectionMatch=projection,pinnedLinuxGoldenMatch=pinned,utf8Base64=b64,sha256=Convert.ToHexString(SHA256.HashData(baseline)),bytes=baseline.Length});
    // Different current runtime golden is a reported incompatibility, not fallback/encoder change.
    if(!projection||!pinned) { Emit(records,"BYTE_MISMATCH");return 2; }
   }
   Emit(records,exactMetadata?"EXACT_METADATA_AND_JSON_MATCH_NOT_PROVIDER_PROOF":"METADATA_SIGNATURE_MISMATCH_JSON_MATCH");return exactMetadata?0:3;
  } catch(Exception ex) {
   string code=ex is FixedStop stop?stop.Code:"UNEXPECTED_EXCEPTION";
   EmitStop(code,ex.GetType().Name);return 1;
  }
 }
 static void EmitStop(string code,string errorClass) {
  // Keep bounded prefix, never discard a known fixed reason or include arbitrary exception text.
  var partial=Records.Take(512).ToList();
  string text=JsonSerializer.Serialize(new{outcome="STOP",code,errorClass,stage=Stage,assembly=CurrentAssembly,partial=true,records=partial});
  while(Encoding.UTF8.GetByteCount(text)>1900000&&partial.Count>0){partial.RemoveAt(partial.Count-1);text=JsonSerializer.Serialize(new{outcome="STOP",code,errorClass,stage=Stage,assembly=CurrentAssembly,partial=true,records=partial});}
  Console.WriteLine(text);
 }
 static void Emit(List<object> records,string outcome) {string s=JsonSerializer.Serialize(new{outcome,framework=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,architecture=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),records});if(Encoding.UTF8.GetByteCount(s)>2*1024*1024)throw new FixedStop("OUTPUT_CAP");Console.WriteLine(s);}
}
sealed class Names:ISignatureTypeProvider<string,int> {
 public string GetArrayType(string e,ArrayShape s)=>e+"[rank="+s.Rank+"]";
 public string GetByReferenceType(string e)=>e+"&";
 public string GetFunctionPointerType(MethodSignature<string> s)=>throw new FixedStop("FUNCTION_POINTER_SIGNATURE_UNSUPPORTED");
 public string GetGenericInstantiation(string t,ImmutableArray<string>a)=>t+"<"+string.Join(",",a)+">";
 public string GetGenericMethodParameter(int c,int i)=>"!!"+i;
 public string GetGenericTypeParameter(int c,int i)=>"!"+i;
 public string GetModifiedType(string m,string u,bool req)=>u+" mod("+m+")";
 public string GetPinnedType(string e)=>e+" pinned";
 public string GetPointerType(string e)=>e+"*";
 public string GetPrimitiveType(PrimitiveTypeCode t)=>t.ToString();
 public string GetSZArrayType(string e)=>e+"[]";
 public string GetTypeFromDefinition(MetadataReader r,TypeDefinitionHandle h,byte k){var t=r.GetTypeDefinition(h);return r.GetString(t.Namespace)+"."+r.GetString(t.Name);}
 public string GetTypeFromReference(MetadataReader r,TypeReferenceHandle h,byte k){var t=r.GetTypeReference(h);return r.GetString(t.Namespace)+"."+r.GetString(t.Name);}
 public string GetTypeFromSpecification(MetadataReader r,int c,TypeSpecificationHandle h,byte k)=>r.GetTypeSpecification(h).DecodeSignature(this,c);
}
