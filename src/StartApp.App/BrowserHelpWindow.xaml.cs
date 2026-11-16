using System.Runtime.InteropServices;
using System.Windows;
using StartApp.Core;
namespace StartApp.App;
public partial class BrowserHelpWindow : Window
{
    public BrowserHelpWindow()
    {
        InitializeComponent();
        ChromeAddress.Text = BrowserCapturePolicy.ChromeSettingsAddress;
        EdgeAddress.Text = BrowserCapturePolicy.EdgeSettingsAddress;
    }
    private void CopyChrome_Click(object sender, RoutedEventArgs e) => Copy(BrowserCapturePolicy.ChromeSettingsAddress);
    private void CopyEdge_Click(object sender, RoutedEventArgs e) => Copy(BrowserCapturePolicy.EdgeSettingsAddress);
    private void Copy(string address)
    {
        try { Clipboard.SetText(address); CopyStatus.Text = "הכתובת הועתקה. הדביקו אותה בדפדפן המתאים."; }
        catch (ExternalException) { CopyStatus.Text = "ההעתקה לא הצליחה. אפשר לסמן ולהעתיק את הכתובת מהשדה."; }
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
