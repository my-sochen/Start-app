namespace StartApp.Core;

public enum LaunchStatus { Started, Failed, Unavailable }
/// <summary>Whether the adapter could attribute a window/document to OUR launch. PID or a new HWND alone is not proof (instance redirection, HWND reuse).</summary>
public enum Ownership { Unknown, Confirmed }
public sealed record LaunchResult(LaunchStatus Status, Ownership Ownership = Ownership.Unknown, string? OpaqueHandle = null, string? Detail = null);

public enum CloseOutcome { Closed, Refused, NotOwned, Unsupported }

/// <summary>Port only; no implementation yet and no Windows behavior is verified. Launches an item with an explicit, validated executable (never a shell command line).</summary>
public interface IItemLauncher
{
    LaunchResult Launch(Item item, LaunchConfig? launch);
}

/// <summary>Adapter-side graceful close of something it launched. Do not call directly: use <see cref="OwnedClose.Try"/>, which enforces the
/// ownership rule before the adapter is reached. No force-kill exists in this contract.</summary>
public interface IItemCloser
{
    CloseOutcome TryCloseGracefully(string opaqueHandle);
}

/// <summary>The single entry point for closing. Ownership Unknown (or no handle) never reaches the adapter.</summary>
public static class OwnedClose
{
    public static CloseOutcome Try(IItemCloser closer, LaunchResult launch)
        => launch.Status == LaunchStatus.Started && launch.Ownership == Ownership.Confirmed && !string.IsNullOrEmpty(launch.OpaqueHandle)
            ? closer.TryCloseGracefully(launch.OpaqueHandle)
            : CloseOutcome.NotOwned;
}
