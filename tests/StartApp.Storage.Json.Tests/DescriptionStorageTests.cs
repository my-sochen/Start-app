using StartApp.Core;
using StartApp.Storage.Json;
using Xunit;

public class DescriptionStorageTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    [Fact] public void Old_file_without_description_loads_with_null_and_is_not_rewritten_by_load()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var old = "{\"schemaVersion\":1,\"items\":{\"it_1\":{\"id\":\"it_1\",\"type\":\"file\",\"name\":\"a\",\"target\":\"C:\\\\a.txt\"}},\"workspaces\":[{\"id\":\"w\",\"name\":\"n\",\"items\":[{\"itemId\":\"it_1\"}]}]}";
        File.WriteAllText(p, old);
        var s = new JsonStore(p).Load();
        Assert.Null(s.Items["it_1"].Description); Assert.Equal("a", s.Items["it_1"].Name);
        Assert.Equal(old, File.ReadAllText(p));
    }
    [Fact] public void Description_round_trips_incl_hebrew_and_line_breaks_and_old_items_stay_without_one()
    {
        var p = Tmp(); var js = new JsonStore(p); var d = "תיאור\nשורה שנייה";
        js.Mutate(c => { var w = c.CreateWorkspace("w"); c.AddToWorkspace(w.Id, c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt", d).Id); c.AddToWorkspace(w.Id, c.GetOrAddItem(ItemType.File, "b", "C:\\b.txt").Id); });
        var s = new JsonStore(p).Load();
        Assert.Equal(d, s.Items.Values.Single(i => i.Name == "a").Description); Assert.Null(s.Items.Values.Single(i => i.Name == "b").Description);
        Assert.DoesNotContain("\"description\":null", File.ReadAllText(p).Replace(" ", ""));
    }
    [Fact] public void Schema1_file_is_upgraded_to_2_on_save_with_a_one_time_byte_exact_backup()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var old = "{\"schemaVersion\":1,\"items\":{\"it_1\":{\"id\":\"it_1\",\"type\":\"file\",\"name\":\"a\",\"target\":\"C:\\\\a.txt\"}},\"workspaces\":[{\"id\":\"w\",\"name\":\"n\",\"items\":[{\"itemId\":\"it_1\"}]}]}";
        File.WriteAllText(p, old);
        using (var js = new JsonStore(p)) js.Mutate(c => c.CreateWorkspace("second"));
        Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(p)); Assert.Equal(old, File.ReadAllText(p + ".schema1.bak"));
        using (var js = new JsonStore(p)) { var s = js.Load(); Assert.Equal(2, s.Workspaces.Count); Assert.Equal("a", s.Items["it_1"].Name); js.Mutate(c => c.CreateWorkspace("third")); }
        Assert.Equal(old, File.ReadAllText(p + ".schema1.bak"));   // never overwritten by later saves
    }
    [Fact] public void Fresh_store_writes_schema2_and_makes_no_backup()
    {
        var p = Tmp(); using var js = new JsonStore(p); js.Mutate(c => c.CreateWorkspace("w"));
        Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(p)); Assert.False(File.Exists(p + ".schema1.bak"));
    }
    [Fact] public void Schema3_file_is_too_new_and_left_untouched()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!); var t = "{\"schemaVersion\":3,\"items\":{},\"workspaces\":[]}"; File.WriteAllText(p, t);
        Assert.Throws<StoreTooNewException>(() => new JsonStore(p).Load()); Assert.Equal(t, File.ReadAllText(p));
    }
    [Fact] public void Backup_failure_stops_the_upgrade_and_leaves_the_schema1_file_untouched()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var old = "{\"schemaVersion\":1,\"items\":{},\"workspaces\":[]}"; File.WriteAllText(p, old);
        Directory.CreateDirectory(p + ".schema1.bak");   // the backup name is taken by a folder: the copy cannot succeed
        using var js = new JsonStore(p);
        Assert.ThrowsAny<Exception>(() => js.Mutate(c => c.CreateWorkspace("w")));
        Assert.Equal(old, File.ReadAllText(p)); Assert.False(File.Exists(p + ".tmp"));
        Assert.Equal(0, new JsonStore(p).Load().Workspaces.Count);
    }
}
