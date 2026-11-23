namespace StartApp.Presentation;

/// <summary>Owns the inline validation message and the form that displays it.
/// Host publishes notifications and diagnostic status after setting both fields.</summary>
public sealed class FieldErrorSession
{
    public string Host { get; set; } = "";
    public string Message { get; set; } = "";
    public string ForHost(string host) => Host == host ? Message : "";
}
