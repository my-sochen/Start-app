using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using StartApp.Presentation;
namespace StartApp.App;
public partial class SettingsView : UserControl
{
    private UpdateViewModel? _hooked;
    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { Detach(); Hook(); };
        Unloaded += (_, _) => Detach();
        Loaded += (_, _) => Hook();
    }
    private void Hook()
    {
        if (_hooked is not null || DataContext is not UpdateViewModel vm) { Apply(); return; }
        _hooked = vm;
        vm.PropertyChanged += OnUpdatesChanged;
        Apply();
    }
    private void Detach()
    {
        if (_hooked is null) return;
        _hooked.PropertyChanged -= OnUpdatesChanged;
        _hooked = null;
    }
    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UpdateViewModel.UpdateAvailable)) Apply();
    }
    // Two approved notice/action states: routine (up to date, manual check) and
    // update available (עדכן עכשיו primary + בדוק שוב secondary).
    private void Apply()
    {
        var available = (DataContext as UpdateViewModel)?.UpdateAvailable == true;
        UpToDateNotice.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        UpdateNotice.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        CheckButton.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        UpdateNowButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        RecheckButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }
}
