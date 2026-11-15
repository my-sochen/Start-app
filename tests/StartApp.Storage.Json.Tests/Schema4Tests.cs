using StartApp.Core;
using StartApp.Storage.Json;
using Xunit;

/// <summary>Schema 4 = schema 3 + custom categories and the per-workspace card design (color, image path).
/// Older files load and upgrade with a one-time backup; newer files are still refused untouched.</summary>
public class Schema4Tests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    [Fact] public void Custom_categories_and_card_design_round_trip()
    {
        var p = Tmp();
        using (var repo = new JsonStore(p))
            repo.Mutate(c =>
            {
                c.AddCustomCategory("מוזיקה");
                var w = c.CreateWorkspace("w");
                c.SetCategory(w.Id, "מוזיקה");
                c.SetCardDesign(w.Id, "#BFDBFE", "C:\\img\\bg.png");
            });
        var text = File.ReadAllText(p);
        Assert.Contains("\"schemaVersion\": 4", text);
        Assert.Contains("customCategories", text); Assert.Contains("#BFDBFE", text); // non-ASCII names are escaped in the JSON
        using (var repo2 = new JsonStore(p))
        {
            var s = repo2.Load();
            Assert.Equal(4, s.SchemaVersion);
            Assert.Equal(new[] { "מוזיקה" }, s.CustomCategories);
            Assert.Equal("מוזיקה", s.Workspaces[0].Category);
            Assert.Equal("#BFDBFE", s.Workspaces[0].Color); Assert.Equal("C:\\img\\bg.png", s.Workspaces[0].ImagePath);
        }
    }

    [Fact] public void Schema3_file_loads_and_upgrades_with_a_backup()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "{\"schemaVersion\":3,\"items\":{},\"workspaces\":[{\"id\":\"w\",\"name\":\"n\",\"items\":[]}]}");
        using (var repo = new JsonStore(p))
        {
            var s = repo.Load();
            Assert.Empty(s.CustomCategories); // missing on older files = none
            repo.Mutate(c => c.AddCustomCategory("x"));
        }
        Assert.Contains("\"schemaVersion\": 4", File.ReadAllText(p));
        Assert.True(File.Exists(p + ".schema3.bak"));
        Assert.Contains("\"schemaVersion\":3", File.ReadAllText(p + ".schema3.bak"));
    }
}
