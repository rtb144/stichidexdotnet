using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Stitchidex.Desktop.Views;

public partial class UsbExportDialog : Window
{
    public UsbExportDialog() => InitializeComponent();
    void OnClose(object? s, RoutedEventArgs e) => Close();
}
