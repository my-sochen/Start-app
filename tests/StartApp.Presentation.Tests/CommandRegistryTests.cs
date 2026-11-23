using StartApp.Presentation;

public class CommandRegistryTests
{
    [Fact] public void Commands_keep_the_original_parameter_and_live_enablement_policy()
    {
        var registry = new CommandRegistry(); var enabled = false; object? received = null; var parameter = new object();
        var command = registry.Add(p => received = p, p => enabled && ReferenceEquals(p, parameter));
        Assert.False(command.CanExecute(parameter)); enabled = true; Assert.True(command.CanExecute(parameter)); Assert.False(command.CanExecute(new object()));
        command.Execute(parameter); Assert.Same(parameter, received);
    }
    [Fact] public void Invalidates_each_registered_command_in_registration_order_on_every_call()
    {
        var registry = new CommandRegistry(); var calls = new List<int>();
        for (var i = 0; i < 3; i++) { var index = i; var c = registry.Add(_ => { }, _ => true); c.CanExecuteChanged += (_, _) => calls.Add(index); }
        registry.RaiseAll(); registry.RaiseAll(); Assert.Equal(new[] { 0, 1, 2, 0, 1, 2 }, calls);
    }
    [Fact] public void Empty_registry_is_safe_and_newly_registered_commands_join_later_invalidations()
    {
        var registry = new CommandRegistry(); registry.RaiseAll(); var calls = 0;
        registry.Add(_ => { }, _ => true).CanExecuteChanged += (_, _) => calls++;
        registry.RaiseAll(); Assert.Equal(1, calls);
    }
    [Fact] public void Handler_exceptions_propagate_and_stop_later_commands_like_original_foreach()
    {
        var registry = new CommandRegistry(); var later = false;
        registry.Add(_ => { }, _ => true).CanExecuteChanged += (_, _) => throw new InvalidOperationException("handler failed");
        registry.Add(_ => { }, _ => true).CanExecuteChanged += (_, _) => later = true;
        Assert.Throws<InvalidOperationException>(() => registry.RaiseAll()); Assert.False(later);
    }
    [Fact] public void Registries_have_independent_commands_and_subscribers()
    {
        var a = new CommandRegistry(); var b = new CommandRegistry(); var calls = 0;
        a.Add(_ => { }, _ => true).CanExecuteChanged += (_, _) => calls++;
        b.RaiseAll(); Assert.Equal(0, calls); a.RaiseAll(); Assert.Equal(1, calls);
    }
}
