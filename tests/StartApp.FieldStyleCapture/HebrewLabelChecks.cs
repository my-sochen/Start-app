using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using StartApp.App;
using StartApp.Presentation;
using StartApp.Storage.Json;
using StartApp.Core;
internal static partial class Program
{
    private static async Task HebrewLabels(Window shell,JsonStore store,MainViewModel vm,string output,Action<string,bool> check)
    {
        var saved=store.Load().Workspaces.Single();store.Mutate(c=>c.SetLaunch(saved.Id,saved.Items.Single().Id!,new LaunchConfig(Browser:Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\notepad.exe"))));
        vm.Reload();vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Idle(shell);await Task.Delay(1500);await Take(shell,output,"hebrew-real-row");
        check("Real row uses Hebrew",Descendants(shell).OfType<TextBlock>().Any(x=>x.Text=="נפתח באמצעות: Notepad"));
        var panel=new StackPanel{FlowDirection=System.Windows.FlowDirection.RightToLeft,Margin=new Thickness(24)};
        shell.Content=panel;
        foreach(var path in new string?[]{null,"","   ",@"C:\Legacy\missing.exe",Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\notepad.exe")}){
            var view=new ProgramIconView{Program=path};panel.Children.Add(view);await Idle(shell);
            var label=view.Children.OfType<TextBlock>().Single();
            check("Blank visibility "+path,view.Visibility==(string.IsNullOrWhiteSpace(path)?Visibility.Collapsed:Visibility.Visible));
            check("Hebrew fallback "+path,label.Text.StartsWith("נפתח באמצעות: "));
            check("Path remains exact "+path,Equals(label.ToolTip,path) && AutomationProperties.GetHelpText(label)==(path??""));
            for(var i=0;i<30 && !string.IsNullOrWhiteSpace(path) && label.Text=="נפתח באמצעות: "+path;i++){await Task.Delay(100);await Idle(shell);}
            check("Settled prefix "+path,label.Text.StartsWith("נפתח באמצעות: ") && !label.Text.Contains("Opens with:"));
            if(path?.Contains("missing.exe")==true)check("Missing program keeps full path",label.Text=="נפתח באמצעות: "+path);
            if(path?.EndsWith("notepad.exe")==true)check("Resolved name is localized prefix plus short name",label.Text=="נפתח באמצעות: Notepad");
        }
        var stale=new ProgramIconView{Program=Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\notepad.exe")};panel.Children.Add(stale);stale.Program=@"C:\Late\missing.exe";await Task.Delay(1000);await Idle(shell);
        check("Late icon cannot overwrite new path",stale.Children.OfType<TextBlock>().Single().Text==@"נפתח באמצעות: C:\Late\missing.exe");
        await Take(shell,output,"hebrew-opener-labels");
    }
}
