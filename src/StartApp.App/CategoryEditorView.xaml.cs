namespace StartApp.App;

/// <summary>Feature content only; overlay, visibility and focus coordination stay with the window.</summary>
public partial class CategoryEditorView : DialogFrame
{
    public CategoryEditorView() => InitializeComponent();
    private void DuplicateNotice_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(() => DuplicateAcknowledgeButton.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }
}
