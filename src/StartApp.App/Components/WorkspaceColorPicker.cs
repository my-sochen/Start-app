using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>Shared preset swatches and a full-spectrum editor. The caller owns its unsaved color.</summary>
public sealed class WorkspaceColorPicker : StackPanel
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(nameof(SelectedColor), typeof(string), typeof(WorkspaceColorPicker), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public string SelectedColor { get => (string)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(string), typeof(WorkspaceColorPicker), new PropertyMetadata("create"));
    public string Target { get => (string)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(nameof(Palette), typeof(IEnumerable<string>), typeof(WorkspaceColorPicker));
    public IEnumerable<string> Palette { get => (IEnumerable<string>)GetValue(PaletteProperty); set => SetValue(PaletteProperty, value); }

    public WorkspaceColorPicker()
    {
        FlowDirection = FlowDirection.RightToLeft;
        var swatches = new ItemsControl();
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        swatches.ItemsPanel = new ItemsPanelTemplate(panel);
        var button = new FrameworkElementFactory(typeof(Button));
        button.SetResourceReference(StyleProperty, "SwatchButton");
        button.SetValue(WidthProperty, 28d); button.SetValue(HeightProperty, 28d);
        button.SetValue(MarginProperty, new Thickness(0, 0, 8, 6));
        button.SetBinding(BackgroundProperty, new Binding(".") { Converter = new ColorBrushConverter() });
        button.SetBinding(ToolTipProperty, new Binding("."));
        button.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("."));
        button.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, _) => SetCurrentValue(SelectedColorProperty, ((Button)s).DataContext as string ?? "")));
        swatches.ItemTemplate = new DataTemplate { VisualTree = button };
        swatches.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(Palette)) { Source = this });
        swatches.SetBinding(IsEnabledProperty, new Binding("CanChooseWorkspaceColor"));
        Children.Add(swatches);
        var current = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4), HorizontalAlignment = HorizontalAlignment.Right };
        var preview = new Border { Width = 18, Height = 18, BorderThickness = new Thickness(1), BorderBrush = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 6, 0) };
        preview.SetBinding(Border.BackgroundProperty, new Binding(nameof(SelectedColor)) { Source = this, Converter = new ColorBrushConverter() });
        var value = new TextBlock { FlowDirection = FlowDirection.LeftToRight, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        value.SetBinding(TextBlock.TextProperty, new Binding(nameof(SelectedColor)) { Source = this });
        current.Children.Add(preview); current.Children.Add(value); Children.Add(current);
        var advanced = new Button { Content = "בחירה מתקדמת", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0) };
        advanced.SetResourceReference(StyleProperty, "HomeToolbarButton");
        System.Windows.Automation.AutomationProperties.SetName(advanced, "Advanced color selection");
        advanced.Click += (_, _) =>
        {
            if (DataContext is not MainViewModel vm || vm.ReadOnly) return;
            var editor = new AdvancedColorWindow(SelectedColor) { Owner = Window.GetWindow(this) };
            if (editor.ShowDialog() == true)
                vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice(Target, editor.ChosenColor));
        };
        advanced.SetBinding(IsEnabledProperty, new Binding("CanChooseWorkspaceColor"));
        Children.Add(advanced);
    }
}
