using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StartApp.Presentation;
namespace StartApp.App;

/// <summary>Owns the card's isolated manual-add session. The original window never navigates.</summary>
public sealed class CardAddWindow : Window
{
    private readonly MainViewModel _session;
    private readonly ManualAddForm _form = new();
    private bool _discardApproved;
    private readonly Grid _shade = new() { Visibility=Visibility.Collapsed, Background=new SolidColorBrush(Color.FromArgb(102,80,90,112)) };
    public CardAddWindow(MainViewModel session)
    {
        _session = session; DataContext = session;
        Title = "הוספת אייטם"; Width = 560; SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        AllowsTransparency = true; Background = Brushes.Transparent;
        FontFamily = new FontFamily("./Assets/Fonts/#Noto Sans Hebrew, Arial");
        var body = new StackPanel();
        var header = new DockPanel();
        var close = new Button { Content = "×", Style = (Style)FindResource("QuietTextButton"), Width=24, Height=24, FontSize=18, ToolTip="סגור" };
        close.Click += (_,_) => Close(); DockPanel.SetDock(close,Dock.Left); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text="הוספת אייטם · " + session.Selected!.Name, FlowDirection=FlowDirection.RightToLeft, TextAlignment=TextAlignment.Left, FontSize=22, FontWeight=FontWeights.SemiBold, TextWrapping=TextWrapping.Wrap });
        body.Children.Add(header); _form.Margin=new Thickness(0,20,0,0); body.Children.Add(_form);
        var picker = new ResourcePickerView { Margin=new Thickness(0,12,0,0), MinWidth=0, Padding=new Thickness(0) };
        picker.SetBinding(VisibilityProperty,new System.Windows.Data.Binding(nameof(MainViewModel.PickerOpen)){Converter=new BooleanToVisibilityConverter()});body.Children.Add(picker);
        var error = new TextBlock { Foreground=Brushes.DarkRed,TextWrapping=TextWrapping.Wrap,FlowDirection=FlowDirection.RightToLeft,Margin=new Thickness(0,10,0,0) };
        error.SetBinding(VisibilityProperty,new System.Windows.Data.Binding(nameof(MainViewModel.SaveBlocked)){Converter=new BooleanToVisibilityConverter()});
        error.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(nameof(MainViewModel.SaveBlockedMessage)));body.Children.Add(error);
        var surface = new DialogFrame { Padding=new Thickness(28),CornerRadius=new CornerRadius(6),BorderBrush=new SolidColorBrush(Color.FromRgb(201,208,220)),BorderThickness=new Thickness(1),HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Child=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=body } };
        var guard = new StackPanel {FlowDirection=FlowDirection.RightToLeft};
        var keep = new Button {Content="×",Style=(Style)FindResource("QuietTextButton"),Width=24,HorizontalAlignment=HorizontalAlignment.Left,ToolTip="חזרה לעריכה"};
        keep.Click+=(_,_)=>{_shade.Visibility=Visibility.Collapsed;_form.FocusTarget();};guard.Children.Add(keep);
        guard.Children.Add(new TextBlock {Text="יש אייטם שטרם נוסף",FontSize=20,FontWeight=FontWeights.SemiBold});
        guard.Children.Add(new TextBlock {Text="להוסיף את האייטם לפני היציאה?",Margin=new Thickness(0,12,0,20)});
        var actions = new WrapPanel {HorizontalAlignment=HorizontalAlignment.Left};
        var save = new Button {Content="שמור וצא",Style=(Style)FindResource("PrimaryActionButton")};
        save.Click+=(_,_)=>{if(session.AddItemCommand.CanExecute(null))session.AddItemCommand.Execute(null);_shade.Visibility=Visibility.Collapsed;if(session.NewTarget.Length==0&&session.NewItemName.Length==0&&session.NewDescription.Length==0){_discardApproved=true;Close();}};
        var discard = new Button {Content="צא בלי לשמור",Style=(Style)FindResource("HomeToolbarButton"),Margin=new Thickness(8,0,0,0)};
        discard.Click+=(_,_)=>{_discardApproved=true;Close();};actions.Children.Add(save);actions.Children.Add(discard);guard.Children.Add(actions);
        _shade.Children.Add(new DialogFrame {Child=guard,Padding=new Thickness(24),Margin=new Thickness(20),CornerRadius=new CornerRadius(6)});
        var root = new Grid();root.Children.Add(surface);root.Children.Add(_shade);Content=root;
        ContentRendered += (_,_) => _form.FocusTarget();
        PreviewKeyDown += (_,e)=>{if(e.Key==Key.Escape){e.Handled=true;if(_shade.Visibility==Visibility.Visible)_shade.Visibility=Visibility.Collapsed;else if(session.PickerOpen)session.ClosePickerCommand.Execute(null);else Close();}};
        Closing += ClosingWindow;
    }
    private void ClosingWindow(object? sender, CancelEventArgs e)
    {
        if (_discardApproved || (_session.NewTarget.Length==0 && _session.NewItemName.Length==0 && _session.NewDescription.Length==0)) return;
        e.Cancel=true; _shade.Visibility=Visibility.Visible;

    }
}
