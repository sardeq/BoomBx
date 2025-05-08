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

namespace BoomBx.Views
{
    public partial class MainWindow : Window
    {
        private List<MMDevice> _playbackDevices = new();
        private List<MMDevice> _captureDevices = new();
        private bool _installationDismissed;
        //private IWavePlayer? _micWaveOut;
        //private IWavePlayer? _audioWaveOut;
        private WasapiCapture? _micCapture;
        private List<Process> _activeProcesses = new();
        private bool _isPlaying;
        //private LoopStream? _loopedAudio;
        private IWavePlayer? _audioWaveOutSpeaker;
        //private LoopStream? _loopedAudioSpeaker;

        //private VolumeSampleProvider? _volumeProvider;
        private VolumeSampleProvider? _volumeProviderVirtual;
        private VolumeSampleProvider? _volumeProviderSpeaker;
        private MixingSampleProvider? _persistentMixer;
        private IWavePlayer? _persistentOutput;
        private SoundItem? _currentSoundSubscription;
        private AppSettings _settings = new AppSettings();

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
        private Func<Task>? _closeSplashAction;
        private enum PlaybackState { Stopped, Playing, Paused }
        private PlaybackState _currentPlaybackState = PlaybackState.Stopped;
        private LoopStream? _virtualLoopStream;
        private LoopStream? _speakerLoopStream;
        private long _pausePositionVirtual;
        private long _pausePositionSpeaker;

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
                if (_currentPlaybackState != PlaybackState.Stopped)
                {
                    StopAudioProcessing();
                }

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
            else if (e.PropertyName == nameof(MainWindowViewModel.IsLoopingEnabled))
            {
                if (_virtualLoopStream != null)
                    _virtualLoopStream.Loop = ViewModel.IsLoopingEnabled;
                if (_speakerLoopStream != null)
                    _speakerLoopStream.Loop = ViewModel.IsLoopingEnabled;
            }
        }
        private void SoundItem_VolumeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SoundItem.Volume) && sender is SoundItem soundItem)
            {
                float newVolume = (float)(soundItem.Volume / 100.0);
                
                if (_volumeProviderVirtual != null)
                    _volumeProviderVirtual.Volume = newVolume;
                
                if (_volumeProviderSpeaker != null)
                    _volumeProviderSpeaker.Volume = newVolume;
            }
        }

        private void PlayPauseHandler(object? sender, RoutedEventArgs e)
        {
            switch (_currentPlaybackState)
            {
                case PlaybackState.Stopped:
                    StartAudioProcessing();
                    break;
                case PlaybackState.Playing:
                    PauseAudioProcessing();
                    break;
                case PlaybackState.Paused:
                    ResumeAudioProcessing();
                    break;
            }
        }

        private void StopHandler(object? sender, RoutedEventArgs e)
        {
            StopAudioProcessing();
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
                StatoMessage.Text = string.Empty;
                ShowProgress("Starting installation...");
                InstallationPanel.IsVisible = false;

                var success = await RunInstallerAsync();

                await Dispatcher.UIThread.InvokeAsync(async() =>
                {
                    InstallationPanel.IsVisible = !success;
                    MainUI.IsVisible = true;

                    if (success)
                    {
                        await LoadAudioDevicesAsync();
                    }
                });

                if (!success && CheckVBCableInstallation())
                {
                    await LoadAudioDevicesAsync();
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

                for (int i = 0; i < 10; i++)
                {
                    if (!CheckVBCableInstallation()) 
                    {
                        await Task.Delay(1000);
                        continue;
                    }
                    
                    UpdateStatus("VB-Cable installed successfully!");
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
                StartPersistentAudioRouting();
            }
        }

        public void CaptureDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (CaptureComboBox.SelectedIndex >= 0)
            {
                SaveDeviceSettings();
                StartPersistentAudioRouting();
            }
        }

        private void StartPersistentAudioRouting()
        {
            StopPersistentAudioRouting();

            if (PlaybackComboBox.SelectedIndex == -1 || CaptureComboBox.SelectedIndex == -1)
                return;

            var playbackDevice = _playbackDevices[PlaybackComboBox.SelectedIndex];
            var captureDevice = _captureDevices[CaptureComboBox.SelectedIndex];

            var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            _persistentMixer = new MixingSampleProvider(targetFormat);
            _persistentMixer.ReadFully = true;

            _micCapture = new WasapiCapture(captureDevice);
            var micProvider = new WaveInProvider(_micCapture);
            var micSampleProvider = ConvertFormat(micProvider.ToSampleProvider(), targetFormat);
            _persistentMixer.AddMixerInput(micSampleProvider);

            _persistentOutput = new WasapiOut(playbackDevice, AudioClientShareMode.Shared, true, 100);
            _persistentOutput.Init(_persistentMixer);
            
            _micCapture.StartRecording();
            _persistentOutput.Play();
        }

        private void StopPersistentAudioRouting()
        {
            _persistentOutput?.Stop();
            _micCapture?.StopRecording();
            
            _persistentOutput?.Dispose();
            _micCapture?.Dispose();
            
            _persistentOutput = null;
            _micCapture = null;
            _persistentMixer = null;
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
                StopAudioProcessing(updateStatus: false);

                if (string.IsNullOrEmpty(ViewModel.SelectedSound?.Path))
                {
                    UpdateStatus("No audio file selected");
                    return;
                }

                try
                {
                    var filePath = ViewModel.SelectedSound.Path;
                    var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
                    float initialVolume = (float)ViewModel.SelectedSound.Volume / 100f;

                    // Initialize audio streams
                    _virtualLoopStream = CreateAudioStream(filePath);
                    _virtualLoopStream.Loop = ViewModel.IsLoopingEnabled;
                    var virtualAudioProvider = ConvertFormat(_virtualLoopStream.ToSampleProvider(), targetFormat);
                    _volumeProviderVirtual = new VolumeSampleProvider(virtualAudioProvider) { Volume = initialVolume };

                    _speakerLoopStream = CreateAudioStream(filePath);
                    _speakerLoopStream.Loop = ViewModel.IsLoopingEnabled;
                    var speakerAudioProvider = ConvertFormat(_speakerLoopStream.ToSampleProvider(), targetFormat);
                    _volumeProviderSpeaker = new VolumeSampleProvider(speakerAudioProvider) { Volume = initialVolume };

                    // Add to mixer
                    if (_persistentMixer != null)
                    {
                        _persistentMixer.AddMixerInput(_volumeProviderVirtual);
                    }

                    // Initialize speaker output
                    using var enumerator = new MMDeviceEnumerator();
                    var defaultSpeaker = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _audioWaveOutSpeaker = new WasapiOut(defaultSpeaker, AudioClientShareMode.Shared, true, 100);
                    _audioWaveOutSpeaker.Init(_volumeProviderSpeaker);
                    _audioWaveOutSpeaker.PlaybackStopped += (s, args) =>
                    {
                        if (_currentPlaybackState == PlaybackState.Playing)
                        {
                            Dispatcher.UIThread.Post(() => StopAudioProcessing());
                        }
                    };
                    _audioWaveOutSpeaker.Play();

                    _currentPlaybackState = PlaybackState.Playing;
                    UpdateStatus($"Playing {Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error: {ex.Message}");
                    StopAudioProcessing(updateStatus: false);
                }
                UpdatePlayPauseButtonState();
            }

            private void PauseAudioProcessing()
            {
                if (_currentPlaybackState != PlaybackState.Playing) return;

                _pausePositionVirtual = _virtualLoopStream?.Position ?? 0;
                _pausePositionSpeaker = _speakerLoopStream?.Position ?? 0;

                _audioWaveOutSpeaker?.Pause();
                if (_persistentMixer != null && _volumeProviderVirtual != null)
                {
                    _persistentMixer.RemoveMixerInput(_volumeProviderVirtual);
                }

                _currentPlaybackState = PlaybackState.Paused;
                UpdateStatus("Playback paused");
                UpdatePlayPauseButtonState();
            }

            private void ResumeAudioProcessing()
            {
                if (_currentPlaybackState != PlaybackState.Paused) return;

                if (_virtualLoopStream != null) _virtualLoopStream.Position = _pausePositionVirtual;
                if (_speakerLoopStream != null) _speakerLoopStream.Position = _pausePositionSpeaker;

                if (_persistentMixer != null && _volumeProviderVirtual != null)
                {
                    _persistentMixer.AddMixerInput(_volumeProviderVirtual);
                }
                _audioWaveOutSpeaker?.Play();

                _currentPlaybackState = PlaybackState.Playing;
                UpdateStatus("Playback resumed");
                UpdatePlayPauseButtonState();
            }

            private void StopAudioProcessing(bool updateStatus = true)
            {
                _currentPlaybackState = PlaybackState.Stopped;

                _pausePositionVirtual = 0;
                _pausePositionSpeaker = 0;

                _audioWaveOutSpeaker?.Stop();
                _audioWaveOutSpeaker?.Dispose();
                _audioWaveOutSpeaker = null;

                if (_persistentMixer != null && _volumeProviderVirtual != null)
                {
                    _persistentMixer.RemoveMixerInput(_volumeProviderVirtual);
                }

                _virtualLoopStream?.Dispose();
                _speakerLoopStream?.Dispose();
                _virtualLoopStream = null;
                _speakerLoopStream = null;

                if (updateStatus) UpdateStatus("Playback stopped");
                UpdatePlayPauseButtonState();
            }

            private void UpdatePlayPauseButtonState()
            {
                Dispatcher.UIThread.Post(() =>
                {
                    PlayPauseButton.Content = _currentPlaybackState switch
                    {
                        PlaybackState.Playing => "⏸ Pause",
                        PlaybackState.Paused => "▶ Resume",
                        _ => "▶ Play"
                    };
                });
            }

        private LoopStream CreateAudioStream(string filePath)
        {
            WaveStream reader = Path.GetExtension(filePath).ToLower() switch
            {
                ".mp3" => new Mp3FileReader(filePath),
                ".wav" => new WaveFileReader(filePath),
                _ => throw new InvalidOperationException("Unsupported file format")
            };
            return new LoopStream(reader);
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


        //to whoever reading this, i will clean up and optimize i promise
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