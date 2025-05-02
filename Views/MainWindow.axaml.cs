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

namespace BoomBx.Views
{
    public partial class MainWindow : Window
    {
        private List<MMDevice> _playbackDevices = new();
        private List<MMDevice> _captureDevices = new();
        private bool _installationDismissed;
        private IWavePlayer? _waveOut;
        private WasapiCapture? _micCapture;
        private List<Process> _activeProcesses = new();
        private bool _isPlaying;
        private LoopStream? _loopedAudio;
        private VolumeSampleProvider? _volumeProvider;
        private SoundItem? _currentSoundSubscription;
        private AppSettings _settings = new AppSettings();

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
        private Func<Task>? _closeSplashAction;

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
            if(!IsVisible)
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
            
            this.ShowInTaskbar = true;
            this.WindowState = WindowState.Normal;
            this.CanResize = false;
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

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedSound))
            {
                if (_currentSoundSubscription != null)
                {
                    _currentSoundSubscription.PropertyChanged -= SoundItem_VolumeChanged;
                }

                _currentSoundSubscription = ViewModel.SelectedSound;

                if (_currentSoundSubscription != null)
                {
                    _currentSoundSubscription.PropertyChanged += SoundItem_VolumeChanged;
                }
            }
        }

        private void SoundItem_VolumeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SoundItem.Volume) && sender is SoundItem soundItem)
            {
                if (_volumeProvider != null)
                {
                    _volumeProvider.Volume = (float)(soundItem.Volume / 100.0);
                }
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

                await LoadAudioDevicesAsync();

                // if (!needsInstallation)
                // {
                //     await LoadAudioDevicesAsync();
                // }

                Console.WriteLine("[11] Initialization complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Initialization failed: {ex}");
                Logger.Log($"Init Error: {ex}");
                await CloseAfterDelay(2000);
            }
        }

        private void TogglePlaybackHandler(object? sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                StopAudioProcessing();
            }
            else
            {
                if (string.IsNullOrEmpty(ViewModel.SelectedFilePath))
                {
                    UpdateStatus("No audio file selected");
                    return;
                }
                StartAudioProcessing();
            }
            UpdatePlayButtonState();
        }

        private void UpdatePlayButtonState()
        {
            Dispatcher.UIThread.Post(() =>
            {
                PlayStopButton.Content = _isPlaying ? "⏹ Stop Audio" : "▶ Play Audio";
            });
        }

        private bool CheckVBCableInstallation()
        {
            Console.WriteLine("[Check] Starting device check");
            using var enumerator = new MMDeviceEnumerator();
            
            var hasInput = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Any(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
            
            var hasOutput = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Any(d => d.FriendlyName.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase));

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
                ShowProgress("Starting installation...");
                InstallationPanel.IsVisible = false;

                var success = await RunInstallerAsync();

                if (success)
                {
                    ShowProgress("Finalizing installation...");
                    await Task.Delay(2000);
                    for (int i = 0; i < 5; i++)
                    {
                        if (!CheckVBCableInstallation()) break;
                        await Task.Delay(1000);
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(async() =>
                {
                    InstallationPanel.IsVisible = !success;
                    MainUI.IsVisible = true;

                    if (success)
                    {
                        await LoadAudioDevicesAsync();
                        UpdateStatus("Installation completed successfully!");
                    }
                    else
                    {
                        UpdateStatus("Installation failed - check temp folder for logs");
                    }
                });

                if (CheckVBCableInstallation())
                {
                    UpdateStatus("Installation failed - please try manually");
                    return;
                }

                await ShowMainUI();
                await LoadAudioDevicesAsync();
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

        private void UpdateStatus(string message)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatMessage.Text = message ?? string.Empty;
                StatusMessage.Text = message ?? string.Empty;
                ProgressStatus.Text = message ?? string.Empty;
                InstallationMessageT.Text = message ?? string.Empty;
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
            try
            {
                ShowProgress("Preparing installer...");
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                Console.WriteLine(baseDir);
                var projectRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\"));
                Console.WriteLine(projectRoot);
                var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLower();
                Console.WriteLine(arch);
                UpdateStatus($"Detected architecture: {arch}");
                var scriptPath = Path.Combine(
                    projectRoot,
                    "InstallScripts",
                    "InstallVBCable.bat"
                );

                if (!File.Exists(scriptPath))
                {
                    await Dispatcher.UIThread.InvokeAsync(() => 
                        InstallationMessageT.Text = $"Installer not found at: {scriptPath}");
                    return false;
                }

                var tcs = new TaskCompletionSource<bool>();
                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{scriptPath}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };

                var process = new Process { StartInfo = processInfo };
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) =>
                {
                    tcs.TrySetResult(true);
                };

                var logPath = Path.Combine(Path.GetTempPath(), "vb_cable_install.log");
                Console.WriteLine($"Expecting log file at: {logPath}");

                try
                {
                    process.Start();
                    _activeProcesses.Add(process);
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => 
                        InstallationMessageT.Text = $"Installation canceled: {ex.Message}");
                    return false;
                }

                var timeoutTask = Task.Delay(120000);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        await Dispatcher.UIThread.InvokeAsync(() => 
                            InstallationMessageT.Text = "Installation timed out");
                        return false;
                    }
                }

                _activeProcesses.Remove(process);

                if (process.ExitCode != 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() => 
                        InstallationMessageT.Text = $"Failed (Code: {process.ExitCode})");
                    return false;
                }

                if (File.Exists(logPath))
                {
                    var logContent = await File.ReadAllTextAsync(logPath);
                    Console.WriteLine("Installation log:\n" + logContent);
                    File.Delete(logPath);
                }

                await Dispatcher.UIThread.InvokeAsync(async() =>
                {
                    await LoadAudioDevicesAsync();
                    InstallationMessage.Text = "Installation complete!";
                });

                if (arch.Contains("64"))
                {
                    var x64Installed = CheckDriverPresence("VBCABLE_Setup_x64.exe");
                    if (!x64Installed)
                    {
                        Logger.Log("64-bit installation verification failed");
                        return false;
                    }
                }
                else
                {
                    var x86Installed = CheckDriverPresence("VBCABLE_Setup.exe");
                    if (!x86Installed)
                    {
                        Logger.Log("32-bit installation verification failed");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => 
                    InstallationMessage.Text = $"Error: {ex.Message}".Trim());
                return false;
            }
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

        private async Task LoadAudioDevicesAsync()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    using var enumerator = new MMDeviceEnumerator();
                    
                    var playback = enumerator
                        .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                        .OrderBy(d => d.FriendlyName)
                        .ToList();

                    var capture = enumerator
                        .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                        .OrderBy(d => d.FriendlyName)
                        .ToList();

                    _playbackDevices = playback;
                    _captureDevices = capture;

                    PlaybackComboBox.ItemsSource = playback.Select(d => d.FriendlyName);
                    CaptureComboBox.ItemsSource = capture.Select(d => d.FriendlyName);
                    
                    LoadDeviceSettings();
                    
                    SelectDefaultDevices();
                });
            }
            catch (Exception ex)
            {
                UpdateStatus($"Device Error: {ex.Message}");
            }
        }

        private void LoadDeviceSettings()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BoomBx", 
                "settings.json");
            
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
                catch { /* idk */ }
            }

            if (!string.IsNullOrEmpty(_settings.LastPlaybackDevice))
            {
                PlaybackComboBox.SelectedItem = _playbackDevices
                    .FirstOrDefault(d => d.FriendlyName == _settings.LastPlaybackDevice)?.FriendlyName;
            }
            
            if (!string.IsNullOrEmpty(_settings.LastCaptureDevice))
            {
                CaptureComboBox.SelectedItem = _captureDevices
                    .FirstOrDefault(d => d.FriendlyName == _settings.LastCaptureDevice)?.FriendlyName;
            }
        }

        private void SaveDeviceSettings()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "BoomBx", 
                "settings.json");
            
            _settings.LastPlaybackDevice = PlaybackComboBox.SelectedItem?.ToString();
            _settings.LastCaptureDevice = CaptureComboBox.SelectedItem?.ToString();
            
            File.WriteAllText(path, JsonSerializer.Serialize(_settings));
        }

        public void PlaybackDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (PlaybackComboBox.SelectedIndex >= 0)
            {
                SaveDeviceSettings();
            }
        }

        public void CaptureDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (CaptureComboBox.SelectedIndex >= 0)
            {
                SaveDeviceSettings();
            }
        }

        public async void AddToLibrary(object? sender, RoutedEventArgs e)
        {
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
                if (!ViewModel.Sounds.Any(s => s.Path == path))
                {
                    var newItem = new SoundItem 
                    { 
                        Path = path,
                        Name = System.IO.Path.GetFileNameWithoutExtension(path)
                    };
                    newItem.PropertyChanged += SoundItem_PropertyChanged;
                    ViewModel.Sounds.Add(newItem);
                }
            }
            SaveSoundLibrary();
        }

        public void RemoveFromLibrary(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectedSound != null)
            {
                ViewModel.Sounds.Remove(ViewModel.SelectedSound);
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
                    
                    Directory.CreateDirectory(appDataDir);

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
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "BoomBx", 
                "sounds.json");
                
            File.WriteAllText(path, 
                JsonSerializer.Serialize(ViewModel.Sounds));
        }

        private void LoadSoundLibrary()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                "BoomBx", 
                "sounds.json");
                
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<List<SoundItem>>(json);

                if (loaded != null)
                {
                    ViewModel.Sounds.Clear();
                    foreach (var item in loaded)
                    {
                        if (!item.IconPath.StartsWith("avares://"))
                        {
                            var iconPath = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "BoomBx",
                                "icons",
                                item.IconPath);

                            if (!File.Exists(iconPath))
                            {
                                item.IconPath = "/Assets/bocchi.jpg";
                            }
                        }
                        
                        item.PropertyChanged += SoundItem_PropertyChanged;
                        ViewModel.Sounds.Add(item);
                    }
                }
            }
        }

        private void SoundItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Removed automatic SaveSoundLibrary call to wait for explicit save
        }

        private void SelectDefaultDevices()
        {
            if (string.IsNullOrEmpty(_settings.LastPlaybackDevice))
            {
                var cableInput = _playbackDevices.FirstOrDefault(d => 
                    d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
                
                PlaybackComboBox.SelectedItem = cableInput?.FriendlyName 
                    ?? _playbackDevices.FirstOrDefault()?.FriendlyName;
            }

            if (string.IsNullOrEmpty(_settings.LastCaptureDevice))
            {
                var defaultMic = _captureDevices.FirstOrDefault(d => 
                    !d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase));
                
                CaptureComboBox.SelectedItem = defaultMic?.FriendlyName 
                    ?? _captureDevices.FirstOrDefault()?.FriendlyName;
            }
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

        private void StartAudioProcessing()
        {
            StopAudioProcessing();

            if (PlaybackComboBox.SelectedIndex == -1 || CaptureComboBox.SelectedIndex == -1)
            {
                UpdateStatus("Please select both devices");
                return;
            }

            if (PlaybackComboBox.SelectedIndex >= _playbackDevices.Count || 
                CaptureComboBox.SelectedIndex >= _captureDevices.Count)
            {
                UpdateStatus("Invalid device selection");
                return;
            }

            var filePath = ViewModel.SelectedFilePath;

            var playbackDevice = _playbackDevices[PlaybackComboBox.SelectedIndex];
            var captureDevice = _captureDevices[CaptureComboBox.SelectedIndex];

            try
            {
                if (string.IsNullOrEmpty(filePath))
                    throw new InvalidOperationException("No audio file selected");

                WaveStream reader = Path.GetExtension(filePath).ToLower() switch
                {
                    ".mp3" => new Mp3FileReader(filePath),
                    ".wav" => new WaveFileReader(filePath),
                    _ => throw new InvalidOperationException("Unsupported file format")
                };

                _loopedAudio = new LoopStream(reader);
                
                _micCapture = new WasapiCapture(captureDevice);
                var micProvider = new WaveInProvider(_micCapture);

                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                var mixer = new MixingSampleProvider(targetFormat);
                
                var silence = new SilenceProvider(targetFormat);
                mixer.AddMixerInput(silence);
                
                var micSampleProvider = ConvertFormat(micProvider.ToSampleProvider(), targetFormat);
                mixer.AddMixerInput(micSampleProvider);
                
                var audioSampleProvider = ConvertFormat(_loopedAudio.ToSampleProvider(), targetFormat);
                _volumeProvider = new VolumeSampleProvider(audioSampleProvider);
                if(ViewModel.SelectedSound != null)
                {
                    _volumeProvider.Volume = (float)(ViewModel.SelectedSound.Volume / 100.0);
                }
                mixer.AddMixerInput(_volumeProvider);

                _waveOut = new WasapiOut(playbackDevice, AudioClientShareMode.Shared, true, 100);
                _waveOut.Init(mixer);

                _micCapture.StartRecording();
                _waveOut.Play();
                _isPlaying = true;

                UpdateStatus($"Playing {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex}");
                UpdateStatus($"Error: {ex.Message}");
                StopAudioProcessing(updateStatus: false);
            }
        }

        private void StopAudioProcessing(bool updateStatus = true)
        {
            _isPlaying = false;

            _waveOut?.Stop();
            _micCapture?.StopRecording();
            _loopedAudio?.Dispose();
            
            _waveOut?.Dispose();
            _micCapture?.Dispose();
            
            _waveOut = null;
            _micCapture = null;
            _loopedAudio = null;

            if (updateStatus)
            {
                UpdateStatus("Playback stopped");
            }
        }

        private ISampleProvider ConvertFormat(ISampleProvider input, WaveFormat targetFormat)
        {
            if (input.WaveFormat.SampleRate != targetFormat.SampleRate)
            {
                input = new WdlResamplingSampleProvider(input, targetFormat.SampleRate);
            }

            if (input.WaveFormat.Channels != targetFormat.Channels)
            {
                input = targetFormat.Channels == 2 
                    ? new MonoToStereoSampleProvider(input) 
                    : new StereoToMonoSampleProvider(input);
            }

            return input;
        }
    }
}