namespace StartApp.Core;

public enum PreflightState { Ready, Missing, Unsupported, Unavailable }
/// <summary>Result of checking one item before opening. Missing = the saved target was not found. Unsupported = the saved form must never be launched
/// (program alias, link scheme other than http/https, bad launch config, type mismatch). Unavailable = could not be checked (reason in Detail).</summary>
public sealed record PreflightFinding(PreflightState State, string? Detail = null);

/// <summary>Port: environment checks before opening. Must not launch, create or modify anything.</summary>
public interface IItemPreflight
{
    PreflightFinding Check(Item item, LaunchConfig? launch);
}

public static class PreflightRules
{
    /// <summary>User-approved link policy (Nov 11): only http and https are opened. Other schemes stay saved and are never opened.</summary>
    public static bool IsOpenableLink(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && u.Host.Length > 0;

    /// <summary>Pure form check shared by every adapter. Aliases such as "notepad" are flagged, never resolved through PATH or the shell.</summary>
    public static PreflightFinding? CheckForm(Item item, LaunchConfig? launch)
    {
        if (item.Type == ItemType.Program && !Catalog.IsAbsoluteWindowsPath(item.Target))
            return new(PreflightState.Unsupported, "program must be saved as a full path; aliases are not supported");
        if (item.Type == ItemType.Link && !IsOpenableLink(item.Target))
            return new(PreflightState.Unsupported, "only http and https links are opened; this link stays saved but is not opened");
        if ((item.Type is ItemType.File or ItemType.Folder) && !Catalog.IsAbsoluteWindowsPath(item.Target))
            return new(PreflightState.Unsupported, "file and folder targets must be saved as full paths");
        var le = Catalog.ValidateLaunch(item.Type, launch);
        return le is null ? null : new(PreflightState.Unsupported, le);
    }
}

/// <summary>Whether the saved target is already open. Unknown is the honest default: no reliable detection exists yet, and a running program says nothing
/// about which document it has open. Open (confirmed) may only be returned on reliable exact-target evidence.</summary>
public enum OpenState { NotOpen, Open, Unknown }

/// <summary>Port: already-open detection. Must not launch, close or modify anything. Until an adapter exists every item is Unknown.</summary>
public interface IOpenStateDetector
{
    OpenState Detect(Item item, LaunchConfig? launch);
}

/// <summary>Default: no detection, so nothing is ever claimed to be a duplicate and nothing is ever claimed to be safe.</summary>
public sealed class NoOpenStateDetection : IOpenStateDetector
{
    public OpenState Detect(Item item, LaunchConfig? launch) => OpenState.Unknown;
}
