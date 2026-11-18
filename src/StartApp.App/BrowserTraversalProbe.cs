using System.Diagnostics;
namespace StartApp.App;

// Compiled out of normal builds. Controlled native-fixture synchronization, never an application option.
public static class BrowserTraversalProbe
{
#if STARTAPP_CAPTURE_TEST
    public static Action<string, int>? Observer;
#endif
    [Conditional("STARTAPP_CAPTURE_TEST")]
    public static void At(string stage, int index)
    {
#if STARTAPP_CAPTURE_TEST
        Observer?.Invoke(stage, index);
#endif
    }
}
