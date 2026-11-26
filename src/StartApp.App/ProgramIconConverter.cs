using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>Saved OpenWith program identity, distinct from the item's target icon. Bounded background lookup, never UI-thread extraction.</summary>
public sealed class ProgramIconView : StackPanel
{
    public static readonly DependencyProperty ProgramProperty = DependencyProperty.Register(nameof(Program), typeof(string), typeof(ProgramIconView), new PropertyMetadata(null, Changed));
    public string? Program { get => (string?)GetValue(ProgramProperty); set => SetValue(ProgramProperty, value); }
    private const string Prefix = "נפתח באמצעות: ";
    private readonly Image _icon = new() { Width = 14, Height = 14, Margin = new Thickness(4,0,0,1) };
    private readonly TextBlock _label = new() { FontSize = 11, Foreground = Brushes.DarkSlateGray };
    private readonly TextBlock _prefix = new() { Text = Prefix, FontSize = 11, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(4,0,0,0) };
    private int _generation;
    public ProgramIconView()
    {
        Orientation = Orientation.Horizontal; FlowDirection = FlowDirection.RightToLeft; Margin = new Thickness(0,0,8,2);
        var identity = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.RightToLeft };
        identity.Children.Add(_label); identity.Children.Add(_icon);
        Children.Add(_prefix); Children.Add(identity);
        Loaded += (_,_) => Refresh(); Unloaded += (_,_) => _generation++;
    }
    private void SetIdentity(string value)
    {
        _label.Text = value;
        // Keep the full accessible phrase although the visible prefix and identity are separate.
        AutomationProperties.SetName(_label, Prefix + value);
    }
    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs args) => ((ProgramIconView)obj).Refresh();
    private async void Refresh()
    {
        var gen = ++_generation; var path = Program;
        Visibility = string.IsNullOrWhiteSpace(path) ? Visibility.Collapsed : Visibility.Visible;
        _icon.Source = null; _icon.Visibility = Visibility.Collapsed;
        SetIdentity(path ?? ""); _label.ToolTip = path; AutomationProperties.SetHelpText(_label, path ?? "");
        if (!IsLoaded || string.IsNullOrWhiteSpace(path)) return;
        var icon = await TargetIconLoader.Get(path, "Program");
        if (gen != _generation || !IsLoaded || icon is null) return;
        _icon.Source = icon; _icon.Visibility = Visibility.Visible;
        SetIdentity(ProgramDisplayNames.Resolve(path));
    }
}
