using ComboBox = System.Windows.Controls.ComboBox;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StartApp.App;
internal static partial class Program
{
    private static void Other(Window window,ComboBox combo,string output,string name,Action<string,bool> check)
    {
        var original=OpenWithPicker.GetPath(combo);
        var choice=combo.Items.Cast<OpenWithPicker.Choice>().Single(x=>x.Label=="אחר...");
        var observed=false;Exception? error=null;
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(400)};
        timer.Tick+=(_,_)=>{
            timer.Stop();
            try {
                check(name+" Other preserves bound path",OpenWithPicker.GetPath(combo)==original);
                check(name+" Other restores selected path",OpenWithPicker.SelectedPath(combo)==original);
                var handle=FindWindow("#32770","פתח באמצעות");
                check(name+" native coming soon message is visible",handle!=IntPtr.Zero);
                if(handle!=IntPtr.Zero && GetWindowRect(handle,out var bounds)){
                    using var image=new System.Drawing.Bitmap(bounds.Right-bounds.Left,bounds.Bottom-bounds.Top);
                    using(var graphics=System.Drawing.Graphics.FromImage(image))graphics.CopyFromScreen(bounds.Left,bounds.Top,0,0,image.Size);
                    image.Save(Path.Combine(output,name+"-coming-soon.png"));
                }
                observed=true;
            }catch(Exception e){error=e;}
            finally{Key(0x1B);}
        };
        timer.Start();combo.SelectedItem=choice;
        if(error is not null)throw error;
        check(name+" Other message was observed",observed);
        check(name+" Other returns without path change",OpenWithPicker.GetPath(combo)==original);
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string className,string title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window,out Rect bounds);
    [StructLayout(LayoutKind.Sequential)] private struct Rect {public int Left,Top,Right,Bottom;}
}
