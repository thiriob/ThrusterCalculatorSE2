using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ThrusterCalculator.Gui.Services;
using ThrusterCalculator.Gui.ViewModels;
using ThrusterCalculator.Gui.Views;

namespace ThrusterCalculator.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Composition root: the only place the GUI touches config loading. Everything below
            // this line sees a GameData and nothing else — no files, no game, no producer types.
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(ConfigSource.Load()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
