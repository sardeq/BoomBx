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
using Avalonia.Logging;
using Avalonia.Controls;
using Avalonia.Threading;


namespace BoomBx;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);

    public SplashScreen _splash;
    private IClassicDesktopStyleApplicationLifetime? _desktop;


    public App()
    {
        if (!AttachConsole(-1)) AllocConsole();
        _splash = new SplashScreen();
    }

    public void UpdateSplashStatus(string message)
    {
        _splash?.UpdateStatus(message);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            
            BindingPlugins.DataValidators.RemoveAt(0);
            
            try
            {
                _splash.Show();
                
                var mainWindow = new MainWindow();
                await mainWindow.InitializeAsync();
                
                var tcs = new TaskCompletionSource();
                
                mainWindow.SetCloseSplashAction(async () => 
                {
                    try
                    {
                        Dispatcher.UIThread.Post(() => 
                        {
                            _desktop.MainWindow = mainWindow;
                            mainWindow.Show();
                        });

                        await _splash.CloseSplashAsync();
                        
                        await mainWindow.StartMainInitialization();
                    }
                    finally
                    {
                        tcs.SetResult();
                    }
                });

                await mainWindow.CloseSplashAsync();
                await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal startup error: {ex}");
                throw;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeMainWindowAsync(MainWindow mainWindow)
    {
        await Task.Delay(100);
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Cleanup code here
    }
}