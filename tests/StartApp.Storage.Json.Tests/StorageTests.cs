using StartApp.Core;
using StartApp.Storage.Json;
using Xunit;

public class JsonStoreTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    [Fact] public void RoundTrip_PreservesMembershipLaunch()
    {
        var p = Tmp(); var js = new JsonStore(p);
        js.Mutate(c => { var w = c.CreateWorkspace("w"); var i = c.GetOrAddItem(ItemType.Link, "l", "https://x.com"); c.AddToWorkspace(w.Id, i.Id, new LaunchConfig(null, "C:\\Tools\\chrome.exe")); });
        var r = js.Load(); Assert.Equal("C:\\Tools\\chrome.exe", r.Workspaces.Single().Items.Single().Launch!.Browser);
        Assert.False(File.Exists(p + ".tmp"));
    }
    [Fact] public void CorruptFile_FailsClosed_OriginalKept_DiagnosticCopyMade()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "{ not json");
        var ex = Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load());
        Assert.True(File.Exists(p)); Assert.True(File.Exists(ex.DiagnosticCopy));
    }
    [Fact] public void DanglingReference_IsRejectedOnLoad()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "{\"schemaVersion\":1,\"items\":{},\"workspaces\":[{\"id\":\"w\",\"name\":\"n\",\"items\":[{\"itemId\":\"missing\"}]}]}");
        Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load()); Assert.True(File.Exists(p));
    }
    [Fact] public void SaveRefusesInconsistentStore()
    {
        var s = new Store(); s.Workspaces.Add(new Workspace { Id = "w", Items = { new Membership("nope") } });
        using var js = new JsonStore(Tmp());
        Assert.Throws<InvalidOperationException>(() => js.Mutate(c => c.Store.Workspaces.Add(s.Workspaces[0])));
    }
    [Fact] public void ConcurrentMutationsInProcess_AreSerialized()
    {
        var p = Tmp(); var js = new JsonStore(p); js.Mutate(c => c.CreateWorkspace("w"));
        Parallel.For(0, 20, n => js.Mutate(c => { var i = c.GetOrAddItem(ItemType.Link, "l" + n, "https://x.com/" + n); c.AddToWorkspace(c.Store.Workspaces[0].Id, i.Id); }));
        Assert.Equal(20, js.Load().Workspaces[0].Items.Count);
    }
}

public class StoreSafetyTests
{
    [Fact]
    public void NewerSchemaIsNotOverwritten()
    {
        var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var raw = "{\"schemaVersion\":99,\"items\":{},\"workspaces\":[]}";
        File.WriteAllText(p, raw);
        var st = new JsonStore(p);
        Assert.Throws<StoreTooNewException>(() => st.Load());
        Assert.Throws<StoreTooNewException>(() => st.Mutate(c => { }));
        Assert.Equal(raw, File.ReadAllText(p));
    }

    [Fact]
    public void CorruptFileFailsClosedAcrossRestart()
    {
        var p = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(p, "{not json");
        Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load());
        // "restart": a brand-new instance must still refuse, and must not overwrite
        var st2 = new JsonStore(p);
        Assert.Throws<StoreCorruptException>(() => st2.Mutate(c => c.CreateWorkspace("x")));
        Assert.Equal("{not json", File.ReadAllText(p));
        var moved = st2.DiscardInvalidFile();
        Assert.True(File.Exists(moved));
        Assert.Empty(st2.Load().Workspaces);
    }
}

