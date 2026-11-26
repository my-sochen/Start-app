namespace StartApp.Core;
public interface ISignInReadiness
{
    Task<bool> WaitAsync(CancellationToken cancellationToken);
}
