using System.ComponentModel;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>Owns the window's subscription lifetime to exactly one workspace session.
/// No Window or WPF dependency. Replacement detaches the old session before attaching the new one.</summary>
internal sealed class WindowSessionSubscriptions(
    Action focusAdd, Action<string> copyAddress, Action closeApproved,
    Action pickImage, Action pickCreateImage, PropertyChangedEventHandler propertyChanged) : IDisposable
{
    public MainViewModel? Current { get; private set; }

    public void Replace(MainViewModel? session)
    {
        if (ReferenceEquals(session, Current)) return;
        Dispose();
        if (session is null) return;
        session.FocusAddRequested += focusAdd;
        session.CopyAddressRequested += copyAddress;
        session.AppCloseApproved += closeApproved;
        session.PickImageRequested += pickImage;
        session.PickCreateImageRequested += pickCreateImage;
        session.PropertyChanged += propertyChanged;
        Current = session;
    }

    public void Dispose()
    {
        if (Current is null) return;
        Current.FocusAddRequested -= focusAdd;
        Current.CopyAddressRequested -= copyAddress;
        Current.AppCloseApproved -= closeApproved;
        Current.PickImageRequested -= pickImage;
        Current.PickCreateImageRequested -= pickCreateImage;
        Current.PropertyChanged -= propertyChanged;
        Current = null;
    }
}
