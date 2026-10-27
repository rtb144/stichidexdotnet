using Avalonia.Controls;
using Avalonia.Interactivity;
namespace Stitchidex.Desktop.Views;

public partial class BulkTagDialog : Window
{
    public BulkTagDialog() => InitializeComponent();
    void OnApply(object? s, RoutedEventArgs e) => Close(true);
    void OnCancel(object? s, RoutedEventArgs e) => Close(false);
}
