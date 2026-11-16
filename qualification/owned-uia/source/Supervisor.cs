using System.Diagnostics;
using System.Text.Json;
// Starts two retained owned children only. Raw byte drains discard all child text.
static class Supervisor {
 sealed record Child(Process Process,Task Stdout,Task Stderr);
 static readonly ExactOutputBudget output=new(65536);
 static readonly List<Child> children=new();
 static Child Start(string dotnet,string dll,params string[] args){
  var info=new ProcessStartInfo(dotnet){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};info.ArgumentList.Add(dll);foreach(var a in args)info.ArgumentList.Add(a);
  var p=new Process{StartInfo=info};if(!p.Start())throw new Exception();
  var child=new Child(p,output.Drain(p.StandardOutput.BaseStream),output.Drain(p.StandardError.BaseStream));children.Add(child);return child;
 }
 static bool Drained(int milliseconds)=>Task.WaitAll(children.SelectMany(c=>new[]{c.Stdout,c.Stderr}).ToArray(),milliseconds);
 static bool Run(string dotnet,string root,string dir,Stopwatch watch,out string code){
  code="SUPERVISOR_STOP";var fixture=Start(dotnet,Path.Combine(root,"Fixture.dll"),dir);var ready=Stopwatch.StartNew();bool seen=false;
  while(ready.Elapsed.TotalSeconds<5&&!fixture.Process.HasExited){if(output.Overflow)throw new Exception();try{using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"ack.json")));if(j.RootElement.GetProperty("pid").GetInt32()!=fixture.Process.Id)throw new Exception();seen=j.RootElement.GetProperty("step").GetInt32()==0;if(seen)break;}catch(IOException){}catch(JsonException){}Thread.Sleep(25);}
  if(!seen){code="FIXTURE_NOT_READY";return false;}
  string file=Path.Combine(root,"observation.json");if(File.Exists(file))throw new Exception();
  var observer=Start(dotnet,Path.Combine(root,"Observer.dll"),dir,fixture.Process.Id.ToString(),file);
  while(!observer.Process.WaitForExit(50)){
   if(output.Overflow){code="CHILD_OUTPUT_CAP";return false;}if(watch.Elapsed.TotalSeconds>32){code="OBSERVATION_DEADLINE";return false;}
   if(File.Exists(file)&&new FileInfo(file).Length>65536){code="OBSERVATION_OUTPUT_CAP";return false;}
  }
  if(observer.Process.ExitCode!=0){code="OBSERVER_FAILED";return false;}
  if(!File.Exists(file)||new FileInfo(file).Length>65536){code="OBSERVATION_OUTPUT_INVALID";return false;}
  using var result=JsonDocument.Parse(File.ReadAllText(file));if(result.RootElement.GetProperty("outcome").GetString()!="OWNED_TOPOLOGY_CHECKS_COMPLETED_CANONICAL_REPORTED_SEPARATELY")throw new Exception();
  if(!fixture.Process.WaitForExit(1000)||fixture.Process.ExitCode!=0){code="FIXTURE_CLEAN_EXIT_FAILED";return false;}
  string stop=Path.Combine(dir,"expected-stop.json");if(!File.Exists(stop)||new FileInfo(stop).Length>1024){code="EXPECTED_STOP_EVIDENCE_MISSING";return false;}
  using var stopped=JsonDocument.Parse(File.ReadAllText(stop));if(stopped.RootElement.GetProperty("pid").GetInt32()!=fixture.Process.Id||stopped.RootElement.GetProperty("stage").GetInt32()!=3||stopped.RootElement.GetProperty("code").GetString()!="EXPECTED_STOP"){code="EXPECTED_STOP_EVIDENCE_INVALID";return false;}
  // Both processes exited first; now settle all four pipes to EOF, then decide overflow.
  if(!Drained(1000)){code="CHILD_OUTPUT_EOF_TIMEOUT";return false;}
  if(output.Overflow){code="CHILD_OUTPUT_CAP";return false;}
  code="OWNED_TOPOLOGY_PASS_CANONICAL_SEPARATE";return true;
 }
 static int Main(string[] args){
  if(args.Length!=2)return 2;string root=Path.GetFullPath(args[1]);string dir=Path.Combine(root,"ipc-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  var watch=Stopwatch.StartNew();string code="SUPERVISOR_STOP";bool candidate=false,cleanup=true,eof=false,finalPass=false;int forced=0;int[] exits=[];
  try{candidate=Run(args[0],root,dir,watch,out code);}catch{code="SUPERVISOR_EXCEPTION";}
  finally{
   foreach(var child in children){try{if(!child.Process.HasExited){forced++;child.Process.Kill();if(!child.Process.WaitForExit(2000))cleanup=false;}}catch{cleanup=false;}}
   try{eof=Drained(1000);exits=children.Select(c=>c.Process.HasExited?c.Process.ExitCode:int.MinValue).ToArray();}catch{cleanup=false;}
   // Final predicate cannot be overwritten by late bytes or success-path early returns.
   bool pass=finalPass=FinalSupervisorVerdict.Pass(candidate,cleanup,eof,output.Overflow,forced,exits);
   if(candidate&&!pass)code="FINAL_CLEANUP_OR_OUTPUT_REFUSAL";
   File.WriteAllText(Path.Combine(root,"supervisor.json"),JsonSerializer.Serialize(new{outcome=code,pass,cleanupConfirmed=cleanup,allChildPipesAtEof=eof,forcedOwnedChildTerminations=forced,childExitCodes=exits,elapsedSeconds=watch.Elapsed.TotalSeconds,childRawPipeBytesObserved=output.Bytes,outputOverflow=output.Overflow}));
   foreach(var child in children)child.Process.Dispose();
  }
  return finalPass?0:1;
 }
}
