using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShoutingIguana.ViewModels;

namespace ShoutingIguana;

public partial class App : Application
{
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        // Configure DI container
        var services = new ServiceCollection();
        
        // Register ViewModels
        services.AddSingleton<MainViewModel>();
        
        // Future: Register services here
        // services.AddSingleton<IYourService, YourService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create main window with DI-injected ViewModel
        var mainWindow = new MainWindow
        {
            DataContext = _serviceProvider.GetRequiredService<MainViewModel>()
        };
        
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider.Dispose();
        base.OnExit(e);
    }
}

