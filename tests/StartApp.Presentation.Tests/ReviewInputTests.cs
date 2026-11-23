using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class ReviewInputTests
{
    private static JsonStore Repo() => new(Path.Combine(Path.GetTempPath(), "review-" + Guid.NewGuid(), "store.json"));
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:test@example.com")]
    [InlineData("data:text/html,test")]
    public void Website_input_rejects_non_web_schemes_every_catalog_entry(string target)
    {
        Assert.Equal(Catalog.WebsiteTargetError, Catalog.ValidateInputTarget(ItemType.Link, target));
        var c = new Catalog(new Store());
        Assert.Throws<ArgumentException>(() => c.GetOrAddItem(ItemType.Link, "bad", target));
        foreach (var kind in Enum.GetValues<GlobalListKind>())
            Assert.Throws<ArgumentException>(() => c.AddGlobalEntry(kind, ItemType.Link, "bad", target));
    }
    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("http://localhost:8080/")]
    public void Web_input_remains_valid(string target) => Assert.Null(Catalog.ValidateInputTarget(ItemType.Link, target));
    [Fact] public void Legacy_unsupported_website_does_not_make_library_unreadable()
    {
        using var repo = Repo();
        var path = Path.Combine(Path.GetTempPath(), "review-legacy-" + Guid.NewGuid(), "store.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"schemaVersion\":5,\"items\":{\"old\":{\"id\":\"old\",\"type\":\"link\",\"name\":\"old\",\"target\":\"javascript:alert(1)\"}},\"workspaces\":[{\"id\":\"w\",\"name\":\"work\",\"items\":[{\"id\":\"m\",\"itemId\":\"old\"}]}],\"globalItems\":[],\"captureWhitelist\":[],\"captureBlacklist\":[],\"customCategories\":[]}");
        using var old = new JsonStore(path);
        Assert.Single(old.Load().Workspaces);
        old.Mutate(c => c.Store.Workspaces[0].Name = "still usable");
        Assert.Equal("still usable", old.Load().Workspaces[0].Name);
    }
    [Fact] public void Pasted_web_target_becomes_website_even_when_file_selected()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "work"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.File; vm.NewTarget = "https://example.com/pasted";
        Assert.Equal(ItemType.Link, vm.NewItemType);
        vm.NewItemType = ItemType.File; // submission checks again, even if the dropdown was changed afterward
        vm.AddItemCommand.Execute(null);
        Assert.Equal(ItemType.Link, Assert.Single(repo.Load().Items.Values).Type);
    }
    [Theory]
    [InlineData(GlobalListKind.Whitelist, GlobalListKind.Blacklist)]
    [InlineData(GlobalListKind.Blacklist, GlobalListKind.Whitelist)]
    public void Opposite_capture_lists_block_both_add_orders_without_removing_original(GlobalListKind first, GlobalListKind second)
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.OpenGlobalEnvCommand.Execute(null);
        foreach(var kind in new[]{first,second}) { vm.StartGlobalAddCommand.Execute(kind); vm.NewEntryTypeIndex=0;vm.NewEntryName="same";vm.NewEntryTarget="https://example.com/same";vm.ConfirmGlobalAddCommand.Execute(null); }
        Assert.True(vm.GlobalConflictOpen); Assert.Equal(second, vm.AddingKind);
        Assert.Single(repo.Load().CaptureWhitelist.Concat(repo.Load().CaptureBlacklist).Where(e => e.Target == "https://example.com/same"));
        Assert.Equal("https://example.com/same", vm.NewEntryTarget);
        vm.CloseGlobalConflictCommand.Execute(null);
        Assert.False(vm.GlobalConflictOpen);
        Assert.Single((first == GlobalListKind.Whitelist ? repo.Load().CaptureWhitelist : repo.Load().CaptureBlacklist).Where(e=>!e.IsDefault));
        vm.ConfirmGlobalAddCommand.Execute(null); vm.MoveGlobalConflictCommand.Execute(null);
        Assert.Null(vm.AddingKind); Assert.False(vm.GlobalConflictOpen);
        Assert.Single((second == GlobalListKind.Whitelist ? repo.Load().CaptureWhitelist : repo.Load().CaptureBlacklist).Where(e=>!e.IsDefault));
        Assert.Empty((first == GlobalListKind.Whitelist ? repo.Load().CaptureWhitelist : repo.Load().CaptureBlacklist).Where(e=>!e.IsDefault));
    }
    [Fact] public void Program_alias_conflict_uses_capture_matching_not_only_exact_path()
    {
        var c=new Catalog(new Store()); c.AddGlobalEntry(GlobalListKind.Blacklist,ItemType.Program,"Chrome","chrome.exe");
        Assert.Throws<ArgumentException>(()=>c.AddGlobalEntry(GlobalListKind.Whitelist,ItemType.Program,"browser",@"C:\Apps\Chrome.EXE"));
    }
    [Theory]
    [InlineData(ItemType.Link, "https://example.com/item")]
    [InlineData(ItemType.File, @"C:\sample.txt")]
    [InlineData(ItemType.Folder, @"C:\sample")]
    [InlineData(ItemType.Program, "sample.exe")]
    public void Confirmed_move_covers_all_types_and_does_not_affect_global_items(ItemType type, string target)
    {
        var c = new Catalog(new Store());
        c.AddGlobalEntry(GlobalListKind.GlobalItems, type, "sample", target);
        c.AddGlobalEntry(GlobalListKind.Whitelist, type, "sample", target);
        c.MoveGlobalEntry(GlobalListKind.Blacklist, type, "sample", target);
        Assert.Empty(c.Store.CaptureWhitelist); Assert.Single(c.Store.CaptureBlacklist); Assert.Single(c.Store.GlobalItems);
        c.MoveGlobalEntry(GlobalListKind.Whitelist, type, "sample", target);
        Assert.Single(c.Store.CaptureWhitelist); Assert.Empty(c.Store.CaptureBlacklist);
    }
    [Fact] public void Invalid_confirmed_move_does_not_remove_original()
    {
        var c=new Catalog(new Store()); c.AddGlobalEntry(GlobalListKind.Whitelist,ItemType.Link,"same","https://example.com/");
        Assert.Throws<ArgumentException>(()=>c.MoveGlobalEntry(GlobalListKind.Blacklist,ItemType.Link,"same","javascript:alert(1)"));
        Assert.Single(c.Store.CaptureWhitelist); Assert.Empty(c.Store.CaptureBlacklist);
    }

}
