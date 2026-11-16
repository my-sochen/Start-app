using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Text.Json;
// Synthetic UI only. No browser, credentials, prompts, sockets or external files.
static class Fixture {
 [STAThread] static int Main(string[] args){
  if(args.Length!=1)return 2;string dir=Path.GetFullPath(args[0]);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  var panel=new StackPanel();var value=new TextBox{Text="owned-value-A",Width=250};AutomationProperties.SetAutomationId(value,"owned-value");AutomationProperties.SetName(value,"Owned value");
  var toggle=new CheckBox{Content="Owned toggle",IsThreeState=true,IsChecked=false};AutomationProperties.SetAutomationId(toggle,"owned-toggle");
  var text=new TextBlock{Text="Owned static text"};AutomationProperties.SetAutomationId(text,"owned-text");
  panel.Children.Add(value);panel.Children.Add(toggle);panel.Children.Add(text);
  var main=new Window{Title="Synthetic observation fixture",Width=400,Height=220,Content=panel};AutomationProperties.SetAutomationId(main,"owned-main");
  var sentinel=new Window{Title="Synthetic sentinel",Width=160,Height=80,Content=new TextBlock{Text="Owned sentinel"}};AutomationProperties.SetAutomationId(sentinel,"owned-sentinel");
  void Ack(int step)=>File.WriteAllText(Path.Combine(dir,"ack.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,step}));
  int stage=0;var lifetime=System.Diagnostics.Stopwatch.StartNew();
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};
  timer.Tick+=(_,_)=>{try{
   if(lifetime.Elapsed.TotalSeconds>30){app.Shutdown(3);return;}
   string file=Path.Combine(dir,"command.txt");if(!File.Exists(file))return;string command=File.ReadAllText(file);File.Delete(file);
   if(command=="on"&&stage==0){value.Text="owned-value-B";toggle.IsChecked=true;stage=1;Ack(stage);}
   else if(command=="mixed"&&stage==1){toggle.IsChecked=null;stage=2;Ack(stage);}
   else if(command=="close"&&stage==2){main.Close();stage=3;Ack(stage);}
   else if(command=="stop"&&stage==3){File.WriteAllText(Path.Combine(dir,"expected-stop.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,stage,code="EXPECTED_STOP"}));app.Shutdown(0);}
   else app.Shutdown(4);
  }catch{app.Shutdown(5);}};
  main.Loaded+=(_,_)=>{sentinel.Show();main.UpdateLayout();Ack(0);timer.Start();};return app.Run(main);
 }
}
