using System.Collections.Specialized;
using StartApp.Presentation;

public class StagedItemCollectionTests
{
    private sealed record Row(string Name, string Group);
    [Theory] [InlineData(1)] [InlineData(100)]
    public void Moves_to_next_same_group_skipping_other_groups_without_replacing_row(int delta)
    {
        var a = new Row("a", "links"); var b = new Row("b", "files"); var c = new Row("c", "links");
        var rows = new StagedItemCollection<Row>(r => r.Group); rows.Rows.Add(a); rows.Rows.Add(b); rows.Rows.Add(c);
        var events = new List<NotifyCollectionChangedEventArgs>(); rows.Rows.CollectionChanged += (_, e) => events.Add(e);
        Assert.True(rows.MoveWithinGroup(a, delta)); Assert.Equal(new[] { b, c, a }, rows.Rows); Assert.Same(a, rows.Rows[2]);
        var change = Assert.Single(events); Assert.Equal(NotifyCollectionChangedAction.Move, change.Action); Assert.Equal(0, change.OldStartingIndex); Assert.Equal(2, change.NewStartingIndex);
    }
    [Theory] [InlineData(-1)] [InlineData(-100)]
    public void Moves_up_to_previous_same_group(int delta)
    {
        var a = new Row("a", "links"); var b = new Row("b", "files"); var c = new Row("c", "links");
        var rows = new StagedItemCollection<Row>(r => r.Group); rows.Rows.Add(a); rows.Rows.Add(b); rows.Rows.Add(c);
        Assert.True(rows.MoveWithinGroup(c, delta)); Assert.Equal(new[] { c, a, b }, rows.Rows);
    }
    [Fact] public void Group_edges_zero_delta_and_unknown_row_are_noops_without_collection_events()
    {
        var a = new Row("a", "links"); var b = new Row("b", "files"); var c = new Row("c", "links");
        var rows = new StagedItemCollection<Row>(r => r.Group); rows.Rows.Add(a); rows.Rows.Add(b); rows.Rows.Add(c); var changes = 0;
        rows.Rows.CollectionChanged += (_, _) => changes++;
        Assert.False(rows.MoveWithinGroup(a, -1)); Assert.False(rows.MoveWithinGroup(c, 1)); Assert.False(rows.MoveWithinGroup(b, 1));
        Assert.False(rows.MoveWithinGroup(b, -1)); Assert.False(rows.MoveWithinGroup(a, 0)); Assert.False(rows.MoveWithinGroup(new Row("missing", "links"), 1));
        Assert.Equal(0, changes); Assert.Equal(new[] { a, b, c }, rows.Rows);
    }
    [Fact] public void Groups_use_exact_case_sensitive_identity()
    {
        var a = new Row("a", "links"); var b = new Row("b", "Links");
        var rows = new StagedItemCollection<Row>(r => r.Group); rows.Rows.Add(a); rows.Rows.Add(b);
        Assert.False(rows.MoveWithinGroup(a, 1)); Assert.False(rows.MoveWithinGroup(b, -1));
    }
    [Fact] public void Empty_collection_and_separate_owners_have_no_shared_rows_or_order()
    {
        var a = new StagedItemCollection<Row>(r => r.Group); var b = new StagedItemCollection<Row>(r => r.Group);
        var row = new Row("a", "links"); Assert.False(a.MoveWithinGroup(row, 1)); a.Rows.Add(row); Assert.Empty(b.Rows);
    }
}
