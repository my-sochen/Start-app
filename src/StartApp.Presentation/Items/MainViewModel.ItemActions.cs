using System.Windows.Input;
using StartApp.Core;
namespace StartApp.Presentation;
public sealed partial class MainViewModel
{
    /// <summary>העתק כתובת/קישור/נתיב from the row actions: direct copy, no popup. The view puts the target on the clipboard.</summary>
    public ICommand CopyAddressCommand { get; private set; } = null!;
    /// <summary>הזז למעלה / הזז למטה: reorder this instance inside its display group.</summary>
    public ICommand MoveItemUpCommand { get; private set; } = null!;
    public ICommand MoveItemDownCommand { get; private set; } = null!;
    public ICommand RemoveItemCommand { get; private set; } = null!;

    private void InitCopyAddressCommand()
    {
        CopyAddressCommand = Cmd(p => { CopyAddressRequested?.Invoke(((ItemRow)p!).Target); Status = "Address copied"; }, p => p is ItemRow);
    }

    private void InitItemReorderCommands()
    {
        MoveItemUpCommand = Cmd(p => Apply(c => c.MoveInstance(Selected!.Id, ((ItemRow)p!).InstanceId, -1)), p => !ReadOnly && Selected != null && p is ItemRow);
        MoveItemDownCommand = Cmd(p => Apply(c => c.MoveInstance(Selected!.Id, ((ItemRow)p!).InstanceId, 1)), p => !ReadOnly && Selected != null && p is ItemRow);
    }

    private void InitRemoveItemCommand()
    {
        RemoveItemCommand = Cmd(p =>
        {
            var row = (ItemRow)p!;
            // Deleting the very row whose inline editor is open: a dirty editor wins - the delete is refused
            // with a clear status instead of stranding the edit on a missing instance (PR19 review). A clean
            // editor just closes before the delete.
            if (ItemEditOpen && row.InstanceId == _itemEdit.InstanceId)
            {
                if (ItemEditDirty) { Status = "Not deleted: save or cancel the open edit of this item first"; return; }
                ItemEditOpen = false;
            }
            if (AddressEditOpen && row.InstanceId == _addressEdit.InstanceId)
            {
                if (AddressEditDirty) { Status = "Not deleted: save or cancel the open address edit of this item first"; return; }
                AddressEditOpen = false;
            }
            Apply(c => c.RemoveInstance(Selected!.Id, row.InstanceId));
        }, p => !ReadOnly && Selected != null && p is ItemRow);
    }

