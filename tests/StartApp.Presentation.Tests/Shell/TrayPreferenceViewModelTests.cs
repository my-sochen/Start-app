using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;
public class TrayPreferenceViewModelTests
{
    [Fact] public void Preference_persists_and_failed_save_reverts_with_visible_error()
    {
        var path = Path.Combine(Path.GetTempPath(), "tray-vm-" + Guid.NewGuid().ToString("N"), "store.json");
        using var repo = new JsonStore(path); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.False(vm.KeepRunningInTray); vm.NewWorkspaceDescription = "unsaved draft"; vm.KeepRunningInTray = true;
        Assert.True(vm.CreateDirty); Assert.Equal("unsaved draft",vm.NewWorkspaceDescription);
        Assert.True(repo.Load().KeepRunningInTray); Assert.Empty(vm.TrayPreferenceError);
        using var other = new JsonStore(path); var blocked = new MainViewModel(other, new NotImplementedLauncher());
        blocked.KeepRunningInTray = false; Assert.True(blocked.KeepRunningInTray); Assert.NotEmpty(blocked.TrayPreferenceError);
        Assert.True(repo.Load().KeepRunningInTray);
    }
}
