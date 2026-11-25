using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

class Source(ApprovedUpdate offer):IApprovedUpdateSource {public Task<ApprovedUpdate?> ReadAsync(CancellationToken t)=>Task.FromResult<ApprovedUpdate?>(offer);}
class Package(string path):IUpdatePackageTransport {public Task DownloadAsync(ApprovedUpdate u,string to,IProgress<int>? p,CancellationToken t){File.Copy(path,to);p?.Report(100);return Task.CompletedTask;}}
class Program {
 static List<object> Checks=[];
 static void Check(string name,bool ok){Checks.Add(new{name,ok});if(!ok)throw new Exception(name);}
 static string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
 static async Task<int> Main(string[] args){var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);try{
  var old=Path.Combine(root,"old");var latest=Path.Combine(root,"latest");var installed=Path.Combine(root,"installed");var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"StartApp","store.json");
  Check("isolated runner has no existing store",!File.Exists(data));Directory.CreateDirectory(Path.GetDirectoryName(data)!);
  using(var store=new JsonStore(data)){store.Mutate(c=>{var w=c.CreateWorkspace("upgrade retained workspace");c.SetCardDesign(w.Id,"#12AB34",null);});}
  var before=Hash(data);Copy(old,installed);
  Check("old beta assembly is stamped",System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(installed,"StartApp.App.dll")).ProductVersion!.StartsWith("0.2.0-beta.1+sha."));
  var running=Process.Start(new ProcessStartInfo(Path.Combine(installed,"StartApp.App.exe")){UseShellExecute=false})!;await Task.Delay(2000);Check("old installed beta launches",!running.HasExited);running.Kill();await running.WaitForExitAsync();Check("old app keeps user data",Hash(data)==before);
  var zip=Path.Combine(root,"latest.zip");ZipFile.CreateFromDirectory(latest,zip);var offer=new ApprovedUpdate("0.2.0-beta.9",new string('a',40),Hash(zip),$"1/2/{new FileInfo(zip).Length}/3");
  StagedUpdate? stage=null;using(var vm=new UpdateViewModel(new Source(offer),"0.2.0-beta.1",_=>throw new Exception("browser unexpectedly used"),revalidateBeforeDownload:false,packageTransport:new Package(zip),updateWorkRoot:Path.Combine(root,"updates"))){vm.StagedUpdateReady+=s=>stage=s;await vm.CheckAsync();Check("beta1 offers direct beta9 skipping intermediates",vm.UpdateAvailable);vm.DownloadCommand.Execute(null);await vm.CurrentCheck;Check("actual updater downloads verifies stages",stage!=null);}
  // Real product CLI mode and process-exit wait, with real product binary relaunch.
  var helper=Path.Combine(root,"helper");Copy(installed,helper);var waiting=Process.Start(new ProcessStartInfo("cmd.exe","/c ping -n 3 127.0.0.1 > nul"){CreateNoWindow=true})!;
  var apply=Process.Start(new ProcessStartInfo(Path.Combine(helper,"StartApp.App.exe"),$"--apply-update --staging \"{stage!.StagingDirectory}\" --target \"{installed}\" --wait-pid {waiting.Id} --relaunch \"{Path.Combine(installed,"StartApp.App.exe")}\""){UseShellExecute=false})!;
  await apply.WaitForExitAsync();Check("actual apply helper exits successful",apply.ExitCode==0);await Task.Delay(2000);
  var relaunched=Process.GetProcessesByName("StartApp.App").Where(x=>x.Id!=apply.Id).ToArray();Check("updated app relaunched",relaunched.Length==1);foreach(var p in relaunched){p.Kill();await p.WaitForExitAsync();}
  Check("latest beta bytes installed",Hash(Path.Combine(installed,"StartApp.App.dll"))==Hash(Path.Combine(latest,"StartApp.App.dll")));Check("data byte-identical after direct upgrade",Hash(data)==before);
  using(var store=new JsonStore(data)){var w=store.Load().Workspaces.Single();Check("workspace and custom color readable after update",w.Name=="upgrade retained workspace"&&w.Color=="#12AB34");}
  // Failure before staging: production hash check must reject and retain install/data.
  var prior=Hash(Path.Combine(installed,"StartApp.App.dll"));try{await UpdatePackageStager.StageAsync(offer with {Sha256=new string('0',64)},new Package(zip),Path.Combine(root,"bad-hash"),null,CancellationToken.None);throw new Exception("bad hash accepted");}catch(UpdatePackageVerificationException){Check("bad download rejected",true);}
  Check("failed download keeps installed binary and data",Hash(Path.Combine(installed,"StartApp.App.dll"))==prior&&Hash(data)==before);
  // Force swap failure after old install moved aside by using a target child as staging.
  var child=Path.Combine(installed,"staging-child");Directory.CreateDirectory(child);File.WriteAllText(Path.Combine(child,"marker"),"test");try{UpdateSwapper.ApplyStagedUpdate(child,installed);throw new Exception("expected rollback missing");}catch(DirectoryNotFoundException){Check("swap failure reached rollback",true);}
  Check("rollback restores previous binary and data",Hash(Path.Combine(installed,"StartApp.App.dll"))==prior&&Hash(data)==before);
  var afterRollback=Process.Start(new ProcessStartInfo(Path.Combine(installed,"StartApp.App.exe")){UseShellExecute=false})!;await Task.Delay(1500);Check("restored app launches after rollback",!afterRollback.HasExited);afterRollback.Kill();await afterRollback.WaitForExitAsync();
  return 0;
 }catch(Exception e){File.WriteAllText(Path.Combine(root,"error.txt"),e.ToString());return 1;}finally{File.WriteAllText(Path.Combine(root,"checks.json"),JsonSerializer.Serialize(Checks,new JsonSerializerOptions{WriteIndented=true}));}}
 static void Copy(string from,string to){Directory.CreateDirectory(to);foreach(var d in Directory.GetDirectories(from,"*",SearchOption.AllDirectories))Directory.CreateDirectory(Path.Combine(to,Path.GetRelativePath(from,d)));foreach(var f in Directory.GetFiles(from,"*",SearchOption.AllDirectories))File.Copy(f,Path.Combine(to,Path.GetRelativePath(from,f)),true);}
}
