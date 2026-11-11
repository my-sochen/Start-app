namespace StartApp.Core;

public sealed class StoreTooNewException(int found, int supported) : Exception($"Store schema {found} is newer than supported {supported}; file left untouched.");

public sealed class StoreCorruptException(string reason, string? diagnosticCopy) : Exception("Store file is invalid and was left in place: " + reason) { public string? DiagnosticCopy { get; } = diagnosticCopy; }

public sealed class StoreLockedException(Exception? cause = null) : Exception("Could not take the single-writer lock (another instance, or the lock file is not accessible).", cause);
