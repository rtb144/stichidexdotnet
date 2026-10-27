using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Stitchidex.Desktop.ViewModels;
using Stitchidex.Desktop.Views;
namespace Stitchidex.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
        {
            var vm = new MainWindowViewModel(Environment.GetEnvironmentVariable("STITCHIDEX_DB") is { Length: > 0 } db ? db : MainWindowViewModel.DefaultDatabasePath());
            d.MainWindow = new MainWindow { DataContext = vm };
            // Optional automation hooks (used to render documentation screenshots).
            if (Environment.GetEnvironmentVariable("STITCHIDEX_SCAN_FOLDER") is { Length: > 0 } f) _ = vm.AddFolderAsync(f);
            if (Environment.GetEnvironmentVariable("STITCHIDEX_THEME") is { Length: > 0 } t) vm.SelectedTheme = t;
            if (Environment.GetEnvironmentVariable("STITCHIDEX_SEARCH") is { Length: > 0 } q)
                Avalonia.Threading.DispatcherTimer.RunOnce(() => vm.SearchText = q, TimeSpan.FromSeconds(4));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
