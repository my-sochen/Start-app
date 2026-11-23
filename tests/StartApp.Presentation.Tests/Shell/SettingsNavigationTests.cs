using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>The in-window הגדרות screen (approved settings handoff): opened from the top bar,
/// closed by חזרה לסביבות, independent of the overview/details navigation below it.</summary>
public class SettingsNavigationTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    [Fact] public void Settings_opens_and_closes_without_changing_the_screen_below()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.False(vm.InSettings);
        Assert.True(vm.OpenSettingsCommand.CanExecute(null));
        Assert.False(vm.CloseSettingsCommand.CanExecute(null));
        vm.OpenSettingsCommand.Execute(null);
        Assert.True(vm.InSettings);
        Assert.True(vm.InOverview); // settings covers, never replaces, the navigation below
        Assert.False(vm.OpenSettingsCommand.CanExecute(null));
        Assert.True(vm.CloseSettingsCommand.CanExecute(null));
        vm.CloseSettingsCommand.Execute(null);
        Assert.False(vm.InSettings);
        Assert.True(vm.InOverview);
    }
}
