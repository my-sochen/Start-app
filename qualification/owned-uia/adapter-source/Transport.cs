using System.Collections.Immutable;
using StartApp.Observation.Contracts;
namespace StartApp.Observation.Windows;

public enum NavigationView { RawDesktopChildren, ControlDescendants }
public enum CacheScope { Element }
public enum CacheMode { Full }
public enum CacheFilter { Raw }
public enum PropertyKey { ProcessId, ControlType, Name, ClassName, AutomationId, Hwnd, ValueAvailable, ToggleAvailable, Value, ToggleState }
public enum PatternKey { Value, Toggle }
public sealed record CacheStage(CacheScope Scope, CacheMode Mode, CacheFilter Filter, ImmutableArray<PropertyKey> Properties, ImmutableArray<PatternKey> Patterns)
{
    public static CacheStage For(ImmutableArray<PropertyKey> properties, ImmutableArray<PatternKey> patterns) => new(CacheScope.Element, CacheMode.Full, CacheFilter.Raw, properties, patterns);
}
public enum TransportStatus { Present, Unsupported, NotRequested, Failed }
public sealed record TransportValue(TransportStatus Status, object? Value)
{
    public static TransportValue Present(object? value) => new(TransportStatus.Present, value);
}
public sealed record ControlTypeData(int Id, string ProgrammaticName);
// Transport classifies actual pattern CLR types, never the adapter's requested pattern.
public enum ActualPattern { Value, Toggle, Other }
public sealed class TransportReadException : Exception { public TransportReadException() : base("Cached transport read failed") { } }
// One fresh transport instance per source.Open. Only a later UIA implementation may touch provider objects.
public interface IWindowsTransport : IDisposable
{
    object DesktopRoot();
    object? FirstChild(object element, NavigationView view, CacheStage stage);
    object? NextSibling(object element, NavigationView view, CacheStage stage);
    // Return a newly cached wrapper for the SAME logical element; no cross-element replacement.
    object Refresh(object element, CacheStage stage);
    TransportValue CachedProperty(object cached, PropertyKey property, bool ignoreDefaultValue);
    TransportValue CachedPattern(object cached, PatternKey pattern);
}
// Separate external witness. No native calls, job-handle discovery or process identity claim here.
public interface IJobWitness { Field<bool> IsMember(int providerPid, AcquisitionRequest request); }
