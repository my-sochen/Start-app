using System.Collections.Immutable;
using System.Runtime.CompilerServices;
namespace StartApp.Observation.Contracts;

// Pure adapter-contract engine. No real provider implementation/effects/native witnesses.
public sealed class AcquisitionOwner : IDisposable
{
    readonly Guid attempt = Guid.NewGuid(); readonly int ownerThread = Environment.CurrentManagedThreadId;
    readonly IObservationSource source; readonly Func<long> clock; readonly AcquisitionPolicy policy;
    readonly ConditionalWeakTable<IObservationSession, object> sessions = new();
    readonly Dictionary<Ticket, (Snapshot Snapshot, object[] Refs)> published = new();
    bool disposed, busy; long begin, last; Guid acquisition; Phase phase; Plan plan; Stage stage; int? index, ordinal; FieldId? field; FieldStatus? status;
    sealed class Refusal(FailureCode code) : Exception { public FailureCode Code => code; }
    sealed class MutableRow(int index, int parent, object handle, ProviderJobWitness job) { public int Index = index; public int Parent = parent; public object Handle = handle; public ProviderJobWitness Job = job; public Dictionary<FieldId, object> Fields = new(); }
    public AcquisitionOwner(IObservationSource source, Func<long> clock, AcquisitionPolicy? policy = null) { this.source = source ?? throw new ArgumentNullException(nameof(source)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); this.policy = policy ?? new(); }
    void ThreadCheck() { if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Wrong owner thread"); }
    void Check(CancellationToken token) { ThreadCheck(); if (token.IsCancellationRequested) throw new Refusal(FailureCode.Cancelled); long now = clock(); if (now < last) throw new Refusal(FailureCode.ClockReversed); last = now; }
    object? Navigate(Func<object?> read, CancellationToken token) { field = null; status = null; Check(token); var result = read(); Check(token); return result; }
    Refusal Aggregate(FailureCode code) { index = null; field = null; status = null; return new Refusal(code); }
    T Read<T>(IObservationSession session, MutableRow row, FieldId id, CancellationToken token)
    {
        index = row.Index; field = id; status = null; Check(token);
        if (!row.Fields.TryGetValue(id, out var value)) { var observed = session.ReadCached<T>(row.Handle, id); if (observed is null) throw new Refusal(FailureCode.NullField); row.Fields.Add(id, observed); value = observed; }
        Check(token);
        if (value is not Field<T> typed) throw new Refusal(FailureCode.InvalidType);
        status = typed.Status; if (!typed.TryGet(out var result)) throw new Refusal(FailureCode.RequiredUnavailable); return result!;
    }
    ProviderJobWitness Job(IObservationSession session, object handle, int? nodeIndex, CancellationToken token)
    {
        index = nodeIndex; field = FieldId.ProviderPid; status = null; Check(token); long start = last;
        var pid = session.ReadCached<int>(handle, FieldId.ProviderPid); Check(token); if (pid is null) throw new Refusal(FailureCode.NullField); status = pid.Status;
        if (!pid.TryGet(out int p) || p <= 0) throw new Refusal(FailureCode.UnknownPid);
        field = null; status = null; var member = session.InCurrentJob(p); Check(token); if (member is null) throw new Refusal(FailureCode.NullField); status = member.Status;
        if (!member.TryGet(out bool m)) throw new Refusal(FailureCode.MembershipUnavailable);
        return new(p, m, new(start, last));
    }
    public AcquisitionResult Acquire(Phase requestedPhase, Plan requestedPlan, CancellationToken token = default)
    {
        // Reentrant/wrong-thread calls cannot corrupt active context. Standalone failure is minted separately.
        if (Environment.CurrentManagedThreadId != ownerThread || disposed || busy) { var code = Environment.CurrentManagedThreadId != ownerThread ? FailureCode.WrongThread : disposed ? FailureCode.Disposed : FailureCode.Reentrant; return new AcquisitionResult.Failure(new(attempt, Guid.NewGuid(), requestedPhase, requestedPlan, new(0, 0), Stage.Opening, null, null, null, null, code)); }
        busy = true; acquisition = Guid.NewGuid(); phase = requestedPhase; plan = requestedPlan; begin = last = 0; stage = Stage.Opening; index = ordinal = null; field = null; status = null;
        IObservationSession? session = null; bool retained = false;
        try
        {
            begin = last = clock(); Check(token);
            if (!Enum.IsDefined(phase)) throw new Refusal(FailureCode.WrongPhase);
            if (!Enum.IsDefined(plan)) throw new Refusal(FailureCode.WrongPlan);
            if (plan == Plan.InspectDiscovery && phase is Phase.Setup or Phase.PromptCandidates) throw new Refusal(FailureCode.WrongPhase);
            if (plan == Plan.CanonicalShape && phase is not (Phase.PreConnect or Phase.OffPrecheck)) throw new Refusal(FailureCode.WrongPhase);
            session = source.Open(new AcquisitionRequest(attempt, acquisition, phase, plan)); if (session is null) throw new Refusal(FailureCode.NullSession); if (sessions.TryGetValue(session, out _)) { session = null; throw new Refusal(FailureCode.ReusedSession); }
            sessions.Add(session, new object()); Check(token);
            var roots = new List<(object Handle, ProviderJobWitness Job)>(); stage = Stage.DesktopRoots;
            var root = Navigate(session.FirstDesktopRoot, token); int seen = 0; var desktopHandles = new HashSet<object>(ReferenceEqualityComparer.Instance);
            while (root is not null)
            {
                ordinal = seen; if (!desktopHandles.Add(root)) throw new Refusal(FailureCode.BindingMismatch); if (seen++ >= policy.DesktopRoots) throw new Refusal(FailureCode.DesktopBudget);
                var job = Job(session, root, null, token);
                if (job.Member) { if (roots.Count >= policy.AcceptedRoots) throw new Refusal(FailureCode.RootCap); roots.Add((root, job)); }
                root = Navigate(() => session.NextDesktopRoot(root), token);
            }
            ordinal = null; stage = Stage.Traversal; var rows = new List<MutableRow>(); var handles = new HashSet<object>(ReferenceEqualityComparer.Instance); var queue = new Queue<(object Handle, int Parent, int Depth, ProviderJobWitness? Job)>();
            foreach (var r in roots) queue.Enqueue((r.Handle, -1, 0, r.Job));
            while (queue.TryDequeue(out var next))
            {
                index = rows.Count; field = null; status = null; Check(token); if (rows.Count >= policy.Nodes) throw new Refusal(FailureCode.NodeCap);
                if (!handles.Add(next.Handle)) throw new Refusal(FailureCode.BindingMismatch);
                var job = next.Job ?? Job(session, next.Handle, rows.Count, token); if (!job.Member) throw new Refusal(FailureCode.NonJobDescendant);
                var row = new MutableRow(rows.Count, next.Parent, next.Handle, job); row.Fields.Add(FieldId.ProviderPid, Field<int>.Present(job.ProviderPid)); rows.Add(row);
                var child = Navigate(() => session.FirstControlChild(next.Handle), token);
                if (child is not null && next.Depth >= policy.Depth) throw new Refusal(FailureCode.DepthCap);
                int children = 0;
                while (child is not null) { if (++children > policy.Siblings) throw new Refusal(FailureCode.SiblingCap); if (rows.Count + queue.Count >= policy.Nodes) throw new Refusal(FailureCode.NodeCap); queue.Enqueue((child, row.Index, next.Depth + 1, null)); child = Navigate(() => session.NextControlSibling(child), token); }
            }
            if (rows.Count == 0) throw Aggregate(FailureCode.ExactCount);
            InspectOutcome? inspect = null;
            if (plan == Plan.InspectDiscovery) inspect = Inspect(session, rows, token);
            if (plan == Plan.CanonicalShape) Shape(session, rows, token);
            stage = Stage.Publication; index = null; field = null; status = null; Check(token);
            var frozen = rows.Select(r => new ObservedRow(r.Index, r.Parent, r.Job, r.Fields.ToImmutableDictionary())).ToImmutableArray();
            var snapshot = new Snapshot(attempt, acquisition, phase, plan, new(begin, last), frozen, inspect); var ticket = new Ticket();
            published.Add(ticket, (snapshot, rows.Select(r => r.Handle).ToArray())); held.Add(ticket, session); retained = true;
            return new AcquisitionResult.Success(snapshot, ticket);
        }
        catch (Exception ex) { var code = ex is Refusal r ? r.Code : ex is OperationCanceledException && token.IsCancellationRequested ? FailureCode.Cancelled : FailureCode.SourceFailed; return new AcquisitionResult.Failure(new(attempt, acquisition, phase, plan, new(begin, Math.Max(begin, last)), stage, index, ordinal, field, status, code)); }
        finally { if (!retained && session is not null) { try { session.Dispose(); } catch { RecordDisposalIssue(); } } busy = false; }
    }
    readonly Dictionary<Ticket, IObservationSession> held = new();
    readonly List<FailureEvidence> lifecycleFailures = new();
    public bool DisposalFailure { get { ThreadCheck(); return lifecycleFailures.Count > 0; } }
    public ImmutableArray<FailureEvidence> LifecycleFailures { get { ThreadCheck(); return lifecycleFailures.ToImmutableArray(); } }
    void RecordDisposalIssue() => lifecycleFailures.Add(new(attempt, acquisition, phase, plan, new(begin, Math.Max(begin, last)), Stage.Binding, null, null, null, null, FailureCode.DisposalFailed));
    InspectOutcome Inspect(IObservationSession s, List<MutableRow> rows, CancellationToken token)
    {
        stage = Stage.Inspect;
        int Root(int i) { while (rows[i].Parent != -1) i = rows[i].Parent; return i; }
        Role Kind(MutableRow row) => Mappings.RoleOf(Read<int>(s, row, FieldId.ControlTypeId, token));
        bool ValueMatches(MutableRow row) { if (!Read<bool>(s, row, FieldId.ValueAvailable, token)) return false; if (Read<PatternType>(s, row, FieldId.ValuePatternType, token) != PatternType.Value) throw new Refusal(FailureCode.PatternMismatch); return Read<string>(s, row, FieldId.Value, token) == InspectAddress; }
        var boxes = rows.Where(r => Kind(r) == Role.Edit && Read<string>(s, r, FieldId.ClassName, token) == "OmniboxViewViews" && ValueMatches(r)).ToArray();
        if (boxes.Length != 1) throw Aggregate(FailureCode.ExactCount); int root = Root(boxes[0].Index);
        var documents = rows.Where(r => Root(r.Index) == root && Kind(r) == Role.Document && Read<string>(s, r, FieldId.AutomationId, token) == "RootWebArea" && Read<string>(s, r, FieldId.Name, token) == "Inspect with Edge Developer Tools" && ValueMatches(r)).ToArray();
        if (documents.Length != 1) throw Aggregate(FailureCode.ExactCount); int document = documents[0].Index;
        MutableRow Child(Role kind, string name, string? id = null) { var matches = rows.Where(r => r.Parent == document && Kind(r) == kind && Read<string>(s, r, FieldId.Name, token) == name && (id is null || Read<string>(s, r, FieldId.AutomationId, token) == id)).ToArray(); if (matches.Length != 1) throw Aggregate(FailureCode.ExactCount); return matches[0]; }
        var checkbox = Child(Role.CheckBox, "Allow remote debugging for this browser instance", "remote-debugging-enabled"); var broad = Child(Role.Text, Broad); var trust = Child(Role.Text, Trust);
        if (!Read<bool>(s, checkbox, FieldId.ToggleAvailable, token) || Read<PatternType>(s, checkbox, FieldId.TogglePatternType, token) != PatternType.Toggle) throw new Refusal(FailureCode.PatternMismatch);
        var toggle = Mappings.ToggleOf(Read<int>(s, checkbox, FieldId.ToggleState, token)); if (toggle is null) throw new Refusal(FailureCode.InvalidType);
        return new(root, boxes[0].Index, document, checkbox.Index, broad.Index, trust.Index, toggle.Value);
        // Discovery ONLY. No expected toggle/state/native/current/RuntimeID guard or effect approval.
    }
    void Shape(IObservationSession s, List<MutableRow> rows, CancellationToken token) { stage = Stage.Shape; foreach (var row in rows) { Read<string>(s, row, FieldId.ProgrammaticName, token); Read<string>(s, row, FieldId.Name, token); Read<string>(s, row, FieldId.ClassName, token); Read<string>(s, row, FieldId.AutomationId, token); Read<int>(s, row, FieldId.ShapeProcessId, token); Read<int>(s, row, FieldId.Hwnd, token); } }
    public bool VerifyRetainedBinding(Ticket ticket, Snapshot snapshot, int nodeIndex) { ThreadCheck(); if (disposed || busy) throw new InvalidOperationException("Owner unavailable"); return published.TryGetValue(ticket, out var entry) && ReferenceEquals(entry.Snapshot, snapshot) && nodeIndex >= 0 && nodeIndex < entry.Refs.Length; }
    readonly Dictionary<Ticket, BoundCanonicalDigest> digests = new();
    public BoundCanonicalDigest CreateDigest(Ticket ticket, Snapshot snapshot) { ThreadCheck(); if (!VerifyRetainedBinding(ticket, snapshot, 0)) throw new InvalidOperationException("Unbound snapshot"); if (digests.TryGetValue(ticket, out var existing)) return existing; var digest = new BoundCanonicalDigest(CanonicalShape.Project(snapshot)); digests.Add(ticket, digest); return digest; }
    public bool VerifyDigest(Ticket ticket, Snapshot snapshot, BoundCanonicalDigest digest) { ThreadCheck(); return VerifyRetainedBinding(ticket, snapshot, 0) && digests.TryGetValue(ticket, out var bound) && ReferenceEquals(bound, digest); }
    public void Release(Ticket ticket) { ThreadCheck(); if (busy) throw new InvalidOperationException("Owner active"); digests.Remove(ticket); published.Remove(ticket, out var old); if (held.Remove(ticket, out var session)) { busy = true; try { session.Dispose(); } catch { if (old.Snapshot is not null) lifecycleFailures.Add(new(old.Snapshot.AttemptId, old.Snapshot.AcquisitionId, old.Snapshot.Phase, old.Snapshot.Plan, old.Snapshot.Interval, Stage.Binding, null, null, null, null, FailureCode.DisposalFailed)); } finally { busy = false; } } }
    public void Dispose() { ThreadCheck(); if (busy) throw new InvalidOperationException("Owner active"); foreach (var ticket in held.Keys.ToArray()) Release(ticket); disposed = true; }
    public const string InspectAddress = "edge://inspect/#remote-debugging";
    public const string Broad = "Turning on this setting allows external apps to request full control of this browser. This includes read access to your saved data, cookies and site data, and the ability to navigate to any URL.";
    public const string Trust = "Only web developers should turn on this feature, and only use it with trusted apps.";
}
