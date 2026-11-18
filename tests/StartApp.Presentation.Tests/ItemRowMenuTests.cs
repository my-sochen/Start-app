using StartApp.Core;
using StartApp.Presentation;

public class ItemRowMenuTests
{
    [Theory]
    [InlineData("Program", true)]
    [InlineData("Link", false)]
    [InlineData("Folder", false)]
    [InlineData("File", false)]
    [InlineData("Unknown", false)]
    public void Saved_row_program_flag_matches_its_current_type(string type, bool expected)
    {
        var row = new ItemRow("instance", "item", "name", "target", type, "group");
        Assert.Equal(expected, row.IsProgram);
        Assert.False((row with { Type = "File" }).IsProgram);
        Assert.True((row with { Type = "Program" }).IsProgram);
    }

    [Theory]
    [InlineData(ItemType.Program, true)]
    [InlineData(ItemType.Link, false)]
    [InlineData(ItemType.Folder, false)]
    [InlineData(ItemType.File, false)]
    public void Staged_row_program_flag_keeps_the_same_menu_rule(ItemType type, bool expected)
    {
        var row = new MainViewModel.StagedRow(new CapturedItem(type, "name", "target"));
        Assert.Equal(expected, row.IsProgram);
    }
}
