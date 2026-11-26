using StartApp.Core;
using StartApp.Storage.Json;
using Xunit;

public class TrayPreferenceTests
{
    static string PathName() => Path.Combine(Path.GetTempPath(), "tray-" + Guid.NewGuid().ToString("N"), "store.json");
    [Fact] public void Old_store_defaults_off_and_is_backed_up_before_preference_upgrade()
    {
        var path = PathName(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var original = "{\"schemaVersion\":6,\"items\":{},\"workspaces\":[]}"; File.WriteAllText(path, original);
        using var repo = new JsonStore(path); Assert.False(repo.Load().KeepRunningInTray); Assert.Equal(original, File.ReadAllText(path));
        repo.Mutate(c => c.SetKeepRunningInTray(true)); Assert.True(repo.Load().KeepRunningInTray);
        Assert.Equal(original, File.ReadAllText(path + ".schema6.bak"));
        repo.Mutate(c => c.CreateWorkspace("unrelated change")); Assert.True(repo.Load().KeepRunningInTray);
        repo.Mutate(c => c.SetKeepRunningInTray(false)); Assert.False(repo.Load().KeepRunningInTray);
    }
}
