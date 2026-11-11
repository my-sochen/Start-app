namespace StartApp.Core;

/// <summary>Persistence port owned by Core. Implementations: fail closed on invalid/newer data, single writer, isolated mutation.</summary>
public interface IStoreRepository : IDisposable
{
    Store Load();
    /// <summary>Applies the change to an isolated copy, validates, persists, and returns the saved state. Nothing partial leaks on failure.</summary>
    Store Mutate(Action<Catalog> change);
}
