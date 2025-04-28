using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using BoomBx.ViewModels;
using BoomBx.Views;
using System.Runtime.InteropServices;


namespace BoomBx;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);

    public App()
    {
        if (!AttachConsole(-1)) AllocConsole();
        Console.WriteLine("----- Application Starting -----");
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Logger.Log($"CRASH: {e.ExceptionObject}");
            Console.WriteLine($"CRASH: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Logger.Log($"TASK ERROR: {e.Exception}");
            Console.WriteLine($"TASK ERROR: {e.Exception}");
        };
            
        try
        {
           
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                BindingPlugins.DataValidators.RemoveAt(0);
                desktop.MainWindow = new MainWindow();
                desktop.Exit += OnExit;
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal startup error: {ex}");
            throw;
        }
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Cleanup code here
    }
}