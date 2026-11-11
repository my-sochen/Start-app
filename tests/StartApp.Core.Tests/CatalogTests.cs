using StartApp.Core;
using Xunit;

public class CatalogTests
{
    private static (Catalog c, Workspace a, Workspace b) Two() { var c = new Catalog(new Store()); return (c, c.CreateWorkspace("work"), c.CreateWorkspace("studies")); }

    [Fact] public void SameTargetIsOneCatalogItem()
    {
        var (c, _, _) = Two();
        var x = c.GetOrAddItem(ItemType.File, "spec", "C:\\Docs\\Spec.docx");
        var y = c.GetOrAddItem(ItemType.File, "other name", "C:\\Docs\\Spec.docx");
        Assert.Throws<ArgumentException>(() => c.GetOrAddItem(ItemType.File, "padded", " C:\\Docs\\Spec.docx "));
        Assert.Equal(x.Id, y.Id); Assert.Single(c.Store.Items);
        var z = c.GetOrAddItem(ItemType.File, "case differs", "c:\\docs\\spec.docx");
        Assert.NotEqual(x.Id, z.Id); // conservative: no case folding / slash rewriting
    }
    [Fact] public void NoDuplicateMembership_AndLaunchConfigNotOverwritten()
    {
        var (c, a, _) = Two(); var i = c.GetOrAddItem(ItemType.File, "f", "C:/f.txt");
        Assert.True(c.AddToWorkspace(a.Id, i.Id, new LaunchConfig("C:\\Tools\\code.exe")));
        Assert.False(c.AddToWorkspace(a.Id, i.Id, new LaunchConfig("C:\\Tools\\notepad.exe")));
        Assert.Equal("C:\\Tools\\code.exe", a.Items.Single().Launch!.OpenWith);
    }
    [Fact] public void SameFileDifferentAppPerWorkspace()
    {
        var (c, a, b) = Two(); var i = c.GetOrAddItem(ItemType.File, "f", "C:/f.txt");
        c.AddToWorkspace(a.Id, i.Id, new LaunchConfig("C:\\Tools\\code.exe")); c.AddToWorkspace(b.Id, i.Id, new LaunchConfig("C:\\Tools\\notepad.exe"));
        Assert.Single(c.Store.Items);
        Assert.Equal("C:\\Tools\\notepad.exe", b.Items.Single().Launch!.OpenWith);
    }
    [Fact] public void RemoveLastReference_DeletesOrphan_ButSharedStays()
    {
        var (c, a, b) = Two(); var s = c.GetOrAddItem(ItemType.Folder, "p", "C:/p"); var o = c.GetOrAddItem(ItemType.Link, "l", "https://x.com/");
        c.AddToWorkspace(a.Id, s.Id); c.AddToWorkspace(b.Id, s.Id); c.AddToWorkspace(a.Id, o.Id);
        c.RemoveFromWorkspace(a.Id, s.Id); Assert.True(c.Store.Items.ContainsKey(s.Id));
        c.RemoveFromWorkspace(a.Id, o.Id); Assert.False(c.Store.Items.ContainsKey(o.Id));
        Assert.Empty(c.Validate());
    }
    [Fact] public void DeleteWorkspace_CleansOrphansOnly()
    {
        var (c, a, b) = Two(); var s = c.GetOrAddItem(ItemType.Folder, "p", "C:/p"); var u = c.GetOrAddItem(ItemType.Folder, "q", "C:/q");
        c.AddToWorkspace(a.Id, s.Id); c.AddToWorkspace(a.Id, u.Id); c.AddToWorkspace(b.Id, s.Id);
        c.DeleteWorkspace(a.Id); Assert.True(c.Store.Items.ContainsKey(s.Id)); Assert.False(c.Store.Items.ContainsKey(u.Id));
    }
    [Fact] public void LocalTargetReplace_DoesNotAffectOtherWorkspace_ReusesMatch_KeepsLaunch()
    {
        var (c, a, b) = Two(); var old = c.GetOrAddItem(ItemType.File, "f", "C:/old.txt"); var other = c.GetOrAddItem(ItemType.File, "g", "C:/new.txt");
        c.AddToWorkspace(a.Id, old.Id, new LaunchConfig("C:\\Tools\\x.exe")); c.AddToWorkspace(b.Id, old.Id);
        var r = c.ReplaceTargetLocal(a.Id, old.Id, "C:/new.txt");
        Assert.Equal(other.Id, r.Id); Assert.Equal("C:\\Tools\\x.exe", a.Items.Single().Launch!.OpenWith);
        Assert.Equal(old.Id, b.Items.Single().ItemId); Assert.True(c.Store.Items.ContainsKey(old.Id));
        c.ReplaceTargetLocal(b.Id, old.Id, "C:/z.txt"); Assert.False(c.Store.Items.ContainsKey(old.Id)); Assert.Empty(c.Validate());
    }
    [Fact] public void ReplaceToTargetAlreadyInWorkspace_KeepsOneMembership()
    {
        var (c, a, _) = Two(); var i1 = c.GetOrAddItem(ItemType.File, "1", "C:/1"); var i2 = c.GetOrAddItem(ItemType.File, "2", "C:/2");
        c.AddToWorkspace(a.Id, i1.Id); c.AddToWorkspace(a.Id, i2.Id);
        c.ReplaceTargetLocal(a.Id, i1.Id, "C:/2"); Assert.Single(a.Items); Assert.Empty(c.Validate());
    }
    [Fact] public void ReverseLookupByScan()
    {
        var (c, a, b) = Two(); var i = c.GetOrAddItem(ItemType.Link, "l", "https://x.com");
        c.AddToWorkspace(a.Id, i.Id); c.AddToWorkspace(b.Id, i.Id);
        Assert.Equal(2, c.WorkspacesContaining(i.Id).Count());
    }
}


