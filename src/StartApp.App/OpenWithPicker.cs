using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace StartApp.App;

/// <summary>Launch selection with real installed paths, the system default, and a file chooser.</summary>
public static class OpenWithPicker
{
    public sealed record Choice(string Label, string Value) { public override string ToString() => Label; }
    private const string Browse = "\u0001browse";
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached("Path", typeof(string), typeof(OpenWithPicker), new PropertyMetadata(null, Changed));
    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached("Updating", typeof(bool), typeof(OpenWithPicker), new PropertyMetadata(false));
    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(OpenWithPicker), new PropertyMetadata(false));
    public static string GetPath(DependencyObject o) => (string?)o.GetValue(PathProperty) ?? "";
    public static void SetPath(DependencyObject o, string value) => o.SetValue(PathProperty, value);
    public static string SelectedPath(ComboBox combo) => combo.SelectedItem is Choice c && c.Value != Browse ? c.Value : GetPath(combo);
    private static void Changed(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ComboBox combo) return;
        if (!(bool)combo.GetValue(HookedProperty)) { combo.SetValue(HookedProperty, true); combo.SelectionChanged += Selected; }
        Populate(combo);
    }
    private static void Populate(ComboBox combo)
    {
        combo.SetValue(UpdatingProperty, true);
        try
        {
            var current = GetPath(combo);
            var choices = new List<Choice> { new("ברירת המחדל של Windows", "") };
            void Add(string label, string path) { if (File.Exists(path) && !choices.Any(c => c.Value.Equals(path, StringComparison.OrdinalIgnoreCase))) choices.Add(new(label, path)); }
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
            {
                Add("Google Chrome", System.IO.Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"));
                Add("Microsoft Edge", System.IO.Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
                Add("Mozilla Firefox", System.IO.Path.Combine(root, "Mozilla Firefox", "firefox.exe"));
            }
            Add("Notepad", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"));
            if (current.Length > 0 && !choices.Any(c => c.Value.Equals(current, StringComparison.OrdinalIgnoreCase))) choices.Add(new(current, current));
            choices.Add(new("בחירת תוכנה אחרת...", Browse));
            combo.DisplayMemberPath = nameof(Choice.Label); combo.SelectedValuePath = nameof(Choice.Value); combo.ItemsSource = choices; combo.SelectedValue = current;
        }
        finally { combo.SetValue(UpdatingProperty, false); }
    }
    private static void Selected(object sender, SelectionChangedEventArgs e)
    {
        var combo = (ComboBox)sender;
        if ((bool)combo.GetValue(UpdatingProperty) || combo.SelectedItem is not Choice choice) return;
        var path = choice.Value;
        if (path == Browse)
        {
            var picker = new OpenFileDialog { Title = "בחירת תוכנה לפתיחת האייטם", Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*", CheckFileExists = true };
            if (picker.ShowDialog(Window.GetWindow(combo)) != true) { Populate(combo); return; }
            path = picker.FileName;
        }
        combo.SetCurrentValue(PathProperty, path);
        Populate(combo);
    }
}
