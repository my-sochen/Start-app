using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using StartApp.Presentation;
using StartApp.Storage.Json;

// Separate test executable, NOT compiled into the product or added to the solution.
// Renders the product XAML using WPF, synthetic source and fresh synthetic store.
// No real browser/network call, App.OnStartup or real LOCALAPPDATA is used.
internal static class Program
{
    private sealed class SyntheticSource(Func<CancellationToken, Task<ApprovedUpdate?>> read) : IApprovedUpdateSource
    { public Task<ApprovedUpdate?> ReadAsync(CancellationToken token) => read(token); }
    private static readonly ApprovedUpdate Patch = new("1.0.1", new string('a', 40), new string('b', 64));
    private static readonly List<object> Evidence = new();
    [STAThread]
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 1) return 2;
        var output = Path.GetFullPath(args[0]);
        // Refuse reuse, never delete anything. The caller must choose a NEW folder.
        if (Directory.Exists(output) || File.Exists(output)) return 3;
        Directory.CreateDirectory(output);
        var appAssembly=typeof(MainWindow).Assembly;
        var metadata=appAssembly.GetCustomAttributesData().Where(a=>a.AttributeType.FullName=="System.Reflection.AssemblyMetadataAttribute" && a.ConstructorArguments.Count==2 && a.ConstructorArguments[0].Value as string=="StartAppSourceCommit").ToArray();
        var info=appAssembly.GetCustomAttributesData().Single(a=>a.AttributeType.FullName=="System.Reflection.AssemblyInformationalVersionAttribute").ConstructorArguments[0].Value as string;
        if(metadata.Length!=1 || metadata[0].ConstructorArguments[1].Value is not string loadedSource || InstalledBuildIdentity.Parse(info,loadedSource) is null)throw new Exception("Loaded App assembly identity invalid");
        var loadedPath=appAssembly.Location;
        Evidence.Add(new{state="loaded-App-identity",sourceCommit=loadedSource,informationalVersion=info,appAssemblySha256=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(loadedPath))),assemblyName=appAssembly.GetName().Name,assemblyFile=Path.GetFileName(loadedPath),identitySource="actual loaded App assembly reflection and its on-disk bytes",scope="synthetic controlled native test only; no production activation"});
        // Construct the actual default factory without starting any command/event.
        // Inspect its selected source; no ReadAsync, startup or browser call here.
        using (var defaultUpdates = UpdateComposition.Create())
        {
            var sourceField = typeof(UpdateViewModel).GetField("_source", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?? throw new Exception("Default composition source field unavailable");
            if (sourceField.GetValue(defaultUpdates) is not MainBetaNoticeSource || defaultUpdates.InstalledVersionDisplay != "גרסה מותקנת: 0.1.0-proto.2")
                throw new Exception("Stamped default Create must select actual-identity MAIN source");
            Evidence.Add(new { state="default-factory-source-selection", factory="UpdateComposition.Create", selectedSource="MainBetaNoticeSource", installedVersion=defaultUpdates.InstalledVersionDisplay, scope="construction and source type only; no commands, startup, HTTP or browser execution" });
        }
        var app = new StartApp.App.App();
        // Load App.xaml resources into Application.Current (the parameterless ctor does not; generated Main normally does this). Startup never fires without Run().
        app.InitializeComponent();
        // Explicitly isolated root for fixtures. Production default composition is
        // now live-read capable and must not run against public APIs in this test.
        var window = new MainWindow(new UpdateViewModel(new InactiveUpdateSource(),null,_=>throw new Exception("Unexpected fixture browser"))) { Width = 900, Height = 560 };
        using var store = new JsonStore(Path.Combine(output, "synthetic-store", "store.json"));
        window.DataContext = new MainViewModel(store, new NotImplementedLauncher());
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                // Find the real production update row via its existing view-model type.
                var row = ((Grid)window.Content).Children.OfType<Border>().Single(b => b.DataContext is UpdateViewModel);
                foreach (var width in new[] { 900, 720 })
                {
                    window.Width = width;
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-inactive");
                    CaptureSettings(window, window.Updates, output, $"{width}-settings-inactive", width);
                    var opened = new List<string>();
                    using var available = new UpdateViewModel(new SyntheticSource(_ => Task.FromResult<ApprovedUpdate?>(Patch)), "1.0.0", uri => opened.Add(uri.AbsoluteUri));
                    row.DataContext = available;
                    await available.CheckAsync();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-available");
                    CaptureSettings(window, available, output, $"{width}-settings-available", width);
                    CapturePopup(window, available, output, $"{width}-startup-popup", width);
                    available.DownloadCommand.Execute(null);
                    if (opened.Count != 1 || opened[0] != UpdateViewModel.OfficialWebsite) throw new Exception("Synthetic handoff target mismatch");
                    var betaOpened = new List<string>();
                    using var beta = new UpdateViewModel(new SyntheticSource(_ => Task.FromResult<ApprovedUpdate?>(new("0.2.0-beta.1", new string('a',40), new string('b',64)))),
                        "0.1.0-proto.2", uri => betaOpened.Add(uri.AbsoluteUri), revalidateBeforeDownload: true);
                    int betaNotices = 0; beta.StartupUpdateAvailable += () => betaNotices++;
                    await beta.CheckOnStartupOnceAsync(); await beta.CheckOnStartupOnceAsync();
                    if (betaNotices != 1 || !beta.UpdateAvailable || !beta.Status.Contains("בטא")) throw new Exception("Beta startup notice missing");
                    CaptureSettings(window, beta, output, $"{width}-beta-settings", width);
                    CapturePopup(window, beta, output, $"{width}-beta-startup-popup", width);
                    if (betaOpened.Count != 0) throw new Exception("Beta notice opened browser without explicit click");
                    beta.DownloadCommand.Execute(null); await beta.CurrentCheck;
                    if (betaOpened.Count != 1 || betaOpened[0] != UpdateViewModel.OfficialWebsite) throw new Exception("Beta handoff mismatch");
                    using var failure = new UpdateViewModel(new SyntheticSource(_ => throw new HttpRequestException("synthetic")), "1.0.0", _ => throw new Exception("Unexpected browser call"));
                    row.DataContext = failure; await failure.CheckAsync();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-failure");
                    CaptureSettings(window, failure, output, $"{width}-settings-failure", width);
                    var gate = new TaskCompletionSource<ApprovedUpdate?>();
                    using var busy = new UpdateViewModel(new SyntheticSource(_ => gate.Task), "1.0.0", _ => throw new Exception("Unexpected browser call"));
                    row.DataContext = busy; var pending = busy.CheckAsync();
                    await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Capture(window, output, $"{width}-busy");
                    CaptureSettings(window, busy, output, $"{width}-settings-busy", width);
                    gate.SetResult(null); await pending;
                    row.DataContext = window.Updates;
                }
                await CaptureProductionEventPath(window, output);
                await CaptureMainNoticeEventPath(window, output);
                File.WriteAllText(Path.Combine(output, "capture-evidence.json"), JsonSerializer.Serialize(Evidence, new JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "capture-error.txt"), ex.ToString()); app.Shutdown(1); }
        };
        return app.Run(window);
    }
    private sealed class CatalogHandler(byte[] body) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (request.RequestUri!.AbsoluteUri != "https://my-sochen.github.io/Start-app/synthetic-beta-notice.json") throw new Exception("Wrong source");
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(body) };
            response.Content.Headers.ContentType = new("application/json"); return Task.FromResult(response);
        }
    }
    private static async Task CaptureProductionEventPath(Window owner, string output)
    {
        // Exact real composition reads actual App identity. Handler supplies bytes only, never a real request.
        var now = DateTimeOffset.UtcNow;
        var doc = new { schema=1, revision=1, channel="beta-notice", generated_utc=now.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ssZ"), expires_utc=now.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ"), releases=new[] {new {version="0.2.0-beta.1", tag="v0.2.0-beta.1", commit=new string('a',40), release_id=1, asset_id=2, asset="StartApp-win-x64.zip", size=10, sha256=new string('b',64), layout="portable-v1", approval_record_id="synthetic-not-approval"}}};
        foreach (var width in new[] {900,720})
        {
            var handler = new CatalogHandler(JsonSerializer.SerializeToUtf8Bytes(doc)); var opened = new List<string>();
            var updates = UpdateComposition.CreateWithTransport(new("https://my-sochen.github.io/Start-app/synthetic-beta-notice.json"), handler, uri=>opened.Add(uri.AbsoluteUri), ()=>now);
            if (updates.InstalledVersionDisplay != "גרסה מותקנת: 0.1.0-proto.2") throw new Exception("Actual App identity not composed");
            using var store = new JsonStore(Path.Combine(output,"event-store-"+width,"store.json"));
            var actual = new MainWindow(updates) { Owner=owner, Width=width, Height=560, DataContext=new MainViewModel(store,new NotImplementedLauncher()) };
            actual.Show();
            var deadline=DateTime.UtcNow.AddSeconds(5); UpdateAvailableWindow[] notices=[];
            do {await actual.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);notices=actual.OwnedWindows.OfType<UpdateAvailableWindow>().ToArray();if(notices.Length==1)break;await Task.Delay(25);}while(DateTime.UtcNow<deadline);
            if(notices.Length!=1||handler.Calls!=1||opened.Count!=0||!updates.UpdateAvailable)throw new Exception("Real Loaded/source/event/popup chain failed");
            if(updates.Status != "גרסת בטא זמינה 0.2.0-beta.1. ההורדה תיפתח באתר הרשמי; ההתקנה ידנית.")throw new Exception("Exact isolated offered beta version missing");
            var popup=notices.Single();CheckVersionInlines(popup);Capture(popup,output,width+"-event-beta-popup");CaptureSettings(actual,updates,output,width+"-event-beta-settings",width);
            await updates.CheckOnStartupOnceAsync();
            if(handler.Calls!=1||actual.OwnedWindows.OfType<UpdateAvailableWindow>().Count()!=1)throw new Exception("Duplicate startup discovery/popup");
            popup.Close();await actual.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
            if(!updates.UpdateAvailable||opened.Count!=0)throw new Exception("Dismissal affected optional offer");
            updates.DownloadCommand.Execute(null);await updates.CurrentCheck;
            if(handler.Calls!=2||opened.Count!=1||opened.Single()!=UpdateViewModel.OfficialWebsite)throw new Exception("Event path explicit click/revalidation failed");
            Evidence.Add(new {state=width+"-production-event-path", handler.Calls, popupCreatedBy="MainWindow.Loaded -> real stamped composition -> BetaNoticeSource HTTP/schema -> startup event -> owned actual popup", network="synthetic handler only", publication="none", explicitBrowserSinkCalls=opened.Count});
            actual.Close();
        }
    }
    private sealed class MainNoticeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string,string> _routes=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"main-notice.fixture.json")))!;
        public readonly List<string> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            const string root="https://api.github.com/repos/my-sochen/Start-app";
            if(request.RequestUri is null || !request.RequestUri.AbsoluteUri.StartsWith(root+"/",StringComparison.Ordinal) || request.Headers.Authorization is not null)throw new Exception("Unexpected MAIN notice request");
            var path=request.RequestUri.AbsoluteUri[root.Length..];Requests.Add(path);
            var response=new HttpResponseMessage(System.Net.HttpStatusCode.OK){RequestMessage=request,Content=new StringContent(_routes[path],System.Text.Encoding.UTF8,"application/json")};return Task.FromResult(response);
        }
    }
    private static async Task CaptureMainNoticeEventPath(Window owner,string output)
    {
        foreach(var width in new[]{900,720})
        {
            var handler=new MainNoticeHandler();var opened=new List<string>();
            var updates=UpdateComposition.CreateWithMainNoticeTransport(handler,u=>opened.Add(u.AbsoluteUri),()=>new DateTimeOffset(2026,11,18,9,0,0,TimeSpan.Zero));
            if(updates.InstalledVersionDisplay!="גרסה מותקנת: 0.1.0-proto.2")throw new Exception("Actual combined App identity missing");
            using var store=new JsonStore(Path.Combine(output,"main-notice-store-"+width,"store.json"));
            var actual=new MainWindow(updates){Owner=owner,Width=width,Height=560,DataContext=new MainViewModel(store,new NotImplementedLauncher())};actual.Show();
            var deadline=DateTime.UtcNow.AddSeconds(5);UpdateAvailableWindow[] notices=[];
            do{await actual.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);notices=actual.OwnedWindows.OfType<UpdateAvailableWindow>().ToArray();if(notices.Length==1)break;await Task.Delay(25);}while(DateTime.UtcNow<deadline);
            if(notices.Length!=1 || handler.Requests.Count!=11 || opened.Count!=0 || !updates.UpdateAvailable)throw new Exception("MAIN notice Loaded/source/popup chain failed");
            var popup=notices.Single();CheckVersionInlines(popup);Capture(popup,output,width+"-main-notice-popup");CaptureSettings(actual,updates,output,width+"-main-notice-settings",width);
            await updates.CheckOnStartupOnceAsync();if(handler.Requests.Count!=11 || actual.OwnedWindows.OfType<UpdateAvailableWindow>().Count()!=1)throw new Exception("MAIN notice duplicate startup");
            popup.Close();updates.DownloadCommand.Execute(null);await updates.CurrentCheck;
            if(handler.Requests.Count!=22 || opened.Count!=1 || opened.Single()!=UpdateViewModel.OfficialWebsite)throw new Exception("MAIN notice explicit recheck/site handoff");
            Evidence.Add(new{state=width+"-main-notice-event-path",requests=handler.Requests.ToArray(),startupReads=11,clickReads=11,explicitBrowserSinkCalls=opened.Count,publication="none",network="fixed synthetic API transcript only",chain="MainWindow.Loaded -> actual stamped App factory -> MainBetaNoticeSource tree/blob/publication/asset/tag/ref -> owned popup -> explicit site sink"});
            actual.Close();
        }
    }
    private static void CheckVersionInlines(Window window)
    {
        var probe=new VersionDisplayText {FlowDirection=FlowDirection.RightToLeft,DisplayText="גרסת בטא זמינה 0.2.0-beta.1. ההתקנה ידנית."};
        if(probe.Inlines.OfType<System.Windows.Documents.Run>().Count(r=>r.Text=="0.2.0-beta.1"&&r.FlowDirection==FlowDirection.LeftToRight)!=1)throw new Exception("LTR inline before sentence period missing");
        probe.DisplayText="אין כרגע גרסה מאושרת";
        if(string.Concat(probe.Inlines.OfType<System.Windows.Documents.Run>().Select(r=>r.Text))!=probe.DisplayText||probe.Inlines.OfType<System.Windows.Documents.Run>().Any(r=>r.ReadLocalValue(System.Windows.Documents.Inline.FlowDirectionProperty)!=DependencyProperty.UnsetValue))throw new Exception("Old version inline retained after status changed");
        var displays=Descendants(window).OfType<VersionDisplayText>().ToArray();
        if(displays.Length==0)throw new Exception("No version display controls");
        foreach(var block in displays)
        {
            var runs=block.Inlines.OfType<System.Windows.Documents.Run>().ToArray();
            if(string.Concat(runs.Select(r=>r.Text))!=block.DisplayText)throw new Exception("Version display changed original text");
            foreach(var token in new[]{"0.1.0-proto.2","0.2.0-beta.1"}.Where(block.DisplayText.Contains))
                if(runs.Count(r=>r.Text==token&&r.FlowDirection==FlowDirection.LeftToRight)!=1)throw new Exception("Exact LTR version inline missing");
        }
    }
    private static void CapturePopup(Window owner, UpdateViewModel updates, string output, string name, int width)
    {
        var popup = new UpdateAvailableWindow(updates) { Owner = owner, Width = width == 900 ? 560 : 460, Height = 300 };
        popup.Show(); popup.UpdateLayout();
        var buttons = Descendants(popup).OfType<Button>().ToArray();
        if (!buttons.Any(b => ReferenceEquals(b.Command, updates.DownloadCommand) && b.IsEnabled))
            throw new Exception("Popup official update button missing or disabled");
        if (!buttons.Any(b => b.Content as string == "לא עכשיו")) throw new Exception("Popup dismissal missing");
        CheckVersionInlines(popup);Capture(popup, output, name); popup.Close();
        if (!updates.UpdateAvailable) throw new Exception("Popup dismissal changed update state");
    }
    private static void CaptureSettings(Window owner, UpdateViewModel updates, string output, string name, int width)
    {
        // Actual settings screen XAML (the in-window הגדרות view), shared synthetic VM,
        // hosted standalone for the capture. Production embeds the same view as an overlay.
        var host = new Window { Owner = owner, Width = width, Height = 560, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        host.Content = new SettingsView { DataContext = updates };
        host.Show(); host.UpdateLayout();
        var buttons = Descendants(host).OfType<Button>().Where(b => b.Content is string text && text == "בדוק עדכונים").ToArray();
        if (buttons.Length != 1) throw new Exception("Settings must own exactly one manual check");
        var topBar = ((Grid)owner.Content).Children.OfType<Border>().Single(b => b.DataContext is UpdateViewModel);
        if (Descendants(topBar).OfType<Button>().Any(b => b.Content is string text && text == "בדוק עדכונים"))
            throw new Exception("Manual check must not remain in main top bar");
        if (!Descendants(host).OfType<TextBlock>().Any(t => t.Text == "עדכונים"))
            throw new Exception("Updates group header missing");
        if (!Descendants(host).OfType<TextBlock>().Any(t => t.Text == updates.VersionValueDisplay))
            throw new Exception("Installed version value missing");
        Capture(host, output, name); host.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Descendants(child)) yield return next; }
    }
    private static void Capture(Window window, string output, string name)
    {
        window.UpdateLayout();
        // Render the window visual root, not its margin/RTL-transformed child.
        // The root includes the actual opaque window background and preserves
        // inherited RTL transforms, avoiding clipped/mirrored child-only renders.
        var width = checked((int)Math.Ceiling(window.ActualWidth)); var height = checked((int)Math.Ceiling(window.ActualHeight));
        var popup = window is UpdateAvailableWindow;
        if (width < (popup ? 460 : 720) || height < (popup ? 260 : 400))
            throw new Exception($"Invalid window render bounds: {width}x{height}");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        // Window.ActualWidth/Height include nonclient space not painted by WPF.
        // Find every painted pixel. Only discard wholly transparent TRAILING
        // rows/columns, never content, leading space or a partly painted strip.
        var pixels = new byte[checked(width * height * 4)];
        bitmap.CopyPixels(pixels, checked(width * 4), 0);
        var paintedWidth = 0; var paintedHeight = 0;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (pixels[(y * width + x) * 4 + 3] != 0)
                { paintedWidth = Math.Max(paintedWidth, x + 1); paintedHeight = Math.Max(paintedHeight, y + 1); }
        // Refuse empty/implausible captures rather than hide a rendering failure.
        if (paintedWidth < width - 40 || paintedHeight < height - 80 || paintedWidth == 0 || paintedHeight == 0)
            throw new Exception($"Unexpected painted bounds: {paintedWidth}x{paintedHeight} within {width}x{height}");
        var painted = new CroppedBitmap(bitmap, new Int32Rect(0, 0, paintedWidth, paintedHeight));
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(painted));
        using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
        var buttons = Descendants(window).OfType<Button>().Where(b => b.Content is string s && (s == "בדוק עדכונים" || s == "פתח הורדה רשמית" || s == "עדכון - פתח הורדה רשמית" || s == "לא עכשיו")).Select(b => new { text = b.Content, enabled = b.IsEnabled, visible = b.IsVisible, width = b.ActualWidth }).ToArray();
        Evidence.Add(new { state = name, requestedWindowWidth = window.Width, actualWindowWidth = window.ActualWidth, minimumWindowWidth = window.MinWidth, width, height, paintedWidth, paintedHeight, buttons, rendering = "Actual WPF window visual root at 96dpi; only wholly transparent trailing nonclient strips omitted; native titlebar/taskbar not captured; synthetic update source and browser sink" });
    }
}
