using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Stitchidex.Desktop.ViewModels;
namespace Stitchidex.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is MainWindowViewModel vm && vm.Patterns.Count > 0 && vm.SelectedPattern == null)
            {
                vm.SelectedPattern = vm.Patterns[0];
                GridList.SelectedItem = vm.SelectedPattern;
            }
        });
    }
    MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    async void OnAddFolder(object? s, RoutedEventArgs e)
    {
        var r = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add embroidery folder", AllowMultiple = false });
        if (r.Count > 0 && r[0].TryGetLocalPath() is { } p) await Vm.AddFolderAsync(p);
    }

    void SyncSelection(System.Collections.IList items)
    {
        Vm.SelectedPatterns.Clear();
        foreach (var i in items.OfType<PatternItemViewModel>()) Vm.SelectedPatterns.Add(i);
    }
    void OnGridSelectionChanged(object? s, SelectionChangedEventArgs e) => SyncSelection(GridList.SelectedItems!);
    void OnTableSelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (!TableGrid.IsVisible) return;
        if (TableGrid.SelectedItem is PatternItemViewModel p) Vm.SelectedPattern = p;
        SyncSelection(TableGrid.SelectedItems);
    }

    async void OnBulkTag(object? s, RoutedEventArgs e)
    {
        var sel = Vm.EffectiveSelection();
        if (sel.Count == 0) { Vm.StatusMessage = "Select one or more patterns to tag."; return; }
        var vm = new BulkTagDialogViewModel(sel.Count);
        if (await new BulkTagDialog { DataContext = vm }.ShowDialog<bool>(this))
            Vm.ApplyBulkTags(BulkTagDialogViewModel.Split(vm.AddText), BulkTagDialogViewModel.Split(vm.RemoveText));
    }

    async void OnSendToUsb(object? s, RoutedEventArgs e)
    {
        var sel = Vm.EffectiveSelection();
        if (sel.Count == 0) { Vm.StatusMessage = "Select patterns to send to USB."; return; }
        await new UsbExportDialog { DataContext = new UsbExportDialogViewModel(Vm.Usb, sel) }.ShowDialog(this);
    }

    void OnWindowKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (Vm.IsScanning) Vm.CancelScanCommand.Execute(null);
            else if (!string.IsNullOrEmpty(Vm.SearchText)) Vm.SearchText = "";
            else if (GridList.SelectedItems?.Count > 1 && Vm.SelectedPattern is { } cur)
            {
                GridList.SelectedItems.Clear();
                GridList.SelectedItem = cur;
            }
        }
        else if (e.Key == Key.F2)
        {
            QuickTagBox?.Focus();
            e.Handled = true;
        }
    }
}
