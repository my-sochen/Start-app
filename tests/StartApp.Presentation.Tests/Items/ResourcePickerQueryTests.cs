using System.Text.Json;
using StartApp.Core;
using StartApp.Presentation;

public class ResourcePickerQueryTests
{
    private static Store Fixture()
    {
        var s = new Store();
        s.Items.Add("b", new("b", ItemType.Link, "Beta", "https://same.example/"));
        s.Items.Add("a", new("a", ItemType.Link, "Alpha", "https://alpha.example/"));
        s.Items.Add("case", new("case", ItemType.Link, "alpha", "https://case.example/"));
        s.Items.Add("f", new("f", ItemType.File, "File", @"C:\file.txt"));
        s.Workspaces.Add(new() { Id="w1", Items=new() { new("b", Name:"Local name"), new("b"), new("a") } });
        s.Workspaces.Add(new() { Id="w2", Items=new() { new("b") } });
        return s;
    }

    [Fact] public void Reads_original_names_and_counts_workspaces_not_duplicate_instances()
    {
        var rows = ResourcePickerQuery.Read(Fixture(), ItemType.Link, "").ToArray();
        Assert.Equal(new[] { "b", "a", "case" }, rows.Select(x=>x.ItemId));
        Assert.Equal("Beta", rows[0].Name);
        Assert.Equal(new[] { 2, 1, 0 }, rows.Select(x=>x.Usage));
        Assert.All(rows, x=>Assert.Equal(ItemType.Link,x.Type));
    }

    [Theory]
    [InlineData("BETA", "b")]
    [InlineData("SAME.EXAMPLE", "b")]
    [InlineData("case.example", "case")]
    public void Searches_names_and_targets_case_insensitively(string query, string id)
    {
        Assert.Equal(id, Assert.Single(ResourcePickerQuery.Read(Fixture(),ItemType.Link,query)).ItemId);
    }

    [Fact] public void Equal_usage_and_case_insensitive_name_keep_catalog_insertion_order()
    {
        var s=Fixture();s.Workspaces.Clear();
        Assert.Equal(new[] { "a", "case", "b" }, ResourcePickerQuery.Read(s,ItemType.Link,"").Select(x=>x.ItemId));
    }

    [Fact] public void Type_filter_and_read_leave_entire_store_unchanged()
    {
        var s=Fixture();var before=JsonSerializer.Serialize(s);
        Assert.Equal("f",Assert.Single(ResourcePickerQuery.Read(s,ItemType.File,"")).ItemId);
        Assert.Empty(ResourcePickerQuery.Read(s,ItemType.Folder,""));
        Assert.Equal(before,JsonSerializer.Serialize(s));
    }
}
