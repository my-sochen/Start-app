namespace StartApp.Core;
public sealed record SignInSchedule(Task LocalReady, Task<bool> SitesReady);
public interface ISignInReadiness
{
    SignInSchedule Start(CancellationToken cancellationToken);
}
