using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StartApp.App;
using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;

internal static partial class Program
{
    private sealed class IconServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        public readonly string Origin;
        public int Requests;
        public bool OnlyFavicon = true, NoPrivateHeaders = true;
        public IconServer(int status, byte[] bytes, string? extra = null, bool length = true)
        {
            _listener.Start(); Origin = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _worker = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        await using var stream = client.GetStream();
                        var header = new List<byte>(); var one = new byte[1];
                        while (header.Count < 8192 && await stream.ReadAsync(one, _stop.Token) > 0)
                        {
                            header.Add(one[0]);
                            if (header.Count >= 4 && Encoding.ASCII.GetString(header.TakeLast(4).ToArray()) == "\r\n\r\n") break;
                        }
                        var text = Encoding.ASCII.GetString(header.ToArray());
                        Interlocked.Increment(ref Requests);
                        OnlyFavicon &= text.Split('\r')[0] == "GET /favicon.ico HTTP/1.1";
                        NoPrivateHeaders &= !text.Contains("Referer:", StringComparison.OrdinalIgnoreCase) && !text.Contains("Cookie:", StringComparison.OrdinalIgnoreCase) && !text.Contains("Authorization:", StringComparison.OrdinalIgnoreCase);
                        var h = $"HTTP/1.1 {status} Test\r\nConnection: close\r\nContent-Type: image/png\r\n" + (length ? $"Content-Length: {bytes.Length}\r\n" : "") + extra + "\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(h), _stop.Token);
                        try { await stream.WriteAsync(bytes, _stop.Token); } catch (IOException) { }
                    }
                }
                catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException or IOException) { }
            });
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); _worker.GetAwaiter().GetResult(); _stop.Dispose(); }
    }
    private sealed class LongFailure : IItemPreflight
    {
        public PreflightFinding Check(Item item, LaunchConfig? launch) => new(PreflightState.Missing, "C:\\controlled-test\\" + new string('x', 220) + "\\מסמך.txt\n" + string.Concat(Enumerable.Repeat("פרטים ארוכים לבדיקה בלבד. ", 16)));
    }
    private static int Readiness(string directory)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        var output = Path.GetFullPath(directory);
        if (Directory.Exists(output) || File.Exists(output)) return 3;
        Directory.CreateDirectory(output);
        var app = new Application();
        using var store = new JsonStore(Path.Combine(output, "isolated-store", "store.json"));
        store.Mutate(c =>
        {
            var ws = c.CreateWorkspace("בדיקת פרטים ארוכים");
            c.AddToWorkspace(ws.Id, c.GetOrAddItem(ItemType.File, "פתוח - " + string.Concat(Enumerable.Repeat("שם מסמך ארוך בעברית ", 7)), "C:\\controlled-test\\missing.txt").Id);
        });
        var vm = new MainViewModel(store, new Launcher(), new LongFailure());
        var window = new MainWindow { DataContext = vm, Width = 720, Height = 400 };
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                // Real HTTP loader and image decoder, local controlled origins only. Never external private data.
                var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 140, 40, 255, 0, 140, 40, 255, 0, 140, 40, 255, 0, 140, 40, 255 }, 8);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var data = new MemoryStream(); encoder.Save(data);
                using var valid = new IconServer(200, data.ToArray());
                using var sink = new IconServer(200, data.ToArray());
                using var redirect = new IconServer(302, Array.Empty<byte>(), $"Location: {sink.Origin}/redirected\r\n");
                // Establish that these exact oversized bytes decode with the product's decode settings.
                // Otherwise null could merely mean invalid image data, not an enforced byte limit.
                var decodableOversize = new byte[262145]; data.ToArray().CopyTo(decodableOversize, 0);
                using (var proof = new MemoryStream(decodableOversize))
                {
                    var decoded = new BitmapImage(); decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad;
                    decoded.DecodePixelWidth = 20; decoded.StreamSource = proof; decoded.EndInit(); decoded.Freeze();
                    Check("exact oversized fixture independently decodes with product settings", decoded.PixelWidth > 0 && decodableOversize.Length > 262144);
                }
                using var headerLarge = new IconServer(200, decodableOversize);
                using var streamLarge = new IconServer(200, decodableOversize, length: false);
                var icon = await TargetIconLoader.Get(valid.Origin + "/private/document?token=synthetic#fragment", "Link");
                Check("real local HTTP favicon decodes", icon is BitmapSource);
                Check("only origin favicon requested, no saved path/query or private headers", valid.Requests == 1 && valid.OnlyFavicon && valid.NoPrivateHeaders);
                Check("redirect icon falls back", await TargetIconLoader.Get(redirect.Origin + "/private?token=synthetic", "Link") is null);
                Check("cross-origin redirect destination never contacted", sink.Requests == 0 && redirect.Requests == 1);
                Check("oversize declared length rejected", await TargetIconLoader.Get(headerLarge.Origin + "/private", "Link") is null && headerLarge.Requests == 1);
                Check("oversize body without content length rejected", await TargetIconLoader.Get(streamLarge.Origin + "/private", "Link") is null && streamLarge.Requests == 1);
                var icons = new Window { Width = 720, Height = 400, Title = "Controlled icon evidence" };
                icons.Content = new StackPanel { Children = { new TextBlock { Text = "Controlled same-origin favicon and unavailable fallback" }, new Image { Source = icon, Width = 40, Height = 40 }, new Image { Source = TargetIconLoader.Fallback("Link"), Width = 40, Height = 40 } } };
                icons.Show(); await Idle(icons); Capture(icons, output, "controlled-http-icon-and-fallback"); icons.Close();
                vm.OpenWorkspaceCommand.Execute(null); vm.ConfirmSelectionCommand.Execute(null); await vm.OpenTask; await Idle(window);
                vm.ToastDetailsCommand.Execute(null); await Idle(window);
                foreach (var expander in Descendants(window).OfType<Expander>().Where(x => x.IsVisible)) expander.IsExpanded = true;
                await Idle(window); Capture(window, output, "long-rtl-expanded-720");
                window.Width = 900; window.Height = 560; await Idle(window); Capture(window, output, "long-rtl-expanded-900");
                Check("long results remain open", vm.LastResultsOpen && vm.LastOpeningResults.Count == 1);
                var duplicate = new MainViewModel(store, new Launcher(), detector: new Detector());
                window.DataContext = duplicate; duplicate.OpenDetailsCommand.Execute(duplicate.Workspaces.Single());
                duplicate.OpenWorkspaceCommand.Execute(null); duplicate.ConfirmSelectionCommand.Execute(null); await duplicate.OpenTask; await Idle(window);
                Check("synthetic long duplicate visible for spacing review", duplicate.OpenDecisionPending);
                Capture(window, output, "long-rtl-duplicate-900");
                window.Width = 720; window.Height = 400; await Idle(window); Capture(window, output, "long-rtl-duplicate-720");
                app.Shutdown(0);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(output, "error.txt"), e.ToString()); app.Shutdown(1); }
            finally { File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(Checks, new JsonSerializerOptions { WriteIndented = true })); }
        };
        return app.Run(window);
    }
}
