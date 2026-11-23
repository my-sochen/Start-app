namespace StartApp.Presentation;

/// <summary>Owns the host's command collection and invalidates it in registration order.
/// It does not choose command policy or catch callback exceptions.</summary>
public sealed class CommandRegistry
{
    private readonly List<RelayCommand> _commands = new();

    public RelayCommand Add(Action<object?> execute, Func<object?, bool> canExecute)
    {
        var command = new RelayCommand(execute, canExecute);
        _commands.Add(command);
        return command;
    }

    public void RaiseAll()
    {
        foreach (var command in _commands) command.Raise();
    }
}
