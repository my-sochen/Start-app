using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace StartApp.App;

/// <summary>Shared dialog surface without a template, presenter or extra visual/layout layer.
/// Content, actions, overlay shade, focus and close rules remain at each call site.
/// Dialog-specific dimensions, padding, border, corner and alignment can override these defaults.</summary>
public class DialogFrame : Border
{
    public DialogFrame()
    {
        Background = Brushes.White;
        Padding = new Thickness(16);
        CornerRadius = new CornerRadius(4);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
    }
}
