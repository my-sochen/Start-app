using StartApp.Core;
using Xunit;

public class DescriptionTests
{
    [Fact] public void New_item_keeps_description_and_none_is_null()
    {
        var c = new Catalog(new Store());
        Assert.Equal("my notes", c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt", "my notes").Description);
        Assert.Null(c.GetOrAddItem(ItemType.File, "b", "C:\\b.txt").Description);
        Assert.Null(c.GetOrAddItem(ItemType.File, "c", "C:\\c.txt", "").Description);
    }
    [Fact] public void Existing_description_is_never_overwritten_but_an_empty_one_is_filled()
    {
        var c = new Catalog(new Store());
        c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt", "first");
        Assert.Equal("first", c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt", "second").Description);
        c.GetOrAddItem(ItemType.File, "b", "C:\\b.txt");
        Assert.Equal("filled", c.GetOrAddItem(ItemType.File, "b", "C:\\b.txt", "filled").Description);
        Assert.Equal("filled", c.GetOrAddItem(ItemType.File, "b", "C:\\b.txt").Description);
        Assert.Equal(2, c.Store.Items.Count);
    }
    [Fact] public void Validation_rejects_too_long_and_control_characters_but_allows_line_breaks_and_unicode()
    {
        Assert.Null(Catalog.ValidateDescription(null)); Assert.Null(Catalog.ValidateDescription(""));
        Assert.Null(Catalog.ValidateDescription("שורה 1\r\nשורה 2\tסוף"));
        Assert.Null(Catalog.ValidateDescription(new string('x', Catalog.MaxDescriptionLength)));
        Assert.NotNull(Catalog.ValidateDescription(new string('x', Catalog.MaxDescriptionLength + 1)));
        Assert.NotNull(Catalog.ValidateDescription("bad\u0000char"));
        var c = new Catalog(new Store());
        Assert.Throws<ArgumentException>(() => c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt", "bad\u0007"));
        Assert.Empty(c.Store.Items);
    }
    [Fact] public void ReplaceTargetLocal_carries_the_description_to_the_new_item()
    {
        var c = new Catalog(new Store()); var w = c.CreateWorkspace("w");
        var it = c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt", "about a"); c.AddToWorkspace(w.Id, it.Id);
        var n = c.ReplaceTargetLocal(w.Id, it.Id, "C:\\a2.txt");
        Assert.Equal("about a", n.Description);
    }
    [Fact] public void Validate_reports_an_invalid_description_found_in_data()
    {
        var s = new Store(); s.Items["it_1"] = new Item("it_1", ItemType.File, "a", "C:\\a.txt", new string('x', 501));
        Assert.Contains(new Catalog(s).Validate(), e => e.Contains("description is longer"));
    }
    [Fact] public void Whitespace_only_description_means_none_but_a_real_one_is_stored_untrimmed()
    {
        var c = new Catalog(new Store());
        Assert.Null(c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt", "   \t ").Description);
        Assert.Equal(" x ", c.GetOrAddItem(ItemType.File, "b", "C:\\b.txt", " x ").Description);
    }
    [Fact] public void ReplaceTargetLocal_to_an_existing_item_keeps_that_items_own_description_and_drops_the_old_one()
    {
        var c = new Catalog(new Store()); var w = c.CreateWorkspace("w");
        var a = c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt", "about a"); c.AddToWorkspace(w.Id, a.Id);
        var b = c.GetOrAddItem(ItemType.File, "b", "C:\\b.txt", "about b"); var w2 = c.CreateWorkspace("w2"); c.AddToWorkspace(w2.Id, b.Id);
        var n = c.ReplaceTargetLocal(w.Id, a.Id, "C:\\b.txt");
        Assert.Equal(b.Id, n.Id); Assert.Equal("about b", n.Description); Assert.DoesNotContain(c.Store.Items.Values, i => i.Description == "about a");
    }
}
