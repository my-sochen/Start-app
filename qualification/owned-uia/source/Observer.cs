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
 static readonly DiagnosticJournal journal=new();static readonly DiagnosticContext diagnostic=new(journal,()=>time.Elapsed.TotalSeconds);
 static string? lastLocalRefusal=>diagnostic.LastLocalRefusal;
 static void Mark(string checkpoint)=>journal.Checkpoint(checkpoint);
 static string Scalar<T>(FieldId field,T value)=>field switch {
  FieldId.ProviderPid or FieldId.ShapeProcessId or FieldId.Hwnd or FieldId.RuntimeId=>"IDENTITY_SUPPRESSED",
  FieldId.ControlTypeId=>value is int type&&type is >=50000 and <=50050?"TYPE_"+type:"OTHER",
  FieldId.ToggleState=>value is int toggle&&toggle is >=0 and <=2?"TOGGLE_"+toggle:"OTHER",
  FieldId.ValueAvailable or FieldId.ToggleAvailable=>value is bool b?b?"TRUE":"FALSE":"OTHER",
  FieldId.ValuePatternType or FieldId.TogglePatternType=>value is PatternType pattern?pattern.ToString():"OTHER",
  FieldId.Value=>value is string text?text=="owned-value-A"?"VALUE_A":text=="owned-value-B"?"VALUE_B":"OTHER_STRING":"OTHER",
  FieldId.AutomationId=>value is string id?DiagnosticJournal.Id(id):"UNKNOWN",
  _=>"SUPPRESSED"
 };
 static void Need(bool value,string code){if(!value){diagnostic.Fail(code);throw new CheckFailure(code);}}
 sealed class CheckFailure(string code):Exception {public string Code=code;}
 static void Fact(string code,object value){if(facts.Count>=64){diagnostic.Fail("FACT_CAP");throw new CheckFailure("FACT_CAP");}facts.Add(new{code,value});}
 static int Main(string[] args){if(args.Length!=3)return 2;string dir=args[0];int pid=int.Parse(args[1]);string output=args[2];int result=1;string state="STOP";bool joined=false;Mark("OBSERVER_START");
  try {var thread=new Thread(()=>{try{Run(dir,pid);state="OWNED_TOPOLOGY_CHECKS_COMPLETED_CANONICAL_REPORTED_SEPARATELY";result=0;}catch(CheckFailure e){state=e.Code;}catch(Exception e){journal.Record(time.Elapsed.TotalSeconds,"OBSERVER","UNKNOWN","NA","EXCEPTION",DiagnosticJournal.ExceptionToken(e));state="UNEXPECTED_OBSERVER_EXCEPTION";}});thread.SetApartmentState(ApartmentState.MTA);thread.Start();joined=thread.Join(TimeSpan.FromSeconds(24));if(!joined){journal.Record(time.Elapsed.TotalSeconds,"OBSERVER_JOIN","UNKNOWN","NA","EXCEPTION","THREAD_TIMEOUT");return 7;}}
  finally {try{var json=JsonSerializer.Serialize(new{outcome=state,elapsedSeconds=time.Elapsed.TotalSeconds,reportComplete=joined,facts=joined?facts.ToArray():Array.Empty<object>(),diagnostics=journal.Snapshot()});if(Encoding.UTF8.GetByteCount(json)>65536){result=1;state="OBSERVER_REPORT_CAP";diagnostic.Fail(state);json=JsonSerializer.Serialize(new{outcome=state,reportComplete=false,diagnostics=journal.Snapshot()});}File.WriteAllText(output,json);}catch{result=1;throw;}}
  return result;
 }
 static void Command(string dir,string command,int step){
  Mark("COMMAND_"+command.ToUpperInvariant());journal.Record(time.Elapsed.TotalSeconds,"IPC_COMMAND","UNKNOWN","NA","BEGIN","NA","STAGE_"+step);
  File.WriteAllText(Path.Combine(dir,"command.txt"),command);var stop=Stopwatch.StartNew();
  while(stop.Elapsed.TotalSeconds<2){try{using var j=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"ack.json")));int seen=j.RootElement.GetProperty("step").GetInt32();journal.Record(time.Elapsed.TotalSeconds,"IPC_ACK","UNKNOWN","NA","RETURN","PARSED","STAGE_"+step,seen is >=0 and <=3?"STAGE_"+seen:"INVALID_STAGE");if(seen==step)return;}catch(IOException){journal.Record(time.Elapsed.TotalSeconds,"IPC_ACK","UNKNOWN","NA","EXCEPTION","IO_EXCEPTION");}catch(JsonException){journal.Record(time.Elapsed.TotalSeconds,"IPC_ACK","UNKNOWN","NA","EXCEPTION","JSON_PARSE");}Thread.Sleep(25);}
  Need(false,"FIXTURE_ACK_TIMEOUT");
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
  public void CheckFrozenCache(){Mark("FROZEN_CACHE");Step();Need(valueCache is not null,"VALUE_CACHE_NOT_CAPTURED");journal.Record(time.Elapsed.TotalSeconds,"FROZEN_VALUE","owned-value","Value","BEGIN","NA");var cached=inner.CachedProperty(valueCache!,PropertyKey.Value,true);journal.Record(time.Elapsed.TotalSeconds,"FROZEN_VALUE","owned-value","Value","RETURN",cached.Status.ToString(),"VALUE_A",cached.Value is string cv&&cv=="owned-value-A"?"VALUE_A":"OTHER");Need(cached.Status==TransportStatus.Present&&cached.Value is string s&&s=="owned-value-A","CACHE_SILENT_CURRENT_FALLBACK");journal.Record(time.Elapsed.TotalSeconds,"OMITTED_NAME","owned-value","Name","BEGIN","NA");var omitted=inner.CachedProperty(valueCache!,PropertyKey.Name,true);journal.Record(time.Elapsed.TotalSeconds,"OMITTED_NAME","owned-value","Name","RETURN",omitted.Status.ToString(),"NON_PRESENT");Fact("OMITTED_CACHE_PROPERTY",new{status=omitted.Status.ToString()});Need(omitted.Status!=TransportStatus.Present,"OMITTED_CACHE_CURRENT_FALLBACK");}
  public TransportValue CachedPattern(object e,PatternKey p){Step();return inner.CachedPattern(e,p);}
  public void Dispose()=>inner.Dispose();
 }
 static readonly HashSet<string> KnownIds=new(new[]{"owned-main","owned-sentinel","owned-value","owned-toggle","owned-text"},StringComparer.Ordinal);
 sealed class CapturedSource(IObservationSource source,int pid):IObservationSource {int next;
  public CapturedSession? Last;
  public IObservationSession Open(AcquisitionRequest request)=>Last=new CapturedSession(source.Open(request),pid,request.Plan==Plan.CanonicalShape,++next);
 }
 sealed class CapturedSession(IObservationSession inner,int pid,bool canonical,int sessionOrdinal):IObservationSession {
  public readonly Dictionary<string,object> ById=new();
  readonly Dictionary<object,int> owned=new(ReferenceEqualityComparer.Instance);
  readonly HashSet<object> desktop=new(ReferenceEqualityComparer.Instance);
  public readonly HashSet<object> OwnedDesktop=new(ReferenceEqualityComparer.Instance);
  public object? FailureAttribution;bool attributed;readonly int ownerThread=Environment.CurrentManagedThreadId;
  object? Root(object? n){if(n is not null)desktop.Add(n);return n;}
  object? Nav(string operation,Func<object?> action){journal.Record(time.Elapsed.TotalSeconds,operation,"UNKNOWN","NA","BEGIN","NA",session:sessionOrdinal);try{var n=action();journal.Record(time.Elapsed.TotalSeconds,operation,"UNKNOWN","NA","RETURN",n is null?"END":"NODE",session:sessionOrdinal);return n;}catch(Exception e){journal.Record(time.Elapsed.TotalSeconds,operation,"UNKNOWN","NA","EXCEPTION",DiagnosticJournal.ExceptionToken(e),session:sessionOrdinal);throw;}}
  public object? FirstDesktopRoot()=>Root(Nav("FIRST_DESKTOP",inner.FirstDesktopRoot));public object? NextDesktopRoot(object n)=>Root(Nav("NEXT_DESKTOP",()=>inner.NextDesktopRoot(n)));public object? FirstControlChild(object n)=>Nav("FIRST_CONTROL",()=>inner.FirstControlChild(n));public object? NextControlSibling(object n)=>Nav("NEXT_CONTROL",()=>inner.NextControlSibling(n));
  public Field<T> ReadCached<T>(object n,FieldId f){
   if(Environment.CurrentManagedThreadId!=ownerThread)return inner.ReadCached<T>(n,f);
   string token=ById.FirstOrDefault(x=>ReferenceEquals(x.Value,n)).Key??"UNKNOWN";int? ordinalBefore=owned.TryGetValue(n,out var oi)?oi:null;
   journal.Record(time.Elapsed.TotalSeconds,"FIELD_READ",token,f.ToString(),"BEGIN","NOT_RETURNED",session:sessionOrdinal,handle:ordinalBefore);
   Field<T> value;try{value=inner.ReadCached<T>(n,f);}catch(Exception e){journal.Record(time.Elapsed.TotalSeconds,"FIELD_READ",token,f.ToString(),"EXCEPTION",DiagnosticJournal.ExceptionToken(e),session:sessionOrdinal,handle:ordinalBefore);throw;}
   journal.Record(time.Elapsed.TotalSeconds,"FIELD_READ",token,f.ToString(),"RETURN",value.Status.ToString(),observed:value.TryGet(out var scalar)?Scalar(f,scalar):"UNAVAILABLE",session:sessionOrdinal,handle:ordinalBefore);
   if(f==FieldId.ProviderPid&&value is Field<int> p&&p.TryGet(out int seen)&&seen==pid){if(!owned.ContainsKey(n))owned.Add(n,owned.Count);if(desktop.Contains(n))OwnedDesktop.Add(n);}
   if(f==FieldId.AutomationId&&value is Field<string> idField&&idField.TryGet(out var id)&&id is not null&&KnownIds.Contains(id)){Need(owned.ContainsKey(n),"ID_WITHOUT_OWNED_PID");Need(!ById.ContainsKey(id)||ReferenceEquals(ById[id],n),"DUPLICATE_SYNTHETIC_ID");ById[id]=n;}
   if(canonical&&value.Status!=FieldStatus.Present&&!attributed){
    attributed=true;bool isOwned=owned.TryGetValue(n,out int ordinal);string idStatus="NOT_READ",typeStatus="NOT_READ",idToken="UNKNOWN";int? controlType=null;
    // Supplemental diagnostics only after the original failed read, at most two fields.
    // Never repeat the failed field, never read foreign identity/text, never change its status.
    if(isOwned){
     if(f!=FieldId.AutomationId){try{var x=Supplement<string>(n,FieldId.AutomationId,ordinal);idStatus=x.Status.ToString();if(x.TryGet(out var v)&&v is not null&&KnownIds.Contains(v))idToken=v;}catch{idStatus="EXCEPTION";}}
     if(f!=FieldId.ControlTypeId){try{var x=Supplement<int>(n,FieldId.ControlTypeId,ordinal);typeStatus=x.Status.ToString();if(x.TryGet(out int v)&&v>=50000&&v<=50050)controlType=v;}catch{typeStatus="EXCEPTION";}}
    }
    FailureAttribution=new{fixturePidConfirmed=isOwned,ownedHandleOrdinal=isOwned?(int?)ordinal:null,originalField=f.ToString(),originalStatus=value.Status.ToString(),idStatus,idToken,typeStatus,controlType,supplementalReadLimit=2,failedFieldRetried=false};
   }
   return value;
  }
  Field<T> Supplement<T>(object n,FieldId f,int ordinal){journal.Record(time.Elapsed.TotalSeconds,"SUPPLEMENTAL_READ","UNKNOWN",f.ToString(),"BEGIN","NOT_RETURNED",session:sessionOrdinal,handle:ordinal);try{var v=inner.ReadCached<T>(n,f);journal.Record(time.Elapsed.TotalSeconds,"SUPPLEMENTAL_READ","UNKNOWN",f.ToString(),"RETURN",v.Status.ToString(),observed:v.TryGet(out var scalar)?Scalar(f,scalar):"UNAVAILABLE",session:sessionOrdinal,handle:ordinal);return v;}catch(Exception e){journal.Record(time.Elapsed.TotalSeconds,"SUPPLEMENTAL_READ","UNKNOWN",f.ToString(),"EXCEPTION",DiagnosticJournal.ExceptionToken(e),session:sessionOrdinal,handle:ordinal);throw;}}
  public void ReadIdsForOwned(int rows){Need(owned.Count==rows,"TOPOLOGY_OWNED_HANDLE_COUNT");foreach(var n in owned.Keys.ToArray())ReadCached<string>(n,FieldId.AutomationId);}
  public Field<bool> InCurrentJob(int seen){journal.Record(time.Elapsed.TotalSeconds,"PID_WITNESS","UNKNOWN","NA","BEGIN","NA",session:sessionOrdinal);try{var v=inner.InCurrentJob(seen);journal.Record(time.Elapsed.TotalSeconds,"PID_WITNESS","UNKNOWN","NA","RETURN",v.Status.ToString(),observed:v.TryGet(out var member)?member?"TRUE":"FALSE":"UNAVAILABLE",session:sessionOrdinal);return v;}catch(Exception e){journal.Record(time.Elapsed.TotalSeconds,"PID_WITNESS","UNKNOWN","NA","EXCEPTION",DiagnosticJournal.ExceptionToken(e),session:sessionOrdinal);throw;}}public void Dispose(){journal.Record(time.Elapsed.TotalSeconds,"SESSION_DISPOSE","UNKNOWN","NA","BEGIN","NA",session:sessionOrdinal);try{inner.Dispose();journal.Record(time.Elapsed.TotalSeconds,"SESSION_DISPOSE","UNKNOWN","NA","RETURN","DISPOSED",session:sessionOrdinal);}catch(Exception e){journal.Record(time.Elapsed.TotalSeconds,"SESSION_DISPOSE","UNKNOWN","NA","EXCEPTION",DiagnosticJournal.ExceptionToken(e),session:sessionOrdinal);throw;}}
 }
 static void ReportFailure(string label,AcquisitionResult.Failure f,CapturedSession? session){var e=f.Evidence;Fact(label,new{phase=e.Phase.ToString(),plan=e.Plan.ToString(),code=e.Code.ToString(),stage=e.Stage.ToString(),field=e.Field?.ToString(),status=e.Status?.ToString(),e.NodeIndex,e.DesktopOrdinal,attribution=session?.FailureAttribution});}
 static void CanonicalProbe(AcquisitionOwner owner,CapturedSource source,Phase phase){Mark("CANONICAL_"+phase);
  var result=owner.Acquire(phase,Plan.CanonicalShape);if(lastLocalRefusal is not null)throw new CheckFailure(lastLocalRefusal);
  if(result is AcquisitionResult.Failure f){ReportFailure("CANONICAL_REFUSAL",f,source.Last);Need(f.Evidence.Code==FailureCode.RequiredUnavailable&&f.Evidence.Stage==Stage.Shape&&f.Evidence.Status==FieldStatus.Unsupported,"CANONICAL_NON_SUPPORT_UNRELATED_FAILURE");Fact("CANONICAL_SCOPE",new{phase=phase.ToString(),ready=false,digestCreated=false,effectAuthorized=false});return;}
  var ok=(AcquisitionResult.Success)result;var digest=owner.CreateDigest(ok.Ticket,ok.Snapshot);Need(owner.VerifyDigest(ok.Ticket,ok.Snapshot,digest),"CANONICAL_DIGEST_BINDING");owner.Release(ok.Ticket);Need(!owner.VerifyDigest(ok.Ticket,ok.Snapshot,digest),"CANONICAL_DIGEST_RELEASE");Fact("CANONICAL_SCOPE",new{phase=phase.ToString(),ready=true,digestCreated=true,effectAuthorized=false});
 }
 static void ReadOwnedIds(CapturedSession session,Snapshot snapshot){
  // Provider PID reads established owned handles; every explicit read is still through actual adapter.
  session.ReadIdsForOwned(snapshot.Rows.Length);
 }
 static T Present<T>(IObservationSession session,object handle,FieldId field){var value=session.ReadCached<T>(handle,field);if(!value.TryGet(out var scalar)){journal.Record(time.Elapsed.TotalSeconds,"REQUIRE_PRESENT","UNKNOWN",field.ToString(),"ASSERTION_FAILED",value.Status.ToString(),"PRESENT");Need(false,"REQUIRED_SYNTHETIC_FIELD_UNAVAILABLE");}return scalar!;}
 static void Run(string dir,int pid){
  // Original baseline enumeration ONCE, separate from adapter. Foreign rows: PID only, never names/IDs.
  Mark("BASELINE");var baseline=AutomationElement.RootElement.FindAll(TreeScope.Children,Condition.TrueCondition);Need(baseline.Count<=256,"BASELINE_RESULT_CAP");var own=new List<AutomationElement>();
  foreach(AutomationElement e in baseline)if(e.Current.ProcessId==pid)own.Add(e);
  Need(own.Count>=2&&own.Count<=16,"BASELINE_OWNED_ROOT_COUNT");
  Mark("RAW_COMPARISON");var rawOwned=new List<AutomationElement>();var walker=new TreeWalker(Condition.TrueCondition);var raw=walker.GetFirstChild(AutomationElement.RootElement);int count=0;
  while(raw is not null){Need(count++<256,"RAW_RESULT_CAP");if(raw.Current.ProcessId==pid)rawOwned.Add(raw);raw=walker.GetNextSibling(raw);}
  Need(rawOwned.Count==own.Count&&own.All(a=>rawOwned.Count(b=>Automation.Compare(a,b))==1),"BASELINE_RAW_OWNED_SET_DIFFERENCE");
  Fact("DESKTOP_COMPARISON",new{baselineCount=baseline.Count,rawCount=count,ownedBaseline=own.Count,ownedRaw=rawOwned.Count,foreignIdentityOrTextRecorded=false});
  TracedTransport? trace=null;var traces=new List<TracedTransport>();var source=new CapturedSource(new WindowsObservationSource(_=>{trace=new TracedTransport(new UiaTransport(),pid);traces.Add(trace);return trace;},new OwnedWitness(pid)),pid);
  using var owner=new AcquisitionOwner(source,Stopwatch.GetTimestamp,new(desktopRoots:256));CanonicalProbe(owner,source,Phase.PreConnect);Mark("TOPOLOGY_INITIAL");var acquired=owner.Acquire(Phase.PreConnect,Plan.TopologyOnly);
  if(lastLocalRefusal is not null)throw new CheckFailure(lastLocalRefusal);
  if(acquired is AcquisitionResult.Failure f){ReportFailure("TOPOLOGY_REFUSAL",f,source.Last);throw new CheckFailure("INITIAL_ACQUISITION_REFUSED");}
  var ok=(AcquisitionResult.Success)acquired;var session=source.Last!;Mark("IDS_INITIAL");ReadOwnedIds(session,ok.Snapshot);Fact("INITIAL_KNOWN_IDS",new{present=new[]{"owned-main","owned-sentinel","owned-value","owned-toggle","owned-text"}.Where(session.ById.ContainsKey).ToArray()});
  Need(session.ById.ContainsKey("owned-value")&&session.ById.ContainsKey("owned-toggle")&&session.ById.ContainsKey("owned-text"),"FIXTURE_IDS_MISSING");
  object value=session.ById["owned-value"],toggle=session.ById["owned-toggle"],text=session.ById["owned-text"];
  Mark("VALUE_AVAILABLE");Need(Present<bool>(session,value!,FieldId.ValueAvailable),"VALUE_PATTERN_UNAVAILABLE");Mark("VALUE_PATTERN");Need(Present<PatternType>(session,value!,FieldId.ValuePatternType)==PatternType.Value,"VALUE_PATTERN_TYPE");Mark("VALUE_INITIAL");Need(Present<string>(session,value!,FieldId.Value)=="owned-value-A","INITIAL_VALUE");
  Mark("TOGGLE_AVAILABLE");Need(Present<bool>(session,toggle!,FieldId.ToggleAvailable),"TOGGLE_PATTERN");Mark("TOGGLE_PATTERN");Need(Present<PatternType>(session,toggle!,FieldId.TogglePatternType)==PatternType.Toggle,"TOGGLE_PATTERN");Mark("TOGGLE_INITIAL");Need(Present<int>(session,toggle!,FieldId.ToggleState)==0,"INITIAL_OFF");
  Mark("TEXT_AVAILABILITY");Need(!Present<bool>(session,text!,FieldId.ValueAvailable),"STATIC_TEXT_VALUE_AVAILABILITY");
  Mark("TEXT_VALUE_NEGATIVE");var unsupported=session.ReadCached<string>(text!,FieldId.Value);Fact("STATIC_TEXT_VALUE_STATUS",new{status=unsupported.Status.ToString()});Need(unsupported.Status!=FieldStatus.Present,"STATIC_TEXT_FAKE_DEFAULT");
  Mark("TOPOLOGY_DIGEST_NEGATIVE");bool topologyDigestRejected=false;try{owner.CreateDigest(ok.Ticket,ok.Snapshot);}catch(InvalidOperationException){topologyDigestRejected=true;}Need(topologyDigestRejected,"TOPOLOGY_TICKET_MINTED_CANONICAL_DIGEST");Fact("TOPOLOGY_SCOPE",new{canonicalDigestRejected=true,effectAuthorized=false});
  Command(dir,"on",1);trace!.CheckFrozenCache();Mark("VALUE_REFRESH");Need(Present<string>(session,value!,FieldId.Value)=="owned-value-B","REFRESH_AFTER_OWNED_UPDATE");Mark("TOGGLE_ON_REFRESH");Need(Present<int>(session,toggle!,FieldId.ToggleState)==1,"REFRESH_AFTER_OWNED_UPDATE");
  Command(dir,"mixed",2);Mark("TOGGLE_MIXED_REFRESH");Need(Present<int>(session,toggle!,FieldId.ToggleState)==2,"INDETERMINATE_REFRESH");
  Fact("STAGED_READS_AND_REFRESH",new{trace!.Calls,trace.Refreshes,trace.IdentityMatches,trace.RuntimeMatches,trace.SawRaw,trace.SawControl,ok.Snapshot.Rows.Length,threeToggleStatesObserved=true,valueUpdateObserved=true,oldCacheRemainedA=true,omittedFieldDidNotReadCurrent=true});
  Mark("FOREIGN_HANDLE_NEGATIVE");int before=trace.Calls;bool rejected=false;try{session.ReadCached<int>(new object(),FieldId.ProviderPid);}catch(Exception e){rejected=true;journal.Record(time.Elapsed.TotalSeconds,"FOREIGN_HANDLE_PROBE","UNKNOWN","ProviderPid","RETURN",DiagnosticJournal.ExceptionToken(e),"LOCAL_REJECTION");}journal.Record(time.Elapsed.TotalSeconds,"PROBE_CALL_DELTA","UNKNOWN","NA","RETURN",trace.Calls==before?"ZERO":"NONZERO");Need(rejected&&trace.Calls==before,"FOREIGN_HANDLE_NOT_REJECTED_LOCALLY");
  Mark("WRONG_THREAD_NEGATIVE");var thread=new Thread(()=>{try{session.ReadCached<string>(value!,FieldId.Name);}catch(Exception e){rejected=true;journal.Record(time.Elapsed.TotalSeconds,"WRONG_THREAD_PROBE","owned-value","Name","RETURN",DiagnosticJournal.ExceptionToken(e),"LOCAL_REJECTION");}});rejected=false;thread.Start();Need(thread.Join(1000)&&rejected&&trace.Calls==before,"WRONG_THREAD_NOT_REJECTED_LOCALLY");
  Command(dir,"close",3);
  // Close handshake is UI dispatcher completion, not proof of provider invalidation.
  Mark("STALE_AFTER_CLOSE");string stale;try{stale=session.ReadCached<string>(value!,FieldId.Value).Status.ToString();}
  catch(CheckFailure){throw;}
  catch(ElementNotAvailableException){stale="ELEMENT_NOT_AVAILABLE_EXCEPTION";}
  catch{Fact("AFTER_FIXTURE_CLOSE",new{status="UNCLASSIFIED_EXCEPTION",automaticRetry=false,providerInvalidationConfirmed=false});throw new CheckFailure("STALE_READ_UNCLASSIFIED_EXCEPTION");}
  if(lastLocalRefusal is not null)throw new CheckFailure(lastLocalRefusal);
  Fact("AFTER_FIXTURE_CLOSE",new{status=stale,automaticRetry=false,providerInvalidationConfirmed=false,meaning="single adapter unavailable-or-specific-element-exception observation, not cause proof"});Need(stale!=FieldStatus.Present.ToString(),"PROVIDER_STALE_PRESENT_AFTER_CLOSE");
  Mark("RELEASE_NEGATIVE");owner.Release(ok.Ticket);before=trace.Calls;rejected=false;try{session.ReadCached<string>(value!,FieldId.Name);}catch(Exception e){rejected=true;journal.Record(time.Elapsed.TotalSeconds,"RELEASE_PROBE","owned-value","Name","RETURN",DiagnosticJournal.ExceptionToken(e),"LOCAL_REJECTION");}journal.Record(time.Elapsed.TotalSeconds,"PROBE_CALL_DELTA","UNKNOWN","NA","RETURN",trace.Calls==before?"ZERO":"NONZERO");Need(rejected&&trace.Calls==before,"RELEASE_INVALIDATION");
  Mark("TOPOLOGY_AFTER_CLOSE");var fresh=owner.Acquire(Phase.OffPrecheck,Plan.TopologyOnly);if(lastLocalRefusal is not null)throw new CheckFailure(lastLocalRefusal);if(fresh is AcquisitionResult.Failure freshFailure)ReportFailure("FRESH_TOPOLOGY_REFUSAL",freshFailure,source.Last);Need(fresh is AcquisitionResult.Success,"FRESH_AFTER_CLOSE_ACQUISITION_REFUSED");var newOk=(AcquisitionResult.Success)fresh;Mark("IDS_AFTER_CLOSE");ReadOwnedIds(source.Last!,newOk.Snapshot);Mark("FRESH_ACQUISITION_ID");Need(newOk.Snapshot.AcquisitionId!=ok.Snapshot.AcquisitionId,"ACQUISITION_ID_REUSE");Mark("KNOWN_IDS_AFTER_CLOSE");Need(source.Last!.ById.Keys.Order().SequenceEqual(new[]{"owned-sentinel"}),"NEW_ACQUISITION_STALE_MAIN_CONTROLS");Mark("ROOTS_AFTER_CLOSE");var survivingRoots=newOk.Snapshot.Rows.Where(r=>r.Parent==-1).ToArray();Need(survivingRoots.Length==1&&source.Last.OwnedDesktop.Count==1&&source.Last.ById.TryGetValue("owned-sentinel",out var survivingHandle)&&source.Last.OwnedDesktop.Contains(survivingHandle),"AFTER_CLOSE_SURVIVING_ROOTS");Mark("OLD_HANDLE_NEGATIVE");before=trace!.Calls;rejected=false;try{source.Last.ReadCached<string>(value!,FieldId.Name);}catch(Exception e){rejected=true;journal.Record(time.Elapsed.TotalSeconds,"OLD_HANDLE_PROBE","owned-value","Name","RETURN",DiagnosticJournal.ExceptionToken(e),"LOCAL_REJECTION");}journal.Record(time.Elapsed.TotalSeconds,"PROBE_CALL_DELTA","UNKNOWN","NA","RETURN",trace.Calls==before?"ZERO":"NONZERO");Need(rejected&&trace.Calls==before,"OLD_HANDLE_ACCEPTED_BY_NEW_SESSION");owner.Release(newOk.Ticket);CanonicalProbe(owner,source,Phase.OffPrecheck);
  Mark("FINAL_COUNTS");Fact("FINAL_SESSION_CALL_COUNTS",new{perSessionLimit=4096,sessionCallCounts=traces.Select(t=>t.Calls).ToArray(),excludesBaselineIdentityAndInnerCacheCalls=true});
  Fact("LIFETIME",new{foreignAndWrongThreadBlockedBeforeTransport=true,releaseInvalidated=true,newAcquisitionAfterClose=true,oldHandleRejectedByNewSession=true});Mark("FINAL_STOP");journal.Record(time.Elapsed.TotalSeconds,"IPC_STOP","UNKNOWN","NA","BEGIN","NA");File.WriteAllText(Path.Combine(dir,"command.txt"),"stop");journal.Record(time.Elapsed.TotalSeconds,"IPC_STOP","UNKNOWN","NA","RETURN","WRITTEN");
 }
}
