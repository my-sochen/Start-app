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
    [Fact] public void DuplicateAdds_CreateIndependentInstances_OlderInstanceNeverModified()
    {
        var (c, a, _) = Two(); var i = c.GetOrAddItem(ItemType.File, "f", "C:/f.txt");
        var m1 = c.AddToWorkspace(a.Id, i.Id, new LaunchConfig("C:\\Tools\\code.exe"), "first", "d1");
        var m2 = c.AddToWorkspace(a.Id, i.Id, new LaunchConfig("C:\\Tools\\notepad.exe"), "second");
        Assert.NotEqual(m1.Id, m2.Id); Assert.Equal(2, a.Items.Count); Assert.Single(c.Store.Items);
        Assert.Equal("C:\\Tools\\code.exe", a.Items[0].Launch!.OpenWith); Assert.Equal("first", a.Items[0].Name); Assert.Equal("d1", a.Items[0].Description);
        Assert.Equal("C:\\Tools\\notepad.exe", a.Items[1].Launch!.OpenWith); Assert.Equal("second", a.Items[1].Name); Assert.Null(a.Items[1].Description);
        Assert.Empty(c.Validate());
        c.RemoveInstance(a.Id, m1.Id);
        Assert.Single(a.Items); Assert.Equal(m2.Id, a.Items[0].Id); Assert.True(c.Store.Items.ContainsKey(i.Id)); // still referenced by m2
        c.RemoveInstance(a.Id, m2.Id); Assert.False(c.Store.Items.ContainsKey(i.Id)); // orphan cleanup after the last instance
    }
    [Fact] public void HasTarget_PerWorkspace_AndInstanceValidation()
    {
        var (c, a, b) = Two(); var i = c.GetOrAddItem(ItemType.Link, "l", "https://x.com/");
        Assert.False(c.HasTarget(a.Id, ItemType.Link, "https://x.com/"));
        c.AddToWorkspace(a.Id, i.Id);
        Assert.True(c.HasTarget(a.Id, ItemType.Link, "https://x.com/"));
        Assert.False(c.HasTarget(b.Id, ItemType.Link, "https://x.com/"));   // another workspace: no duplicate warning there
        Assert.False(c.HasTarget(a.Id, ItemType.File, "https://x.com/"));   // identity includes the type
        Assert.Throws<ArgumentException>(() => c.AddToWorkspace(b.Id, i.Id, null, "   "));
        Assert.Throws<ArgumentException>(() => c.AddToWorkspace(b.Id, i.Id, null, null, new string('x', 501)));
    }
    [Fact] public void OlderFile_InstancesWithoutId_GetIds_OnCatalogLoad()
    {
        var s = new Store();
        s.Items["it_1"] = new Item("it_1", ItemType.File, "a", "C:\\a.txt");
        var w = new Workspace { Id = "ws_1", Name = "w" }; w.Items.Add(new Membership("it_1")); s.Workspaces.Add(w);
        var c = new Catalog(s);
        Assert.NotNull(w.Items[0].Id); Assert.Empty(c.Validate());
    }
    [Fact] public void Category_Defaults_AndEmptyIsInvalid()
    {
        var c = new Catalog(new Store());
        var w = c.CreateWorkspace("plain");
        Assert.Equal("כללי", w.EffectiveCategory);
        var x = c.CreateWorkspace("music", null, "music projects");
        Assert.Equal("music projects", x.EffectiveCategory);
        x.Category = "  "; Assert.Contains(c.Validate(), e => e.Contains("category is empty"));
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
        var m1 = c.AddToWorkspace(a.Id, old.Id, new LaunchConfig("C:\\Tools\\x.exe")); var m2 = c.AddToWorkspace(b.Id, old.Id);
        var r = c.ReplaceTargetLocal(a.Id, m1.Id, "C:/new.txt");
        Assert.Equal(other.Id, r.Id); Assert.Equal("C:\\Tools\\x.exe", a.Items.Single().Launch!.OpenWith);
        Assert.Equal(other.Id, a.Items.Single().ItemId);
        Assert.Equal(old.Id, b.Items.Single().ItemId); Assert.True(c.Store.Items.ContainsKey(old.Id));
        c.ReplaceTargetLocal(b.Id, m2.Id, "C:/z.txt"); Assert.False(c.Store.Items.ContainsKey(old.Id)); Assert.Empty(c.Validate());
    }
    [Fact] public void ReplaceToTargetAlreadyInWorkspace_KeepsBothInstancesIndependent()
    {
        var (c, a, _) = Two(); var i1 = c.GetOrAddItem(ItemType.File, "1", "C:/1"); var i2 = c.GetOrAddItem(ItemType.File, "2", "C:/2");
        var m1 = c.AddToWorkspace(a.Id, i1.Id, null, "mine"); var m2 = c.AddToWorkspace(a.Id, i2.Id);
        c.ReplaceTargetLocal(a.Id, m1.Id, "C:/2");
        Assert.Equal(2, a.Items.Count); Assert.All(a.Items, m => Assert.Equal(i2.Id, m.ItemId));
        Assert.Equal("mine", a.Items[0].Name); Assert.NotEqual(a.Items[0].Id, a.Items[1].Id); Assert.Empty(c.Validate());
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
        c.AddToWorkspace(w.Id, f.Id, new LaunchConfig("C:\\Tom & Jerry\\My Editor\\ed.exe"));
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
    [Fact] public void UpdateWorkspace_ReplacesFields_AndValidates()
    {
        var c = new Catalog(new Store());
        var w = c.CreateWorkspace("w", "d1", "cat1");
        Assert.NotNull(w.CreatedAt);
        c.UpdateWorkspace(w.Id, "w2", "d2", "cat2");
        Assert.Equal("w2", w.Name); Assert.Equal("d2", w.Description); Assert.Equal("cat2", w.Category);
        c.UpdateWorkspace(w.Id, "w2", "   ", "  ");
        Assert.Null(w.Description); Assert.Null(w.Category); Assert.Equal("כללי", w.EffectiveCategory);
        Assert.Throws<ArgumentException>(() => c.UpdateWorkspace(w.Id, " ", null, null));
        Assert.Throws<ArgumentException>(() => c.UpdateWorkspace(w.Id, "w2", new string('x', 501), null));
        Assert.Empty(c.Validate());
    }
    [Fact] public void CreateWorkspace_RequiresAName()
    {
        var c = new Catalog(new Store());
        Assert.Throws<ArgumentException>(() => c.CreateWorkspace("  "));
        Assert.Empty(c.Store.Workspaces);
    }
}