public class RevisionTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    [Fact] public void SecondWriterInstanceIsRefused()
    {
        var p = Tmp();
        using var a = new JsonStore(p); a.Mutate(c => c.CreateWorkspace("w"));
        using var b = new JsonStore(p);
        Assert.Throws<StoreLockedException>(() => b.Mutate(c => c.CreateWorkspace("x")));
        Assert.Single(a.Load().Workspaces);
    }
    [Fact] public void FailedMutationWritesNothingAndLeaksNothing()
    {
        var p = Tmp(); using var js = new JsonStore(p); js.Mutate(c => c.CreateWorkspace("w"));
        var before = File.ReadAllText(p);
        Assert.Throws<InvalidOperationException>(() => js.Mutate(c => { c.CreateWorkspace("half"); throw new InvalidOperationException(); }));
        Assert.Equal(before, File.ReadAllText(p));
        Assert.Single(js.Load().Workspaces);
    }
    [Fact] public void LaunchValidation_RejectsShellAndWrongType()
    {
        var c = new Catalog(new Store()); var w = c.CreateWorkspace("w");
        var f = c.GetOrAddItem(ItemType.File, "f", "C:\\a.txt");
        var l = c.GetOrAddItem(ItemType.Link, "l", "https://x.com");
        Assert.Throws<ArgumentException>(() => c.AddToWorkspace(w.Id, f.Id, new LaunchConfig("cmd /c calc & x")));
        Assert.Throws<ArgumentException>(() => c.AddToWorkspace(w.Id, f.Id, new LaunchConfig(null, "C:\\Tools\\chrome.exe")));
        Assert.Throws<ArgumentException>(() => c.AddToWorkspace(w.Id, l.Id, new LaunchConfig("C:\\Tools\\code.exe")));
        Assert.True(c.AddToWorkspace(w.Id, f.Id, new LaunchConfig("C:\\Program Files\\code.exe")));
    }
    [Fact] public void ExternalJsonWithBadLaunchIsRejectedOnLoad()
    {
        var p = Tmp();
        File.WriteAllText(p, "{\"schemaVersion\":1,\"items\":{\"i\":{\"id\":\"i\",\"type\":\"file\",\"name\":\"n\",\"target\":\"C:\\\\a\"}},\"workspaces\":[{\"id\":\"w\",\"name\":\"n\",\"items\":[{\"itemId\":\"i\",\"launch\":{\"openWith\":\"a & b\"}}]}]}");
        Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load());
        Assert.True(File.Exists(p));
    }
}

public class WriteFailureAndProcessTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    [Fact] public void WriteFailure_LeavesOriginalIntact_AndReportsFailure()
    {
        var p = Tmp(); using var js = new JsonStore(p); js.Mutate(c => c.CreateWorkspace("w"));
        var before = File.ReadAllText(p);
        Directory.CreateDirectory(p + ".tmp"); // temp path unusable -> persist step fails
        Assert.ThrowsAny<Exception>(() => js.Mutate(c => c.CreateWorkspace("x")));
        Assert.Equal(before, File.ReadAllText(p));
        Directory.Delete(p + ".tmp");
        Assert.Single(js.Load().Workspaces);
    }

    [Fact] public void SeparateProcessHoldingLock_BlocksThisWriter()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var probe = Path.Combine(AppContext.BaseDirectory, "StartApp.LockProbe.dll");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{probe}\" \"{p}\" 4000") { RedirectStandardOutput = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        Assert.Equal("LOCKED", proc.StandardOutput.ReadLine());   // child now holds the writer lock
        using var js = new JsonStore(p);
        Assert.Throws<StoreLockedException>(() => js.Mutate(c => c.CreateWorkspace("x")));
        proc.WaitForExit();
        js.Mutate(c => c.CreateWorkspace("after"));                // lock released with the process
        Assert.Contains(js.Load().Workspaces, w => w.Name == "after");
    }

    [Fact] public void DiscardInvalidFile_does_not_move_the_file_while_another_writer_holds_the_lock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "store.json");
        using (var holder = new JsonStore(p)) { holder.Mutate(c => c.CreateWorkspace("w")); }
        File.WriteAllText(p, "{ not json");
        using var other = new JsonStore(p);
        using var writer = new JsonStore(p); // second instance becomes the lock holder
        writer.GetType().GetMethod("EnsureWriter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(writer, null);
        Assert.Throws<StoreLockedException>(() => other.DiscardInvalidFile());
        Assert.Equal("{ not json", File.ReadAllText(p));
        Assert.Empty(Directory.GetFiles(dir, "store.json.invalid-*"));
    }

    [Fact] public void DiscardInvalidFile_moves_the_file_when_it_holds_the_lock()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "store.json"); File.WriteAllText(p, "{ not json");
        using var st = new JsonStore(p);
        var moved = st.DiscardInvalidFile();
        Assert.NotNull(moved); Assert.False(File.Exists(p)); Assert.Equal("{ not json", File.ReadAllText(moved!));
    }
}
