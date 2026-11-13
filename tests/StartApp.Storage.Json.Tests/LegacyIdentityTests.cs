using StartApp.Core;
using StartApp.Storage.Json;
using Xunit;

/// <summary>PR19 review: legacy files (schema 1/2, memberships without ids) must get a STABLE in-memory
/// identity, so the first mutation after a load can address the exact instance the UI displayed. Before the
/// fix each load assigned a fresh random id and the first remove/edit/reorder threw KeyNotFoundException.</summary>
public class LegacyIdentityTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private const string Legacy2 = """{"schemaVersion":2,"items":{"it_a":{"id":"it_a","type":"file","name":"legacy file","target":"C:\\a.txt"}},"workspaces":[{"id":"ws_a","name":"old workspace","items":[{"itemId":"it_a"}]}]}""";
    private static string WriteLegacy() { var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, Legacy2); return p; }

    [Fact] public void Legacy_id_is_identical_across_loads_without_a_save()
    {
        var p = WriteLegacy();
        using var js = new JsonStore(p);
        var first = js.Load().Workspaces.Single().Items.Single().Id;
        var second = js.Load().Workspaces.Single().Items.Single().Id;
        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact] public void First_remove_after_legacy_load_addresses_the_displayed_id()
    {
        var p = WriteLegacy();
        using var js = new JsonStore(p);
        var displayed = js.Load().Workspaces.Single().Items.Single().Id!; // what the UI shows
        js.Mutate(c => c.RemoveInstance("ws_a", displayed)); // threw KeyNotFoundException before the fix
        Assert.Empty(js.Load().Workspaces.Single().Items);
    }

    [Fact] public void First_update_after_legacy_load_addresses_the_displayed_id()
    {
        var p = WriteLegacy();
        using var js = new JsonStore(p);
        var displayed = js.Load().Workspaces.Single().Items.Single().Id!;
        js.Mutate(c => c.UpdateInstance("ws_a", displayed, "renamed", "d"));
        var m = js.Load().Workspaces.Single().Items.Single();
        Assert.Equal("renamed", m.Name);
        Assert.Equal("d", m.Description);
    }

    private const string Legacy1 = """{"schemaVersion":1,"items":{"it_a":{"id":"it_a","type":"file","name":"legacy file","target":"C:\\a.txt"}},"workspaces":[{"id":"ws_a","name":"old workspace","items":[{"itemId":"it_a"}]}]}""";
    private const string LegacyTwo = """{"schemaVersion":2,"items":{"it_a":{"id":"it_a","type":"file","name":"f1","target":"C:\\a.txt"},"it_b":{"id":"it_b","type":"file","name":"f2","target":"C:\\b.txt"}},"workspaces":[{"id":"ws_a","name":"w","items":[{"itemId":"it_a"},{"itemId":"it_b"}]}]}""";
    private const string LegacyShared = """{"schemaVersion":2,"items":{"it_a":{"id":"it_a","type":"file","name":"shared","target":"C:\\a.txt"}},"workspaces":[{"id":"ws_a","name":"w1","items":[{"itemId":"it_a"}]},{"id":"ws_b","name":"w2","items":[{"itemId":"it_a"}]}]}""";
    private static string WriteText(string json) { var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, json); return p; }
    private static string Derived(string ws, string item) => "mi_" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ws + "\u0000" + item)))[..8].ToLowerInvariant();

    [Fact] public void Schema1_first_remove_addresses_the_displayed_id()
    {
        var p = WriteText(Legacy1);
        using var js = new JsonStore(p);
        var displayed = js.Load().Workspaces.Single().Items.Single().Id!;
        js.Mutate(c => c.RemoveInstance("ws_a", displayed));
        Assert.Empty(js.Load().Workspaces.Single().Items);
    }

    [Fact] public void Reading_a_legacy_file_never_writes_to_disk()
    {
        var p = WriteLegacy();
        using var js = new JsonStore(p);
        var before = File.ReadAllText(p);
        js.Load(); js.Load();
        Assert.Equal(before, File.ReadAllText(p)); // upgrade happens only on a real save
    }

    [Fact] public void First_reorder_after_legacy_load_addresses_the_displayed_id()
    {
        var p = WriteText(LegacyTwo);
        using var js = new JsonStore(p);
        var displayed = js.Load().Workspaces.Single().Items.Skip(1).Single().Id!; // second row
        js.Mutate(c => c.MoveInstance("ws_a", displayed, -1));
        Assert.Equal("it_b", js.Load().Workspaces.Single().Items.First().ItemId);
    }

    [Fact] public void Shared_item_in_two_workspaces_gets_distinct_stable_ids()
    {
        var p = WriteText(LegacyShared);
        using var js = new JsonStore(p);
        var s = js.Load();
        var idA = s.Workspaces.Single(w => w.Id == "ws_a").Items.Single().Id!;
        var idB = s.Workspaces.Single(w => w.Id == "ws_b").Items.Single().Id!;
        Assert.NotEqual(idA, idB);
        js.Mutate(c => c.RemoveInstance("ws_b", idB)); // first op on the second workspace uses its own id
        var after = js.Load();
        Assert.Empty(after.Workspaces.Single(w => w.Id == "ws_b").Items);
        Assert.Single(after.Workspaces.Single(w => w.Id == "ws_a").Items); // other workspace unaffected
    }

    [Fact] public void Collision_with_persisted_ids_allocates_deterministic_suffixes()
    {
        var d1 = Derived("ws_b", "it_a");
        var json = """{"schemaVersion":3,"items":{"it_x":{"id":"it_x","type":"file","name":"x","target":"C:\\x.txt"},"it_y":{"id":"it_y","type":"file","name":"y","target":"C:\\y.txt"},"it_a":{"id":"it_a","type":"file","name":"a","target":"C:\\a.txt"}},"workspaces":[{"id":"ws_a","name":"w1","items":[{"itemId":"it_x","id":"__D1__"},{"itemId":"it_y","id":"__D2__"}]},{"id":"ws_b","name":"w2","items":[{"itemId":"it_a"}]}]}"""
            .Replace("__D1__", d1).Replace("__D2__", d1 + "_2");
        var p = WriteText(json);
        using var js = new JsonStore(p);
        var legacy = js.Load().Workspaces.Single(w => w.Id == "ws_b").Items.Single();
        Assert.Equal(d1 + "_3", legacy.Id); // primary and _2 taken by persisted ids
        var all = js.Load().Workspaces.SelectMany(w => w.Items).Select(m => m.Id).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        js.Mutate(c => c.RemoveInstance("ws_b", legacy.Id!));
        Assert.Empty(js.Load().Workspaces.Single(w => w.Id == "ws_b").Items);
    }
}
