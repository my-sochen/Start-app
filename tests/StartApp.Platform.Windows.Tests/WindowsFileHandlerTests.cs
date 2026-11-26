using Xunit;
namespace StartApp.Platform.Windows.Tests;
public class WindowsFileHandlerTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Editor\\editor.exe\" \"%1\"", true)]
    [InlineData("\"C:\\Program Files\\Editor\\editor.exe\" %L", true)]
    [InlineData("\"C:\\Program Files\\Editor\\editor.exe\" --unsafe \"%1\"", false)]
    [InlineData("\"C:\\Program Files\\Editor\\editor.exe\" \"%1\" --tail", false)]
    [InlineData("\"C:\\Other\\editor.exe\" \"%1\"", false)]
    [InlineData("C:\\Program Files\\Editor\\editor.exe %1", false)]
    [InlineData("cmd.exe /c start %1", false)]
    [InlineData("", false)]
    public void Only_exact_direct_single_file_commands_are_supported(string command, bool supported)
        => Assert.Equal(supported, WindowsFileHandlers.DirectFileCommand(command, @"C:\Program Files\Editor\editor.exe"));
}
