using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using StartApp.Observation.Contracts;
using StartApp.Observation.Windows;
using StartApp.Observation.Uia;

// This executable is the proposed activation boundary. Never run during source preparation.
static class Observer {
 static readonly List<object> facts=new();static readonly Stopwatch time=Stopwatch.StartNew();
 static string? lastLocalRefusal;
 static void Need(bool value,string code){if(!value){lastLocalRefusal=code;throw new CheckFailure(code);}}
 sealed class CheckFailure(string code):Exception {public string Code=code;}
 static void Fact(string code,object value){if(facts.Count>=64)throw new CheckFailure("FACT_CAP");facts.Add(new{code,value});}
 static int Main(string[] args){if(args.Length!=3)return 2;string dir=args[0];int pid=int.Parse(args[1]);string output=args[2];int result=1;string state="STOP";
  try {var thread=new Thread(()=>{try{Run(dir,pid);state="OWNED_SYNTHETIC_CHECKS_COMPLETED_NOT_BROWSER_PROOF";result=0;}catch(CheckFailure e){state=e.Code;}catch{state="UNEXPECTED_OBSERVER_EXCEPTION";}});thread.SetApartmentState(ApartmentState.MTA);thread.Start();if(!thread.Join(TimeSpan.FromSeconds(24)))return 7;}
  finally {var json=JsonSerializer.Serialize(new{outcome=state,elapsedSeconds=time.Elapsed.TotalSeconds,facts});if(Encoding.UTF8.GetByteCount(json)<=65536)File.WriteAllText(output,json);}
  return result;
 }
 static void Command(string dir,string command,int step){
  File.WriteAllText(Path.Combine(dir,"command.txt"),command);var stop=Stopwatch.StartNew();
  while(stop.Elapsed.TotalSeconds<2){try{using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"ack.json")));if(j.RootElement.GetProperty("step").GetInt32()==step)return;}catch(IOException){}catch(JsonException){}Thread.Sleep(25);}
  throw new CheckFailure("FIXTURE_ACK_TIMEOUT");
 }
 sealed class OwnedWitness(int pid):IJobWitness {public Field<bool> IsMember(int seen,AcquisitionRequest request)=>Field<bool>.Present(seen==pid);}
 sealed class TracedTransport(UiaTransport inner,int pid):IWindowsTransport {
  public int Calls,Refreshes,IdentityMatches,RuntimeMatches;public int DesktopSeen;object? valueCache;public bool SawRaw,SawControl;
  readonly HashSet<object> ownedRaw=new(ReferenceEqualityComparer.Instance);
  void Step(){Need(++Calls<=4096,"TRANSPORT_CALL_CAP");Need(time.Elapsed.TotalSeconds<=20,"OBSERVER_BUDGET");}
  public object DesktopRoot(){Step();return inner.DesktopRoot();}
  public object? FirstChild(object e,NavigationView view,CacheStage stage){Step();SawRaw|=view==NavigationView.RawDesktopChildren;SawControl|=view==NavigationView.ControlDescendants;Need(stage.Properties.IsEmpty&&stage.Patterns.IsEmpty,"NAVIGATION_PREFETCH");var n=inner.FirstChild(e,view,stage);if(view==NavigationView.RawDesktopChildren&&n is not null)DesktopSeen++;return n;}
  public object? NextSibling(object e,NavigationView view,CacheStage stage){Step();var n=inner.NextSibling(e,view,stage);if(view==NavigationView.RawDesktopChildren&&n is not null)DesktopSeen++;return n;}
  public object Refresh(object e,CacheStage stage){Step();Need(stage.Scope==CacheScope.Element&&stage.Mode==CacheMode.Full&&stage.Filter==CacheFilter.Raw,"STAGE_POLICY");Need(stage.Properties.Length<=1&&stage.Patterns.Length<=1,"STAGE_READSET");var updated=inner.Refresh(e,stage);Refreshes++;
   // PID stages include foreign roots: never inspect their identity or emit their data.
   if(stage.Properties.Contains(PropertyKey.ProcessId)){var p=inner.CachedProperty(updated,PropertyKey.ProcessId,true);if(p.Status==TransportStatus.Present&&p.Value is int id&&id==pid)ownedRaw.Add(updated);}
   if(ownedRaw.Contains(e)||ownedRaw.Contains(updated)){
    Need(Automation.Compare((AutomationElement)e,(AutomationElement)updated),"REFRESH_LOGICAL_IDENTITY_CHANGED");IdentityMatches++;
    Need(((AutomationElement)e).GetRuntimeId().SequenceEqual(((AutomationElement)updated).GetRuntimeId()),"REFRESH_RUNTIME_ID_CHANGED");RuntimeMatches++;ownedRaw.Add(updated);
   }return updated;
  }
  public TransportValue CachedProperty(object e,PropertyKey p,bool ignore){Step();Need(ignore,"DEFAULT_FALLBACK");var result=inner.CachedProperty(e,p,ignore);if(p==PropertyKey.Value&&result.Status==TransportStatus.Present&&result.Value is string s&&s=="owned-value-A")valueCache=e;return result;}
  public void CheckFrozenCache(){Step();Need(valueCache is not null,"VALUE_CACHE_NOT_CAPTURED");var cached=inner.CachedProperty(valueCache!,PropertyKey.Value,true);Need(cached.Status==TransportStatus.Present&&cached.Value is string s&&s=="owned-value-A","CACHE_SILENT_CURRENT_FALLBACK");var omitted=inner.CachedProperty(valueCache!,PropertyKey.Name,true);Fact("OMITTED_CACHE_PROPERTY",new{status=omitted.Status.ToString()});Need(omitted.Status!=TransportStatus.Present,"OMITTED_CACHE_CURRENT_FALLBACK");}
  public TransportValue CachedPattern(object e,PatternKey p){Step();return inner.CachedPattern(e,p);}
  public void Dispose()=>inner.Dispose();
 }
 sealed class CapturedSource(IObservationSource source):IObservationSource {
  public CapturedSession? Last;
  public IObservationSession Open(AcquisitionRequest request)=>Last=new CapturedSession(source.Open(request));
 }
 sealed class CapturedSession(IObservationSession inner):IObservationSession {
  public readonly Dictionary<string,object> ById=new();
  public object? FirstDesktopRoot()=>inner.FirstDesktopRoot();public object? NextDesktopRoot(object n)=>inner.NextDesktopRoot(n);public object? FirstControlChild(object n)=>inner.FirstControlChild(n);public object? NextControlSibling(object n)=>inner.NextControlSibling(n);
  public Field<T> ReadCached<T>(object n,FieldId f){var value=inner.ReadCached<T>(n,f);if(f==FieldId.AutomationId&&value is Field<string> s&&s.TryGet(out var id)&&id is not null&&id.StartsWith("owned-",StringComparison.Ordinal)){Need(!ById.ContainsKey(id)||ReferenceEquals(ById[id],n),"DUPLICATE_SYNTHETIC_ID");ById[id]=n;}return value;}
  public Field<bool> InCurrentJob(int pid)=>inner.InCurrentJob(pid);public void Dispose()=>inner.Dispose();
 }
 static T Present<T>(IObservationSession session,object handle,FieldId field){var value=session.ReadCached<T>(handle,field);Need(value.TryGet(out var scalar),"REQUIRED_SYNTHETIC_FIELD_UNAVAILABLE");return scalar!;}
 static void Run(string dir,int pid){
  // Original baseline enumeration ONCE, separate from adapter. Foreign rows: PID only, never names/IDs.
  var baseline=AutomationElement.RootElement.FindAll(TreeScope.Children,Condition.TrueCondition);Need(baseline.Count<=256,"BASELINE_RESULT_CAP");var own=new List<AutomationElement>();
  foreach(AutomationElement e in baseline)if(e.Current.ProcessId==pid)own.Add(e);
  Need(own.Count>=2&&own.Count<=16,"BASELINE_OWNED_ROOT_COUNT");
  var rawOwned=new List<AutomationElement>();var walker=new TreeWalker(Condition.TrueCondition);var raw=walker.GetFirstChild(AutomationElement.RootElement);int count=0;
  while(raw is not null){Need(count++<256,"RAW_RESULT_CAP");if(raw.Current.ProcessId==pid)rawOwned.Add(raw);raw=walker.GetNextSibling(raw);}
  Need(rawOwned.Count==own.Count&&own.All(a=>rawOwned.Count(b=>Automation.Compare(a,b))==1),"BASELINE_RAW_OWNED_SET_DIFFERENCE");
  Fact("DESKTOP_COMPARISON",new{baselineCount=baseline.Count,rawCount=count,ownedBaseline=own.Count,ownedRaw=rawOwned.Count,foreignIdentityOrTextRecorded=false});
  TracedTransport? trace=null;var traces=new List<TracedTransport>();var source=new CapturedSource(new WindowsObservationSource(_=>{trace=new TracedTransport(new UiaTransport(),pid);traces.Add(trace);return trace;},new OwnedWitness(pid)));
  using var owner=new AcquisitionOwner(source,Stopwatch.GetTimestamp,new(desktopRoots:256));var acquired=owner.Acquire(Phase.PreConnect,Plan.CanonicalShape);
  if(lastLocalRefusal is not null)throw new CheckFailure(lastLocalRefusal);
  if(acquired is AcquisitionResult.Failure f){Fact("ACQUIRE_REFUSAL",new{code=f.Evidence.Code.ToString(),stage=f.Evidence.Stage.ToString(),field=f.Evidence.Field?.ToString(),status=f.Evidence.Status?.ToString()});throw new CheckFailure("INITIAL_ACQUISITION_REFUSED");}
  var ok=(AcquisitionResult.Success)acquired;var session=source.Last!;
  Need(session.ById.ContainsKey("owned-value")&&session.ById.ContainsKey("owned-toggle")&&session.ById.ContainsKey("owned-text"),"FIXTURE_IDS_MISSING");
  object value=session.ById["owned-value"],toggle=session.ById["owned-toggle"],text=session.ById["owned-text"];
  Need(Present<bool>(session,value!,FieldId.ValueAvailable),"VALUE_PATTERN_UNAVAILABLE");Need(Present<PatternType>(session,value!,FieldId.ValuePatternType)==PatternType.Value,"VALUE_PATTERN_TYPE");Need(Present<string>(session,value!,FieldId.Value)=="owned-value-A","INITIAL_VALUE");
  Need(Present<bool>(session,toggle!,FieldId.ToggleAvailable)&&Present<PatternType>(session,toggle!,FieldId.TogglePatternType)==PatternType.Toggle,"TOGGLE_PATTERN");Need(Present<int>(session,toggle!,FieldId.ToggleState)==0,"INITIAL_OFF");
  Need(!Present<bool>(session,text!,FieldId.ValueAvailable),"STATIC_TEXT_VALUE_AVAILABILITY");
  var unsupported=session.ReadCached<string>(text!,FieldId.Value);Fact("STATIC_TEXT_VALUE_STATUS",new{status=unsupported.Status.ToString()});Need(unsupported.Status!=FieldStatus.Present,"STATIC_TEXT_FAKE_DEFAULT");
  var digest=owner.CreateDigest(ok.Ticket,ok.Snapshot);Need(owner.VerifyDigest(ok.Ticket,ok.Snapshot,digest),"DIGEST_BINDING");
  Command(dir,"on",1);trace!.CheckFrozenCache();Need(Present<string>(session,value!,FieldId.Value)=="owned-value-B"&&Present<int>(session,toggle!,FieldId.ToggleState)==1,"REFRESH_AFTER_OWNED_UPDATE");
  Command(dir,"mixed",2);Need(Present<int>(session,toggle!,FieldId.ToggleState)==2,"INDETERMINATE_REFRESH");
  Fact("STAGED_READS_AND_REFRESH",new{trace!.Calls,trace.Refreshes,trace.IdentityMatches,trace.RuntimeMatches,trace.SawRaw,trace.SawControl,ok.Snapshot.Rows.Length,threeToggleStatesObserved=true,valueUpdateObserved=true,oldCacheRemainedA=true,omittedFieldDidNotReadCurrent=true});
  int before=trace.Calls;bool rejected=false;try{session.ReadCached<int>(new object(),FieldId.ProviderPid);}catch{rejected=true;}Need(rejected&&trace.Calls==before,"FOREIGN_HANDLE_NOT_REJECTED_LOCALLY");
  var thread=new Thread(()=>{try{session.ReadCached<string>(value!,FieldId.Name);}catch{rejected=true;}});rejected=false;thread.Start();Need(thread.Join(1000)&&rejected&&trace.Calls==before,"WRONG_THREAD_NOT_REJECTED_LOCALLY");
  Command(dir,"close",3);
  // Close handshake is UI dispatcher completion, not proof of provider invalidation.
  string stale;try{stale=session.ReadCached<string>(value!,FieldId.Value).Status.ToString();}
  catch(CheckFailure){throw;}
  catch(ElementNotAvailableException){stale="ELEMENT_NOT_AVAILABLE_EXCEPTION";}
  catch{Fact("AFTER_FIXTURE_CLOSE",new{status="UNCLASSIFIED_EXCEPTION",automaticRetry=false,providerInvalidationConfirmed=false});throw new CheckFailure("STALE_READ_UNCLASSIFIED_EXCEPTION");}
  if(lastLocalRefusal is not null)throw new CheckFailure(lastLocalRefusal);
  Fact("AFTER_FIXTURE_CLOSE",new{status=stale,automaticRetry=false,providerInvalidationConfirmed=false,meaning="single adapter unavailable-or-specific-element-exception observation, not cause proof"});Need(stale!=FieldStatus.Present.ToString(),"PROVIDER_STALE_PRESENT_AFTER_CLOSE");
  owner.Release(ok.Ticket);before=trace.Calls;rejected=false;try{session.ReadCached<string>(value!,FieldId.Name);}catch{rejected=true;}Need(rejected&&trace.Calls==before&&!owner.VerifyDigest(ok.Ticket,ok.Snapshot,digest),"RELEASE_INVALIDATION");
  var fresh=owner.Acquire(Phase.OffPrecheck,Plan.CanonicalShape);if(lastLocalRefusal is not null)throw new CheckFailure(lastLocalRefusal);Need(fresh is AcquisitionResult.Success,"FRESH_AFTER_CLOSE_ACQUISITION_REFUSED");var newOk=(AcquisitionResult.Success)fresh;Need(newOk.Snapshot.AcquisitionId!=ok.Snapshot.AcquisitionId,"ACQUISITION_ID_REUSE");Need(source.Last!.ById.Keys.Order().SequenceEqual(new[]{"owned-sentinel"}),"NEW_ACQUISITION_STALE_MAIN_CONTROLS");var survivingRoots=newOk.Snapshot.Rows.Where(r=>r.Parent==-1).ToArray();Need(survivingRoots.Length==1&&survivingRoots[0].Get<string>(FieldId.AutomationId).TryGet(out var survivingId)&&survivingId=="owned-sentinel","AFTER_CLOSE_SURVIVING_ROOTS");before=trace!.Calls;rejected=false;try{source.Last.ReadCached<string>(value!,FieldId.Name);}catch{rejected=true;}Need(rejected&&trace.Calls==before,"OLD_HANDLE_ACCEPTED_BY_NEW_SESSION");owner.Release(newOk.Ticket);
  Fact("FINAL_SESSION_CALL_COUNTS",new{perSessionLimit=4096,sessionCallCounts=traces.Select(t=>t.Calls).ToArray(),excludesBaselineIdentityAndInnerCacheCalls=true});
  Fact("LIFETIME",new{foreignAndWrongThreadBlockedBeforeTransport=true,releaseInvalidated=true,newAcquisitionAfterClose=true,oldHandleRejectedByNewSession=true});File.WriteAllText(Path.Combine(dir,"command.txt"),"stop");
 }
}
