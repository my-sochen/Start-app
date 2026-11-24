namespace StartApp.Core;

public enum RevealStatus { Revealed, Missing, Invalid, Unavailable }
public sealed record RevealResult(RevealStatus Status, string? Detail = null);

/// <summary>Shows a file/program in its containing folder, without opening or executing it.</summary>
public interface IFileRevealer
{
    Task<RevealResult> RevealAsync(string path);
}
