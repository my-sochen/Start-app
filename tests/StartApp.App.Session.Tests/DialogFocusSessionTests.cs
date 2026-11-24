using StartApp.App;

public class DialogFocusSessionTests
{
    [Theory] [InlineData(true)] [InlineData(false)]
    public void Opening_captures_focus_and_input_mode_once_in_original_order(bool keyboard)
    {
        var calls = new List<string>(); var target = new object();
        var session = new DialogFocusSession<object>(
            () => { calls.Add("capture"); return target; },
            () => { calls.Add("input"); return keyboard; },
            entry => { Assert.Equal(keyboard, entry); calls.Add("focus"); },
            (_, _) => throw new Exception("must not restore while open"));
        session.Update(false); session.Update(true); session.Update(true);
        Assert.Equal(new[] { "capture", "input", "focus" }, calls);
    }

    [Fact] public void Close_restores_captured_target_and_mode_not_later_values_only_once()
    {
        var original = new object(); var current = original; var keyboard = true; var calls = 0;
        var session = new DialogFocusSession<object>(() => current, () => keyboard, _ => { }, (target, entry) =>
        { Assert.Same(original, target); Assert.True(entry); calls++; });
        session.Update(true); current = new object(); keyboard = false;
        session.Update(true); session.Update(false); session.Update(false);
        Assert.Equal(1, calls);
    }

    [Fact] public void Reopening_captures_new_target_and_input_mode()
    {
        var first = new object(); var second = new object(); var current = first; var keyboard = false;
        var restored = new List<(object, bool)>();
        var session = new DialogFocusSession<object>(() => current, () => keyboard, _ => { }, (target, entry) => restored.Add((target, entry)));
        session.Update(true); session.Update(false); current = second; keyboard = true;
        session.Update(true); session.Update(false);
        Assert.Equal(new[] { (first, false), (second, true) }, restored);
    }

    [Fact] public void Missing_return_target_still_focuses_dialog_and_does_not_restore()
    {
        var focused = 0;
        var session = new DialogFocusSession<object>(() => null, () => false, _ => focused++, (_, _) => throw new Exception("no target"));
        session.Update(true); session.Update(false); Assert.Equal(1, focused);
    }

    [Fact] public void Different_dialog_sessions_keep_their_own_return_target()
    {
        var a = new object(); var b = new object(); var returned = new List<object>();
        var first = new DialogFocusSession<object>(() => a, () => false, _ => { }, (t, _) => returned.Add(t));
        var second = new DialogFocusSession<object>(() => b, () => true, _ => { }, (t, _) => returned.Add(t));
        first.Update(true); second.Update(true); second.Update(false); first.Update(false);
        Assert.Equal(new[] { b, a }, returned);
    }
}
