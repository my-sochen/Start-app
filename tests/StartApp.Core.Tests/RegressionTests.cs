using StartApp.Core;
using Xunit;

public class IdCollisionTests
{
    [Fact] public void ItemIdCollision_DoesNotOverwriteExistingItem()
    {
        var parts = new Queue<string>(new[] { "same", "same", "other" });
        var c = new Catalog(new Store(), () => parts.Dequeue());
        var a = c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt");
        var b = c.GetOrAddItem(ItemType.File, "b", "C:\\b.txt");
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(2, c.Store.Items.Count);
        Assert.Equal("C:\\a.txt", c.Store.Items[a.Id].Target);
    }
    [Fact] public void WorkspaceIdCollision_GetsDistinctId()
    {
        var parts = new Queue<string>(new[] { "same", "same", "other" });
        var c = new Catalog(new Store(), () => parts.Dequeue());
        var a = c.CreateWorkspace("a"); var b = c.CreateWorkspace("b");
        Assert.NotEqual(a.Id, b.Id); Assert.Empty(c.Validate());
    }
}
