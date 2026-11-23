using StartApp.Presentation;

public class CaptureDuplicateSessionTests
{
    [Fact] public void New_session_is_idle_without_target_message_group_or_choices()
    {
        var s = new CaptureDuplicateSession<object, object>(); Assert.True(s.IsIdle); Assert.False(s.SinglePending);
        Assert.Null(s.SingleTarget); Assert.Equal("", s.SingleMessage); Assert.False(s.GroupPending); Assert.Empty(s.Choices);
    }
    [Fact] public void Single_drag_target_blocks_idle_until_released_and_keeps_message_independent()
    {
        var target = new object(); var s = new CaptureDuplicateSession<object, object> { SingleTarget = target, SingleMessage = "message" };
        Assert.Same(target, s.SingleTarget); Assert.True(s.SinglePending); Assert.False(s.IsIdle);
        s.SingleTarget = null; Assert.False(s.SinglePending); Assert.True(s.IsIdle); Assert.Equal("message", s.SingleMessage);
    }
    [Fact] public void Group_and_single_pending_are_independent_and_both_must_clear_before_idle()
    {
        var s = new CaptureDuplicateSession<object, object> { SingleTarget = new object(), GroupPending = true };
        s.SingleTarget = null; Assert.False(s.IsIdle); s.GroupPending = false; Assert.True(s.IsIdle);
        s.SingleTarget = new object(); Assert.False(s.IsIdle); Assert.False(s.GroupPending);
    }
    [Fact] public void Group_choices_keep_reference_order_and_do_not_implicitly_change_pending_state()
    {
        var a = new object(); var b = new object(); var s = new CaptureDuplicateSession<object, object>();
        var changes = 0; s.Choices.CollectionChanged += (_, _) => changes++;
        s.Choices.Add(a); s.Choices.Add(b); Assert.Equal(new[] { a, b }, s.Choices); Assert.Equal(2, changes); Assert.True(s.IsIdle);
        s.GroupPending = true; s.Choices.Clear(); Assert.True(s.GroupPending); Assert.False(s.IsIdle); Assert.Equal(3, changes);
    }
    [Fact] public void Sessions_do_not_share_targets_choices_messages_or_pending_flags()
    {
        var a = new CaptureDuplicateSession<object, object> { SingleTarget = new object(), SingleMessage = "warning", GroupPending = true };
        var b = new CaptureDuplicateSession<object, object>(); a.Choices.Add(new object());
        Assert.True(b.IsIdle); Assert.Empty(b.Choices); Assert.Equal("", b.SingleMessage); Assert.Null(b.SingleTarget);
    }
}
