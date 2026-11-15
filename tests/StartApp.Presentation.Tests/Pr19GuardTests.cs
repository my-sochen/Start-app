using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>PR19 review regressions: a freshly started app with saved data must not look dirty, and
/// "save and exit" must not drop a concurrently open inline item edit; an unrelated mutation must not
/// silently close a dirty edit form either.</summary>
public class Pr19GuardTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private static (JsonStore repo, MainViewModel vm) ExistingStore()
    {
        var repo = new JsonStore(Tmp());
        repo.Mutate(c => { var w = c.CreateWorkspace("saved"); var it = c.GetOrAddItem(ItemType.File, "file", "C:\\a.txt"); c.AddToWorkspace(w.Id, it.Id); });
        return (repo, new MainViewModel(repo, new NotImplementedLauncher()));
    }

    [Fact] public void Fresh_startup_with_saved_workspace_is_not_dirty_and_closes_clean()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        Assert.False(vm.InDetails);
        Assert.False(vm.DetailsDirty);       // was true: blank edit fields compared against the saved name
        Assert.True(vm.RequestAppClose());   // no unsaved prompt when nothing was touched
        Assert.False(vm.UnsavedOpen);
    }

    [Fact] public void Save_and_exit_saves_both_details_and_inline_item_edit()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        bool approved = false; vm.AppCloseApproved += () => approved = true;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        vm.EditName = "unsaved workspace";
        vm.EditItemCommand.Execute(vm.Items.Single());
        vm.EditItemName = "unsaved item";
        Assert.True(vm.DetailsDirty); Assert.True(vm.ItemEditDirty);
        Assert.False(vm.RequestAppClose());
        vm.SaveAndExitCommand.Execute(null);
        Assert.True(approved);
        Assert.Equal("unsaved workspace", repo.Load().Workspaces.Single().Name);
        Assert.Equal("unsaved item", repo.Load().Workspaces.Single().Items.Single().Name); // was lost
    }

    [Fact] public void Unrelated_mutation_keeps_a_dirty_item_edit_open()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        repo.Mutate(c => { var w = c.Store.Workspaces.Single(); var it = c.GetOrAddItem(ItemType.File, "file2", "C:\\b.txt"); c.AddToWorkspace(w.Id, it.Id); });
        vm.Reload();
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var first = vm.Items.First(); var second = vm.Items.Skip(1).First();
        vm.EditItemCommand.Execute(first);
        vm.EditItemName = "edited name";
        Assert.True(vm.ItemEditDirty);
        vm.MoveItemUpCommand.Execute(second); // unrelated Apply/refresh path
        Assert.True(vm.ItemEditOpen);           // the dirty form survives the refresh with its fields
        Assert.Equal("edited name", vm.EditItemName);
        var after = repo.Load(); var ws = after.Workspaces.Single(); // the reorder really happened
        Assert.Equal(2, ws.Items.Count);
        Assert.Equal("C:\\b.txt", after.Items[ws.Items[0].ItemId].Target);
        Assert.Equal("C:\\a.txt", after.Items[ws.Items[1].ItemId].Target);
    }

    [Fact] public void Deleting_the_item_being_edited_is_blocked_while_dirty()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var row = vm.Items.Single();
        vm.EditItemCommand.Execute(row); vm.EditItemName = "keep?";
        vm.RemoveItemCommand.Execute(row);
        Assert.Single(repo.Load().Workspaces.Single().Items);  // not deleted
        Assert.True(vm.ItemEditOpen); Assert.True(vm.ItemEditDirty);
        Assert.Equal("keep?", vm.EditItemName);                // input preserved
        Assert.Contains("Not deleted", vm.Status);
        vm.SaveItemEditCommand.Execute(null);                   // editor target still valid
        Assert.Equal("keep?", repo.Load().Workspaces.Single().Items.Single().Name);
    }

    [Fact] public void Deleting_the_item_being_edited_closes_a_clean_editor()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var row = vm.Items.Single();
        vm.EditItemCommand.Execute(row);                        // open, no changes
        vm.RemoveItemCommand.Execute(row);
        Assert.Empty(repo.Load().Workspaces.Single().Items);
        Assert.False(vm.ItemEditOpen);
    }

    [Fact] public void Switching_workspace_is_blocked_while_an_item_edit_is_dirty()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        repo.Mutate(c => c.CreateWorkspace("second"));
        vm.Reload();
        vm.OpenDetailsCommand.Execute(vm.Workspaces.First(w => w.Name == "saved"));
        vm.EditItemCommand.Execute(vm.Items.Single()); vm.EditItemName = "keep?";
        vm.OpenDetailsCommand.Execute(vm.Workspaces.First(w => w.Name == "second"));
        Assert.Equal("saved", vm.Selected!.Name);               // navigation refused
        Assert.True(vm.ItemEditOpen); Assert.Equal("keep?", vm.EditItemName);
        Assert.Contains("switching workspaces", vm.Status);
    }

    [Fact] public void Save_after_external_removal_preserves_input_instead_of_throwing()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        vm.EditItemCommand.Execute(vm.Items.Single()); vm.EditItemName = "keep?";
        var loaded = repo.Load(); var wsId = loaded.Workspaces.Single().Id; var instId = loaded.Workspaces.Single().Items.Single().Id!;
        repo.Mutate(c => c.RemoveInstance(wsId, instId));       // deleted outside the VM
        vm.Reload();
        vm.SaveItemEditCommand.Execute(null);                   // must not throw (was KeyNotFoundException)
        Assert.True(vm.ItemEditOpen);                           // input preserved for the user to decide
        Assert.Equal("keep?", vm.EditItemName);
        Assert.Contains("Not saved", vm.Status);
    }

    [Fact] public void Address_save_after_external_removal_preserves_input_instead_of_throwing()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        vm.EditAddressCommand.Execute(vm.Items.Single()); vm.EditAddress = "C:\\new.txt";
        var loaded = repo.Load(); var wsId = loaded.Workspaces.Single().Id; var instId = loaded.Workspaces.Single().Items.Single().Id!;
        repo.Mutate(c => c.RemoveInstance(wsId, instId));
        vm.Reload();
        vm.SaveAddressCommand.Execute(null);                    // must not throw
        Assert.True(vm.AddressEditOpen);
        Assert.Equal("C:\\new.txt", vm.EditAddress);
        Assert.Contains("Not saved", vm.Status);
    }

    private static (JsonStore repo, MainViewModel vm) TwoItemStore()
    {
        var repo = new JsonStore(Tmp());
        repo.Mutate(c => { var w = c.CreateWorkspace("w"); foreach (var n in new[] { "a", "b" }) { var i = c.GetOrAddItem(ItemType.File, n, "C:\\" + n + ".txt"); c.AddToWorkspace(w.Id, i.Id); } });
        return (repo, new MainViewModel(repo, new NotImplementedLauncher()));
    }

    [Fact] public void Starting_another_rows_edit_is_refused_while_a_draft_is_dirty()
    {
        var (repo, vm) = TwoItemStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var a = vm.Items[0]; var b = vm.Items[1];
        vm.EditItemCommand.Execute(a); vm.EditItemName = "unsaved-A";
        vm.EditItemCommand.Execute(b);                          // user-clickable while the inline editor is open
        Assert.True(vm.ItemEditOpen);                           // still editing A, draft untouched
        Assert.Equal("unsaved-A", vm.EditItemName);
        Assert.True(vm.ItemEditDirty);
        Assert.False(vm.UnsavedOpen);
        Assert.Contains("Save or cancel", vm.Status);
        var savedB = repo.Load(); Assert.Equal("b", savedB.Items[savedB.Workspaces.Single().Items[1].ItemId].Name); // nothing silently saved either
        vm.SaveItemEditCommand.Execute(null);
        Assert.Equal("unsaved-A", repo.Load().Workspaces.Single().Items[0].Name);
        vm.EditItemCommand.Execute(b);                          // after the save the switch works
        Assert.Equal("b", vm.EditItemName);
    }

    [Fact] public void Reopening_the_same_rows_editor_while_dirty_keeps_the_draft()
    {
        var (repo, vm) = TwoItemStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var a = vm.Items[0];
        vm.EditItemCommand.Execute(a); vm.EditItemName = "unsaved-A";
        vm.EditItemCommand.Execute(a);                          // same-row re-open must not reset the fields
        Assert.Equal("unsaved-A", vm.EditItemName);
        Assert.True(vm.ItemEditDirty);
        Assert.Contains("Save or cancel", vm.Status);
    }

    [Fact] public void Address_popup_is_refused_while_the_item_edit_is_dirty_and_an_explicit_save_is_never_overwritten()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var row = vm.Items.Single();
        vm.EditItemCommand.Execute(row); vm.EditItemTarget = "C:\\inline.txt";
        vm.EditAddressCommand.Execute(row);                     // refused: a dirty editor owns the form
        Assert.False(vm.AddressEditOpen);
        Assert.Contains("Save or cancel", vm.Status);
        vm.SaveItemEditCommand.Execute(null);                   // the accepted edit wins legitimately
        Assert.Equal("C:\\inline.txt", repo.Load().Items[repo.Load().Workspaces.Single().Items.Single().ItemId].Target);
        vm.EditAddressCommand.Execute(row);                     // now clean: the popup opens
        Assert.True(vm.AddressEditOpen);
        vm.EditAddress = "C:\\popup.txt";
        vm.SaveAddressCommand.Execute(null);                    // explicit save
        Assert.Equal("C:\\popup.txt", repo.Load().Items[repo.Load().Workspaces.Single().Items.Single().ItemId].Target);
        Assert.True(vm.RequestAppClose());                      // no stale draft remains to overwrite it on exit
        Assert.Equal("C:\\popup.txt", repo.Load().Items[repo.Load().Workspaces.Single().Items.Single().ItemId].Target);
    }

    [Fact] public void Item_edit_is_refused_while_the_address_popup_is_dirty()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var row = vm.Items.Single();
        vm.EditAddressCommand.Execute(row); vm.EditAddress = "C:\\popup.txt";
        vm.EditItemCommand.Execute(row);                        // reverse direction is guarded too
        Assert.False(vm.ItemEditOpen);
        Assert.Contains("address edit", vm.Status);
        vm.SaveAddressCommand.Execute(null);
        Assert.Equal("C:\\popup.txt", repo.Load().Items[repo.Load().Workspaces.Single().Items.Single().ItemId].Target);
        vm.EditItemCommand.Execute(row);                        // opens once the popup is saved
        Assert.True(vm.ItemEditOpen);
        Assert.False(vm.ItemEditDirty);
    }

    [Fact] public void A_clean_editor_is_replaced_by_another_rows_editor_without_a_dialog()
    {
        var (repo, vm) = TwoItemStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var a = vm.Items[0]; var b = vm.Items[1];
        vm.EditItemCommand.Execute(a);                          // open, no changes
        vm.EditItemCommand.Execute(b);                          // clean replace: normal UX preserved
        Assert.True(vm.ItemEditOpen);
        Assert.Equal("b", vm.EditItemName);
        Assert.False(vm.ItemEditDirty);
        Assert.False(vm.UnsavedOpen);
    }

    [Fact] public void A_failed_save_keeps_the_dirty_editor_and_keeps_refusing_other_editors_until_cancelled()
    {
        var (repo, vm) = TwoItemStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        var a = vm.Items[0]; var b = vm.Items[1];
        vm.EditItemCommand.Execute(a); vm.EditItemName = "draft-A";
        var loaded = repo.Load(); var wsId = loaded.Workspaces.Single().Id; var instId = loaded.Workspaces.Single().Items[0].Id!;
        repo.Mutate(c => c.RemoveInstance(wsId, instId));       // deleted outside the VM
        vm.Reload();
        vm.SaveItemEditCommand.Execute(null);                   // fails: target missing, input kept
        Assert.True(vm.ItemEditOpen); Assert.True(vm.ItemEditDirty);
        Assert.Equal("draft-A", vm.EditItemName);
        vm.EditItemCommand.Execute(b);                          // still refused while the failed draft is open
        Assert.Equal("draft-A", vm.EditItemName);
        vm.CancelItemEditCommand.Execute(null);                 // user decides: discard via the dialog
        Assert.True(vm.UnsavedOpen);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.False(vm.ItemEditOpen);
        vm.EditItemCommand.Execute(b);                          // now the other editor opens
        Assert.Equal("b", vm.EditItemName);
    }

    [Fact] public void Save_after_external_removal_without_reload_is_handled_inside_the_mutation()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        vm.EditItemCommand.Execute(vm.Items.Single()); vm.EditItemName = "keep?";
        var loaded = repo.Load(); var wsId = loaded.Workspaces.Single().Id; var instId = loaded.Workspaces.Single().Items.Single().Id!;
        repo.Mutate(c => c.RemoveInstance(wsId, instId));       // deleted outside the VM, NO vm.Reload:
        vm.SaveItemEditCommand.Execute(null);                   // Mutate reloads disk; the in-mutation guard must catch it (was KeyNotFoundException)
        Assert.True(vm.ItemEditOpen);
        Assert.Equal("keep?", vm.EditItemName);
        Assert.Contains("Save failed", vm.Status);
        Assert.Empty(repo.Load().Workspaces.Single().Items);    // nothing was written
    }

    [Fact] public void Address_save_after_external_removal_without_reload_is_handled_inside_the_mutation()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        vm.EditAddressCommand.Execute(vm.Items.Single()); vm.EditAddress = "C:\\new.txt";
        var loaded = repo.Load(); var wsId = loaded.Workspaces.Single().Id; var instId = loaded.Workspaces.Single().Items.Single().Id!;
        repo.Mutate(c => c.RemoveInstance(wsId, instId));       // no vm.Reload
        vm.SaveAddressCommand.Execute(null);                    // must not throw
        Assert.True(vm.AddressEditOpen);
        Assert.Equal("C:\\new.txt", vm.EditAddress);
        Assert.Contains("Save failed", vm.Status);
    }

    [Fact] public void Save_and_exit_does_not_approve_when_the_captured_target_vanished_without_reload()
    {
        var (repo, vm) = ExistingStore(); using var _ = repo;
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        vm.EditItemCommand.Execute(vm.Items.Single()); vm.EditItemName = "keep?";
        var loaded = repo.Load(); var wsId = loaded.Workspaces.Single().Id; var instId = loaded.Workspaces.Single().Items.Single().Id!;
        repo.Mutate(c => c.RemoveInstance(wsId, instId));       // no vm.Reload
        bool approved = false; vm.AppCloseApproved += () => approved = true;
        Assert.False(vm.RequestAppClose());                     // unsaved dialog opens
        vm.SaveAndExitCommand.Execute(null);                    // save fails inside the mutation...
        Assert.False(approved);                                 // ...so the close is NOT approved
        Assert.True(vm.UnsavedOpen);                            // dialog stays open for the user to decide
        Assert.True(vm.ItemEditOpen);
        Assert.Equal("keep?", vm.EditItemName);
    }
}
