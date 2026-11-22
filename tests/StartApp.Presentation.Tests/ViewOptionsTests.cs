using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>תצוגה menu state (approved 22.11 handoff): sort orders and never hides, filters hide and combine,
/// chips remove one filter at a time and ניקוי הכל clears all, the counter stays live.</summary>
public class ViewOptionsTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    private static MainViewModel Seed(out JsonStore repo)
    {
        repo = new JsonStore(Tmp());
        repo.Mutate(c =>
        {
            var alpha = c.CreateWorkspace("אלף", category: "כללי");
            var bet = c.CreateWorkspace("bet", category: null);
            var gimel = c.CreateWorkspace("גימל", category: "מוסיקה");
            c.AddCustomCategory("מוסיקה");
            c.SetCardDesign(alpha.Id, "#C94F6D", null);
            c.SetCardDesign(gimel.Id, "#2E8B7A", null);
            var code = c.GetOrAddItem(ItemType.Program, "Visual Studio Code", "C:\\Apps\\Code.exe");
            var gpt = c.GetOrAddItem(ItemType.Link, "gpt", "https://gpt.example.com");
            c.AddToWorkspace(alpha.Id, code.Id);
            c.AddToWorkspace(bet.Id, gpt.Id);
            c.TouchLastOpened(bet.Id); c.TouchLastOpened(bet.Id);
            c.TouchLastOpened(gimel.Id);
        });
        return new MainViewModel(repo, new NotImplementedLauncher());
    }

    [Fact] public void Default_sort_is_recent_entries_descending()
    {
        var vm = Seed(out _);
        Assert.Equal(ViewSort.RecentEntries, vm.CurrentSort);
        Assert.Equal(new[] { "גימל", "bet", "אלף" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray()); // גימל was opened last
    }

    [Fact] public void Sort_orders_and_never_hides()
    {
        var vm = Seed(out _);
        vm.CurrentSort = ViewSort.Alphabetical;
        Assert.Equal(3, vm.DisplayWorkspaces.Count);
        var expectedNames = new[] { "אלף", "bet", "גימל" }.OrderBy(n => n, StringComparer.CurrentCulture).ToArray();
        Assert.Equal(expectedNames, vm.DisplayWorkspaces.Select(w => w.Name).ToArray()); // לפי הא' ב' in the current culture
        vm.CurrentSort = ViewSort.ItemCount;
        Assert.Equal(3, vm.DisplayWorkspaces.Count);
        Assert.Equal(new[] { "אלף", "bet" }, vm.DisplayWorkspaces.Take(2).Select(w => w.Name).ToArray());
        vm.CurrentSort = ViewSort.LaunchCount;
        Assert.Equal(new[] { "bet", "גימל", "אלף" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray());
        vm.CurrentSort = ViewSort.CreationDate;
        Assert.Equal(3, vm.DisplayWorkspaces.Count);
    }

    [Fact] public void Launch_count_tracks_actual_launches()
    {
        var vm = Seed(out var repo);
        var bet = repo.Load().Workspaces.Single(w => w.Name == "bet");
        Assert.Equal(2, bet.LaunchCount);
        Assert.Equal(0, repo.Load().Workspaces.Single(w => w.Name == "אלף").LaunchCount);
    }

    [Fact] public void Category_filter_is_multi_select_or()
    {
        var vm = Seed(out _);
        vm.ToggleCategoryFilter("כללי");
        Assert.Equal(new[] { "bet", "אלף" }, vm.DisplayWorkspaces.OrderBy(w => w.Name).Select(w => w.Name).ToArray());
        vm.ToggleCategoryFilter("מוסיקה");
        Assert.Equal(3, vm.DisplayWorkspaces.Count); // OR inside the dimension
        vm.ToggleCategoryFilter("כללי");
        Assert.Equal(new[] { "גימל" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray());
    }

    [Fact] public void Color_filter_offers_colors_in_use_and_filters()
    {
        var vm = Seed(out _);
        Assert.Equal(new[] { "#2E8B7A", "#C94F6D" }, vm.FilterColorOptions.ToArray());
        vm.ToggleColorFilter("#c94f6d"); // case-insensitive match on the stored value
        Assert.Equal(new[] { "אלף" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray());
        Assert.True(vm.IsColorFiltered("#C94F6D"));
    }

    [Fact] public void Content_item_filter_lists_real_items_and_filters_to_containing_workspaces()
    {
        var vm = Seed(out _);
        var programs = vm.ContentItemsOfType(ItemType.Program);
        Assert.Equal(new[] { "Visual Studio Code" }, programs.Select(i => i.Name).ToArray());
        vm.SetContentItemFilter(programs.Single());
        Assert.Equal(new[] { "אלף" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray());
        Assert.Equal("תוכנות: Visual Studio Code", vm.FilterChips.Single().Label);
    }

    [Fact] public void Contains_text_matches_item_names_inside_the_workspace()
    {
        var vm = Seed(out _);
        vm.FilterContainsText = "gpt";
        Assert.Equal(new[] { "bet" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray());
        Assert.Equal("מכיל: gpt", vm.FilterChips.Single().Label);
    }

    [Fact] public void Filters_combine_across_dimensions_and_chips_remove_one_at_a_time()
    {
        var vm = Seed(out _);
        vm.ToggleColorFilter("#C94F6D");
        vm.SetContentItemFilter(vm.ContentItemsOfType(ItemType.Program).Single());
        Assert.Equal(new[] { "אלף" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray());
        Assert.Equal(2, vm.FilterChips.Count);
        Assert.True(vm.HasActiveFilters);
        Assert.Equal("מוצגת 1 מתוך 3", vm.FilteredCountText);
        vm.RemoveFilterChip("item");
        Assert.Equal(new[] { "אלף" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray()); // still filtered by color only
        vm.RemoveFilterChip("color:#C94F6D");
        Assert.Equal(3, vm.DisplayWorkspaces.Count);
        Assert.False(vm.HasActiveFilters);
    }

    [Fact] public void Clear_all_removes_every_filter_and_restores_the_full_list()
    {
        var vm = Seed(out _);
        vm.ToggleCategoryFilter("כללי");
        vm.FilterContainsText = "gpt";
        Assert.Equal(new[] { "bet" }, vm.DisplayWorkspaces.Select(w => w.Name).ToArray());
        vm.ClearFilters();
        Assert.Equal(3, vm.DisplayWorkspaces.Count);
        Assert.Empty(vm.FilterChips);
        Assert.False(vm.HasActiveFilters);
        Assert.Equal("", vm.FilterContainsText);
    }

    [Fact] public void Counter_and_color_options_stay_live_across_edits()
    {
        var vm = Seed(out _);
        Assert.Equal("מוצגת 3 מתוך 3", vm.FilteredCountText);
        vm.NewName = "חדש"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.Equal("מוצגת 4 מתוך 4", vm.FilteredCountText);
    }

    [Fact] public void Stats_line_uses_the_fixed_order_and_omits_zero_types()
    {
        var vm = Seed(out var repo);
        repo.Mutate(c =>
        {
            var w = c.CreateWorkspace("סטטיסטיקה");
            var site = c.GetOrAddItem(ItemType.Link, "site", "https://a.example.com");
            var site2 = c.GetOrAddItem(ItemType.Link, "site2", "https://b.example.com");
            var prog = c.GetOrAddItem(ItemType.Program, "prog", "C:\\Apps\\p.exe");
            var folder = c.GetOrAddItem(ItemType.Folder, "docs", "C:\\Docs");
            var file = c.GetOrAddItem(ItemType.File, "f", "C:\\Docs\\f.txt");
            var file2 = c.GetOrAddItem(ItemType.File, "f2", "C:\\Docs\\f2.txt");
            foreach (var it in new[] { site, site2, prog, folder, file, file2 }) c.AddToWorkspace(w.Id, it.Id);
        });
        var stats = repo.Load().Workspaces.Single(w => w.Name == "סטטיסטיקה");
        var vmFresh = new MainViewModel(repo, new NotImplementedLauncher()); // constructed after the items exist
        // approved order 22.11: אתרים · תוכנות · תיקיות · קבצים; 1 = singular without a number.
        Assert.Equal("2 אתרים · תוכנה · תיקייה · 2 קבצים", vmFresh.StatsText(stats));
        var vm2 = Seed(out var repo2);
        repo2.Mutate(c => c.CreateWorkspace("ריקה"));
        Assert.Equal("עדיין אין אייטמים", vm2.StatsText(repo2.Load().Workspaces.Single(w => w.Name == "ריקה")));
    }

    [Fact] public void SetDefaultWorkspace_persists_and_clears_through_the_vm()
    {
        var vm = Seed(out var repo);
        var alpha = repo.Load().Workspaces.Single(w => w.Name == "אלף");
        Assert.Null(vm.DefaultWorkspaceId);
        vm.SetDefaultWorkspace(alpha.Id);
        Assert.Equal(alpha.Id, repo.Load().DefaultWorkspaceId);
        Assert.Equal(alpha.Id, vm.DefaultWorkspaceId);
        vm.SetDefaultWorkspace(null); // "ללא סביבת ברירת מחדל"
        Assert.Null(repo.Load().DefaultWorkspaceId);
    }

    [Fact] public void Deleting_the_default_workspace_clears_the_default()
    {
        var vm = Seed(out var repo);
        var alpha = repo.Load().Workspaces.Single(w => w.Name == "אלף");
        vm.SetDefaultWorkspace(alpha.Id);
        repo.Mutate(c => c.DeleteWorkspace(alpha.Id));
        Assert.Null(repo.Load().DefaultWorkspaceId);
    }

    [Fact] public void ToggleDefaultForCommand_sets_clears_and_confirms_with_the_approved_toast()
    {
        var vm = Seed(out var repo);
        var alpha = repo.Load().Workspaces.Single(w => w.Name == "אלף");
        var bet = repo.Load().Workspaces.Single(w => w.Name == "bet");
        vm.ToggleDefaultForCommand.Execute(alpha); // no existing default: immediate, no dialog
        Assert.Equal(alpha.Id, repo.Load().DefaultWorkspaceId);
        Assert.False(vm.DefaultConfirmOpen);
        Assert.Equal($"הסביבה 'אלף' הוגדרה כברירת מחדל · תיפתח עם הפעלת המחשב", vm.ToastMessage);
        Assert.False(vm.ToastPersistent); // ~3s toast
        vm.ToggleDefaultForCommand.Execute(bet); // replacing an existing default opens the confirm dialog (owner 22.11)
        Assert.True(vm.DefaultConfirmOpen);
        Assert.Equal(alpha.Id, repo.Load().DefaultWorkspaceId); // nothing changes until confirmed
        vm.ConfirmDefaultReplaceCommand.Execute(null);
        Assert.False(vm.DefaultConfirmOpen);
        Assert.Equal(bet.Id, repo.Load().DefaultWorkspaceId);
        Assert.False(vm.IsDefaultWorkspace(alpha));
        vm.ToggleDefaultForCommand.Execute(bet); // same row on the default clears it immediately ("הסר הגדרה כברירת מחדל")
        Assert.Null(repo.Load().DefaultWorkspaceId);
        Assert.False(vm.DefaultConfirmOpen); // removal never opens the dialog
        Assert.Equal("הוסרה ההגדרה כברירת מחדל", vm.ToastMessage);
    }

    [Fact] public void Replace_confirm_dialog_names_both_workspaces_and_cancel_changes_nothing()
    {
        var vm = Seed(out var repo);
        var alpha = repo.Load().Workspaces.Single(w => w.Name == "אלף");
        var bet = repo.Load().Workspaces.Single(w => w.Name == "bet");
        vm.SetDefaultWorkspace(alpha.Id);
        vm.SetDefaultWorkspace(bet.Id);
        Assert.True(vm.DefaultConfirmOpen);
        Assert.Contains("'אלף'", vm.DefaultConfirmBody); // current
        Assert.Contains("'bet'", vm.DefaultConfirmBody); // candidate
        Assert.Contains("'bet'", vm.DefaultConfirmNotice);
        vm.CancelDefaultReplaceCommand.Execute(null); // cancel/✕/outside: nothing changes
        Assert.False(vm.DefaultConfirmOpen);
        Assert.Equal(alpha.Id, repo.Load().DefaultWorkspaceId);
        vm.SetDefaultWorkspace(bet.Id); // again, then confirm
        vm.ConfirmDefaultReplaceCommand.Execute(null);
        Assert.Equal(bet.Id, repo.Load().DefaultWorkspaceId);
        Assert.Equal($"הסביבה 'bet' הוגדרה כברירת מחדל · תיפתח עם הפעלת המחשב", vm.ToastMessage);
    }

    [Fact] public void Details_button_label_tracks_the_default_state()
    {
        var vm = Seed(out var repo);
        var alpha = repo.Load().Workspaces.Single(w => w.Name == "אלף");
        vm.OpenDetailsCommand.Execute(alpha);
        Assert.False(vm.SelectedIsDefault);
        Assert.Equal("הגדר כברירת מחדל", vm.DefaultToggleLabel);
        vm.ToggleDefaultForCommand.Execute(vm.Selected);
        Assert.True(vm.SelectedIsDefault);
        Assert.Equal("הסר הגדרה כברירת מחדל", vm.DefaultToggleLabel);
    }

    [Fact] public void Default_workspace_badge_marks_exactly_one_card()
    {
        var vm = Seed(out var repo);
        var all = repo.Load().Workspaces;
        Assert.All(all, w => Assert.False(vm.IsDefaultWorkspace(w))); // no default set: no badge anywhere
        var alpha = all.Single(w => w.Name == "אלף");
        repo.Mutate(c => c.Store.DefaultWorkspaceId = alpha.Id);
        var vm2 = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.True(vm2.IsDefaultWorkspace(alpha));
        Assert.All(all.Where(w => w.Id != alpha.Id), w => Assert.False(vm2.IsDefaultWorkspace(w)));
        repo.Mutate(c => c.Store.DefaultWorkspaceId = null); // "ללא סביבת ברירת מחדל"
        var vm3 = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.All(all, w => Assert.False(vm3.IsDefaultWorkspace(w)));
    }
}