public class UpdateInstanceTests
{
    private static Catalog NewCatalog() => new(new Store());

    [Fact] public void UpdateInstance_changes_only_that_instance()
    {
        var c = NewCatalog();
        var item = c.GetOrAddItem(ItemType.File, "a.txt", "C:\\a.txt");
        var w1 = c.CreateWorkspace("w1", null); var w2 = c.CreateWorkspace("w2", null);
        var m1 = c.AddToWorkspace(w1.Id, item.Id, name: "one");
        var m2 = c.AddToWorkspace(w1.Id, item.Id, name: "two");
        var m3 = c.AddToWorkspace(w2.Id, item.Id, name: "other ws");
        c.UpdateInstance(w1.Id, m1.Id!, "renamed", "d");
        Assert.Equal("renamed", c.Store.Workspaces.Single(w => w.Id == w1.Id).Items.Single(m => m.Id == m1.Id).Name);
        Assert.Equal("two", c.Store.Workspaces.Single(w => w.Id == w1.Id).Items.Single(m => m.Id == m2.Id).Name);
        Assert.Equal("other ws", c.Store.Workspaces.Single(w => w.Id == w2.Id).Items.Single(m => m.Id == m3.Id).Name);
        Assert.Equal("a.txt", c.Store.Items[item.Id].Name); // the shared catalog record keeps its original name
    }

    [Fact] public void UpdateInstance_validates_and_normalizes()
    {
        var c = NewCatalog();
        var item = c.GetOrAddItem(ItemType.File, "a.txt", "C:\\a.txt");
        var w = c.CreateWorkspace("w", null);
        var m = c.AddToWorkspace(w.Id, item.Id, name: "one", description: "keep?");
        Assert.Throws<ArgumentException>(() => c.UpdateInstance(w.Id, m.Id!, " ", null)); // empty name rejected
        c.UpdateInstance(w.Id, m.Id!, null, "  "); // null name = fallback to the item name; blank description = none
        var saved = c.Store.Workspaces.Single().Items.Single();
        Assert.Null(saved.Name); Assert.Null(saved.Description);
        Assert.Throws<KeyNotFoundException>(() => c.UpdateInstance(w.Id, "mi_missing", "x", null));
    }

