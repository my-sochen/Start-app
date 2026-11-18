using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StartApp.Core;

namespace StartApp.App;

/// <summary>Target icon only. Never the OpenWith icon, detection evidence or a safety badge.</summary>
public sealed class TargetIcon : Image
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(string), typeof(TargetIcon), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty ItemTypeProperty = DependencyProperty.Register(nameof(ItemType), typeof(string), typeof(TargetIcon), new PropertyMetadata("File", Changed));
    public string Target { get => (string)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public string ItemType { get => (string)GetValue(ItemTypeProperty); set => SetValue(ItemTypeProperty, value); }
    private int _request;
    public TargetIcon() { Width = Height = 18; Stretch = Stretch.Uniform; Loaded += (_, _) => Refresh(); Unloaded += (_, _) => _request++; IsHitTestVisible = false; }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TargetIcon)d).Refresh();
    private async void Refresh()
    {
        var generation = ++_request; Source = TargetIconLoader.Fallback(ItemType);
        if (!IsLoaded || string.IsNullOrWhiteSpace(Target)) return;
        var icon = await TargetIconLoader.Get(Target, ItemType);
        if (generation == _request && IsLoaded && icon is not null) Source = icon;
    }
}

internal static class TargetIconLoader
{
    private sealed record Request(string Key, string Target, string Type, TaskCompletionSource<ImageSource?> Completion);
    // At most two native calls can be stuck. The queue and all per-key tasks are bounded; a timeout never spawns a replacement worker.
    private static readonly Channel<Request> Queue = Channel.CreateBounded<Request>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.Wait });
    private static readonly Dictionary<string, ImageSource?> Cache = new();
    private static readonly Dictionary<string, Task<ImageSource?>> Pending = new();
    private static readonly object Gate = new();
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, Credentials = null, MaxConnectionsPerServer = 2 }) { Timeout = TimeSpan.FromSeconds(3) };
    static TargetIconLoader() { for (var i = 0; i < 2; i++) _ = Task.Run(Worker); }
    public static async Task<ImageSource?> Get(string target, string type)
    {
        if (type == "Folder") return null;
        var safeTarget = type == "Link" ? ItemIconPolicy.WebIconAddress(target) : target;
        if (safeTarget is null) return null;
        var key = type + "\n" + safeTarget;
        Task<ImageSource?> task;
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var icon)) return icon;
            if (!Pending.TryGetValue(key, out task!))
            {
                var completion = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                task = completion.Task;
                if (!Queue.Writer.TryWrite(new(key, safeTarget, type, completion))) return null;
                Pending[key] = task;
            }
        }
        // Only the view's wait expires. A blocked native extraction retains its worker slot until it actually returns.
        return await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))) == task ? await task : null;
    }
    private static async Task Worker()
    {
        await foreach (var request in Queue.Reader.ReadAllAsync())
        {
            ImageSource? result = null;
            try { result = request.Type == "Link" ? await WebIcon(request.Target) : LocalIcon(request.Target); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.ExternalException or HttpRequestException or OperationCanceledException or InvalidOperationException or System.Security.SecurityException or FormatException) { }
            finally
            {
                lock (Gate)
                {
                    if (Cache.Count >= 256) Cache.Clear();
                    Cache[request.Key] = result; Pending.Remove(request.Key);
                }
                request.Completion.TrySetResult(result);
            }
        }
    }
    private static ImageSource? LocalIcon(string target)
    {
        if (!Catalog.IsAbsoluteWindowsPath(target) || !File.Exists(target)) return null;
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(target);
        if (icon is null) return null;
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(20, 20)); image.Freeze(); return image;
    }
    private static async Task<ImageSource?> WebIcon(string address)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 262144) return null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var bytes = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, timeout.Token); if (count == 0) break;
            if (bytes.Length + count > 262144) return null;
            bytes.Write(buffer, 0, count);
        }
        if (bytes.Length == 0) return null;
        bytes.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 20; image.StreamSource = bytes; image.EndInit(); image.Freeze(); return image;
    }
    public static ImageSource Fallback(string type)
    {
        var geometry = type switch
        {
            "Link" => "M10,1 A9,9 0 1 1 9.99,1 M1,10 L19,10 M10,1 C4,5 4,15 10,19 M10,1 C16,5 16,15 10,19",
            "Folder" => "M1,5 L8,5 10,7 19,7 19,18 1,18 Z",
            "Program" => "M2,2 L18,2 18,18 2,18 Z M2,6 L18,6 M5,4 L6,4 M8,4 L9,4",
            _ => "M4,1 L12,1 17,6 17,19 4,19 Z M12,1 L12,6 17,6 M7,10 L14,10 M7,14 L14,14"
        };
        var drawing = new GeometryDrawing(Brushes.Transparent, new Pen(Brushes.SlateGray, 1.25), Geometry.Parse(geometry));
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
