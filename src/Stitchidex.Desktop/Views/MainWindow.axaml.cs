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
        if (r.Count == 0 || r[0].TryGetLocalPath() is not { } p) return;
        if (!Stitchidex.Core.Services.UsbDriveService.InspectPathVolume(p).IsRemovable) { await Vm.AddFolderAsync(p); return; }
        switch (await ShowRemovableDriveImportChoiceAsync())
        {
            case 1: await Vm.ImportUsbFolderToComputerAsync(p); break;
            case 2: await Vm.AddFolderAsync(p); break;
        }
    }

    /// <summary>1 = copy to computer, 2 = keep on USB (catalog + offline cache), 0 = cancel.</summary>
    async Task<int> ShowRemovableDriveImportChoiceAsync()
    {
        int result = 0;
        var dlg = new Window { Title = "Patterns on a USB stick", Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        Button B(string text, int v, bool primary = false)
        {
            var b = new Button { Content = text, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Padding = new Avalonia.Thickness(12, 10), FontSize = 15 };
            if (primary) b.Classes.Add("accent");
            b.Click += (_, _) => { result = v; dlg.Close(); };
            return b;
        }
        dlg.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "This folder is on a USB stick.", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = "USB sticks can get lost or wear out. Would you like to keep a safe copy of these patterns on this computer?", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 15 },
                B("📥 Copy to My Computer (Recommended Backup)", 1, true),
                B("🔌 Keep on USB Stick (Catalog + Offline Cache)", 2),
                B("Cancel", 0),
            }
        };
        await dlg.ShowDialog(this);
        return result;
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
        await new UsbExportDialog { DataContext = new UsbExportDialogViewModel(Vm.Usb, sel, Vm.Catalog) }.ShowDialog(this);
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
