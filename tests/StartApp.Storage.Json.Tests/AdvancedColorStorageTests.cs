using StartApp.Core;
using StartApp.Storage.Json;
using Xunit;

public class AdvancedColorStorageTests
{
    static string PathName() => Path.Combine(Path.GetTempPath(), "color-store-" + Guid.NewGuid().ToString("N"), "store.json");
    [Fact] public void History_round_trips_case_insensitively_in_first_choice_order_and_survives_card_deletion()
    {
        var path = PathName();
        using (var repo = new JsonStore(path))
        {
            repo.Mutate(c => { WorkspaceColors.Remember(c.Store, "#aabbcc"); WorkspaceColors.Remember(c.Store, "#112233"); WorkspaceColors.Remember(c.Store, "#AABBCC"); });
        }
        using var reopened = new JsonStore(path);
        Assert.Equal(new[] { "#AABBCC", "#112233" }, reopened.Load().AdvancedColorHistory); Assert.Empty(reopened.Load().Workspaces);
    }
    [Fact] public void Schema5_file_is_unchanged_on_read_and_byte_exact_backed_up_before_history_write()
    {
        var path = PathName(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var old = "{\"schemaVersion\":5,\"items\":{},\"workspaces\":[]}"; File.WriteAllText(path, old);
        using var repo = new JsonStore(path); Assert.Empty(repo.Load().AdvancedColorHistory); Assert.Equal(old, File.ReadAllText(path));
        repo.Mutate(c => WorkspaceColors.Remember(c.Store, "#123456"));
        Assert.Equal(old, File.ReadAllText(path + ".schema5.bak")); Assert.Equal(JsonStore.SupportedSchema, repo.Load().SchemaVersion);
    }
    [Theory] [InlineData("null")] [InlineData("[null]")] [InlineData("[\"#xyzxyz\"]")] [InlineData("[\"#aabbcc\",\"#AABBCC\"]")]
    public void Invalid_history_is_refused_without_overwriting_original(string history)
    {
        var path = PathName(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var old = "{\"schemaVersion\":6,\"items\":{},\"workspaces\":[],\"advancedColorHistory\":" + history + "}"; File.WriteAllText(path, old);
        using var repo = new JsonStore(path); Assert.Throws<StoreCorruptException>(() => repo.Load()); Assert.Equal(old, File.ReadAllText(path));
    }
    [Fact] public void Invalid_history_mutation_cannot_change_disk()
    {
        var path = PathName(); using var repo = new JsonStore(path); repo.Mutate(c => c.CreateWorkspace("original")); var before = File.ReadAllBytes(path);
        Assert.Throws<ArgumentException>(() => repo.Mutate(c => WorkspaceColors.Remember(c.Store, "red")));
        Assert.Equal(before, File.ReadAllBytes(path));
    }
}
