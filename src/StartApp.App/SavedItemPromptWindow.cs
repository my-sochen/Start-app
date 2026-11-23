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
        FontFamily = new FontFamily("./Assets/Fonts/#Noto Sans Hebrew, Arial");
        var body = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
        body.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,14,0,18) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var ok = new Button { Content = confirm, Style = (Style)FindResource("PrimaryActionButton"), IsDefault = true };
        System.Windows.Automation.AutomationProperties.SetName(ok, notice ? "Close item notice" : "Move capture list item");
        ok.Click += (_, _) => DialogResult = true; actions.Children.Add(ok);
        if (!notice)
        {
            var cancel = new Button { Content = "ביטול", Style = (Style)FindResource("HomeToolbarButton"), Margin = new Thickness(8,0,0,0), IsCancel = true };
            System.Windows.Automation.AutomationProperties.SetName(cancel, "Close capture list conflict");
            cancel.Click += (_, _) => DialogResult = false; actions.Children.Add(cancel);
        }
        body.Children.Add(actions);
        Content = new Border { Child = body, Padding = new Thickness(24), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(201,208,220)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } };
    }
    internal static SavedItemPromptWindow Conflict(string message) => new("הפריט נמצא ברשימה הנגדית", message, "העבר לרשימה המבוקשת");
    internal static SavedItemPromptWindow Notice(string title, string message) => new(title, message, "הבנתי", true);
}
