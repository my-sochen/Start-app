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
    private readonly Image _icon = new() { Width = 14, Height = 14, Margin = new Thickness(0,0,4,1) };
    private readonly TextBlock _label = new() { FontSize = 11, Foreground = Brushes.DarkSlateGray, Margin = new Thickness(0,0,8,2) };
    private int _generation;
    public ProgramIconView() { Orientation = Orientation.Horizontal; Children.Add(_icon); Children.Add(_label); Loaded += (_,_) => Refresh(); Unloaded += (_,_) => _generation++; }
    private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs args) => ((ProgramIconView)obj).Refresh();
    private async void Refresh()
    {
        var gen = ++_generation; var path = Program;
        Visibility = string.IsNullOrWhiteSpace(path) ? Visibility.Collapsed : Visibility.Visible;
        _icon.Source = null; _icon.Visibility = Visibility.Collapsed;
        _label.Text = "Opens with: " + path; _label.ToolTip = path; AutomationProperties.SetHelpText(_label, path ?? "");
        if (!IsLoaded || string.IsNullOrWhiteSpace(path)) return;
        var icon = await TargetIconLoader.Get(path, "Program");
        if (gen != _generation || !IsLoaded || icon is null) return;
        _icon.Source = icon; _icon.Visibility = Visibility.Visible;
        _label.Text = "Opens with: " + ProgramDisplayNames.Resolve(path);
    }
}
