using StartApp.Core;
namespace StartApp.Presentation;
/// <summary>Placeholder adapter: starts nothing. Replaced by a Windows adapter only after launch authorization and ownership policy are decided.</summary>
public sealed class NotImplementedLauncher : IItemLauncher
{
    public LaunchResult Launch(Item item, LaunchConfig? launch) => new(LaunchStatus.Unavailable, Detail: "launching not implemented");
}
