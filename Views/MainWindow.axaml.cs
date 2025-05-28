using Avalonia.Controls;
using Avalonia.Interactivity;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;
using NAudio.Wave.SampleProviders;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using System.Threading;
using Avalonia.Themes.Fluent;
using System.Runtime.InteropServices;
using Avalonia.Platform.Storage;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.ComponentModel;
using BoomBx.ViewModels;
using BoomBx.Models;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Reflection;
using System.Text;
using Avalonia.Media;
using System.Text.Json.Serialization;
using Avalonia.Input;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;
using BoomBx.Services;

namespace BoomBx.Views
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings = new();

        private bool _installationDismissed;
        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
        private Func<Task>? _closeSplashAction;

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }


        public void SetCloseSplashAction(Func<Task> action)
        {
            _closeSplashAction = action;
        }

        public async Task CloseSplashAsync()
        {
            if (_closeSplashAction != null)
            {
                await _closeSplashAction();
            }
        }


        public new void Show()
        {
            if (!IsVisible)
            {
                Dispatcher.UIThread.Post(() => base.Show());
            }
        }

        public MainWindow()
        {
            Console.SetOut(new SplashTextWriter());
            Console.WriteLine("[1] MainWindow constructor started");

            this.Styles.Add(new FluentTheme());

            InitializeComponent();

            DataContext = new MainWindowViewModel();

            _deviceManager = new DeviceManager(
                (MainWindowViewModel)DataContext!,
                _settings,
                Dispatcher.UIThread
            );

            InitializeAudioService();
            InitializeTts();

            this.ShowInTaskbar = true;
            this.WindowState = WindowState.Normal;
            this.Opacity = 1;
            this.IsVisible = false;
            Console.WriteLine("[3] Window properties set");
        }

        public async Task InitializeAsync()
        {
            try
            {
                Console.WriteLine("[2] Starting async initialization");
                await LoadIconAsync();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (DataContext is INotifyPropertyChanged npc)
                    {
                        npc.PropertyChanged += ViewModel_PropertyChanged;
                    }
                });
                Console.WriteLine("[2] Async initialization complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Initialization failed: {ex}");
                throw;
            }
        }

        public async Task StartMainInitialization()
        {
            try
            {
                Console.WriteLine("[4] Starting main initialization");
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await MainWindow_LoadedAsync();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Main initialization failed: {ex}");
                throw;
            }
        }

        private async Task LoadIconAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    var uri = new Uri("avares://BoomBx/Assets/bocchi.jpg");
                    using var stream = AssetLoader.Open(uri);
                    var testImage = new Bitmap(stream);
                    Console.WriteLine("Default icon loaded successfully");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading default icon: {ex}");
            }
        }

        private async Task MainWindow_LoadedAsync()
        {
            try
            {
                LoadSoundLibrary();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    this.IsVisible = true;
                    //this.CanResize = true;
                    this.InvalidateMeasure();
                    this.InvalidateArrange();
                });

                Console.WriteLine("[5] MainWindow_LoadedAsync started");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var needsInstallation = !_installationDismissed && await Task.Run(() => CheckVBCableInstallation());

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    MainUI.IsVisible = true;
                    InstallationPanel.IsVisible = needsInstallation;
                    InstallationMessage.Text = needsInstallation
                        ? "VB-Cable not detected. Please install to continue full functionality."
                        : "";
                });

                await _deviceManager.InitializeAsync();
                _audioService?.InitializeDevices();

                Console.WriteLine("[11] Initialization complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Initialization failed: {ex}");
                Logger.Log($"Init Error: {ex}");
                await CloseAfterDelay(2000);
            }
        }

        private bool CheckVBCableInstallation()
        {
            Console.WriteLine("[Check] Starting device check");
            using var enumerator = new MMDeviceEnumerator();

            bool hasInput = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Any(d => d.FriendlyName.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasOutput = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Any(d => d.FriendlyName.IndexOf("CABLE Output", StringComparison.OrdinalIgnoreCase) >= 0);

            return !(hasInput && hasOutput);
        }

        private async Task ShowInstallationUI()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallationPanel.IsVisible = true;
                MainUI.IsVisible = false;
            });
        }

        private async Task ShowMainUI()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallationPanel.IsVisible = false;
                MainUI.IsVisible = true;
            });
        }

        private async void OnInstallClicked(object? sender, RoutedEventArgs e)
        {
            Button? installButton = sender as Button;
            if (installButton == null) return;

            try
            {
                installButton.IsEnabled = false;
                StatoMessage.Text = string.Empty;
                ShowProgress("Starting installation...");
                InstallationPanel.IsVisible = false;

                var success = await RunInstallerAsync();

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    InstallationPanel.IsVisible = !success;
                    MainUI.IsVisible = true;

                    if (success)
                    {
                        await _deviceManager.InitializeAsync();
                    }
                });

                if (!success && CheckVBCableInstallation())
                {
                    await _deviceManager.InitializeAsync();
                    UpdateStatus("Manual installation required - Download from vb-audio.com", true);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Installation error: {ex.Message}");
                _ = CloseAfterDelay(5000);
            }
            finally
            {
                HideProgress();
                installButton.IsEnabled = true;
            }
        }

        private void OnExitClicked(object? sender, RoutedEventArgs e)
        {
            _installationDismissed = true;
            InstallationPanel.IsVisible = false;
            StatoMessage.Text = "Some features may require VB-Cable";
        }

        private void UpdateStatus(string message, bool isError = false)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatMessage.Text = message ?? string.Empty;
                StatusMessage.Text = message ?? string.Empty;
                ProgressStatus.Text = message ?? string.Empty;
                InstallationMessageT.Text = message ?? string.Empty;

                StatoMessage.Text = message ?? string.Empty;
                StatoMessage.Foreground = isError
                    ? new SolidColorBrush(Colors.OrangeRed)
                    : new SolidColorBrush(Colors.LightGray);

                if (!isError)
                {
                    Task.Delay(5000).ContinueWith(_ =>
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (StatoMessage.Text == message)
                                StatoMessage.Text = string.Empty;
                        }));
                }
            });
        }

        private async Task CloseAfterDelay(int milliseconds)
        {
            await Task.Delay(milliseconds);
            await SafeShutdown();
        }

        private async Task SafeShutdown()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var lifetime = Application.Current?.ApplicationLifetime
                        as IClassicDesktopStyleApplicationLifetime;
                    lifetime?.Shutdown(0);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Shutdown error: {ex}");
            }
        }

        private async Task<bool> RunInstallerAsync()
        {
            string tempDir = "";
            string logPath = Path.Combine(Path.GetTempPath(), "vb_cable_install.log");

            try
            {
                ShowProgress("Preparing installer...");
                UpdateStatus("Starting VB-Cable installation...");

                if (File.Exists(logPath)) File.Delete(logPath);

                var assembly = Assembly.GetExecutingAssembly();
                const string resourceName = "BoomBx.InstallScripts.InstallVBCable.bat";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    UpdateStatus("Error: Missing installation components", true);
                    return false;
                }

                tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);
                var scriptPath = Path.Combine(tempDir, "InstallVBCable.bat");

                await using (var fileStream = File.Create(scriptPath))
                {
                    await stream.CopyToAsync(fileStream);
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    Verb = "runas",
                    UseShellExecute = true, // Required for admin elevation
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = tempDir
                };

                using var process = new Process { StartInfo = processInfo };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Installation failed: {ex.Message}", true);
                    return false;
                }

                var timeout = TimeSpan.FromMinutes(3);
                if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    process.Kill();
                    UpdateStatus("Installation timed out - Please try again", true);
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    var errorMessage = process.ExitCode switch
                    {
                        1 => "Download failed - Check internet connection",
                        2 => "File extraction failed - Antivirus might be blocking",
                        3 => "Driver installation failed - Run as Administrator",
                        _ => $"Installation error (Code: {process.ExitCode})"
                    };

                    UpdateStatus(errorMessage, true);
                    return false;
                }

                for (int i = 0; i < 20; i++) //extended to 20 seconds until everything stable
                {
                    if (!CheckVBCableInstallation())
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    UpdateStatus("VB-Cable installed successfully!");
                    await _deviceManager.InitializeAsync();
                    return true;
                }

                UpdateStatus("Installation completed but verification failed", true);
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Critical error: {ex.Message}", true);
                return false;
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
                catch { /* Ignore cleanup errors */ }
                HideProgress();
            }
        }

        public void PlaybackDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is MMDevice device)
            {
                _audioService?.HandlePlaybackDeviceChanged(device);
            }
        }

        public void CaptureDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is MMDevice device)
            {
                _audioService?.HandleCaptureDeviceChanged(device);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _audioService?.Dispose();
            base.OnClosed(e);
        }

        private bool CheckDriverPresence(string installerName)
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var hasInput = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .Any(d => d.FriendlyName.Contains("CABLE Input"));

                var hasOutput = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    .Any(d => d.FriendlyName.Contains("CABLE Output"));

                return hasInput && hasOutput;
            }
            catch
            {
                return false;
            }
        }

        private void ShowProgress(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressStatus.Text = message;
                ProgressBarContainer.IsVisible = true;
            });
        }

        private void HideProgress()
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressBarContainer.IsVisible = false;
            });
        }

        public async void AddToLibrary(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSoundboard == null) return;

            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Audio Files",
                AllowMultiple = true,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Audio Files") { Patterns = new[] { "*.mp3", "*.wav" } }
                }
            });

            foreach (var file in files)
            {
                var path = file.Path.LocalPath;
                if (!ViewModel.SelectedSoundboard.Sounds.Any(s => s.Path == path))
                {
                    var newItem = new SoundItem
                    {
                        Path = path,
                        Name = System.IO.Path.GetFileNameWithoutExtension(path)
                    };
                    newItem.PropertyChanged += SoundItem_PropertyChanged;
                    ViewModel.SelectedSoundboard.Sounds.Add(newItem);
                }
            }
            SaveSoundLibrary();
        }

        public void RemoveFromLibrary(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSoundboard != null &&
                ViewModel.SelectedSound != null)
            {
                ViewModel.SelectedSoundboard.Sounds.Remove(ViewModel.SelectedSound);
                SaveSoundLibrary();
            }
        }

        private async void ChangeIconClicked(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound == null) return;

            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Icon Image",
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new("Image Files") { Patterns = new[] { "*.jpg", "*.png", "*.bmp" } }
                }
            });

            if (files.Count > 0 && files[0].TryGetLocalPath() is string localPath)
            {
                try
                {
                    var appDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "BoomBx",
                        "icons");

                    var iconDir = Path.Combine(appDataDir, "icons");
                    Directory.CreateDirectory(iconDir);

                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(localPath)}";
                    var destPath = Path.Combine(appDataDir, fileName);

                    File.Copy(localPath, destPath, overwrite: true);

                    ViewModel.SelectedSound.IconPath = fileName;
                    SaveSoundLibrary();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving icon: {ex}");
                    UpdateStatus("Failed to save icon");
                }
            }
        }

        private void SaveChangesClicked(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound != null)
            {
                SaveSoundLibrary();
                LoadSoundLibrary();
                UpdateStatus("Changes saved successfully!");
            }
        }

        private void SaveSoundLibrary()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BoomBx");

            Directory.CreateDirectory(appDataDir);
            var path = Path.Combine(appDataDir, "soundboards.json");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.Preserve
            };

            File.WriteAllText(path, JsonSerializer.Serialize(ViewModel.Soundboards, options));
        }


        private void LoadSoundLibrary()
        {
            try
            {
                var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BoomBx");
                Directory.CreateDirectory(appDataDir);
                var path = Path.Combine(appDataDir, "soundboards.json");

                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var options = new JsonSerializerOptions
                    {
                        ReferenceHandler = ReferenceHandler.Preserve,
                        PropertyNameCaseInsensitive = true
                    };

                    var soundboards = JsonSerializer.Deserialize<ObservableCollection<Soundboard>>(json, options);

                    ViewModel.Soundboards.Clear();
                    if (soundboards != null)
                    {
                        foreach (var board in soundboards)
                        {
                            ViewModel.Soundboards.Add(board);
                        }
                    }
                }

                if (!ViewModel.Soundboards.Any())
                {
                    var defaultBoard = new Soundboard { Name = "Default" };
                    ViewModel.Soundboards.Add(defaultBoard);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading sound library: {ex}");
            }
        }

        private void SoundItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Removed automatic SaveSoundLibrary call to wait for explicit save
        }

        public void PlayThroughDeviceHandler(object? sender, RoutedEventArgs e)
        {
            try
            {
                StartAudioProcessing();
                StatusMessage.Text = "Routing audio through virtual microphone...";
            }
            catch (Exception ex)
            {
                StatusMessage.Text = $"Error: {ex.Message}";
            }
        }
        


        private void SoundboardList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is Soundboard selected)
            {
                ViewModel.SelectedSoundboard = selected;
            }
        }

        public async void AddSoundboard(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog { Prompt = "Enter soundboard name:" };
            var result = await dialog.ShowDialog<string>(this);

            if (!string.IsNullOrWhiteSpace(result))
            {
                var soundboard = new Soundboard { Name = result };
                ViewModel.Soundboards.Add(soundboard);
                ViewModel.SelectedSoundboard = soundboard;
                SaveSoundLibrary();
            }
        }

        public async void RenameSoundboard(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSoundboard == null) return;

            var dialog = new InputDialog
            {
                Title = "Rename Soundboard",
                Prompt = "Enter new name:",
                InputText = ViewModel.SelectedSoundboard.Name ?? string.Empty
            };

            var newName = await dialog.ShowDialog<string>(this);

            if (!string.IsNullOrWhiteSpace(newName))
            {
                ViewModel.SelectedSoundboard.Name = newName;
                SaveSoundLibrary();
            }
        }

        public void RemoveSoundboard(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSoundboard != null &&
                ViewModel.Soundboards.Count > 1)
            {
                ViewModel.Soundboards.Remove(ViewModel.SelectedSoundboard);
                ViewModel.SelectedSoundboard = ViewModel.Soundboards.FirstOrDefault();
                SaveSoundLibrary();
            }
        }

        //to whoever reading this, i will clean up and optimize i promise LATER
        private void OpenGitHub(object? sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/sardeq",
                UseShellExecute = true
            });
        }

        private void OpenTrello(object? sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://trello.com/b/74PesGFa/boombx",
                UseShellExecute = true
            });
        }
    }
}