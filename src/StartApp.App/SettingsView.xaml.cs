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
        if (e.PropertyName is nameof(UpdateViewModel.UpdateAvailable) or nameof(UpdateViewModel.IsUpToDate) or nameof(UpdateViewModel.Status)) Apply();
    }
    // Two approved notice/action states: routine (up to date, manual check) and
    // update available (עדכן עכשיו primary + בדוק שוב secondary).
    private void Apply()
    {
        var available = (DataContext as UpdateViewModel)?.UpdateAvailable == true;
        UpToDateNotice.Visibility = (DataContext as UpdateViewModel)?.IsUpToDate == true ? Visibility.Visible : Visibility.Collapsed;
        var vm = DataContext as UpdateViewModel;
        UncheckedNotice.Visibility = !available && vm?.IsUpToDate != true ? Visibility.Visible : Visibility.Collapsed;
        StatusLine.Visibility = UncheckedNotice.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        UncheckedText.Text = vm?.CheckCommand.CanExecute(null) == true && vm.Status == "בדיקת עדכונים זמינה לפי בקשה." ? "עדיין לא נבדקו עדכונים בהפעלה זו. לחיצה על בדוק עדכונים תבדוק עכשיו." : vm?.Status ?? "טרם נבדק";
        UpdateNotice.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        CheckButton.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        UpdateNowButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        RecheckButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }
}