    [Fact] public void SetLaunch_saves_per_instance_and_validates()
    {
        var c = NewCatalog();
        var link = c.GetOrAddItem(ItemType.Link, "a.example", "https://a.example/");
        var file = c.GetOrAddItem(ItemType.File, "a.txt", "C:\\a.txt");
        var w = c.CreateWorkspace("w", null);
        var m1 = c.AddToWorkspace(w.Id, link.Id, name: "one");
        var m2 = c.AddToWorkspace(w.Id, link.Id, name: "two"); // second instance of the same address
        c.SetLaunch(w.Id, m1.Id!, new LaunchConfig(Browser: "C:\\Browsers\\other.exe"));
        Assert.Equal("C:\\Browsers\\other.exe", c.Store.Workspaces.Single().Items.Single(m => m.Id == m1.Id).Launch?.Browser);
        Assert.Null(c.Store.Workspaces.Single().Items.Single(m => m.Id == m2.Id).Launch); // per instance, never per address
        Assert.Throws<ArgumentException>(() => c.SetLaunch(w.Id, m1.Id!, new LaunchConfig(OpenWith: "C:\\x.exe"))); // openWith not allowed for a link
        var mf = c.AddToWorkspace(w.Id, file.Id, name: "doc");
        Assert.Throws<ArgumentException>(() => c.SetLaunch(w.Id, mf.Id!, new LaunchConfig(Browser: "C:\\b.exe"))); // browser only for links
        Assert.Throws<ArgumentException>(() => c.SetLaunch(w.Id, mf.Id!, new LaunchConfig(OpenWith: "notepad"))); // not an absolute path
        c.SetLaunch(w.Id, m1.Id!, null); // clearing returns to the Windows default
        Assert.Null(c.Store.Workspaces.Single().Items.Single(m => m.Id == m1.Id).Launch);
        Assert.Throws<KeyNotFoundException>(() => c.SetLaunch(w.Id, "mi_missing", null));
    }

    [Fact] public void MoveInstance_swaps_within_the_display_group_only()
    {
        var c = NewCatalog();
        var f1 = c.GetOrAddItem(ItemType.File, "f1", "C:\\f1");
        var f2 = c.GetOrAddItem(ItemType.File, "f2", "C:\\f2");
        var p1 = c.GetOrAddItem(ItemType.Program, "p1", "C:\\p1.exe"); // programs are their own display group (settled Nov 12, superseding the combined files+programs group)
        var l1 = c.GetOrAddItem(ItemType.Link, "l1", "https://a.example/");
        var w = c.CreateWorkspace("w", null);
        var mf1 = c.AddToWorkspace(w.Id, f1.Id); var ml1 = c.AddToWorkspace(w.Id, l1.Id);
        var mf2 = c.AddToWorkspace(w.Id, f2.Id); var mp1 = c.AddToWorkspace(w.Id, p1.Id);
        string[] Order() => c.Store.Workspaces.Single().Items.Select(m => m.ItemId).ToArray();
        c.MoveInstance(w.Id, mf2.Id!, -1); // f2 swaps with f1, skipping the link that sits between them in the list
        Assert.Equal(new[] { f2.Id, l1.Id, f1.Id, p1.Id }, Order());
        c.MoveInstance(w.Id, ml1.Id!, 1); // the link is alone in its group: no-op
        Assert.Equal(new[] { f2.Id, l1.Id, f1.Id, p1.Id }, Order());
        c.MoveInstance(w.Id, mf2.Id!, -1); // already first in its group: no-op
        Assert.Equal(new[] { f2.Id, l1.Id, f1.Id, p1.Id }, Order());
        c.MoveInstance(w.Id, mf1.Id!, 1); // file vs program is now a group boundary: no-op
        Assert.Equal(new[] { f2.Id, l1.Id, f1.Id, p1.Id }, Order());
        Assert.Throws<KeyNotFoundException>(() => c.MoveInstance(w.Id, "mi_missing", 1));
    }
}
