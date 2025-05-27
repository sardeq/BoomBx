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
        private DeviceManager _deviceManager;
        private readonly AppSettings _settings = new();


        private List<MMDevice> _playbackDevices = new();
        private List<MMDevice> _captureDevices = new();
        private bool _installationDismissed;
        private IWavePlayer? _audioWaveOutSpeaker;
        private VolumeSampleProvider? _volumeProviderVirtual;
        private VolumeSampleProvider? _volumeProviderSpeaker;
        private MixingSampleProvider? _persistentMixer;
        private SoundItem? _currentSoundSubscription;
        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
        private Func<Task>? _closeSplashAction;
        private enum PlaybackState { Stopped, Playing, Paused }
        private PlaybackState _currentPlaybackState = PlaybackState.Stopped;
        private LoopStream? _virtualLoopStream;
        private LoopStream? _speakerLoopStream;

        private void MinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseClick(object? sender, RoutedEventArgs e) => Close();

        private EqualizerSampleProvider? _equalizerVirtual;
        private EqualizerSampleProvider? _equalizerSpeaker;
        private PitchShifter? _pitchShifterVirtual;
        private PitchShifter? _pitchShifterSpeaker;
        
        #if WINDOWS
            private SpeechSynthesizer _synth = new();
        #else
                private object _synth = new();
        #endif
        
        private MemoryStream? _ttsAudioStream;


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

        protected override void OnClosed(EventArgs e)
        {
            _deviceManager.Dispose();
            base.OnClosed(e);
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
                (MainWindowViewModel)DataContext, 
                _settings,
                Dispatcher.UIThread
            );

            #if WINDOWS
                InitializeTts();
            #endif

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
            if (sender is not SoundItem soundItem ||
                _currentPlaybackState != PlaybackState.Playing ||
                _volumeProviderVirtual == null ||
                _volumeProviderSpeaker == null)
            {
                return;
            }

            try
            {
                switch (e.PropertyName)
                {
                    case nameof(SoundItem.Volume):
                        _volumeProviderVirtual.Volume = (float)soundItem.Volume / 100;
                        _volumeProviderSpeaker.Volume = (float)soundItem.Volume / 100;
                        break;

                    case nameof(SoundItem.Bass):
                    case nameof(SoundItem.Treble):
                        _equalizerVirtual?.UpdateFilters(soundItem);
                        _equalizerSpeaker?.UpdateFilters(soundItem);
                        break;

                    case nameof(SoundItem.Pitch):
                        _pitchShifterVirtual?.SetPitch((float)soundItem.Pitch);
                        _pitchShifterSpeaker?.SetPitch((float)soundItem.Pitch);
                        break;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠️ Update failed: {ex.Message}");
            }
        }

        private void UpdateVolume(SoundItem soundItem)
        {
            float newVolume = (float)soundItem.Volume / 100f;

            if (_volumeProviderVirtual != null)
                _volumeProviderVirtual.Volume = newVolume;

            if (_volumeProviderSpeaker != null)
                _volumeProviderSpeaker.Volume = newVolume;
        }

        private void UpdateEqualization(SoundItem soundItem)
        {
            _equalizerVirtual?.UpdateFilters(soundItem);
            _equalizerSpeaker?.UpdateFilters(soundItem);
        }

        private void UpdatePitch(SoundItem soundItem)
        {
            if (_pitchShifterVirtual != null)
                _pitchShifterVirtual.PitchFactor = (float)soundItem.Pitch;

            if (_pitchShifterSpeaker != null)
                _pitchShifterSpeaker.PitchFactor = (float)soundItem.Pitch;
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

        public void PlaybackDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is MMDevice device)
            {
                _deviceManager.HandlePlaybackDeviceChanged(device);
            }
        }

        public void CaptureDeviceChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is MMDevice device)
            {
                _deviceManager.HandleCaptureDeviceChanged(device);
            }
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
            try
            {
                StopAudioProcessing(updateStatus: false);

                if (ViewModel.SelectedSound == null)
                {
                    UpdateStatus("🔇 No sound selected");
                    return;
                }

                if (string.IsNullOrWhiteSpace(ViewModel.SelectedSound.Path) || !File.Exists(ViewModel.SelectedSound.Path))
                {
                    UpdateStatus("❌ Invalid audio file path");
                    return;
                }

                var tempVirtualStream = CreateAudioStream(ViewModel.SelectedSound.Path);
                var tempSpeakerStream = CreateAudioStream(ViewModel.SelectedSound.Path);

                if (tempVirtualStream == null || tempSpeakerStream == null)
                {
                    UpdateStatus("⚠️ Failed to initialize audio streams");
                    tempVirtualStream?.Dispose();
                    tempSpeakerStream?.Dispose();
                    return;
                }

                if (tempVirtualStream.WaveFormat == null || tempSpeakerStream.WaveFormat == null)
                {
                    UpdateStatus("⚠️ Audio stream has invalid format");
                    tempVirtualStream.Dispose();
                    tempSpeakerStream.Dispose();
                    return;
                }

                _virtualLoopStream = tempVirtualStream;
                _speakerLoopStream = tempSpeakerStream;
                _virtualLoopStream.Loop = ViewModel.IsLoopingEnabled;
                _speakerLoopStream.Loop = ViewModel.IsLoopingEnabled;

                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                float initialVolume = (float)(ViewModel.SelectedSound.Volume / 100.0);

                var virtualChain = CreateProcessingChain(_virtualLoopStream, targetFormat, ViewModel.SelectedSound, initialVolume);
                var speakerChain = CreateProcessingChain(_speakerLoopStream, targetFormat, ViewModel.SelectedSound, initialVolume);

                if (virtualChain?.Volume == null || speakerChain?.Volume == null)
                {
                    UpdateStatus("⚠️ Failed to create processing chain");
                    StopAudioProcessing(updateStatus: false);
                    return;
                }

                if (virtualChain is { } vChain)
                {
                    _equalizerVirtual = vChain.Eq;
                    _pitchShifterVirtual = vChain.Pitch;
                    _volumeProviderVirtual = vChain.Volume;
                }

                if (speakerChain is { } sChain)
                {
                    _equalizerSpeaker = sChain.Eq;
                    _pitchShifterSpeaker = sChain.Pitch;
                    _volumeProviderSpeaker = sChain.Volume;
                }

                InitializeVirtualOutput();
                InitializeSpeakerOutput();

                _currentPlaybackState = PlaybackState.Playing;
                UpdateStatus($"🎵 Playing {Path.GetFileName(ViewModel.SelectedSound.Path)}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"⛔ Error: {ex.Message}");
                StopAudioProcessing(updateStatus: false);
            }
            finally
            {
                UpdatePlayPauseButtonState();
            }
        }


        private (EqualizerSampleProvider Eq, PitchShifter Pitch, VolumeSampleProvider Volume)?
            CreateProcessingChain(LoopStream stream, WaveFormat format, SoundItem sound, float volume)
        {
            try
            {
                if (stream == null) throw new ArgumentNullException(nameof(stream));
                if (sound == null) throw new ArgumentNullException(nameof(sound));
                if (stream.WaveFormat == null) throw new InvalidOperationException("Stream has no WaveFormat");

                var sampleProvider = stream.ToSampleProvider();
                if (sampleProvider == null) throw new InvalidOperationException("ToSampleProvider returned null");
                if (sampleProvider.WaveFormat == null) throw new InvalidOperationException("Sample provider has no WaveFormat");

                var provider = ConvertFormat(sampleProvider, format);
                if (provider == null) throw new InvalidOperationException("Format conversion returned null");
                if (provider.WaveFormat == null) throw new InvalidOperationException("Converted provider has no WaveFormat");

                var eq = new EqualizerSampleProvider(provider, sound);
                var pitch = new PitchShifter(eq);
                pitch.SetPitch((float)sound.Pitch);
                var vol = new VolumeSampleProvider(pitch) { Volume = volume };
                return (eq, pitch, vol);
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠️ Processing chain error: {ex.Message} (Type: {ex.GetType().Name})");
                return null;
            }
        }

        private void InitializeVirtualOutput()
        {
            try
            {
                if (_volumeProviderVirtual != null && 
                    _deviceManager.IsUsingVirtualOutput)
                {
                    _deviceManager.AddMixerInput(_volumeProviderVirtual);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"🔇 Virtual output error: {ex.Message}");
            }
        }

        private void InitializeSpeakerOutput()
        {
            try
            {
                if (_volumeProviderSpeaker == null) return;

                using var enumerator = new MMDeviceEnumerator();
                var defaultSpeaker = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    ?? throw new InvalidOperationException("No output device found");

                _audioWaveOutSpeaker = new WasapiOut(defaultSpeaker, AudioClientShareMode.Shared, true, 100);
                _audioWaveOutSpeaker.PlaybackStopped += HandlePlaybackStopped;
                _audioWaveOutSpeaker.Init(_volumeProviderSpeaker);
                _audioWaveOutSpeaker.Play();
            }
            catch (Exception ex)
            {
                UpdateStatus($"🔈 Speaker error: {ex.Message}");
                StopAudioProcessing(updateStatus: false);
            }
        }

        private void HandlePlaybackStopped(object? sender, StoppedEventArgs args)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_currentPlaybackState == PlaybackState.Playing && args.Exception == null)
                {
                    StopAudioProcessing();
                    UpdatePlayPauseButtonState();
                    UpdateStatus("Playback completed");
                }
            });
        }

        private void PauseAudioProcessing()
        {
            if (_currentPlaybackState != PlaybackState.Playing) return;

            _audioWaveOutSpeaker?.Pause();
            if (_persistentMixer != null && _volumeProviderVirtual != null)
            {
                _persistentMixer.RemoveMixerInput(_volumeProviderVirtual);
            }
            _currentPlaybackState = PlaybackState.Paused;
            UpdatePlayPauseButtonState();
        }

        private void ResumeAudioProcessing()
        {
            if (_currentPlaybackState != PlaybackState.Paused) return;

            if (_persistentMixer != null && _volumeProviderVirtual != null)
            {
                _persistentMixer.AddMixerInput(_volumeProviderVirtual);
            }
            _audioWaveOutSpeaker?.Play();
            _currentPlaybackState = PlaybackState.Playing;
            UpdatePlayPauseButtonState();
        }

        private void StopAudioProcessing(bool updateStatus = true)
        {
            try
            {
                _currentPlaybackState = PlaybackState.Stopped;

                // Stop and dispose speaker output
                _audioWaveOutSpeaker?.Stop();
                _audioWaveOutSpeaker?.Dispose();

                // Remove virtual output from mixer
                if (_volumeProviderVirtual != null)
                {
                    _deviceManager.RemoveMixerInput(_volumeProviderVirtual);
                }

                // Dispose audio streams
                _virtualLoopStream?.Dispose();
                _speakerLoopStream?.Dispose();

                // Reset processing chain
                _equalizerVirtual = null;
                _equalizerSpeaker = null;
                _pitchShifterVirtual = null;
                _pitchShifterSpeaker = null;
                _volumeProviderVirtual = null;
                _volumeProviderSpeaker = null;
                _virtualLoopStream = null;
                _speakerLoopStream = null;

                if (updateStatus) UpdateStatus("⏹ Playback stopped");
            }
            catch (Exception ex)
            {
                UpdateStatus($"⚠️ Stop error: {ex.Message}");
            }
            finally
            {
                UpdatePlayPauseButtonState();
            }
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

                StopButton.IsVisible = _currentPlaybackState != PlaybackState.Stopped;
            });
        }

        private LoopStream? CreateAudioStream(string filePath)
        {
            try
            {
                WaveStream reader = Path.GetExtension(filePath).ToLower() switch
                {
                    ".mp3" => new Mp3FileReader(filePath),
                    ".wav" => new WaveFileReader(filePath),
                    _ => throw new InvalidOperationException("Unsupported file format")
                };
                if (reader.WaveFormat == null ||
                    reader.WaveFormat.Channels <= 0 ||
                    reader.WaveFormat.SampleRate <= 0)
                {
                    throw new InvalidOperationException("Audio file has an invalid format");
                }
                return new LoopStream(reader);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error loading audio: {ex.Message}");
                return null;
            }
        }

        private ISampleProvider ConvertFormat(ISampleProvider input, WaveFormat targetFormat)
        {
            if (input.WaveFormat.Channels != targetFormat.Channels)
            {
                if (targetFormat.Channels == 2)
                {
                    if (input.WaveFormat.Channels == 1)
                    {
                        input = new MonoToStereoSampleProvider(input);
                    }
                    else if (input.WaveFormat.Channels > 2)
                    {
                        input = new DownmixToStereoSampleProvider(input);
                    }
                    // If already 2 channels, no conversion needed
                }
                else if (targetFormat.Channels == 1)
                {
                    if (input.WaveFormat.Channels == 2)
                    {
                        input = new StereoToMonoSampleProvider(input);
                    }
                    else if (input.WaveFormat.Channels > 2)
                    {
                        throw new NotSupportedException("Downmixing to mono from multi-channel not implemented");
                    }
                    // If already 1 channel, no conversion needed
                }
            }

            if (input.WaveFormat.SampleRate != targetFormat.SampleRate)
            {
                input = new WdlResamplingSampleProvider(input, targetFormat.SampleRate);
            }

            return input;
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


        #if WINDOWS
        private void InitializeTts()
        {
            #if WINDOWS
                _synth.SetOutputToNull();
                RefreshVoices();
            #endif
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private void RefreshVoices()
        {
            #if WINDOWS
                ViewModel.AvailableVoices.Clear();
                foreach (var voice in _synth.GetInstalledVoices().Where(v => v.Enabled))
                {
                    ViewModel.AvailableVoices.Add(voice.VoiceInfo);
                }
                ViewModel.SelectedVoice = ViewModel.AvailableVoices.FirstOrDefault();
            #endif
        }
        #else
            private void InitializeTts()
            {
                ViewModel.AvailableVoices.Clear();
            }
        #endif

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async void PreviewTts_Click(object sender, RoutedEventArgs e)
        {
        #if WINDOWS
            if (string.IsNullOrWhiteSpace(ViewModel.TtsText)) return;

            try
            {
                StopAudioProcessing();
                _ttsAudioStream?.Dispose();

                _ttsAudioStream = new MemoryStream();
                var formatInfo = new SpeechAudioFormatInfo(44100, AudioBitsPerSample.Sixteen, AudioChannel.Stereo);
                
                _synth.SetOutputToAudioStream(_ttsAudioStream, formatInfo);
                
                if (ViewModel.SelectedVoice != null)
                {
                    _synth.SelectVoice(ViewModel.SelectedVoice.Name);
                }

                await Task.Run(() => _synth.Speak(ViewModel.TtsText)); 
                _ttsAudioStream.Position = 0;
                StartTtsPlayback();
            }
            catch (Exception ex)
            {
                UpdateStatus($"TTS Error: {ex.Message}");
            }
        #else
            await Task.CompletedTask;
        #endif
        }

        private void StartTtsPlayback()
        {
            try
            {
                StopAudioProcessing(updateStatus: false);

                var ttsReader = new WaveFileReader(_ttsAudioStream);
                var loopStream = new LoopStream(ttsReader) { Loop = ViewModel.IsLoopingEnabled };

                var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                float initialVolume = (float)(ViewModel.TtsVolume / 100.0);

                var soundParams = new SoundItem
                {
                    Volume = ViewModel.TtsVolume,
                    Pitch = ViewModel.TtsPitch,
                    Bass = 0,
                    Treble = 0
                };

                var virtualChain = CreateProcessingChain(loopStream, targetFormat, soundParams, initialVolume);
                var speakerChain = CreateProcessingChain(loopStream, targetFormat, soundParams, initialVolume);

                if (virtualChain?.Volume == null || speakerChain?.Volume == null)
                {
                    UpdateStatus("⚠️ Failed to create TTS processing chain");
                    return;
                }

                // Set up providers
                _equalizerVirtual = virtualChain.Value.Eq;
                _pitchShifterVirtual = virtualChain.Value.Pitch;
                _volumeProviderVirtual = virtualChain.Value.Volume;

                _equalizerSpeaker = speakerChain.Value.Eq;
                _pitchShifterSpeaker = speakerChain.Value.Pitch;
                _volumeProviderSpeaker = speakerChain.Value.Volume;

                // Initialize outputs
                InitializeVirtualOutput();
                InitializeSpeakerOutput();

                _currentPlaybackState = PlaybackState.Playing;
                UpdateStatus($"🗣️ Playing TTS: {ViewModel.TtsText.TrimTo(20)}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"TTS Playback Error: {ex.Message}");
            }
            finally
            {
                UpdatePlayPauseButtonState();
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private async void SaveTtsAudio_Click(object sender, RoutedEventArgs e)
        {
            if (_ttsAudioStream == null) return;

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                {
                    UpdateStatus("Unable to access file system");
                    return;
                }
                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save TTS Audio",
                    SuggestedFileName = $"TTS_{DateTime.Now:yyyyMMddHHmmss}.wav",
                    FileTypeChoices = new[] 
                    {
                        new FilePickerFileType("WAV Audio") 
                        { 
                            Patterns = new[] { "*.wav" } 
                        }
                    }
                });

                if (file != null)
                {
                    await using var stream = await file.OpenWriteAsync();
                    _ttsAudioStream.Position = 0;
                    await _ttsAudioStream.CopyToAsync(stream);

                    var newItem = new SoundItem
                    {
                        Path = file.Path.AbsolutePath,
                        Name = $"TTS: {ViewModel.TtsText.TrimTo(20)}",
                        Volume = ViewModel.TtsVolume,
                        Pitch = ViewModel.TtsPitch
                    };

                    ViewModel.SelectedSoundboard?.Sounds.Add(newItem);
                    SaveSoundLibrary();
                    UpdateStatus("💾 TTS audio saved to soundboard!");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Save Error: {ex.Message}");
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