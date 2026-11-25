using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace StartApp.App;

/// <summary>Shared category selection plus the existing manager, used by workspace drafts and capture.</summary>
public sealed class CategoryPicker : StackPanel
{
    public static readonly DependencyProperty SelectedCategoryProperty = DependencyProperty.Register(
        nameof(SelectedCategory), typeof(string), typeof(CategoryPicker), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public string SelectedCategory { get => (string)GetValue(SelectedCategoryProperty); set => SetValue(SelectedCategoryProperty, value); }
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(nameof(Target), typeof(string), typeof(CategoryPicker), new PropertyMetadata("create"));
    public string Target { get => (string)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }

    public CategoryPicker()
    {
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        select.SetResourceReference(StyleProperty, "DetailsCombo");
        select.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("CategoryChoices"));
        select.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(SelectedCategory)) { Source = this, Mode = BindingMode.TwoWay });
        System.Windows.Automation.AutomationProperties.SetName(select, "Workspace category");
        Children.Add(select);
        var manage = new Button { Content = "ניהול קטגוריות", FontSize = 12, Padding = new Thickness(6, 3, 6, 3), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        manage.SetResourceReference(StyleProperty, "HomeToolbarButton");
        manage.SetBinding(Button.CommandProperty, new Binding("ManageCategoriesCommand"));
        manage.SetBinding(Button.CommandParameterProperty, new Binding(nameof(Target)) { Source = this });
        System.Windows.Automation.AutomationProperties.SetName(manage, "Manage workspace categories");
        Children.Add(manage);
    }
}
