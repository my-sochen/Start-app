using StartApp.Core;
using StartApp.Storage.Json;
using Xunit;

public class CategoryUpgradeTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "categories-" + Guid.NewGuid().ToString("N"), "store.json");
    private static string Legacy(string categories) => "{\"schemaVersion\":6,\"items\":{},\"workspaces\":[{\"id\":\"w\",\"name\":\"old\",\"category\":\"מוזיקה\",\"items\":[]}],\"customCategories\":" + categories + "}";

    [Fact] public void Legacy_same_name_becomes_builtin_with_assignments_kept_and_original_bytes_backed_up_before_save()
    {
        var path = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = System.Text.Encoding.UTF8.GetBytes(Legacy("[\"מוזיקה\",\"personal\"]")); File.WriteAllBytes(path, bytes);
        using var repo = new JsonStore(path); var loaded = repo.Load();
        Assert.Equal(new[] { "personal" }, loaded.CustomCategories); Assert.Equal("מוזיקה", loaded.Workspaces.Single().Category);
        Assert.Equal(bytes, File.ReadAllBytes(path)); Assert.False(File.Exists(path + ".categories.bak"));
        repo.Mutate(c => c.CreateWorkspace("new"));
        Assert.Equal(bytes, File.ReadAllBytes(path + ".categories.bak"));
        Assert.Equal(new[] { "personal" }, repo.Load().CustomCategories);
        Assert.Equal("מוזיקה", repo.Load().Workspaces.Single(x => x.Id == "w").Category);
    }

    [Theory] [InlineData("[\"מוזיקה\",\"מוזיקה\"]")] [InlineData("[\"כללי\"]")]
    public void Upgrade_does_not_hide_corrupt_duplicate_or_original_general_shadow(string categories)
    {
        var path = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(path)!); var body = Legacy(categories); File.WriteAllText(path, body);
        using var repo = new JsonStore(path); Assert.Throws<StoreCorruptException>(() => repo.Load()); Assert.Equal(body, File.ReadAllText(path));
    }

    [Fact] public void Existing_different_category_backup_is_preserved_and_hash_backup_keeps_current_original()
    {
        var path = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(path)!); var body = Legacy("[\"מוזיקה\"]"); File.WriteAllText(path, body);
        File.WriteAllText(path + ".categories.bak", "older backup");
        using var repo = new JsonStore(path); repo.Mutate(c => c.CreateWorkspace("new"));
        Assert.Equal("older backup", File.ReadAllText(path + ".categories.bak"));
        var backup = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "store.json.categories.*.bak")); Assert.Equal(body, File.ReadAllText(backup));
    }
}
