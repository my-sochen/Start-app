namespace StartApp.Core;

/// <summary>Current capability, not a promise of a qualified existing-session connector.</summary>
public static class BrowserCapturePolicy
{
    public const string UnavailableNote = "לכידת לשוניות Chrome ו-Edge אינה זמינה כעת. אפשר להוסיף כתובות ידנית; לכידת תוכנות ותיקיות נשארת זמינה. להסבר: עזרה והגדרות דפדפן במסך הראשי.";
    public const string ChromeSettingsAddress = "chrome://inspect/#remote-debugging";
    public const string EdgeSettingsAddress = "edge://inspect/#remote-debugging";
    public static IOpenItemCapturer Compose(IOpenItemCapturer windowsCapture) =>
        new CompositeCapturer(windowsCapture, new BrowserCaptureUnavailable());
}

/// <summary>Never reads browser state, selects tabs, changes settings or opens a connection.</summary>
public sealed class BrowserCaptureUnavailable : IOpenItemCapturer
{
    public CaptureScan Scan() => new(Array.Empty<CapturedItem>(), new[] { BrowserCapturePolicy.UnavailableNote });
}
