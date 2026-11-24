namespace StartApp.Presentation;

/// <summary>Thread-safe handoff from blocking workers to the deadline publisher.
/// The publisher owns the first fault before handoff; afterwards exactly one observer receives it.
/// Late result writes cannot change the published snapshot.</summary>
public sealed class CheckResultPublication<T> where T : struct
{
    private readonly object _gate = new();
    private readonly T?[] _results;
    private Exception? _fault;
    private bool _published;
    private bool _reported;
    public CheckResultPublication(int count) => _results = new T?[count];
    public void Record(int index, T result)
    {
        lock (_gate) { if (!_published) _results[index] = result; }
    }
    public Exception? ObserveFault(Exception error)
    {
        lock (_gate)
        {
            _fault ??= error;
            if (!_published || _reported) return null;
            _reported = true;
            return _fault;
        }
    }
    public (T?[] Results, Exception? Fault) Publish()
    {
        lock (_gate)
        {
            _published = true;
            var done = (T?[])_results.Clone();
            if (_fault is not null) _reported = true;
            return (done, _fault);
        }
    }
}
