using System.IO;
using StartApp.Platform.Windows;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
namespace StartApp.App;

/// <summary>Launch selection with installed paths and the system default. Custom choices are deferred in beta.</summary>
public static class OpenWithPicker
{
    public sealed record Choice(string Label, string Value) { public override string ToString() => Label; }
    private const string FolderDefaultHint = "פתח באמצעות אינו זמין לתיקיות בגרסת הבטא.";
    private const string Browse = "\u0001browse";
    public static readonly DependencyProperty DefaultLabelProperty = DependencyProperty.RegisterAttached("DefaultLabel", typeof(string), typeof(OpenWithPicker), new PropertyMetadata("ברירת המחדל של Windows", Changed));
    public static string GetDefaultLabel(DependencyObject o) => (string)o.GetValue(DefaultLabelProperty);
    public static void SetDefaultLabel(DependencyObject o, string value) => o.SetValue(DefaultLabelProperty, value);
    public static readonly DependencyProperty FileTargetProperty = DependencyProperty.RegisterAttached("FileTarget", typeof(string), typeof(OpenWithPicker), new PropertyMetadata(null, Changed));
    public static string? GetFileTarget(DependencyObject o) => (string?)o.GetValue(FileTargetProperty);
    public static void SetFileTarget(DependencyObject o, string? value) => o.SetValue(FileTargetProperty, value);
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached("Path", typeof(string), typeof(OpenWithPicker), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty WebsiteTargetProperty = DependencyProperty.RegisterAttached("WebsiteTarget", typeof(string), typeof(OpenWithPicker), new PropertyMetadata("", Changed));
    public static string GetWebsiteTarget(DependencyObject o) => (string?)o.GetValue(WebsiteTargetProperty) ?? "";
    public static void SetWebsiteTarget(DependencyObject o, string value) => o.SetValue(WebsiteTargetProperty, value);
    private static bool IsWebsite(ComboBox combo) => GetFileTarget(combo) is null && Uri.TryCreate(GetWebsiteTarget(combo), UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https");
    private static string AssociationString(uint flags, uint kind, string association) => WindowsProtocolAssociations.Query(flags, kind, association);
    private static IReadOnlyList<Choice> RegisteredSitePrograms(string scheme)
    {
        var choices = new List<Choice>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var registered = root.OpenSubKey(@"SOFTWARE\RegisteredApplications");
                if (registered is null) continue;
                foreach (var entry in registered.GetValueNames())
                {
                    if (registered.GetValue(entry) is not string capabilityPath) continue;
                    using var protocols = root.OpenSubKey(capabilityPath + @"\URLAssociations");
                    if (protocols?.GetValue(scheme) is not string progId || string.IsNullOrWhiteSpace(progId)) continue;
                    var path = AssociationString(0x800 /* ASSOCF_INIT_FIXED_PROGID */, 2 /* ASSOCSTR_EXECUTABLE */, progId);
                    if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                    var label = AssociationString(0x800, 4, progId);
                    choices.Add(new(string.IsNullOrWhiteSpace(label) ? entry : label, path));
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        return choices;
    }
    private static string DisplayDefault(ComboBox combo)
    {
        var fallback = GetDefaultLabel(combo);
        if (GetFileTarget(combo) is not null || !Uri.TryCreate(GetWebsiteTarget(combo), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return fallback;
        // Read only the user's protocol association. Never change the stored launch path or system defaults.
        var name = AssociationString(0x1000 /* ASSOCF_IS_PROTOCOL */, 4 /* ASSOCSTR_FRIENDLYAPPNAME */, uri.Scheme);
        return string.IsNullOrWhiteSpace(name) ? fallback : name + " (ברירת מחדל)";
    }
    private static readonly DependencyProperty FileHandlersProperty = DependencyProperty.RegisterAttached("FileHandlers", typeof(IReadOnlyList<WindowsFileHandlers.Handler>), typeof(OpenWithPicker), new PropertyMetadata(null));
    private static void FileDropdownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo || GetFileTarget(combo) is not { } target) return;
        // Query once on demand, never while typing a target path or restoring a selection.
        combo.SetValue(FileHandlersProperty, WindowsFileHandlers.ForTarget(target));
        Populate(combo);
    }
    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached("Updating", typeof(bool), typeof(OpenWithPicker), new PropertyMetadata(false));
    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached("Hooked", typeof(bool), typeof(OpenWithPicker), new PropertyMetadata(false));
    public static string GetPath(DependencyObject o) => (string?)o.GetValue(PathProperty) ?? "";
    public static void SetPath(DependencyObject o, string value) => o.SetValue(PathProperty, value);
    public static string SelectedPath(ComboBox combo) => combo.SelectedItem is Choice c && c.Value != Browse ? c.Value : GetPath(combo);
    private static void Changed(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ComboBox combo) return;
        if (GetDefaultLabel(combo) == "ללא") combo.ToolTip = FolderDefaultHint;
        else if (Equals(combo.ToolTip, FolderDefaultHint)) combo.ClearValue(FrameworkElement.ToolTipProperty);
        if (!(bool)combo.GetValue(HookedProperty)) { combo.SetValue(HookedProperty, true); combo.SelectionChanged += Selected; combo.DropDownOpened += FileDropdownOpened; }
        if (e.Property == FileTargetProperty) combo.ClearValue(FileHandlersProperty);
        Populate(combo);
    }
    private static void Populate(ComboBox combo)
    {
        combo.SetValue(UpdatingProperty, true);
        try
        {
            var current = GetPath(combo);
            var choices = new List<Choice> { new(DisplayDefault(combo), "") };
            void Add(string label, string path) { if (File.Exists(path) && !choices.Any(c => c.Value.Equals(path, StringComparison.OrdinalIgnoreCase))) choices.Add(new(label, path)); }
            if (GetFileTarget(combo) is null)
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct())
            {
                Add("Google Chrome", System.IO.Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"));
                Add("Microsoft Edge", System.IO.Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
                Add("Mozilla Firefox", System.IO.Path.Combine(root, "Mozilla Firefox", "firefox.exe"));
            }
            if (IsWebsite(combo))
            {
                foreach (var program in RegisteredSitePrograms(new Uri(GetWebsiteTarget(combo)).Scheme)) Add(program.Label, program.Value);
            }
            else if (GetFileTarget(combo) is not null)
                foreach (var handler in (IReadOnlyList<WindowsFileHandlers.Handler>?)combo.GetValue(FileHandlersProperty) ?? Array.Empty<WindowsFileHandlers.Handler>()) Add(handler.Label, handler.Path);
            else Add("Notepad", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe"));
            if (current.Length > 0 && !choices.Any(c => c.Value.Equals(current, StringComparison.OrdinalIgnoreCase))) choices.Add(new(current, current));
            choices.Add(new(IsWebsite(combo) || GetFileTarget(combo) is not null ? "אחר..." : "בחירת תוכנה אחרת...", Browse));
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
            var website = IsWebsite(combo);
            if (website || GetFileTarget(combo) is not null)
            {
                // Other is an action, not a saved launch preference. Restore selection before
                // the modal message so binding and dirty state never see a sentinel value.
                Populate(combo);
                MessageBox.Show(Window.GetWindow(combo), "בקרוב", "פתח באמצעות", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                return;
            }
            var picker = new OpenFileDialog { Title = website ? "בחירת תוכנה לפתיחת אתרים (http/https)" : "בחירת תוכנה לפתיחת האייטם", Filter = website ? "Programs (*.exe)|*.exe" : "Programs (*.exe)|*.exe|All files (*.*)|*.*", CheckFileExists = true };
            if (picker.ShowDialog(Window.GetWindow(combo)) != true) { Populate(combo); return; }
            path = picker.FileName;
        }
        combo.SetCurrentValue(PathProperty, path);
        Populate(combo);
    }
}
