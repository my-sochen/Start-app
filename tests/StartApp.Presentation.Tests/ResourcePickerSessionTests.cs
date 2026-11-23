using StartApp.Core;
using StartApp.Presentation;

public class ResourcePickerSessionTests
{
    private static Store Store()
    {
        var store = new Store(); var c = new Catalog(store);
        c.GetOrAddItem(ItemType.Link, "website", "https://example.org");
        c.GetOrAddItem(ItemType.File, "file", @"C:\file.txt");
        return store;
    }
    [Fact] public void Closed_by_default_with_link_type_empty_search_and_no_results()
    {
        var s = new ResourcePickerSession(); Assert.False(s.IsOpen); Assert.Equal(ItemType.Link, s.Type);
        Assert.Equal("", s.Search); Assert.Empty(s.Read(Store()));
    }
    [Fact] public void Query_uses_current_type_and_search_only_when_open_without_writing_store()
    {
        var store = Store(); var s = new ResourcePickerSession { IsOpen = true };
        Assert.Equal("website", Assert.Single(s.Read(store)).Name);
        s.Type = ItemType.File; Assert.Equal("file", Assert.Single(s.Read(store)).Name);
        s.Search = "missing"; Assert.Empty(s.Read(store));
        s.Search = "FILE.TXT"; Assert.Equal("file", Assert.Single(s.Read(store)).Name);
        Assert.Equal(2, store.Items.Count); Assert.Empty(store.Workspaces);
    }
    [Fact] public void Close_does_not_reset_type_or_search_draft()
    {
        var s = new ResourcePickerSession { IsOpen = true, Type = ItemType.File, Search = "file" };
        s.IsOpen = false; Assert.Empty(s.Read(Store()));
        Assert.Equal(ItemType.File, s.Type); Assert.Equal("file", s.Search);
        s.IsOpen = true; Assert.Single(s.Read(Store()));
    }
    [Fact] public void Sessions_keep_independent_visibility_type_and_search()
    {
        var a = new ResourcePickerSession { IsOpen = true, Type = ItemType.File, Search = "file" };
        var b = new ResourcePickerSession { IsOpen = true };
        Assert.Equal(ItemType.File, Assert.Single(a.Read(Store())).Type);
        Assert.Equal(ItemType.Link, Assert.Single(b.Read(Store())).Type);
        a.IsOpen = false; Assert.Single(b.Read(Store())); Assert.Equal("", b.Search);
    }
}
