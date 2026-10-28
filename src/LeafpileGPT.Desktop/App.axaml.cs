using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LeafpileGPT.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            var storyPath = DesktopLaunchArguments.ResolveStoryPath(desktop.Args ?? []);
            if (storyPath is not null)
            {
                mainWindow.Opened += async (_, _) => await mainWindow.LoadStoryFromPathAsync(storyPath);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
