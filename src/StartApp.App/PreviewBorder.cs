using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace StartApp.App;

// Accessibility/testability host for the card design preview.
// A plain WPF Border never reaches UIA: UIElement.OnCreateAutomationPeer returns null
// (dotnet/wpf PresentationCore System/Windows/UIElement.cs) and Border/Decorator do not
// override it, so AutomationProperties.Name on a Border is invisible to UIA clients.
// This subclass exposes the SAME visual element through a FrameworkElementAutomationPeer:
// ControlType.Custom, the real render bounds (used by the e2e frame crop), the
// AutomationProperties.Name, and no interaction patterns or focusable/button semantics.
public class PreviewBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new FrameworkElementAutomationPeer(this);
    }
}
