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
            var settings = AppSettings.Load();
            var viewModel = new MainWindowViewModel(ConfigSource.Load(), settings);

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Only on a clean exit. A crash leaves the previous file intact rather than
            // persisting whatever state caused it.
            desktop.Exit += (_, _) =>
            {
                viewModel.CaptureInto(settings);
                settings.Save();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