    public sealed record ItemActionTarget(string ItemId, string WorkspaceId, string InstanceId, string WorkspaceName, string Name, string Description, string Target, ItemType Type, LaunchConfig? Launch);
    public AllItemRow? AllItemFromDetail(ItemRow row)
    {
        // Resolve the selected instance's effective fields, not another copy's name or launch settings.
        var instance = ItemInstances().FirstOrDefault(x => x.Workspace.Id == Selected?.Id && (x.Member.Id ?? x.Member.ItemId) == row.InstanceId);
        return instance.Member is null ? null : MakeAllItemRow(new[] { instance });
    }
    public IReadOnlyList<Workspace> ItemActionWorkspaces(AllItemRow row, bool destinations = false)
    {
        // Preserve the add-to-environment rule: destinations already holding this address
        // remain excluded. The full-field grouping changes edit scope, not that policy.
        if (destinations) return _current.Workspaces.Where(w => !w.Items.Any(m => _current.Items.TryGetValue(m.ItemId, out var item) && item.Type.ToString() == row.Type && item.Target == row.Target)).ToArray();
        var ids = MatchingInstances(row).Select(x => x.Workspace.Id).ToHashSet();
        return _current.Workspaces.Where(w => ids.Contains(w.Id)).ToArray();
    }
    public IReadOnlyList<ItemActionTarget> ItemActionTargets(AllItemRow row, IReadOnlyList<string> workspaceIds) => MatchingInstances(row).Where(x => workspaceIds.Contains(x.Workspace.Id)).Select(x => new ItemActionTarget(x.Item.Id, x.Workspace.Id, x.Member.Id ?? x.Member.ItemId, x.Workspace.Name, x.Member.Name ?? x.Item.Name, x.Member.Description ?? x.Item.Description ?? "", x.Item.Target, x.Item.Type, x.Member.Launch)).ToArray();
    private bool ItemActionsAllowed => !ReadOnly && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending && !ItemEditDirty && !AddressEditDirty;
    private static void ValidateActionTargets(Catalog catalog, AllItemRow row, IReadOnlyList<ItemActionTarget> targets)
    {
        foreach (var target in targets)
        {
            var member = catalog.Store.Workspaces.FirstOrDefault(w => w.Id == target.WorkspaceId)?.Items.FirstOrDefault(m => (m.Id ?? m.ItemId) == target.InstanceId);
            if (member is null || member.ItemId != target.ItemId || !catalog.Store.Items.TryGetValue(member.ItemId, out var item) || item.Target != target.Target || item.Type != target.Type || IdentityOf(item, member) != IdentityOf(row))
                throw new ArgumentException("The selected item changed or was removed. Reopen its actions and choose again.");
        }
    }
    public bool DeleteSelectedItem(AllItemRow row, IReadOnlyList<ItemActionTarget> targets)
    {
        if (!ItemActionsAllowed || targets.Count == 0) { Status = "לא ניתן לבצע את הפעולה כעת. שמרו או בטלו עריכה פתוחה ונסו שוב."; return false; }
        var ok = Apply(c => { ValidateActionTargets(c, row, targets); foreach (var target in targets) c.RemoveInstance(target.WorkspaceId, target.InstanceId); });
        if (ok) { if (InAllItems) RefreshAllItems(); ShowToast("האייטם נמחק מהסביבות שנבחרו בלבד.", false); }
        else ShowToast("לא בוצעה מחיקה. הנתונים לא השתנו.", true, Status);
        return ok;
    }
    public bool SaveSelectedItemEdits(AllItemRow row, IReadOnlyList<ItemActionTarget> targets, string? changedName, string? changedDescription, string? changedTarget, bool changeLaunch = false, string? changedProgram = null)
    {
        if (!ItemActionsAllowed || targets.Count == 0) { Status = "לא ניתן לבצע את הפעולה כעת. שמרו או בטלו עריכה פתוחה ונסו שוב."; return false; }
        if (changedName is not null && string.IsNullOrWhiteSpace(changedName)) { Status = "חסר שם לאייטם."; return false; }
        if (changedTarget is not null) foreach (var type in targets.Select(t => t.Type).Distinct()) if (Catalog.ValidateInputTarget(type, changedTarget) is { } error) { Status = error; return false; }
        if (changedDescription is not null && Catalog.ValidateDescription(changedDescription) is { } descriptionError) { Status = descriptionError; return false; }
        LaunchConfig? NewLaunch(ItemType type) => string.IsNullOrWhiteSpace(changedProgram) ? null : type == ItemType.Link ? new LaunchConfig(Browser: changedProgram.Trim()) : new LaunchConfig(OpenWith: changedProgram.Trim());
        if (changeLaunch) foreach (var type in targets.Select(t => t.Type).Distinct()) if (Catalog.ValidateLaunch(type, NewLaunch(type)) is { } launchError) { Status = launchError; return false; }
        var ok = Apply(c =>
        {
            ValidateActionTargets(c, row, targets);
            // Keep the chosen environments, but include every identical instance within them.
            // Refuse a partial or stale selection after the transaction reloads disk state.
            var workspaceIds = targets.Select(t => t.WorkspaceId).ToHashSet();
            var expected = c.Store.Workspaces.Where(w => workspaceIds.Contains(w.Id)).SelectMany(w => w.Items.Where(m => c.Store.Items.TryGetValue(m.ItemId, out var item) && IdentityOf(item, m) == IdentityOf(row)).Select(m => (w.Id, m.Id ?? m.ItemId))).ToHashSet();
            var selected = targets.Select(t => (t.WorkspaceId, t.InstanceId)).ToHashSet();
            if (selected.Count != targets.Count || !expected.SetEquals(selected)) throw new ArgumentException("The identical item group changed. Reopen its editor before saving.");
            foreach (var target in targets)
            {
                var member = c.Store.Workspaces.First(w => w.Id == target.WorkspaceId).Items.First(m => (m.Id ?? m.ItemId) == target.InstanceId);
                var item = c.Store.Items[member.ItemId];
                if (changeLaunch && member.Launch != target.Launch) throw new ArgumentException("The launch settings changed while this form was open. Reopen it before saving.");
                if ((member.Name ?? item.Name) != target.Name || (member.Description ?? item.Description ?? "") != target.Description) throw new ArgumentException("The item's text changed while the form was open. Reopen it before saving.");
            }
            foreach (var target in targets)
            {
                var member = c.Store.Workspaces.First(w => w.Id == target.WorkspaceId).Items.First(m => (m.Id ?? m.ItemId) == target.InstanceId);
                if (changedTarget is not null) c.ReplaceTargetLocal(target.WorkspaceId, target.InstanceId, changedTarget);
                c.UpdateInstance(target.WorkspaceId, target.InstanceId, changedName ?? (changedTarget is not null ? target.Name : member.Name), changedDescription ?? (changedTarget is not null ? target.Description : member.Description));
                if (changeLaunch) c.SetLaunch(target.WorkspaceId, target.InstanceId, NewLaunch(target.Type));
            }
        });
        if (ok) { if (InAllItems) RefreshAllItems(); ShowToast("השינויים נשמרו בכל העותקים הזהים בסביבות שנבחרו.", false); }
        return ok;
    }
    public bool AddItemToEnvironments(AllItemRow row, IReadOnlyList<string> workspaceIds)
    {
        if (!ItemActionsAllowed || workspaceIds.Count == 0) return false;
        var ok = Apply(c =>
        {
            if (!c.Store.Items.TryGetValue(row.ItemId, out var item) || item.Target != row.Target || item.Type.ToString() != row.Type) throw new ArgumentException("The item no longer exists.");
            if (!c.Store.Workspaces.SelectMany(w => w.Items).Any(m => m.ItemId == row.ItemId && IdentityOf(item, m) == IdentityOf(row))) throw new ArgumentException("The source item changed. Reopen its actions before adding it.");
            foreach (var id in workspaceIds)
                if (!c.Store.Workspaces.Any(w => w.Id == id) || c.HasTarget(id, item.Type, item.Target)) throw new ArgumentException("A destination changed or already contains this item. Reopen the list.");
            foreach (var id in workspaceIds)
            {
                var added = c.AddToWorkspace(id, row.ItemId, row.Launch, row.Name);
                // Keep an explicit empty description empty even for a legacy shared-description item.
                c.UpdateInstance(id, added.Id!, row.Name, row.Description);
            }
        });
        if (ok) { if (InAllItems) RefreshAllItems(); ShowToast("האייטם נוסף לסביבות שנבחרו.", false); }
        else ShowToast("האייטם לא נוסף. הנתונים לא השתנו.", true, Status);
        return ok;
    }
    public bool OpenItemWithInEnvironment(AllItemRow row, string workspaceId)
    {
        if (!ItemActionsAllowed) return false;
        var workspace = _current.Workspaces.FirstOrDefault(w => w.Id == workspaceId);
        var matching = MatchingInstances(row).Where(x => x.Workspace.Id == workspaceId).ToArray();
        var member = matching.FirstOrDefault().Member;
        if (workspace is null || member is null || matching.Length != 1) return false;
        InAllItems = false; OpenDetails(workspace);
        if (Items.FirstOrDefault(r => r.InstanceId == (member.Id ?? member.ItemId)) is not { } detail) return false;
        BeginOpenWith(detail); return true;
    }
}
