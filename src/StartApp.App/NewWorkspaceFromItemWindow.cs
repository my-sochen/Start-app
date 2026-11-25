using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StartApp.Presentation;
namespace StartApp.App;

internal sealed class NewWorkspaceFromItemWindow : Window
{
    public NewWorkspaceFromItemWindow(MainViewModel vm, SavedItemTransfer source)
    {
        Title = "סביבה חדשה מהאייטם"; Width = 480; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Background = Brushes.Transparent; AllowsTransparency = true;
        FontSize = 15; FontFamily = new FontFamily("./Assets/Fonts/#Noto Sans Hebrew, Arial");
        var body = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
        var header = new DockPanel { FlowDirection = FlowDirection.LeftToRight };
        var close = new Button { Content = "×", Style = (Style)FindResource("QuietTextButton"), Width = 24, Height = 24 };
        System.Windows.Automation.AutomationProperties.SetName(close, "Close dialog");
        close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Left); header.Children.Add(close);
        body.Children.Add(header);
        header.Children.Add(new TextBlock { Text = "הוסף לסביבה חדשה", FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        body.Children.Add(new TextBlock { Text = source.Name + " יועתק לסביבה החדשה. המקור לא ישתנה.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,14,0,18) });
        body.Children.Add(new TextBlock { Text = "שם הסביבה", Margin = new Thickness(0,0,0,4) });
        var input = new TextBox { Style = (Style)FindResource("DetailsInput") };
        System.Windows.Automation.AutomationProperties.SetName(input, "New item environment name"); body.Children.Add(input);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(155,48,48)), Margin = new Thickness(0,8,0,8) }; body.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,12,0,0) };
        var create = new Button { Content = "צור סביבה והוסף", IsEnabled = false, IsDefault = true, Style = (Style)FindResource("DialogPrimaryButton") };
        System.Windows.Automation.AutomationProperties.SetName(create, "Create item environment");
        input.TextChanged += (_, _) => create.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        create.Click += (_, _) => { if (vm.CreateWorkspaceFromSavedItem(source, input.Text)) DialogResult = true; else error.Text = vm.Status; };
        var cancel = new Button { Content = "ביטול", IsCancel = true, Style = (Style)FindResource("DialogSecondaryButton"), Margin = new Thickness(8,0,0,0) };
        System.Windows.Automation.AutomationProperties.SetName(cancel, "Cancel item environment");cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(create);actions.Children.Add(cancel);body.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(224,230,240)), BorderThickness = new Thickness(0,1,0,0), Padding = new Thickness(0,20,0,0), Margin = new Thickness(0,8,0,0), Child = actions });
        Content = new DialogFrame { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Child = body, Padding = new Thickness(28), Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(201,208,220)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        Loaded += (_, _) => input.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } };
    }
}
