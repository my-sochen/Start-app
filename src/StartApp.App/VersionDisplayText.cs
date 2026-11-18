using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
namespace StartApp.App;
public sealed class VersionDisplayText : TextBlock
{
    private static readonly Regex Version = new(@"(?<![A-Za-z0-9.\-])(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8})\.(?:0|[1-9][0-9]{0,8})(?:-(?:proto|beta)\.(?:0|[1-9][0-9]{0,8}))?(?![A-Za-z0-9\-])",RegexOptions.CultureInvariant);
    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(nameof(DisplayText),typeof(string),typeof(VersionDisplayText),new PropertyMetadata("",Changed));
    public string DisplayText { get=>(string)GetValue(DisplayTextProperty);set=>SetValue(DisplayTextProperty,value); }
    private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
    {
        var view=(VersionDisplayText)target;var text=args.NewValue as string??"";view.Inlines.Clear();int start=0;
        foreach(Match match in Version.Matches(text))
        {
            if(match.Index>start)view.Inlines.Add(new Run(text[start..match.Index]));
            view.Inlines.Add(new Run(match.Value){FlowDirection=FlowDirection.LeftToRight});start=match.Index+match.Length;
        }
        if(start<text.Length)view.Inlines.Add(new Run(text[start..]));
    }
}
