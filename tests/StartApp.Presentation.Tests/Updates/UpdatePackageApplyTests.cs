using System.IO.Compression;
using System.Security.Cryptography;
using StartApp.Presentation;

namespace StartApp.Presentation.Tests;

public class UpdatePackageApplyTests
{
    private sealed class FakeTransport(byte[] zipBytes, bool failWithIo = false) : IUpdatePackageTransport
    {
        public int Calls;
        public Task DownloadAsync(ApprovedUpdate update, string destinationPath, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            if (failWithIo) throw new IOException("simulated network failure");
            File.WriteAllBytes(destinationPath, zipBytes);
            progress?.Report(100);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSource(ApprovedUpdate? update) : IApprovedUpdateSource
    {
        public Task<ApprovedUpdate?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(update);
    }

    private static readonly ApprovedUpdate Offer = new("0.2.0-beta.1", new string('a', 40), "", "11/22/0/33");

    private static (byte[] Bytes, string Sha256, long Size) MakeZip()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("StartApp.App.exe").Open()))
                writer.Write("new version bytes");
            using (var nestedWriter = new StreamWriter(archive.CreateEntry("runtimes/win/StartApp.Core.dll").Open()))
                nestedWriter.Write("dll bytes");
        }
        var bytes = memory.ToArray();
        return (bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength);
    }

    private static ApprovedUpdate ApprovedFor(byte[] bytes, string sha256, long size) =>
        new(Offer.Version, Offer.Commit, sha256, $"11/22/{size}/33");

    private static UpdateViewModel NewVm(ApprovedUpdate offer, IUpdatePackageTransport transport, string workRoot) =>
        new(new FixedSource(offer), "0.1.0-beta.1", _ => { }, revalidateBeforeDownload: false,
            packageTransport: transport, updateWorkRoot: workRoot);

    [Fact]
    public async Task Click_downloads_verifies_and_stages_then_raises_ready()
    {
        var (bytes, sha256, size) = MakeZip();
        var root = Path.Combine(Path.GetTempPath(), "startapp-upd-" + Guid.NewGuid().ToString("N"));
        var transport = new FakeTransport(bytes);
        using var vm = NewVm(ApprovedFor(bytes, sha256, size), transport, root);
        StagedUpdate? staged = null; vm.StagedUpdateReady += s => staged = s;
        await vm.CheckAsync();
        Assert.True(vm.UpdateAvailable);
        Assert.Contains("בלי אתר ובלי התקנה ידנית", vm.Status);
        vm.DownloadCommand.Execute(null);
        await vm.CurrentCheck;
        Assert.NotNull(staged);
        Assert.Equal(Offer.Version, staged!.Version);
        Assert.True(File.Exists(Path.Combine(staged.StagingDirectory, "StartApp.App.exe")));
        Assert.True(File.Exists(Path.Combine(staged.StagingDirectory, "runtimes", "win", "StartApp.Core.dll")));
        Assert.Contains("נסגרת כעת", vm.Status);
        Assert.False(Directory.EnumerateFiles(root).Any(f => f.EndsWith(".zip"))); // package zip cleaned after extract
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Hash_mismatch_cancels_install_and_keeps_current_version()
    {
        var (bytes, _, size) = MakeZip();
        var root = Path.Combine(Path.GetTempPath(), "startapp-upd-" + Guid.NewGuid().ToString("N"));
        using var vm = NewVm(ApprovedFor(bytes, new string('0', 64), size), new FakeTransport(bytes), root);
        var raised = false; vm.StagedUpdateReady += _ => raised = true;
        await vm.CheckAsync();
        vm.DownloadCommand.Execute(null);
        await vm.CurrentCheck;
        Assert.False(raised);
        Assert.Equal("לא ניתן לאמת את חבילת העדכון. ההתקנה בוטלה והגרסה הנוכחית ממשיכה לעבוד.", vm.Status);
        Assert.False(vm.IsBusy);
        Assert.False(Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any());
    }

    [Fact]
    public async Task Size_mismatch_against_publication_record_cancels_install()
    {
        var (bytes, sha256, _) = MakeZip();
        var root = Path.Combine(Path.GetTempPath(), "startapp-upd-" + Guid.NewGuid().ToString("N"));
        using var vm = NewVm(ApprovedFor(bytes, sha256, bytes.LongLength + 1), new FakeTransport(bytes), root);
        var raised = false; vm.StagedUpdateReady += _ => raised = true;
        await vm.CheckAsync();
        vm.DownloadCommand.Execute(null);
        await vm.CurrentCheck;
        Assert.False(raised);
        Assert.Equal("לא ניתן לאמת את חבילת העדכון. ההתקנה בוטלה והגרסה הנוכחית ממשיכה לעבוד.", vm.Status);
    }

    [Fact]
    public async Task Download_failure_offers_the_manual_route_and_never_stages()
    {
        var (bytes, sha256, size) = MakeZip();
        var root = Path.Combine(Path.GetTempPath(), "startapp-upd-" + Guid.NewGuid().ToString("N"));
        using var vm = NewVm(ApprovedFor(bytes, sha256, size), new FakeTransport(bytes, failWithIo: true), root);
        var raised = false; vm.StagedUpdateReady += _ => raised = true;
        await vm.CheckAsync();
        vm.DownloadCommand.Execute(null);
        await vm.CurrentCheck;
        Assert.False(raised);
        Assert.Contains(UpdateViewModel.OfficialWebsite, vm.Status);
        Assert.False(vm.IsBusy);
        Assert.True(vm.UpdateAvailable); // the offer stays so the user can retry
    }

    [Fact]
    public async Task Transport_throw_leaves_no_staging_dir_and_zip_cleaned()
    {
        var (bytes, sha256, size) = MakeZip();
        var root = Path.Combine(Path.GetTempPath(), "startapp-upd-" + Guid.NewGuid().ToString("N"));
        var transport = new FakeTransport(bytes, failWithIo: true);
        await Assert.ThrowsAsync<IOException>(() => UpdatePackageStager.StageAsync(ApprovedFor(bytes, sha256, size), transport, root, null, CancellationToken.None));
        Assert.False(Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any());
    }

    [Fact]
    public async Task Zip_entry_escaping_staging_dir_is_rejected()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            archive.CreateEntry("../escape.txt");
        var bytes = memory.ToArray();
        var offer = ApprovedFor(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength);
        var root = Path.Combine(Path.GetTempPath(), "startapp-upd-" + Guid.NewGuid().ToString("N"));
        await Assert.ThrowsAsync<UpdatePackageVerificationException>(() => UpdatePackageStager.StageAsync(offer, new FakeTransport(bytes), root, null, CancellationToken.None));
    }

    [Fact]
    public void Swap_replaces_install_dir_with_staged_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "startapp-swap-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "install"); var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "StartApp.App.exe"), "old");
        Directory.CreateDirectory(staging); File.WriteAllText(Path.Combine(staging, "StartApp.App.exe"), "new");
        UpdateSwapper.ApplyStagedUpdate(staging, target);
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "StartApp.App.exe")));
        Assert.False(Directory.Exists(staging));
        Assert.Single(Directory.GetDirectories(root)); // backup removed after success
    }

    [Fact]
    public void Swap_with_missing_staging_rolls_back_install_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "startapp-swap-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "install");
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "StartApp.App.exe"), "old");
        Assert.Throws<DirectoryNotFoundException>(() => UpdateSwapper.ApplyStagedUpdate(Path.Combine(root, "missing"), target));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "StartApp.App.exe")));
    }

    [Fact]
    public void Apply_mode_times_out_when_app_never_exits()
    {
        var code = UpdateSwapper.RunApplyMode("x", "y", 7, "z", processAlive: _ => true, relauncher: _ => { }, waitTimeout: TimeSpan.FromMilliseconds(300));
        Assert.Equal(2, code);
    }

    [Fact]
    public void Apply_mode_swaps_and_relaunches_after_app_exit()
    {
        var root = Path.Combine(Path.GetTempPath(), "startapp-swap-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "install"); var staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "StartApp.App.exe"), "old");
        Directory.CreateDirectory(staging); File.WriteAllText(Path.Combine(staging, "StartApp.App.exe"), "new");
        string? relaunched = null;
        var code = UpdateSwapper.RunApplyMode(staging, target, 7, "relaunch.exe", processAlive: _ => false, relauncher: exe => relaunched = exe);
        Assert.Equal(0, code);
        Assert.Equal("relaunch.exe", relaunched);
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "StartApp.App.exe")));
    }
}