public class IdentityAndLaunchTests
{
    [Fact] public void AmpersandAndSpacesInExecutablePathAreLegitimate_ButArgsAndAliasesAreNot()
    {
        var c = new Catalog(new Store()); var w = c.CreateWorkspace("w"); var f = c.GetOrAddItem(ItemType.File, "f", "C:\\a.txt");
        Assert.True(c.AddToWorkspace(w.Id, f.Id, new LaunchConfig("C:\\Tom & Jerry\\My Editor\\ed.exe")));
        var w2 = c.CreateWorkspace("w2");
        Assert.Throws<ArgumentException>(() => c.AddToWorkspace(w2.Id, f.Id, new LaunchConfig("code")));            // alias
        Assert.Throws<ArgumentException>(() => c.AddToWorkspace(w2.Id, f.Id, new LaunchConfig("C:\\x\\ed.exe ")));  // trailing space
        Assert.Throws<ArgumentException>(() => c.AddToWorkspace(w2.Id, f.Id, new LaunchConfig("..\\ed.exe")));       // relative
    }
    [Fact] public void LinkMustBeAbsoluteUri_AndTargetsAreNeverTrimmed()
    {
        var c = new Catalog(new Store());
        Assert.Throws<ArgumentException>(() => c.GetOrAddItem(ItemType.Link, "l", "example.com"));
        Assert.Throws<ArgumentException>(() => c.GetOrAddItem(ItemType.Link, "l", "https://x.com "));
        Assert.Throws<ArgumentException>(() => c.GetOrAddItem(ItemType.File, "f", ""));
    }
}

public class OwnedCloseTests
{
    private sealed class Spy : IItemCloser { public int Calls; public CloseOutcome TryCloseGracefully(string h) { Calls++; return CloseOutcome.Closed; } }

    [Fact] public void UnknownOwnership_NeverReachesAdapter()
    {
        var spy = new Spy();
        Assert.Equal(CloseOutcome.NotOwned, OwnedClose.Try(spy, new LaunchResult(LaunchStatus.Started, Ownership.Unknown, "h")));
        Assert.Equal(CloseOutcome.NotOwned, OwnedClose.Try(spy, new LaunchResult(LaunchStatus.Started, Ownership.Confirmed, null)));
        Assert.Equal(CloseOutcome.NotOwned, OwnedClose.Try(spy, new LaunchResult(LaunchStatus.Failed, Ownership.Confirmed, "h")));
        Assert.Equal(0, spy.Calls);
    }
    [Fact] public void ConfirmedOwnership_ReachesAdapter()
    {
        var spy = new Spy();
        Assert.Equal(CloseOutcome.Closed, OwnedClose.Try(spy, new LaunchResult(LaunchStatus.Started, Ownership.Confirmed, "h")));
        Assert.Equal(1, spy.Calls);
    }
}
