using StartApp.Presentation;

namespace StartApp.Presentation.Tests;

public sealed class InstalledBuildIdentityTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    [Theory]
    [InlineData("0.2.0-proto.1")]
    [InlineData("1.0.1")]
    [InlineData("1.0.0-beta.1")]
    [InlineData("0.2.0-beta.0")]
    [InlineData("0.2.0-beta.9")]
    public void Explicit_version_and_matching_commit_are_required(string version)
    {
        var identity = InstalledBuildIdentity.Parse(version + "+sha." + Sha, Sha);
        Assert.NotNull(identity); Assert.Equal(version, identity.Version); Assert.Equal(Sha, identity.Commit);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("1.0.0")]
    [InlineData("1.0.0.0")]
    [InlineData("1.0.0+other")]
    [InlineData("01.0.0+sha.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1.0.0-preview.1+sha.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Unstamped_or_unsupported_identity_stays_disabled(string? stamp) =>
        Assert.Null(InstalledBuildIdentity.Parse(stamp, Sha));
    [Fact] public void Mismatched_commit_is_not_installed_identity() =>
        Assert.Null(InstalledBuildIdentity.Parse("1.0.0+sha." + Sha, new string('b', 40)));
    [Fact] public void Missing_commit_is_not_installed_identity() =>
        Assert.Null(InstalledBuildIdentity.Parse("1.0.0+sha." + Sha, null));
}
