using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StartApp.App;
using Application=System.Windows.Application;
internal static class Program
{
    [STAThread]static int Main(string[] args)
    {
        if(args.Length!=1||Directory.Exists(args[0]))return 2;
        Directory.CreateDirectory(args[0]);RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
        var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/StartApp.App;component/SharedResources.xaml")});
        var settings=new SettingsView();var global=new GlobalEnvView();
        var panel=new StackPanel{Margin=new Thickness(16)};
        foreach(var enabled in new[]{true,false})
        {
            var wrap=new WrapPanel{Width=820,Margin=new Thickness(0,0,0,16)};panel.Children.Add(wrap);
            foreach(var name in new[]{"TitleBarTab","HomeToolbarButton","PrimaryActionButton","QuietTextButton","SettingsPrimaryButton","SettingsSecondaryButton","GlobalSecondaryButton","AddLinkButton","CompactActionButton","CompactIconButton","DialogPrimaryButton","DialogSecondaryButton"})
            {
                var b=new Button{Content="פעולה Test",IsEnabled=enabled,Margin=new Thickness(4)};
                object? style=app.TryFindResource(name);
                if(style is null)
                {
                    style=name switch{"SettingsPrimaryButton"=>settings.Resources["PrimaryButton"],"SettingsSecondaryButton"=>settings.Resources["SecondaryButton"],"GlobalSecondaryButton"=>global.Resources["SecondaryButton"],"AddLinkButton"=>global.Resources["AddLink"],_=>null};
                    if(name=="CompactActionButton")b.Padding=new Thickness(12,2,12,2);
                    if(name=="CompactIconButton")b.Padding=new Thickness(6,0,6,0);
                    if(name=="DialogPrimaryButton"){style=app.FindResource("PrimaryActionButton");b.Height=38;}
                    if(name=="DialogSecondaryButton"){style=app.FindResource("HomeToolbarButton");b.Height=38;b.Padding=new Thickness(16,7,16,7);}
                }
                if(style is Style s)b.Style=s;wrap.Children.Add(b);
            }
        }
        var w=new Window{Content=panel,Width=880,Height=310,Left=12,Top=12,Background=Brushes.White,FontSize=15,FontFamily=new FontFamily("./Assets/Fonts/#Noto Sans Hebrew, Arial"),WindowStyle=WindowStyle.None};var code=0;
        w.ContentRendered+=async(_,_)=>
        {
            try
            {
                Keyboard.ClearFocus();w.Focus();await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(200);w.UpdateLayout();
                var image=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var f=File.Create(Path.Combine(args[0],"button-variants.png"));png.Save(f);
            }
            catch(Exception e){code=1;File.WriteAllText(Path.Combine(args[0],"error.txt"),e.ToString());}
            finally{w.Close();app.Shutdown(code);}
        };app.Run(w);return code;
    }
}
