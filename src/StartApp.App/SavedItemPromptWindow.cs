using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace StartApp.App;

/// <summary>Owner-modal version of the capture-list Move/Cancel confirmation, reusable outside the global screen.</summary>
internal sealed class SavedItemPromptWindow : Window
{
    internal SavedItemPromptWindow(string title, string message, string confirm, bool notice = false)
    {
        Title = title; Width = 440; SizeToContent = SizeToContent.Height; WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Background = Brushes.Transparent; AllowsTransparency = true;
        FontSize = 15; FontFamily = new FontFamily("./Assets/Fonts/#Noto Sans Hebrew, Arial");
        var body = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
        var header = new DockPanel { FlowDirection = FlowDirection.LeftToRight };
        var close = new Button { Content = "×", Style = (Style)FindResource("QuietTextButton"), Width = 24, Height = 24 };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close dialog");
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Left); header.Children.Add(close);
        body.Children.Add(header);
        header.Children.Add(new TextBlock { Text = title, FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(34,40,51)), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,14,0,18) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        var ok = new Button { Content = confirm, Style = (Style)FindResource("DialogPrimaryButton"), IsDefault = true };
        System.Windows.Automation.AutomationProperties.SetName(ok, notice ? "Close item notice" : "Move capture list item");
        ok.Click += (_, _) => DialogResult = true;
        if (!notice)
        {
            var cancel = new Button { Content = "ביטול", Style = (Style)FindResource("DialogSecondaryButton"), Margin = new Thickness(0,0,8,0), IsCancel = true };
            System.Windows.Automation.AutomationProperties.SetName(cancel, "Close capture list conflict");
            cancel.Click += (_, _) => DialogResult = false; actions.Children.Add(cancel);
        }
        actions.Children.Add(ok);
        body.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(224,230,240)), BorderThickness = new Thickness(0,1,0,0), Padding = new Thickness(0,20,0,0), Margin = new Thickness(0,8,0,0), Child = actions });
        Content = new DialogFrame { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Child = body, Padding = new Thickness(28), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(201,208,220)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } };
    }
    internal static SavedItemPromptWindow Conflict(string message) => new("הפריט נמצא ברשימה הנגדית", message, "העבר לרשימה המבוקשת");
    internal static SavedItemPromptWindow Notice(string title, string message) => new(title, message, "הבנתי", true);
}
