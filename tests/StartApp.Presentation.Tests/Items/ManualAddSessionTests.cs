using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

public class ManualAddSessionTests
{
    [Fact] public void Defaults_match_manual_add_form()
    {
        var s = new ManualAddSession(); Assert.Equal(ItemType.File, s.Type);
        Assert.Equal("", s.Target); Assert.Equal("", s.Name); Assert.Equal("", s.Description);
        Assert.False(s.DuplicatePending); Assert.Equal("", s.DuplicateMessage);
    }
    [Theory] [InlineData("https://example.com/path")] [InlineData("http://example.com")]
    public void Web_targets_override_selected_type_without_mutating_draft(string target)
    {
        var s = new ManualAddSession { Type = ItemType.Folder, Target = target };
        Assert.Equal(ItemType.Link, s.EffectiveType); Assert.Equal(ItemType.Folder, s.Type); Assert.Equal(target, s.Target);
    }
    [Theory] [InlineData(ItemType.File)] [InlineData(ItemType.Folder)] [InlineData(ItemType.Program)]
    public void Non_web_target_keeps_selected_type(ItemType type)
    {
        var s = new ManualAddSession { Type = type, Target = @"C:\work\file.txt" };
        Assert.Equal(type, s.EffectiveType);
    }
    [Fact] public void Validation_uses_same_catalog_order_without_changing_whitespace_or_confirmation()
    {
        var s = new ManualAddSession { Target = " invalid ", Description = new string('x', 501), Name = new string('n', 1000), DuplicatePending = true, DuplicateMessage = "pending" };
        Assert.Equal(Catalog.ValidateInputTarget(s.EffectiveType, s.Target), s.ValidationError);
        Assert.Equal(" invalid ", s.Target); Assert.True(s.DuplicatePending); Assert.Equal("pending", s.DuplicateMessage);
        s.Target = "https://example.com";
        Assert.Equal(Catalog.ValidateDescription(s.Description), s.ValidationError);
    }
    [Fact] public void Blank_local_name_is_optional_and_validation_is_non_mutating()
    {
        var s = new ManualAddSession { Target = "https://example.com", Name = "   " };
        Assert.Null(s.ValidationError); Assert.Equal("   ", s.Name);
    }
    [Fact] public void Nonblank_name_uses_exact_catalog_validation()
    {
        var s = new ManualAddSession { Target = "https://example.com", Name = new string('x', 1000) };
        Assert.Equal(Catalog.ValidateInstanceName(s.Name), s.ValidationError); Assert.Null(s.ValidationError);
    }
    [Fact] public void Draft_and_confirmation_state_do_not_leak_between_owners()
    {
        var a = new ManualAddSession { Target = "https://a.example", DuplicatePending = true, DuplicateMessage = "a" };
        var b = new ManualAddSession(); Assert.Equal("", b.Target); Assert.False(b.DuplicatePending); Assert.Equal("", b.DuplicateMessage);
        a.DuplicatePending = false; Assert.Equal("https://a.example", a.Target);
    }
    [Fact] public void Host_clears_confirmation_on_same_value_assignment_without_spurious_field_notification()
    {
        using var repo = new JsonStore(Path.Combine(Path.GetTempPath(), "manual-add-" + Guid.NewGuid(), "store.json"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.NewName = "work"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewTarget = "https://example.com"; vm.AddItemCommand.Execute(null);
        vm.NewTarget = "https://example.com"; vm.AddItemCommand.Execute(null); Assert.True(vm.DuplicatePending);
        var events = new List<string?>(); vm.PropertyChanged += (_, e) => events.Add(e.PropertyName);
        vm.NewItemName = vm.NewItemName;
        Assert.False(vm.DuplicatePending); Assert.Equal("", vm.DuplicateMessage);
        Assert.DoesNotContain(nameof(vm.NewItemName), events); Assert.Contains(nameof(vm.DuplicatePending), events);
        Assert.Single(repo.Load().Workspaces.Single().Items);
    }
    [Fact] public void Host_confirmation_adds_independent_instance_and_resets_draft_only_on_success()
    {
        using var repo = new JsonStore(Path.Combine(Path.GetTempPath(), "manual-add-" + Guid.NewGuid(), "store.json"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.NewName = "work"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewTarget = "https://example.com"; vm.AddItemCommand.Execute(null);
        vm.NewTarget = "https://example.com"; vm.NewItemName = "local"; vm.NewDescription = "description"; vm.AddItemCommand.Execute(null);
        Assert.True(vm.DuplicatePending); Assert.Single(repo.Load().Workspaces.Single().Items);
        vm.ConfirmDuplicateCommand.Execute(null);
        Assert.False(vm.DuplicatePending); Assert.Equal("", vm.NewTarget); Assert.Equal("", vm.NewItemName); Assert.Equal("", vm.NewDescription);
        var rows = repo.Load().Workspaces.Single().Items; Assert.Equal(2, rows.Count); Assert.Equal("local", rows[1].Name); Assert.Equal("description", rows[1].Description);
        Assert.NotEqual(rows[0].Id, rows[1].Id);
    }
}
