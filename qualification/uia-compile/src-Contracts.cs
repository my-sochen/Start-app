using System.Collections.Immutable;
namespace StartApp.Observation.Contracts;

public enum FieldStatus { Present, Unsupported, NotRequested, AcquisitionFailed }
public sealed class Field<T>
{
    public FieldStatus Status { get; }
    private readonly T? value;
    private Field(FieldStatus status, T? value) { Status = status; this.value = value; }
    public static Field<T> Present(T value) => value is null ? throw new ArgumentNullException(nameof(value)) : new(FieldStatus.Present, value);
    public static Field<T> Unavailable(FieldStatus status) => !Enum.IsDefined(status) || status == FieldStatus.Present ? throw new ArgumentException("Explicit unavailable status required") : new(status, default);
    public bool TryGet(out T? result) { result = value; return Status == FieldStatus.Present; }
    internal T Require() => Status == FieldStatus.Present ? value! : throw new InvalidOperationException("Required unavailable field");
}
public enum Phase { Setup, PreOn, PostOn, PreConnect, PromptCandidates, OffPrecheck, PostOff }
public enum Plan { TopologyOnly, InspectDiscovery, CanonicalShape }
public enum FieldId { ProviderPid, ControlTypeId, ProgrammaticName, Hwnd, Enabled, Offscreen, ClassName, AutomationId, Name, ValueAvailable, ValuePatternType, Value, ToggleAvailable, TogglePatternType, ToggleState, RuntimeId, ShapeProcessId }
public enum Role { Window, Edit, Document, CheckBox, Text, Pane, Other }
public enum Toggle { Off = 0, On = 1, Indeterminate = 2 }
public enum PatternType { Value, Toggle, WrongType }
public static class Mappings
{
    public static Role RoleOf(int id) => id switch { 50032 => Role.Window, 50004 => Role.Edit, 50030 => Role.Document, 50002 => Role.CheckBox, 50020 => Role.Text, 50033 => Role.Pane, _ => Role.Other };
    public static Toggle? ToggleOf(int value) => value is >= 0 and <= 2 ? (Toggle)value : null;
}
public readonly record struct SampleInterval
{
    public long Begin { get; }
    public long End { get; }
    public SampleInterval(long begin, long end) { if (begin > end) throw new ArgumentException("Inverted interval"); Begin = begin; End = end; }
}
public enum FailureCode { WrongThread, Disposed, Reentrant, Cancelled, SourceFailed, ClockReversed, ReusedSession, DesktopBudget, RootCap, NodeCap, DepthCap, SiblingCap, UnknownPid, MembershipUnavailable, NonJobDescendant, InvalidType, RequiredUnavailable, PatternMismatch, ExactCount, WrongPhase, WrongPlan, BindingMismatch, ShapeIncomplete, DisposalFailed, NullSession, NullField }
public enum Stage { Opening, DesktopRoots, Traversal, Inspect, Shape, Publication, Binding }
public sealed record FailureEvidence(Guid AttemptId, Guid AcquisitionId, Phase Phase, Plan Plan, SampleInterval Interval, Stage Stage, int? NodeIndex, int? DesktopOrdinal, FieldId? Field, FieldStatus? Status, FailureCode Code);
public sealed record ProviderJobWitness(int ProviderPid, bool Member, SampleInterval Interval);
public sealed class ObservedRow
{
    public int Index { get; }
    public int Parent { get; }
    public ProviderJobWitness Job { get; }
    public ImmutableDictionary<FieldId, object> Fields { get; }
    internal ObservedRow(int index, int parent, ProviderJobWitness job, ImmutableDictionary<FieldId, object> fields) { Index = index; Parent = parent; Job = job; Fields = fields; }
    public Field<T> Get<T>(FieldId field) { if (!Fields.TryGetValue(field, out var value)) return Field<T>.Unavailable(FieldStatus.NotRequested); return value is Field<T> typed ? typed : throw new InvalidOperationException("Field type mismatch"); }
}
public sealed class InspectOutcome
{
    public int RootIndex { get; }
    public int OmniboxIndex { get; }
    public int DocumentIndex { get; }
    public int CheckboxIndex { get; }
    public int BroadTextIndex { get; }
    public int TrustTextIndex { get; }
    public Toggle ToggleState { get; }
    internal InspectOutcome(int root, int omnibox, int document, int checkbox, int broad, int trust, Toggle toggle) { RootIndex = root; OmniboxIndex = omnibox; DocumentIndex = document; CheckboxIndex = checkbox; BroadTextIndex = broad; TrustTextIndex = trust; ToggleState = toggle; }
}
public sealed class Snapshot
{
    public Guid AttemptId { get; }
    public Guid AcquisitionId { get; }
    public Phase Phase { get; }
    public Plan Plan { get; }
    public SampleInterval Interval { get; }
    public ImmutableArray<ObservedRow> Rows { get; }
    public InspectOutcome? Inspect { get; }
    internal Snapshot(Guid attempt, Guid acquisition, Phase phase, Plan plan, SampleInterval interval, ImmutableArray<ObservedRow> rows, InspectOutcome? inspect) { Inspect = inspect; AttemptId = attempt; AcquisitionId = acquisition; Phase = phase; Plan = plan; Interval = interval; Rows = rows; }
}
// No public constructors, Completed bit or failure+snapshot combination.
public abstract class AcquisitionResult
{
    private AcquisitionResult() { }
    public sealed class Success : AcquisitionResult { public Snapshot Snapshot { get; } public Ticket Ticket { get; } internal Success(Snapshot snapshot, Ticket ticket) { Snapshot = snapshot; Ticket = ticket; } }
    public sealed class Failure : AcquisitionResult { public FailureEvidence Evidence { get; } internal Failure(FailureEvidence evidence) { Evidence = evidence; } }
}
public sealed class Ticket { internal Ticket() { } }
// Proposed observation-only budget. Total desktop256 is NEW, not baseline-equivalence/effect guard.
public sealed record AcquisitionPolicy
{
    public int DesktopRoots { get; }
    public int AcceptedRoots { get; }
    public int Nodes { get; }
    public int Depth { get; }
    public int Siblings { get; }
    public AcquisitionPolicy(int desktopRoots = 256, int acceptedRoots = 16, int nodes = 512, int depth = 12, int siblings = 64) { if (desktopRoots < 1 || desktopRoots > 4096 || acceptedRoots < 1 || acceptedRoots > 16 || nodes < 1 || nodes > 512 || depth < 0 || depth > 12 || siblings < 1 || siblings > 64) throw new ArgumentOutOfRangeException(); DesktopRoots = desktopRoots; AcceptedRoots = acceptedRoots; Nodes = nodes; Depth = depth; Siblings = siblings; }
}
// In-memory/future-adapter seam only. No UIA/COM/native implementations supplied.
// Each Open MUST make a new phase-local source acquisition; engine also rejects session identity reuse.
public enum DesktopScope { DirectDesktopChildren }
public enum DescendantView { Control }
public sealed class AcquisitionRequest
{
    public Guid AttemptId { get; }
    public Guid AcquisitionId { get; }
    public Phase Phase { get; }
    public Plan Plan { get; }
    public DesktopScope DesktopScope => DesktopScope.DirectDesktopChildren;
    public DescendantView DescendantView => DescendantView.Control;
    internal AcquisitionRequest(Guid attemptId, Guid acquisitionId, Phase phase, Plan plan) { AttemptId = attemptId; AcquisitionId = acquisitionId; Phase = phase; Plan = plan; }
}
public interface IObservationSource { IObservationSession Open(AcquisitionRequest request); }
public interface IObservationSession : IDisposable
{
    object? FirstDesktopRoot(); object? NextDesktopRoot(object root);
    object? FirstControlChild(object node); object? NextControlSibling(object node);
    // MUST extract explicitly acquired cached field, ignore-default semantics. No hidden Current fallback.
    // Staged cache acquisition timing belongs in snapshot interval. No atomicity implied.
    Field<T> ReadCached<T>(object node, FieldId field);
    Field<bool> InCurrentJob(int providerPid);
}
