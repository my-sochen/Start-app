using StartApp.Core;
using StartApp.Presentation;

public class CardDesignSessionTests
{
    [Fact] public void Captures_identity_and_originals_without_retargeting_when_workspace_changes()
    {
        var workspace=new Workspace { Id="one", Color="#FFFFFF", ImagePath=@"C:\one.png" };
        var s=new CardDesignSession();s.CaptureOriginal(workspace);
        workspace.Id="two";workspace.Color="#000000";workspace.ImagePath=@"C:\two.png";
        Assert.Equal("one",s.WorkspaceId);Assert.Equal("#FFFFFF",s.OriginalColor);Assert.Equal(@"C:\one.png",s.OriginalImagePath);
    }
    [Fact] public void Null_originals_become_empty_and_capture_does_not_publish_draft_or_open()
    {
        var s=new CardDesignSession { Color="draft", ImagePath="draft-image" };
        s.CaptureOriginal(new Workspace { Id="one" });
        Assert.Equal("",s.OriginalColor);Assert.Equal("",s.OriginalImagePath);
        Assert.Equal("draft",s.Color);Assert.Equal("draft-image",s.ImagePath);Assert.False(s.IsOpen);
    }
    [Fact] public void Dirty_is_scoped_to_open_session_and_uses_exact_untrimmed_values()
    {
        var s=new CardDesignSession();s.CaptureOriginal(new Workspace { Id="one", Color="#FFFFFF" });
        s.Color=s.OriginalColor;s.ImagePath=s.OriginalImagePath;s.IsOpen=true;Assert.False(s.IsDirty);
        s.Color=" #FFFFFF ";Assert.True(s.IsDirty);s.Color="#FFFFFF";Assert.False(s.IsDirty);
        s.ImagePath="image";Assert.True(s.IsDirty);s.IsOpen=false;Assert.False(s.IsDirty);
        Assert.Equal("image",s.ImagePath);
    }
    [Fact] public void Separate_sessions_do_not_share_drafts_or_captured_identity()
    {
        var a=new CardDesignSession();var b=new CardDesignSession();
        a.CaptureOriginal(new Workspace { Id="a" });b.CaptureOriginal(new Workspace { Id="b" });
        a.IsOpen=true;a.Color="#000000";Assert.Equal("",b.Color);Assert.False(b.IsDirty);Assert.Equal("b",b.WorkspaceId);
    }
}
