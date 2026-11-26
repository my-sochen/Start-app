using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

namespace StartApp.Presentation.Tests;

/// <summary>סביבה גלובלית (approved 22.11 03:08): the tab's three lists persist, the blacklist ships removable defaults
/// (Chrome, Edge, Start-app), a v4 file gains them on upgrade, and captures apply the lists — blacklist rows never appear
/// (including הוסף ממה שפתוח, which shares the same scan path) and whitelist rows pin to the top (priority only, 03:13).</summary>
public class GlobalEnvTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    private sealed class FixedCapturer(params CapturedItem[] items) : IOpenItemCapturer
    {
        public CaptureScan Scan() => new(items, Array.Empty<string>());
    }
    private static void ScanDone(MainViewModel vm)
    {
        Assert.NotNull(vm.CurrentScan);
        Assert.True(vm.CurrentScan!.Wait(TimeSpan.FromSeconds(15)), "scan did not finish");
        vm.CurrentScan!.GetAwaiter().GetResult();
        Assert.False(vm.IsScanning);
    }

    [Fact] public void Add_and_remove_round_trip_persists_per_list()
    {
        using var repo = new JsonStore(Tmp());
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.OpenGlobalEnvCommand.Execute(null);
        Assert.True(vm.InGlobalEnv);
        Assert.Equal(3, vm.BlacklistRows.Count); // shipped defaults
        Assert.All(vm.BlacklistRows, r => Assert.True(r.Entry.IsDefault));

        vm.StartGlobalAddCommand.Execute(GlobalListKind.GlobalItems);
        Assert.Equal(GlobalListKind.GlobalItems, vm.AddingKind);
        vm.NewEntryTypeIndex = 0; vm.NewEntryName = "Gmail"; vm.NewEntryTarget = "https://mail.google.com/";
        vm.ConfirmGlobalAddCommand.Execute(null);
        Assert.Null(vm.AddingKind);
        var row = Assert.Single(vm.GlobalItemRows);
        Assert.Equal("Gmail", row.Name); Assert.Equal("אתר", row.TypeLabel);

        vm.StartGlobalAddCommand.Execute(GlobalListKind.Blacklist);
        Assert.Equal(1, vm.NewEntryTypeIndex); // blacklist add defaults to תוכנה
        vm.NewEntryName = "Discord"; vm.NewEntryTarget = "Discord.exe";
        vm.ConfirmGlobalAddCommand.Execute(null);
        Assert.Equal(4, vm.BlacklistRows.Count);

        var saved = repo.Load();
        Assert.Single(saved.GlobalItems);
        Assert.Equal(4, saved.CaptureBlacklist.Count);

        vm.RemoveGlobalEntryCommand.Execute(vm.BlacklistRows.Single(r => r.Entry.IsDefault && r.Name == "Chrome"));
        Assert.Equal(3, vm.BlacklistRows.Count);
        Assert.DoesNotContain(repo.Load().CaptureBlacklist, e => e.Name == "Chrome"); // a removed default stays removed
    }

    [Fact] public void Duplicate_target_returns_existing_row_instead_of_duplicating()
    {
        using var repo = new JsonStore(Tmp());
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.OpenGlobalEnvCommand.Execute(null);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            vm.StartGlobalAddCommand.Execute(GlobalListKind.Whitelist);
            vm.NewEntryTypeIndex = 2; vm.NewEntryName = "לוח משימות"; vm.NewEntryTarget = "C:\\docs\\board.md";
            vm.ConfirmGlobalAddCommand.Execute(null);
        }
        Assert.Single(vm.WhitelistRows);
    }

    [Fact] public void Invalid_target_shows_an_error_and_keeps_the_form()
    {
        using var repo = new JsonStore(Tmp());
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.OpenGlobalEnvCommand.Execute(null);
        vm.StartGlobalAddCommand.Execute(GlobalListKind.GlobalItems);
        vm.NewEntryTypeIndex = 0; vm.NewEntryName = "not a link"; vm.NewEntryTarget = ":::bad:::";
        vm.ConfirmGlobalAddCommand.Execute(null);
        Assert.NotNull(vm.AddingKind);
        Assert.True(vm.GlobalEnvErrorVisible);
        Assert.Empty(repo.Load().GlobalItems);
    }

    [Fact] public void V4_file_upgrades_with_the_seeded_blacklist_defaults()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "{\"schemaVersion\":4,\"items\":{},\"workspaces\":[],\"customCategories\":[]}");
        using (var repo = new JsonStore(p))
        {
            var s = repo.Load();
            Assert.Equal(3, s.CaptureBlacklist.Count);
            Assert.Contains(s.CaptureBlacklist, e => e.Target == "chrome.exe" && e.IsDefault);
            repo.Mutate(c => c.AddGlobalEntry(GlobalListKind.GlobalItems, ItemType.Link, "Gmail", "https://mail.google.com/"));
        }
        var text = File.ReadAllText(p);
        using (var saved = System.Text.Json.JsonDocument.Parse(text))
            Assert.Equal(JsonStore.SupportedSchema, saved.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Contains("globalItems", text);
        Assert.True(File.Exists(p + ".schema4.bak"));
        using (var repo2 = new JsonStore(p))
        {
            var s2 = repo2.Load();
            Assert.Single(s2.GlobalItems);
            Assert.Equal(3, s2.CaptureBlacklist.Count);
        }
    }

    [Fact] public void Removed_blacklist_default_stays_removed_after_reload()
    {
        var p = Tmp();
        using (var repo = new JsonStore(p))
            repo.Mutate(c => c.RemoveGlobalEntry(GlobalListKind.Blacklist, "ge_default_chrome"));
        using (var repo2 = new JsonStore(p))
            Assert.DoesNotContain(repo2.Load().CaptureBlacklist, e => e.Target == "chrome.exe");
    }

    [Fact] public void Capture_excludes_blacklisted_and_pins_whitelist_on_top()
    {
        using var repo = new JsonStore(Tmp());
        repo.Mutate(c =>
        {
            c.AddGlobalEntry(GlobalListKind.Whitelist, ItemType.Folder, "תיקיית מסמכים", "C:\\Users\\Yitzhak\\Documents");
            c.AddGlobalEntry(GlobalListKind.Blacklist, ItemType.Program, "Discord", "Discord.exe");
        });
        var cap = new FixedCapturer(
            new CapturedItem(ItemType.Program, "Discord", "C:\\Users\\Yitzhak\\AppData\\Local\\Discord\\Discord.exe"),
            new CapturedItem(ItemType.Program, "Chrome", "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"),
            new CapturedItem(ItemType.Link, "example.com", "https://example.com/"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        Assert.Equal(2, vm.CaptureRows.Count);
        Assert.Equal("תיקיית מסמכים", vm.CaptureRows[0].Item.Name); // pinned on top although never detected open
        Assert.Equal("example.com", vm.CaptureRows[1].Item.Name);
        Assert.DoesNotContain(vm.CaptureRows, r => r.Item.Name is "Discord" or "Chrome");
    }

    [Fact] public void Whitelisted_item_detected_open_is_not_duplicated()
    {
        using var repo = new JsonStore(Tmp());
        repo.Mutate(c => c.AddGlobalEntry(GlobalListKind.Whitelist, ItemType.Program, "Notepad", "notepad.exe"));
        var cap = new FixedCapturer(new CapturedItem(ItemType.Program, "Notepad", "C:\\Windows\\System32\\notepad.exe"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); ScanDone(vm);
        var row = Assert.Single(vm.CaptureRows);
        Assert.Equal("Notepad", row.Item.Name);
    }

    private sealed class RecordingLauncher : IItemLauncher
    {
        public readonly List<Item> Calls = new();
        public LaunchResult Next = new(LaunchStatus.Started);
        public LaunchResult Launch(Item item, LaunchConfig? launch) { Calls.Add(item); return Next; }
    }

    [Fact] public void Autostart_opens_every_global_item_and_reports()
    {
        using var repo = new JsonStore(Tmp());
        repo.Mutate(c => { c.AddGlobalEntry(GlobalListKind.GlobalItems, ItemType.Link, "Gmail", "https://mail.google.com/"); c.AddGlobalEntry(GlobalListKind.GlobalItems, ItemType.Link, "WhatsApp", "https://web.whatsapp.com/"); });
        var launcher = new RecordingLauncher();
        var vm = new MainViewModel(repo, launcher);
        vm.OpenGlobalItemsOnStart();
        Assert.Equal(2, launcher.Calls.Count);
        Assert.Contains("Gmail", launcher.Calls.Select(i => i.Name));
        Assert.Equal("נפתחו 2 פריטי סביבה גלובלית", vm.Status);
    }

    [Fact] public void Autostart_names_items_the_launcher_could_not_open()
    {
        using var repo = new JsonStore(Tmp());
        repo.Mutate(c => { c.AddGlobalEntry(GlobalListKind.GlobalItems, ItemType.Link, "Gmail", "https://mail.google.com/"); c.AddGlobalEntry(GlobalListKind.GlobalItems, ItemType.Link, "WhatsApp", "https://web.whatsapp.com/"); });
        var launcher = new RecordingLauncher();
        var vm = new MainViewModel(repo, launcher);
        launcher.Next = new LaunchResult(LaunchStatus.Failed);
        vm.OpenGlobalItemsOnStart();
        Assert.Equal(2, launcher.Calls.Count); // every item was attempted
        Assert.Equal("נפתחו 0; לא ניתן לפתוח: Gmail, WhatsApp", vm.Status); // nothing claims success it did not get
    }

    [Fact] public void Autostart_with_no_global_items_says_so()
    {
        using var repo = new JsonStore(Tmp());
        var launcher = new RecordingLauncher();
        var vm = new MainViewModel(repo, launcher);
        vm.OpenGlobalItemsOnStart();
        Assert.Empty(launcher.Calls);
        Assert.Equal("אין פריטי סביבה גלובלית לפתיחה", vm.Status);
    }
}
