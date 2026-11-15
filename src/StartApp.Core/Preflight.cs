namespace StartApp.Core;

public enum PreflightState { Ready, Missing, Unsupported, Unavailable, NeedsDecision }
/// <summary>Result of checking one item before opening. Missing = the saved target was not found. Unsupported = the saved form must never be launched
/// (program alias, link scheme other than http/https, bad launch config, type mismatch). Unavailable = could not be checked (reason in Detail). NeedsDecision = the item could run code or its real target could not be verified (program/script/shortcut
/// extension, reparse point, interpreter used as a launch program, ambiguous link form): it is never opened by this slice; only a future explicit item-specific decision could allow it.</summary>
public sealed record PreflightFinding(PreflightState State, string? Detail = null);

/// <summary>Port: environment checks before opening. Must not launch, create or modify anything.</summary>
public interface IItemPreflight
{
    PreflightFinding Check(Item item, LaunchConfig? launch);
}

public static class PreflightRules
{
    /// <summary>User-approved link policy (Nov 11): only http and https are opened. Other schemes stay saved and are never opened.</summary>
    public static bool IsOpenableLink(string target) => LinkFormProblem(target) is null && IsHttpScheme(target);
    private static bool IsHttpScheme(string t) => t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Strict SYNTACTIC check of the saved link text, which is what gets launched (saved bytes are never rewritten). Accepts only
    /// http(s)://authority[/path][?query][#fragment] where the authority is plain ASCII host[:port] with no userinfo, no percent-encoding, no backslash,
    /// no whitespace or control characters, and the parsed URI agrees on scheme and a non-empty host. Anything else is "ambiguous": it is refused, not normalized.</summary>
    public static string? LinkFormProblem(string t)
    {
        if (!IsHttpScheme(t)) return "not an http or https link";
        if (t.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')) return "link contains whitespace, control characters or a backslash";
        var rest = t[(t.IndexOf("://", StringComparison.Ordinal) + 3)..];
        var end = rest.IndexOfAny(new[] { '/', '?', '#' }); var authority = end < 0 ? rest : rest[..end];
        if (authority.Length == 0) return "link has no host";
        if (authority.Contains('@')) return "link contains user information before the host";
        if (authority.Contains('%')) return "link host is percent-encoded";
        if (authority.Any(c => c > 127)) return "link host contains non-ASCII characters";
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || u.Host.Length == 0 || !(u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)) return "link could not be parsed as an absolute http(s) URI";
        return null;
    }

    /// <summary>Programs that interpret or execute their argument. Refused as an explicit OpenWith/Browser program: passing a path to them is code execution, not "opening".</summary>
    public static readonly IReadOnlySet<string> InterpreterPrograms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "msiexec.exe", "bash.exe", "wsl.exe", "python.exe", "pythonw.exe", "node.exe", "java.exe", "javaw.exe", "msbuild.exe", "installutil.exe", "regasm.exe", "forfiles.exe", "schtasks.exe", "at.exe", "explorer.exe" };

    public static bool IsInterpreter(string exePath)
    {
        var name = exePath.TrimEnd('.', ' '); var i = name.LastIndexOfAny(new[] { '\\', '/' }); return InterpreterPrograms.Contains(i >= 0 ? name[(i + 1)..] : name);
    }

    /// <summary>Extensions the Windows shell would EXECUTE or redirect rather than open as a document. A File/Folder item opened through the default handler
    /// must never be one of these (that would bypass the Program rule and the http/https link rule). Candidate fail-closed denylist, a labelled technical
    /// proposal and not a user-approved list; an allowlist would be stricter but is a product decision. Not a security proof: file associations are user-configurable.</summary>
    public static readonly IReadOnlySet<string> ShellExecutableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".scr", ".pif", ".bat", ".cmd", ".msi", ".msp", ".msc", ".cpl", ".ps1", ".psm1", ".ps1xml", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".hta", ".lnk", ".url", ".reg", ".inf", ".scf", ".jar", ".application", ".appref-ms", ".gadget", ".xbap", ".cer", ".settingcontent-ms", ".library-ms", ".search-ms", ".appx", ".msix", ".diagcab",
    };

    /// <summary>Reason a saved File/Folder path must not be given to the default shell handler, or null. Trailing dots/spaces are ignored because Windows ignores them
    /// when resolving the name; a colon after the drive letter means an alternate data stream or device syntax and is refused.</summary>
    public static string? ShellOpenBlockReason(string path)
    {
        var rest = path.Length >= 2 && path[1] == ':' ? path[2..] : path;
        if (rest.Contains(':')) return "path contains ':' (alternate data stream or device syntax)";
        var name = path.TrimEnd('.', ' ');
        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        var leaf = slash >= 0 ? name[(slash + 1)..] : name;
        var dot = leaf.LastIndexOf('.');
        if (dot >= 0 && ShellExecutableExtensions.Contains(leaf[dot..])) return $"{leaf[dot..]} files are not opened through the default handler because they can run code";
        return null;
    }

    /// <summary>Pure form check shared by every adapter. Aliases such as "notepad" are flagged, never resolved through PATH or the shell.</summary>
    public static PreflightFinding? CheckForm(Item item, LaunchConfig? launch)
    {
        if (item.Type == ItemType.Program && !Catalog.IsAbsoluteWindowsPath(item.Target))
            return new(PreflightState.Unsupported, "program must be saved as a full path; aliases are not supported");
        if (item.Type == ItemType.Link)
        {
            if (!IsHttpScheme(item.Target)) return new(PreflightState.Unsupported, "only http and https links are opened; this link stays saved but is not opened");
            if (LinkFormProblem(item.Target) is { } lp) return new(PreflightState.NeedsDecision, "ambiguous link form, not opened: " + lp);
        }
        if ((item.Type is ItemType.File or ItemType.Folder) && !Catalog.IsAbsoluteWindowsPath(item.Target))
            return new(PreflightState.Unsupported, "file and folder targets must be saved as full paths");
        var le = Catalog.ValidateLaunch(item.Type, launch);
        if (le is not null) return new(PreflightState.Unsupported, le);
        // Not exempted by an explicit OpenWith: a code-capable target stays code-capable (the handler may run it), so it needs an explicit decision either way.
        if (item.Type is ItemType.File or ItemType.Folder && ShellOpenBlockReason(item.Target) is { } why)
            return new(PreflightState.NeedsDecision, why);
        foreach (var exe in new[] { launch?.OpenWith, launch?.Browser })
            if (exe is not null && IsInterpreter(exe)) return new(PreflightState.NeedsDecision, "the launch program interprets or executes what it is given");
        return null;
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
