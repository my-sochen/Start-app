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
        var saved=store.Load().Workspaces.Single();store.Mutate(c=>{var file=c.GetOrAddItem(ItemType.File,"Text file",@"C:\fixture.txt");c.AddToWorkspace(saved.Id,file.Id,new LaunchConfig(OpenWith:Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\notepad.exe")));});
        vm.Reload();vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());await Idle(shell);await Task.Delay(1500);await Take(shell,output,"hebrew-real-row");
        check("Real row uses Hebrew",Descendants(shell).OfType<TextBlock>().Any(x=>x.Text=="Notepad" && AutomationProperties.GetName(x)=="Notepad"));
        var panel=new StackPanel{FlowDirection=System.Windows.FlowDirection.RightToLeft,Margin=new Thickness(24)};
        var probe=new Window{Owner=shell,Width=700,Height=320,Content=panel};probe.Show();await Idle(probe);
        foreach(var path in new string?[]{null,"","   ",@"C:\Legacy\missing.exe",@"C:\Legacy\tool-v2.1 (64)_123!.exe",@"C:\Legacy\tool.123",@"C:\Legacy\tool.!!!",Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\notepad.exe")}){
            var view=new ProgramIconView{Program=path};panel.Children.Add(view);await Idle(shell);
            var label=Descendants(view).OfType<TextBlock>().Single(x=>x.Text!="נפתח באמצעות: ");
            check("Prefix is separate "+path,view.Children.OfType<TextBlock>().Single().Text=="נפתח באמצעות: ");
            check("Blank visibility "+path,view.Visibility==(string.IsNullOrWhiteSpace(path)?Visibility.Collapsed:Visibility.Visible));
            check("Hebrew fallback "+path,AutomationProperties.GetName(label)==label.Text);
            check("Path remains exact "+path,Equals(label.ToolTip,path) && AutomationProperties.GetHelpText(label)==(path??""));
            for(var i=0;i<30 && !string.IsNullOrWhiteSpace(path) && label.Text==path;i++){await Task.Delay(100);await Idle(shell);}
            check("Settled prefix "+path,AutomationProperties.GetName(label)==label.Text && !label.Text.Contains("Opens with:"));
            if(path?.Contains("Legacy")==true)check("Missing program keeps full path",label.Text==path);
            if(path?.EndsWith("notepad.exe")==true){
                check("Resolved name is localized prefix plus short name",label.Text=="Notepad");
                var icon=Descendants(view).OfType<System.Windows.Controls.Image>().Single();
                await Idle(probe);probe.UpdateLayout();
                var iconCenter=icon.PointToScreen(new System.Windows.Point(icon.ActualWidth/2,icon.ActualHeight/2));
                var nameCenter=label.PointToScreen(new System.Windows.Point(label.ActualWidth/2,label.ActualHeight/2));
                check("Icon visually left of name",iconCenter.X<nameCenter.X);
                var iconRight=icon.PointToScreen(new System.Windows.Point(icon.ActualWidth,0)).X;
                var nameLeft=label.PointToScreen(new System.Windows.Point(0,0)).X;
                check("Icon and name bounds do not overlap",iconRight<=nameLeft);
            }
        }
        var stale=new ProgramIconView{Program=Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\notepad.exe")};panel.Children.Add(stale);stale.Program=@"C:\Late\missing.exe";await Task.Delay(1000);await Idle(shell);
        check("Late icon cannot overwrite new path",Descendants(stale).OfType<TextBlock>().Any(x=>x.Text==@"C:\Late\missing.exe"));
        await Take(probe,output,"hebrew-opener-labels");probe.Close();
    }
}
