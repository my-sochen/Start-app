using StartApp.Core;
using StartApp.Presentation;

public class WorkspaceViewSessionTests
{
    [Fact] public void Defaults_match_overview_and_whitespace_filter_is_inactive()
    {
        var s = new WorkspaceViewSession(); Assert.Equal(ViewSort.RecentEntries, s.Sort); Assert.Equal(CardSizeOption.Medium, s.CardSize); Assert.Equal(ViewTemplate.Cards, s.Template);
        s.ContainsText = "  "; Assert.False(s.HasActiveFilters); Assert.Empty(s.Chips());
    }
    [Fact] public void Filter_dimensions_combine_and_categories_and_colors_ignore_case()
    {
        var a = new Workspace { Id = "a", Name = "match", Category = "Work", Color = "#AABBCC" };
        var b = new Workspace { Id = "b", Name = "match", Category = "Other", Color = "#AABBCC" };
        var c = new Workspace { Id = "c", Name = "match", Category = "Work", Color = "#000000" };
        var s = new WorkspaceViewSession(); s.Categories.Add("work"); s.Colors.Add("#aabbcc"); s.ContainsText = " MATCH ";
        Assert.Same(a, Assert.Single(s.Query(new[] { a, b, c }, new Store()))); Assert.Equal(" MATCH ", s.ContainsText);
    }
    [Fact] public void Content_filter_is_exact_identity_and_single_mode_ignores_whitespace_text()
    {
        var a = new Workspace { Id = "a", Items = new() { new Membership("i") } }; var b = new Workspace { Id = "b", Items = new() { new Membership("I") } };
        var s = new WorkspaceViewSession { ItemId = "i", ItemLabel = "item", ContainsText = " " };
        Assert.True(s.IsSingleContentFilter); Assert.True(s.HasActiveFilters); Assert.Same(a, Assert.Single(s.Query(new[] { a, b }, new Store())));
        s.Categories.Add("c"); Assert.False(s.IsSingleContentFilter);
    }
    [Fact] public void Text_uses_local_name_description_before_catalog_fallback_and_also_target()
    {
        var store = new Store(); store.Items["i"] = new Item("i", ItemType.File, "catalog", @"C:\needle.txt", "catalog description");
        var w = new Workspace { Name = "w", Items = new() { new Membership("i", Name: "local", Description: "local description") } };
        var s = new WorkspaceViewSession { ContainsText = "CATALOG" }; Assert.Empty(s.Query(new[] { w }, store));
        s.ContainsText = "local description"; Assert.Single(s.Query(new[] { w }, store));
        s.ContainsText = "needle"; Assert.Single(s.Query(new[] { w }, store));
    }
    [Theory] [InlineData(ViewSort.CreationDate)] [InlineData(ViewSort.ItemCount)] [InlineData(ViewSort.LaunchCount)] [InlineData(ViewSort.RecentEntries)]
    public void Descending_sorts_keep_original_order_on_ties(ViewSort sort)
    {
        var old = new Workspace { Id = "old", Name = "old" }; var a = new Workspace { Id = "a", Name = "a", CreatedAt = DateTime.UtcNow, LastOpenedAt = DateTime.UtcNow, LaunchCount = 2, Items = new() { new Membership("i") } };
        var b = new Workspace { Id = "b", Name = "b", CreatedAt = a.CreatedAt, LastOpenedAt = a.LastOpenedAt, LaunchCount = a.LaunchCount, Items = new() { new Membership("i") } };
        var s = new WorkspaceViewSession { Sort = sort }; Assert.Equal(new[] { "a", "b", "old" }, s.Query(new[] { old, a, b }, new Store()).Select(w => w.Id));
    }
    [Fact] public void Chips_keep_color_category_item_text_order_and_exact_keys()
    {
        var s = new WorkspaceViewSession { ItemId = "id", ItemLabel = "type: item", ContainsText = "  word  " }; s.Categories.Add("work"); s.Colors.Add("#FFFFFF");
        Assert.Equal(new[] { "color:#FFFFFF", "cat:work", "item", "contains" }, s.Chips().Select(c => c.Key)); Assert.Equal("מכיל: word", s.Chips().Last().Label);
    }
    [Fact] public void Sessions_do_not_share_filters_and_query_does_not_mutate_input()
    {
        var a = new WorkspaceViewSession(); var b = new WorkspaceViewSession(); a.Categories.Add("x"); a.ItemId = "id";
        Assert.Empty(b.Categories); Assert.False(b.HasActiveFilters); var rows = new[] { new Workspace { Name = "one" } }; Assert.Empty(a.Query(rows, new Store())); Assert.Single(rows);
    }
}
