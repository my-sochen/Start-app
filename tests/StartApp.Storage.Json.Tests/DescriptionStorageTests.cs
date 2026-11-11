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
}
