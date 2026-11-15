using StartApp.Core;
using StartApp.Storage.Json;
using Xunit;

public class SchemaShapeTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private static string Write(string json) { var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, json); return p; }

    [Fact] public void CaseVariantSchemaVersion99_IsNotTreatedAsSchema1()
    {
        var p = Write("{\"SchemaVersion\":99,\"items\":{},\"workspaces\":[]}");
        using var js = new JsonStore(p);
        Assert.ThrowsAny<Exception>(() => js.Load());
        Assert.Contains("99", File.ReadAllText(p));
    }
    [Fact] public void MissingSchemaVersion_FailsClosed()
    {
        var p = Write("{\"items\":{},\"workspaces\":[]}");
        Assert.ThrowsAny<Exception>(() => new JsonStore(p).Load());
    }
    [Fact] public void UnknownProperty_FailsClosed()
    {
        var p = Write("{\"schemaVersion\":1,\"items\":{},\"workspaces\":[],\"future\":true}");
        Assert.ThrowsAny<Exception>(() => new JsonStore(p).Load());
    }
    [Fact] public void UndefinedNumericItemType_FailsClosed()
    {
        var p = Write("{\"schemaVersion\":1,\"items\":{\"i\":{\"id\":\"i\",\"type\":7,\"name\":\"n\",\"target\":\"x\"}},\"workspaces\":[]}");
        Assert.ThrowsAny<Exception>(() => new JsonStore(p).Load());
    }
    [Fact] public void LoadWhileAnotherProcessHoldsTheWriterLock_ReturnsACompleteState()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var probe = Path.Combine(AppContext.BaseDirectory, "StartApp.LockProbe.dll");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"\"{probe}\" \"{p}\" 2500") { RedirectStandardOutput = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        Assert.Equal("LOCKED", proc.StandardOutput.ReadLine());
        using var reader = new JsonStore(p);
        var s = reader.Load(); // reads are allowed; state is the old-or-new complete file, never partial
        Assert.Contains(s.Workspaces, w => w.Name == "probe");
        proc.WaitForExit();
    }
}

public class StructuralNullAndKeyTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private static string Write(string json) { var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, json); return p; }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"items\":null,\"workspaces\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{},\"workspaces\":null}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{},\"workspaces\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{},\"workspaces\":[{\"id\":\"w\",\"name\":\"n\",\"items\":null}]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{},\"workspaces\":[{\"id\":\"w\",\"name\":null,\"items\":[]}]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{},\"workspaces\":[{\"id\":\"w\",\"name\":\"n\",\"items\":[null]}]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{\"i\":null},\"workspaces\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{\"i\":{\"id\":\"i\",\"type\":\"file\",\"name\":null,\"target\":\"C:\\\\a\"}},\"workspaces\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{\"i\":{\"id\":\"other\",\"type\":\"file\",\"name\":\"n\",\"target\":\"C:\\\\a\"}},\"workspaces\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"items\":{\"i\":{\"id\":\"i\",\"type\":\"file\",\"name\":\"n\",\"target\":\"C:\\\\a\"},\"i\":{\"id\":\"i\",\"type\":\"file\",\"name\":\"n\",\"target\":\"C:\\\\b\"}},\"workspaces\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"items\":{},\"workspaces\":[]}")]
    public void MalformedButParsableShapes_FailClosedAsCorrupt_AndBytesStay(string json)
    {
        var p = Write(json);
        Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load());
        Assert.Equal(json, File.ReadAllText(p));
    }

    [Fact] public void TypeSevenString_IsRejectedByEnumCheck_CharacterizationOnly()
    {
        var p = Write("{\"schemaVersion\":1,\"items\":{\"i\":{\"id\":\"i\",\"type\":\"7\",\"name\":\"n\",\"target\":\"x\"}},\"workspaces\":[]}");
        Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load());
    }

    [Fact] public void SpacedFailedLoads_OfUnchangedContent_KeepOneDiagnosticCopy_ChangedContentGetsItsOwn()
    {
        var p = Write("{broken");
        for (var i = 0; i < 4; i++) { Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load()); Thread.Sleep(15); }
        var dir = Path.GetDirectoryName(p)!;
        Assert.Single(Directory.GetFiles(dir, "*.diag-*"));
        File.WriteAllText(p, "{broken2");
        Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load());
        Assert.Equal(2, Directory.GetFiles(dir, "*.diag-*").Length);
        Assert.Equal("{broken2", File.ReadAllText(p));
    }

    [Fact] public void RepeatedFailedLoads_DoNotAccumulateDiagnosticCopies()
    {
        var p = Write("{broken");
        for (var i = 0; i < 4; i++) Assert.Throws<StoreCorruptException>(() => new JsonStore(p).Load());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(p)!, "*.diag-*"));
    }
}
