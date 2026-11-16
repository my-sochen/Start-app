using StartApp.Observation.Contracts;
namespace StartApp.Observation.Windows;

// No activation entrypoint or default transport. Host must inject a fresh transport and witness.
public sealed class WindowsObservationSource : IObservationSource
{
    readonly Func<AcquisitionRequest, IWindowsTransport> factory; readonly IJobWitness witness;
    public WindowsObservationSource(Func<AcquisitionRequest, IWindowsTransport> factory, IJobWitness witness) { this.factory = factory ?? throw new ArgumentNullException(nameof(factory)); this.witness = witness ?? throw new ArgumentNullException(nameof(witness)); }
    readonly int thread = Environment.CurrentManagedThreadId;
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<IWindowsTransport, object> used = new();
    public IObservationSession Open(AcquisitionRequest request)
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Source thread mismatch");
        ArgumentNullException.ThrowIfNull(request);
        var transport = factory(request) ?? throw new InvalidOperationException("Null transport");
        if (used.TryGetValue(transport, out _)) throw new InvalidOperationException("Reused transport");
        used.Add(transport, new object());
        return new WindowsObservationSession(request, transport, witness);
    }
}
public sealed class WindowsObservationSession : IObservationSession
{
    sealed class Handle(object raw) { public object Raw = raw; }
    readonly AcquisitionRequest request; readonly IWindowsTransport transport; readonly IJobWitness witness;
    readonly int thread = Environment.CurrentManagedThreadId; bool disposed, busy;
    readonly HashSet<Handle> handles = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<object, Handle> initial = new(ReferenceEqualityComparer.Instance);
    static readonly CacheStage NavigationStage = CacheStage.For([], []);
    internal WindowsObservationSession(AcquisitionRequest request, IWindowsTransport transport, IJobWitness witness) { this.request = request; this.transport = transport; this.witness = witness; }
    void Live() { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Session thread mismatch"); if (disposed) throw new ObjectDisposedException(nameof(WindowsObservationSession)); if (busy) throw new InvalidOperationException("Session active"); }
    Handle Validate(object node) { Live(); if (node is not Handle handle || !handles.Contains(handle)) throw new InvalidOperationException("Foreign or stale session handle"); return handle; }
    T Call<T>(Func<T> action) { Live(); busy = true; try { return action(); } finally { busy = false; } }
    object? Keep(object? raw) { if (raw is null) return null; if (initial.TryGetValue(raw, out var known)) return known; var handle = new Handle(raw); handles.Add(handle); initial.Add(raw, handle); return handle; }
    public object? FirstDesktopRoot() => Call(() => Keep(transport.FirstChild(transport.DesktopRoot(), NavigationView.RawDesktopChildren, NavigationStage)));
    public object? NextDesktopRoot(object node) { var h = Validate(node); return Call(() => Keep(transport.NextSibling(h.Raw, NavigationView.RawDesktopChildren, NavigationStage))); }
    public object? FirstControlChild(object node) { var h = Validate(node); return Call(() => Keep(transport.FirstChild(h.Raw, NavigationView.ControlDescendants, NavigationStage))); }
    public object? NextControlSibling(object node) { var h = Validate(node); return Call(() => Keep(transport.NextSibling(h.Raw, NavigationView.ControlDescendants, NavigationStage))); }
    static (PropertyKey? Property, PatternKey? Pattern, Type Type) Mapping(FieldId id) => id switch
    {
        FieldId.ProviderPid or FieldId.ShapeProcessId => (PropertyKey.ProcessId, null, typeof(int)),
        FieldId.ControlTypeId => (PropertyKey.ControlType, null, typeof(int)),
        FieldId.ProgrammaticName => (PropertyKey.ControlType, null, typeof(string)),
        FieldId.Name => (PropertyKey.Name, null, typeof(string)),
        FieldId.ClassName => (PropertyKey.ClassName, null, typeof(string)),
        FieldId.AutomationId => (PropertyKey.AutomationId, null, typeof(string)),
        FieldId.Hwnd => (PropertyKey.Hwnd, null, typeof(int)),
        FieldId.ValueAvailable => (PropertyKey.ValueAvailable, null, typeof(bool)),
        FieldId.ToggleAvailable => (PropertyKey.ToggleAvailable, null, typeof(bool)),
        FieldId.ValuePatternType => (null, PatternKey.Value, typeof(PatternType)),
        FieldId.TogglePatternType => (null, PatternKey.Toggle, typeof(PatternType)),
        FieldId.Value => (PropertyKey.Value, PatternKey.Value, typeof(string)),
        FieldId.ToggleState => (PropertyKey.ToggleState, PatternKey.Toggle, typeof(int)),
        _ => throw new ArgumentException("Field outside implemented readsets")
    };
    public Field<T> ReadCached<T>(object node, FieldId field)
    {
        var h = Validate(node); var map = Mapping(field); if (map.Type != typeof(T)) throw new ArgumentException("Field generic type mismatch");
        return Call(() =>
        {
            try
            {
                var stage = CacheStage.For(map.Property is { } p ? [p] : [], map.Pattern is { } pat ? [pat] : []);
                var replacement = transport.Refresh(h.Raw, stage) ?? throw new InvalidOperationException("Null cache wrapper");
                // Only replace the private transport wrapper, never the owner-visible handle.
                // Transport owns aliases and must keep old navigation handles valid until session disposal.
                if (initial.TryGetValue(replacement, out var alias) && !ReferenceEquals(alias, h)) throw new InvalidOperationException("Refresh aliases a different handle"); initial[replacement] = h; h.Raw = replacement;
                var value = map.Property is { } property ? transport.CachedProperty(replacement, property, true) : transport.CachedPattern(replacement, map.Pattern!.Value);
                if (value is null) throw new InvalidOperationException("Null transport result");
                if (value.Status != TransportStatus.Present) return Field<T>.Unavailable(value.Status switch { TransportStatus.Unsupported => FieldStatus.Unsupported, TransportStatus.NotRequested => FieldStatus.NotRequested, TransportStatus.Failed => FieldStatus.AcquisitionFailed, _ => throw new InvalidOperationException("Invalid transport status") });
                object? scalar = value.Value;
                if (map.Property == PropertyKey.ControlType) { if (scalar is not ControlTypeData control) throw new InvalidOperationException("Wrong control type"); scalar = field == FieldId.ControlTypeId ? (object)control.Id : control.ProgrammaticName; }
                if (map.Property is null) { if (scalar is not ActualPattern actual) throw new InvalidOperationException("Wrong pattern classification"); scalar = actual switch { ActualPattern.Value => PatternType.Value, ActualPattern.Toggle => PatternType.Toggle, _ => PatternType.WrongType }; }
                if (scalar is not T typed) throw new InvalidOperationException("Null or wrong cached scalar type");
                return Field<T>.Present(typed);
            }
            catch (TransportReadException) { return Field<T>.Unavailable(FieldStatus.AcquisitionFailed); }
        });
    }
    public Field<bool> InCurrentJob(int pid) { Live(); if (pid <= 0) throw new ArgumentOutOfRangeException(nameof(pid)); return Call(() => witness.IsMember(pid, request)); }
    public void Dispose() { if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Session thread mismatch"); if (disposed) return; if (busy) throw new InvalidOperationException("Session active"); disposed = true; handles.Clear(); initial.Clear(); transport.Dispose(); }
}
