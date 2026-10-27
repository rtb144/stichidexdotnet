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
            d.MainWindow = new MainWindow { DataContext = new MainWindowViewModel(MainWindowViewModel.DefaultDatabasePath()) };
        base.OnFrameworkInitializationCompleted();
    }
}
