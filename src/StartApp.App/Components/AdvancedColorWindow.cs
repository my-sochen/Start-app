using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls.Primitives;
using StartApp.Core;

namespace StartApp.App;

/// <summary>RGB/hex entry and HSV spectrum edit a local draft. Only Choose returns a value to the caller.</summary>
public sealed class AdvancedColorWindow : Window
{
    private readonly Rectangle _spectrum = new() { Height = 170 };
    private readonly Slider _hue = new() { Minimum = 0, Maximum = 360, TickFrequency = 1 };
    private readonly TextBox _hex = new() { Width = 110, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox[] _rgb = { new(), new(), new() };
    private readonly Border _preview = new() { Height = 36, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly Button _choose = new() { Content = "בחר צבע", MinWidth = 90 };
    private readonly Canvas _markerLayer = new() { IsHitTestVisible = false };
    private readonly Ellipse _marker = new() { Width = 10, Height = 10, Stroke = Brushes.White, StrokeThickness = 2, Fill = Brushes.Transparent };
    private double _s = 1, _v = 1;
    private bool _sync;
    public string ChosenColor { get; private set; } = "#7189BB";

    public AdvancedColorWindow(string initial)
    {
        Title = "בחירה מתקדמת"; Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FlowDirection = FlowDirection.RightToLeft;
        var root = new StackPanel { Margin = new Thickness(18) };
        root.Children.Add(new TextBlock { Text = "בחירת צבע", FontSize = 16, Margin = new Thickness(0, 0, 0, 12) });
        var spectrum = new Grid { ClipToBounds = true, FlowDirection = FlowDirection.LeftToRight };
        spectrum.Children.Add(_spectrum);
        var white = new LinearGradientBrush(Colors.White, Colors.Transparent, 0);
        spectrum.Children.Add(new Rectangle { Fill = white, IsHitTestVisible = false });
        spectrum.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Colors.Transparent, Colors.Black, 90), IsHitTestVisible = false });
        spectrum.MouseLeftButtonDown += (_, e) => { spectrum.CaptureMouse(); Pick(e.GetPosition(spectrum), spectrum.ActualWidth, spectrum.ActualHeight); };
        spectrum.MouseMove += (_, e) => { if (spectrum.IsMouseCaptured) Pick(e.GetPosition(spectrum), spectrum.ActualWidth, spectrum.ActualHeight); };
        spectrum.MouseLeftButtonUp += (_, _) => spectrum.ReleaseMouseCapture();
        _markerLayer.Children.Add(_marker); spectrum.Children.Add(_markerLayer);
        spectrum.SizeChanged += (_, _) => PositionMarker();
        System.Windows.Automation.AutomationProperties.SetName(spectrum, "Color spectrum");
        root.Children.Add(spectrum);
        root.Children.Add(new TextBlock { Text = "גוון", Margin = new Thickness(0, 10, 0, 2) });
        System.Windows.Automation.AutomationProperties.SetName(_hue, "Hue");
        _hue.FlowDirection = FlowDirection.LeftToRight;
        var rainbow = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
        for (int i = 0; i <= 6; i++) rainbow.GradientStops.Add(new GradientStop(Hsv(i * 60, 1, 1), i / 6d));
        _hue.Background = rainbow;
        _hue.ValueChanged += (_, _) => { if (!_sync) UpdateFromHsv(); };
        root.Children.Add(_hue);
        _preview.Margin = new Thickness(0, 8, 0, 8); root.Children.Add(_preview);
        var hexRow = new DockPanel(); hexRow.Children.Add(new TextBlock { Text = "קוד צבע", Width = 70, VerticalAlignment = VerticalAlignment.Center });
        _hex.TextChanged += (_, _) => { if (!_sync) ReadHex(); }; hexRow.Children.Add(_hex); root.Children.Add(hexRow);
        System.Windows.Automation.AutomationProperties.SetName(_hex, "Color hex");
        var rgbRow = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 8, 0, 8) };
        for (int i = 0; i < 3; i++)
        {
            rgbRow.Children.Add(new TextBlock { Text = new[] { "R", "G", "B" }[i], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _rgb[i].Width = 55; _rgb[i].Margin = new Thickness(0, 0, 10, 0); rgbRow.Children.Add(_rgb[i]);
            System.Windows.Automation.AutomationProperties.SetName(_rgb[i], new[] { "Red", "Green", "Blue" }[i]);
            _rgb[i].TextChanged += (_, _) => { if (!_sync) ReadRgb(); };
        }
        root.Children.Add(rgbRow); root.Children.Add(_error);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "ביטול", MinWidth = 70, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        cancel.SetResourceReference(StyleProperty, "HomeToolbarButton"); _choose.SetResourceReference(StyleProperty, "PrimaryActionButton");
        _choose.Click += (_, _) => { if (_choose.IsEnabled) DialogResult = true; };
        actions.Children.Add(cancel); actions.Children.Add(_choose); root.Children.Add(actions);
        Content = new DialogFrame { Child = root, Background = Brushes.White, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        SetColor(Catalog.ValidateColor(initial) == null && !string.IsNullOrEmpty(initial) ? initial : "#7189BB");
    }

    private void PositionMarker()
    {
        Canvas.SetLeft(_marker, _s * _spectrum.ActualWidth - 5);
        Canvas.SetTop(_marker, (1 - _v) * _spectrum.ActualHeight - 5);
    }
    private void Pick(Point p, double width, double height) { if (width <= 0 || height <= 0) return; _s = Math.Clamp(p.X / width, 0, 1); _v = 1 - Math.Clamp(p.Y / height, 0, 1); UpdateFromHsv(); }
    private static Color Hsv(double hue, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = hue switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    private void UpdateFromHsv() => SetColor(Hsv(_hue.Value, _s, _v).ToString()[3..].Insert(0, "#"), preserveHsv: true);
    private void Invalid() { _error.Text = "הזינו קוד צבע בן שש ספרות אחרי סולמית, או ערכי אדום, ירוק וכחול בין אפס למאתיים חמישים וחמש."; _choose.IsEnabled = false; }
    private void ReadHex() { if (Catalog.ValidateColor(_hex.Text) != null || _hex.Text.Length != 7) { Invalid(); return; } SetColor(_hex.Text); }
    private void ReadRgb()
    {
        var values = new byte[3];
        for (int i = 0; i < 3; i++) if (!byte.TryParse(_rgb[i].Text, NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) { Invalid(); return; }
        SetColor($"#{values[0]:X2}{values[1]:X2}{values[2]:X2}");
    }
    private static void SyncText(TextBox input, string value)
    {
        if (input.Text == value) return;
        int start = input.SelectionStart, length = input.SelectionLength;
        bool focused = input.IsKeyboardFocused;
        input.Text = value;
        if (focused) input.Select(Math.Min(start, value.Length), Math.Min(length, Math.Max(0, value.Length - start)));
    }
    private void SetColor(string hex, bool preserveHsv = false)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex); ChosenColor = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        _sync = true;
        try
        {
            if (!preserveHsv)
            {
                double r = c.R / 255d, g = c.G / 255d, b = c.B / 255d, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
                _v = max; _s = max == 0 ? 0 : delta / max;
                if (delta != 0) _hue.Value = ((max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60 + 360) % 360;
            }
            SyncText(_hex, ChosenColor); SyncText(_rgb[0], c.R.ToString()); SyncText(_rgb[1], c.G.ToString()); SyncText(_rgb[2], c.B.ToString());
            _preview.Background = new SolidColorBrush(c); _spectrum.Fill = new SolidColorBrush(Hsv(_hue.Value, 1, 1));
            PositionMarker(); _error.Text = ""; _choose.IsEnabled = true;
        }
        finally { _sync = false; }
    }
}
